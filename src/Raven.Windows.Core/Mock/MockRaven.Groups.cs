#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.Mock;

/// <summary>
/// The mock's groups (API.md, "Groups"). Groups are lab only: a release mock answers
/// groups.list with available: false and every other groups.* op with NOT_AVAILABLE_YET, as a
/// release engine does. A lab mock (<c>labNetwork</c>, the app's --mock-lab) has one demo group,
/// "Weekend trip", whose admin is Ali; it holds you, Ali, Mina, Rena and Lena, a member who is
/// not your friend (her name is the label Ali gave her). Your messages carry one receipt per
/// member: friends in touch confirm within about a second, Mina's copy waits (she is away),
/// Rena's goes out and is confirmed later, and Lena's is passed on by Ali (via relay) and
/// confirmed after that. Each change is a group_changed event; a member's message is a
/// group_message event (and the unread count goes up). Limits: 16 members, you included (owner
/// decision 2026-10-28), a name of 1 to 64 characters, 8,192 bytes of text.
/// </summary>
public sealed partial class MockRaven
{
    /// <summary>The demo group's id (32 hex characters, opaque to the app).</summary>
    public const string DemoGroupId = "5eed0a7e1d2b3c4d5e6f708192a3b4c5";

    /// <summary>The demo group's member who is not your friend (Ali added her).</summary>
    public const string LenaAddress = "rvn1qlenanotafriend0000000000000000000000";

    private readonly List<MockGroup> _groups = new();
    private bool _groupsSeeded;
    private long _nextGroupMessage;

    private sealed class MockGroup
    {
        public string Id = "";
        public string Name = "";
        public string Status = GroupStatuses.Active;
        public string AdminAddress = "";
        public readonly List<GroupMember> Members = new();
        public readonly List<GroupMessage> Messages = new();
        public int Unread;
    }

    private bool GroupsLive => _lab;

    private JsonNode GroupsDispatch(string op, JsonObject a)
    {
        RequireReady();
        if (op == RavenOps.GroupsList)
        {
            if (!GroupsLive) return new JsonObject { ["available"] = false, ["groups"] = new JsonArray() };
            SeedGroups();
            var list = new JsonArray();
            foreach (MockGroup g in _groups.OrderByDescending(g => g.Messages.Count == 0 ? 0 : g.Messages.Max(m => m.AtMs)))
                list.Add(GroupNode(g));
            return new JsonObject { ["available"] = true, ["groups"] = list };
        }
        if (!GroupsLive)
            throw new MockError(RavenErrorCodes.NotAvailableYet, Loc.Get("error.notAvailableYet"), $"{op}: groups are lab only (RAVEN_LAB_GROUPS)");
        SeedGroups();

        switch (op)
        {
            case RavenOps.GroupsGet:
                return GroupView(FindGroup(ReqString(a, "group")));

            case RavenOps.GroupsCreate:
                return GroupView(CreateGroup(ReqString(a, "name"), AddressList(a)));

            case RavenOps.GroupsAdd:
            {
                MockGroup g = FindGroup(ReqString(a, "group"));
                RequireAdmin(g);
                List<string> adding = AddressList(a);
                foreach (string address in adding)
                {
                    if (g.Members.Any(m => m.Address == address))
                        throw new MockError(RavenErrorCodes.GroupAlreadyMember, "That friend is in the group already.", address);
                }
                if (g.Members.Count + adding.Count > GroupLimits.MaxMembers) throw TooLarge(g.Members.Count + adding.Count);
                foreach (string address in adding) g.Members.Add(MemberFor(FindFriend(address)));
                GroupChanged(g, NowMs);
                return GroupView(g);
            }

            case RavenOps.GroupsRemove:
            {
                MockGroup g = FindGroup(ReqString(a, "group"));
                RequireAdmin(g);
                string member = ReqString(a, "member");
                GroupMember? gone = g.Members.FirstOrDefault(m => m.Address == member && !m.Me);
                if (gone is null) throw Bad($"{member} is not a member of {g.Id}");
                g.Members.Remove(gone);
                GroupChanged(g, NowMs);
                return GroupView(g);
            }

            case RavenOps.GroupsLeave:
            {
                MockGroup g = FindGroup(ReqString(a, "group"));
                if (g.AdminAddress == MyAddress)
                {
                    throw new MockError(RavenErrorCodes.GroupAdminCannotLeave,
                        "The admin can't leave the group. Remove the other members instead.", "admin");
                }
                if (g.Status == GroupStatuses.Active)
                {
                    g.Status = GroupStatuses.Left;
                    GroupChanged(g, NowMs);
                }
                return new JsonObject { ["group"] = GroupNode(g) };
            }

            case RavenOps.GroupsHistory:
            {
                MockGroup g = FindGroup(ReqString(a, "group"));
                int limit = Math.Clamp(OptInt(a, "limit") ?? 100, 1, 500);
                List<GroupMessage> ordered = g.Messages.OrderBy(m => m.AtMs).ToList();
                if (OptString(a, "before") is string before)
                {
                    int cut = ordered.FindIndex(m => m.Id == before);
                    if (cut >= 0) ordered = ordered.Take(cut).ToList();
                }
                var list = new JsonArray();
                foreach (GroupMessage m in ordered.Skip(Math.Max(0, ordered.Count - limit))) list.Add(ToNode(m));
                return new JsonObject { ["messages"] = list };
            }

            case RavenOps.GroupsSend:
            {
                MockGroup g = FindGroup(ReqString(a, "group"));
                return new JsonObject { ["message"] = ToNode(SendToGroup(g, ReqString(a, "text"))) };
            }

            case RavenOps.GroupsMarkRead:
            {
                MockGroup g = FindGroup(ReqString(a, "group"));
                if (g.Unread != 0)
                {
                    g.Unread = 0;
                    GroupChanged(g, NowMs);
                }
                return Obj(("group", g.Id), ("unread", 0));
            }

            default:
                throw Bad($"unknown op '{op}'");
        }
    }

