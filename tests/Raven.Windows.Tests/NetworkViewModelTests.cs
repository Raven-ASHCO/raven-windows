#if DEBUG
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Mock;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Core.ViewModels;

namespace Raven.Windows.Tests;

/// <summary>
/// The Network page's view model on MockRaven: every way state (on, off, preview, lab, not on
/// this computer), the "Lab switches on" banner, switches, NOT_AVAILABLE_YET, the consent
/// sheet (nothing sent before the yes, then agreed: true) and CONSENT_NEEDED, the restart
/// prompt (stopped, advice, managed), the firewall and nearby notes, and the details.
/// </summary>
[TestClass]
public sealed class NetworkViewModelTests
{
    private static MockRaven Release() => new(ManualTimeProvider.StartingAt2026(), startReady: true, demoEvents: false);

    private static MockRaven Lab() => new(ManualTimeProvider.StartingAt2026(), startReady: true, demoEvents: false, labNetwork: true);

    /// <summary>E1: "This profile's port: 7420" two lines under "Listening on port 17420".</summary>
    [TestMethod]
    public void ProfilePort_IsSaidOnlyWhileNothingListens()
    {
        Assert.IsNull(NetworkViewModel.ProfilePortLine(new NetworkStatus { Node = "running", Port = 17420 }));
        Assert.AreEqual(7420, NetworkViewModel.ProfilePortLine(new NetworkStatus { Node = "not_running", Port = 7420 }));
    }

    private static async Task<NetworkViewModel> LoadedAsync(IRavenEngine engine)
    {
        var vm = new NetworkViewModel(engine);
        await vm.LoadAsync();
        Assert.IsNull(vm.Error, vm.Error?.Detail);
        return vm;
    }

    private static WayRow Way(NetworkViewModel vm, string id) => vm.Ways.Single(w => w.Id == id);

    // ------------------------------------------------------------------ way states

    /// <summary>
    /// A release build: LAN direct is on, and every other way and protection shows the state the
    /// engine reports (held ones as Preview, with no switch to turn on), so the test holds whichever
    /// ways a release opens. No lab banner.
    /// </summary>
    [TestMethod]
    public async Task Release_LanIsOn_EveryOtherWayAsTheEngineSays_AndNoBanner()
    {
        MockRaven engine = Release();
        NetworkViewModel vm = await LoadedAsync(engine);
        NetworkStatus reported = await engine.NetworkStatusAsync();

        WayRow lan = Way(vm, "lan");
        Assert.AreEqual(WayStates.On, lan.State);
        Assert.AreEqual(new Chip(Loc.Get("net.on"), Tone.Success), lan.StateChip);
        Assert.AreEqual(Loc.Get("network.way.lan"), lan.Does);
        Assert.AreEqual(NetworkSettings.Lan, lan.Setting);
        Assert.IsTrue(lan.IsOn && lan.SwitchEnabled);
        Assert.IsNull(lan.RunningText, "one state per row: On already says it (E1)");
        Assert.IsNotNull(lan.Detail, "the CLI's LAN row goes under Details");

        Assert.AreEqual(14, vm.Ways.Count, "13 ways and Tor");
        if (reported.Tor is null)
            Assert.IsNull(Way(vm, "tor").Tor, "no Tor state from raven-node: none shown");
        foreach (WayRow way in vm.Ways.Where(w => w.Id != "lan"))
        {
            NetworkWay? said = reported.Way(way.Id);
            Assert.IsNotNull(said, way.Id);
            Assert.AreEqual(said.State, way.State, way.Id);
            Assert.AreEqual(NetworkPresenter.StateChip(said.State), way.StateChip, way.Id);
            Assert.AreEqual(said.Reason, way.Reason, way.Id);
            Assert.IsFalse(string.IsNullOrEmpty(way.Does), way.Id);
            Assert.IsFalse(way.Lab, way.Id + ": a release build opens no way by a lab switch");
            if (said.State == WayStates.Preview)
            {
                Assert.AreEqual(Loc.Get("net.preview"), way.StateChip.Text, way.Id);
                Assert.AreEqual(Tone.Warning, way.StateChip.Tone, way.Id);
                Assert.IsFalse(way.SwitchEnabled, way.Id + ": a held way can't be switched on");
                Assert.IsFalse(way.IsOn, way.Id);
                Assert.AreEqual("Not in this version of Raven yet.", way.Reason, way.Id);
            }
        }
        Assert.AreEqual(6, vm.Protections.Count);
        foreach (WayRow protection in vm.Protections)
        {
            NetworkWay? said = reported.Way(protection.Id);
            Assert.IsNotNull(said, protection.Id);
            Assert.AreEqual(said.State, protection.State, protection.Id);
            if (said.State == WayStates.Preview) Assert.IsFalse(protection.HasSwitch, protection.Id);
        }

        Assert.IsFalse(vm.ShowLabBanner, "a release build never shows the banner");
        Assert.AreEqual(string.Empty, vm.LabBannerText);
        Assert.AreEqual(0, vm.FirewallLines.Count);
        // raven-node runs: its listening line gives the port, so the profile's port is not said too (E1).
        Assert.AreEqual("running", reported.Node);
        CollectionAssert.DoesNotContain(vm.ComputerLines.ToList(), Loc.Format("network.thisComputer.port", ("port", MockRaven.DemoPort)));
        CollectionAssert.Contains(vm.ComputerLines.ToList(), Loc.Format("network.thisComputer.listening", ("addr", "0.0.0.0:7420")));
        CollectionAssert.Contains(vm.RelayLines.ToList(), Loc.Get("network.relays.none"));
        CollectionAssert.Contains(vm.RouterLines.ToList(), Loc.Format("network.portMapping.setting", ("setting", Loc.Get("network.value.notDecided"))));
        CollectionAssert.Contains(vm.ProxyLines.ToList(), Loc.Format("network.proxy.setting", ("setting", Loc.Get("network.proxy.auto"))));
        if (reported.Nearby.State == "held")
            CollectionAssert.Contains(vm.NearbyLines.ToList(), Loc.Get("network.nearby.state.held"));
        Assert.AreEqual(Loc.Format("network.carry.mine", ("setting", Loc.Get("network.value.notDecided"))), vm.CarryMineLines[0]);
        Assert.IsFalse(vm.CarryForFriendsOn);
        if (reported.Way("custody")?.State == WayStates.Preview)
            Assert.IsFalse(vm.CarryForFriendsEnabled, "custody is held: carrying for friends can't be turned on");
    }

