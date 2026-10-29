using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Raven.Windows.Core.Engine;

// Types of the desktop API contract v1 (desktop/api/API.md, "Types").
// Enumerations are kept as strings so an unknown value from a newer engine never breaks
// parsing (unknown fields and values must be ignored, API.md "Envelope").

/// <summary>Operation names of contract v1.</summary>
public static class RavenOps
{
    public const string AppInfo = "app.info";
    public const string ProfileState = "profile.state";
    public const string IdentityCreate = "identity.create";
    public const string IdentityMe = "identity.me";
    public const string CardMake = "card.make";
    public const string CardParse = "card.parse";
    public const string ContactsList = "contacts.list";
    public const string ContactsAdd = "contacts.add";
    public const string ContactsRename = "contacts.rename";
    public const string ContactsRemove = "contacts.remove";
    public const string ContactsSetAddress = "contacts.set_address";
    public const string VerifyCheckWords = "verify.check_words";
    public const string VerifyCheckOldCode = "verify.check_old_code";
    public const string VerifyWordHint = "verify.word_hint";
    public const string MessagesHistory = "messages.history";
    public const string MessagesSend = "messages.send";
    public const string MessagesRetry = "messages.retry";
    public const string MessagesStop = "messages.stop";
    public const string MessagesDelete = "messages.delete";
    public const string MessagesDetails = "messages.details";
    public const string MessagesMarkRead = "messages.mark_read";
    public const string NodeStatus = "node.status";
    public const string NodeStart = "node.start";
    public const string NodeStop = "node.stop";
    public const string SettingsGet = "settings.get";
    public const string SettingsSet = "settings.set";
    public const string EventsPoll = "events.poll";
    public const string PrivacyState = "privacy.state";
    public const string PrivacyLock = "privacy.lock";
    public const string PrivacyUnlock = "privacy.unlock";
    public const string PrivacyEncrypt = "privacy.encrypt";
    public const string PrivacyErase = "privacy.erase";
    public const string NetworkStatus = "network.status";
    public const string NetworkSet = "network.set";
    public const string NetworkCheck = "network.check";
    public const string ContactsRoutes = "contacts.routes";
    public const string ContactsSetRoutes = "contacts.set_routes";
    public const string SecurityStatus = "security.status";
    public const string LockState = "lock.state";
    public const string LockNow = "lock.now";
    public const string LockUnlock = "lock.unlock";
    public const string UpdatesStatus = "updates.status";
    public const string GroupsList = "groups.list";
    public const string GroupsGet = "groups.get";
    public const string GroupsCreate = "groups.create";
    public const string GroupsAdd = "groups.add";
    public const string GroupsRemove = "groups.remove";
    public const string GroupsLeave = "groups.leave";
    public const string GroupsHistory = "groups.history";
    public const string GroupsSend = "groups.send";
    public const string GroupsMarkRead = "groups.mark_read";
}

/// <summary>Error codes of contract v1 ("Error codes (first set)").</summary>
public static class RavenErrorCodes
{
    public const string NotInitialized = "NOT_INITIALIZED";
    public const string ProfileUnreadable = "PROFILE_UNREADABLE";
    public const string ProfileLocked = "PROFILE_LOCKED";
    public const string KeychainDenied = "KEYCHAIN_DENIED";
    public const string NodeNotRunning = "NODE_NOT_RUNNING";
    public const string NotAContact = "NOT_A_CONTACT";
    public const string ContactBlocked = "CONTACT_BLOCKED";
    public const string NameClash = "NAME_CLASH";
    public const string CardInvalid = "CARD_INVALID";
    public const string WordsIncomplete = "WORDS_INCOMPLETE";
    public const string MessageTooLong = "MESSAGE_TOO_LONG";
    public const string NotSentMeetFirst = "NOT_SENT_MEET_FIRST";
    public const string TimeRanOut = "TIME_RAN_OUT";
    public const string NotAvailableYet = "NOT_AVAILABLE_YET";
    public const string BadRequest = "BAD_REQUEST";
    public const string Internal = "INTERNAL";
    /// <summary>network.set: a setting that needs the person's yes ("agreed").</summary>
    public const string ConsentNeeded = "CONSENT_NEEDED";
    /// <summary>lock.unlock: that Raven passphrase is not right.</summary>
    public const string PassphraseWrong = "PASSPHRASE_WRONG";
    /// <summary>lock.unlock: this profile has a Raven passphrase and none came.</summary>
    public const string PassphraseNeeded = "PASSPHRASE_NEEDED";
    /// <summary>lock.unlock: too many wrong passphrases; wait, then try again.</summary>
    public const string UnlockWait = "UNLOCK_WAIT";
    /// <summary>groups.*: that group isn't on this computer any more.</summary>
    public const string GroupNotFound = "GROUP_NOT_FOUND";
    /// <summary>groups.add / groups.remove: only the group's admin adds and removes.</summary>
    public const string GroupNotAdmin = "GROUP_NOT_ADMIN";
    /// <summary>groups.send: you left the group or were removed.</summary>
    public const string GroupNotMember = "GROUP_NOT_MEMBER";
    /// <summary>More members than a group may have (16, you included).</summary>
    public const string GroupTooLarge = "GROUP_TOO_LARGE";
    public const string GroupAdminCannotLeave = "GROUP_ADMIN_CANNOT_LEAVE";
    public const string GroupAlreadyMember = "GROUP_ALREADY_MEMBER";
    public const string GroupBadName = "GROUP_BAD_NAME";
    /// <summary>groups.send: too many group copies still wait; try again once some are delivered.</summary>
    public const string GroupQueueFull = "GROUP_QUEUE_FULL";
    /// <summary>groups.send: none of the other members is your friend.</summary>
    public const string GroupNoRoute = "GROUP_NO_ROUTE";
}

