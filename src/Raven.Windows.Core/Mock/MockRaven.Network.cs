#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.Mock;

/// <summary>
/// The mock's network ops (API.md "Network"; NETWORK_PARITY.md for the gates). By default the
/// answers are a release build's: LAN direct on, every other way held (preview), a held way
/// never turned on, Lock v2, security notices and updates not in this version. With
/// <c>labNetwork</c> it answers as a developer build with <see cref="LabSwitches"/> set: those
/// ways read off or lab and can be switched on, the settings that help friends need
/// "agreed": true (CONSENT_NEEDED), Internet direct, p2p, port mapping and the proxy apply when
/// the background program starts again (restart_needed), node.stop follows the engine's stop
/// rules on Windows, and Lock v2 asks for <see cref="LabPassphrase"/>.
/// </summary>
public sealed partial class MockRaven
{
    /// <summary>The switches a lab mock says it sees (names only, as the engine lists them).</summary>
    public static readonly IReadOnlyList<string> LabSwitches = new[]
    {
        "RAVEN_LAB_ADVISORY", "RAVEN_LAB_CUSTODY_HANDOFF", "RAVEN_LAB_FRIEND_RELAY", "RAVEN_LAB_GROUPS",
        "RAVEN_LAB_HORIZON", "RAVEN_LAB_LOCAL_PROTECTION", "RAVEN_LAB_NEARBY", "RAVEN_LAB_ROUTE_HINTS",
        "RAVEN_LAB_SECURITY_EVENTS", "RAVEN_LAB_TEST_A", "RAVEN_LAB_TOR",
    };

    /// <summary>The demo Raven passphrase Lock v2's Unlock wants in a lab mock.</summary>
    public const string LabPassphrase = "open sesame";

    private const string TestA = "RAVEN_LAB_TEST_A";
    private const string HeldReason = "Not in this version of Raven yet.";
    private const string LabReason = "On for testing only: a developer build's lab switch turned it on.";
    /// <summary>The lab mock's Tor line after "Tor: " and its outage history (states only, as raven-node says them).</summary>
    public const string MockTorOutages = "onion service outages since start: 1, the last lasted 2 min and ended 1 h ago";
    private const string MockTorWords = "ready (" + MockTorOutages + ")";
    private const string NotHereReason = "This computer can't use Bluetooth nearby (no Bluetooth LE support for it).";
    private const string MockSelfPeerId = "12D3KooWMockSelfPeer";

    private readonly bool _lab;

    // The network settings (node_policy.json in the engine). A release mock only ever turns
    // LAN, hide IP and the bridge/store/relay flags; the rest stays off there.
    private bool _lanOff;
    private long? _lanUntilMs;
    private bool _hideIp;
    private bool _bridge = true;
    private bool _store = true;
    private bool _relay;
    private bool _automatic = true;
    private bool _internet;
    private string _internetListen = string.Empty;
    private bool _p2p;
    private string _p2pListen = string.Empty;
    private List<string> _p2pRelays = new();
    private bool? _portMapping;
    private string _proxy = "auto";
    private bool? _nearby;
    private bool _carryForFriends;
    private bool? _friendsCarryMine;
    private bool _handoff;
    private bool _relayOffer;
    private readonly Dictionary<string, FriendRoutes> _routes = new();

    // What the background program uses: settings that apply at a restart are taken at node.start.
    private bool _usedInternet;
    private bool _usedP2p;
    private bool _usedPortMapping;

    // Lock v2.
    private bool _lockedV2;
    private int _wrongPassphrases;
    private long _unlockWaitUntilMs;

    /// <summary>Tests: a login service runs the background program (node.stop answers "managed").</summary>
    public bool LoginServiceRunsNode { get; set; }

    /// <summary>Tests: false makes Bluetooth nearby "not on this computer" (no Bluetooth LE).</summary>
    public bool BluetoothLe { get; set; } = true;

    private sealed class FriendRoutes
    {
        public string? Internet { get; set; }
        public string? P2p { get; set; }
        public List<string> Via { get; set; } = new();
        public List<string> MailFriends { get; set; } = new();
        public bool HideIp { get; set; }
    }

    private sealed record WayInfo(string Id, string Name, string Gate, string[] Switches);

