using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.Presentation;

/// <summary>The colour family of a path (tokens.json color.pathRule).</summary>
public enum PathFamily
{
    Lan,
    Nearby,
    Tor,
    Direct,
    Relay,
    Friend,
    Other,
}

/// <summary>
/// The Calm path chips ("LAN direct", "Bluetooth", "via Tor", "Direct", "Held by a friend") and the
/// colour family of a path kind or a way. Only what the engine reports: a kind this app doesn't
/// know shows the engine's own words.
/// </summary>
public static class CalmPath
{
    /// <summary>
    /// tokens.json pathRule: lan; nearby; tor (the onion carrier); internet, hole punched and 443 are direct; p2p and
    /// relay are relay; friend, handoff and letter are friend; anything else other.
    /// </summary>
    public static PathFamily Family(string? pathKind) => pathKind switch
    {
        PathKinds.Lan => PathFamily.Lan,
        PathKinds.Nearby => PathFamily.Nearby,
        PathKinds.Onion => PathFamily.Tor,
        PathKinds.Internet or PathKinds.P2pHolePunched or PathKinds.WebSocket => PathFamily.Direct,
        PathKinds.P2p or PathKinds.P2pDirect or PathKinds.P2pReversed or PathKinds.P2pRelayed => PathFamily.Relay,
        PathKinds.Friend or PathKinds.Handoff or PathKinds.Letter => PathFamily.Friend,
        _ => PathFamily.Other,
    };

    /// <summary>The family of a way on the Network page (its icon tile's colour).</summary>
    public static PathFamily FamilyOfWay(string? wayId) => wayId switch
    {
        "lan" or "lan_beacon" => PathFamily.Lan,
        "nearby" => PathFamily.Nearby,
        "onion" or "tor" => PathFamily.Tor,
        "internet" or "port_mapping" or "random_port" => PathFamily.Direct,
        "p2p" or "friend_relay" or "websocket" or "proxy" or "route_hints" => PathFamily.Relay,
        "custody" or "letters" or "handoff" => PathFamily.Friend,
        _ => PathFamily.Other,
    };

    /// <summary>
    /// The chip under a message: the Calm words for the common kinds, the short words of
    /// <see cref="PathPresenter.Chip"/> for the rest; with <paramref name="technical"/> (Settings &gt;
    /// Appearance &gt; Show technical details) the engine's exact path words.
    /// </summary>
    public static string Chip(string? pathKind, string? path, bool technical = false)
    {
        if (technical)
        {
            string words = TextSanitizer.SingleLine(path);
            if (words.Length > 0) return words;
        }
        return pathKind switch
        {
            PathKinds.Lan => Loc.Get("chip.lan"),
            PathKinds.Nearby => Loc.Get("chip.nearby"),
            PathKinds.Onion => Loc.Get("chip.tor"),
            PathKinds.Internet => Loc.Get("chip.internet"),
            PathKinds.Friend => Loc.Get("chip.friend"),
            _ => PathPresenter.Chip(pathKind, path),
        };
    }

    /// <summary>The route line's words: the path and, for the common kinds, what it means ("LAN direct · same network").</summary>
    public static string RouteLabel(string? pathKind, string? path)
    {
        string chip = Chip(pathKind, path);
        if (chip.Length == 0) return string.Empty;
        string? note = pathKind switch
        {
            PathKinds.Lan => Loc.Get("route.note.lan"),
            PathKinds.Nearby => Loc.Get("route.note.nearby"),
            PathKinds.Onion => Loc.Get("route.note.tor"),
            PathKinds.Internet => Loc.Get("route.note.internet"),
            PathKinds.P2pRelayed or PathKinds.WebSocket => Loc.Get("route.note.relay"),
            PathKinds.Friend => Loc.Get("route.note.friend"),
            _ => null,
        };
        return note is null ? chip : chip + " · " + note;
    }
}

/// <summary>The sidebar's own lines.</summary>
public static class NavPresenter
{
    /// <summary>
    /// "3 ways on" beside Network: the ways the background program says are on (node.status
    /// carriers), or null when it isn't known (no background program).
    /// </summary>
    public static string? WaysOn(NodeStatus? node)
    {
        if (node is null || !node.Running) return null;
        Carriers c = node.Carriers;
        int on = new[] { c.Lan, c.Internet, c.P2p, c.Custody, c.Nearby }.Count(s => s == WayStates.On);
        return on == 1 ? Loc.Get("nav.wayOn") : Loc.Format("nav.waysOn", ("n", on));
    }

