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

/// <summary>
/// Groups (API.md, "Groups"): the mock follows the engine's contract (lab only; 16 members, you
/// included; one admin who can't leave; receipts per member; group_message and group_changed),
/// events.poll reads a group_message's GroupMessage, and the presenter says only what the engine
/// reports ("Delivered to 2 of 4").
/// </summary>
[TestClass]
public sealed class GroupsTests
{
    private static (MockRaven Engine, ManualTimeProvider Time) Lab(bool demoEvents = false)
    {
        ManualTimeProvider time = ManualTimeProvider.StartingAt2026();
        return (new MockRaven(time, startReady: true, demoEvents: demoEvents, labNetwork: true), time);
    }

    [TestMethod]
    public async Task Release_HasNoGroups()
    {
        var engine = new MockRaven(ManualTimeProvider.StartingAt2026(), startReady: true, demoEvents: false);
        GroupsList list = await engine.GroupsListAsync();
        Assert.IsFalse(list.Available);
        Assert.AreEqual(0, list.Groups.Count);
        RavenException ex = await Assert.ThrowsExactlyAsync<RavenException>(
            () => engine.GroupsCreateAsync("Trip", new[] { MockRaven.AliAddress }));
        Assert.AreEqual(RavenErrorCodes.NotAvailableYet, ex.Code);
    }

    [TestMethod]
    public async Task Lab_HasTheDemoGroup()
    {
        (MockRaven engine, _) = Lab();
        GroupsList list = await engine.GroupsListAsync();
        Assert.IsTrue(list.Available);
        Group trip = list.Groups.Single();
        Assert.AreEqual(MockRaven.DemoGroupId, trip.Id);
        Assert.AreEqual(32, trip.Id.Length);
        Assert.IsFalse(trip.Admin, "Ali is its admin");
        Assert.AreEqual(5, trip.Members);
        Assert.AreEqual(GroupStatuses.Active, trip.Status);
        Assert.AreEqual(Loc.Get("you.title"), trip.LastFrom);

        GroupView view = await engine.GroupsGetAsync(trip.Id);
        Assert.AreEqual(1, view.Members.Count(m => m.Me));
        Assert.AreEqual(MockRaven.AliAddress, view.Members.Single(m => m.Admin).Address);
        GroupMember lena = view.Members.Single(m => m.Address == MockRaven.LenaAddress);
        Assert.IsFalse(lena.Friend, "a member who is not your friend");

        IReadOnlyList<GroupMessage> history = await engine.GroupsHistoryAsync(trip.Id, null, 100);
        Assert.AreEqual(3, history.Count);
        Assert.IsTrue(history.Zip(history.Skip(1)).All(p => p.First.AtMs <= p.Second.AtMs), "oldest first");
        Assert.AreEqual("Ali", history[1].ViaName, "Lena's message was passed on by Ali");
        GroupMessage mine = history.Single(m => m.Mine);
        Assert.AreEqual(4, mine.Total);
        Assert.AreEqual(2, mine.Delivered);
        Assert.AreEqual(Loc.Format("group.delivered", ("n", 2), ("total", 4)), GroupPresenter.Tick(mine));
        Assert.IsTrue(mine.Receipts.Single(r => r.Address == MockRaven.LenaAddress).PassedOn);
    }