public static class ProfileStates
{
    public const string New = "new";
    public const string Ready = "ready";
    public const string Locked = "locked";
    public const string Unreadable = "unreadable";
}

public static class Presences
{
    public const string InTouch = "in_touch";
    public const string Waiting = "waiting";
    public const string None = "none";
    public const string NoAddress = "no_address";
}

public static class Directions
{
    public const string In = "in";
    public const string Out = "out";
}

public static class MessageStates
{
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Waiting = "waiting";
    public const string TimeRanOut = "time_ran_out";
    public const string NotSentMeetFirst = "not_sent_meet_first";
    public const string Failed = "failed";
}

public static class CheckResults
{
    public const string Match = "match";
    public const string Typo = "typo";
    public const string Mismatch = "mismatch";
}

public static class WhenQuitOptions
{
    public const string Keep = "keep";
    public const string StopIfStarted = "stop_if_started";
    public const string Ask = "ask";
}

public static class EventKinds
{
    public const string StatusOk = "status_ok";
    public const string SendStarted = "send_started";
    public const string Receipt = "receipt";
    public const string Incoming = "incoming";
    /// <summary>Yours, sent by another program on this profile (the terminal app, the CLI).</summary>
    public const string Outgoing = "outgoing";
    public const string Retry = "retry";
    public const string Presence = "presence";
    public const string State = "state";
    public const string Node = "node";
    /// <summary>A friend's unread count changed (a message came, or the chat was read here or in the terminal app).</summary>
    public const string Unread = "unread";
    /// <summary>A member's message came to a group ({group, message: GroupMessage}).</summary>
    public const string GroupMessage = "group_message";
    /// <summary>A group's members, status, unread count or a receipt changed, or a group appeared ({group}).</summary>
    public const string GroupChanged = "group_changed";
}

/// <summary>node.stop's "stop": what happened (API.md, "Node").</summary>
public static class NodeStopResults
{
    public const string Stopped = "stopped";
    /// <summary>Asked to stop and still answering (it may need a few more seconds).</summary>
    public const string StillRunning = "still_running";
    /// <summary>Not started by this app: "note" says how to stop it by hand.</summary>
    public const string Advice = "advice";
    /// <summary>A login service runs it and would start it again: left running.</summary>
    public const string Managed = "managed";
    public const string NotRunning = "not_running";
    public const string Failed = "failed";
}

/// <summary>Message.path_kind (API.md, "Types"): every label raven-node can report.</summary>
public static class PathKinds
{
    public const string Lan = "lan";
    public const string Internet = "internet";
    /// <summary>p2p, path not known.</summary>
    public const string P2p = "p2p";
    public const string P2pDirect = "p2p_direct";
    public const string P2pReversed = "p2p_reversed";
    public const string P2pRelayed = "p2p_relayed";
    public const string P2pHolePunched = "p2p_hole_punched";
    /// <summary>Relayed while every relay in use runs over WebSocket/TLS (port 443).</summary>
    public const string WebSocket = "websocket";
    public const string Nearby = "nearby";
    /// <summary>Friend custody ("Held by a friend"; said while it waits too).</summary>
    public const string Friend = "friend";
    /// <summary>Multi-hop custody ("Passed on between friends").</summary>
    public const string Handoff = "handoff";
    public const string Letter = "letter";
    /// <summary>The friend's Tor onion service ("via Tor"; lab, RAVEN_LAB_TOR).</summary>
    public const string Onion = "onion";
}

/// <summary>The settings network.set takes (API.md, "Network"), by their wire names.</summary>
public static class NetworkSettings
{
    public const string Lan = "lan";
    public const string LanFor = "lan_for";
    public const string Internet = "internet";
    public const string InternetListen = "internet_listen";
    public const string P2p = "p2p";
    public const string P2pListen = "p2p_listen";
    public const string Relays = "relays";
    public const string PortMapping = "port_mapping";
    public const string Proxy = "proxy";
    public const string Nearby = "nearby";
    public const string CarryForFriends = "carry_for_friends";
    public const string FriendsCarryMine = "friends_carry_mine";
    public const string Handoff = "handoff";
    public const string RelayForFriends = "relay_for_friends";
    public const string Bridge = "bridge";
    public const string Store = "store";
    public const string Relay = "relay";
    public const string HideIp = "hide_ip";
    /// <summary>The person's yes to what a setting means (CONSENT_NEEDED without it).</summary>
    public const string Agreed = "agreed";

    /// <summary>The settings the engine turns on only with "agreed": true.</summary>
    public static bool EngineNeedsConsent(string setting)
        => setting is CarryForFriends or FriendsCarryMine or Handoff;
}

/// <summary>The confirmation word privacy.erase requires (API.md).</summary>
public static class EraseConfirmation
{
    public const string Word = "erase";

    /// <summary>True when the user typed the word (spaces around it and letter case ignored).</summary>
    public static bool Matches(string? typed)
        => string.Equals(typed?.Trim(), Word, System.StringComparison.OrdinalIgnoreCase);
}

public sealed class AppInfo
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("build")] public string Build { get; set; } = "";
    [JsonPropertyName("platform")] public string Platform { get; set; } = "";
}