    /// <summary>Every way and protection in the engine's order, with its gate and lab switches.</summary>
    private static readonly WayInfo[] CarrierWays =
    {
        new("lan", "Same network (Wi-Fi, cable or hotspot)", "LAN_DIRECT_PRODUCTION_ENABLED", Array.Empty<string>()),
        new("lan_beacon", "Finding friends on a new network", "LAN_BEACON_PRODUCTION_ENABLED", new[] { "RAVEN_LAB_LAN_BEACON" }),
        new("internet", "Internet direct", "INTERNET_DIRECT_PRODUCTION_ENABLED", new[] { TestA }),
        new("port_mapping", "Router port opening (UPnP, NAT-PMP, PCP)", "P2P_PRODUCTION_ENABLED", new[] { TestA }),
        new("p2p", "Peer-to-peer and relays", "P2P_PRODUCTION_ENABLED", new[] { TestA }),
        new("friend_relay", "Relay for friends", "FRIEND_RELAY_PRODUCTION_ENABLED", new[] { TestA, "RAVEN_LAB_FRIEND_RELAY" }),
        new("websocket", "Relays over WebSocket (port 443)", "WSS_PRODUCTION_ENABLED", new[] { TestA, "RAVEN_LAB_WSS" }),
        new("proxy", "Web proxy for relays", "WSS_PROXY_PRODUCTION_ENABLED", new[] { TestA, "RAVEN_LAB_WSS", "RAVEN_LAB_WSS_PROXY" }),
        new("custody", "Friends carry your messages", "FRIEND_CUSTODY_PRODUCTION_ENABLED", new[] { TestA }),
        new("letters", "Sealed letters with friends", "ASYNC_LETTER_PRODUCTION_ENABLED", new[] { TestA, "RAVEN_LAB_HORIZON", "RAVEN_LAB_LETTER" }),
        new("handoff", "Passing messages on between friends", "CUSTODY_HANDOFF_PRODUCTION_ENABLED", new[] { TestA, "RAVEN_LAB_CUSTODY_HANDOFF" }),
        new("route_hints", "Routes friends tell each other", "ROUTE_HINTS_PRODUCTION_ENABLED", new[] { "RAVEN_LAB_ROUTE_HINTS" }),
        new("nearby", "Bluetooth nearby", "NEARBY_PRODUCTION_ENABLED", new[] { TestA, "RAVEN_LAB_NEARBY" }),
        new("tor", "Tor", "ONION_PRODUCTION_ENABLED", new[] { TestA, "RAVEN_LAB_TOR" }),
    };

    private static readonly WayInfo[] ProtectionWays =
    {
        new("random_port", "A port of its own for this profile", "LISTEN_PORT_RANDOM_PRODUCTION_ENABLED", new[] { TestA, "RAVEN_LAB_RANDOM_PORT" }),
        new("link_knock", "Silent to strangers (link knock)", "LINK_KNOCK_PRODUCTION_ENABLED", new[] { TestA, "RAVEN_LAB_LINK_KNOCK" }),
        new("link_padding", "Padding that hides message sizes", "LINK_PADDING_PRODUCTION_ENABLED", new[] { TestA, "RAVEN_LAB_LINK_PADDING" }),
        new("lock_v2", "Lock while running", "LOCAL_PROTECTION_PRODUCTION_ENABLED", new[] { TestA, "RAVEN_LAB_LOCAL_PROTECTION" }),
        new("security_events", "Security notices", "SECURITY_EVENTS_PRODUCTION_ENABLED", new[] { TestA, "RAVEN_LAB_SECURITY_EVENTS" }),
        new("updates_from_friends", "Updates passed on by friends", "MESH_UPDATE_PRODUCTION_ENABLED",
            new[] { TestA, "RAVEN_LAB_ADVISORY", "RAVEN_LAB_RELEASE_BUNDLE", "RAVEN_LAB_MESH_UPDATE" }),
    };

    /// <summary>The settings network.set takes, in the order it applies them.</summary>
    private static readonly string[] SetOrder =
    {
        "lan", "internet", "p2p", "port_mapping", "proxy", "nearby", "carry_for_friends", "friends_carry_mine",
        "handoff", "relay_for_friends", "bridge", "store", "relay", "hide_ip",
    };

    /// <summary>The engine's honest words for the bridge, store and relay flags.</summary>
    private static readonly Dictionary<string, string> Effects = new()
    {
        ["bridge"] = "Raven's background program passes on sealed envelopes other devices hand it on its bridge listeners (the older mesh envelope path; a program started by Raven listens for these on this computer only). Messages to your friends don't use it.",
        ["store"] = "Lets Raven's background program keep sealed envelopes until their next hop is ready. In this version it always has a next hop, so this changes nothing yet.",
        ["relay"] = "Reserved for relaying between Raven's own links. This version of Raven's background program doesn't use it, so this changes nothing yet. To lend friends a relay, use Relay for friends.",
    };

    // The CLI's own questions (crates/ash custody_cli.rs), as network.status texts carries them.
    private const string CarryForFriendsText = """
        Hold sealed messages for your friends while they are offline?
        - Only between two people who are both your verified contacts.
        - What this computer keeps for them is sealed: you can't read it.
        - It does learn, for every message it holds, who sent it and who it is for
          (both Raven identities), when, how big, and when it was collected. Over the
          internet it also sees the IP address each friend connects from (or their relay's).
        - Each message stays until the recipient's Raven confirms it collected it, or
          until it expires (at most 24 hours). Deleting clears Raven's file; full-disk
          encryption protects what the disk itself may still hold.
        - The envelopes are signed, so while this computer holds a message it holds
          proof of who wrote to whom, when, and how much.
        - At most 128 messages per pair at a time, 64 MiB in all.
        """;

    private const string FriendsCarryMineText = """
        Use friends as mailboxes?
        When a contact cannot be reached, raven-node can leave a sealed copy of your
        message with a friend you have both verified, and collect mail left for you
        with friends who offer to hold it. A friend's Raven can't read your messages,
        but it learns that you wrote to someone, to whom, and when.
        Your mail friends also see when your Raven checks in with them (every few
        minutes while it runs) and about how many of your contacts you are in touch with.
        Turn on? [y/N]
        """;

