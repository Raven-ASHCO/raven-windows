#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Mock;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Tests;

[TestClass]
public sealed class MockRavenTests
{
    private static (MockRaven Engine, ManualTimeProvider Clock) Ready(bool demo = true)
    {
        ManualTimeProvider clock = ManualTimeProvider.StartingAt2026();
        return (new MockRaven(clock, startReady: true, demoEvents: demo), clock);
    }

    [TestMethod]
    public async Task FirstRun_IdentityIsCreatedWithTheDemoWords()
    {
        var engine = new MockRaven(ManualTimeProvider.StartingAt2026());

        Assert.AreEqual(ProfileStates.New, (await engine.ProfileStateAsync()).State);
        RavenException notReady = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.IdentityMeAsync());
        Assert.AreEqual(RavenErrorCodes.NotInitialized, notReady.Code);

        IdentityCreated created = await engine.IdentityCreateAsync("  Sam  ");
        CollectionAssert.AreEqual(
            new[] { "turtle", "frog", "bread", "wolf", "paper", "laptop", "funnel", "mango", "honey", "wagon" },
            created.Words);
        Assert.AreEqual(MockRaven.MyOldCode, created.OldCode);
        Assert.AreEqual(ProfileStates.Ready, (await engine.ProfileStateAsync()).State);

