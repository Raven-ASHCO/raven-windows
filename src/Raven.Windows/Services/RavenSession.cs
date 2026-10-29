using System;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Helpers;

namespace Raven.Windows.Services;

/// <summary>
/// The app's connection to the engine: cached settings, node status and privacy state, and
/// the event pump (events.poll every 500 ms while the window is shown, every 5 s when only
/// the tray icon is left; API.md).
/// <para>
/// The engine calls themselves run off the UI thread (<see cref="Engine"/> is a
/// <see cref="BackgroundEngine"/>); every <c>await</c> here and in the pages resumes on the
/// UI thread (the DispatcherQueue synchronization context set in Program.Main), so the
/// cached state, the events and the change notifications all happen on the UI thread.
/// </para>
/// </summary>
public sealed class RavenSession
{
    private readonly DispatcherQueueTimer _pollTimer;
    private long _afterSeq;
    private bool _polling;

    public RavenSession(IRavenEngine engine, DispatcherQueue dispatcher)
    {
        Engine = engine;
        _pollTimer = dispatcher.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromMilliseconds(MotionTokens.PollWindowMs);
        _pollTimer.IsRepeating = true;
        _pollTimer.Tick += (_, _) => _ = PollOnceAsync();
    }

    public IRavenEngine Engine { get; }

    public RavenSettings Settings { get; private set; } = new();

    public NodeStatus? Node { get; private set; }

    /// <summary>privacy.state as last read; Available is false until the privacy pack lands.</summary>
    public PrivacyState Privacy { get; private set; } = new();

    /// <summary>lock.state (Lock v2) as last read; null before the first answer or when it failed.</summary>
    public LockState? LockV2 { get; private set; }

    /// <summary>Lock now works through Lock v2: this build has it and the running background program serves it.</summary>
    public bool CanLockV2 => LockV2 is { Available: true, Supported: true, Locked: false };

    /// <summary>When the last real status check succeeded (status_ok), engine clock.</summary>
    public long LastOkMs { get; private set; }

    /// <summary>True when the engine says the background program does not answer.</summary>
    public bool NodeDown { get; private set; }

    public bool IsPolling => _pollTimer.IsRunning;

    /// <summary>One engine event, on the UI thread, in sequence order.</summary>
    public event Action<RavenEvent>? EventReceived;

    /// <summary>Node status or reachability changed.</summary>
    public event Action? NodeChanged;

    /// <summary>The engine dropped events (events.poll answered reset): reload what is on screen.</summary>
    public event Action? Resynced;

    /// <summary>Settings changed (for example Reduced motion).</summary>
    public event Action? SettingsChanged;

    public static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public void StartPolling()
    {
        if (!_pollTimer.IsRunning) _pollTimer.Start();
    }

    public void StopPolling() => _pollTimer.Stop();

    /// <summary>Poll fast while the window is visible, slowly from the tray.</summary>
    public void SetWindowVisible(bool visible)
    {
        _pollTimer.Interval = TimeSpan.FromMilliseconds(visible ? MotionTokens.PollWindowMs : MotionTokens.PollTrayMs);
    }

    public async Task RefreshSettingsAsync()
    {
        Settings = await Engine.SettingsGetAsync();
        Motion.AppReducedMotion = Settings.ReducedMotion;
        SettingsChanged?.Invoke();
    }

    /// <summary>This app's own preferences changed (Show technical details): the screens re-render as for a settings change.</summary>
    public void NotifyPrefsChanged() => SettingsChanged?.Invoke();

    /// <summary>Changes some settings (only the given fields) and keeps the cache in step.</summary>
    public async Task<RavenSettings> UpdateSettingsAsync(JsonObject partial)
    {
        Settings = await Engine.SettingsSetAsync(partial);
        Motion.AppReducedMotion = Settings.ReducedMotion;
        SettingsChanged?.Invoke();
        return Settings;
    }

    /// <summary>
    /// Reads privacy.state. A failure counts as "not available", so the privacy actions stay
    /// disabled with the Preview label rather than offering something that cannot work.
    /// </summary>
    public async Task<PrivacyState> RefreshPrivacyAsync()
    {
        try
        {
            Privacy = await Engine.PrivacyStateAsync();
        }
        catch (RavenException ex)
        {
            Debug.WriteLine($"Raven: privacy.state failed: {ex.Code} {ex.Detail}");
            Privacy = new PrivacyState();
        }
        return Privacy;
    }

    /// <summary>
    /// Reads lock.state. A failure (no profile yet, an older engine) counts as "no Lock v2", so
    /// Lock now stays with the privacy pack's rule.
    /// </summary>
    public async Task<LockState?> RefreshLockAsync()
    {
        try
        {
            LockV2 = await Engine.LockStateAsync();
        }
        catch (RavenException ex)
        {
            Debug.WriteLine($"Raven: lock.state failed: {ex.Code} {ex.Detail}");
            LockV2 = null;
        }
        return LockV2;
    }

    /// <summary>A lock.* answer the pages got (Lock now, Unlock).</summary>
    public void SetLock(LockState state) => LockV2 = state;

    /// <summary>After privacy.erase the engine starts over: forget the cached state and event position.</summary>
    public void ResetAfterErase()
    {
        StopPolling();
        _afterSeq = 0;
        Node = null;
        LastOkMs = 0;
        NodeDown = false;
        Privacy = new PrivacyState();
        LockV2 = null;
    }

    public async Task RefreshNodeAsync()
    {
        try
        {
            SetNode(await Engine.NodeStatusAsync());
        }
        catch (RavenException ex)
        {
            Debug.WriteLine($"Raven: node.status failed: {ex.Code} {ex.Detail}");
            NodeDown = true;
            NodeChanged?.Invoke();
        }
    }

    public void SetNode(NodeStatus status)
    {
        Node = status;
        NodeDown = !status.Running;
        if (status.LastOkMs > LastOkMs) LastOkMs = status.LastOkMs;
        NodeChanged?.Invoke();
    }

    private async Task PollOnceAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            EventsPage page = await Engine.EventsPollAsync(_afterSeq);
            // After a reset the engine's seq is the one to continue from (API.md, "Events").
            if (page.Seq > _afterSeq || page.Reset) _afterSeq = page.Seq;
            if (NodeDown && Node is { Running: true })
            {
                NodeDown = false;
                NodeChanged?.Invoke();
            }
            foreach (RavenEvent e in page.Events)
            {
                if (e.Seq <= 0) continue;
                Apply(e);
                EventReceived?.Invoke(e);
            }
            if (page.Reset) Resynced?.Invoke();
        }
        catch (RavenException ex)
        {
            Debug.WriteLine($"Raven: events.poll failed: {ex.Code} {ex.Detail}");
            if (ex.Code == RavenErrorCodes.NodeNotRunning && !NodeDown)
            {
                NodeDown = true;
                NodeChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Raven: events.poll failed: " + ex);
        }
        finally
        {
            _polling = false;
        }
    }

    private void Apply(RavenEvent e)
    {
        switch (e.Kind)
        {
            case EventKinds.StatusOk:
                LastOkMs = e.AtMs;
                if (NodeDown)
                {
                    NodeDown = false;
                    NodeChanged?.Invoke();
                }
                break;
            case EventKinds.Node when e.Status is not null:
                SetNode(e.Status);
                break;
        }
    }
}
