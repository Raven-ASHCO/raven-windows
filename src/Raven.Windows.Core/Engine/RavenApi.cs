using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Raven.Windows.Core.Engine;

/// <summary>
/// Typed wrappers over <see cref="IRavenEngine.CallAsync"/>, one per operation of
/// desktop/api/API.md. They only shape arguments and results; all logic stays in the engine.
/// </summary>
public static class RavenApi
{
    /// <summary>Calls <paramref name="op"/> and deserialises the result into <typeparamref name="T"/>.</summary>
    public static async Task<T> CallAsync<T>(this IRavenEngine engine, string op, JsonObject? args = null, CancellationToken ct = default)
        where T : class, new()
    {
        JsonObject result = await engine.CallAsync(op, args, ct).ConfigureAwait(false);
        try
        {
            return result.Deserialize<T>(RavenJson.Options) ?? new T();
        }
        catch (JsonException ex)
        {
            throw RavenException.Protocol($"{op}: result has an unexpected shape ({ex.Message})", ex);
        }
    }

    // ---- app and profile ------------------------------------------------------------

    public static Task<AppInfo> AppInfoAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<AppInfo>(RavenOps.AppInfo, null, ct);

    public static Task<ProfileState> ProfileStateAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<ProfileState>(RavenOps.ProfileState, null, ct);

    public static Task<IdentityCreated> IdentityCreateAsync(this IRavenEngine e, string? displayName, CancellationToken ct = default)
    {
        var args = new JsonObject();
        if (!string.IsNullOrWhiteSpace(displayName)) args["display_name"] = displayName.Trim();
        return e.CallAsync<IdentityCreated>(RavenOps.IdentityCreate, args, ct);
    }

    public static Task<Me> IdentityMeAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<Me>(RavenOps.IdentityMe, null, ct);

    // ---- cards ----------------------------------------------------------------------

    public static Task<CardMade> CardMakeAsync(this IRavenEngine e, bool includeAddress, CancellationToken ct = default)
        => e.CallAsync<CardMade>(RavenOps.CardMake, new JsonObject { ["include_address"] = includeAddress }, ct);

    /// <summary>Reads a friend's invite or card. Never returns the friend's security words.</summary>
    public static Task<CardParsed> CardParseAsync(this IRavenEngine e, string text, CancellationToken ct = default)
        => e.CallAsync<CardParsed>(RavenOps.CardParse, new JsonObject { ["text"] = text }, ct);

    // ---- contacts -------------------------------------------------------------------

    public static async Task<IReadOnlyList<Contact>> ContactsListAsync(this IRavenEngine e, CancellationToken ct = default)
        => (await e.CallAsync<ContactsResult>(RavenOps.ContactsList, null, ct).ConfigureAwait(false)).Contacts;

    /// <summary>Adds a friend from an invite or card; the friend is saved as not verified.</summary>
    public static async Task<Contact> ContactsAddAsync(this IRavenEngine e, string text, string name, CancellationToken ct = default)
        => (await e.CallAsync<ContactResult>(RavenOps.ContactsAdd, new JsonObject { ["text"] = text, ["name"] = name }, ct).ConfigureAwait(false)).Contact;

    public static async Task<Contact> ContactsRenameAsync(this IRavenEngine e, string address, string name, CancellationToken ct = default)
        => (await e.CallAsync<ContactResult>(RavenOps.ContactsRename, new JsonObject { ["address"] = address, ["name"] = name }, ct).ConfigureAwait(false)).Contact;

    public static async Task ContactsRemoveAsync(this IRavenEngine e, string address, CancellationToken ct = default)
        => await e.CallAsync(RavenOps.ContactsRemove, new JsonObject { ["address"] = address }, ct).ConfigureAwait(false);

    public static async Task<Contact> ContactsSetAddressAsync(this IRavenEngine e, string address, string lan, CancellationToken ct = default)
        => (await e.CallAsync<ContactResult>(RavenOps.ContactsSetAddress, new JsonObject { ["address"] = address, ["lan"] = lan }, ct).ConfigureAwait(false)).Contact;