    [TestMethod]
    public async Task Send_ReceiptsFillAsMembersConfirm()
    {
        (MockRaven engine, ManualTimeProvider time) = Lab();
        long after = (await engine.EventsPollAsync(0)).Seq;
        GroupSent sent = await engine.GroupsSendAsync(MockRaven.DemoGroupId, "Who has the map?");
        Assert.IsTrue(sent.Message.Mine);
        Assert.AreEqual(4, sent.Message.Total);
        Assert.AreEqual(0, sent.Message.Delivered, "nothing is confirmed when it returns");
        Assert.IsTrue(sent.Message.Receipts.All(r => r.State == GroupReceiptStates.Waiting));

        time.Advance(3000);
        EventsPage page = await engine.EventsPollAsync(after);
        Assert.IsTrue(page.Events.Any(e => e.Kind == EventKinds.GroupChanged && e.Group == MockRaven.DemoGroupId));

        GroupMessage now = (await engine.GroupsHistoryAsync(MockRaven.DemoGroupId, null, 100)).Single(m => m.Id == sent.Message.Id);
        Assert.AreEqual(GroupReceiptStates.Delivered, now.Receipts.Single(r => r.Address == MockRaven.AliAddress).State);
        Assert.AreEqual(GroupReceiptStates.Waiting, now.Receipts.Single(r => r.Address == MockRaven.MinaAddress).State, "Mina is away");
        Assert.AreEqual(GroupReceiptStates.Delivered, now.Receipts.Single(r => r.Address == MockRaven.LenaAddress).State, "passed on by Ali, then confirmed");
        Assert.AreEqual(3, now.Delivered);
        Assert.IsFalse(GroupPresenter.AllDelivered(now));
        Assert.AreEqual(Loc.Format("group.note.waiting", ("n", 1)), GroupPresenter.Note(now));
    }

    [TestMethod]
    public async Task GroupMessageEvent_CarriesTheGroupMessage()
    {
        (MockRaven engine, ManualTimeProvider time) = Lab(demoEvents: true);
        long after = (await engine.EventsPollAsync(0)).Seq;
        time.Advance(4500);
        EventsPage page = await engine.EventsPollAsync(after);
        RavenEvent e = page.Events.Single(x => x.Kind == EventKinds.GroupMessage);
        Assert.AreEqual(MockRaven.DemoGroupId, e.Group);
        Assert.IsNull(e.Message, "not a chat message");
        Assert.IsNotNull(e.GroupMessage);
        Assert.AreEqual("Lena", e.GroupMessage!.FromName);
        Assert.AreEqual("Ali", e.GroupMessage.ViaName);
        Assert.IsFalse(e.GroupMessage.Mine);
        Assert.AreEqual(1, (await engine.GroupsListAsync()).Groups.Single().Unread);
        Assert.AreEqual(0, await engine.GroupsMarkReadAsync(MockRaven.DemoGroupId));
        Assert.AreEqual(0, (await engine.GroupsListAsync()).Groups.Single().Unread);
    }

    [TestMethod]
    public async Task Create_MakesYouItsAdmin_AndTheAdminCantLeave()
    {
        (MockRaven engine, _) = Lab();
        GroupView made = await engine.GroupsCreateAsync("  Book club ", new[] { MockRaven.AliAddress, MockRaven.RenaAddress });
        Assert.AreEqual("Book club", made.Group.Name);
        Assert.IsTrue(made.Group.Admin);
        Assert.AreEqual(3, made.Group.Members);
        Assert.IsTrue(made.Members.Single(m => m.Me).Admin);
        Assert.IsTrue(GroupPresenter.CanManage(made.Group));
        Assert.IsFalse(GroupPresenter.CanLeave(made.Group), "the admin can't leave (owner decision)");

        RavenException leave = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.GroupsLeaveAsync(made.Group.Id));
        Assert.AreEqual(RavenErrorCodes.GroupAdminCannotLeave, leave.Code);

