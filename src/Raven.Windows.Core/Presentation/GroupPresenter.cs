using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.Presentation;

/// <summary>How one member's copy of a message of yours shows in the group ring and the list under it.</summary>
public enum ReceiptTone
{
    /// <summary>Confirmed by the member's signed receipt (successFill).</summary>
    Delivered,
    /// <summary>Waiting, passed on by another member (warningFill): reached through a member, not confirmed yet.</summary>
    PassedOn,
    /// <summary>Waiting on this computer or sent and not confirmed yet (borderStrong).</summary>
    Pending,
    /// <summary>Given up (dangerFill).</summary>
    Failed,
}

/// <summary>
/// Group chats as the sidebar, the header and the chat say them (API.md, "Groups"). Only what the
/// engine reports: the members' presence comes from contacts.list for members who are your friends.
/// </summary>
public static class GroupPresenter
{
    public static string DisplayName(Group group)
    {
        string name = TextSanitizer.SingleLine(group.Name);
        return name.Length > 0 ? name : TextSanitizer.SingleLine(group.ShortId);
    }

    public static string MemberName(GroupMember member)
    {
        if (member.Me) return Loc.Get("you.title");
        string name = TextSanitizer.SingleLine(member.Name);
        return name.Length > 0 ? name : TextSanitizer.SingleLine(member.ShortAddress);
    }

    /// <summary>
    /// The sidebar's line under the group's name: "5 members \u00B7 3 in touch now", "5 members", or
    /// that you left or were removed. <paramref name="inTouch"/> counts the members who are your
    /// friends and in touch now (null: not known yet).
    /// </summary>
    public static string StatusLine(Group group, int? inTouch)
    {
        switch (group.Status)
        {
            case GroupStatuses.Left:
                return Loc.Get("group.left");
            case GroupStatuses.Removed:
                return Loc.Get("group.removed");
        }
        return inTouch is int n && n > 0
            ? Loc.Format("group.membersInTouch", ("n", group.Members), ("inTouch", n))
            : Loc.Format("group.members", ("n", group.Members));
    }

    /// <summary>Members who are your friends and in touch now (presence from contacts.list).</summary>
    public static int InTouch(IEnumerable<GroupMember> members, IReadOnlyDictionary<string, Contact> contacts)
        => members.Count(m => !m.Me && m.Friend && contacts.TryGetValue(m.Address, out Contact? c) && c.Presence == Presences.InTouch);

    /// <summary>The header's line: "5 members \u00B7 a sealed copy for each", or that you left or were removed.</summary>
    public static string HeaderLine(Group group)
        => group.IsActive ? Loc.Format("group.subtitle", ("n", group.Members)) : StatusLine(group, null);

    /// <summary>The route line's words: "4 sealed copies, one for each member" (you excluded).</summary>
    public static string RouteLine(Group group) => Loc.Format("group.route", ("n", Math.Max(0, group.Members - 1)));

    /// <summary>"Message Weekend trip".</summary>
    public static string Composer(Group group) => Loc.Format("home.composer", ("name", DisplayName(group)));

    /// <summary>The tick of a message of yours: "Delivered to 3 of 5" (the engine's delivered and total).</summary>
    public static string Tick(GroupMessage message)
    {
        int total = message.Total > 0 ? message.Total : message.Receipts.Count;
        int delivered = message.Total > 0 ? message.Delivered : message.Receipts.Count(r => r.State == GroupReceiptStates.Delivered);
        return Loc.Format("group.delivered", ("n", delivered), ("total", total));
    }

    /// <summary>All copies confirmed.</summary>
    public static bool AllDelivered(GroupMessage message)
    {
        int total = message.Total > 0 ? message.Total : message.Receipts.Count;
        int delivered = message.Total > 0 ? message.Delivered : message.Receipts.Count(r => r.State == GroupReceiptStates.Delivered);
        return total > 0 && delivered >= total;
    }

