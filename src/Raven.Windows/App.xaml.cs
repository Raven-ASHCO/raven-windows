using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;
using Raven.Windows.Services;
#if DEBUG
using Raven.Windows.Core.Mock;
#endif

namespace Raven.Windows;

/// <summary>
/// App lifetime: picks the engine (Raven's engine in raven_client.dll; MockRaven only in a
/// Debug build started with --demo, --mock-ready or --mock-locked, see
/// <see cref="EngineChoice"/>) and wraps it in a <see cref="BackgroundEngine"/> so no engine call runs on
/// the UI thread, opens the window, owns the notification-area icon (its menu lists the
/// friends; it "breathes" slowly while Raven listens) and exits cleanly.
/// </summary>
public partial class App : Application
{
    private MainWindow? _window;
    private RavenSession? _session;
    private TrayIcon? _tray;
    private DispatcherQueue? _dispatcher;
    private DispatcherQueueTimer? _breath;
    private long _breathStep;

    public App()
    {
        InitializeComponent();
    }

    public static new App? Current => Application.Current as App;

    public bool HasTray => _tray is not null;

    /// <summary>This app's own preferences: the theme and "Show technical details" (Settings &gt; Appearance).</summary>
    public AppPrefs Prefs { get; } = AppPrefsStore.Load();

    /// <summary>Keeps <see cref="Prefs"/> for the next start.</summary>
    public void SavePrefs() => AppPrefsStore.Save(Prefs);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        string[] argv = Environment.GetCommandLineArgs();

        _session = new RavenSession(new BackgroundEngine(CreateEngine(argv)), _dispatcher);
        _session.NodeChanged += UpdateTray;
        _session.NodeChanged += () => _ = _session?.RefreshLockAsync(); // Lock v2 follows the background program
        _session.SettingsChanged += UpdateTray;

        _window = new MainWindow(_session);
        // In front, not behind other windows (E1): show, activate and bring it forward.
        _window.ShowAndActivate();

        _tray = TrayIcon.TryCreate(
            TrayBreath.FrameFiles.Select(f => Path.Combine(AppContext.BaseDirectory, "Assets", f)).ToList(),
            Loc.Get("app.name"));
        if (_tray is not null)
        {
            _tray.CommandInvoked += OnTrayCommand;
            _tray.FriendInvoked += address => _ = _window?.OpenFriendAsync(address);
            _tray.MenuProvider = BuildTrayMenu;
            _breath = _dispatcher.CreateTimer();
            _breath.Interval = TimeSpan.FromMilliseconds(TrayBreath.StepMs);
            _breath.IsRepeating = true;
            _breath.Tick += (_, _) => Breathe();
        }

        _ = _window.StartAsync();
    }

    /// <summary>Another launch of Raven was redirected here: show the window.</summary>
    public void OnRedirectedActivation()
    {
        _dispatcher?.TryEnqueue(() => _window?.ShowAndActivate());
    }

    /// <summary>Removes the tray icon, stops polling and ends the process.</summary>
    public void ExitApp()
    {
        _breath?.Stop();
        _tray?.Dispose();
        _tray = null;
        _session?.StopPolling();
        _window?.CloseForExit();
        Exit();
    }

    private static IRavenEngine CreateEngine(IReadOnlyList<string> argv)
    {
#if DEBUG
        // The in-memory mock (desktop/api/API.md, "Mocks") only when asked for: --demo, or
        // --mock-ready (skips the first-run screens) / --mock-locked (the Locked screen) /
        // --mock-lab (ready, answering the network ops as a developer build with lab switches).
        if (EngineChoice.DemoRequested(argv))
        {
            bool lab = EngineChoice.Contains(argv, EngineChoice.MockLabArgument);
            return new MockRaven(
                startReady: lab || EngineChoice.Contains(argv, EngineChoice.MockReadyArgument),
                startLocked: EngineChoice.Contains(argv, EngineChoice.MockLockedArgument),
                labNetwork: lab);
        }
#endif
        // Raven's engine: raven_client.dll next to Raven.exe, on the CLI's profile.
        return new RavenClientEngine();
    }

    private void OnTrayCommand(TrayCommand command)
    {
        if (_window is null) return;
        switch (command)
        {
            case TrayCommand.Open:
                _window.ShowAndActivate();
                break;
            case TrayCommand.AddFriend:
                _ = _window.OpenAddFriendAsync();
                break;
            case TrayCommand.Lock:
                if (_session is not null && (_session.Privacy.Available || _session.CanLockV2)) _ = _window.LockAsync();
                break;
            case TrayCommand.QuitWindow:
                _ = _window.RequestQuitAsync(stopBackground: false);
                break;
            case TrayCommand.QuitAll:
                _ = _window.RequestQuitAsync(stopBackground: true);
                break;
        }
    }

    /// <summary>
    /// Built each time the menu opens: status lines first (not clickable), the friends (as in the
    /// macOS menu), then the actions; texts from strings.en.json (<see cref="TrayMenu"/>).
    /// </summary>
    private IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        if (_session is null) return Array.Empty<TrayMenuItem>();
        bool ready = _window?.IsShellShown == true;
        IReadOnlyList<(Contact Contact, int Unread)> friends =
            _window?.TrayFriends() ?? Array.Empty<(Contact Contact, int Unread)>();
        // "Lock now" works with the privacy pack's lock or with Lock v2 (lock.state).
        bool canLock = _session.Privacy.Available || _session.CanLockV2;
        return TrayMenu.Build(_session.Node, _session.NodeDown, ready, friends, canLock, RavenSession.NowMs);
    }

    /// <summary>Node or settings changed: hover text, and breathing on or off.</summary>
    private void UpdateTray()
    {
        if (_tray is null || _session is null) return;
        _tray.SetTooltip(TrayMenu.StatusLine(_session.Node));
        bool listening = TrayMenu.IsListening(_session.Node);
        if (listening && _tray.FrameCount > 1)
        {
            // Runs only while Raven listens; each step checks the motion settings again.
            if (_breath is { IsRunning: false }) _breath.Start();
        }
        else
        {
            _breath?.Stop();
            _breathStep = 0;
            _tray.SetFrame(0);
        }
    }

    /// <summary>One breathing step (1 s): 100%, 80%, 60%, 80%; still with motion off.</summary>
    private void Breathe()
    {
        if (_tray is null || _session is null) return;
        if (!TrayMenu.IsListening(_session.Node))
        {
            UpdateTray(); // stops the timer
            return;
        }
        bool breathing = TrayBreath.ShouldBreathe(_session.Node, Motion.Enabled);
        _breathStep = breathing ? _breathStep + 1 : 0;
        _tray.SetFrame(TrayBreath.FrameAt(_breathStep, breathing));
    }
}