    // ---- verification ---------------------------------------------------------------

    /// <summary>
    /// Compares the 10 words a friend read aloud. Returns "match", "typo" or "mismatch"
    /// (<see cref="CheckResults"/>); the friend's words are never returned.
    /// </summary>
    public static async Task<string> VerifyCheckWordsAsync(this IRavenEngine e, string address, IReadOnlyList<string> words, CancellationToken ct = default)
    {
        var list = new JsonArray(words.Select(w => (JsonNode?)JsonValue.Create(w)).ToArray());
        var args = new JsonObject { ["address"] = address, ["words"] = list };
        return (await e.CallAsync<CheckWordsResult>(RavenOps.VerifyCheckWords, args, ct).ConfigureAwait(false)).Result;
    }

    /// <summary>
    /// Compares the old letters-and-digits code a friend with an older Raven read aloud.
    /// Returns "match" or "mismatch" (<see cref="CheckResults"/>); on a match the engine
    /// saves the friend as verified, exactly as with the words.
    /// </summary>
    public static async Task<string> VerifyCheckOldCodeAsync(this IRavenEngine e, string address, string code, CancellationToken ct = default)
    {
        var args = new JsonObject { ["address"] = address, ["code"] = code };
        return (await e.CallAsync<CheckWordsResult>(RavenOps.VerifyCheckOldCode, args, ct).ConfigureAwait(false)).Result;
    }

    /// <summary>Completes a word from Raven's list after 3 letters; null when there is no single word.</summary>
    public static async Task<string?> VerifyWordHintAsync(this IRavenEngine e, string prefix, CancellationToken ct = default)
        => (await e.CallAsync<WordHintResult>(RavenOps.VerifyWordHint, new JsonObject { ["prefix"] = prefix }, ct).ConfigureAwait(false)).Word;

    // ---- messages -------------------------------------------------------------------

    public static async Task<IReadOnlyList<Message>> MessagesHistoryAsync(this IRavenEngine e, string address, string? before, int limit, CancellationToken ct = default)
    {
        var args = new JsonObject { ["address"] = address, ["limit"] = limit };
        if (!string.IsNullOrEmpty(before)) args["before"] = before;
        return (await e.CallAsync<MessagesResult>(RavenOps.MessagesHistory, args, ct).ConfigureAwait(false)).Messages;
    }

    public static async Task<Message> MessagesSendAsync(this IRavenEngine e, string address, string text, CancellationToken ct = default)
        => (await e.CallAsync<MessageResult>(RavenOps.MessagesSend, new JsonObject { ["address"] = address, ["text"] = text }, ct).ConfigureAwait(false)).Message;

    public static async Task<Message> MessagesRetryAsync(this IRavenEngine e, string id, CancellationToken ct = default)
        => (await e.CallAsync<MessageResult>(RavenOps.MessagesRetry, new JsonObject { ["id"] = id }, ct).ConfigureAwait(false)).Message;

    public static async Task<Message> MessagesStopAsync(this IRavenEngine e, string id, CancellationToken ct = default)
        => (await e.CallAsync<MessageResult>(RavenOps.MessagesStop, new JsonObject { ["id"] = id }, ct).ConfigureAwait(false)).Message;

    /// <summary>messages.delete: an ended message of yours (not sent, failed, time ran out) leaves the chat for good.</summary>
    public static async Task MessagesDeleteAsync(this IRavenEngine e, string id, CancellationToken ct = default)
        => await e.CallAsync(RavenOps.MessagesDelete, new JsonObject { ["id"] = id }, ct).ConfigureAwait(false);

    /// <summary>
    /// messages.mark_read: the friend's chat is read up to <paramref name="upTo"/> (default its
    /// newest message), here and in the terminal app; never moved back. Returns the friend's
    /// unread count afterwards.
    /// </summary>
    public static async Task<int> MessagesMarkReadAsync(this IRavenEngine e, string address, string? upTo = null, CancellationToken ct = default)
    {
        var args = new JsonObject { ["address"] = address };
        if (!string.IsNullOrEmpty(upTo)) args["id"] = upTo;
        return (await e.CallAsync<MarkReadResult>(RavenOps.MessagesMarkRead, args, ct).ConfigureAwait(false)).Unread;
    }

