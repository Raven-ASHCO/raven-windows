#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Mock;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Core.ViewModels;

namespace Raven.Windows.Tests;

/// <summary>
/// The other new screens on MockRaven: Check network (with and without the router), a
/// friend's routes and mail friends, Security notices, Lock v2 (lock now, the passphrase
/// errors, the wait) and Updates (read-only, installing stays in the terminal).
/// </summary>
[TestClass]
public sealed class NetworkScreensTests
{
    private static MockRaven Release() => new(ManualTimeProvider.StartingAt2026(), startReady: true, demoEvents: false);

    private static MockRaven Lab(ManualTimeProvider? clock = null)
        => new(clock ?? ManualTimeProvider.StartingAt2026(), startReady: true, demoEvents: false, labNetwork: true);

    private static async Task<Contact> ContactAsync(IRavenEngine engine, string address)
        => (await engine.ContactsListAsync()).Single(c => c.Address == address);

    // ------------------------------------------------------------------ check network

    [TestMethod]
    public async Task Check_ShowsTheSummary_AndAsksTheRouterOnlyOnRequest()
    {
        var engine = new RecordingEngine(Release());
        var vm = new NetworkCheckViewModel(engine);

        await vm.RunAsync();
        Assert.IsNull(vm.Error);
        CollectionAssert.Contains(vm.Summary.Select(l => l.Title).ToList(), "LAN: on");
        Assert.AreEqual("All set: Raven can send and receive messages on this computer.", vm.NextStep);
        CheckLine receiving = vm.Lines.First();
        Assert.AreEqual("Raven receives messages on this network.", receiving.Title);
        Assert.IsNull(receiving.Words, "the doctor's words are under Technical details only (E1)");
        Assert.IsFalse(vm.Lines.Any(l => l.Title.Contains(':', System.StringComparison.Ordinal) && l.Title.StartsWith("policy", System.StringComparison.Ordinal)),
            "a line without a plain sentence shows only under Technical details");
        CollectionAssert.Contains(vm.Technical.ToList(), "receiving: yes");
        Assert.IsTrue(vm.Technical.Any(l => l.StartsWith("policy: ", System.StringComparison.Ordinal)));
        Assert.IsTrue(vm.Technical.Any(l => l.Contains("ash send", System.StringComparison.Ordinal)), "the CLI's next step");
        foreach (string command in new[] { "`", "ash ", "raven ", "Ctrl-Z" })
        {
            Assert.IsFalse(vm.NextStep.Contains(command, System.StringComparison.Ordinal), command);
            Assert.IsFalse(vm.Summary.Any(l => l.Title.Contains(command, System.StringComparison.Ordinal)), command);
        }
        Assert.IsFalse(vm.Summary.Any(l => l.Title.StartsWith("next step", System.StringComparison.Ordinal)), "the next step is said once");
        Assert.IsNull(vm.RouterLines, "the router isn't asked unless the person asks");
        Assert.AreEqual(false, (bool)engine.ArgsOf(RavenOps.NetworkCheck).Single()["router"]!);

        await vm.RunAsync(router: true);
        Assert.IsNotNull(vm.RouterLines);
        CollectionAssert.Contains(vm.RouterLines!.Select(l => l.Title).ToList(), "No UPnP router answered.");
        Assert.AreEqual(true, (bool)engine.ArgsOf(RavenOps.NetworkCheck).Last()["router"]!);
        Assert.IsFalse(vm.Busy);
        Assert.IsFalse(vm.AskingRouter);

        await vm.RunAsync();
        Assert.IsNotNull(vm.RouterLines, "the router's last answer stays on screen");
    }

    [TestMethod]
    public async Task Check_Failure_ShowsTheEnginesWords()
    {
        var engine = new StubEngine().Fail(RavenOps.NetworkCheck, RavenErrorCodes.NotInitialized, Loc.Get("error.notInitialized"), "profile is new");
        var vm = new NetworkCheckViewModel(engine);
        await vm.RunAsync();
        Assert.AreEqual(RavenErrorCodes.NotInitialized, vm.Error!.Code);
        Assert.AreEqual(Loc.Get("error.notInitialized"), vm.Error.Message);
        Assert.AreEqual(0, vm.Summary.Count);
    }

    // ------------------------------------------------------------------ a friend's routes

