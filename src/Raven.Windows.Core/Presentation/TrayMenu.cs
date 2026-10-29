using System.Collections.Generic;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.Presentation;

/// <summary>Commands of the notification-area menu (strings "tray.*").</summary>
public enum TrayCommand
{
    Open = 1,
    AddFriend = 2,
    Lock = 3,
    QuitWindow = 4,
    QuitAll = 5,
    /// <summary>Open Raven on one friend's chat (<see cref="TrayMenuItem.Address"/>).</summary>
    OpenFriend = 6,
}

/// <summary>One line of the tray menu. A line without a command is a status line (greyed, not clickable).</summary>
public sealed record TrayMenuItem(string Text, TrayCommand? Command, bool Enabled = true, bool SeparatorBefore = false, string? Address = null);

/// <summary>
/// The notification-area menu, the same as the macOS menu bar menu and the Linux tray menu
/// (DESIGN.md, "Menu bar / tray"): the status, the friends, Open Raven, Add a friend…, Lock now
/// (greyed out until the privacy pack), Quit window (keeps receiving) and Quit and stop the
/// background program. Every text comes from strings.en.json.
/// </summary>
public static class TrayMenu
{
    /// <summary>True when Raven listens on the network (not only on this computer).</summary>
    public static bool IsListening(NodeStatus? node) =>
        node is { Running: true } && !string.IsNullOrEmpty(node.Listening) && !node.IsLocalOnly;

    /// <summary>The first line of the menu, also the icon's hover text.</summary>
    /// <summary>"Raven is listening on Wi-Fi" only on Wi-Fi (<paramref name="wifi"/>; null: ask this computer), else "on your network".</summary>
    public static string StatusLine(NodeStatus? node, bool? wifi = null)
    {
        if (!IsListening(node)) return Loc.Get("tray.notListening");
        return Loc.Get(wifi ?? NetworkKind.IsWiFi(node?.Address) ? "tray.listening" : "tray.listening.network");
    }

    /// <summary>"Ali · In touch 5 min ago", or "2 new from Rena" while there are unread messages (as on macOS).</summary>
    public static string FriendLine(Contact contact, int unread, long nowMs)
    {
        string name = ContactPresenter.DisplayName(contact);
        return unread > 0
            ? Loc.Format("home.newFrom", ("n", unread), ("name", name))
            : name + " · " + ContactPresenter.StatusLine(contact, nowMs);
    }

    /// <param name="node">The last node status (null before the first answer).</param>
    /// <param name="nodeDown">The engine says the background program does not answer.</param>
    /// <param name="ready">The main screen is shown (set up and not locked): friends and Add a friend are offered.</param>
    /// <param name="friends">Friends in the sidebar's order, with their unread counts.</param>
    /// <param name="privacyAvailable">privacy.state "available": Lock now works.</param>
    /// <param name="nowMs">Now, for the friends' status lines.</param>
    public static IReadOnlyList<TrayMenuItem> Build(NodeStatus? node, bool nodeDown, bool ready,
        IReadOnlyList<(Contact Contact, int Unread)> friends, bool privacyAvailable, long nowMs)
    {
        var items = new List<TrayMenuItem>
        {
            new TrayMenuItem(StatusLine(node), null, Enabled: false),
            node is { Running: true } && !nodeDown
                ? new TrayMenuItem("♥ " + Loc.Get("tray.background"), null, Enabled: false)
                : new TrayMenuItem(Loc.Get("home.heartbeat.down"), null, Enabled: false),
        };

        bool first = true;
        if (ready)
        {
            foreach ((Contact contact, int unread) in friends)
            {
                items.Add(new TrayMenuItem(FriendLine(contact, unread, nowMs), TrayCommand.OpenFriend,
                    SeparatorBefore: first, Address: contact.Address));
                first = false;
            }
        }

        items.Add(new TrayMenuItem(Loc.Get("tray.open"), TrayCommand.Open, SeparatorBefore: true));
        items.Add(new TrayMenuItem(Loc.Get("tray.addFriend"), TrayCommand.AddFriend, Enabled: ready));
        // "Lock now" needs the privacy pack: greyed out while privacy.state says it is not available.
        items.Add(new TrayMenuItem(Loc.Get("tray.lock"), TrayCommand.Lock, Enabled: ready && privacyAvailable));
        items.Add(new TrayMenuItem(Loc.Get("tray.quitWindow"), TrayCommand.QuitWindow, SeparatorBefore: true));
        items.Add(new TrayMenuItem(Loc.Get("tray.quitAll"), TrayCommand.QuitAll));
        return items;
    }
}

/// <summary>
/// The tray icon "breathes" while Raven listens, like the macOS menu-bar mark (DESIGN.md,
/// "Honest motion": the mark breathes while listening): one icon frame per second, 100%, 80%,
/// 60%, 80% opacity. Windows tray icons cannot animate, so the app swaps prepared icons
/// (tools/make-tray-frames.py). Nothing moves with Windows "Animation effects" off or Raven's
/// "Reduced motion" on, and the timer runs only while Raven listens.
/// </summary>
public static class TrayBreath
{
    /// <summary>Icon files in Assets: frame 0 is the normal icon.</summary>
    public static readonly IReadOnlyList<string> FrameFiles = new[] { "RavenTray.ico", "RavenTrayDim1.ico", "RavenTrayDim2.ico" };

    /// <summary>The frames shown one after another (indexes into <see cref="FrameFiles"/>).</summary>
    public static readonly IReadOnlyList<int> Sequence = new[] { 0, 1, 2, 1 };

    /// <summary>One step per second (the macOS menu bar uses its 1-second clock).</summary>
    public const int StepMs = 1000;

    /// <summary>True when the icon should breathe now.</summary>
    public static bool ShouldBreathe(NodeStatus? node, bool motionEnabled) => motionEnabled && TrayMenu.IsListening(node);

    /// <summary>The frame for a step; frame 0 when breathing is off.</summary>
    public static int FrameAt(long step, bool breathing) =>
        breathing ? Sequence[(int)(((step % Sequence.Count) + Sequence.Count) % Sequence.Count)] : 0;
}