        Me me = await engine.IdentityMeAsync();
        Assert.AreEqual("Sam", me.DisplayName);
        Assert.AreEqual(10, me.Words.Count);
        StringAssert.StartsWith(me.Invite, "raven:" + MockRaven.MyAddress);
    }

    [TestMethod]
    public async Task DemoFriends_MatchTheContract()
    {
        (MockRaven engine, _) = Ready();
        IReadOnlyList<Contact> contacts = await engine.ContactsListAsync();
        Assert.AreEqual(3, contacts.Count);

        Contact ali = contacts.Single(c => c.Name == "Ali");
        Assert.IsTrue(ali.Verified);
        Assert.AreEqual(Presences.InTouch, ali.Presence);
        Assert.IsNull(ali.Waiting);

        Contact mina = contacts.Single(c => c.Name == "Mina");
        Assert.IsNotNull(mina.Waiting);
        Assert.AreEqual(1, mina.Waiting!.Count);
        Assert.AreEqual(Presences.Waiting, mina.Presence);

        Contact rena = contacts.Single(c => c.Name == "Rena");
        Assert.IsFalse(rena.Verified);

        foreach (Contact c in contacts)
        {
            Assert.AreEqual(AvatarRule.IndexFor(c.Address), c.AvatarIndex, "avatar_index follows tokens.json avatarRule");
            Assert.IsFalse(string.IsNullOrEmpty(c.ShortAddress));
        }
    }

    [TestMethod]
    public async Task DemoEvents_ReplayTheMockupOnce()
    {
        (MockRaven engine, ManualTimeProvider clock) = Ready();
        var seen = new List<RavenEvent>();
        long after = 0;

        async Task PollAsync()
        {
            EventsPage page = await engine.EventsPollAsync(after);
            Assert.IsTrue(page.Events.All(e => e.Seq > after), "events.poll returns only events after after_seq");
            seen.AddRange(page.Events);
            after = page.Seq;
        }

        await PollAsync(); // t0: schedules the demo, first status check
        Assert.AreEqual(EventKinds.StatusOk, seen.Single().Kind);

        clock.Advance(1300);
        await PollAsync();
        RavenEvent incoming = seen.Single(e => e.Kind == EventKinds.Incoming);
        Assert.AreEqual(MockRaven.AliAddress, incoming.Message!.Address);
        Assert.AreEqual(Directions.In, incoming.Message.Direction);

        clock.Advance(1400); // 2.7 s: Rena writes and is in touch
        await PollAsync();
        Assert.AreEqual(2, seen.Count(e => e.Kind == EventKinds.Incoming));
        Assert.IsTrue(seen.Any(e => e.Kind == EventKinds.Presence && e.Address == MockRaven.RenaAddress && e.Presence == Presences.InTouch));

        clock.Advance(3400); // 6.1 s: Raven tries Mina again
        await PollAsync();
        RavenEvent retry = seen.Single(e => e.Kind == EventKinds.Retry);
        Assert.AreEqual(MockRaven.MinaAddress, retry.Address);
        Assert.AreEqual(2, retry.Attempts);

        clock.Advance(4500); // 10.6 s: Mina is back, her waiting message is delivered with a receipt
        await PollAsync();
        Assert.IsTrue(seen.Any(e => e.Kind == EventKinds.Presence && e.Address == MockRaven.MinaAddress && e.Presence == Presences.InTouch));
        RavenEvent receipt = seen.Single(e => e.Kind == EventKinds.Receipt);
        Assert.AreEqual(MockRaven.LanPath, receipt.Path);
        Contact mina = (await engine.ContactsListAsync()).Single(c => c.Name == "Mina");
        Assert.IsNull(mina.Waiting);

        // Sequence numbers only grow, and the demo does not repeat.
        CollectionAssert.AreEqual(seen.Select(e => e.Seq).OrderBy(s => s).ToList(), seen.Select(e => e.Seq).ToList());
        clock.Advance(30_000);
        await PollAsync();
        Assert.AreEqual(2, seen.Count(e => e.Kind == EventKinds.Incoming));
    }

    [TestMethod]
    public async Task Heartbeat_IsAStatusCheckEveryTwoSeconds()
    {
        (MockRaven engine, ManualTimeProvider clock) = Ready(demo: false);
        EventsPage first = await engine.EventsPollAsync(0);
        Assert.AreEqual(1, first.Events.Count(e => e.Kind == EventKinds.StatusOk));

        clock.Advance(500);
        EventsPage soon = await engine.EventsPollAsync(first.Seq);
        Assert.AreEqual(0, soon.Events.Count, "no heartbeat before 2 s");

        clock.Advance(1600);
        EventsPage later = await engine.EventsPollAsync(soon.Seq);
        Assert.AreEqual(EventKinds.StatusOk, later.Events.Single().Kind);
    }

    [TestMethod]
    public async Task Send_ToAFriendInTouch_IsDeliveredOnlyWithAReceipt()
    {
        (MockRaven engine, ManualTimeProvider clock) = Ready(demo: false);
        long after = (await engine.EventsPollAsync(0)).Seq;

        Message sent = await engine.MessagesSendAsync(MockRaven.AliAddress, "Yes, I'll bring it");
        Assert.AreEqual(MessageStates.Sending, sent.State);
        Assert.AreEqual(Directions.Out, sent.Direction);

        EventsPage page = await engine.EventsPollAsync(after);
        Assert.IsTrue(page.Events.Any(e => e.Kind == EventKinds.SendStarted && e.Id == sent.Id));
        after = page.Seq;

        clock.Advance(800);
        page = await engine.EventsPollAsync(after);
        Assert.IsTrue(page.Events.Any(e => e.Kind == EventKinds.State && e.Id == sent.Id && e.State == MessageStates.Sent));
        Assert.IsFalse(page.Events.Any(e => e.Kind == EventKinds.Receipt));
        Assert.AreEqual(MessageStates.Sent, (await History(engine, MockRaven.AliAddress)).Single(m => m.Id == sent.Id).State,
            "not delivered before the signed receipt");
        after = page.Seq;

        clock.Advance(400);
        page = await engine.EventsPollAsync(after);
        RavenEvent receipt = page.Events.Single(e => e.Kind == EventKinds.Receipt);
        Assert.AreEqual(sent.Id, receipt.Id);
        Assert.AreEqual(MockRaven.LanPath, receipt.Path);
        Assert.AreEqual(MessageStates.Delivered, (await History(engine, MockRaven.AliAddress)).Single(m => m.Id == sent.Id).State);
    }

    [TestMethod]
    public async Task Send_ToAFriendNotInTouch_Waits_AndCanBeStopped()
    {
        (MockRaven engine, ManualTimeProvider clock) = Ready(demo: false);
        Message sent = await engine.MessagesSendAsync(MockRaven.RenaAddress, "Hello");
        clock.Advance(1000);
        await engine.EventsPollAsync(0);

        Message waiting = (await History(engine, MockRaven.RenaAddress)).Single(m => m.Id == sent.Id);
        Assert.AreEqual(MessageStates.Waiting, waiting.State);
        Assert.IsNotNull(waiting.UntilMs);
        Assert.AreEqual(1, (await engine.ContactsListAsync()).Single(c => c.Name == "Rena").Waiting!.Count);

        MessageDetails details = await engine.MessagesDetailsAsync(sent.Id);
        Assert.IsFalse(string.IsNullOrWhiteSpace(details.StatusText));
        Assert.IsFalse(string.IsNullOrWhiteSpace(details.WhyText));
        Assert.IsFalse(string.IsNullOrWhiteSpace(details.ActionText));
        // E1: plain lines under Details; ids and states only under Technical details.
        StringAssert.StartsWith(details.Detail, "Message ID: ");
        Assert.IsFalse(details.Detail.Contains("state=", System.StringComparison.Ordinal), details.Detail);
        StringAssert.Contains(details.Technical ?? string.Empty, "state=waiting");

        Message stopped = await engine.MessagesStopAsync(sent.Id);
        Assert.AreEqual(MessageStates.Failed, stopped.State);
        Assert.IsNull((await engine.ContactsListAsync()).Single(c => c.Name == "Rena").Waiting);
    }

    [TestMethod]
    public async Task Send_RejectsTooLongMessages()
    {
        (MockRaven engine, _) = Ready(demo: false);
        RavenException ex = await Assert.ThrowsExactlyAsync<RavenException>(
            () => engine.MessagesSendAsync(MockRaven.AliAddress, new string('a', MockRaven.MaxMessageLength + 1)));
        Assert.AreEqual(RavenErrorCodes.MessageTooLong, ex.Code);
        Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message));
    }

    [TestMethod]
    public async Task Cards_AreParsedWithoutTheFriendsWords_AndAddedAsNotVerified()
    {
        (MockRaven engine, _) = Ready(demo: false);
        const string invite = "raven:rvn1qnewfriendaddress0123456789abcdefgh:" +
            "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff@192.168.1.50:7420";

        JsonObject raw = await engine.CallAsync(RavenOps.CardParse, new JsonObject { ["text"] = invite });
        Assert.IsFalse(raw.ContainsKey("words"), "card.parse never returns the friend's security words");
        CardParsed parsed = await engine.CardParseAsync(invite);
        Assert.IsTrue(parsed.HasAddress);

        RavenException bad = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.CardParseAsync("hello there"));
        Assert.AreEqual(RavenErrorCodes.CardInvalid, bad.Code);

        RavenException clash = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.ContactsAddAsync(invite, "ali"));
        Assert.AreEqual(RavenErrorCodes.NameClash, clash.Code);

        Contact added = await engine.ContactsAddAsync(invite, "Sara");
        Assert.IsFalse(added.Verified);
        Assert.AreEqual(4, (await engine.ContactsListAsync()).Count);
    }

    [TestMethod]
    public async Task Cards_InternetRoutesAreLeftOutWithANote_InRelease_AndReadInTheLab()
    {
        const string card = "RAVEN CARD v1\naddress=rvn1qcardfriendaddress0123456789abcdef\n" +
            "lan=192.168.1.60:7420\ninet=198.51.100.7:7422\np2p=/ip4/198.51.100.7/tcp/7423\n";

        (MockRaven release, _) = Ready(demo: false);
        CardParsed left = await release.CardParseAsync(card);
        CollectionAssert.AreEqual(new[] { "internet", "p2p" }, left.IgnoredRoutes!);
        Assert.IsTrue(left.HasAddress, "the LAN address stays");
        Assert.AreEqual("lan", left.Routes.Single().Kind);
        StringAssert.Contains(left.Note!, "inet= and p2p=");
        Assert.AreEqual(Loc.Get("add.paste.ignoredRoutes"), CardPresenter.IgnoredRoutesLine(left));
        Assert.AreEqual(left.Note, CardPresenter.EngineNote(left));

        CardParsed plain = await release.CardParseAsync("RAVEN CARD v1\naddress=rvn1qcardfriendaddress0123456789abcdef\n");
        Assert.AreEqual(0, plain.IgnoredRoutes!.Count);
        Assert.IsNull(plain.Note);
        Assert.IsNull(CardPresenter.IgnoredRoutesLine(plain));
        Assert.IsFalse(plain.HasAddress);

        var lab = new MockRaven(ManualTimeProvider.StartingAt2026(), startReady: true, demoEvents: false, labNetwork: true);
        CardParsed read = await lab.CardParseAsync(card);
        CollectionAssert.AreEqual(new[] { "lan", "internet", "p2p" }, read.Routes.Select(r => r.Kind).ToArray());
        Assert.AreEqual(0, read.IgnoredRoutes!.Count);
        Assert.IsNull(CardPresenter.IgnoredRoutesLine(read));
    }

    [TestMethod]
    public async Task Settings_ChangeOnlyTheGivenFields()
    {
        (MockRaven engine, _) = Ready(demo: false);
        RavenSettings before = await engine.SettingsGetAsync();
        RavenSettings after = await engine.SettingsSetAsync(new JsonObject { ["reduced_motion"] = true, ["keep_chats_days"] = null });
        Assert.IsTrue(after.ReducedMotion);
        Assert.IsNull(after.KeepChatsDays, "null means Always");
        Assert.AreEqual(before.WhenQuit, after.WhenQuit);
        Assert.AreEqual(before.Receive, after.Receive);
    }

    [TestMethod]
    public async Task NodeStart_StartsThenListens()
    {
        var clock = ManualTimeProvider.StartingAt2026();
        var engine = new MockRaven(clock, startReady: false, demoEvents: false);
        await engine.IdentityCreateAsync(null);
        await engine.SettingsSetAsync(new JsonObject { ["receive"] = true });

        NodeStatus starting = await engine.NodeStartAsync();
        Assert.IsTrue(starting.IsStarting);
        clock.Advance(1300);
        EventsPage page = await engine.EventsPollAsync(0);
        NodeStatus listening = page.Events.Last(e => e.Kind == EventKinds.Node).Status!;
        Assert.AreEqual("7420", listening.ListeningPort);
        Assert.IsFalse(listening.IsLocalOnly);
    }

    [TestMethod]
    public void NoResponse_CarriesSecretLookingFields()
    {
        (MockRaven engine, ManualTimeProvider clock) = Ready();
        var requests = new (string Op, JsonObject? Args)[]
        {
            (RavenOps.AppInfo, null), (RavenOps.ProfileState, null), (RavenOps.IdentityMe, null),
            (RavenOps.CardMake, new JsonObject { ["include_address"] = true }), (RavenOps.ContactsList, null),
            (RavenOps.MessagesHistory, new JsonObject { ["address"] = MockRaven.MinaAddress, ["limit"] = 10 }),
            (RavenOps.NodeStatus, null), (RavenOps.SettingsGet, null), (RavenOps.EventsPoll, new JsonObject { ["after_seq"] = 0 }),
            (RavenOps.PrivacyState, null),
            (RavenOps.VerifyCheckOldCode, new JsonObject { ["address"] = MockRaven.RenaAddress, ["code"] = "wrong-code" }),
        };
        clock.Advance(11_000);
        foreach ((string op, JsonObject? args) in requests)
        {
            string response = engine.Handle(RavenEnvelope.BuildRequest(op, args));
            Assert.IsNull(RavenEnvelope.FindSecretField(JsonNode.Parse(response)), op);
        }
    }

    [TestMethod]
    public async Task UnknownOperation_IsABadRequest()
    {
        (MockRaven engine, _) = Ready(demo: false);
        RavenException ex = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.CallAsync("nope.nothing"));
        Assert.AreEqual(RavenErrorCodes.BadRequest, ex.Code);
        Assert.AreEqual(Loc.Get("error.badRequest"), ex.Message);
        Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Detail));

        RavenException missingArgument = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.CallAsync(RavenOps.ContactsRename));
        Assert.AreEqual(RavenErrorCodes.BadRequest, missingArgument.Code);
    }

    [TestMethod]
    public async Task ErrorMessages_UseTheSharedTexts()
    {
        var fresh = new MockRaven(ManualTimeProvider.StartingAt2026(), demoEvents: false);
        RavenException notReady = await Assert.ThrowsExactlyAsync<RavenException>(() => fresh.ContactsListAsync());
        Assert.AreEqual(Loc.Get("error.notInitialized"), notReady.Message);

        (MockRaven engine, _) = Ready(demo: false);
        RavenException stranger = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.MessagesHistoryAsync("rvn1qnosuchfriend", null, 10));
        Assert.AreEqual(RavenErrorCodes.NotAContact, stranger.Code);
        Assert.AreEqual(Loc.Get("error.notAContact"), stranger.Message);
        RavenException card = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.CardParseAsync("hello there"));
        Assert.AreEqual(Loc.Get("error.cardInvalid"), card.Message);
    }

    [TestMethod]
    public async Task Privacy_IsNotAvailableYet_AndChangesNothing()
    {
        (MockRaven engine, _) = Ready(demo: false);
        PrivacyState state = await engine.PrivacyStateAsync();
        Assert.IsFalse(state.Available);
        Assert.IsFalse(state.Encrypted);
        Assert.IsFalse(state.Locked);

        var calls = new Func<Task>[]
        {
            () => engine.PrivacyLockAsync(),
            () => engine.PrivacyUnlockAsync(),
            () => engine.PrivacyEncryptAsync(),
            () => engine.PrivacyEraseAsync(),
        };
        foreach (Func<Task> call in calls)
        {
            RavenException ex = await Assert.ThrowsExactlyAsync<RavenException>(call);
            Assert.AreEqual(RavenErrorCodes.NotAvailableYet, ex.Code);
            Assert.AreEqual(Loc.Get("error.notAvailableYet"), ex.Message);
        }

        Assert.AreEqual(ProfileStates.Ready, (await engine.ProfileStateAsync()).State);
        Assert.AreEqual(3, (await engine.ContactsListAsync()).Count);
    }

    [TestMethod]
    public async Task Erase_NeedsTheConfirmationWord()
    {
        (MockRaven engine, _) = Ready(demo: false);
        RavenException missing = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.CallAsync(RavenOps.PrivacyErase));
        Assert.AreEqual(RavenErrorCodes.BadRequest, missing.Code);
        RavenException wrong = await Assert.ThrowsExactlyAsync<RavenException>(
            () => engine.CallAsync(RavenOps.PrivacyErase, new JsonObject { ["confirm"] = "yes" }));
        Assert.AreEqual(RavenErrorCodes.BadRequest, wrong.Code);

        Assert.IsTrue(EraseConfirmation.Matches(" Erase "));
        Assert.IsFalse(EraseConfirmation.Matches("eras"));
        Assert.IsFalse(EraseConfirmation.Matches(null));
    }

    [TestMethod]
    public async Task LockedProfile_CanBeReviewed_ButNotUnlockedYet()
    {
        var engine = new MockRaven(ManualTimeProvider.StartingAt2026(), demoEvents: false, startLocked: true);
        Assert.AreEqual(ProfileStates.Locked, (await engine.ProfileStateAsync()).State);
        Assert.IsTrue((await engine.PrivacyStateAsync()).Locked);
        RavenException locked = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.ContactsListAsync());
        Assert.AreEqual(RavenErrorCodes.ProfileLocked, locked.Code);
        Assert.AreEqual(Loc.Get("error.profileLocked"), locked.Message);
        RavenException unlock = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.PrivacyUnlockAsync());
        Assert.AreEqual(RavenErrorCodes.NotAvailableYet, unlock.Code);
    }

    private static async Task<IReadOnlyList<Message>> History(MockRaven engine, string address)
        => await engine.MessagesHistoryAsync(address, null, 50);
}
#endif