public sealed class ProfileState
{
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

public sealed class IdentityCreated
{
    [JsonPropertyName("address")] public string Address { get; set; } = "";
    [JsonPropertyName("words")] public List<string> Words { get; set; } = new();
    [JsonPropertyName("old_code")] public string OldCode { get; set; } = "";
}

public sealed class Me
{
    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    [JsonPropertyName("address")] public string Address { get; set; } = "";
    [JsonPropertyName("words")] public List<string> Words { get; set; } = new();
    [JsonPropertyName("old_code")] public string OldCode { get; set; } = "";
    [JsonPropertyName("card")] public string Card { get; set; } = "";
    [JsonPropertyName("invite")] public string Invite { get; set; } = "";
}

public sealed class CardMade
{
    [JsonPropertyName("card")] public string Card { get; set; } = "";
    [JsonPropertyName("invite")] public string Invite { get; set; } = "";
    [JsonPropertyName("address_included")] public string? AddressIncluded { get; set; }
}

public sealed class CardRoute
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("preview")] public bool Preview { get; set; }
}

/// <summary>Result of card.parse. It never carries the friend's security words.</summary>
public sealed class CardParsed
{
    [JsonPropertyName("short_address")] public string ShortAddress { get; set; } = "";
    [JsonPropertyName("has_address")] public bool HasAddress { get; set; }
    [JsonPropertyName("routes")] public List<CardRoute> Routes { get; set; } = new();
    /// <summary>
    /// The route kinds the card named that the engine left out (internet, p2p): no key signs a
    /// card's addresses, so release builds never read them (API.md, card.parse).
    /// </summary>
    [JsonPropertyName("ignored_routes")] public List<string>? IgnoredRoutes { get; set; }
    /// <summary>The engine's own line about the routes it left out; null when it left none out.</summary>
    [JsonPropertyName("note")] public string? Note { get; set; }
}

public sealed class WaitingInfo
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("until_ms")] public long UntilMs { get; set; }
}

public sealed class Contact
{
    [JsonPropertyName("address")] public string Address { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("short_address")] public string ShortAddress { get; set; } = "";
    [JsonPropertyName("verified")] public bool Verified { get; set; }
    [JsonPropertyName("blocked")] public bool Blocked { get; set; }
    [JsonPropertyName("presence")] public string Presence { get; set; } = Presences.None;
    [JsonPropertyName("last_contact_ms")] public long? LastContactMs { get; set; }
    [JsonPropertyName("waiting")] public WaitingInfo? Waiting { get; set; }
    [JsonPropertyName("avatar_index")] public int AvatarIndex { get; set; }
    [JsonPropertyName("lan")] public string? Lan { get; set; }
    /// <summary>
    /// The friend's messages after the one last read on this computer, in this app or the
    /// terminal app (the engine counts them; API.md, "Read and unread").
    /// </summary>
    [JsonPropertyName("unread")] public int Unread { get; set; }
}

