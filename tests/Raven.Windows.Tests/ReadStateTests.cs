#if DEBUG
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Mock;

namespace Raven.Windows.Tests;

/// <summary>
/// E1 round two: read and unread come from the engine, which shares one read marker per friend
/// with the terminal app (Contact.unread, the unread event, messages.mark_read). A chat read in
/// either app is read in both, and the counts survive a restart.
/// </summary>
[TestClass]
public sealed class ReadStateTests
{
    private static async Task<int> UnreadOf(IRavenEngine engine, string address)
        => (await engine.ContactsListAsync()).Single(c => c.Address == address).Unread;

    [TestMethod]
    public async Task Unread_IsCountedByTheEngine_AndMarkReadNeverMovesBack()
    {
        ManualTimeProvider clock = ManualTimeProvider.StartingAt2026();
        var engine = new MockRaven(clock, startReady: true, demoEvents: true);
        var seen = new List<RavenEvent>();
        long after = 0;

        async Task PollAsync()
        {
            EventsPage page = await engine.EventsPollAsync(after);
            seen.AddRange(page.Events);
            after = page.Seq;
        }

        // Ali's question was read before the demo: nothing unread.
        Assert.IsTrue((await engine.ContactsListAsync()).All(c => c.Unread == 0));
        await PollAsync();
        clock.Advance(2700); // Ali writes at 1.2 s, Rena at 2.6 s
        await PollAsync();
        Assert.AreEqual(1, await UnreadOf(engine, MockRaven.RenaAddress));
        RavenEvent counted = seen.Last(e => e.Kind == EventKinds.Unread);
        Assert.AreEqual(MockRaven.RenaAddress, counted.Address);
        Assert.AreEqual(1, counted.Unread);

        // Read here (or in the terminal app): the count goes, and an event says so.
        Assert.AreEqual(0, await engine.MessagesMarkReadAsync(MockRaven.RenaAddress));
        await PollAsync();
        Assert.AreEqual(0, seen.Last(e => e.Kind == EventKinds.Unread && e.Address == MockRaven.RenaAddress).Unread);
        Assert.AreEqual(0, await UnreadOf(engine, MockRaven.RenaAddress));

        // Never back: Ali's oldest message marked now leaves his newest read.
        IReadOnlyList<Message> ali = await engine.MessagesHistoryAsync(MockRaven.AliAddress, null, 100);
        Assert.AreEqual(0, await engine.MessagesMarkReadAsync(MockRaven.AliAddress, ali.Last().Id));
        Assert.AreEqual(0, await engine.MessagesMarkReadAsync(MockRaven.AliAddress, ali.First().Id));

        // Yours are never unread.
        await engine.MessagesSendAsync(MockRaven.RenaAddress, "hi");
        Assert.AreEqual(0, await UnreadOf(engine, MockRaven.RenaAddress));

        RavenException gone = await Assert.ThrowsExactlyAsync<RavenException>(
            () => engine.MessagesMarkReadAsync(MockRaven.AliAddress, "no-such-message"));
        Assert.AreEqual(RavenErrorCodes.BadRequest, gone.Code);
    }

    [TestMethod]
    public async Task MarkRead_SendsTheNewestMessageShown()
    {
        var engine = new RecordingEngine(new StubEngine().Answer(RavenOps.MessagesMarkRead, """{"address":"rvn1x","unread":2}"""));
        Assert.AreEqual(2, await engine.MessagesMarkReadAsync("rvn1x", "m7"));
        await engine.MessagesMarkReadAsync("rvn1x");
        IReadOnlyList<JsonObject> args = engine.ArgsOf(RavenOps.MessagesMarkRead);
        Assert.AreEqual("m7", (string?)args[0]["id"]);
        Assert.IsFalse(args[1].ContainsKey("id"), "no id: the newest");
    }

    [TestMethod]
    public async Task UnreadFields_Decode_AndAnOlderEngineReadsAsZero()
    {
        var engine = new StubEngine()
            .Answer(RavenOps.ContactsList, """{"contacts":[{"address":"a","name":"n","short_address":"s","verified":true,"presence":"none"},{"address":"b","name":"m","short_address":"t","verified":true,"presence":"none","unread":3}]}""")
            .Answer(RavenOps.EventsPoll, """{"seq":4,"events":[{"seq":4,"kind":"unread","at_ms":5,"address":"b","unread":0}],"reset":false}""");
        IReadOnlyList<Contact> contacts = await engine.ContactsListAsync();
        Assert.AreEqual(0, contacts[0].Unread);
        Assert.AreEqual(3, contacts[1].Unread);
        RavenEvent e = (await engine.EventsPollAsync(0)).Events.Single();
        Assert.AreEqual(EventKinds.Unread, e.Kind);
        Assert.AreEqual(0, e.Unread);
    }
}
#endif