    private const string HandoffText = """
        This node may pass sealed messages on from a verified contact to another
        verified contact (your friend), and hold sealed messages a friend passes on
        for someone who asked you to. You cannot read them. When passing on you see
        who sent it and which friend it goes to, never the final recipient; when
        holding you see the recipient and the friend who brought it, not the sender.
        """;

    // ------------------------------------------------------------------ gates

    private static WayInfo? Info(string id)
        => CarrierWays.FirstOrDefault(w => w.Id == id) ?? ProtectionWays.FirstOrDefault(w => w.Id == id);

    /// <summary>The way's gate is open: LAN direct always; another only in a lab mock with all its switches.</summary>
    private bool Live(string id)
    {
        if (id == "lan") return true;
        if (!_lab) return false;
        WayInfo? info = Info(id);
        return info is not null && info.Switches.All(s => LabSwitches.Contains(s));
    }

    private bool OpenedByLab(string id) => id != "lan" && Live(id);

    private IEnumerable<string> LabWays()
        => CarrierWays.Concat(ProtectionWays).Select(w => w.Id).Where(OpenedByLab);

    private bool NodeAnswers => _node.Running && !string.IsNullOrEmpty(_node.Listening);

    /// <summary>The way a setting turns on (its gate), or null for one without a gate.</summary>
    private static string? GateOf(string setting) => setting switch
    {
        "internet" => "internet",
        "p2p" or "port_mapping" => "p2p",
        "proxy" => "proxy",
        "nearby" => "nearby",
        "carry_for_friends" or "friends_carry_mine" => "custody",
        "handoff" => "handoff",
        "relay_for_friends" => "friend_relay",
        _ => null,
    };

    // ------------------------------------------------------------------ network.status

    private bool? SettingOf(string id) => id switch
    {
        "lan" => !_lanOff,
        "internet" => _internet,
        "port_mapping" => _portMapping == true,
        "p2p" => _p2p,
        "friend_relay" => _relayOffer,
        "custody" => _friendsCarryMine == true || _routes.Values.Any(r => r.MailFriends.Count > 0),
        "handoff" => _handoff,
        "nearby" => _nearby == true,
        _ => null,
    };

    private bool? RunningOf(string id)
    {
        if (!NodeAnswers) return null;
        return id switch
        {
            "lan" => !_lanOff,
            "lan_beacon" => false,
            "internet" => _usedInternet,
            "port_mapping" => _usedP2p && _usedPortMapping,
            "p2p" => _usedP2p,
            "friend_relay" => _usedP2p && _relayOffer && Live("friend_relay"),
            "websocket" => false,
            "proxy" => false,
            "route_hints" => Live("route_hints"),
            "nearby" => Live("nearby") && _nearby == true && BluetoothLe,
            // Tor (no switch): ready in the lab mock while its node answers.
            "tor" => Live("tor"),
            _ => null,
        };
    }

    private static bool CanSet(string id)
        => id is "lan" or "internet" or "port_mapping" or "p2p" or "friend_relay" or "proxy" or "custody" or "handoff" or "nearby";