    [TestMethod]
    public async Task Routes_ShowSavedRoutes_HintsAndWhoCarriesMail()
    {
        var clock = ManualTimeProvider.StartingAt2026();
        MockRaven engine = new(clock, startReady: true, demoEvents: false);
        var vm = new ContactRoutesViewModel(engine, await ContactAsync(engine, MockRaven.AliAddress), () => clock.NowMs);
        await vm.LoadAsync();
        Assert.IsNull(vm.Error);

        Assert.AreEqual("lan", vm.RouteLines.First().Kind);
        Assert.AreEqual(new Chip(Loc.Get("net.on"), Tone.Success), vm.RouteLines.First().StateChip);
        Assert.IsTrue(vm.RouteLines.First().CanRemove);
        RouteLine nearby = vm.RouteLines.Last();
        Assert.AreEqual("nearby", nearby.Kind);
        Assert.AreEqual(Loc.Get("net.preview"), nearby.StateChip.Text);
        Assert.IsFalse(nearby.CanRemove, "nearby comes with verifying");

        Assert.AreEqual(Loc.Format("network.routes.hints.state", ("state", Loc.Get("net.preview"))), vm.HintsText);
        CollectionAssert.AreEqual(new[] { Loc.Format("network.routes.hints.none", ("name", "Ali")) }, vm.LearnedLines.ToList());
        CollectionAssert.Contains(vm.CarrierLines.ToList(), Loc.Format("network.routes.noCarriers", ("name", "Ali")));
        // How long a message could wait: the terminal app's rule (carriers.away), not the coverage
        // class's words; "nobody can carry it" adds no line (E1).
        long until = vm.Routes!.Carriers.Away!.UntilMs!.Value;
        CollectionAssert.Contains(vm.CarrierLines.ToList(),
            Loc.Format("network.routes.away", ("name", "Ali"), ("time", RelativeTime.Deadline(until, clock.NowMs))));
        Assert.IsFalse(vm.CarrierLines.Any(l => l.Contains("nobody can carry it", StringComparison.Ordinal)));

        // Only verified friends other than Ali can carry Ali's mail (Rena isn't verified).
        CollectionAssert.AreEqual(new[] { "Mina" }, vm.CustodianChoices.Select(c => c.Name).ToList());
        Assert.IsFalse(vm.CustodianChoices.Single().Selected);
        Assert.IsFalse(vm.HideIp);
    }

    [TestMethod]
    public async Task Routes_Change_RouteMailFriendsAndHideIp()
    {
        MockRaven engine = Release();
        var vm = new ContactRoutesViewModel(engine, await ContactAsync(engine, MockRaven.AliAddress));
        await vm.LoadAsync();

        Assert.IsTrue(await vm.SaveRoutesAsync("203.0.113.5:7422", null, null));
        Assert.IsTrue(vm.Saved);
        Assert.AreEqual("203.0.113.5:7422", vm.InternetRoute);
        Assert.IsTrue(vm.RouteLines.Any(r => r.Kind == "internet" && r.Value == "203.0.113.5:7422"));

        Assert.IsTrue(await vm.SaveRoutesAsync(null, "12D3KooWMockPeer", new[] { "/ip4/198.51.100.4/tcp/7423/p2p/12D3KooWRelay", " " }));
        RouteLine p2p = vm.RouteLines.Single(r => r.Kind == "p2p");
        Assert.AreEqual(Loc.Format("network.routes.via", ("relays", "/ip4/198.51.100.4/tcp/7423/p2p/12D3KooWRelay")), p2p.Via);
        CollectionAssert.AreEqual(new[] { "/ip4/198.51.100.4/tcp/7423/p2p/12D3KooWRelay" }, vm.P2pVia.ToList());

        // A bad route: the engine's words, nothing saved.
        Assert.IsFalse(await vm.SaveRoutesAsync("no-port-here", null, null));
        Assert.AreEqual(RavenErrorCodes.BadRequest, vm.Error!.Code);
        Assert.IsFalse(vm.Saved);
        Assert.AreEqual("203.0.113.5:7422", vm.InternetRoute);

        Assert.IsTrue(await vm.SetMailFriendsAsync(new[] { MockRaven.MinaAddress }));
        Assert.IsNull(vm.Error);
        Assert.IsTrue(vm.CustodianChoices.Single().Selected);
        CollectionAssert.Contains(vm.CarrierLines.ToList(), Loc.Format("network.routes.listed", ("name", "Ali"), ("friends", "Mina")));

        Assert.IsTrue(await vm.SetHideIpAsync(true));
        Assert.IsTrue(vm.HideIp);

        Assert.IsTrue(await vm.RemoveRouteAsync("internet"));
        Assert.IsFalse(vm.RouteLines.Any(r => r.Kind == "internet"));
        Assert.AreEqual(string.Empty, vm.InternetRoute);

        Assert.IsTrue(await vm.SetMailFriendsAsync(new List<string>()));
        Assert.IsFalse(vm.CustodianChoices.Single().Selected, "an empty list clears them");
    }