    /// <summary>
    /// The note beside the tick: "1 waiting \u00B7 1 not confirmed yet \u00B7 1 not delivered"; empty
    /// when every copy is confirmed.
    /// </summary>
    public static string Note(GroupMessage message)
    {
        var parts = new List<string>();
        int waiting = message.Receipts.Count(r => r.State == GroupReceiptStates.Waiting);
        int sent = message.Receipts.Count(r => r.State == GroupReceiptStates.Sent);
        int failed = message.Receipts.Count(r => r.State == GroupReceiptStates.Failed);
        if (waiting > 0) parts.Add(Loc.Format("group.note.waiting", ("n", waiting)));
        if (sent > 0) parts.Add(Loc.Format("group.note.sent", ("n", sent)));
        if (failed > 0) parts.Add(Loc.Format("group.note.failed", ("n", failed)));
        return string.Join(" \u00B7 ", parts);
    }

    public static ReceiptTone ToneOf(GroupReceipt receipt) => receipt.State switch
    {
        GroupReceiptStates.Delivered => ReceiptTone.Delivered,
        GroupReceiptStates.Failed => ReceiptTone.Failed,
        GroupReceiptStates.Waiting when receipt.PassedOn => ReceiptTone.PassedOn,
        _ => ReceiptTone.Pending,
    };

    /// <summary>The ring's arcs, one per member it was for, in the order of the receipts.</summary>
    public static IReadOnlyList<ReceiptTone> Segments(GroupMessage message) => message.Receipts.Select(ToneOf).ToList();

    /// <summary>One member's line under the ring: "Delivered", "Sent, not confirmed yet", "Waiting", "Not delivered".</summary>
    public static string ReceiptText(GroupReceipt receipt) => receipt.State switch
    {
        GroupReceiptStates.Delivered => Loc.Get("tick.delivered"),
        GroupReceiptStates.Sent => Loc.Get("tick.sent"),
        GroupReceiptStates.Failed => Loc.Get("tick.failed"),
        _ => Loc.Get("group.receipt.waiting"),
    };

    /// <summary>Under a member's message passed on by another member: "Passed on by Ali".</summary>
    public static string? PassedOnBy(GroupMessage message)
    {
        string via = TextSanitizer.SingleLine(message.ViaName);
        return message.Mine || via.Length == 0 ? null : Loc.Format("group.passedOn", ("name", via));
    }

    /// <summary>The chip beside a member reached through another member.</summary>
    public static string? ViaMember(GroupReceipt receipt) => receipt.PassedOn ? Loc.Get("group.receipt.viaMember") : null;

    /// <summary>What Narrator says for a message of yours: its text, then "Delivered to 3 of 5" and the note.</summary>
    public static string AccessibleTick(GroupMessage message)
    {
        string note = Note(message);
        return note.Length == 0 ? Tick(message) : Tick(message) + ", " + note;
    }

    /// <summary>
    /// The friends who can still be added: your friends (not blocked) who are not members yet, up
    /// to the 16-member cap (you included).
    /// </summary>
    public static IReadOnlyList<Contact> Addable(IEnumerable<Contact> contacts, IEnumerable<GroupMember> members)
    {
        var inGroup = new HashSet<string>(members.Select(m => m.Address), StringComparer.Ordinal);
        return contacts.Where(c => !c.Blocked && !inGroup.Contains(c.Address)).ToList();
    }

    /// <summary>How many more members fit (16 in all, you included).</summary>
    public static int Room(int members) => Math.Max(0, GroupLimits.MaxMembers - members);

    /// <summary>
    /// Leave is offered to a member who is not the admin, while the group is active (the admin
    /// can't leave: owner decision 2026-10-28).
    /// </summary>
    public static bool CanLeave(Group group) => group.IsActive && !group.Admin;

    /// <summary>Add and remove are the admin's, while the group is active.</summary>
    public static bool CanManage(Group group) => group.IsActive && group.Admin;
}