    /// <summary>Tor: a way of its own in the Tor colour with no switch, and raven-node's state, why and outage history.</summary>
    [TestMethod]
    public async Task Lab_TorShowsRavenNodesStateAndOutageHistory()
    {
        NetworkViewModel vm = await LoadedAsync(Lab());
        WayRow tor = Way(vm, "tor");
        Assert.AreEqual(WayStates.Lab, tor.State);
        Assert.IsFalse(tor.HasSwitch, "Tor runs by itself");
        Assert.AreEqual(Loc.Get("network.way.tor"), tor.Does);
        Assert.AreEqual("ONION_PRODUCTION_ENABLED", tor.Gate);
        CollectionAssert.Contains(tor.LabSwitches.ToList(), "RAVEN_LAB_TOR");
        Assert.AreEqual(PathFamily.Tor, CalmPath.FamilyOfWay(tor.Id));
        Assert.IsNotNull(tor.Tor);
        Assert.AreEqual(new Chip(Loc.Get("network.tor.phase.ready"), Tone.Success), tor.Tor!.Phase);
        Assert.IsTrue(tor.Tor.Ready);
        CollectionAssert.AreEqual(
            new[] { Loc.Format("network.tor.outages", ("text", MockRaven.MockTorOutages)) },
            tor.Tor.Lines.ToList());
    }