    /// <summary>The brand row's heartbeat: "your node: ok · now", or that it isn't responding.</summary>
    public static (string Text, bool Ok) Heartbeat(NodeStatus? node, bool nodeDown, long lastOkMs, long nowMs)
    {
        if (nodeDown || node is null || !node.Running) return (Loc.Get("home.heartbeat.down"), false);
        if (node.IsStarting) return (Loc.Get("net.starting"), true);
        long last = lastOkMs > 0 ? lastOkMs : nowMs;
        return (Loc.Format("home.heartbeat.ok", ("when", RelativeTime.SinceCheck(last, nowMs))), true);
    }
}

/// <summary>
/// The away card under a chat whose messages wait (Calm "a friend away"): only what the engine
/// says. The friend "isn't reachable now" (not "away"); the time is the engine's deadline; a
/// friend holding a sealed copy is said only when a waiting message's path kind says so.
/// </summary>
public static class AwayPresenter
{
    public readonly record struct Card(string Title, string Body, string? Held, string? OldestId, int Count);

    /// <summary>The card, or null when no message to the friend waits.</summary>
    public static Card? For(Contact contact, IEnumerable<Message> messages, long nowMs)
    {
        List<Message> waiting = messages.Where(m => m.IsOutgoing && m.State == MessageStates.Waiting).OrderBy(m => m.AtMs).ToList();
        int count = Math.Max(waiting.Count, contact.Waiting?.Count ?? 0);
        if (count == 0) return null;
        string name = ContactPresenter.DisplayName(contact);
        string title = Loc.Format("away.title", ("name", name));
        long? until = contact.Waiting is { UntilMs: > 0 } w ? w.UntilMs
            : waiting.Where(m => m.UntilMs is > 0).Select(m => m.UntilMs).Min();
        string body = until is long u && u > 0
            ? Loc.Format("away.body", ("time", RelativeTime.Deadline(u, nowMs)), ("name", name))
            : Loc.Format("away.body.noTime", ("name", name));
        bool held = waiting.Any(m => CalmPath.Family(m.PathKind) == PathFamily.Friend);
        return new Card(title, body, held ? Loc.Format("away.held", ("name", name)) : null, waiting.FirstOrDefault()?.Id, count);
    }
}

/// <summary>The seven Settings sections, in order (Calm Settings).</summary>
public static class SettingsSections
{
    public readonly record struct Section(string Id, string TitleKey, string Glyph, string Tile);

    public const string General = "general";
    public const string Appearance = "appearance";
    public const string Privacy = "privacy";
    public const string Lock = "lock";
    public const string Network = "network";
    public const string Updates = "updates";
    public const string About = "about";

    /// <summary>Id, title key, Segoe Fluent Icons glyph and the tile style's name part (RavenSectionTile{Tile}Style).</summary>
    public static IReadOnlyList<Section> All { get; } = new[]
    {
        new Section(General, "settings.general", "\uE713", "General"),
        new Section(Appearance, "settings.appearance", "\uE790", "Appearance"),
        new Section(Privacy, "settings.privacy", "\uEA18", "Privacy"),
        new Section(Lock, "lock.section", "\uE72E", "Lock"),
        new Section(Network, "settings.network", "\uE701", "Network"),
        new Section(Updates, "updates.title", "\uE896", "Updates"),
        new Section(About, "net.about", "\uE946", "About"),
    };

    public static int IndexOf(string? id)
    {
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i].Id == id) return i;
        }
        return -1;
    }
}

/// <summary>The theme the person chose in Settings &gt; Appearance.</summary>
public enum ThemeChoice
{
    System,
    Light,
    Dark,
}

/// <summary>
/// This app's own preferences, which name no one and live beside the app (not in Raven's profile):
/// the theme and "Show technical details". Raven's engine keeps every other setting.
/// </summary>
public sealed class AppPrefs
{
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public bool ShowTechnical { get; set; }

    /// <summary>Reads the saved JSON; anything unreadable gives the defaults.</summary>
    public static AppPrefs Parse(string? json)
    {
        var prefs = new AppPrefs();
        if (string.IsNullOrWhiteSpace(json)) return prefs;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return prefs;
            if (o["theme"] is JsonValue t && t.TryGetValue(out string? theme))
            {
                prefs.Theme = theme switch
                {
                    "light" => ThemeChoice.Light,
                    "dark" => ThemeChoice.Dark,
                    _ => ThemeChoice.System,
                };
            }
            if (o["show_technical"] is JsonValue d && d.TryGetValue(out bool technical)) prefs.ShowTechnical = technical;
        }
        catch (JsonException)
        {
            // A broken file: the defaults.
        }
        return prefs;
    }

    public string ToJson() => new JsonObject
    {
        ["theme"] = Theme switch
        {
            ThemeChoice.Light => "light",
            ThemeChoice.Dark => "dark",
            _ => "system",
        },
        ["show_technical"] = ShowTechnical,
    }.ToJsonString();
}
