using System;
using System.Collections.Generic;
using System.Globalization;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.Presentation;

/// <summary>A state chip: its text and tone.</summary>
public readonly record struct Chip(string Text, Tone Tone);

/// <summary>
/// The Tor way's state as raven-node's Status reports it (NetworkStatus "tor"): the phase chip
/// ("Ready", "Starting (45 %)", "Blocked on this network"; raven-node's own words for a phase this
/// app doesn't know), and the lines under it ("Why: ...", "Outage history: ..."). States only.
/// </summary>
public sealed record TorView(Chip Phase, bool Ready, IReadOnlyList<string> Lines);

/// <summary>
/// The Network screens' small rules: the state chip of a way, the plain line of what each way
/// does, which network.set setting a way's switch changes, ports for the firewall note, and
/// sizes. Every text comes from strings.en.json; ways and reasons are the engine's own words.
/// </summary>
public static class NetworkPresenter
{
    /// <summary>The default Internet direct port (API.md, network.set "internet").</summary>
    public const string DefaultInternetPort = "7422";

    /// <summary>The default peer-to-peer port (API.md, network.set "p2p").</summary>
    public const string DefaultP2pPort = "7423";

    /// <summary>
    /// The peer-to-peer addresses the Network page lists: those friends could use. Loopback,
    /// link-local and unspecified ones go under Technical details only (E1: six "Peer-to-peer
    /// address" rows, loopback ones among them).
    /// </summary>
    public static List<string> P2pAddressesShown(IEnumerable<string> addresses)
    {
        var shown = new List<string>();
        foreach (string a in addresses)
        {
            if (!P2pAddressIsLocalOnly(a)) shown.Add(a);
        }
        return shown;
    }

    /// <summary>A multiaddr ("/ip4/127.0.0.1/tcp/7423/...") whose IP only this computer or this link can reach.</summary>
    public static bool P2pAddressIsLocalOnly(string multiaddr)
    {
        string[] parts = (multiaddr ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        string ip = parts[1].ToLowerInvariant();
        switch (parts[0])
        {
            case "ip4":
                return ip.StartsWith("127.", StringComparison.Ordinal) || ip.StartsWith("169.254.", StringComparison.Ordinal)
                    || ip == "0.0.0.0";
            case "ip6":
                string bare = ip.Split('%')[0];
                return bare is "::1" or "::" || bare.StartsWith("fe8", StringComparison.Ordinal)
                    || bare.StartsWith("fe9", StringComparison.Ordinal) || bare.StartsWith("fea", StringComparison.Ordinal)
                    || bare.StartsWith("feb", StringComparison.Ordinal);
            default:
                return false;
        }
    }

    /// <summary>The chip for a way's state (on / off / preview / lab / not on this computer).</summary>
    public static Chip StateChip(string? state) => state switch
    {
        WayStates.On => new Chip(Loc.Get("net.on"), Tone.Success),
        WayStates.Off => new Chip(Loc.Get("net.wayOff"), Tone.Secondary),
        WayStates.Preview => new Chip(Loc.Get("net.preview"), Tone.Warning),
        WayStates.Lab => new Chip(Loc.Get("net.lab"), Tone.Accent),
        WayStates.NotOnThisOs => new Chip(Loc.Get("net.notOnThisOs"), Tone.Secondary),
        // A state a newer engine knows and this app does not: show its word as it is.
        _ => new Chip(TextSanitizer.SingleLine(state), Tone.Secondary),
    };

    /// <summary>One plain line of what a way (or protection) does; empty for a way this app doesn't know.</summary>
    public static string WhatItDoes(string id) => id switch
    {
        "lan" => Loc.Get("network.way.lan"),
        "lan_beacon" => Loc.Get("network.way.lanBeacon"),
        "internet" => Loc.Get("network.way.internet"),
        "port_mapping" => Loc.Get("network.way.portMapping"),
        "p2p" => Loc.Get("network.way.p2p"),
        // No consent sheet (the engine asks none): what lending a relay costs stands beside its switch.
        "friend_relay" => Loc.Get("network.way.friendRelay") + " " + Loc.Get("network.relayForFriends.effect"),
        "websocket" => Loc.Get("network.way.websocket"),
        "proxy" => Loc.Get("network.way.proxy"),
        "custody" => Loc.Get("network.way.custody"),
        "letters" => Loc.Get("network.way.letters"),
        "handoff" => Loc.Get("network.way.handoff"),
        "route_hints" => Loc.Get("network.way.routeHints"),
        "nearby" => Loc.Get("network.way.nearby"),
        "tor" => Loc.Get("network.way.tor"),
        "random_port" => Loc.Get("network.way.randomPort"),
        "link_knock" => Loc.Get("network.way.linkKnock"),
        "link_padding" => Loc.Get("network.way.linkPadding"),
        "lock_v2" => Loc.Get("network.way.lockV2"),
        "security_events" => Loc.Get("network.way.securityEvents"),
        "updates_from_friends" => Loc.Get("network.way.updatesFromFriends"),
        _ => string.Empty,
    };

    /// <summary>
    /// The network.set setting a way's switch changes, or null when the way has no switch
    /// (it runs by itself when its gate is open).
    /// </summary>
    public static string? SettingFor(string wayId) => wayId switch
    {
        "lan" => NetworkSettings.Lan,
        "internet" => NetworkSettings.Internet,
        "port_mapping" => NetworkSettings.PortMapping,
        "p2p" => NetworkSettings.P2p,
        "friend_relay" => NetworkSettings.RelayForFriends,
        "proxy" => NetworkSettings.Proxy,
        "custody" => NetworkSettings.FriendsCarryMine,
        "handoff" => NetworkSettings.Handoff,
        "nearby" => NetworkSettings.Nearby,
        _ => null,
    };

    /// <summary>
    /// The port of a listen setting: "0.0.0.0:7422" gives "7422", "7423" gives "7423",
    /// "/ip4/0.0.0.0/tcp/7423" gives "7423"; null when there is none.
    /// </summary>
    public static string? PortOf(string? listen)
    {
        if (string.IsNullOrWhiteSpace(listen)) return null;
        string text = listen.Trim();
        foreach (string marker in new[] { "/tcp/", "/udp/" })
        {
            int at = text.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0)
            {
                string rest = text[(at + marker.Length)..];
                int slash = rest.IndexOf('/');
                return Digits(slash >= 0 ? rest[..slash] : rest);
            }
        }
        int colon = text.LastIndexOf(':');
        return Digits(colon >= 0 ? text[(colon + 1)..] : text);
    }