        GroupView added = await engine.GroupsAddAsync(made.Group.Id, new[] { MockRaven.MinaAddress });
        Assert.AreEqual(4, added.Group.Members);
        RavenException again = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.GroupsAddAsync(made.Group.Id, new[] { MockRaven.MinaAddress }));
        Assert.AreEqual(RavenErrorCodes.GroupAlreadyMember, again.Code);
        GroupView removed = await engine.GroupsRemoveAsync(made.Group.Id, MockRaven.RenaAddress);
        Assert.AreEqual(3, removed.Group.Members);
    }

    [TestMethod]
    public async Task Create_RefusesStrangersBadNamesAndMoreThanSixteen()
    {
        (MockRaven engine, _) = Lab();
        RavenException stranger = await Assert.ThrowsExactlyAsync<RavenException>(
            () => engine.GroupsCreateAsync("Trip", new[] { MockRaven.LenaAddress }));
        Assert.AreEqual(RavenErrorCodes.NotAContact, stranger.Code);
        RavenException name = await Assert.ThrowsExactlyAsync<RavenException>(
            () => engine.GroupsCreateAsync("   ", new[] { MockRaven.AliAddress }));
        Assert.AreEqual(RavenErrorCodes.GroupBadName, name.Code);

        Assert.AreEqual(16, GroupLimits.MaxMembers);
        var seventeen = Enumerable.Range(0, 16).Select(i => $"rvn1qmember{i:D2}000000000000000000000000").ToList();
        RavenException tooMany = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.GroupsCreateAsync("Big", seventeen));
        Assert.AreEqual(RavenErrorCodes.GroupTooLarge, tooMany.Code, "16 friends and you are 17");
        Assert.AreEqual(13, GroupPresenter.Room(3));
        Assert.AreEqual(0, GroupPresenter.Room(20));
    }

    [TestMethod]
    public async Task Leave_KeepsTheHistory_AndSendingStops()
    {
        (MockRaven engine, _) = Lab();
        Group trip = (await engine.GroupsListAsync()).Groups.Single();
        Assert.IsTrue(GroupPresenter.CanLeave(trip));
        Assert.IsFalse(GroupPresenter.CanManage(trip), "only Ali adds and removes");
        RavenException notAdmin = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.GroupsRemoveAsync(trip.Id, MockRaven.RenaAddress));
        Assert.AreEqual(RavenErrorCodes.GroupNotAdmin, notAdmin.Code);

        Group left = await engine.GroupsLeaveAsync(trip.Id);
        Assert.AreEqual(GroupStatuses.Left, left.Status);
        Assert.AreEqual(Loc.Get("group.left"), GroupPresenter.StatusLine(left, 2));
        Assert.AreEqual(3, (await engine.GroupsHistoryAsync(trip.Id, null, 100)).Count, "the history stays");
        RavenException send = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.GroupsSendAsync(trip.Id, "hi"));
        Assert.AreEqual(RavenErrorCodes.GroupNotMember, send.Code);
    }

    [TestMethod]
    public async Task Send_RefusesTooLongAndUnknownGroups()
    {
        (MockRaven engine, _) = Lab();
        RavenException tooLong = await Assert.ThrowsExactlyAsync<RavenException>(
            () => engine.GroupsSendAsync(MockRaven.DemoGroupId, new string('x', GroupLimits.MaxMessageBytes + 1)));
        Assert.AreEqual(RavenErrorCodes.MessageTooLong, tooLong.Code);
        RavenException unknown = await Assert.ThrowsExactlyAsync<RavenException>(
            () => engine.GroupsGetAsync("00000000000000000000000000000000"));
        Assert.AreEqual(RavenErrorCodes.GroupNotFound, unknown.Code);
    }

    [TestMethod]
    public void Presenter_SaysOnlyWhatTheEngineReports()
    {
        var group = new Group { Id = "g", Name = "Weekend trip", Members = 5, Status = GroupStatuses.Active };
        Assert.AreEqual(Loc.Format("group.membersInTouch", ("n", 5), ("inTouch", 3)), GroupPresenter.StatusLine(group, 3));
        Assert.AreEqual(Loc.Format("group.members", ("n", 5)), GroupPresenter.StatusLine(group, 0));
        Assert.AreEqual(Loc.Format("group.members", ("n", 5)), GroupPresenter.StatusLine(group, null));
        Assert.AreEqual(Loc.Format("group.subtitle", ("n", 5)), GroupPresenter.HeaderLine(group));
        Assert.AreEqual(Loc.Format("group.route", ("n", 4)), GroupPresenter.RouteLine(group), "a copy for each other member");
        Assert.AreEqual(Loc.Format("home.composer", ("name", "Weekend trip")), GroupPresenter.Composer(group));
        group.Status = GroupStatuses.Removed;
        Assert.AreEqual(Loc.Get("group.removed"), GroupPresenter.StatusLine(group, 3));
        Assert.AreEqual(Loc.Get("group.removed"), GroupPresenter.HeaderLine(group));

        var contacts = new Dictionary<string, Contact>
        {
            ["a"] = new Contact { Address = "a", Presence = Presences.InTouch },
            ["b"] = new Contact { Address = "b", Presence = Presences.Waiting },
        };
        var members = new[]
        {
            new GroupMember { Address = "me", Me = true },
            new GroupMember { Address = "a", Friend = true },
            new GroupMember { Address = "b", Friend = true },
            new GroupMember { Address = "c", Friend = false },
        };
        Assert.AreEqual(1, GroupPresenter.InTouch(members, contacts));

        var message = new GroupMessage
        {
            Mine = true,
            Receipts =
            {
                new GroupReceipt { State = GroupReceiptStates.Delivered },
                new GroupReceipt { State = GroupReceiptStates.Sent },
                new GroupReceipt { State = GroupReceiptStates.Waiting, Via = "relay" },
                new GroupReceipt { State = GroupReceiptStates.Failed },
            },
        };
        Assert.AreEqual(Loc.Format("group.delivered", ("n", 1), ("total", 4)), GroupPresenter.Tick(message), "counted from the receipts without totals");
        CollectionAssert.AreEqual(new[] { ReceiptTone.Delivered, ReceiptTone.Pending, ReceiptTone.PassedOn, ReceiptTone.Failed },
            GroupPresenter.Segments(message).ToArray());
        Assert.AreEqual(string.Join(" \u00B7 ", Loc.Format("group.note.waiting", ("n", 1)), Loc.Format("group.note.sent", ("n", 1)), Loc.Format("group.note.failed", ("n", 1))),
            GroupPresenter.Note(message));
        Assert.AreEqual(Loc.Get("group.receipt.viaMember"), GroupPresenter.ViaMember(message.Receipts[2]));
        Assert.IsNull(GroupPresenter.ViaMember(message.Receipts[0]));
        Assert.AreEqual(Loc.Get("tick.sent"), GroupPresenter.ReceiptText(message.Receipts[1]));
        Assert.AreEqual(Loc.Get("group.receipt.waiting"), GroupPresenter.ReceiptText(message.Receipts[2]));
        StringAssert.StartsWith(GroupPresenter.AccessibleTick(message), GroupPresenter.Tick(message));
        Assert.IsNull(GroupPresenter.PassedOnBy(message), "yours");
        Assert.AreEqual(Loc.Format("group.passedOn", ("name", "Ali")), GroupPresenter.PassedOnBy(new GroupMessage { ViaName = "Ali" }));

        var addable = GroupPresenter.Addable(
            new[] { new Contact { Address = "a" }, new Contact { Address = "x" }, new Contact { Address = "y", Blocked = true } },
            members);
        CollectionAssert.AreEqual(new[] { "x" }, addable.Select(c => c.Address).ToArray());
    }

    [TestMethod]
    public void EveryGroupOperation_IsInTheContract()
    {
        string[] ops = { RavenOps.GroupsList, RavenOps.GroupsGet, RavenOps.GroupsCreate, RavenOps.GroupsAdd, RavenOps.GroupsRemove,
            RavenOps.GroupsLeave, RavenOps.GroupsHistory, RavenOps.GroupsSend, RavenOps.GroupsMarkRead };
        string api = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoPaths.RequireWindowsRoot(), "core", "desktop", "api", "API.md"));
        foreach (string op in ops) StringAssert.Contains(api, "`" + op + "`", op);
        StringAssert.Contains(api, "\"group_message\"");
        StringAssert.Contains(api, "\"group_changed\"");
    }
}
#endif
