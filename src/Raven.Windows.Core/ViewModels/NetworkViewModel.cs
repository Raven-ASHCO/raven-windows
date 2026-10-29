using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.ViewModels;

/// <summary>One way to reach friends (or a protection) as the Network page shows it.</summary>
public sealed class WayRow
{
    public string Id { get; init; } = string.Empty;

    /// <summary>The engine's name for the way.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The engine's state word (<see cref="WayStates"/>).</summary>
    public string State { get; init; } = string.Empty;

    public Chip StateChip { get; init; }

    /// <summary>One plain line of what the way does (strings.en.json).</summary>
    public string Does { get; init; } = string.Empty;

    /// <summary>Why it is in that state (the engine's sentence).</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>The CLI's own row for the way, where it has one (shown under Details).</summary>
    public string? Detail { get; init; }

    /// <summary>The network.set setting its switch changes; null when the way has no switch.</summary>
    public string? Setting { get; init; }

    public bool IsOn { get; init; }

    /// <summary>
    /// The switch can be used: turning on needs the way's gate open (on, off or lab); turning
    /// off is always allowed, so a way that is on stays switchable even while held.
    /// </summary>
    public bool SwitchEnabled { get; init; }

    /// <summary>"In use now" / "Not in use now" when the background program says.</summary>
    public string? RunningText { get; init; }

    /// <summary>A Windows note (Bluetooth nearby is inactive on Windows).</summary>
    public string? Note { get; init; }

    /// <summary>The Tor way only: its state as raven-node reports it (null otherwise, and while it reports none).</summary>
    public TorView? Tor { get; init; }

    public bool Lab { get; init; }
    public string Gate { get; init; } = string.Empty;
    public IReadOnlyList<string> LabSwitches { get; init; } = Array.Empty<string>();

    public bool HasSwitch => Setting is not null;
}

/// <summary>A help-friends flag (bridge, store, relay) with what it really does.</summary>
/// <param name="Technical">The engine's own words for the flag, shown only under Technical details.</param>
/// <param name="Shown">False hides the switch: Store and Relay change nothing in this version, so they show only while on (E1).</param>
public sealed record FlagRow(string Setting, string Label, bool IsOn, string Effect, string? Technical = null, bool Shown = true);

/// <summary>A setting waiting for the person's yes (the consent sheet).</summary>
public sealed record ConsentRequest(ConsentTopic Topic, string EngineText, string Setting, JsonNode? Value, JsonObject? Companions);

/// <summary>"Restart the background program?": the changes that wait for it.</summary>
public sealed record RestartPrompt(IReadOnlyList<NetworkChange> Changes, bool StartedByApp);

public enum RestartKind
{
    Restarted,
    /// <summary>A login service runs it; left running.</summary>
    Managed,
    /// <summary>Not started by this app: stop it by hand.</summary>
    Advice,
    StillRunning,
    Failed,
}

/// <summary>What happened when the app tried node.stop then node.start.</summary>
public sealed record RestartResult(RestartKind Kind, string Text, NodeStatus? Node);

/// <summary>What a network.set from the page led to.</summary>
public enum SetOutcome
{
    /// <summary>Nothing was sent (busy, no switch, nothing pending).</summary>
    Nothing,
    Changed,
    /// <summary>Changed; a running background program applies it when it starts again.</summary>
    RestartNeeded,
    /// <summary>Nothing was sent yet: <see cref="NetworkViewModel.PendingConsent"/> waits for a yes.</summary>
    NeedsConsent,
    /// <summary>Held in this build (NOT_AVAILABLE_YET); nothing was changed.</summary>
    NotAvailableYet,
    Failed,
}

/// <summary>
/// The Network page (API.md "Network", NETWORK_PARITY.md "What the screens need next"): one
/// row per way from network.status with its state chip, what it does and why it is in that
/// state, a switch where network.set allows it; ports, the router, relays, nearby, who carries
/// mail both ways, the help-friends flags and hide IP; the "Lab switches on" banner (developer
/// builds only); the consent sheet before a setting that helps friends; and "Restart the
/// background program" when a change waits for it. UI-free: the page renders this state.
/// </summary>
public sealed class NetworkViewModel
{
    private readonly IRavenEngine _engine;
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