    // ------------------------------------------------------------------ demo group

    private void SeedGroups()
    {
        if (_groupsSeeded || !GroupsLive) return;
        _groupsSeeded = true;
        long now = NowMs;
        Friend ali = FindFriend(AliAddress);
        var trip = new MockGroup { Id = DemoGroupId, Name = "Weekend trip", AdminAddress = AliAddress };
        trip.Members.Add(MemberFor(ali, admin: true));
        trip.Members.Add(MeMember(admin: false));
        trip.Members.Add(MemberFor(FindFriend(MinaAddress)));
        trip.Members.Add(MemberFor(FindFriend(RenaAddress)));
        trip.Members.Add(new GroupMember
        {
            Address = LenaAddress, ShortAddress = ShortAddress(LenaAddress), Name = "Lena", Label = "Lena",
            Friend = false, Admin = false, Me = false,
        });
        trip.Messages.Add(GroupIncoming(trip, ali.Contact.Address, "Ali", "Cabin is booked, Friday to Sunday.", now - 50 * Minute, viaName: null));
        trip.Messages.Add(GroupIncoming(trip, LenaAddress, "Lena", "I'll bring the camping stove.", now - 40 * Minute, viaName: "Ali"));
        GroupMessage mine = NewMine(trip, "Great, I'll bring the map and snacks.", now - 30 * Minute);
        foreach (GroupReceipt r in mine.Receipts)
        {
            r.State = r.Address == AliAddress ? GroupReceiptStates.Delivered
                : r.Address == RenaAddress ? GroupReceiptStates.Delivered
                : GroupReceiptStates.Waiting;
        }
        Count(mine);
        trip.Messages.Add(mine);
        _groups.Add(trip);
    }

    /// <summary>A lab mock's demo: Lena (passed on by Ali) writes to the group a few seconds after the first look.</summary>
    private void ScheduleGroupDemo(long t0)
    {
        if (!GroupsLive) return;
        SeedGroups();
        Schedule(t0 + 4000, t =>
        {
            MockGroup? found = _groups.FirstOrDefault(g => g.Id == DemoGroupId);
            if (found is null || found.Status != GroupStatuses.Active) return;
            MockGroup trip = found;
            GroupMessage m = GroupIncoming(trip, LenaAddress, "Lena", "Who has the cabin key?", t, viaName: "Ali");
            trip.Messages.Add(m);
            trip.Unread++;
            GroupMessage copy = Clone(m);
            Emit(EventKinds.GroupMessage, t, e => { e.Group = trip.Id; e.GroupMessage = copy; });
            GroupChanged(trip, t);
        });
    }