    [TestMethod]
    public void Tor_PhasesWhyRetryAndUnknownWords()
    {
        static TorView View(TorState t) => NetworkPresenter.Tor(new NetworkStatus { Tor = t })!;
        Assert.IsNull(NetworkPresenter.Tor(new NetworkStatus()), "no Tor line: nothing said");
        TorView starting = View(new TorState { Phase = "starting", Words = "starting (45 %)", Percent = 45 });
        Assert.AreEqual(Loc.Format("network.tor.phase.starting", ("percent", 45)), starting.Phase.Text);
        Assert.AreEqual("Starting (45 %)", starting.Phase.Text);
        Assert.AreEqual(Tone.Secondary, starting.Phase.Tone);
        Assert.IsFalse(starting.Ready);
        Assert.AreEqual(0, starting.Lines.Count);
        TorView blocked = View(new TorState { Phase = "blocked", Words = "blocked", Why = "bridges needed", Retry = "retrying in 2 min" });
        Assert.AreEqual(Loc.Get("network.tor.phase.blocked"), blocked.Phase.Text);
        Assert.AreEqual(Tone.Warning, blocked.Phase.Tone);
        CollectionAssert.AreEqual(new[] { "Why: bridges needed; retrying in 2 min" }, blocked.Lines.ToList());
        TorView unreachable = View(new TorState
        {
            Phase = "unreachable", Words = "ready, onion service unreachable", Why = "self-check did not reach it",
            Outages = "onion service outages since start: 2",
        });
        Assert.AreEqual(Loc.Get("network.tor.phase.unreachable"), unreachable.Phase.Text);
        Assert.IsTrue(unreachable.Ready);
        Assert.AreEqual(Tone.Warning, unreachable.Phase.Tone);
        CollectionAssert.AreEqual(
            new[] { "Why: self-check did not reach it", "Outage history: onion service outages since start: 2" },
            unreachable.Lines.ToList());
        foreach ((string phase, string key) in new[]
        {
            ("ready", "network.tor.phase.ready"), ("publishing", "network.tor.phase.publishing"),
            ("restarting", "network.tor.phase.restarting"), ("could_not_start", "network.tor.phase.couldNotStart"),
            ("not_built", "network.tor.phase.notBuilt"), ("off", "network.tor.phase.off"),
        })
            Assert.AreEqual(Loc.Get(key), View(new TorState { Phase = phase, Words = phase }).Phase.Text, phase);
        Assert.AreEqual("something new", View(new TorState { Phase = "something_new", Words = "something new" }).Phase.Text);
    }

    [TestMethod]
    public async Task Lab_WaysReadOffOrLab_AndTheBannerNamesTheSwitches()
    {
        NetworkViewModel vm = await LoadedAsync(Lab());

        Assert.IsTrue(vm.ShowLabBanner);
        StringAssert.Contains(vm.LabBannerText, "RAVEN_LAB_TEST_A");
        StringAssert.Contains(vm.LabBannerText, "RAVEN_LAB_CUSTODY_HANDOFF");

        WayRow internet = Way(vm, "internet");
        Assert.AreEqual(WayStates.Off, internet.State);
        Assert.AreEqual(new Chip(Loc.Get("net.wayOff"), Tone.Secondary), internet.StateChip);
        Assert.IsTrue(internet.SwitchEnabled, "an open gate can be switched on");
        Assert.IsFalse(internet.IsOn);
        Assert.IsTrue(internet.Lab, "opened by a lab switch");
        CollectionAssert.Contains(internet.LabSwitches.ToList(), "RAVEN_LAB_TEST_A");
        Assert.AreEqual("INTERNET_DIRECT_PRODUCTION_ENABLED", internet.Gate);

        WayRow hints = Way(vm, "route_hints");
        Assert.AreEqual(WayStates.Lab, hints.State);
        Assert.AreEqual(new Chip(Loc.Get("net.lab"), Tone.Accent), hints.StateChip);
        Assert.IsFalse(hints.HasSwitch, "route hints run by themselves");

        Assert.AreEqual(WayStates.Preview, Way(vm, "websocket").State, "its own switch is not set");
        Assert.AreEqual(WayStates.Preview, Way(vm, "proxy").State);
        Assert.IsFalse(Way(vm, "proxy").SwitchEnabled);
        Assert.AreEqual(WayStates.On, Way(vm, "lan").State, "LAN direct is released: on, not lab");
        Assert.IsTrue(vm.CarryForFriendsEnabled);
        Assert.AreEqual(WayStates.Lab, vm.Protections.Single(p => p.Id == "lock_v2").State);
    }

    [TestMethod]
    public async Task NotOnThisComputer_WhenThereIsNoBluetoothLe()
    {
        MockRaven engine = Lab();
        engine.BluetoothLe = false;
        NetworkViewModel vm = await LoadedAsync(engine);
        WayRow nearby = Way(vm, "nearby");
        Assert.AreEqual(WayStates.NotOnThisOs, nearby.State);
        Assert.AreEqual(new Chip(Loc.Get("net.notOnThisOs"), Tone.Secondary), nearby.StateChip);
        Assert.IsFalse(nearby.SwitchEnabled);

        MockRaven release = Release();
        release.BluetoothLe = false;
        Assert.AreEqual(WayStates.NotOnThisOs, Way(await LoadedAsync(release), "nearby").State, "not on this OS comes before preview");
    }