    [TestMethod]
    public async Task Routes_InALabBuild_ShowWhatTheFriendTold()
    {
        var clock = ManualTimeProvider.StartingAt2026();
        MockRaven engine = Lab(clock);
        var vm = new ContactRoutesViewModel(engine, await ContactAsync(engine, MockRaven.AliAddress), () => clock.NowMs);
        await vm.LoadAsync();
        Assert.AreEqual(Loc.Format("network.routes.hints.state", ("state", Loc.Get("net.lab"))), vm.HintsText);
        long expires = vm.Routes!.Learned!.ExpiresAtMs;
        string until = expires > 0 ? RelativeTime.Deadline(expires, clock.NowMs) : "\u2013";
        Assert.AreEqual(Loc.Format("network.routes.hints.counts", ("relays", 1), ("direct", 1), ("mail", 1), ("time", until)), vm.LearnedLines[0]);
        // Kept for 7 days: "kept until" has its date (E1 round 4).
        StringAssert.Contains(vm.LearnedLines[0], " Oct ");
        CollectionAssert.Contains(vm.LearnedLines.ToList(),
            Loc.Format("network.routes.peerId", ("id", "12D3KooWMockAliPeer")) + " \u00B7 " + Loc.Get("network.routes.peerId.proven"));
        CollectionAssert.Contains(vm.CarrierLines.ToList(),
            Loc.Format("network.routes.told", ("name", "Ali"), ("friends", Loc.Format("network.routes.takesHandoffs", ("friend", "Mina")))));
        Assert.AreEqual(Loc.Get("net.lab"), vm.RouteLines.Last().StateChip.Text, "nearby is lab here");
    }

    [TestMethod]
    public async Task Routes_NeverAskForTheFriendsWords()
    {
        var engine = new RecordingEngine(Release());
        var vm = new ContactRoutesViewModel(engine, await ContactAsync(engine, MockRaven.RenaAddress));
        await vm.LoadAsync();
        Assert.IsFalse(engine.Ops.Any(op => op.StartsWith("verify.", System.StringComparison.Ordinal) || op == RavenOps.IdentityMe));
        Assert.IsFalse(vm.RouteLines.Any(r => r.Kind == "nearby"), "nearby is offered only to a verified friend");
    }

    // ------------------------------------------------------------------ security, Lock, updates

    [TestMethod]
    public async Task Security_ReleaseBuild_SaysWhatIsNotInThisVersion()
    {
        var vm = new SecurityViewModel(Release());
        await vm.LoadAsync();
        Assert.IsNull(vm.SecurityError);
        Assert.AreEqual(Loc.Get("security.preview"), vm.SecurityText);
        Assert.AreEqual(0, vm.Notices.Count);
        Assert.AreEqual(0, vm.NodeLines.Count);
        Assert.IsNull(vm.SecurityLabText);

        Assert.AreEqual(Loc.Get("updates.notAvailable"), vm.UpdatesText);
        StringAssert.Contains(vm.InstallText, "raven update install");
        Assert.AreEqual("cli_only", vm.Updates!.Install);

        Assert.AreEqual(Loc.Get("lock.notAvailable"), vm.Lock.StatusText);
        Assert.IsFalse(vm.Lock.CanLockNow);
        Assert.IsFalse(await vm.Lock.LockNowAsync());
        Assert.IsTrue(vm.Lock.Error!.IsNotAvailableYet);
    }