    public static Task<MessageDetails> MessagesDetailsAsync(this IRavenEngine e, string id, CancellationToken ct = default)
        => e.CallAsync<MessageDetails>(RavenOps.MessagesDetails, new JsonObject { ["id"] = id }, ct);

    // ---- node -----------------------------------------------------------------------

    public static Task<NodeStatus> NodeStatusAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<NodeStatus>(RavenOps.NodeStatus, null, ct);

    public static Task<NodeStatus> NodeStartAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<NodeStatus>(RavenOps.NodeStart, null, ct);

    public static Task<NodeStatus> NodeStopAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<NodeStatus>(RavenOps.NodeStop, null, ct);

    // ---- settings -------------------------------------------------------------------

    public static Task<RavenSettings> SettingsGetAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<RavenSettings>(RavenOps.SettingsGet, null, ct);

    /// <summary>
    /// Changes some settings. Only the fields present in <paramref name="partial"/> change;
    /// <c>"keep_chats_days": null</c> means "Always".
    /// </summary>
    public static Task<RavenSettings> SettingsSetAsync(this IRavenEngine e, JsonObject partial, CancellationToken ct = default)
        => e.CallAsync<RavenSettings>(RavenOps.SettingsSet, partial, ct);

    // ---- events and privacy ----------------------------------------------------------

    /// <summary>
    /// events.poll. A group_message event's "message" is a GroupMessage, which the typed
    /// <see cref="RavenEvent.Message"/> (a chat message) can't hold: it is read into
    /// <see cref="RavenEvent.GroupMessage"/> here.
    /// </summary>
    public static async Task<EventsPage> EventsPollAsync(this IRavenEngine e, long afterSeq, CancellationToken ct = default)
    {
        JsonObject result = await e.CallAsync(RavenOps.EventsPoll, new JsonObject { ["after_seq"] = afterSeq }, ct).ConfigureAwait(false);
        try
        {
            EventsPage page = result.Deserialize<EventsPage>(RavenJson.Options) ?? new EventsPage();
            if (result["events"] is JsonArray raw)
            {
                for (int i = 0; i < page.Events.Count && i < raw.Count; i++)
                {
                    RavenEvent ev = page.Events[i];
                    if (ev.Kind != EventKinds.GroupMessage) continue;
                    ev.Message = null; // not a chat message
                    if (raw[i] is JsonObject o && o["message"] is JsonObject m)
                        ev.GroupMessage = m.Deserialize<GroupMessage>(RavenJson.Options);
                }
            }
            return page;
        }
        catch (JsonException ex)
        {
            throw RavenException.Protocol($"{RavenOps.EventsPoll}: result has an unexpected shape ({ex.Message})", ex);
        }
    }

    /// <summary>Whether the privacy actions exist yet (available) and their state.</summary>
    public static Task<PrivacyState> PrivacyStateAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<PrivacyState>(RavenOps.PrivacyState, null, ct);

    /// <summary>NOT_AVAILABLE_YET until the privacy pack lands.</summary>
    public static Task<ProfileState> PrivacyLockAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<ProfileState>(RavenOps.PrivacyLock, null, ct);

    /// <summary>NOT_AVAILABLE_YET until the privacy pack lands.</summary>
    public static Task<ProfileState> PrivacyUnlockAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<ProfileState>(RavenOps.PrivacyUnlock, null, ct);

    /// <summary>Encrypts everything Raven stores on this computer (NOT_AVAILABLE_YET until the privacy pack lands).</summary>
    public static Task<ProfileState> PrivacyEncryptAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<ProfileState>(RavenOps.PrivacyEncrypt, null, ct);