    private static string? Digits(string text)
    {
        if (text.Length == 0 || text.Length > 5) return null;
        foreach (char c in text) if (c < '0' || c > '9') return null;
        return text;
    }

    /// <summary>"512 bytes", "4.0 KB", "1.5 MB" (decimal units, invariant digits).</summary>
    public static string Size(long bytes)
    {
        if (bytes < 1000) return bytes.ToString(CultureInfo.InvariantCulture) + " bytes";
        double kb = bytes / 1000.0;
        if (kb < 1000) return kb.ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        double mb = kb / 1000.0;
        if (mb < 1000) return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        return (mb / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
    }

    /// <summary>
    /// The engine's consent text (texts.&lt;setting&gt;, the CLI's own question) without the
    /// terminal's "[y/N]" prompt at its end; the app asks with its own buttons.
    /// </summary>
    public static string ConsentText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string clean = text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        int prompt = clean.LastIndexOf("[y/N]", StringComparison.OrdinalIgnoreCase);
        if (prompt >= 0 && clean[(prompt + "[y/N]".Length)..].Trim().Length == 0)
        {
            int lineStart = clean.LastIndexOf('\n', prompt);
            clean = (lineStart >= 0 ? clean[..lineStart] : string.Empty).TrimEnd();
        }
        return clean;
    }

    /// <summary>
    /// Bluetooth nearby on Windows: inactive until Raven knows which address Windows uses
    /// (NETWORK_PARITY.md, row N7w). Null once the background program reports a random address.
    /// </summary>
    public static string? WindowsNearbyNote(NetworkStatus status)
    {
        if (string.Equals(status.Nearby.AddressPrivacy, "random", StringComparison.Ordinal)) return null;
        return Loc.Get("network.nearby.windows");
    }

    /// <summary>A developer build's way to try nearby anyway (an environment switch): Technical details only (E1).</summary>
    public static string? WindowsNearbyLab(NetworkStatus status) =>
        status.Build == "dev" && WindowsNearbyNote(status) is not null ? Loc.Get("network.nearby.windowsLab") : null;