public sealed class Message
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("address")] public string Address { get; set; } = "";
    [JsonPropertyName("direction")] public string Direction { get; set; } = Directions.Out;
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("at_ms")] public long AtMs { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = MessageStates.Sending;
    [JsonPropertyName("until_ms")] public long? UntilMs { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    /// <summary>The stable kind of Path (API.md: lan, internet, p2p_relayed, websocket, nearby, friend, handoff, letter, ...).</summary>
    [JsonPropertyName("path_kind")] public string? PathKind { get; set; }
    /// <summary>
    /// When its receipt came, given only for a delivery more than a minute after it was written
    /// (API.md): the tick then says "Delivered 15:10".
    /// </summary>
    [JsonPropertyName("delivered_at_ms")] public long? DeliveredAtMs { get; set; }
    [JsonPropertyName("attempts")] public int? Attempts { get; set; }

    [JsonIgnore] public bool IsOutgoing => Direction == Directions.Out;
}

public sealed class MessageDetails
{
    [JsonPropertyName("message")] public Message Message { get; set; } = new();
    [JsonPropertyName("status_text")] public string StatusText { get; set; } = "";
    [JsonPropertyName("why_text")] public string WhyText { get; set; } = "";
    [JsonPropertyName("action_text")] public string ActionText { get; set; } = "";
    [JsonPropertyName("detail")] public string Detail { get; set; } = "";
    /// <summary>The developer fields (ids, raven-node's own words), shown only under "Technical details".</summary>
    [JsonPropertyName("technical")] public string? Technical { get; set; }
}

public sealed class Carriers
{
    [JsonPropertyName("lan")] public string Lan { get; set; } = "on";
    [JsonPropertyName("internet")] public string Internet { get; set; } = "preview";
    [JsonPropertyName("p2p")] public string P2p { get; set; } = "preview";
    [JsonPropertyName("custody")] public string Custody { get; set; } = "preview";
    [JsonPropertyName("nearby")] public string Nearby { get; set; } = "preview";
}

public sealed class NodeStatus
{
    [JsonPropertyName("running")] public bool Running { get; set; }
    [JsonPropertyName("started_by_app")] public bool StartedByApp { get; set; }
    [JsonPropertyName("pid")] public int? Pid { get; set; }
    [JsonPropertyName("listening")] public string? Listening { get; set; }
    [JsonPropertyName("receive")] public bool Receive { get; set; }
    [JsonPropertyName("address")] public string? Address { get; set; }
    [JsonPropertyName("last_ok_ms")] public long LastOkMs { get; set; }
    [JsonPropertyName("build")] public string Build { get; set; } = "";
    [JsonPropertyName("carriers")] public Carriers Carriers { get; set; } = new();
    /// <summary>The ways a developer build's lab switch turned on (empty in a release build and the mock).</summary>
    [JsonPropertyName("lab")] public List<string> Lab { get; set; } = new();
    /// <summary>The RAVEN_LAB_* switches the engine sees (names only).</summary>
    [JsonPropertyName("lab_switches")] public List<string> LabSwitches { get; set; } = new();
    /// <summary>"listening", "local_only", "send_only", "not_running", "not_answering" or "starting".</summary>
    [JsonPropertyName("state")] public string? State { get; set; }
    /// <summary>Why a running node does not receive: "addr_not_here", "port_in_use" or "lan_off".</summary>
    [JsonPropertyName("not_receiving")] public string? NotReceiving { get; set; }
    /// <summary>node.stop only: what happened (<see cref="NodeStopResults"/>).</summary>
    [JsonPropertyName("stop")] public string? Stop { get; set; }
    /// <summary>node.stop only: how to stop it by hand ("advice"), or the reason ("failed").</summary>
    [JsonPropertyName("note")] public string? Note { get; set; }

    /// <summary>The node runs but is not listening yet (the "node (starting)" state of DESIGN.md).</summary>
    [JsonIgnore] public bool IsStarting => Running && string.IsNullOrEmpty(Listening);

    /// <summary>Port part of <see cref="Listening"/>, e.g. "7420" for "0.0.0.0:7420".</summary>
    [JsonIgnore]
    public string? ListeningPort
    {
        get
        {
            if (string.IsNullOrEmpty(Listening)) return null;
            int colon = Listening.LastIndexOf(':');
            return colon >= 0 && colon < Listening.Length - 1 ? Listening[(colon + 1)..] : null;
        }
    }

    /// <summary>True when the node only accepts connections from this computer.</summary>
    [JsonIgnore]
    public bool IsLocalOnly => Listening is not null
        && (Listening.StartsWith("127.", System.StringComparison.Ordinal) || Listening.StartsWith("[::1]", System.StringComparison.Ordinal));
}

/// <summary>
/// privacy.state: whether the privacy actions exist in this engine (false until the privacy
/// pack lands; the app then shows them with the Preview label), and their state.
/// </summary>
public sealed class PrivacyState
{
    [JsonPropertyName("available")] public bool Available { get; set; }
    [JsonPropertyName("encrypted")] public bool Encrypted { get; set; }
    [JsonPropertyName("locked")] public bool Locked { get; set; }
}

public sealed class RavenSettings
{
    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    [JsonPropertyName("receive")] public bool Receive { get; set; }
    [JsonPropertyName("reduced_motion")] public bool ReducedMotion { get; set; }
    [JsonPropertyName("when_quit")] public string WhenQuit { get; set; } = WhenQuitOptions.Ask;
    [JsonPropertyName("keep_chats_days")] public int? KeepChatsDays { get; set; }
    [JsonPropertyName("forget_addresses")] public bool ForgetAddresses { get; set; }
    [JsonPropertyName("lock_on_sleep")] public bool LockOnSleep { get; set; }
}

/// <summary>One engine event (events.poll). Fields beyond seq/kind/at_ms depend on the kind.</summary>
public sealed class RavenEvent
{
    [JsonPropertyName("seq")] public long Seq { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("at_ms")] public long AtMs { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("path_kind")] public string? PathKind { get; set; }
    [JsonPropertyName("ms")] public long? Ms { get; set; }
    /// <summary>A receipt event's: when it came, only for a late delivery (as Message.DeliveredAtMs).</summary>
    [JsonPropertyName("delivered_at_ms")] public long? DeliveredAtMs { get; set; }
    [JsonPropertyName("message")] public Message? Message { get; set; }
    [JsonPropertyName("address")] public string? Address { get; set; }
    [JsonPropertyName("attempts")] public int? Attempts { get; set; }
    [JsonPropertyName("presence")] public string? Presence { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    /// <summary>A state event's deadline (also sent when a waiting message's deadline becomes known).</summary>
    [JsonPropertyName("until_ms")] public long? UntilMs { get; set; }
    [JsonPropertyName("status")] public NodeStatus? Status { get; set; }
    /// <summary>An unread event's count.</summary>
    [JsonPropertyName("unread")] public int? Unread { get; set; }
    /// <summary>group_message and group_changed: the group's id.</summary>
    [JsonPropertyName("group")] public string? Group { get; set; }

    /// <summary>
    /// group_message: the member's message. Its "message" field is a GroupMessage, not a chat
    /// Message; <see cref="RavenApi.EventsPollAsync"/> reads it into this property.
    /// </summary>
    [JsonIgnore] public GroupMessage? GroupMessage { get; set; }

    /// <summary>Fields this version of the app does not know (kept, never used).</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class EventsPage
{
    [JsonPropertyName("seq")] public long Seq { get; set; }
    [JsonPropertyName("events")] public List<RavenEvent> Events { get; set; } = new();

    /// <summary>
    /// The engine dropped events after after_seq (its buffer is bounded), or after_seq is from
    /// before it started: reload what is on screen instead of trusting the events alone.
    /// </summary>
    [JsonPropertyName("reset")] public bool Reset { get; set; }
}

// ---- network (API.md, "Network") ------------------------------------------------------

/// <summary>A way's state in network.status.</summary>
public static class WayStates
{
    public const string On = "on";
    public const string Off = "off";
    /// <summary>Held in this build: shown with the Preview label, cannot be turned on.</summary>
    public const string Preview = "preview";
    /// <summary>On only through a developer build's lab switch.</summary>
    public const string Lab = "lab";
    public const string NotOnThisOs = "not_on_this_os";
}

/// <summary>One way to reach friends, or a protection, in network.status.</summary>
public sealed class NetworkWay
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = WayStates.Preview;
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
    [JsonPropertyName("lab")] public bool Lab { get; set; }
    [JsonPropertyName("gate")] public string Gate { get; set; } = "";
    [JsonPropertyName("lab_switches")] public List<string> LabSwitches { get; set; } = new();
    [JsonPropertyName("can_set")] public bool CanSet { get; set; }
    [JsonPropertyName("setting")] public bool? Setting { get; set; }
    [JsonPropertyName("running")] public bool? Running { get; set; }
    [JsonPropertyName("detail")] public string? Detail { get; set; }
}

public sealed class PortMapping
{
    [JsonPropertyName("setting")] public string Setting { get; set; } = "";
    [JsonPropertyName("router")] public string Router { get; set; } = "";
    [JsonPropertyName("nat_pmp_pcp")] public string NatPmpPcp { get; set; } = WayStates.Preview;
    [JsonPropertyName("v4")] public string? V4 { get; set; }
    [JsonPropertyName("v4_port")] public int? V4Port { get; set; }
    [JsonPropertyName("v6")] public string? V6 { get; set; }
}

public sealed class RelayInUse
{
    [JsonPropertyName("relay")] public string Relay { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("friend_address")] public string? FriendAddress { get; set; }
}

public sealed class ProxyState
{
    [JsonPropertyName("setting")] public string Setting { get; set; } = "auto";
    [JsonPropertyName("in_use")] public string InUse { get; set; } = "";
    [JsonPropertyName("lines")] public List<string> Lines { get; set; } = new();
}

public sealed class LabelValue
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}

public sealed class NearbyState
{
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("saved")] public bool? Saved { get; set; }
    [JsonPropertyName("on_this_os")] public bool OnThisOs { get; set; }
    [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
    [JsonPropertyName("radio")] public string? Radio { get; set; }
    [JsonPropertyName("address_privacy")] public string? AddressPrivacy { get; set; }
    [JsonPropertyName("in_range")] public int? InRange { get; set; }
    [JsonPropertyName("connected")] public int? Connected { get; set; }
    [JsonPropertyName("rows")] public List<LabelValue> Rows { get; set; } = new();
}

/// <summary>
/// The Tor onion carrier as raven-node's Status reports it (API.md, NetworkStatus "tor"): states
/// only, never an address. Present only while raven-node's Status has its Tor line.
/// </summary>
public sealed class TorState
{
    /// <summary>"ready", "publishing", "unreachable", "starting", "restarting", "blocked", "could_not_start", "not_built", "off" or "unknown".</summary>
    [JsonPropertyName("phase")] public string Phase { get; set; } = "";
    /// <summary>raven-node's line after "Tor: ".</summary>
    [JsonPropertyName("words")] public string Words { get; set; } = "";
    [JsonPropertyName("percent")] public int? Percent { get; set; }
    [JsonPropertyName("why")] public string? Why { get; set; }
    [JsonPropertyName("retry")] public string? Retry { get; set; }
    /// <summary>The onion service's outage history since raven-node started.</summary>
    [JsonPropertyName("outages")] public string? Outages { get; set; }
}

public sealed class FriendsCarryMine
{
    [JsonPropertyName("setting")] public string Setting { get; set; } = "not_decided";
    [JsonPropertyName("mail_friends")] public int MailFriends { get; set; }
    [JsonPropertyName("chosen_by_hand")] public bool ChosenByHand { get; set; }
    [JsonPropertyName("offering")] public int Offering { get; set; }
    [JsonPropertyName("offering_over_internet")] public int OfferingOverInternet { get; set; }
    [JsonPropertyName("offering_reachable")] public int OfferingReachable { get; set; }
    [JsonPropertyName("listed_by_hand")] public int ListedByHand { get; set; }
}

public sealed class CarryForFriends
{
    [JsonPropertyName("on")] public bool On { get; set; }
    [JsonPropertyName("held_messages")] public long HeldMessages { get; set; }
    [JsonPropertyName("held_bytes")] public long HeldBytes { get; set; }
    [JsonPropertyName("handoff")] public bool Handoff { get; set; }
    [JsonPropertyName("handoff_lines")] public List<string> HandoffLines { get; set; } = new();
}

public sealed class RelayForFriends
{
    [JsonPropertyName("setting")] public bool Setting { get; set; }
    [JsonPropertyName("offering")] public bool Offering { get; set; }
    [JsonPropertyName("offered_to")] public int OfferedTo { get; set; }
}

/// <summary>Who carries mail, both ways, and the relay this computer lends.</summary>
public sealed class CarryState
{
    [JsonPropertyName("friends_carry_mine")] public FriendsCarryMine FriendsCarryMine { get; set; } = new();
    [JsonPropertyName("carry_for_friends")] public CarryForFriends CarryForFriends { get; set; } = new();
    [JsonPropertyName("letters_left")] public List<string> LettersLeft { get; set; } = new();
    [JsonPropertyName("relay_for_friends")] public RelayForFriends RelayForFriends { get; set; } = new();
}

public sealed class HelpEffects
{
    [JsonPropertyName("bridge")] public string Bridge { get; set; } = "";
    [JsonPropertyName("store")] public string Store { get; set; } = "";
    [JsonPropertyName("relay")] public string Relay { get; set; } = "";
}

/// <summary>The node policy flags raven node bridge|store|relay sets, with what each really does.</summary>
public sealed class HelpFlags
{
    [JsonPropertyName("bridge")] public bool Bridge { get; set; }
    [JsonPropertyName("store")] public bool Store { get; set; }
    [JsonPropertyName("relay")] public bool Relay { get; set; }
    [JsonPropertyName("automatic")] public bool Automatic { get; set; }
    [JsonPropertyName("effects")] public HelpEffects Effects { get; set; } = new();
}

/// <summary>What the settings that need a yes mean (shown before "agreed": true).</summary>
public sealed class NetworkTexts
{
    [JsonPropertyName("carry_for_friends")] public string CarryForFriends { get; set; } = "";
    [JsonPropertyName("friends_carry_mine")] public string FriendsCarryMine { get; set; } = "";
    [JsonPropertyName("handoff")] public string Handoff { get; set; } = "";
}

/// <summary>Result of network.status.</summary>
public sealed class NetworkStatus
{
    [JsonPropertyName("build")] public string Build { get; set; } = "";
    [JsonPropertyName("node")] public string Node { get; set; } = "";
    [JsonPropertyName("carriers")] public List<NetworkWay> Carriers { get; set; } = new();
    [JsonPropertyName("protections")] public List<NetworkWay> Protections { get; set; } = new();
    [JsonPropertyName("listening")] public List<string> Listening { get; set; } = new();
    [JsonPropertyName("port")] public int Port { get; set; }
    [JsonPropertyName("internet_listen")] public string? InternetListen { get; set; }
    [JsonPropertyName("p2p_listen")] public string? P2pListen { get; set; }
    [JsonPropertyName("p2p_addresses")] public List<string> P2pAddresses { get; set; } = new();
    [JsonPropertyName("lan_until_ms")] public long? LanUntilMs { get; set; }
    [JsonPropertyName("port_mapping")] public PortMapping PortMapping { get; set; } = new();
    [JsonPropertyName("relays")] public List<RelayInUse> Relays { get; set; } = new();
    [JsonPropertyName("proxy")] public ProxyState Proxy { get; set; } = new();
    [JsonPropertyName("nearby")] public NearbyState Nearby { get; set; } = new();
    /// <summary>The Tor onion carrier's state, while raven-node reports it (null otherwise).</summary>
    [JsonPropertyName("tor")] public TorState? Tor { get; set; }
    [JsonPropertyName("carry")] public CarryState Carry { get; set; } = new();
    [JsonPropertyName("help")] public HelpFlags Help { get; set; } = new();
    [JsonPropertyName("hide_ip")] public bool HideIp { get; set; }
    [JsonPropertyName("lab_switches")] public List<string> LabSwitches { get; set; } = new();
    [JsonPropertyName("texts")] public NetworkTexts Texts { get; set; } = new();
    /// <summary>node_policy.json can't be read: the defaults apply until a change repairs it.</summary>
    [JsonPropertyName("policy_unreadable")] public bool? PolicyUnreadable { get; set; }

    /// <summary>One way (or protection) by its id, or null.</summary>
    public NetworkWay? Way(string id)
    {
        foreach (var w in Carriers) if (w.Id == id) return w;
        foreach (var w in Protections) if (w.Id == id) return w;
        return null;
    }
}

/// <summary>One change network.set made ("applies": "now" or "restart").</summary>
public sealed class NetworkChange
{
    [JsonPropertyName("setting")] public string Setting { get; set; } = "";
    [JsonPropertyName("value")] public JsonElement Value { get; set; }
    [JsonPropertyName("effect")] public string Effect { get; set; } = "";
    [JsonPropertyName("applies")] public string Applies { get; set; } = "";
    [JsonPropertyName("detail")] public List<string> Detail { get; set; } = new();
}

/// <summary>Result of network.set.</summary>
public sealed class NetworkSetResult
{
    [JsonPropertyName("changes")] public List<NetworkChange> Changes { get; set; } = new();
    [JsonPropertyName("restart_needed")] public bool RestartNeeded { get; set; }
    [JsonPropertyName("status")] public NetworkStatus Status { get; set; } = new();
}

public sealed class CheckSummaryRow
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}