    [TestMethod]
    public async Task Nearby_SaysItIsInactiveOnWindows_AndNeverClaimsInUse()
    {
        NetworkViewModel vm = await LoadedAsync(Lab());
        Assert.AreEqual(SetOutcome.Changed, await vm.SetSwitchAsync(Way(vm, "nearby"), true));

        WayRow nearby = Way(vm, "nearby");
        Assert.AreEqual(WayStates.Lab, nearby.State);
        Assert.IsTrue(nearby.IsOn);
        Assert.AreEqual(Loc.Get("network.nearby.windows"), nearby.Note);
        CollectionAssert.Contains(vm.NearbyTechnical.ToList(), Loc.Get("network.nearby.windowsLab"));
        Assert.IsFalse(vm.NearbyLines.Any(l => l.Contains("RAVEN_", System.StringComparison.Ordinal) || l.Contains("raven nearby", System.StringComparison.Ordinal)));
        Assert.IsNull(nearby.RunningText, "the radio is on, but Windows nearby stays inactive");
        Assert.AreEqual(vm.NearbyNote, nearby.Note);
        CollectionAssert.Contains(vm.NearbyLines.ToList(), Loc.Get("network.nearby.state.live"));
        CollectionAssert.Contains(vm.NearbyLines.ToList(), Loc.Format("network.nearby.address", ("value", "public")));
    }

    // ------------------------------------------------------------------ switches and errors