    // ------------------------------------------------------------------ operations

    private MockGroup CreateGroup(string name, List<string> members)
    {
        string clean = TextSanitizer.SingleLine(name);
        if (clean.Length == 0 || clean.Length > GroupLimits.MaxNameChars)
            throw new MockError(RavenErrorCodes.GroupBadName, "Give the group a name of 1 to 64 characters.", $"name length {clean.Length}");
        List<string> distinct = members.Distinct(StringComparer.Ordinal).Where(m => m != MyAddress).ToList();
        if (distinct.Count + 1 > GroupLimits.MaxMembers) throw TooLarge(distinct.Count + 1);
        var g = new MockGroup { Id = NewGroupId(clean), Name = clean, AdminAddress = MyAddress };
        g.Members.Add(MeMember(admin: true));
        foreach (string address in distinct) g.Members.Add(MemberFor(FindFriend(address)));
        _groups.Add(g);
        GroupChanged(g, NowMs);
        return g;
    }

    private GroupMessage SendToGroup(MockGroup g, string text)
    {
        if (g.Status != GroupStatuses.Active)
            throw new MockError(RavenErrorCodes.GroupNotMember, "You're no longer in this group, so nothing can be sent to it.", g.Status);
        if (!_node.Running)
        {
            throw new MockError(RavenErrorCodes.NodeNotRunning,
                "Raven's background program isn't running. Turn on receiving in Settings to start it.", "node not running");
        }
        string clean = TextSanitizer.Clean(text).Trim();
        if (clean.Length == 0) throw Bad("empty text");
        if (Encoding.UTF8.GetByteCount(clean) > GroupLimits.MaxMessageBytes)
            throw new MockError(RavenErrorCodes.MessageTooLong, Loc.Get("error.messageTooLong"), $"{Encoding.UTF8.GetByteCount(clean)} > {GroupLimits.MaxMessageBytes} bytes");
        if (!g.Members.Any(m => !m.Me && m.Friend))
            throw new MockError(RavenErrorCodes.GroupNoRoute, "None of the other members is your friend, so nobody can pass it on.", "no friend among the members");

        long now = NowMs;
        GroupMessage message = NewMine(g, clean, now);
        g.Messages.Add(message);
        foreach (GroupReceipt receipt in message.Receipts) PlanCopy(g, message, receipt, now);
        return Clone(message);
    }

    /// <summary>
    /// One member's copy: a friend in touch confirms within about a second; Mina (away) waits;
    /// Rena's copy leaves and is confirmed later; a member who is not your friend is passed on
    /// by the admin and confirmed after that.
    /// </summary>
    private void PlanCopy(MockGroup g, GroupMessage message, GroupReceipt receipt, long now)
    {
        void Set(string state, long t)
        {
            if (receipt.State is GroupReceiptStates.Delivered or GroupReceiptStates.Failed) return;
            receipt.State = state;
            Count(message);
            GroupChanged(g, t);
        }

        if (receipt.PassedOn)
        {
            Schedule(now + 2400, t => Set(GroupReceiptStates.Delivered, t));
            return;
        }
        Friend? friend = _friends.FirstOrDefault(f => f.Contact.Address == receipt.Address);
        if (friend is null) return;
        switch (friend.Contact.Presence)
        {
            case Presences.InTouch:
                Schedule(now + 600, t => Set(GroupReceiptStates.Sent, t));
                Schedule(now + 1000, t => Set(GroupReceiptStates.Delivered, t));
                break;
            case Presences.Waiting:
                break; // stays waiting on this computer
            default:
                Schedule(now + 900, t => Set(GroupReceiptStates.Sent, t));
                Schedule(now + 1700, t => Set(GroupReceiptStates.Delivered, t));
                break;
        }
    }

    // ------------------------------------------------------------------ helpers

    private GroupMessage NewMine(MockGroup g, string text, long at)
    {
        var message = new GroupMessage
        {
            Id = NewGroupMessageId(),
            Group = g.Id,
            FromAddress = MyAddress,
            FromName = Loc.Get("you.title"),
            Mine = true,
            Text = text,
            AtMs = at,
        };
        foreach (GroupMember m in g.Members.Where(m => !m.Me))
        {
            message.Receipts.Add(new GroupReceipt
            {
                Address = m.Address,
                Name = m.Name,
                State = GroupReceiptStates.Waiting,
                Via = m.Friend ? "direct" : "relay",
            });
        }
        Count(message);
        return message;
    }