public sealed class CheckRow
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("plain")] public string? Plain { get; set; }
}

/// <summary>Result of network.check (raven doctor's summary and network rows).</summary>
public sealed class NetworkCheck
{
    [JsonPropertyName("summary")] public List<CheckSummaryRow> Summary { get; set; } = new();
    [JsonPropertyName("next_step")] public string NextStep { get; set; } = "";
    [JsonPropertyName("rows")] public List<CheckRow> Rows { get; set; } = new();
    [JsonPropertyName("router")] public List<CheckRow>? Router { get; set; }
    /// <summary>The CLI's own summary and next step: shown only under Technical details.</summary>
    [JsonPropertyName("technical")] public List<string>? Technical { get; set; }
}

public sealed class RouteEntry
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("via")] public List<string> Via { get; set; } = new();
    [JsonPropertyName("next")] public string? Next { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = WayStates.Preview;
}

public sealed class LearnedRoutes
{
    [JsonPropertyName("relays")] public int Relays { get; set; }
    [JsonPropertyName("direct")] public int Direct { get; set; }
    [JsonPropertyName("mail_friends")] public int MailFriends { get; set; }
    [JsonPropertyName("peer_id")] public string? PeerId { get; set; }
    [JsonPropertyName("peer_id_proven")] public bool PeerIdProven { get; set; }
    [JsonPropertyName("lan_port")] public int? LanPort { get; set; }
    [JsonPropertyName("expires_at_ms")] public long ExpiresAtMs { get; set; }
}