    [TestMethod]
    public async Task Security_LabBuild_ShowsNoticesCountsAndUpdates()
    {
        var vm = new SecurityViewModel(Lab());
        await vm.LoadAsync();
        Assert.AreEqual(Loc.Get("security.kept.live"), vm.SecurityText);
        Assert.AreEqual(Loc.Get("network.labOnly"), vm.SecurityLabText);
        NoticeView notice = vm.Notices.Single();
        Assert.AreEqual(Loc.Get("security.warning"), notice.LevelText);
        Assert.AreEqual(Tone.Warning, notice.Tone);
        Assert.AreEqual("Someone who isn't your contact keeps trying to connect", notice.Headline);
        CollectionAssert.Contains(notice.Details.ToList(), "How serious: low");
        Assert.AreEqual(Loc.Format("security.counted", ("n", 2)), vm.CountedText);
        Assert.AreEqual(5, vm.NodeLines.Count);
        Assert.AreEqual(Loc.Format("security.node.strangers", ("n", 3)), vm.NodeLines[0]);

        Assert.AreEqual("No update is waiting.", vm.UpdatesText);
        Assert.AreEqual(Loc.Get("network.labOnly"), vm.UpdatesLabText);
        Assert.AreEqual(2, vm.UpdatesLines.Count);

        Assert.AreEqual(Loc.Get("lock.ready"), vm.Lock.StatusText);
        Assert.AreEqual(Loc.Get("network.labOnly"), vm.Lock.LabText);
        Assert.IsTrue(vm.Lock.CanLockNow);
    }

    [TestMethod]
    public async Task Lock_LockNow_ThenThePassphraseErrors_ThenUnlock()
    {
        ManualTimeProvider clock = ManualTimeProvider.StartingAt2026();
        var vm = new LockViewModel(Lab(clock));
        await vm.LoadAsync();
        Assert.IsTrue(vm.AsksPassphrase);

        Assert.IsTrue(await vm.LockNowAsync());
        Assert.IsTrue(vm.IsLocked);
        Assert.AreEqual(Loc.Get("lock.isLocked"), vm.StatusText);
        Assert.IsFalse(vm.CanLockNow);

        Assert.IsFalse(await vm.UnlockAsync(null));
        Assert.AreEqual(RavenErrorCodes.PassphraseNeeded, vm.Error!.Code);
        Assert.AreEqual(Loc.Get("error.passphraseNeeded"), vm.Error.Message);

        Assert.IsFalse(await vm.UnlockAsync("not it"));
        Assert.AreEqual(RavenErrorCodes.PassphraseWrong, vm.Error!.Code);
        Assert.IsTrue(vm.IsLocked);

        Assert.IsTrue(await vm.UnlockAsync(MockRaven.LabPassphrase));
        Assert.IsNull(vm.Error);
        Assert.IsFalse(vm.IsLocked);
        Assert.AreEqual("Raven's background program has its keys again.", vm.Note);
    }

    [TestMethod]
    public async Task Lock_TooManyWrongPassphrases_MeansWait()
    {
        ManualTimeProvider clock = ManualTimeProvider.StartingAt2026();
        var vm = new LockViewModel(Lab(clock));
        await vm.LoadAsync();
        await vm.LockNowAsync();
        await vm.UnlockAsync("one");
        await vm.UnlockAsync("two");
        Assert.IsFalse(await vm.UnlockAsync("three"));
        Assert.AreEqual(RavenErrorCodes.UnlockWait, vm.Error!.Code);
        Assert.AreEqual(Loc.Get("error.unlockWait"), vm.Error.Message);

        Assert.IsFalse(await vm.UnlockAsync(MockRaven.LabPassphrase), "even the right one waits");
        Assert.AreEqual(RavenErrorCodes.UnlockWait, vm.Error!.Code);

        clock.Advance(31_000);
        Assert.IsTrue(await vm.UnlockAsync(MockRaven.LabPassphrase));
    }

    [TestMethod]
    public async Task Lock_NeedsARunningBackgroundProgram()
    {
        var mock = new MockRaven(ManualTimeProvider.StartingAt2026(), demoEvents: false, labNetwork: true);
        await mock.IdentityCreateAsync(null);
        var vm = new LockViewModel(mock);
        await vm.LoadAsync();
        Assert.IsTrue(vm.State!.Available);
        Assert.IsFalse(vm.State.Supported);
        Assert.AreEqual(Loc.Get("lock.notSupported"), vm.StatusText);
        Assert.IsFalse(vm.CanLockNow);

        Assert.IsFalse(await vm.LockNowAsync());
        Assert.IsTrue(vm.Error!.IsNotAvailableYet);
        StringAssert.Contains(vm.Error.Detail, "raven lock");
    }
}
#endif