    /// <summary>
    /// Removes Raven's data from this computer; afterwards profile.state is "new"
    /// (NOT_AVAILABLE_YET until the privacy pack lands). The app asks the user to type
    /// "erase" first; the engine checks the same word.
    /// </summary>
    public static async Task PrivacyEraseAsync(this IRavenEngine e, CancellationToken ct = default)
        => await e.CallAsync(RavenOps.PrivacyErase, new JsonObject { ["confirm"] = EraseConfirmation.Word }, ct).ConfigureAwait(false);

    // ---- network (API.md, "Network") --------------------------------------------------

    /// <summary>Every way to reach friends and what the background program does.</summary>
    public static Task<NetworkStatus> NetworkStatusAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<NetworkStatus>(RavenOps.NetworkStatus, null, ct);

    /// <summary>
    /// Changes network settings; <paramref name="partial"/> uses the wire names, e.g.
    /// <c>{"store": false}</c> or <c>{"carry_for_friends": true, "agreed": true}</c>. A held way
    /// turned on is NOT_AVAILABLE_YET; a setting without the person's yes is CONSENT_NEEDED.
    /// </summary>
    public static Task<NetworkSetResult> NetworkSetAsync(this IRavenEngine e, JsonObject partial, CancellationToken ct = default)
        => e.CallAsync<NetworkSetResult>(RavenOps.NetworkSet, partial, ct);

    /// <summary>raven doctor's summary and network rows; router also asks the router (read-only, about 4 s).</summary>
    public static Task<NetworkCheck> NetworkCheckAsync(this IRavenEngine e, bool router = false, CancellationToken ct = default)
        => e.CallAsync<NetworkCheck>(RavenOps.NetworkCheck, new JsonObject { ["router"] = router }, ct);

    public static Task<ContactRoutes> ContactsRoutesAsync(this IRavenEngine e, string address, CancellationToken ct = default)
        => e.CallAsync<ContactRoutes>(RavenOps.ContactsRoutes, new JsonObject { ["address"] = address }, ct);

    /// <summary>
    /// Sets one friend's routes; <paramref name="changes"/> uses the wire names (internet, p2p,
    /// via, clear, custodians, hide_ip).
    /// </summary>
    public static Task<SetRoutesResult> ContactsSetRoutesAsync(this IRavenEngine e, string address, JsonObject changes, CancellationToken ct = default)
    {
        var args = new JsonObject { ["address"] = address };
        foreach (var kv in changes) args[kv.Key] = kv.Value?.DeepClone();
        return e.CallAsync<SetRoutesResult>(RavenOps.ContactsSetRoutes, args, ct);
    }

    public static Task<SecurityStatus> SecurityStatusAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<SecurityStatus>(RavenOps.SecurityStatus, null, ct);

    public static Task<LockState> LockStateAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<LockState>(RavenOps.LockState, null, ct);

    /// <summary>Lock v2: the background program keeps running without its keys.</summary>
    public static Task<LockState> LockNowAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<LockState>(RavenOps.LockNow, null, ct);

    /// <summary>Lock v2's unlock; the passphrase goes only to the background program.</summary>
    public static Task<LockState> LockUnlockAsync(this IRavenEngine e, string? passphrase = null, CancellationToken ct = default)
    {
        var args = new JsonObject();
        if (passphrase is not null) args["passphrase"] = passphrase;
        return e.CallAsync<LockState>(RavenOps.LockUnlock, args, ct);
    }

    /// <summary>raven update status; installing stays in the CLI.</summary>
    public static Task<UpdatesStatus> UpdatesStatusAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<UpdatesStatus>(RavenOps.UpdatesStatus, null, ct);

    // ---- groups (API.md, "Groups") ----------------------------------------------------

    /// <summary>groups.list: available is false in a release build (then no Groups section shows).</summary>
    public static Task<GroupsList> GroupsListAsync(this IRavenEngine e, CancellationToken ct = default)
        => e.CallAsync<GroupsList>(RavenOps.GroupsList, null, ct);

    public static Task<GroupView> GroupsGetAsync(this IRavenEngine e, string group, CancellationToken ct = default)
        => e.CallAsync<GroupView>(RavenOps.GroupsGet, new JsonObject { ["group"] = group }, ct);