/// <summary>A friend named among a contact's mail friends.</summary>
public sealed class FriendRef
{
    [JsonPropertyName("address")] public string? Address { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("takes_handoffs")] public bool? TakesHandoffs { get; set; }
    [JsonPropertyName("usable")] public bool? Usable { get; set; }
}

public sealed class Coverage
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("until")] public string Until { get; set; } = "";
}

/// <summary>
/// How long a message written now could wait while the friend is away (carriers.away): until a
/// time, or not at all (meet first). The rule raven contact list and the terminal app use.
/// FirstMessage: none can because Raven hasn't connected with the friend yet (a first message
/// needs you both online).
/// </summary>
public sealed class AwayWait
{
    [JsonPropertyName("until_ms")] public long? UntilMs { get; set; }
    [JsonPropertyName("meet_first")] public bool? MeetFirst { get; set; }
    [JsonPropertyName("first_message")] public bool? FirstMessage { get; set; }
}

public sealed class ContactCarriers
{
    [JsonPropertyName("hints")] public string Hints { get; set; } = WayStates.Preview;
    [JsonPropertyName("told")] public List<FriendRef> Told { get; set; } = new();
    [JsonPropertyName("told_unknown")] public int ToldUnknown { get; set; }
    [JsonPropertyName("listed")] public List<FriendRef> Listed { get; set; } = new();
    [JsonPropertyName("coverage")] public Coverage? Coverage { get; set; }
    [JsonPropertyName("away")] public AwayWait? Away { get; set; }
    [JsonPropertyName("receipts_stranded")] public bool ReceiptsStranded { get; set; }
}

