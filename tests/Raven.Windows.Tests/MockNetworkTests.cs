#if DEBUG
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Mock;

namespace Raven.Windows.Tests;

/// <summary>
/// The mock's network answers (API.md, "Network" and "Mocks"): a release build's, with LAN
/// direct on, every other way held, a held way never turned on, and the help-friends flags,
/// LAN and hide IP kept as set.
/// </summary>
[TestClass]
public sealed class MockNetworkTests
{
    private static MockRaven Ready() => new(ManualTimeProvider.StartingAt2026(), startReady: true, demoEvents: false);

    [TestMethod]
    public async Task Network_AnswersLikeARelease()
    {
        MockRaven engine = Ready();
        NetworkStatus net = await engine.NetworkStatusAsync();
        Assert.AreEqual(WayStates.On, net.Way("lan")?.State);
        foreach (NetworkWay w in net.Carriers.Concat(net.Protections).Where(w => w.Id != "lan"))
        {
            Assert.AreEqual(WayStates.Preview, w.State, w.Id);
            Assert.IsFalse(w.Lab, w.Id);
        }
        Assert.AreEqual(0, net.LabSwitches.Count);
        StringAssert.Contains(net.Help.Effects.Store, "changes nothing yet");

        RavenException held = await Assert.ThrowsExactlyAsync<RavenException>(
            () => engine.NetworkSetAsync(new JsonObject { ["p2p"] = true, ["store"] = false }));
        Assert.AreEqual(RavenErrorCodes.NotAvailableYet, held.Code);
        Assert.IsTrue((await engine.NetworkStatusAsync()).Help.Store, "a refusal changed nothing");

        NetworkSetResult set = await engine.NetworkSetAsync(new JsonObject { ["store"] = false, ["lan"] = false });
        Assert.AreEqual(2, set.Changes.Count);
        Assert.IsFalse(set.Status.Help.Store);
        Assert.IsFalse(set.Status.Help.Automatic);
        Assert.AreEqual(WayStates.Off, set.Status.Way("lan")?.State);

        NetworkCheck check = await engine.NetworkCheckAsync(router: true);
        Assert.IsTrue(check.Summary.Any(r => r.Key == "lan"));
        Assert.IsNotNull(check.Router);

        Assert.IsFalse((await engine.SecurityStatusAsync()).Available);
        Assert.IsFalse((await engine.LockStateAsync()).Available);
        RavenException lockNow = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.LockNowAsync());
        Assert.AreEqual(RavenErrorCodes.NotAvailableYet, lockNow.Code);
        Assert.AreEqual("cli_only", (await engine.UpdatesStatusAsync()).Install);
    }

    [TestMethod]
    public async Task ContactRoutes_AndMailFriends()
    {
        MockRaven engine = Ready();
        ContactRoutes ali = await engine.ContactsRoutesAsync(MockRaven.AliAddress);
        Assert.AreEqual("lan", ali.Routes.First().Kind);
        Assert.AreEqual("nearby", ali.Routes.Last().Kind);

        SetRoutesResult set = await engine.ContactsSetRoutesAsync(MockRaven.AliAddress, new JsonObject
        {
            ["internet"] = "203.0.113.5:7422",
            ["custodians"] = new JsonArray(JsonValue.Create(MockRaven.MinaAddress)),
        });
        Assert.IsTrue(set.Routes.Routes.Any(r => r.Kind == "internet"));
        Assert.AreEqual(MockRaven.MinaAddress, set.Routes.Carriers.Listed.Single().Address);

        // Rena is not verified: he can't hold anyone's mail.
        RavenException bad = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.ContactsSetRoutesAsync(
            MockRaven.AliAddress, new JsonObject { ["custodians"] = new JsonArray(JsonValue.Create(MockRaven.RenaAddress)) }));
        Assert.AreEqual(RavenErrorCodes.BadRequest, bad.Code);
    }

    [TestMethod]
    public async Task NodeStop_FollowsTheEnginesStopRules()
    {
        MockRaven engine = Ready(); // the demo's background program was started earlier, not by this app
        NodeStatus advice = await engine.NodeStopAsync();
        Assert.AreEqual(NodeStopResults.Advice, advice.Stop);
        Assert.IsTrue(advice.Running, "the app stops only a background program it started");
        StringAssert.Contains(advice.Note!, "taskkill /F /PID 4242");
        Assert.IsNull((await engine.NodeStatusAsync()).Stop, "only node.stop says what happened");

        engine.LoginServiceRunsNode = true;
        NodeStatus managed = await engine.NodeStopAsync();
        Assert.AreEqual(NodeStopResults.Managed, managed.Stop);
        Assert.IsTrue(managed.Running);
    }

    [TestMethod]
    public async Task LabMock_ListsItsSwitchesAndWays()
    {
        var engine = new MockRaven(ManualTimeProvider.StartingAt2026(), startReady: true, demoEvents: false, labNetwork: true);
        NodeStatus node = await engine.NodeStatusAsync();
        CollectionAssert.Contains(node.LabSwitches, "RAVEN_LAB_TEST_A");
        CollectionAssert.Contains(node.Lab, "p2p");
        CollectionAssert.DoesNotContain(node.Lab, "lan", "LAN direct is released, not opened by a switch");
        CollectionAssert.DoesNotContain(node.Lab, "websocket", "its own switch is not set");

        NetworkStatus net = await engine.NetworkStatusAsync();
        CollectionAssert.AreEqual(MockRaven.LabSwitches.ToList(), net.LabSwitches);
        Assert.AreEqual(WayStates.Off, net.Way("p2p")?.State);
        Assert.IsTrue(net.Way("p2p")!.Lab);

        // Turning a lab way on: lab, and it waits for a restart while a background program runs.
        NetworkSetResult set = await engine.NetworkSetAsync(new JsonObject { ["p2p"] = true });
        Assert.IsTrue(set.RestartNeeded);
        Assert.AreEqual("restart", set.Changes.Single().Applies);
        Assert.AreEqual(WayStates.Lab, set.Status.Way("p2p")?.State);
        Assert.AreEqual("7423", set.Status.P2pListen);

        // A proxy with a user name or password is refused before anything changes.
        RavenException proxy = await Assert.ThrowsExactlyAsync<RavenException>(
            () => engine.NetworkSetAsync(new JsonObject { ["proxy"] = "http://user:secret@proxy.example:8080" }));
        Assert.AreEqual(RavenErrorCodes.BadRequest, proxy.Code);
    }

    [TestMethod]
    public void NetworkResults_DecodeNewerValuesWithoutFailing()
    {
        var json = JsonNode.Parse("""
            {"build":"release","carriers":[{"id":"nearby","state":"not_on_this_os","reason":"x","future":1}],
             "protections":[],"help":{"bridge":true,"store":true,"relay":false},"a_newer_field":true}
            """)!.AsObject();
        NetworkStatus net = json.Deserialize<NetworkStatus>(RavenJson.Options)!;
        Assert.AreEqual(WayStates.NotOnThisOs, net.Way("nearby")?.State);
        var msg = JsonNode.Parse("""{"id":"m","state":"delivered","path":"Held by a friend","path_kind":"friend"}""")!
            .Deserialize<Message>(RavenJson.Options)!;
        Assert.AreEqual("friend", msg.PathKind);
    }
}
#endif