    /// <summary>Creates a group with friends (you are its admin).</summary>
    public static Task<GroupView> GroupsCreateAsync(this IRavenEngine e, string name, IReadOnlyList<string> members, CancellationToken ct = default)
        => e.CallAsync<GroupView>(RavenOps.GroupsCreate, new JsonObject { ["name"] = name, ["members"] = Addresses(members) }, ct);

    /// <summary>Adds friends to a group (admin only).</summary>
    public static Task<GroupView> GroupsAddAsync(this IRavenEngine e, string group, IReadOnlyList<string> members, CancellationToken ct = default)
        => e.CallAsync<GroupView>(RavenOps.GroupsAdd, new JsonObject { ["group"] = group, ["members"] = Addresses(members) }, ct);

    /// <summary>Removes one member (admin only).</summary>
    public static Task<GroupView> GroupsRemoveAsync(this IRavenEngine e, string group, string member, CancellationToken ct = default)
        => e.CallAsync<GroupView>(RavenOps.GroupsRemove, new JsonObject { ["group"] = group, ["member"] = member }, ct);

    /// <summary>Leaves a group (not the admin); its history stays on this computer.</summary>
    public static async Task<Group> GroupsLeaveAsync(this IRavenEngine e, string group, CancellationToken ct = default)
        => (await e.CallAsync<GroupResult>(RavenOps.GroupsLeave, new JsonObject { ["group"] = group }, ct).ConfigureAwait(false)).Group;

    /// <summary>The group's messages, oldest first.</summary>
    public static async Task<IReadOnlyList<GroupMessage>> GroupsHistoryAsync(this IRavenEngine e, string group, string? before, int limit, CancellationToken ct = default)
    {
        var args = new JsonObject { ["group"] = group, ["limit"] = limit };
        if (!string.IsNullOrEmpty(before)) args["before"] = before;
        return (await e.CallAsync<GroupMessagesResult>(RavenOps.GroupsHistory, args, ct).ConfigureAwait(false)).Messages;
    }

    /// <summary>Sends one message to the group; it returns at once and each copy goes out by itself.</summary>
    public static Task<GroupSent> GroupsSendAsync(this IRavenEngine e, string group, string text, CancellationToken ct = default)
        => e.CallAsync<GroupSent>(RavenOps.GroupsSend, new JsonObject { ["group"] = group, ["text"] = text }, ct);

    /// <summary>The group counts as read on this computer.</summary>
    public static async Task<int> GroupsMarkReadAsync(this IRavenEngine e, string group, CancellationToken ct = default)
        => (await e.CallAsync<MarkReadResult>(RavenOps.GroupsMarkRead, new JsonObject { ["group"] = group }, ct).ConfigureAwait(false)).Unread;

    private static JsonArray Addresses(IReadOnlyList<string> members)
        => new(members.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray());

    // ---- result wrappers --------------------------------------------------------------

    private sealed class GroupResult
    {
        [JsonPropertyName("group")] public Group Group { get; set; } = new();
    }

    private sealed class GroupMessagesResult
    {
        [JsonPropertyName("messages")] public List<GroupMessage> Messages { get; set; } = new();
    }

    private sealed class ContactsResult
    {
        [JsonPropertyName("contacts")] public List<Contact> Contacts { get; set; } = new();
    }

    private sealed class ContactResult
    {
        [JsonPropertyName("contact")] public Contact Contact { get; set; } = new();
    }

    private sealed class MessagesResult
    {
        [JsonPropertyName("messages")] public List<Message> Messages { get; set; } = new();
    }

    private sealed class MessageResult
    {
        [JsonPropertyName("message")] public Message Message { get; set; } = new();
    }

    private sealed class MarkReadResult
    {
        [JsonPropertyName("unread")] public int Unread { get; set; }
    }

    private sealed class CheckWordsResult
    {
        [JsonPropertyName("result")] public string Result { get; set; } = "";
    }

    private sealed class WordHintResult
    {
        [JsonPropertyName("word")] public string? Word { get; set; }
    }
}