/// <summary>
/// Where Raven reaches the friend on the local network now (lan_now), and whether the saved LAN
/// address is another, old one.
/// </summary>
public sealed class LanNow
{
    [JsonPropertyName("addr")] public string Addr { get; set; } = "";
    [JsonPropertyName("at_ms")] public long AtMs { get; set; }
    [JsonPropertyName("saved_stale")] public bool SavedStale { get; set; }
}

/// <summary>Result of contacts.routes: never the friend's words or old code.</summary>
public sealed class ContactRoutes
{
    [JsonPropertyName("address")] public string Address { get; set; } = "";
    [JsonPropertyName("verified")] public bool Verified { get; set; }
    [JsonPropertyName("hide_ip")] public bool HideIp { get; set; }
    [JsonPropertyName("routes")] public List<RouteEntry> Routes { get; set; } = new();
    [JsonPropertyName("learned")] public LearnedRoutes? Learned { get; set; }
    [JsonPropertyName("lan_now")] public LanNow? LanNow { get; set; }
    [JsonPropertyName("carriers")] public ContactCarriers Carriers { get; set; } = new();
}

/// <summary>Result of contacts.set_routes.</summary>
public sealed class SetRoutesResult
{
    [JsonPropertyName("contact")] public Contact Contact { get; set; } = new();
    [JsonPropertyName("routes")] public ContactRoutes Routes { get; set; } = new();
}

public sealed class SecurityNotice
{
    [JsonPropertyName("id")] public long Id { get; set; }
    /// <summary>"alert" or "warning".</summary>
    [JsonPropertyName("level")] public string Level { get; set; } = "";
    [JsonPropertyName("address")] public string? Address { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("headline")] public string Headline { get; set; } = "";
    [JsonPropertyName("what_raven_did")] public string WhatRavenDid { get; set; } = "";
    [JsonPropertyName("what_to_do")] public string WhatToDo { get; set; } = "";
    [JsonPropertyName("when")] public string When { get; set; } = "";
    [JsonPropertyName("details")] public List<LabelValue> Details { get; set; } = new();
}

/// <summary>Result of security.status (raven security, read-only).</summary>
public sealed class SecurityStatus
{
    [JsonPropertyName("available")] public bool Available { get; set; }
    [JsonPropertyName("lab")] public bool Lab { get; set; }
    /// <summary>"off", "unsealed" or "live".</summary>
    [JsonPropertyName("kept")] public string Kept { get; set; } = "off";
    [JsonPropertyName("notices")] public List<SecurityNotice> Notices { get; set; } = new();
    [JsonPropertyName("counted")] public int Counted { get; set; }
    /// <summary>What the running background program counted (missing when it doesn't answer).</summary>
    [JsonPropertyName("node")] public SecurityNodeCounts? Node { get; set; }
}

/// <summary>security.status "node": the running background program's counts.</summary>
public sealed class SecurityNodeCounts
{
    [JsonPropertyName("strangers_refused_1h")] public long StrangersRefused1h { get; set; }
    [JsonPropertyName("failed_handshakes_1h")] public long FailedHandshakes1h { get; set; }
    [JsonPropertyName("bans_active")] public long BansActive { get; set; }
    [JsonPropertyName("link_tag_failures_24h")] public long LinkTagFailures24h { get; set; }
    [JsonPropertyName("alerts_open")] public long AlertsOpen { get; set; }
}

/// <summary>Result of lock.state, lock.now and lock.unlock (Lock v2).</summary>
public sealed class LockState
{
    [JsonPropertyName("available")] public bool Available { get; set; }
    [JsonPropertyName("lab")] public bool Lab { get; set; }
    [JsonPropertyName("supported")] public bool Supported { get; set; }
    [JsonPropertyName("locked")] public bool Locked { get; set; }
    [JsonPropertyName("has_passphrase")] public bool? HasPassphrase { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }
}

/// <summary>Result of updates.status (raven update status, read-only; installing stays in the CLI).</summary>
public sealed class UpdatesStatus
{
    [JsonPropertyName("available")] public bool Available { get; set; }
    [JsonPropertyName("bundles")] public bool Bundles { get; set; }
    [JsonPropertyName("from_friends")] public bool FromFriends { get; set; }
    [JsonPropertyName("lab")] public bool Lab { get; set; }
    [JsonPropertyName("lines")] public List<string> Lines { get; set; } = new();
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("offer")] public List<string>? Offer { get; set; }
    [JsonPropertyName("install")] public string Install { get; set; } = "cli_only";
    [JsonPropertyName("install_text")] public string InstallText { get; set; } = "";
}

