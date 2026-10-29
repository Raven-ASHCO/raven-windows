using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.ViewModels;

/// <summary>One security notice as the Security page shows it.</summary>
public sealed record NoticeView(string LevelText, Tone Tone, string Headline, string Name, string WhatRavenDid,
    string WhatToDo, string When, IReadOnlyList<string> Details);

/// <summary>
/// The Security page: security notices (security.status, read-only: resolving stays in the
/// terminal), Lock (<see cref="LockViewModel"/>) and updates (updates.status, read-only:
/// installing stays in the terminal for now). Each part loads on its own, so one that is not
/// in this build doesn't hide the others.
/// </summary>
public sealed class SecurityViewModel
{
    private readonly IRavenEngine _engine;

    public SecurityViewModel(IRavenEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        Lock = new LockViewModel(engine);
        Lock.Changed += () => Changed?.Invoke();
    }

    public event Action? Changed;

    public LockViewModel Lock { get; }

    // ---- security notices
    public SecurityStatus? Security { get; private set; }
    public ErrorInfo? SecurityError { get; private set; }
    public string SecurityText { get; private set; } = string.Empty;
    public string? SecurityLabText { get; private set; }
    public IReadOnlyList<NoticeView> Notices { get; private set; } = Array.Empty<NoticeView>();
    public string? CountedText { get; private set; }

    /// <summary>What the running background program counted (or why there are no counts).</summary>
    public IReadOnlyList<string> NodeLines { get; private set; } = Array.Empty<string>();

    // ---- updates
    public UpdatesStatus? Updates { get; private set; }
    public ErrorInfo? UpdatesError { get; private set; }
    public string UpdatesText { get; private set; } = string.Empty;
    public string? UpdatesLabText { get; private set; }
    public IReadOnlyList<string> UpdatesLines { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> Offer { get; private set; } = Array.Empty<string>();

    /// <summary>Installing stays in the terminal (the engine's words, else the app's).</summary>
    public string InstallText { get; private set; } = string.Empty;

    public bool Busy { get; private set; }

    public async Task LoadAsync()
    {
        Busy = true;
        Changed?.Invoke();
        try
        {
            await LoadSecurityAsync();
            await LoadUpdatesAsync();
            await Lock.LoadAsync();
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    private async Task LoadSecurityAsync()
    {
        try
        {
            ApplySecurity(await _engine.SecurityStatusAsync());
            SecurityError = null;
        }
        catch (Exception ex)
        {
            SecurityError = ErrorInfo.From(ex);
        }
    }

    private async Task LoadUpdatesAsync()
    {
        try
        {
            ApplyUpdates(await _engine.UpdatesStatusAsync());
            UpdatesError = null;
        }
        catch (Exception ex)
        {
            UpdatesError = ErrorInfo.From(ex);
        }
    }

    private void ApplySecurity(SecurityStatus s)
    {
        Security = s;
        SecurityText = !s.Available
            ? Loc.Get("security.preview")
            : s.Kept switch
            {
                "live" => Loc.Get("security.kept.live"),
                "unsealed" => Loc.Get("security.kept.unsealed"),
                _ => Loc.Get("security.kept.off"),
            };
        SecurityLabText = s.Lab ? Loc.Get("network.labOnly") : null;
        Notices = s.Notices.Select(n => new NoticeView(
            Loc.Get(n.Level == "alert" ? "security.alert" : "security.warning"),
            n.Level == "alert" ? Tone.Danger : Tone.Warning,
            n.Headline,
            n.Name,
            n.WhatRavenDid,
            n.WhatToDo,
            n.When,
            n.Details.Select(d => d.Label + ": " + d.Value).ToList())).ToList();
        CountedText = s.Counted > 0 ? Loc.Format("security.counted", ("n", s.Counted)) : null;

        var node = new List<string>();
        if (s.Node is SecurityNodeCounts counts)
        {
            node.Add(Loc.Format("security.node.strangers", ("n", counts.StrangersRefused1h)));
            node.Add(Loc.Format("security.node.handshakes", ("n", counts.FailedHandshakes1h)));
            node.Add(Loc.Format("security.node.bans", ("n", counts.BansActive)));
            node.Add(Loc.Format("security.node.linkTags", ("n", counts.LinkTagFailures24h)));
            // Its open alerts are the smaller notices, only counted, while no notice needs you;
            // "alerts" is never said then (E1: "Open alerts: 1" beside "Nothing needs you.").
            node.Add(OpenNoticesLine(s.Notices.Count > 0, counts.AlertsOpen));
        }
        else if (s.Available)
        {
            node.Add(Loc.Get("security.node.none"));
        }
        NodeLines = node;
    }

    /// <summary>The background program's count of open alerts, labelled like the line above it.</summary>
    public static string OpenNoticesLine(bool noticesNeedYou, long count) => noticesNeedYou
        ? Loc.Format("security.node.alerts", ("n", count))
        : Loc.Format("security.counted", ("n", count));

    private void ApplyUpdates(UpdatesStatus u)
    {
        Updates = u;
        UpdatesText = u.Available || u.Bundles || u.FromFriends
            ? (u.Summary is { Length: > 0 } summary ? summary : Loc.Get("updates.available"))
            : Loc.Get("updates.notAvailable");
        UpdatesLabText = u.Lab ? Loc.Get("network.labOnly") : null;
        UpdatesLines = u.Lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        Offer = (u.Offer ?? new List<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        InstallText = string.IsNullOrWhiteSpace(u.InstallText) ? Loc.Get("updates.install") : u.InstallText;
    }
}