    [TestMethod]
    public async Task HeldWay_IsRefusedWithNotAvailableYet_AndNothingChanges()
    {
        var engine = new RecordingEngine(Release());
        NetworkViewModel vm = await LoadedAsync(engine);

        SetOutcome outcome = await vm.SetAsync(NetworkSettings.P2p, JsonValue.Create(true));

        Assert.AreEqual(SetOutcome.NotAvailableYet, outcome);
        Assert.IsNotNull(vm.Error);
        Assert.IsTrue(vm.Error!.IsNotAvailableYet);
        Assert.AreEqual(Loc.Get("error.notAvailableYet"), vm.Error.Message);
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.Error.Detail), "the technical text goes under Details");
        Assert.AreEqual(WayStates.Preview, Way(vm, "p2p").State);
        Assert.IsNull(vm.Restart);
        Assert.IsFalse(vm.Busy);
        Assert.AreEqual(RavenOps.NetworkStatus, engine.Ops.Last(), "the page reads what the engine really has");
    }

    [TestMethod]
    public async Task TurningOff_IsAlwaysAllowed_AndLanForAMeetingEndsByItself()
    {
        var engine = new RecordingEngine(Release());
        NetworkViewModel vm = await LoadedAsync(engine);

        Assert.AreEqual(SetOutcome.Changed, await vm.SetSwitchAsync(Way(vm, "lan"), false));
        Assert.AreEqual(WayStates.Off, Way(vm, "lan").State);
        Assert.IsTrue(Way(vm, "lan").SwitchEnabled, "it can be turned on again");
        Assert.AreEqual(1, vm.LastChanges.Count);
        StringAssert.Contains(vm.LastChanges[0].Effect, "stops sending and receiving");

        Assert.AreEqual(SetOutcome.Changed, await vm.SetLanForAsync("1h"));
        JsonObject sent = engine.ArgsOf(RavenOps.NetworkSet).Last();
        Assert.AreEqual(true, (bool)sent[NetworkSettings.Lan]!);
        Assert.AreEqual("1h", (string)sent[NetworkSettings.LanFor]!);
        Assert.AreEqual(WayStates.On, Way(vm, "lan").State);
        string until = Loc.Get("network.thisComputer.lanUntil").Split("{time}")[0];
        Assert.IsTrue(vm.ComputerLines.Any(l => l.StartsWith(until, System.StringComparison.Ordinal)), string.Join(" | ", vm.ComputerLines));

        // The proxy is held in a release build, but "off" is always allowed (the running
        // background program reads it when it starts again).
        Assert.AreEqual(SetOutcome.RestartNeeded, await vm.SetProxyAsync("off"));
        CollectionAssert.Contains(vm.ProxyLines.ToList(), Loc.Format("network.proxy.setting", ("setting", Loc.Get("network.proxy.off"))));
        Assert.IsNull(vm.Error);
    }

    [TestMethod]
    public async Task HelpFlags_And_HideIp_SayWhatTheyReallyDo()
    {
        NetworkViewModel vm = await LoadedAsync(Release());
        Assert.AreEqual(3, vm.HelpFlags.Count);
        Assert.IsTrue(vm.HelpAutomatic);
        FlagRow store = vm.HelpFlags.Single(f => f.Setting == NetworkSettings.Store);
        Assert.IsTrue(store.IsOn);
        StringAssert.Contains(store.Effect, "changes nothing yet");
        StringAssert.Contains(vm.HelpFlags.Single(f => f.Setting == NetworkSettings.Bridge).Effect, "don't use it");
        Assert.AreEqual(Loc.Get("network.help.bridge.plain"), vm.HelpFlags.Single(f => f.Setting == NetworkSettings.Bridge).Effect);
        Assert.IsNotNull(vm.HelpFlags.Single(f => f.Setting == NetworkSettings.Bridge).Technical);
        Assert.IsTrue(store.Shown, "on: shown, so it can be turned off");
        Assert.IsFalse(vm.HelpFlags.Single(f => f.Setting == NetworkSettings.Relay).Shown, "changes nothing: hidden while off (E1)");
        Assert.IsFalse(vm.ProxyLines.Any(l => l.Contains("raven node proxy", System.StringComparison.Ordinal)));

        Assert.AreEqual(SetOutcome.Changed, await vm.SetFlagAsync(NetworkSettings.Store, false));
        Assert.IsFalse(vm.HelpFlags.Single(f => f.Setting == NetworkSettings.Store).IsOn);
        Assert.IsFalse(vm.HelpFlags.Single(f => f.Setting == NetworkSettings.Store).Shown, "off and doing nothing: hidden");
        Assert.IsFalse(vm.HelpAutomatic, "changing one flag turns AUTO off, as the CLI does");

        Assert.AreEqual(SetOutcome.Changed, await vm.SetFlagAsync(NetworkSettings.HideIp, true));
        Assert.IsTrue(vm.HideIp);
    }

    // ------------------------------------------------------------------ consent

    [TestMethod]
    public async Task Consent_NothingIsSentBeforeTheYes_ThenAgreedTrue()
    {
        var engine = new RecordingEngine(Lab());
        NetworkViewModel vm = await LoadedAsync(engine);

        Assert.AreEqual(SetOutcome.NeedsConsent, await vm.SetFlagAsync(NetworkSettings.CarryForFriends, true));
        ConsentRequest request = vm.PendingConsent!;
        Assert.IsNotNull(request);
        Assert.AreEqual(Loc.Get("network.consent.carryForFriends.title"), request.Topic.Title);
        Assert.IsTrue(request.Topic.EngineNeedsConsent);
        StringAssert.StartsWith(request.EngineText, "Hold sealed messages for your friends while they are offline?");
        Assert.AreEqual(0, engine.ArgsOf(RavenOps.NetworkSet).Count, "nothing is sent before the person says yes");

        // No: nothing is sent, nothing changes.
        vm.DeclineConsent();
        Assert.IsNull(vm.PendingConsent);
        Assert.AreEqual(0, engine.ArgsOf(RavenOps.NetworkSet).Count);
        Assert.IsFalse(vm.CarryForFriendsOn);

        // Yes: the setting goes with agreed: true.
        await vm.SetFlagAsync(NetworkSettings.CarryForFriends, true);
        Assert.AreEqual(SetOutcome.Changed, await vm.AgreeAsync());
        JsonObject sent = engine.ArgsOf(RavenOps.NetworkSet).Single();
        Assert.AreEqual(true, (bool)sent[NetworkSettings.CarryForFriends]!);
        Assert.AreEqual(true, (bool)sent[NetworkSettings.Agreed]!);
        Assert.IsTrue(vm.CarryForFriendsOn);
        CollectionAssert.Contains(vm.CarryForFriendsLines.ToList(),
            Loc.Format("network.carry.held", ("n", 2), ("size", NetworkPresenter.Size(4096))));

        // Turning it off needs no yes.
        Assert.AreEqual(SetOutcome.Changed, await vm.SetFlagAsync(NetworkSettings.CarryForFriends, false));
        Assert.IsFalse(engine.ArgsOf(RavenOps.NetworkSet).Last().ContainsKey(NetworkSettings.Agreed));
        Assert.IsFalse(vm.CarryForFriendsOn);
    }

    [TestMethod]
    public async Task Consent_FriendsCarryMine_FromTheCustodySwitch()
    {
        var engine = new RecordingEngine(Lab());
        NetworkViewModel vm = await LoadedAsync(engine);
        WayRow custody = Way(vm, "custody");
        Assert.AreEqual(NetworkSettings.FriendsCarryMine, custody.Setting);
        Assert.AreEqual(WayStates.Off, custody.State);

        Assert.AreEqual(SetOutcome.NeedsConsent, await vm.SetSwitchAsync(custody, true));
        Assert.AreEqual(Loc.Get("network.consent.friendsCarryMine.title"), vm.PendingConsent!.Topic.Title);
        Assert.IsFalse(vm.PendingConsent.EngineText.Contains("[y/N]"), "the terminal's prompt is not shown");
        StringAssert.StartsWith(vm.PendingConsent.EngineText, "Use friends as mailboxes?");

        Assert.AreEqual(SetOutcome.Changed, await vm.AgreeAsync());
        Assert.AreEqual(WayStates.Lab, Way(vm, "custody").State);
        Assert.IsTrue(Way(vm, "custody").IsOn);
        Assert.AreEqual(Loc.Format("network.carry.mine", ("setting", Loc.Get("network.value.on"))), vm.CarryMineLines[0]);
    }

    [TestMethod]
    public async Task RelayAndBridge_NeedNoSheet_TheirEffectIsShown()
    {
        // The consent rule (API.md): a sheet only where the engine asks for "agreed": true.
        var engine = new RecordingEngine(Lab());
        NetworkViewModel vm = await LoadedAsync(engine);

        StringAssert.Contains(Way(vm, "friend_relay").Does, Loc.Get("network.relayForFriends.effect"));
        Assert.AreEqual(SetOutcome.Changed, await vm.SetSwitchAsync(Way(vm, "friend_relay"), true));
        Assert.IsNull(vm.PendingConsent);
        Assert.IsTrue(Way(vm, "friend_relay").IsOn);

        // The bridge is on by default; off and on again are sent at once, beside its effect.
        Assert.AreEqual(SetOutcome.Changed, await vm.SetFlagAsync(NetworkSettings.Bridge, false));
        Assert.AreEqual(SetOutcome.Changed, await vm.SetFlagAsync(NetworkSettings.Bridge, true));
        Assert.IsNull(vm.PendingConsent);
        FlagRow bridge = vm.HelpFlags.Single(f => f.Setting == NetworkSettings.Bridge);
        Assert.IsTrue(bridge.IsOn);
        Assert.IsFalse(string.IsNullOrWhiteSpace(bridge.Effect));
    }

    [TestMethod]
    public async Task ConsentNeeded_FromTheEngine_OpensTheSheet()
    {
        // The engine refuses without "agreed" (CONSENT_NEEDED) with its own words.
        MockRaven mock = Lab();
        RavenException refused = await Assert.ThrowsExactlyAsync<RavenException>(
            () => mock.NetworkSetAsync(new JsonObject { [NetworkSettings.Handoff] = true }));
        Assert.AreEqual(RavenErrorCodes.ConsentNeeded, refused.Code);
        Assert.AreEqual(Loc.Get("error.consentNeeded"), refused.Message);
        Assert.IsFalse((await mock.NetworkStatusAsync()).Carry.CarryForFriends.Handoff, "nothing was changed");

        // A newer engine asks a yes for a setting this app sends straight away: the sheet opens
        // with the engine's words, and No clears the error.
        var engine = new RecordingEngine(Lab());
        engine.Intercept = (op, args) => op == RavenOps.NetworkSet && !args.ContainsKey(NetworkSettings.Agreed)
            ? RecordingEngine.Failure(RavenErrorCodes.ConsentNeeded, Loc.Get("error.consentNeeded"), "store: pass agreed")
            : null;
        NetworkViewModel vm = await LoadedAsync(engine);
        Assert.AreEqual(SetOutcome.NeedsConsent, await vm.SetFlagAsync(NetworkSettings.Store, false));
        Assert.IsTrue(vm.Error!.IsConsentNeeded);
        Assert.AreEqual(Loc.Get("network.consent.generic.title"), vm.PendingConsent!.Topic.Title);
        vm.DeclineConsent();
        Assert.IsNull(vm.Error);
        Assert.IsNull(vm.PendingConsent);

        Assert.AreEqual(SetOutcome.NeedsConsent, await vm.SetFlagAsync(NetworkSettings.Store, false));
        Assert.AreEqual(SetOutcome.Changed, await vm.AgreeAsync());
        Assert.AreEqual(true, (bool)engine.ArgsOf(RavenOps.NetworkSet).Last()[NetworkSettings.Agreed]!);
        Assert.IsNull(vm.Error);
    }

    // ------------------------------------------------------------------ restart

    [TestMethod]
    public async Task Restart_NotStartedByThisApp_SaysHowToStopItByHand()
    {
        var engine = new RecordingEngine(Lab());
        NetworkViewModel vm = await LoadedAsync(engine);

        Assert.AreEqual(SetOutcome.RestartNeeded, await vm.SetSwitchAsync(Way(vm, "internet"), true));
        Assert.IsNotNull(vm.Restart);
        Assert.IsFalse(vm.Restart!.StartedByApp, "the demo's background program was started earlier");
        Assert.AreEqual(NetworkSettings.Internet, vm.Restart.Changes.Single().Setting);
        Assert.AreEqual("restart", vm.Restart.Changes.Single().Applies);
        Assert.IsTrue(vm.RestartPending);

        WayRow internet = Way(vm, "internet");
        Assert.AreEqual(WayStates.Lab, internet.State);
        Assert.AreEqual(Loc.Get("network.running.no"), internet.RunningText, "not used until the restart");

        // Windows Firewall: the prompt and the Internet direct port.
        Assert.AreEqual(Loc.Get("network.firewall.prompt"), vm.FirewallLines[0]);
        CollectionAssert.Contains(vm.FirewallLines.ToList(), Loc.Format("network.firewall.internet", ("port", "7422")));

        RestartResult result = await vm.RestartNodeAsync();
        Assert.AreEqual(RestartKind.Advice, result.Kind);
        StringAssert.Contains(result.Text, "taskkill /F /PID 4242");
        Assert.IsTrue(vm.RestartPending, "the change still waits");
        Assert.IsNull(vm.Restart);
        Assert.AreEqual(result, vm.LastRestart);
        Assert.IsFalse(engine.Ops.Contains(RavenOps.NodeStart), "nothing is started while the old one runs");
    }

    [TestMethod]
    public async Task Restart_StartedByThisApp_StopsAndStartsIt()
    {
        ManualTimeProvider clock = ManualTimeProvider.StartingAt2026();
        var mock = new MockRaven(clock, demoEvents: false, labNetwork: true);
        await mock.IdentityCreateAsync(null);
        await mock.NodeStartAsync();
        clock.Advance(1300);
        await mock.EventsPollAsync(0); // the background program listens now
        var engine = new RecordingEngine(mock);
        NetworkViewModel vm = await LoadedAsync(engine);

        const string relay = "/ip4/198.51.100.9/tcp/7423/p2p/12D3KooWMockRelay";
        Assert.AreEqual(SetOutcome.RestartNeeded, await vm.SetP2pRelaysAsync(new[] { relay, "  " }));
        JsonObject sent = engine.ArgsOf(RavenOps.NetworkSet).Single();
        Assert.AreEqual(true, (bool)sent[NetworkSettings.P2p]!);
        Assert.AreEqual(relay, (string)sent[NetworkSettings.Relays]!.AsArray().Single()!);
        Assert.IsTrue(vm.Restart!.StartedByApp);
        CollectionAssert.Contains(vm.FirewallLines.ToList(), Loc.Format("network.firewall.p2p", ("port", "7423")));

        // Later: the prompt goes, the note that a restart waits stays.
        vm.DismissRestart();
        Assert.IsNull(vm.Restart);
        Assert.IsTrue(vm.RestartPending);

        RestartResult result = await vm.RestartNodeAsync();
        Assert.AreEqual(RestartKind.Restarted, result.Kind);
        Assert.AreEqual(Loc.Get("network.restart.done"), result.Text);
        Assert.IsNotNull(result.Node);
        Assert.IsFalse(vm.RestartPending);
        var ops = engine.Ops.ToList();
        int stop = ops.LastIndexOf(RavenOps.NodeStop);
        Assert.IsTrue(stop >= 0 && ops.IndexOf(RavenOps.NodeStart) > stop, string.Join(", ", ops));

        // Once it listens again it uses p2p: its addresses and the relays show.
        clock.Advance(1300);
        await mock.EventsPollAsync(0);
        await vm.LoadAsync();
        Assert.IsNull(Way(vm, "p2p").RunningText, "in use: the On chip says it");
        Assert.IsTrue(vm.ComputerLines.Any(l => l.Contains("/tcp/7423/p2p/", System.StringComparison.Ordinal)));
        Assert.IsTrue(vm.RelayLines.Any(l => l.EndsWith(Loc.Format("network.relays.offer", ("name", "Ali")), System.StringComparison.Ordinal)),
            string.Join(" | ", vm.RelayLines));
        CollectionAssert.Contains(vm.RelayLines.ToList(),
            Loc.Format("network.relays.line", ("relay", relay), ("state", "reserved")) + " \u00B7 " + Loc.Get("network.relays.manual"));
        CollectionAssert.AreEqual(new[] { relay }, vm.ManualRelays.ToList());
    }

    [TestMethod]
    public async Task Restart_ALoginServiceKeepsItsBackgroundProgram()
    {
        MockRaven mock = Lab();
        mock.LoginServiceRunsNode = true;
        var engine = new RecordingEngine(mock);
        NetworkViewModel vm = await LoadedAsync(engine);
        Assert.AreEqual(SetOutcome.RestartNeeded, await vm.SetSwitchAsync(Way(vm, "port_mapping"), true));

        RestartResult result = await vm.RestartNodeAsync();
        Assert.AreEqual(RestartKind.Managed, result.Kind);
        Assert.AreEqual(Loc.Get("network.restart.managed"), result.Text);
        Assert.IsTrue(result.Node!.Running, "left running");
        Assert.IsFalse(engine.Ops.Contains(RavenOps.NodeStart));
    }

    [TestMethod]
    public async Task NoRestartPrompt_WhenNothingRuns()
    {
        var mock = new MockRaven(ManualTimeProvider.StartingAt2026(), demoEvents: false, labNetwork: true);
        await mock.IdentityCreateAsync(null); // set up, background program not started
        NetworkViewModel vm = await LoadedAsync(mock);
        Assert.AreEqual(SetOutcome.Changed, await vm.SetSwitchAsync(Way(vm, "internet"), true));
        Assert.IsNull(vm.Restart, "it reads the setting when it starts");
        Assert.IsFalse(vm.RestartPending);
        CollectionAssert.Contains(vm.ComputerLines.ToList(), Loc.Get("network.thisComputer.notListening"));
    }

    // ------------------------------------------------------------------ newer engines

    [TestMethod]
    public async Task UnknownStates_AndOddBuilds_StayHonest()
    {
        // A release build never shows the banner, even if it listed a switch.
        var engine = new StubEngine()
            .Answer(RavenOps.NetworkStatus, """
                {"build":"release","lab_switches":["RAVEN_LAB_TEST_A"],
                 "carriers":[{"id":"teleport","name":"Teleport","state":"warming_up","reason":"Soon.","can_set":true}],
                 "protections":[]}
                """)
            .Answer(RavenOps.ContactsList, """{"contacts":[]}""");
        NetworkViewModel vm = await LoadedAsync(engine);
        Assert.IsFalse(vm.ShowLabBanner);
        WayRow teleport = vm.Ways.Single();
        Assert.AreEqual("warming_up", teleport.StateChip.Text);
        Assert.AreEqual(string.Empty, teleport.Does);
        Assert.IsNull(teleport.Setting, "no switch for a way this app doesn't know");
    }

    [TestMethod]
    public async Task LoadFailure_ShowsTheEnginesWords()
    {
        var engine = new StubEngine().Fail(RavenOps.NetworkStatus, RavenErrorCodes.ProfileLocked, Loc.Get("error.profileLocked"), "locked");
        var vm = new NetworkViewModel(engine);
        await vm.LoadAsync();
        Assert.AreEqual(RavenErrorCodes.ProfileLocked, vm.Error!.Code);
        Assert.AreEqual(Loc.Get("error.profileLocked"), vm.Error.Message);
        Assert.AreEqual(0, vm.Ways.Count);
        Assert.IsFalse(vm.Busy);
    }
}
#endif