    /// <summary>
    /// The Windows Firewall note while Internet direct or peer-to-peer is switched on: the
    /// prompt Windows shows, the ports, and the manual rule. Empty when both are off.
    /// </summary>
    public static IReadOnlyList<string> FirewallLines(NetworkStatus status)
    {
        bool internet = status.Way("internet")?.Setting == true;
        bool p2p = status.Way("p2p")?.Setting == true;
        var lines = new List<string>();
        if (!internet && !p2p) return lines;
        lines.Add(Loc.Get("network.firewall.prompt"));
        if (internet)
            lines.Add(Loc.Format("network.firewall.internet", ("port", PortOf(status.InternetListen) ?? DefaultInternetPort)));
        if (p2p)
            lines.Add(Loc.Format("network.firewall.p2p", ("port", PortOf(status.P2pListen) ?? DefaultP2pPort)));
        lines.Add(Loc.Get("network.firewall.manual"));
        return lines;
    }

    /// <summary>
    /// The Tor way's state, or null while raven-node reports no Tor line (its gate closed, a
    /// release build, no background program): then nothing is said beyond the way's own row.
    /// </summary>
    public static TorView? Tor(NetworkStatus status)
    {
        TorState? t = status.Tor;
        if (t is null) return null;
        string text = t.Phase switch
        {
            "ready" => Loc.Get("network.tor.phase.ready"),
            "publishing" => Loc.Get("network.tor.phase.publishing"),
            "unreachable" => Loc.Get("network.tor.phase.unreachable"),
            "starting" => Loc.Format("network.tor.phase.starting", ("percent", t.Percent ?? 0)),
            "restarting" => Loc.Get("network.tor.phase.restarting"),
            "blocked" => Loc.Get("network.tor.phase.blocked"),
            "could_not_start" => Loc.Get("network.tor.phase.couldNotStart"),
            "not_built" => Loc.Get("network.tor.phase.notBuilt"),
            "off" => Loc.Get("network.tor.phase.off"),
            _ => TextSanitizer.SingleLine(t.Words),
        };
        bool ready = t.Phase is "ready" or "publishing" or "unreachable";
        bool trouble = t.Phase is "blocked" or "could_not_start" or "unreachable";
        Tone tone = trouble ? Tone.Warning : ready ? Tone.Success : Tone.Secondary;
        var lines = new List<string>();
        var why = new List<string>();
        foreach (string? part in new[] { t.Why, t.Retry })
        {
            string clean = TextSanitizer.SingleLine(part ?? string.Empty);
            if (clean.Length > 0) why.Add(clean);
        }
        if (why.Count > 0) lines.Add(Loc.Format("network.tor.why", ("text", string.Join("; ", why))));
        string outages = TextSanitizer.SingleLine(t.Outages ?? string.Empty);
        if (outages.Length > 0) lines.Add(Loc.Format("network.tor.outages", ("text", outages)));
        return new TorView(new Chip(text, tone), ready, lines);
    }
}

/// <summary>The path chip on a message, from its path_kind (API.md, Message).</summary>
public static class PathPresenter
{
    /// <summary>The short chip text: "LAN", "Internet", "Relay", "Relay · 443", "Friend"...; empty when there is no path.</summary>
    public static string Chip(string? pathKind, string? path)
    {
        string? known = pathKind switch
        {
            PathKinds.Lan => Loc.Get("network.path.lan"),
            PathKinds.Internet => Loc.Get("network.path.internet"),
            PathKinds.P2p => Loc.Get("network.path.p2p"),
            PathKinds.P2pDirect => Loc.Get("network.path.p2pDirect"),
            PathKinds.P2pReversed => Loc.Get("network.path.p2pReversed"),
            PathKinds.P2pRelayed => Loc.Get("network.path.p2pRelayed"),
            PathKinds.P2pHolePunched => Loc.Get("network.path.p2pHolePunched"),
            PathKinds.WebSocket => Loc.Get("network.path.websocket"),
            PathKinds.Nearby => Loc.Get("network.path.nearby"),
            PathKinds.Friend => Loc.Get("network.path.friend"),
            PathKinds.Handoff => Loc.Get("network.path.handoff"),
            PathKinds.Letter => Loc.Get("network.path.letter"),
            _ => null,
        };
        if (known is not null) return known;
        // An older engine (no path_kind) or a kind this app doesn't know: the engine's words.
        return TextSanitizer.SingleLine(path);
    }

    /// <summary>What Narrator says for the chip: "Delivery path: LAN direct".</summary>
    public static string AccessibleName(string? pathKind, string? path)
    {
        string words = TextSanitizer.SingleLine(path);
        if (words.Length == 0) words = Chip(pathKind, path);
        return words.Length == 0 ? string.Empty : Loc.Format("network.path.a11y", ("path", words));
    }
}