// ---- groups (API.md, "Groups") ---------------------------------------------------------

/// <summary>Group.status.</summary>
public static class GroupStatuses
{
    public const string Active = "active";
    /// <summary>You left (groups.leave): the history stays, nothing more is sent.</summary>
    public const string Left = "left";
    /// <summary>The admin removed you: the history stays, nothing more is sent.</summary>
    public const string Removed = "removed";
}

/// <summary>A member's copy of a message of yours (GroupMessage.receipts[].state).</summary>
public static class GroupReceiptStates
{
    /// <summary>Still on this computer, or passed on by another member and not confirmed yet.</summary>
    public const string Waiting = "waiting";
    /// <summary>Left this computer, no receipt yet.</summary>
    public const string Sent = "sent";
    /// <summary>That member's Raven confirmed it (a signed receipt).</summary>
    public const string Delivered = "delivered";
    /// <summary>Given up.</summary>
    public const string Failed = "failed";
}

/// <summary>The limits of groups v1 (API.md, "Groups"; docs/design/2026-10-groups.md).</summary>
public static class GroupLimits
{
    /// <summary>Members of one group, you included (owner decision 2026-10-28: 16, not the design's 32).</summary>
    public const int MaxMembers = 16;
    /// <summary>A group's name, in characters.</summary>
    public const int MaxNameChars = 64;
    /// <summary>One message's text, in UTF-8 bytes.</summary>
    public const int MaxMessageBytes = 8192;
}

/// <summary>One group (groups.list, groups.get).</summary>
public sealed class Group
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("short_id")] public string ShortId { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = GroupStatuses.Active;
    /// <summary>You are its admin (its creator): you alone add and remove members.</summary>
    [JsonPropertyName("admin")] public bool Admin { get; set; }
    [JsonPropertyName("admin_address")] public string? AdminAddress { get; set; }
    [JsonPropertyName("admin_name")] public string? AdminName { get; set; }
    /// <summary>How many members, you included.</summary>
    [JsonPropertyName("members")] public int Members { get; set; }
    /// <summary>Others' messages received after the group was last read on this computer.</summary>
    [JsonPropertyName("unread")] public int Unread { get; set; }
    [JsonPropertyName("last_ms")] public long? LastMs { get; set; }
    [JsonPropertyName("last_text")] public string? LastText { get; set; }
    /// <summary>The newest message's author (a name; "You" for yours).</summary>
    [JsonPropertyName("last_from")] public string? LastFrom { get; set; }

    [JsonIgnore] public bool IsActive => Status == GroupStatuses.Active;
}

/// <summary>One member of a group (groups.get, groups.create, groups.add, groups.remove).</summary>
public sealed class GroupMember
{
    [JsonPropertyName("address")] public string Address { get; set; } = "";
    [JsonPropertyName("short_address")] public string ShortAddress { get; set; } = "";
    /// <summary>Your name for a member who is your friend, else the name the admin gave them.</summary>
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    /// <summary>The member is one of your friends (false: "not your friend"; never added to your friends by the group).</summary>
    [JsonPropertyName("friend")] public bool Friend { get; set; }
    [JsonPropertyName("admin")] public bool Admin { get; set; }
    /// <summary>This is you.</summary>
    [JsonPropertyName("me")] public bool Me { get; set; }
}

/// <summary>One member's copy of a message of yours.</summary>
public sealed class GroupReceipt
{
    [JsonPropertyName("address")] public string Address { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = GroupReceiptStates.Waiting;
    /// <summary>"direct", or "relay" for a member reached through another member.</summary>
    [JsonPropertyName("via")] public string Via { get; set; } = "direct";

    [JsonIgnore] public bool PassedOn => Via == "relay";
}

/// <summary>One group message (groups.history, groups.send, the group_message event).</summary>
public sealed class GroupMessage
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("group")] public string Group { get; set; } = "";
    [JsonPropertyName("from_address")] public string FromAddress { get; set; } = "";
    [JsonPropertyName("from_name")] public string FromName { get; set; } = "";
    [JsonPropertyName("mine")] public bool Mine { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("at_ms")] public long AtMs { get; set; }
    /// <summary>The member that passed it on, when it was not its author.</summary>
    [JsonPropertyName("via_name")] public string? ViaName { get; set; }
    [JsonPropertyName("delivered")] public int Delivered { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
    /// <summary>One entry per member it was for (only on yours).</summary>
    [JsonPropertyName("receipts")] public List<GroupReceipt> Receipts { get; set; } = new();
}

/// <summary>groups.list.</summary>
public sealed class GroupsList
{
    /// <summary>False in a release build: the apps then show no Groups section.</summary>
    [JsonPropertyName("available")] public bool Available { get; set; }
    [JsonPropertyName("groups")] public List<Group> Groups { get; set; } = new();
}

/// <summary>groups.get, groups.create, groups.add and groups.remove: the group and its members.</summary>
public sealed class GroupView
{
    [JsonPropertyName("group")] public Group Group { get; set; } = new();
    [JsonPropertyName("members")] public List<GroupMember> Members { get; set; } = new();
}

/// <summary>groups.send: the message (it returns at once) and why it could not be asked to go now, if so.</summary>
public sealed class GroupSent
{
    [JsonPropertyName("message")] public GroupMessage Message { get; set; } = new();
    [JsonPropertyName("note")] public string? Note { get; set; }
}