    private JsonObject WayNode(WayInfo info)
    {
        string id = info.Id;
        bool? setting = SettingOf(id);
        string state;
        string reason;
        if (id == "nearby" && !BluetoothLe)
        {
            state = WayStates.NotOnThisOs;
            reason = NotHereReason;
        }
        else if (!Live(id))
        {
            state = WayStates.Preview;
            reason = HeldReason;
        }
        else if (setting == false || (id == "lan_beacon" && _lanOff))
        {
            state = WayStates.Off;
            reason = id switch
            {
                "lan" => "Turned off on this computer: Raven doesn't send or receive on this network until it is turned on again.",
                "custody" => "Raven doesn't leave your messages with friends or collect mail from them.",
                _ => "Turned off.",
            };
        }
        else if (OpenedByLab(id))
        {
            state = WayStates.Lab;
            reason = id == "tor" && NodeAnswers ? "Ready." : LabReason;
        }
        else
        {
            state = WayStates.On;
            reason = id == "lan" ? "On. Works without the internet." : "On.";
        }

        var way = Obj(("id", id), ("name", info.Name), ("state", state), ("reason", reason), ("lab", OpenedByLab(id)),
                      ("gate", info.Gate), ("can_set", CanSet(id)));
        way["lab_switches"] = new JsonArray(info.Switches.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        if (setting is bool on) way["setting"] = on;
        if (RunningOf(id) is bool running) way["running"] = running;
        if (id == "tor" && Live("tor") && NodeAnswers) way["detail"] = MockTorWords;
        if (id == "lan")
            way["detail"] = _lanOff ? "LAN: off (raven lan on turns it back on)" : $"LAN: on, listening on {_node.Listening ?? "nothing yet"}";
        return way;
    }

    private JsonObject NetworkStatusNode()
    {
        var carriers = new JsonArray();
        foreach (WayInfo info in CarrierWays) carriers.Add(WayNode(info));
        var protections = new JsonArray();
        foreach (WayInfo info in ProtectionWays) protections.Add(WayNode(info));

        var listening = new JsonArray();
        if (_node.Running && _node.Listening is string l) listening.Add(JsonValue.Create(l));
        if (NodeAnswers && _usedInternet) listening.Add(JsonValue.Create(InternetListen));

        var status = Obj(("build", "dev"), ("node", NodeAnswers ? "running" : "not_running"), ("port", DemoPort), ("hide_ip", _hideIp));
        status["carriers"] = carriers;
        status["protections"] = protections;
        status["listening"] = listening;
        if (_internetListen.Length > 0) status["internet_listen"] = _internetListen;
        if (_p2pListen.Length > 0) status["p2p_listen"] = _p2pListen;
        if (NodeAnswers && _usedP2p)
        {
            string port = P2pPort;
            status["p2p_addresses"] = new JsonArray(
                JsonValue.Create($"/ip4/{DemoLanAddress}/tcp/{port}/p2p/{MockSelfPeerId}"),
                JsonValue.Create($"/ip4/{DemoLanAddress}/udp/{port}/quic-v1/p2p/{MockSelfPeerId}"));
        }
        if (_lanUntilMs is long until) status["lan_until_ms"] = until;
        status["port_mapping"] = PortMappingNode();
        status["relays"] = RelaysNode();
        var proxy = Obj(("setting", _proxy), ("in_use", ""));
        proxy["lines"] = new JsonArray();
        status["proxy"] = proxy;
        status["nearby"] = NearbyNode();
        // Tor (lab): raven-node's Status has its line only while it runs.
        if (Live("tor") && NodeAnswers)
            status["tor"] = Obj(("phase", "ready"), ("words", MockTorWords), ("outages", MockTorOutages));
        status["carry"] = CarryNode();
        var effects = new JsonObject();
        foreach (KeyValuePair<string, string> kv in Effects) effects[kv.Key] = kv.Value;
        var help = Obj(("bridge", _bridge), ("store", _store), ("relay", _relay), ("automatic", _automatic));
        help["effects"] = effects;
        status["help"] = help;
        status["lab_switches"] = new JsonArray((_lab ? LabSwitches : Array.Empty<string>()).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        status["texts"] = Obj(("carry_for_friends", CarryForFriendsText), ("friends_carry_mine", FriendsCarryMineText), ("handoff", HandoffText));
        return status;
    }

    private string InternetListen => _internetListen.Length > 0 ? _internetListen : "0.0.0.0:7422";

    private string P2pPort => _p2pListen.Length > 0 ? _p2pListen : "7423";

    private JsonObject PortMappingNode()
    {
        string setting = _portMapping switch { true => "on", false => "off", null => "unset" };
        bool mapped = NodeAnswers && _usedP2p && _usedPortMapping;
        string router = !NodeAnswers || !_usedP2p ? "" : mapped ? $"mapped {P2pPort}" : "off";
        var pm = Obj(("setting", setting), ("router", router), ("nat_pmp_pcp", WayStates.Preview));
        if (mapped)
        {
            pm["v4"] = "upnp";
            if (int.TryParse(P2pPort, NumberStyles.None, CultureInfo.InvariantCulture, out int port)) pm["v4_port"] = port;
        }
        return pm;
    }

    private JsonArray RelaysNode()
    {
        var relays = new JsonArray();
        if (!NodeAnswers || !_usedP2p) return relays;
        foreach (string relay in _p2pRelays)
            relays.Add(Obj(("relay", relay), ("state", "reserved"), ("source", "manual")));
        if (Live("friend_relay"))
            relays.Add(Obj(("relay", "/ip4/203.0.113.7/tcp/7423/p2p/12D3KooWMockFriendRelay"), ("state", "reserved"),
                           ("source", "offer"), ("friend_address", AliAddress)));
        return relays;
    }

    private JsonObject NearbyNode()
    {
        bool live = Live("nearby");
        string state = !live ? "held" : !NodeAnswers ? "not_running" : "live";
        var nearby = Obj(("state", state), ("on_this_os", true));
        nearby["saved"] = _nearby is bool saved ? JsonValue.Create(saved) : null;
        var rows = new JsonArray();
        if (state == "live")
        {
            string radio = BluetoothLe ? "powered_on" : "unsupported";
            nearby["enabled"] = _nearby == true;
            nearby["radio"] = radio;
            nearby["address_privacy"] = "public";
            nearby["in_range"] = 0;
            nearby["connected"] = 0;
            rows.Add(Obj(("label", "radio"), ("value", BluetoothLe ? "powered on" : "no Bluetooth LE")));
            rows.Add(Obj(("label", "address"), ("value", "public: nearby stays inactive on this PC")));
        }
        nearby["rows"] = rows;
        return nearby;
    }

    private JsonObject CarryNode()
    {
        int listed = _routes.Values.Count(r => r.MailFriends.Count > 0);
        bool custody = Live("custody");
        int offering = custody ? _friends.Count(f => f.Contact.Verified) : 0;
        var carryForFriends = Obj(("on", _carryForFriends), ("held_messages", _carryForFriends ? 2 : 0),
                                  ("held_bytes", _carryForFriends ? 4096 : 0), ("handoff", _handoff));
        carryForFriends["handoff_lines"] = _handoff
            ? new JsonArray(JsonValue.Create("passed on: 0"), JsonValue.Create("held for others: 0"))
            : new JsonArray();
        bool relayOffering = NodeAnswers && _usedP2p && _relayOffer && Live("friend_relay");
        return new JsonObject
        {
            ["friends_carry_mine"] = Obj(("setting", _friendsCarryMine switch { true => "on", false => "off", null => "not_decided" }),
                                         ("mail_friends", _friendsCarryMine == true ? Math.Min(offering, 2) : 0),
                                         ("chosen_by_hand", false), ("offering", offering), ("offering_over_internet", 0),
                                         ("offering_reachable", 0), ("listed_by_hand", listed)),
            ["carry_for_friends"] = carryForFriends,
            ["letters_left"] = new JsonArray(),
            ["relay_for_friends"] = Obj(("setting", _relayOffer), ("offering", relayOffering), ("offered_to", relayOffering ? 2 : 0)),
        };
    }

    // ------------------------------------------------------------------ network.set

    /// <summary>Everything is checked first (types, gates, the person's yes); then applied in order.</summary>
    private JsonObject NetworkSet(JsonObject a)
    {
        var given = SetOrder.Where(k => a[k] is not null).ToList();
        if (given.Count == 0) throw Bad("network.set needs at least one setting");
        bool agreed = OptBool(a, "agreed") ?? false;
        foreach (string key in given)
        {
            bool on;
            if (key == "proxy")
            {
                string text = OptString(a, key) ?? throw Bad("\"proxy\" must be auto, off or http://host:port");
                if (text.Contains('@')) throw Bad("a proxy address can't hold a user name or password");
                on = !string.Equals(text.Trim(), "off", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                on = OptBool(a, key) ?? throw Bad($"\"{key}\" must be true or false");
            }
            if (!on) continue;
            if (key == "lan" && OptString(a, "lan_for") is string lanFor) MeetingMinutes(lanFor); // a bad time changes nothing
            if (GateOf(key) is string gate && !Live(gate))
                throw new MockError(RavenErrorCodes.NotAvailableYet, Loc.Get("error.notAvailableYet"), $"network.set {key}: held in this version");
            if (NetworkSettings.EngineNeedsConsent(key) && !agreed)
                throw new MockError(RavenErrorCodes.ConsentNeeded, Loc.Get("error.consentNeeded"),
                    $"{key}: show network.status texts.{key} and pass \"agreed\": true with the person's yes");
        }

        var changes = new JsonArray();
        bool restart = false;
        foreach (string key in given)
        {
            JsonNode? value = a[key]?.DeepClone();
            bool on = OptBool(a, key) ?? false;
            bool atRestart = false;
            string effect;
            switch (key)
            {
                case "lan":
                    _lanOff = !on;
                    string? lanFor = on ? OptString(a, "lan_for") : null;
                    _lanUntilMs = lanFor is null ? null : NowMs + MeetingMinutes(lanFor) * 60_000L;
                    effect = !on
                        ? "Raven stops sending and receiving on this network. Friends here can't reach you until you turn it on again."
                        : lanFor is null
                            ? "Raven sends and receives on this network again."
                            : $"Raven sends and receives on this network for {lanFor}, then turns it off by itself.";
                    break;
                case "internet":
                    _internet = on;
                    if (on) _internetListen = OptString(a, "internet_listen") is { Length: > 0 } listen ? listen : InternetListen;
                    atRestart = true;
                    effect = on
                        ? $"Raven listens for friends over the internet on {InternetListen} once its background program restarts."
                        : "Raven closes its internet port when its background program restarts.";
                    break;
                case "p2p":
                    _p2p = on;
                    if (on)
                    {
                        _p2pListen = OptString(a, "p2p_listen") is { Length: > 0 } listen ? listen : P2pPort;
                        if (a["relays"] is JsonArray relays)
                            _p2pRelays = relays.Select(r => r?.GetValue<string>() ?? "").Where(r => r.Length > 0).ToList();
                    }
                    atRestart = true;
                    effect = on
                        ? "Raven reaches friends across networks, directly or through relays, once its background program restarts."
                        : "Raven stops using peer-to-peer and relays when its background program restarts.";
                    break;
                case "port_mapping":
                    _portMapping = on;
                    atRestart = true;
                    effect = on
                        ? "Raven asks your router to open its peer-to-peer port when its background program restarts."
                        : "Raven won't ask your router to open a port.";
                    break;
                case "proxy":
                    _proxy = (OptString(a, key) ?? "auto").Trim();
                    atRestart = true;
                    effect = _proxy.Equals("off", StringComparison.OrdinalIgnoreCase)
                        ? "Raven dials relays directly, never through a web proxy."
                        : "Raven uses a web proxy for relays when a network lets only web traffic out (applies when its background program restarts).";
                    break;
                case "nearby":
                    _nearby = on;
                    effect = on ? "Raven may use Bluetooth to reach your verified friends when they are in range." : "Raven stops using Bluetooth.";
                    break;
                case "carry_for_friends":
                    _carryForFriends = on;
                    effect = on
                        ? "This computer holds sealed messages for your verified friends while they are offline."
                        : "This computer stops taking messages for friends.";
                    break;
                case "friends_carry_mine":
                    _friendsCarryMine = on;
                    effect = on
                        ? "When a friend can't be reached, Raven may leave a sealed copy with a friend you both verified."
                        : "Raven doesn't leave your messages with friends or collect mail from them.";
                    break;
                case "handoff":
                    _handoff = on;
                    effect = on
                        ? "This computer may pass sealed messages on between your verified friends."
                        : "New handoffs are refused.";
                    break;
                case "relay_for_friends":
                    _relayOffer = on;
                    effect = on ? "This computer lends a relay to your verified friends." : "This computer stops relaying for friends.";
                    break;
                case "bridge": _bridge = on; _automatic = false; effect = Effects["bridge"]; break;
                case "store": _store = on; _automatic = false; effect = Effects["store"]; break;
                case "relay": _relay = on; _automatic = false; effect = Effects["relay"]; break;
                default:
                    _hideIp = on;
                    effect = on
                        ? "Your contacts no longer learn this computer's IP address from Raven."
                        : "Your contacts may reach you directly again.";
                    break;
            }
            restart |= atRestart;
            var change = Obj(("setting", key), ("effect", effect), ("applies", atRestart ? "restart" : "now"));
            change["value"] = value;
            change["detail"] = new JsonArray();
            changes.Add(change);
        }
        var result = new JsonObject { ["changes"] = changes, ["status"] = NetworkStatusNode() };
        result["restart_needed"] = restart && NodeAnswers;
        return result;
    }

    private static long MeetingMinutes(string text)
    {
        string t = text.Trim().ToLowerInvariant();
        long per = t.EndsWith('m') ? 1 : t.EndsWith('h') ? 60 : 0;
        if (per == 0 || !long.TryParse(t[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out long n) || n * per is < 1 or > 24 * 60)
            throw Bad("lan_for takes minutes or hours: 30m, 1h, 3h, ... up to 24h");
        return n * per;
    }

    /// <summary>node.start: the background program reads the settings that waited for it.</summary>
    private void ApplyNetworkAtStart()
    {
        _usedInternet = _internet && Live("internet");
        _usedP2p = _p2p && Live("p2p");
        _usedPortMapping = _portMapping == true;
        _lockedV2 = false; // a fresh background program has its keys
    }

    /// <summary>
    /// node.stop as the engine's stop rules on Windows say: a login service keeps it
    /// ("managed"); the app stops only a background program it started ("stopped"); one
    /// started elsewhere gets "advice" (how to stop it by hand).
    /// </summary>
    private JsonObject NodeStop()
    {
        string word;
        string? note = null;
        if (LoginServiceRunsNode && _node.Running)
        {
            word = NodeStopResults.Managed;
            note = "Raven's background program runs as a login service, which would start it again; it was left running.";
        }
        else if (!_node.Running)
        {
            word = NodeStopResults.NotRunning;
        }
        else if (_node.StartedByApp)
        {
            word = NodeStopResults.Stopped;
            _node.Running = false;
            _node.StartedByApp = false;
            _node.Pid = null;
            _node.Listening = null;
            EmitNode(NowMs);
        }
        else
        {
            word = NodeStopResults.Advice;
            note = $"taskkill /F /PID {_node.Pid ?? 0}";
        }
        var result = ToNode(_node).AsObject();
        result["stop"] = word;
        if (note is not null) result["note"] = note;
        return result;
    }

    // ------------------------------------------------------------------ network.check

    private JsonObject NetworkCheck(bool router)
    {
        // As raven-client says it: plain words without commands, the next step once, the CLI's own
        // words under "technical".
        string lan = _lanOff ? "off" : "on";
        var summary = new JsonArray
        {
            Obj(("key", "identity"), ("label", "identity"), ("value", "OK")),
            Obj(("key", "raven_node"), ("label", "raven-node"), ("value", _node.Running ? "running; you can send and receive" : "NOT running")),
            Obj(("key", "lan"), ("label", "LAN"), ("value", lan)),
            Obj(("key", "contacts"), ("label", "contacts"), ("value", _friends.Count.ToString(CultureInfo.InvariantCulture))),
        };
        var rows = new JsonArray
        {
            Obj(("key", "receiving"), ("value", _node.Running ? "yes" : "not_running"),
                ("plain", _node.Running ? "Raven receives messages on this network." : "Raven's background program isn't running.")),
            _usedP2p && NodeAnswers
                ? Obj(("key", "reach_class"), ("value", "relayed (behind a router)"))
                : Obj(("key", "reach_class"), ("value", "off (p2p host not running)"),
                      ("plain", "Peer-to-peer isn't running, so only direct ways are used.")),
            Obj(("key", "policy"), ("value", $"bridge={Lower(_bridge)} store={Lower(_store)} relay={Lower(_relay)}")),
        };
        var check = Obj(("next_step", "All set: Raven can send and receive messages on this computer."));
        check["summary"] = summary;
        check["rows"] = rows;
        check["technical"] = new JsonArray
        {
            "LAN: " + (_lanOff ? "off (raven lan on turns it back on)" : "on"),
            "next step: all set. Send a test message: ash send --contact NAME",
        };
        if (router)
        {
            check["router"] = new JsonArray
            {
                Obj(("key", "router_gateway_v4"), ("value", "found")),
                Obj(("key", "router_pcp"), ("value", "no"), ("plain", "Your router didn't answer PCP.")),
                Obj(("key", "router_upnp_igd"), ("value", "none"), ("plain", "No UPnP router answered.")),
            };
        }
        return check;
    }

    private static string Lower(bool value) => value ? "true" : "false";

    // ------------------------------------------------------------------ contacts.routes

    private string RouteState(string way) => !Live(way) ? WayStates.Preview : OpenedByLab(way) ? WayStates.Lab : WayStates.On;

    private JsonObject ContactRoutesNode(Friend f)
    {
        _routes.TryGetValue(f.Contact.Address, out FriendRoutes? r);
        r ??= new FriendRoutes();
        var routes = new JsonArray();
        JsonObject Route(string kind, string label, string value, string state, IEnumerable<string>? via = null)
        {
            var o = Obj(("kind", kind), ("label", label), ("value", value), ("state", state));
            o["via"] = new JsonArray((via ?? Array.Empty<string>()).Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
            return o;
        }
        if (f.Contact.Lan is string lan) routes.Add(Route("lan", "Same network (Wi-Fi, cable or hotspot)", lan, RouteState("lan")));
        if (r.Internet is string inet) routes.Add(Route("internet", "Internet direct", inet, RouteState("internet")));
        if (r.P2p is string p2p) routes.Add(Route("p2p", "Peer-to-peer and relays", p2p, RouteState("p2p"), r.Via));
        if (f.Contact.Verified) routes.Add(Route("nearby", "Bluetooth nearby", "", RouteState("nearby")));

        var listed = new JsonArray();
        foreach (string address in r.MailFriends)
        {
            string? name = _friends.FirstOrDefault(x => x.Contact.Address == address)?.Contact.Name;
            listed.Add(Obj(("address", address), ("name", name), ("usable", true)));
        }
        var told = new JsonArray();
        bool hintsLive = Live("route_hints");
        if (hintsLive && f.Contact.Address == AliAddress)
            told.Add(Obj(("address", MinaAddress), ("name", "Mina"), ("takes_handoffs", true)));

        var carriers = Obj(("hints", RouteState("route_hints")), ("told_unknown", 0), ("receipts_stranded", false));
        carriers["told"] = told;
        carriers["listed"] = listed;
        if (f.Contact.Verified)
        {
            carriers["coverage"] = told.Count > 0 || listed.Count > 0
                ? Obj(("kind", "mutual_friend"), ("name", "a friend you both verified carries it"), ("until", "up to a day"))
                : Obj(("kind", "none"), ("name", "nobody can carry it"), ("until", "it goes when you meet"));
        }
        // How long a message written now could wait (a release build: until the secure connection
        // with a verified friend ends; a friend not verified was never connected with: a first message).
        carriers["away"] = f.Contact.Verified
            ? Obj(("until_ms", NowMs + 20 * Hour))
            : Obj(("meet_first", true), ("first_message", true));

        var result = Obj(("address", f.Contact.Address), ("verified", f.Contact.Verified), ("hide_ip", r.HideIp));
        result["routes"] = routes;
        result["carriers"] = carriers;
        if (hintsLive && f.Contact.Address == AliAddress)
        {
            result["learned"] = Obj(("relays", 1), ("direct", 1), ("mail_friends", 1), ("peer_id", "12D3KooWMockAliPeer"),
                                    ("peer_id_proven", true), ("lan_port", DemoPort), ("expires_at_ms", NowMs + 7 * 24 * Hour));
        }
        return result;
    }

    private JsonObject SetContactRoutes(JsonObject a)
    {
        Friend f = FindFriend(ReqString(a, "address"));
        string address = f.Contact.Address;
        if (!_routes.TryGetValue(address, out FriendRoutes? r))
        {
            r = new FriendRoutes();
            _routes[address] = r;
        }
        // Everything is checked before anything is saved.
        string? inet = OptString(a, "internet")?.Trim();
        if (inet is { Length: > 0 } && !inet.Contains(':')) throw Bad("internet must be host:port");
        List<string>? custodians = null;
        if (a["custodians"] is JsonArray list)
        {
            custodians = list.Select(x => x?.GetValue<string>() ?? "").ToList();
            bool ok = custodians.Count <= 2
                && custodians.All(p => p != address && _friends.Any(x => x.Contact.Address == p && x.Contact.Verified));
            if (!ok) throw Bad("mail friends must be up to two verified friends");
        }

        if (inet is { Length: > 0 }) r.Internet = inet;
        if (OptString(a, "p2p")?.Trim() is { Length: > 0 } p2p) r.P2p = p2p;
        if (a["via"] is JsonArray via) r.Via = via.Select(v => v?.GetValue<string>() ?? "").Where(v => v.Length > 0).ToList();
        if (a["clear"] is JsonArray clear)
        {
            foreach (JsonNode? c in clear)
            {
                string? what = c?.GetValue<string>();
                if (what == "lan") f.Contact.Lan = null;
                if (what == "internet") r.Internet = null;
                if (what == "p2p")
                {
                    r.P2p = null;
                    r.Via.Clear();
                }
            }
        }
        if (custodians is not null) r.MailFriends = custodians;
        if (OptBool(a, "hide_ip") is bool hide) r.HideIp = hide;
        return new JsonObject { ["contact"] = ToNode(f.Contact), ["routes"] = ContactRoutesNode(f) };
    }

    // ------------------------------------------------------------------ security, Lock v2, updates

    private JsonObject SecurityStatusNode()
    {
        bool live = Live("security_events");
        var notices = new JsonArray();
        if (live)
        {
            var notice = Obj(("id", 1), ("level", "warning"), ("name", "Unknown device"),
                ("headline", "Someone who isn't your contact keeps trying to connect"),
                ("what_raven_did", "Raven refused every attempt and stayed silent to it."),
                ("what_to_do", "Nothing. If it keeps happening on this network, tell whoever runs it."),
                ("when", "this morning"));
            notice["details"] = new JsonArray(
                Obj(("label", "How serious"), ("value", "low")),
                Obj(("label", "What Raven did"), ("value", "refused 3 connections")));
            notices.Add(notice);
        }
        var sec = Obj(("available", live), ("lab", OpenedByLab("security_events")), ("kept", live ? "live" : "off"), ("counted", live ? 2 : 0));
        sec["notices"] = notices;
        if (live && NodeAnswers)
        {
            sec["node"] = Obj(("strangers_refused_1h", 3), ("failed_handshakes_1h", 0), ("bans_active", 0),
                              ("link_tag_failures_24h", 0), ("alerts_open", 0));
        }
        return sec;
    }

    private JsonObject LockStateNode()
    {
        if (!_lab)
            return Obj(("available", false), ("lab", false), ("supported", false), ("locked", _profile == ProfileStates.Locked));
        bool live = Live("lock_v2");
        return Obj(("available", live), ("lab", OpenedByLab("lock_v2")), ("supported", live && NodeAnswers),
                   ("locked", live && _lockedV2), ("has_passphrase", true));
    }

    private JsonObject LockNow()
    {
        if (!Live("lock_v2"))
            throw new MockError(RavenErrorCodes.NotAvailableYet, Loc.Get("error.notAvailableYet"), "lock.now: Lock v2 is held in this version; `raven lock` stops Raven instead");
        if (!NodeAnswers)
            throw new MockError(RavenErrorCodes.NotAvailableYet,
                "Lock while running needs Raven's background program running, in a version that can lock. Nothing was changed.",
                "raven-node does not serve Lock v2 (no node, an older one, or its gate shut); `raven lock` stops it instead");
        _lockedV2 = true;
        return LockStateNode();
    }

    private JsonObject LockUnlock(string? passphrase)
    {
        if (!Live("lock_v2"))
            throw new MockError(RavenErrorCodes.NotAvailableYet, Loc.Get("error.notAvailableYet"), "lock.unlock: Lock v2 is held in this version; use `raven unlock`");
        if (!NodeAnswers)
            throw new MockError(RavenErrorCodes.NodeNotRunning, "Raven's background program didn't unlock. Try again.", "no raven-node answers");
        if (!_lockedV2)
        {
            JsonObject open = LockStateNode();
            open["note"] = "Raven wasn't locked.";
            return open;
        }
        long now = NowMs;
        if (now < _unlockWaitUntilMs)
            throw new MockError(RavenErrorCodes.UnlockWait, Loc.Get("error.unlockWait"), "too many wrong passphrases");
        if (string.IsNullOrEmpty(passphrase))
            throw new MockError(RavenErrorCodes.PassphraseNeeded, Loc.Get("error.passphraseNeeded"), "this profile has a Raven passphrase");
        if (passphrase != LabPassphrase)
        {
            _wrongPassphrases++;
            if (_wrongPassphrases >= 3)
            {
                _wrongPassphrases = 0;
                _unlockWaitUntilMs = now + 30 * Second;
                throw new MockError(RavenErrorCodes.UnlockWait, Loc.Get("error.unlockWait"), "three wrong passphrases: wait 30 s");
            }
            throw new MockError(RavenErrorCodes.PassphraseWrong, Loc.Get("error.passphraseWrong"), "wrong passphrase");
        }
        _wrongPassphrases = 0;
        _lockedV2 = false;
        JsonObject state = LockStateNode();
        state["note"] = "Raven's background program has its keys again.";
        return state;
    }

    private JsonObject UpdatesStatusNode()
    {
        bool advisory = _lab && LabSwitches.Contains("RAVEN_LAB_ADVISORY");
        var upd = Obj(("available", advisory), ("bundles", false), ("from_friends", Live("updates_from_friends")),
                      ("lab", advisory), ("install", "cli_only"),
                      ("install_text", "Install updates with `raven update install` in a terminal. The app shows what is ready; it doesn't install yet."));
        upd["lines"] = advisory
            ? new JsonArray(JsonValue.Create("No update is waiting for this computer."), JsonValue.Create("Signed notices from the Raven project are checked."))
            : new JsonArray(JsonValue.Create("Raven updates are not part of this version."));
        if (advisory) upd["summary"] = "No update is waiting.";
        return upd;
    }
}
#endif