    public NetworkViewModel(IRavenEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>Any state below changed: render again.</summary>
    public event Action? Changed;

    public NetworkStatus? Status { get; private set; }
    public IReadOnlyList<WayRow> Ways { get; private set; } = Array.Empty<WayRow>();
    public IReadOnlyList<WayRow> Protections { get; private set; } = Array.Empty<WayRow>();
    public IReadOnlyList<FlagRow> HelpFlags { get; private set; } = Array.Empty<FlagRow>();
    public bool HelpAutomatic { get; private set; }

    /// <summary>Ports and addresses of this computer.</summary>
    public IReadOnlyList<string> ComputerLines { get; private set; } = Array.Empty<string>();

    /// <summary>Every peer-to-peer address, this computer's own among them: Technical details only (E1).</summary>
    public IReadOnlyList<string> ComputerTechnical { get; private set; } = Array.Empty<string>();

    /// <summary>Router port opening: the setting and what the router said.</summary>
    public IReadOnlyList<string> RouterLines { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> RelayLines { get; private set; } = Array.Empty<string>();

    /// <summary>The relays added by hand that the background program uses now (to edit them).</summary>
    public IReadOnlyList<string> ManualRelays { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> ProxyLines { get; private set; } = Array.Empty<string>();

    /// <summary>The CLI's proxy lines ("change it: raven node proxy ..."): Technical details only (E1).</summary>
    public IReadOnlyList<string> ProxyTechnical { get; private set; } = Array.Empty<string>();

    /// <summary>The saved http://host:port proxy, or empty (auto or off).</summary>
    public string ProxyAddress { get; private set; } = string.Empty;
    public IReadOnlyList<string> NearbyLines { get; private set; } = Array.Empty<string>();
    public string? NearbyNote { get; private set; }

    /// <summary>Bluetooth nearby's raw rows ("turn on: raven nearby enable") and lab switches: Technical details only (E1).</summary>
    public IReadOnlyList<string> NearbyTechnical { get; private set; } = Array.Empty<string>();

    /// <summary>Who carries your mail.</summary>
    public IReadOnlyList<string> CarryMineLines { get; private set; } = Array.Empty<string>();

    /// <summary>The mail this computer carries for friends, handoffs and the relay it lends.</summary>
    public IReadOnlyList<string> CarryForFriendsLines { get; private set; } = Array.Empty<string>();

    /// <summary>The handoff counters, as raven-node words them: Technical details only (E1).</summary>
    public IReadOnlyList<string> CarryTechnical { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> LettersLeft { get; private set; } = Array.Empty<string>();
    public bool CarryForFriendsOn { get; private set; }

    /// <summary>Turning it on needs the custody gate open; turning it off is always allowed.</summary>
    public bool CarryForFriendsEnabled { get; private set; }
    public bool HideIp { get; private set; }

    /// <summary>Windows Firewall: the prompt and the ports, while Internet direct or p2p is on.</summary>
    public IReadOnlyList<string> FirewallLines { get; private set; } = Array.Empty<string>();

    /// <summary>A developer build with lab switches set (never in a release build: it reads none).</summary>
    public bool ShowLabBanner { get; private set; }
    public string LabBannerText { get; private set; } = string.Empty;

    public bool Busy { get; private set; }
    public ErrorInfo? Error { get; private set; }

    /// <summary>The effects of the last network.set (shown once).</summary>
    public IReadOnlyList<NetworkChange> LastChanges { get; private set; } = Array.Empty<NetworkChange>();

    /// <summary>A setting waiting for the person's yes; nothing was sent yet.</summary>
    public ConsentRequest? PendingConsent { get; private set; }

    /// <summary>A change waits for the background program to start again: ask now.</summary>
    public RestartPrompt? Restart { get; private set; }

    /// <summary>Some changes still wait for a restart (the person chose Later).</summary>
    public bool RestartPending { get; private set; }

    public RestartResult? LastRestart { get; private set; }

    // ------------------------------------------------------------------ loading

    /// <summary>Reads network.status (and the friends' names for the relays they lend).</summary>
    public async Task LoadAsync()
    {
        Busy = true;
        Raise();
        try
        {
            NetworkStatus status = await _engine.NetworkStatusAsync();
            await LoadNamesAsync();
            Apply(status);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ErrorInfo.From(ex);
        }
        finally
        {
            Busy = false;
            Raise();
        }
    }

    private async Task LoadNamesAsync()
    {
        try
        {
            IReadOnlyList<Contact> contacts = await _engine.ContactsListAsync();
            _names.Clear();
            foreach (Contact c in contacts) _names[c.Address] = ContactPresenter.DisplayName(c);
        }
        catch (RavenException)
        {
            // Relays lent by friends then show the friend's short address.
        }
    }

    private async Task ReloadQuietlyAsync()
    {
        try
        {
            Apply(await _engine.NetworkStatusAsync());
        }
        catch (RavenException)
        {
            // The page keeps what it showed; the error that brought us here is on screen.
        }
    }

    // ------------------------------------------------------------------ changes

    /// <summary>A way's switch.</summary>
    public Task<SetOutcome> SetSwitchAsync(WayRow row, bool on)
    {
        if (row.Setting is null) return Task.FromResult(SetOutcome.Nothing);
        JsonNode? value = row.Setting == NetworkSettings.Proxy
            ? JsonValue.Create(on ? "auto" : "off")
            : JsonValue.Create(on);
        return SetAsync(row.Setting, value);
    }

    /// <summary>A plain on/off setting (carry_for_friends, bridge, store, relay, hide_ip...).</summary>
    public Task<SetOutcome> SetFlagAsync(string setting, bool on) => SetAsync(setting, JsonValue.Create(on));

    /// <summary>LAN on for a meeting ("30m", "1h", ... up to "24h"), then off by itself.</summary>
    public Task<SetOutcome> SetLanForAsync(string duration)
        => SetAsync(NetworkSettings.Lan, JsonValue.Create(true), new JsonObject { [NetworkSettings.LanFor] = duration });

    /// <summary>
    /// Peer-to-peer with these relays (multiaddrs or friends' names, as <c>raven node p2p on
    /// --relay</c> takes them): turns p2p on, applied when the background program starts again.
    /// </summary>
    public Task<SetOutcome> SetP2pRelaysAsync(IEnumerable<string> relays)
    {
        var list = new JsonArray(relays
            .Select(r => r.Trim())
            .Where(r => r.Length > 0)
            .Select(r => (JsonNode?)JsonValue.Create(r))
            .ToArray());
        return SetAsync(NetworkSettings.P2p, JsonValue.Create(true), new JsonObject { [NetworkSettings.Relays] = list });
    }

    /// <summary>The web proxy: "auto", "off" or "http://host:port" (never a user name or password).</summary>
    public Task<SetOutcome> SetProxyAsync(string text)
    {
        string value = text.Trim();
        return SetAsync(NetworkSettings.Proxy, JsonValue.Create(value.Length == 0 ? "auto" : value));
    }

    /// <summary>
    /// One network.set. Turning on a setting that helps friends first waits for the person's
    /// yes (<see cref="PendingConsent"/>; nothing is sent); everything else is sent at once.
    /// </summary>
    public Task<SetOutcome> SetAsync(string setting, JsonNode? value, JsonObject? companions = null)
    {
        if (Busy) return Task.FromResult(SetOutcome.Nothing);
        if (TurnsOn(value) && ConsentTopics.For(setting) is ConsentTopic topic)
        {
            PendingConsent = new ConsentRequest(topic, ConsentTopics.EngineText(Status, setting), setting, value?.DeepClone(), CloneObject(companions));
            Error = null;
            Raise();
            return Task.FromResult(SetOutcome.NeedsConsent);
        }
        return SendAsync(setting, value, companions, agreed: false);
    }

    /// <summary>The person said yes on the consent sheet: send the setting with "agreed": true.</summary>
    public async Task<SetOutcome> AgreeAsync()
    {
        ConsentRequest? request = PendingConsent;
        if (request is null) return SetOutcome.Nothing;
        PendingConsent = null;
        return await SendAsync(request.Setting, request.Value, request.Companions, agreed: true);
    }

    /// <summary>The person said no (or closed the sheet): nothing is sent, nothing changes.</summary>
    public void DeclineConsent()
    {
        PendingConsent = null;
        if (Error?.IsConsentNeeded == true) Error = null;
        Raise();
    }

    private async Task<SetOutcome> SendAsync(string setting, JsonNode? value, JsonObject? companions, bool agreed)
    {
        if (Busy) return SetOutcome.Nothing;
        var args = new JsonObject { [setting] = value?.DeepClone() };
        if (companions is not null)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in companions) args[pair.Key] = pair.Value?.DeepClone();
        }
        if (agreed) args[NetworkSettings.Agreed] = true;

        Busy = true;
        Error = null;
        LastChanges = Array.Empty<NetworkChange>();
        LastRestart = null;
        Raise();
        try
        {
            NetworkSetResult result = await _engine.NetworkSetAsync(args);
            LastChanges = result.Changes;
            Apply(result.Status);
            if (!result.RestartNeeded) return SetOutcome.Changed;
            RestartPending = true;
            List<NetworkChange> waiting = result.Changes.Where(c => c.Applies == "restart").ToList();
            Restart = new RestartPrompt(waiting, await NodeStartedByAppAsync());
            return SetOutcome.RestartNeeded;
        }
        catch (Exception ex)
        {
            ErrorInfo error = ErrorInfo.From(ex);
            Error = error;
            if (error.IsConsentNeeded)
            {
                // The engine wants a yes this app didn't ask for (a newer engine): ask now.
                ConsentTopic topic = ConsentTopics.For(setting) ?? ConsentTopics.Generic(setting);
                PendingConsent = new ConsentRequest(topic, ConsentTopics.EngineText(Status, setting), setting, value?.DeepClone(), CloneObject(companions));
                return SetOutcome.NeedsConsent;
            }
            await ReloadQuietlyAsync(); // show what the engine really has
            return error.IsNotAvailableYet ? SetOutcome.NotAvailableYet : SetOutcome.Failed;
        }
        finally
        {
            Busy = false;
            Raise();
        }
    }

    private async Task<bool> NodeStartedByAppAsync()
    {
        try
        {
            NodeStatus node = await _engine.NodeStatusAsync();
            return node.StartedByApp;
        }
        catch (RavenException)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ restart

    /// <summary>
    /// node.stop, then node.start when it stopped. Honest about what the app may not do: a
    /// login service keeps its background program ("managed"), and on Windows the app stops
    /// only a background program it started itself ("advice": how to stop it by hand).
    /// </summary>
    public async Task<RestartResult> RestartNodeAsync()
    {
        Busy = true;
        Error = null;
        LastRestart = null;
        Raise();
        RestartResult result;
        try
        {
            NodeStatus stopped = await _engine.NodeStopAsync();
            string word = stopped.Stop ?? (stopped.Running ? NodeStopResults.Failed : NodeStopResults.Stopped);
            switch (word)
            {
                case NodeStopResults.Stopped:
                case NodeStopResults.NotRunning:
                    NodeStatus started = await _engine.NodeStartAsync();
                    result = new RestartResult(RestartKind.Restarted, Loc.Get("network.restart.done"), started);
                    RestartPending = false;
                    break;
                case NodeStopResults.Managed:
                    result = new RestartResult(RestartKind.Managed, Loc.Get("network.restart.managed"), stopped);
                    break;
                case NodeStopResults.Advice:
                    result = new RestartResult(RestartKind.Advice, WithNote("network.restart.advice", stopped.Note), stopped);
                    break;
                case NodeStopResults.StillRunning:
                    result = new RestartResult(RestartKind.StillRunning, Loc.Get("network.restart.stillRunning"), stopped);
                    break;
                default:
                    result = new RestartResult(RestartKind.Failed, WithNote("network.restart.failed", stopped.Note), stopped);
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorInfo error = ErrorInfo.From(ex);
            Error = error;
            result = new RestartResult(RestartKind.Failed, WithNote("network.restart.failed", error.Message), null);
        }
        Restart = null;
        LastRestart = result;
        await ReloadQuietlyAsync();
        Busy = false;
        Raise();
        return result;
    }

    /// <summary>"Later": the changes keep waiting; the page says so until a restart.</summary>
    public void DismissRestart()
    {
        Restart = null;
        Raise();
    }

    /// <summary>The page showed the last changes and restart result; forget them.</summary>
    public void ClearNotices()
    {
        LastChanges = Array.Empty<NetworkChange>();
        LastRestart = null;
        Raise();
    }

    // ------------------------------------------------------------------ building the view

    private void Apply(NetworkStatus s)
    {
        Status = s;
        Ways = s.Carriers.Select(w => Row(w, s)).ToList();
        Protections = s.Protections.Select(w => Row(w, s)).ToList();

        HelpFlags = new[]
        {
            new FlagRow(NetworkSettings.Bridge, Loc.Get("network.help.bridge"), s.Help.Bridge,
                Loc.Get("network.help.bridge.plain"), Technical: s.Help.Effects.Bridge),
            new FlagRow(NetworkSettings.Store, Loc.Get("network.help.store"), s.Help.Store, s.Help.Effects.Store, Shown: s.Help.Store),
            new FlagRow(NetworkSettings.Relay, Loc.Get("network.help.relay"), s.Help.Relay, s.Help.Effects.Relay, Shown: s.Help.Relay),
        };
        HelpAutomatic = s.Help.Automatic;
        HideIp = s.HideIp;

        ComputerLines = BuildComputerLines(s);
        ComputerTechnical = s.P2pAddresses.Select(a => Loc.Format("network.thisComputer.p2pAddress", ("addr", a))).ToList();
        RouterLines = BuildRouterLines(s.PortMapping);
        RelayLines = BuildRelayLines(s.Relays);
        ManualRelays = s.Relays.Where(r => r.Source == "manual" && r.Relay.Length > 0).Select(r => r.Relay).ToList();
        ProxyLines = BuildProxyLines(s.Proxy);
        ProxyTechnical = s.Proxy.Lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        ProxyAddress = s.Proxy.Setting.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? s.Proxy.Setting : string.Empty;
        NearbyLines = BuildNearbyLines(s.Nearby);
        NearbyNote = NetworkPresenter.WindowsNearbyNote(s);
        NearbyTechnical = BuildNearbyTechnical(s);
        CarryMineLines = BuildCarryMineLines(s.Carry.FriendsCarryMine);
        BuildCarryForFriends(s);
        FirewallLines = NetworkPresenter.FirewallLines(s);

        ShowLabBanner = s.Build == "dev" && s.LabSwitches.Count > 0;
        LabBannerText = ShowLabBanner
            ? Loc.Format("network.labBanner.body", ("switches", string.Join(", ", s.LabSwitches)))
            : string.Empty;
    }

    private WayRow Row(NetworkWay w, NetworkStatus s)
    {
        string? setting = w.CanSet ? NetworkPresenter.SettingFor(w.Id) : null;
        bool gateOpen = w.State is WayStates.On or WayStates.Off or WayStates.Lab;
        bool isOn = w.Id switch
        {
            // "auto" is the default, not a choice: the proxy shows on only where its gate is open.
            "proxy" => gateOpen && !string.Equals(s.Proxy.Setting, "off", StringComparison.OrdinalIgnoreCase),
            // The switch is "friends carry my mail" (custody's setting also counts friends listed by hand).
            "custody" => s.Carry.FriendsCarryMine.Setting == "on",
            _ => w.Setting == true,
        };
        bool enabled = setting is not null && (gateOpen || (isOn && w.Id != "proxy"));
        // Windows: nearby stays inactive while Raven takes the PC's Bluetooth address as fixed,
        // even when the radio is on and nearby enabled; don't claim "In use now" then.
        string? note = w.Id == "nearby" ? NetworkPresenter.WindowsNearbyNote(s) : null;
        // One state per row (E1: "Off" beside "In use now"): the chip says it; only a way that is on
        // but not in use now says so.
        string? running = null;
        if (w.Running == false && w.State is WayStates.On or WayStates.Lab && note is null)
            running = Loc.Get("network.running.no");

        return new WayRow
        {
            Id = w.Id,
            Name = TextSanitizer.SingleLine(w.Name).Length > 0 ? TextSanitizer.SingleLine(w.Name) : w.Id,
            State = w.State,
            StateChip = NetworkPresenter.StateChip(w.State),
            Does = NetworkPresenter.WhatItDoes(w.Id),
            Reason = w.Reason,
            Detail = string.IsNullOrWhiteSpace(w.Detail) ? null : w.Detail,
            Setting = setting,
            IsOn = isOn,
            SwitchEnabled = enabled,
            RunningText = running,
            Note = note,
            Tor = w.Id == "tor" ? NetworkPresenter.Tor(s) : null,
            Lab = w.Lab,
            Gate = w.Gate,
            LabSwitches = w.LabSwitches,
        };
    }

    /// <summary>A restart text, then the engine's note (how to stop it by hand, or why it failed) when there is one.</summary>
    private static string WithNote(string key, string? note)
    {
        string text = Loc.Get(key);
        string extra = TextSanitizer.SingleLine(note ?? string.Empty);
        return extra.Length == 0 ? text : text + " " + extra;
    }

    /// <summary>The "This profile's port" line's port: only while raven-node doesn't run.</summary>
    public static int? ProfilePortLine(NetworkStatus s) => s.Node != "running" && s.Port > 0 ? s.Port : null;

    private static List<string> BuildComputerLines(NetworkStatus s)
    {
        var lines = new List<string>();
        // While raven-node runs, the listening lines give the port it really uses; the profile's port
        // is said only while nothing listens (E1: "This profile's port: 7420" under port 17420).
        if (ProfilePortLine(s) is int port) lines.Add(Loc.Format("network.thisComputer.port", ("port", port)));
        if (s.Listening.Count == 0) lines.Add(Loc.Get("network.thisComputer.notListening"));
        foreach (string at in s.Listening) lines.Add(Loc.Format("network.thisComputer.listening", ("addr", at)));
        if (!string.IsNullOrWhiteSpace(s.InternetListen))
            lines.Add(Loc.Format("network.thisComputer.internet", ("addr", s.InternetListen)));
        if (!string.IsNullOrWhiteSpace(s.P2pListen))
            lines.Add(Loc.Format("network.thisComputer.p2p", ("addr", s.P2pListen)));
        // Addresses friends could use; this computer's own (loopback, link-local) only under
        // Technical details (E1: six rows, loopback ones among them).
        foreach (string address in NetworkPresenter.P2pAddressesShown(s.P2pAddresses))
            lines.Add(Loc.Format("network.thisComputer.p2pAddress", ("addr", address)));
        if (s.LanUntilMs is long until && until > 0)
            lines.Add(Loc.Format("network.thisComputer.lanUntil",
                ("time", RelativeTime.Deadline(until, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))));
        if (s.PolicyUnreadable == true) lines.Add(Loc.Get("network.thisComputer.policyUnreadable"));
        return lines;
    }

    private static List<string> BuildRouterLines(PortMapping pm)
    {
        string setting = pm.Setting switch
        {
            "on" => Loc.Get("network.value.on"),
            "off" => Loc.Get("network.value.off"),
            _ => Loc.Get("network.value.notDecided"),
        };
        var lines = new List<string> { Loc.Format("network.portMapping.setting", ("setting", setting)) };
        if (!string.IsNullOrWhiteSpace(pm.Router)) lines.Add(Loc.Format("network.portMapping.router", ("result", pm.Router)));
        // V4 and V6 are raven-node's words (upnp, none, open, ...); V4Port is the port the router forwards.
        if (!string.IsNullOrWhiteSpace(pm.V4))
        {
            lines.Add(pm.V4Port is int p && p > 0
                ? Loc.Format("network.portMapping.v4Port", ("how", pm.V4), ("port", p.ToString(CultureInfo.InvariantCulture)))
                : Loc.Format("network.portMapping.v4", ("how", pm.V4)));
        }
        if (!string.IsNullOrWhiteSpace(pm.V6)) lines.Add(Loc.Format("network.portMapping.v6", ("how", pm.V6)));
        lines.Add(Loc.Format("network.portMapping.natPmpPcp", ("state", NetworkPresenter.StateChip(pm.NatPmpPcp).Text)));
        return lines;
    }

    private List<string> BuildRelayLines(IReadOnlyList<RelayInUse> relays)
    {
        var lines = new List<string>();
        if (relays.Count == 0) lines.Add(Loc.Get("network.relays.none"));
        foreach (RelayInUse relay in relays)
        {
            string line = Loc.Format("network.relays.line", ("relay", relay.Relay), ("state", relay.State));
            string source = relay.Source switch
            {
                "manual" => Loc.Get("network.relays.manual"),
                "offer" => Loc.Format("network.relays.offer", ("name", NameOf(relay.FriendAddress))),
                _ => TextSanitizer.SingleLine(relay.Source),
            };
            lines.Add(source.Length > 0 ? line + " · " + source : line);
        }
        return lines;
    }

    private static List<string> BuildProxyLines(ProxyState proxy)
    {
        string setting = proxy.Setting.ToLowerInvariant() switch
        {
            "" or "auto" => Loc.Get("network.proxy.auto"),
            "off" => Loc.Get("network.proxy.off"),
            _ => proxy.Setting,
        };
        var lines = new List<string> { Loc.Format("network.proxy.setting", ("setting", setting)) };
        if (!string.IsNullOrWhiteSpace(proxy.InUse)) lines.Add(Loc.Format("network.proxy.inUse", ("proxy", proxy.InUse)));
        return lines;
    }

    private static List<string> BuildNearbyTechnical(NetworkStatus s)
    {
        var lines = s.Nearby.Rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Label) || !string.IsNullOrWhiteSpace(r.Value))
            .Select(r => r.Label + ": " + r.Value)
            .ToList();
        if (NetworkPresenter.WindowsNearbyLab(s) is string lab) lines.Add(lab);
        return lines;
    }

    private static List<string> BuildNearbyLines(NearbyState nearby)
    {
        var lines = new List<string>();
        string state = nearby.State switch
        {
            "held" => Loc.Get("network.nearby.state.held"),
            "node_held" => Loc.Get("network.nearby.state.nodeHeld"),
            "not_running" => Loc.Get("network.nearby.state.notRunning"),
            "live" => Loc.Get("network.nearby.state.live"),
            _ => TextSanitizer.SingleLine(nearby.State),
        };
        if (state.Length > 0) lines.Add(state);
        if (!string.IsNullOrWhiteSpace(nearby.Radio)) lines.Add(Loc.Format("network.nearby.radio", ("value", nearby.Radio)));
        if (!string.IsNullOrWhiteSpace(nearby.AddressPrivacy))
            lines.Add(Loc.Format("network.nearby.address", ("value", nearby.AddressPrivacy)));
        if (nearby.InRange is int inRange)
            lines.Add(Loc.Format("network.nearby.inRange", ("n", inRange), ("connected", nearby.Connected ?? 0)));
        return lines;
    }

    private static List<string> BuildCarryMineLines(FriendsCarryMine mine)
    {
        string setting = mine.Setting switch
        {
            "on" => Loc.Get("network.value.on"),
            "off" => Loc.Get("network.value.off"),
            _ => Loc.Get("network.value.notDecided"),
        };
        var lines = new List<string> { Loc.Format("network.carry.mine", ("setting", setting)) };
        if (mine.MailFriends > 0)
        {
            string how = Loc.Get(mine.ChosenByHand ? "network.carry.byHand" : "network.carry.automatic");
            lines.Add(Loc.Format("network.carry.mailFriends", ("n", mine.MailFriends), ("how", how)));
        }
        lines.Add(Loc.Format("network.carry.offering", ("n", mine.Offering), ("internet", mine.OfferingOverInternet), ("reachable", mine.OfferingReachable)));
        if (mine.ListedByHand > 0) lines.Add(Loc.Format("network.carry.listed", ("n", mine.ListedByHand)));
        return lines;
    }

    private void BuildCarryForFriends(NetworkStatus s)
    {
        CarryForFriends carry = s.Carry.CarryForFriends;
        CarryForFriendsOn = carry.On;
        string? custody = s.Way("custody")?.State;
        CarryForFriendsEnabled = carry.On || custody is WayStates.On or WayStates.Off or WayStates.Lab;

        var lines = new List<string>();
        if (carry.On || carry.HeldMessages > 0)
            lines.Add(Loc.Format("network.carry.held", ("n", carry.HeldMessages), ("size", NetworkPresenter.Size(carry.HeldBytes))));
        CarryTechnical = carry.HandoffLines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        RelayForFriends relay = s.Carry.RelayForFriends;
        if (relay.Setting || relay.Offering)
        {
            string state = relay.Offering
                ? Loc.Format("network.carry.relayOffering", ("n", relay.OfferedTo))
                : Loc.Get("network.carry.relayNotOffering");
            lines.Add(Loc.Format("network.carry.relay", ("state", state)));
        }
        CarryForFriendsLines = lines;
        LettersLeft = s.Carry.LettersLeft.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
    }

    private string NameOf(string? address)
    {
        if (string.IsNullOrEmpty(address)) return Loc.Get("network.relays.aFriend");
        if (_names.TryGetValue(address, out string? name)) return name;
        return address.Length <= 12 ? address : address[..7] + "…" + address[^4..];
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>True when the value turns a setting on (true, or a proxy other than "off").</summary>
    public static bool TurnsOn(JsonNode? value)
    {
        if (value is not JsonValue v) return false;
        if (v.TryGetValue(out bool on)) return on;
        if (v.TryGetValue(out string? text)) return !string.Equals(text?.Trim(), "off", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static JsonObject? CloneObject(JsonObject? o) => o?.DeepClone().AsObject();

    private void Raise() => Changed?.Invoke();
}