    private GroupMessage GroupIncoming(MockGroup g, string from, string name, string text, long at, string? viaName) => new()
    {
        Id = NewGroupMessageId(),
        Group = g.Id,
        FromAddress = from,
        FromName = name,
        Mine = false,
        Text = text,
        AtMs = at,
        ViaName = viaName,
    };

    private static void Count(GroupMessage m)
    {
        m.Total = m.Receipts.Count;
        m.Delivered = m.Receipts.Count(r => r.State == GroupReceiptStates.Delivered);
    }

    private GroupMember MeMember(bool admin) => new()
    {
        Address = MyAddress,
        ShortAddress = ShortAddress(MyAddress),
        Name = _settings.DisplayName ?? Loc.Get("you.title"),
        Label = string.Empty,
        Friend = false,
        Admin = admin,
        Me = true,
    };

    private static GroupMember MemberFor(Friend f, bool admin = false) => new()
    {
        Address = f.Contact.Address,
        ShortAddress = f.Contact.ShortAddress,
        Name = f.Contact.Name,
        Label = f.Contact.Name,
        Friend = true,
        Admin = admin,
        Me = false,
    };

    private MockGroup FindGroup(string id)
    {
        string key = id.Trim().ToLowerInvariant();
        return _groups.FirstOrDefault(g => g.Id == key)
               ?? throw new MockError(RavenErrorCodes.GroupNotFound, "That group isn't on this computer any more.", $"no group {id}");
    }

    private static void RequireAdmin(MockGroup g)
    {
        if (g.AdminAddress != MyAddress)
            throw new MockError(RavenErrorCodes.GroupNotAdmin, "Only the group's admin can add or remove members.", "not the admin");
    }

    private static MockError TooLarge(int count)
        => new(RavenErrorCodes.GroupTooLarge, $"A group has at most {GroupLimits.MaxMembers} members, you included.", $"{count} members");

    private static List<string> AddressList(JsonObject a)
    {
        if (a["members"] is not JsonArray items) throw Bad("missing argument 'members'");
        var list = new List<string>();
        foreach (JsonNode? item in items)
        {
            if (item is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
            else throw Bad("members: every item is an address");
        }
        return list;
    }

    private JsonObject GroupNode(MockGroup g)
    {
        GroupMember? admin = g.Members.FirstOrDefault(m => m.Address == g.AdminAddress);
        GroupMessage? last = g.Messages.OrderBy(m => m.AtMs).LastOrDefault();
        var node = Obj(
            ("id", g.Id),
            ("name", g.Name),
            ("short_id", g.Id[..8]),
            ("status", g.Status),
            ("admin", g.AdminAddress == MyAddress),
            ("admin_address", g.AdminAddress),
            ("admin_name", admin?.Name ?? "?"),
            ("members", g.Members.Count),
            ("unread", g.Unread));
        if (last is not null)
        {
            node["last_ms"] = last.AtMs;
            node["last_text"] = TextSanitizer.SingleLine(last.Text);
            node["last_from"] = last.Mine ? Loc.Get("you.title") : last.FromName;
        }
        return node;
    }

    private JsonObject GroupView(MockGroup g)
    {
        var members = new JsonArray();
        foreach (GroupMember m in g.Members) members.Add(ToNode(m));
        return new JsonObject { ["group"] = GroupNode(g), ["members"] = members };
    }

    private void GroupChanged(MockGroup g, long t) => Emit(EventKinds.GroupChanged, t, e => e.Group = g.Id);

    private string NewGroupMessageId() => "g" + (++_nextGroupMessage).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string NewGroupId(string name)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("raven-mock-group/" + name + "/" + Guid.NewGuid()));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    private static GroupMessage Clone(GroupMessage m) => new()
    {
        Id = m.Id, Group = m.Group, FromAddress = m.FromAddress, FromName = m.FromName, Mine = m.Mine, Text = m.Text,
        AtMs = m.AtMs, ViaName = m.ViaName, Delivered = m.Delivered, Total = m.Total,
        Receipts = m.Receipts.Select(r => new GroupReceipt { Address = r.Address, Name = r.Name, State = r.State, Via = r.Via }).ToList(),
    };
}
#endif
