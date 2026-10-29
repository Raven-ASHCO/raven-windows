using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Mock;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Core.ViewModels;

namespace Raven.Windows.Tests;

/// <summary>
/// The third round of E1 fixes in the Windows app: the smallest window and a header that fits it,
/// one wait for an away friend, the address in use in Routes, plain Security and Network lines,
/// the path chip while a friend is away, and late deliveries.
/// </summary>
[TestClass]
public sealed class E1RoundThreeTests
{
    private const long Now = 1_790_000_000_000;
    private const string Friend = "rvn1qwinc000000000000000000000000000000000";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = false };

    /// <summary>
    /// win-c measured 700 x 600 physical pixels on a 125 % screen: the 560 x 480 DIP minimum
    /// (tokens.json layout.minWindow), scaled.
    /// </summary>
    [TestMethod]
    public void SmallestWindow_Is560By480Dips()
    {
        Assert.AreEqual((560, 480), WindowPlacement.MinimumPixels(1.0));
        Assert.AreEqual((700, 600), WindowPlacement.MinimumPixels(1.25));
        Assert.AreEqual((840, 720), WindowPlacement.MinimumPixels(1.5));
        using JsonDocument tokens = JsonDocument.Parse(File.ReadAllText(RepoPaths.DesignFile("tokens.json")));
        JsonElement min = tokens.RootElement.GetProperty("layout").GetProperty("minWindow");
        Assert.AreEqual(min.GetProperty("width").GetInt32(), WindowPlacement.MinWidthDip);
        Assert.AreEqual(min.GetProperty("height").GetInt32(), WindowPlacement.MinHeightDip);
    }

    /// <summary>At narrow widths the status text trims first, then the path pill goes; Routes always shows.</summary>
    [TestMethod]
    public void Header_DropsThePathPillBeforeTheStatusText()
    {
        Assert.IsTrue(HeaderFit.ShowsPathPill(room: 200, pillWidth: 70));
        Assert.IsTrue(HeaderFit.ShowsPathPill(room: 70 + HeaderFit.PillGap + HeaderFit.MinStatusWidth, pillWidth: 70));
        Assert.IsFalse(HeaderFit.ShowsPathPill(room: 70 + HeaderFit.PillGap + HeaderFit.MinStatusWidth - 1, pillWidth: 70));
        Assert.IsFalse(HeaderFit.ShowsPathPill(room: 20, pillWidth: 70));
    }

    /// <summary>14. No "LAN direct" in the header while messages to the friend wait.</summary>
    [TestMethod]
    public void Header_ShowsNoPathWhileTheFriendIsAway()
    {
        var c = new Contact { Address = Friend, Name = "win-c", Verified = true, Presence = Presences.InTouch };
        Assert.AreEqual("LAN direct", ContactPresenter.HeaderPath(c, "LAN direct"));
        c.Presence = Presences.Waiting;
        Assert.IsNull(ContactPresenter.HeaderPath(c, "LAN direct"));
        c.Presence = Presences.None;
        c.Waiting = new WaitingInfo { Count = 1, UntilMs = Now };
        Assert.IsNull(ContactPresenter.HeaderPath(c, "LAN direct"));
        Assert.IsNull(ContactPresenter.HeaderPath(new Contact { Address = Friend, Name = "win-c" }, null));
    }

    /// <summary>15. A late delivery says when it came; a quick one says "Delivered".</summary>
    [TestMethod]
    public void Tick_SaysWhenALateDeliveryCame()
    {
        var m = new Message { Id = "1", Address = Friend, State = MessageStates.Delivered, AtMs = Now - 3_600_000 };
        Assert.AreEqual("✓✓ " + Loc.Get("tick.delivered"), TickPresenter.For(m, "win-c", Now).Text);
        m.DeliveredAtMs = Now - 120_000;
        Assert.AreEqual("✓✓ " + Loc.Format("tick.deliveredAt", ("time", RelativeTime.BubbleTime(Now - 120_000, Now))),
            TickPresenter.For(m, "win-c", Now).Text);
        Message? read = JsonSerializer.Deserialize<Message>(
            "{\"id\":\"1\",\"address\":\"a\",\"direction\":\"out\",\"text\":\"t\",\"at_ms\":1,\"state\":\"delivered\",\"delivered_at_ms\":99}", Json);
        Assert.AreEqual(99L, read!.DeliveredAtMs);
        RavenEvent? receipt = JsonSerializer.Deserialize<RavenEvent>(
            "{\"seq\":1,\"kind\":\"receipt\",\"at_ms\":5,\"id\":\"1\",\"path\":\"LAN direct\",\"ms\":900,\"delivered_at_ms\":5}", Json);
        Assert.AreEqual(5L, receipt!.DeliveredAtMs);
    }

    /// <summary>
    /// 2. How long a message could wait comes from the terminal app's rule (carriers.away), not the
    /// coverage class's "up to 7 days"; friends carrying it come as a second line, only when one can.
    /// </summary>
    [TestMethod]
    public void Routes_SayTheTerminalAppsWait()
    {
        long until = Now + 7 * 24 * 3_600_000L;
        var direct = new ContactCarriers
        {
            Coverage = new Coverage { Kind = "direct", Name = "Raven keeps it until you're both online", Until = "for up to 7 days" },
            Away = new AwayWait { UntilMs = until },
        };
        CollectionAssert.AreEqual(new[] { Loc.Format("network.routes.away", ("name", "mac-a"), ("time", RelativeTime.Deadline(until, Now))) },
            ContactRoutesViewModel.AwayLines(direct, "mac-a", Now).ToList());
        // A week away has its date (E1 round 4: "until Sun 17:15" read on a Sunday looked like now).
        Assert.AreNotEqual(RelativeTime.Clock(until), RelativeTime.Deadline(until, Now));
        var meet = new ContactCarriers { Away = new AwayWait { MeetFirst = true } };
        CollectionAssert.AreEqual(new[] { Loc.Format("network.routes.awayMeetFirst", ("name", "mac-a")) },
            ContactRoutesViewModel.AwayLines(meet, "mac-a", Now).ToList());
        var shared = new ContactCarriers
        {
            Coverage = new Coverage { Kind = "mutual_friend", Name = "a mail friend you share", Until = "up to 7 days (letters) or a day (plain)" },
            Away = new AwayWait { UntilMs = until },
        };
        var lines = ContactRoutesViewModel.AwayLines(shared, "mac-a", Now);
        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual(Loc.Format("network.routes.coverage", ("coverage", "a mail friend you share"),
            ("until", "up to 7 days (letters) or a day (plain)")), lines[1]);
    }

    /// <summary>
    /// Three-machine test: Routes said "meet first" for a friend Raven had never connected with, where
    /// the terminal app says a first message needs you both online. That friend gets the terminal
    /// app's sentence (first_message, next to meet_first); "meet first" is only for an ended connection.
    /// </summary>
    [TestMethod]
    public async Task Routes_TellAFirstMessageFromAnEndedConnection()
    {
        var first = JsonSerializer.Deserialize<ContactCarriers>("{\"away\":{\"meet_first\":true,\"first_message\":true}}", Json)!;
        CollectionAssert.AreEqual(new[] { "A first message needs you and win-c both online (Raven hasn't connected with win-c yet)." },
            ContactRoutesViewModel.AwayLines(first, "win-c", Now).ToList());
        var ended = JsonSerializer.Deserialize<ContactCarriers>("{\"away\":{\"meet_first\":true}}", Json)!;
        CollectionAssert.AreEqual(new[] { "To write to win-c, meet first: Raven can't hold new messages for them now." },
            ContactRoutesViewModel.AwayLines(ended, "win-c", Now).ToList());

        // The demo's friend who isn't verified was never connected with: the first-message rule.
        var engine = new MockRaven(ManualTimeProvider.StartingAt2026(), startReady: true, demoEvents: false);
        ContactRoutes rena = await engine.ContactsRoutesAsync(MockRaven.RenaAddress);
        Assert.IsTrue(rena.Carriers.Away?.FirstMessage == true);
        Assert.IsTrue(rena.Carriers.Away?.MeetFirst == true);
        Assert.AreEqual(Loc.Format("network.routes.awayFirstMessage", ("name", "Rena")),
            ContactRoutesViewModel.AwayLines(rena.Carriers, "Rena", Now).First());
    }

    /// <summary>5. Routes names the address in use, says plainly the saved one is old, and saves the new one.</summary>
    [TestMethod]
    public async Task Routes_ShowTheAddressInUseAndSaveIt()
    {
        string friend = $"{{\"address\":\"{Friend}\",\"name\":\"win-c\",\"short_address\":\"rvn1qwinc\",\"verified\":true,\"presence\":\"in_touch\",\"lan\":\"192.168.0.24:17420\"}}";
        string routes(string saved, bool stale) => $"{{\"address\":\"{Friend}\",\"verified\":true,\"hide_ip\":false," +
            $"\"routes\":[{{\"kind\":\"lan\",\"label\":\"Same network\",\"value\":\"{saved}\",\"state\":\"on\"}}]," +
            $"\"lan_now\":{{\"addr\":\"192.168.0.24:17420\",\"at_ms\":{Now - 300_000},\"saved_stale\":{(stale ? "true" : "false")}}}," +
            "\"carriers\":{\"hints\":\"lab\",\"told\":[],\"told_unknown\":0,\"listed\":[],\"receipts_stranded\":false}}";
        StubEngine engine = new StubEngine()
            .Answer(RavenOps.ContactsList, $"{{\"contacts\":[{friend}]}}")
            .Answer(RavenOps.ContactsRoutes, routes("192.168.1.146:17420", stale: true))
            .Answer(RavenOps.ContactsSetAddress, $"{{\"contact\":{friend}}}");
        var vm = new ContactRoutesViewModel(engine, new Contact { Address = Friend, Name = "win-c", Verified = true }, () => Now);
        await vm.LoadAsync();
        Assert.IsNull(vm.Error);
        Assert.AreEqual(Loc.Format("network.routes.lanNow", ("name", "win-c"), ("addr", "192.168.0.24:17420"),
            ("when", RelativeTime.Ago(Now - 300_000, Now))), vm.LanNowText);
        Assert.AreEqual(Loc.Format("network.routes.lanStale", ("addr", "192.168.1.146:17420"), ("name", "win-c")), vm.LanStaleText);
        Assert.IsTrue(vm.CanUseLanNow);

        engine.Answer(RavenOps.ContactsRoutes, routes("192.168.0.24:17420", stale: false));
        Assert.IsTrue(await vm.UseLanNowAsync());
        Assert.IsTrue(vm.Saved);
        Assert.IsNull(vm.LanStaleText);
        Assert.IsFalse(vm.CanUseLanNow);
        Assert.AreEqual("192.168.0.24:17420", vm.RouteLines.Single(r => r.Kind == "lan").Value);
    }

    /// <summary>6. The Network page lists the peer-to-peer addresses friends could use; this computer's own only under Technical details.</summary>
    [TestMethod]
    public void Network_KeepsLocalOnlyPeerToPeerAddressesTechnical()
    {
        string[] all =
        {
            "/ip4/127.0.0.1/tcp/17423/p2p/12D3", "/ip4/192.168.0.16/tcp/17423/p2p/12D3", "/ip6/::1/tcp/17423/p2p/12D3",
            "/ip6/fe80::1%12/tcp/17423", "/ip4/169.254.3.4/udp/17423/quic-v1", "/ip6/2001:db8::7/tcp/17423",
        };
        CollectionAssert.AreEqual(new[] { "/ip4/192.168.0.16/tcp/17423/p2p/12D3", "/ip6/2001:db8::7/tcp/17423" },
            NetworkPresenter.P2pAddressesShown(all));
        Assert.IsFalse(Loc.Get("network.way.randomPort").Contains("7420", StringComparison.Ordinal));
    }

    /// <summary>7. "Open alerts" is said nowhere: while nothing needs you they are the smaller notices, only counted.</summary>
    [TestMethod]
    public void Security_NeverSaysAlertsWhenNothingNeedsYou()
    {
        Assert.AreEqual(Loc.Format("security.counted", ("n", 1)), SecurityViewModel.OpenNoticesLine(false, 1));
        Assert.AreEqual(Loc.Format("security.node.alerts", ("n", 2)), SecurityViewModel.OpenNoticesLine(true, 2));
        Assert.IsFalse(SecurityViewModel.OpenNoticesLine(true, 2).Contains("alert", StringComparison.OrdinalIgnoreCase));
    }
}
