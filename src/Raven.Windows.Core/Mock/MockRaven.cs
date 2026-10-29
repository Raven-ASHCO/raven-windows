#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Core.Words;

namespace Raven.Windows.Core.Mock;

/// <summary>
/// In-memory engine that follows desktop/api/API.md, so the app can be built and reviewed
/// before raven-client exists. DEBUG builds only.
/// <para>
/// Demo data: Ali (verified, in touch), Mina (verified, one waiting message), Rena (not
/// verified; his old-style code is <see cref="RenaOldCode"/> for "Use the old code instead");
/// your security words are the design's demo words. After the first
/// events.poll it replays the demo events of desktop/design/mockups/desktop-concept.html
/// once: Ali writes, Rena writes, Raven tries Mina again, Mina comes back and her waiting
/// message is delivered. A status check (status_ok) happens every 2 s while the node runs.
/// Groups are lab only, as in the engine (MockRaven.Groups.cs).
/// </para>
/// <para>
/// Every request goes through the real JSON envelope (<see cref="Handle"/> is JSON in, JSON
/// out, like raven_call), so the app's parsing is exercised exactly as with the engine.
/// Error messages use the engine's wording from strings.en.json ("error.*"); a few cases
/// only the mock has keep a mock text. The privacy pack is not there yet: privacy.state
/// says available = false and lock, unlock, encrypt and erase answer NOT_AVAILABLE_YET.
/// </para>
/// </summary>
public sealed partial class MockRaven : IRavenEngine
{
    public const string MyAddress = "rvn1qysluvwl5922yctzd0u9gpr06gn3k7ldfvecule0";
    public const string MyPublicKeyHex = "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a";
    public const string MyOldCode = "If4x-36FU-omFi";
    public const string AliAddress = "rvn1qd7w39xa2ydg8javvjh4waakpjhkkcjvc";
    public const string MinaAddress = "rvn1qt9aey2hj6th5qmt0e8w6h9msvl7p5p4c";
    public const string RenaAddress = "rvn1qs4zdsm6a9pur3t6llmac0czqdm0unyun";

    /// <summary>Rena's old letters-and-digits code (the same demo value in all three apps' mocks).</summary>
    public const string RenaOldCode = "Tq7m-29Xa-hKe4";
    public const string DemoLanAddress = "192.168.1.20";
    public const int DemoPort = 7420;
    public const string LanPath = "LAN direct";
    /// <summary>The stable kind of <see cref="LanPath"/> (API.md, Message path_kind).</summary>
    public const string LanKind = "lan";
    public const int MaxMessageLength = 4000;

    private const long Second = 1000;
    private const long Minute = 60 * Second;
    private const long Hour = 60 * Minute;

    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly bool _demoEvents;
    private readonly List<Friend> _friends = new();
    private readonly List<RavenEvent> _events = new();
    private readonly List<Scheduled> _scheduled = new();
    private readonly RavenSettings _settings;
    private readonly NodeStatus _node;
    private readonly byte[] _myPublicKey = Convert.FromHexString(MyPublicKeyHex);

    private string _profile;
    private long _seq;
    private long _nextId;
    private long _scheduleOrder;
    private long _lastStatusOkMs;
    private bool _demoScheduled;


    /// <param name="time">Clock (tests pass a manual one).</param>
    /// <param name="startReady">Start with an existing identity instead of the first-run flow.</param>
    /// <param name="demoEvents">Replay the demo events after the first events.poll.</param>
    /// <param name="startLocked">
    /// Start with an existing identity that is locked, to review the Locked screen (Unlock then
    /// answers NOT_AVAILABLE_YET, as privacy.lock does, until the privacy pack lands).
    /// </param>
    /// <param name="labNetwork">
    /// Answer the network ops as a developer build with lab switches set
    /// (<see cref="LabSwitches"/>) instead of a release build: those ways can be switched on,
    /// the settings that help friends ask for "agreed", Lock v2 and security notices work.
    /// </param>
    public MockRaven(TimeProvider? time = null, bool startReady = false, bool demoEvents = true, bool startLocked = false, bool labNetwork = false)
    {
        _time = time ?? TimeProvider.System;
        _demoEvents = demoEvents;
        _lab = labNetwork;
        _profile = startLocked ? ProfileStates.Locked : startReady ? ProfileStates.Ready : ProfileStates.New;
        startReady |= startLocked;
        long now = NowMs;

        _settings = new RavenSettings
        {
            DisplayName = null,
            Receive = startReady,
            ReducedMotion = false,
            WhenQuit = WhenQuitOptions.Ask,
            KeepChatsDays = 30,
            ForgetAddresses = true,
            LockOnSleep = false,
        };

        _node = new NodeStatus
        {
            Running = startReady,
            StartedByApp = false,
            Pid = startReady ? 4242 : null,
            Listening = startReady ? $"0.0.0.0:{DemoPort}" : null,
            Receive = startReady,
            Address = DemoLanAddress,
            LastOkMs = startReady ? now : 0,
            Build = "dev",
            Carriers = new Carriers { Lan = "on", Internet = "preview", P2p = "preview", Custody = "preview", Nearby = "preview" },
        };
        if (_lab)
        {
            _node.Carriers = new Carriers { Lan = "on", Internet = "on", P2p = "on", Custody = "on", Nearby = "on" };
            _node.Lab.AddRange(LabWays());
            _node.LabSwitches.AddRange(LabSwitches);
        }

        SeedDemoFriends(now);
    }

    private long NowMs => _time.GetUtcNow().ToUnixTimeMilliseconds();

    /// <inheritdoc />
    public Task<JsonObject> CallAsync(string op, JsonObject? args = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            string response = Handle(RavenEnvelope.BuildRequest(op, args));
            return Task.FromResult(RavenEnvelope.ParseResponse(response));
        }
        catch (Exception ex)
        {
            return Task.FromException<JsonObject>(ex);
        }
    }

    /// <summary>JSON request in, JSON response out: the same shape as raven_call().</summary>
    public string Handle(string requestJson)
    {
        lock (_gate)
        {
            try
            {
                if (JsonNode.Parse(requestJson) is not JsonObject request) throw Bad("request is not a JSON object");
                if (request["v"] is not JsonValue v || !v.TryGetValue(out int version) || version != RavenEnvelope.Version)
                    throw Bad("unsupported contract version");
                if (request["op"] is not JsonValue opValue || !opValue.TryGetValue(out string? op) || string.IsNullOrEmpty(op))
                    throw Bad("missing op");
                JsonObject args = request["args"] as JsonObject ?? new JsonObject();
                return RavenEnvelope.Success(Dispatch(op, args));
            }
            catch (MockError e)
            {
                return RavenEnvelope.Failure(e.Code, e.Message, e.Detail);
            }
            catch (JsonException e)
            {
                return RavenEnvelope.Failure(RavenErrorCodes.BadRequest, Loc.Get("error.badRequest"), e.Message);
            }
            catch (Exception e) when (e is InvalidOperationException or FormatException or ArgumentException)
            {
                return RavenEnvelope.Failure(RavenErrorCodes.Internal, Loc.Get("error.internal"), e.Message);
            }
        }
    }

    // ------------------------------------------------------------------ dispatch

    private JsonNode Dispatch(string op, JsonObject a)
    {
        switch (op)
        {
            case RavenOps.AppInfo:
                return Obj(("version", "0.1.0-mock"), ("build", "dev"), ("platform", "windows"));

            case RavenOps.ProfileState:
                return Obj(("state", _profile));

            case RavenOps.IdentityCreate:
                return IdentityCreate(a);

            case RavenOps.IdentityMe:
                RequireReady();
                return Me();

            case RavenOps.CardMake:
                RequireReady();
                return CardMake(OptBool(a, "include_address") ?? false);

            case RavenOps.CardParse:
            {
                RequireReady();
                ParsedCard card = ParseCard(ReqString(a, "text"));
                // As the engine of version 0.1.0 (API.md, card.parse): a release build reads no
                // Internet or peer-to-peer route from a card (nothing signs them) and says so in
                // `note`; the lab mock reads them, as a debug build with RAVEN_LAB_TEST_A=1 does.
                var routes = new JsonArray();
                var ignored = new JsonArray();
                var ignoredFields = new List<string>();
                if (card.Lan is not null)
                {
                    routes.Add(Obj(("kind", "lan"), ("label", LanPath), ("preview", false)));
                }
                foreach ((bool named, string kind, string label, string field) in new[]
                {
                    (card.Internet, "internet", "Internet direct", "inet="),
                    (card.P2p, "p2p", "Peer-to-peer and relays", "p2p="),
                })
                {
                    if (!named) continue;
                    if (_lab)
                    {
                        routes.Add(Obj(("kind", kind), ("label", label), ("preview", false)));
                    }
                    else
                    {
                        ignored.Add(kind);
                        ignoredFields.Add(field);
                    }
                }
                var result = Obj(("short_address", ShortAddress(card.Address)), ("has_address", routes.Count > 0));
                result["routes"] = routes;
                result["ignored_routes"] = ignored;
                result["note"] = ignoredFields.Count == 0 ? null : JsonValue.Create(
                    $"note: Raven ignored this card's {string.Join(" and ", ignoredFields)}: no key signs a card's addresses, " +
                    "so Raven never dials one; your friend's addresses reach you inside your encrypted link once you are " +
                    "connected (over Tor or the local network)");
                return result;
            }

            case RavenOps.ContactsList:
            {
                RequireReady();
                var list = new JsonArray();
                foreach (Friend friend in _friends.OrderBy(x => x.Contact.Name, StringComparer.OrdinalIgnoreCase))
                    list.Add(ToNode(friend.Contact));
                return new JsonObject { ["contacts"] = list };
            }

            case RavenOps.ContactsAdd:
                RequireReady();
                return new JsonObject { ["contact"] = ToNode(ContactsAdd(ReqString(a, "text"), ReqString(a, "name"))) };

            case RavenOps.ContactsRename:
            {
                RequireReady();
                Friend f = FindFriend(ReqString(a, "address"));
                string name = CheckName(ReqString(a, "name"), except: f);
                f.Contact.Name = name;
                return new JsonObject { ["contact"] = ToNode(f.Contact) };
            }

            case RavenOps.ContactsRemove:
                RequireReady();
                _friends.Remove(FindFriend(ReqString(a, "address")));
                return new JsonObject();

            case RavenOps.ContactsSetAddress:
            {
                RequireReady();
                Friend f = FindFriend(ReqString(a, "address"));
                f.Contact.Lan = ReqString(a, "lan");
                if (f.Contact.Presence == Presences.NoAddress) f.Contact.Presence = Presences.None;
                return new JsonObject { ["contact"] = ToNode(f.Contact) };
            }

            case RavenOps.VerifyCheckWords:
                RequireReady();
                return Obj(("result", CheckWords(FindFriend(ReqString(a, "address")), a["words"] as JsonArray)));

            case RavenOps.VerifyCheckOldCode:
            {
                RequireReady();
                Friend f = FindFriend(ReqString(a, "address"));
                return Obj(("result", CheckOldCode(f, ReqString(a, "code"))));
            }

            case RavenOps.VerifyWordHint:
            {
                string? prefix = WordEntry.Prefix(ReqString(a, "prefix"));
                if (prefix is null || prefix.Length < SecurityWords.MinPrefix) return new JsonObject();
                int? index = SecurityWords.Resolve(prefix);
                return index is int i ? Obj(("word", SecurityWords.List[i])) : new JsonObject();
            }

            case RavenOps.MessagesHistory:
            {
                RequireReady();
                Friend f = FindFriend(ReqString(a, "address"));
                int limit = Math.Clamp(OptInt(a, "limit") ?? 50, 1, 500);
                string? before = OptString(a, "before");
                List<Message> ordered = f.Messages.OrderBy(x => x.AtMs).ToList();
                if (before is not null)
                {
                    int cut = ordered.FindIndex(x => x.Id == before);
                    if (cut >= 0) ordered = ordered.Take(cut).ToList();
                }
                var list = new JsonArray();
                foreach (Message m in ordered.Skip(Math.Max(0, ordered.Count - limit))) list.Add(ToNode(m));
                return new JsonObject { ["messages"] = list };
            }

            case RavenOps.MessagesSend:
                RequireReady();
                return new JsonObject { ["message"] = ToNode(Send(FindFriend(ReqString(a, "address")), ReqString(a, "text"))) };

            case RavenOps.MessagesRetry:
                RequireReady();
                return new JsonObject { ["message"] = ToNode(Retry(ReqString(a, "id"))) };

            case RavenOps.MessagesStop:
                RequireReady();
                return new JsonObject { ["message"] = ToNode(Stop(ReqString(a, "id"))) };

            case RavenOps.MessagesDelete:
                RequireReady();
                return Delete(ReqString(a, "id"));

            case RavenOps.MessagesDetails:
                RequireReady();
                return Details(ReqString(a, "id"));

            case RavenOps.MessagesMarkRead:
                RequireReady();
                return MarkRead(FindFriend(ReqString(a, "address")), OptString(a, "id"));

            case RavenOps.NodeStatus:
                return ToNode(_node);

            case RavenOps.NodeStart:
                RequireReady();
                return ToNode(NodeStart());

            case RavenOps.NodeStop:
                return NodeStop();

            case RavenOps.SettingsGet:
                return ToNode(_settings);

            case RavenOps.SettingsSet:
                ApplySettings(a);
                return ToNode(_settings);

            case RavenOps.EventsPoll:
                return Poll(OptLong(a, "after_seq") ?? 0);

            case RavenOps.PrivacyState:
                return Obj(("available", false), ("encrypted", false), ("locked", _profile == ProfileStates.Locked));

            case RavenOps.PrivacyLock:
            case RavenOps.PrivacyUnlock:
            case RavenOps.PrivacyEncrypt:
                throw NotAvailableYet(op);

            case RavenOps.PrivacyErase:
                if (OptString(a, "confirm") != EraseConfirmation.Word) throw Bad("privacy.erase needs confirm: \"erase\"");
                throw NotAvailableYet(op);

            case RavenOps.NetworkStatus:
                RequireReady();
                return NetworkStatusNode();

            case RavenOps.NetworkSet:
                RequireReady();
                return NetworkSet(a);

            case RavenOps.NetworkCheck:
                RequireReady();
                return NetworkCheck(OptBool(a, "router") ?? false);

            case RavenOps.ContactsRoutes:
                RequireReady();
                return ContactRoutesNode(FindFriend(ReqString(a, "address")));

            case RavenOps.ContactsSetRoutes:
                RequireReady();
                return SetContactRoutes(a);

            case RavenOps.SecurityStatus:
                RequireReady();
                return SecurityStatusNode();

            case RavenOps.LockState:
                return LockStateNode();

            case RavenOps.LockNow:
                RequireReady();
                return LockNow();

            case RavenOps.LockUnlock:
                RequireReady();
                return LockUnlock(OptString(a, "passphrase"));

            case RavenOps.UpdatesStatus:
                RequireReady();
                return UpdatesStatusNode();

            case RavenOps.GroupsList:
            case RavenOps.GroupsGet:
            case RavenOps.GroupsCreate:
            case RavenOps.GroupsAdd:
            case RavenOps.GroupsRemove:
            case RavenOps.GroupsLeave:
            case RavenOps.GroupsHistory:
            case RavenOps.GroupsSend:
            case RavenOps.GroupsMarkRead:
                return GroupsDispatch(op, a);

            default:
                throw Bad($"unknown op '{op}'");
        }
    }

    // ------------------------------------------------------------------ identity and cards

    private JsonNode IdentityCreate(JsonObject a)
    {
        string? name = OptString(a, "display_name");
        _settings.DisplayName = string.IsNullOrWhiteSpace(name) ? null : TextSanitizer.SingleLine(name);
        _profile = ProfileStates.Ready;
        var words = new JsonArray(SecurityWords.WordsFor(_myPublicKey).Select(w => (JsonNode?)JsonValue.Create(w)).ToArray());
        var result = Obj(("address", MyAddress), ("old_code", MyOldCode));
        result["words"] = words;
        return result;
    }

    private JsonNode Me()
    {
        CardMade card = MakeCard(includeAddress: false);
        var words = new JsonArray(SecurityWords.WordsFor(_myPublicKey).Select(w => (JsonNode?)JsonValue.Create(w)).ToArray());
        var result = Obj(("address", MyAddress), ("old_code", MyOldCode), ("card", card.Card), ("invite", card.Invite));
        if (_settings.DisplayName is not null) result["display_name"] = _settings.DisplayName;
        result["words"] = words;
        return result;
    }

    private JsonNode CardMake(bool includeAddress) => ToNode(MakeCard(includeAddress));

    private CardMade MakeCard(bool includeAddress)
    {
        bool canInclude = includeAddress && _node.Running && !string.IsNullOrEmpty(_node.Listening) && !_node.IsLocalOnly;
        string lan = $"{DemoLanAddress}:{DemoPort}";
        var card = new StringBuilder()
            .Append("RAVEN CARD v1 (mock)\n")
            .Append("address=").Append(MyAddress).Append('\n')
            .Append("pub_hex=").Append(MyPublicKeyHex).Append('\n')
            .Append("fingerprint=").Append(MyOldCode).Append('\n');
        if (canInclude) card.Append("lan=").Append(lan).Append('\n');
        return new CardMade
        {
            Card = card.ToString(),
            Invite = $"raven:{MyAddress}:{MyPublicKeyHex}" + (canInclude ? "@" + lan : string.Empty),
            AddressIncluded = canInclude ? lan : null,
        };
    }

    private readonly record struct ParsedCard(string Address, string? Lan, bool Internet = false, bool P2p = false);

    /// <summary>
    /// Mock formats: "raven:&lt;address&gt;:&lt;pub_hex&gt;[@ip:port]" or the "RAVEN CARD" lines
    /// (address=, lan=, and inet= / p2p= for the routes a release build leaves out).
    /// </summary>
    private static ParsedCard ParseCard(string text)
    {
        string t = text.Trim();
        string? address = null;
        string? lan = null;
        bool internet = false;
        bool p2p = false;

        int invite = t.IndexOf("raven:", StringComparison.OrdinalIgnoreCase);
        if (invite >= 0)
        {
            string rest = t[(invite + "raven:".Length)..].Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            int at = rest.IndexOf('@');
            if (at >= 0)
            {
                lan = rest[(at + 1)..];
                rest = rest[..at];
            }
            address = rest.Split(':')[0];
        }
        else
        {
            foreach (string raw in t.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("address=", StringComparison.Ordinal)) address = line["address=".Length..];
                else if (line.StartsWith("lan=", StringComparison.Ordinal)) lan = line["lan=".Length..];
                else if (line.StartsWith("inet=", StringComparison.Ordinal) && line.Length > "inet=".Length) internet = true;
                else if (line.StartsWith("p2p=", StringComparison.Ordinal) && line.Length > "p2p=".Length) p2p = true;
            }
        }

        if (address is null || !address.StartsWith("rvn1", StringComparison.Ordinal) || address.Length < 20
            || !address.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')))
        {
            throw new MockError(RavenErrorCodes.CardInvalid, Loc.Get("error.cardInvalid"), "no rvn1 address found");
        }
        if (address == MyAddress)
        {
            throw new MockError(RavenErrorCodes.CardInvalid,
                "This is your own invite. Paste your friend's invite instead.", "card address equals this identity");
        }
        if (lan is not null && (lan.Length > 64 || !lan.Contains(':'))) lan = null;
        return new ParsedCard(address, lan, internet, p2p);
    }

    // ------------------------------------------------------------------ contacts and words

    private Contact ContactsAdd(string text, string name)
    {
        ParsedCard card = ParseCard(text);
        if (_friends.Any(f => f.Contact.Address == card.Address))
        {
            throw new MockError(RavenErrorCodes.NameClash,
                "This friend is already in your list.", "address already a contact");
        }
        var friend = new Friend(
            new Contact
            {
                Address = card.Address,
                Name = CheckName(name, except: null),
                ShortAddress = ShortAddress(card.Address),
                Verified = false,
                Presence = card.Lan is null ? Presences.NoAddress : Presences.None,
                AvatarIndex = AvatarRule.IndexFor(card.Address),
                Lan = card.Lan,
            },
            SHA256.HashData(Encoding.UTF8.GetBytes(card.Address)));
        _friends.Add(friend);
        return friend.Contact;
    }

    private string CheckName(string name, Friend? except)
    {
        string clean = TextSanitizer.SingleLine(name);
        if (clean.Length == 0)
            throw new MockError(RavenErrorCodes.NameClash, "Type a name for your friend.", "empty name");
        if (_friends.Any(f => f != except && string.Equals(f.Contact.Name, clean, StringComparison.OrdinalIgnoreCase)))
            throw new MockError(RavenErrorCodes.NameClash, Loc.Format("add.name.clash", ("name", clean)), "name already used");
        return clean;
    }

    private static string CheckWords(Friend friend, JsonArray? typed)
    {
        if (typed is null || typed.Count != SecurityWords.TotalWords)
        {
            throw new MockError(RavenErrorCodes.WordsIncomplete, Loc.Get("error.wordsIncomplete"), $"got {typed?.Count ?? 0} words");
        }

        var indexes = new byte[SecurityWords.TotalWords];
        for (int k = 0; k < indexes.Length; k++)
        {
            string? token = typed[k] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
            string? folded = token is null ? null : WordEntry.Prefix(token);
            int? index = folded is null ? null : SecurityWords.Resolve(folded);
            if (index is null) return CheckResults.Typo; // not one of Raven's words: it doesn't fit
            indexes[k] = (byte)index.Value;
        }

        if (!SecurityWords.CheckOk(indexes)) return CheckResults.Typo; // the check word caught it

        byte[] expected = SecurityWords.DeriveIndexes(friend.PublicKey);
        if (!CryptographicOperations.FixedTimeEquals(expected, indexes)) return CheckResults.Mismatch;

        friend.Contact.Verified = true;
        return CheckResults.Match;
    }

    /// <summary>
    /// The old letters-and-digits code: compared case-sensitively after dropping spaces,
    /// separators and invisible characters and folding Persian, Arabic-Indic and full-width
    /// digits (security-words design, "Old code"). A match pins the friend as verified,
    /// exactly like a words match. There is no "typo" for the code: match or mismatch only.
    /// </summary>
    private static string CheckOldCode(Friend friend, string typed)
    {
        string code = NormalizeOldCode(typed);
        if (code.Length == 0) throw Bad("empty code");
        if (friend.OldCode is null) return CheckResults.Mismatch;

        byte[] expected = Encoding.UTF8.GetBytes(NormalizeOldCode(friend.OldCode));
        if (!CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(code))) return CheckResults.Mismatch;

        friend.Contact.Verified = true;
        return CheckResults.Match;
    }

    /// <summary>The old code without spaces, separators or invisible characters, with ASCII digits.</summary>
    internal static string NormalizeOldCode(string typed) => WordEntry.OldCodeKey(typed);

    /// <summary>For tests: the words this mock expects for a friend (the UI never sees them).</summary>
    internal IReadOnlyList<string> FriendWordsForTests(string address)
    {
        lock (_gate) return SecurityWords.WordsFor(FindFriend(address).PublicKey);
    }

    // ------------------------------------------------------------------ messages

    private Message Send(Friend friend, string text)
    {
        if (!_node.Running)
        {
            throw new MockError(RavenErrorCodes.NodeNotRunning,
                "Raven's background program isn't running. Turn on receiving in Settings to start it.", "node not running");
        }
        string clean = TextSanitizer.Clean(text).Trim();
        if (clean.Length == 0) throw Bad("empty text");
        if (clean.Length > MaxMessageLength)
        {
            throw new MockError(RavenErrorCodes.MessageTooLong, Loc.Get("error.messageTooLong"), $"{clean.Length} > {MaxMessageLength} characters");
        }

        long now = NowMs;
        var message = new Message
        {
            Id = NextId(),
            Address = friend.Contact.Address,
            Direction = Directions.Out,
            Text = clean,
            AtMs = now,
            State = MessageStates.Sending,
            Attempts = 0,
        };
        friend.Messages.Add(message);
        StartDelivery(friend, message, now);
        return message;
    }

    private void StartDelivery(Friend friend, Message message, long now)
    {
        message.Attempts = (message.Attempts ?? 0) + 1;
        message.UntilMs = null;

        if (friend.Contact.Presence == Presences.NoAddress || string.IsNullOrEmpty(friend.Contact.Lan))
        {
            message.State = MessageStates.NotSentMeetFirst;
            Emit(EventKinds.State, now, e => { e.Id = message.Id; e.State = message.State; });
            RecomputeWaiting(friend);
            return;
        }

        message.State = MessageStates.Sending;
        RecomputeWaiting(friend);
        Emit(EventKinds.SendStarted, now, e => e.Id = message.Id);

        if (friend.Contact.Presence == Presences.InTouch)
        {
            Schedule(now + 700, t =>
            {
                if (message.State != MessageStates.Sending) return;
                message.State = MessageStates.Sent;
                Emit(EventKinds.State, t, e => { e.Id = message.Id; e.State = message.State; });
            });
            Schedule(now + 1100, t => Deliver(friend, message, LanPath, t - now, t));
        }
        else
        {
            Schedule(now + 900, t =>
            {
                if (message.State != MessageStates.Sending) return;
                message.State = MessageStates.Waiting;
                message.UntilMs = now + 24 * Hour;
                RecomputeWaiting(friend);
                Emit(EventKinds.State, t, e => { e.Id = message.Id; e.State = message.State; });
            });
        }
    }

    private void Deliver(Friend friend, Message message, string path, long ms, long t)
    {
        if (message.State is MessageStates.Delivered or MessageStates.Failed) return;
        message.State = MessageStates.Delivered;
        message.Path = path;
        message.PathKind = LanKind;
        message.UntilMs = null;
        friend.Contact.LastContactMs = t;
        RecomputeWaiting(friend);
        Emit(EventKinds.Receipt, t, e => { e.Id = message.Id; e.Path = path; e.PathKind = LanKind; e.Ms = ms; });
    }

    private Message Retry(string id)
    {
        (Friend friend, Message message) = FindMessage(id);
        if (message.Direction != Directions.Out || message.State is MessageStates.Delivered or MessageStates.Sending or MessageStates.Sent)
            return message;
        if (!_node.Running)
            throw new MockError(RavenErrorCodes.NodeNotRunning, "Raven's background program isn't running. Turn on receiving in Settings to start it.", "node not running");
        StartDelivery(friend, message, NowMs);
        return message;
    }

    private Message Stop(string id)
    {
        (Friend friend, Message message) = FindMessage(id);
        if (message.State is MessageStates.Waiting or MessageStates.Sending)
        {
            message.State = MessageStates.Failed;
            message.UntilMs = null;
            RecomputeWaiting(friend);
            Emit(EventKinds.State, NowMs, e => { e.Id = message.Id; e.State = message.State; });
        }
        return message;
    }

    /// <summary>messages.delete: only an ended message of yours (not sent, failed, time ran out).</summary>
    private JsonObject Delete(string id)
    {
        (Friend friend, Message message) = FindMessage(id);
        if (!message.IsOutgoing || message.State is not (MessageStates.Failed or MessageStates.TimeRanOut or MessageStates.NotSentMeetFirst))
            throw new MockError(RavenErrorCodes.BadRequest,
                "Only a message that wasn't sent can be deleted. Stop a waiting message first.",
                $"message {id} is {message.State}");
        friend.Messages.Remove(message);
        return new JsonObject { ["id"] = id, ["deleted"] = true };
    }

    /// <summary>messages.mark_read: read up to the message <paramref name="id"/> (default the newest); never back.</summary>
    private JsonObject MarkRead(Friend friend, string? id)
    {
        List<Message> ordered = friend.Messages.OrderBy(m => m.AtMs).ToList();
        int end = ordered.Count - 1;
        if (id is not null)
        {
            end = ordered.FindIndex(m => m.Id == id);
            if (end < 0)
                throw new MockError(RavenErrorCodes.BadRequest, "That message isn't on this computer anymore.", $"no message {id} with this friend");
        }
        int marked = friend.ReadUpTo is string current ? ordered.FindLastIndex(m => m.Id == current) : -1;
        if (end > marked) friend.ReadUpTo = ordered[end].Id;
        CountUnread(friend, NowMs);
        return Obj(("address", friend.Contact.Address), ("unread", friend.Contact.Unread));
    }

    /// <summary>
    /// The friend's messages after the one last read (API.md, Contact unread; yours never count),
    /// with an unread event when the count changed.
    /// </summary>
    private void CountUnread(Friend friend, long t)
    {
        List<Message> ordered = friend.Messages.OrderBy(m => m.AtMs).ToList();
        int start = friend.ReadUpTo is string id ? ordered.FindLastIndex(m => m.Id == id) + 1 : 0;
        int count = ordered.Skip(start).Count(m => m.Direction == Directions.In);
        if (count == friend.Contact.Unread) return;
        friend.Contact.Unread = count;
        Emit(EventKinds.Unread, t, e => { e.Address = friend.Contact.Address; e.Unread = count; });
    }

    private JsonNode Details(string id)
    {
        (Friend friend, Message message) = FindMessage(id);
        long now = NowMs;
        string name = friend.Contact.Name;
        string status = string.Empty, why = string.Empty, action = string.Empty;
        switch (message.State)
        {
            case MessageStates.Waiting:
                long until = message.UntilMs ?? now;
                status = Loc.Format("message.status.waiting", ("name", name), ("time", RelativeTime.Deadline(until, now)), ("left", RelativeTime.Left(until, now)));
                why = Loc.Format("message.why.unreachable", ("name", name));
                action = Loc.Format("message.action.wait", ("name", name));
                break;
            case MessageStates.Sending:
            case MessageStates.Sent:
                status = Loc.Format("message.status.sent", ("name", name));
                break;
            case MessageStates.TimeRanOut:
                status = Loc.Get("message.timeRanOut");
                break;
            case MessageStates.Delivered:
                status = Loc.Get("tick.delivered");
                break;
            case MessageStates.NotSentMeetFirst:
                status = Loc.Format("tick.notSent.meetFirst", ("name", name));
                break;
            case MessageStates.Failed:
                status = Loc.Get("tick.failed");
                break;
        }

        // As the engine: plain lines in "detail", the developer fields in "technical".
        string detail = $"Message ID: {message.Id}"
            + (string.IsNullOrEmpty(message.Path) ? string.Empty : $"\nWay: {message.Path}")
            + (message.Attempts is int n && n > 0 ? $"\nTries so far: {n}" : string.Empty);
        string technical = $"id={message.Id} state={message.State} attempts={message.Attempts ?? 0} path={message.Path ?? "-"} at_ms={message.AtMs}"
            + (message.UntilMs is long u ? $" until_ms={u}" : string.Empty) + " (mock engine)";
        var result = Obj(("status_text", status), ("why_text", why), ("action_text", action), ("detail", detail), ("technical", technical));
        result["message"] = ToNode(message);
        return result;
    }

    // ------------------------------------------------------------------ node and settings

    private NodeStatus NodeStart()
    {
        if (_node.Running) return _node;
        long now = NowMs;
        _node.Running = true;
        _node.StartedByApp = true;
        _node.Pid = 4242;
        _node.Listening = null; // starting
        _node.Receive = _settings.Receive;
        ApplyNetworkAtStart(); // what waited for a restart is used from now on
        EmitNode(now);
        Schedule(now + 1200, t =>
        {
            if (!_node.Running) return;
            _node.Listening = ListeningFor(_settings.Receive);
            _node.LastOkMs = t;
            EmitNode(t);
        });
        return _node;
    }

    private static string ListeningFor(bool receive) => receive ? $"0.0.0.0:{DemoPort}" : $"127.0.0.1:{DemoPort}";

    private void ApplySettings(JsonObject a)
    {
        if (a.ContainsKey("display_name"))
        {
            string? name = OptString(a, "display_name");
            _settings.DisplayName = string.IsNullOrWhiteSpace(name) ? null : TextSanitizer.SingleLine(name);
        }
        if (OptBool(a, "receive") is bool receive && receive != _settings.Receive)
        {
            _settings.Receive = receive;
            _node.Receive = receive;
            if (_node.Running && !string.IsNullOrEmpty(_node.Listening))
            {
                _node.Listening = ListeningFor(receive);
                EmitNode(NowMs);
            }
        }
        if (OptBool(a, "reduced_motion") is bool reduced) _settings.ReducedMotion = reduced;
        if (OptString(a, "when_quit") is string whenQuit)
        {
            if (whenQuit is not (WhenQuitOptions.Keep or WhenQuitOptions.StopIfStarted or WhenQuitOptions.Ask))
                throw Bad($"unknown when_quit '{whenQuit}'");
            _settings.WhenQuit = whenQuit;
        }
        if (a.ContainsKey("keep_chats_days"))
        {
            int? days = OptInt(a, "keep_chats_days");
            if (days is not (null or 7 or 30 or 90)) throw Bad($"unsupported keep_chats_days {days}");
            _settings.KeepChatsDays = days;
        }
        if (OptBool(a, "forget_addresses") is bool forget) _settings.ForgetAddresses = forget;
        if (OptBool(a, "lock_on_sleep") is bool lockOnSleep) _settings.LockOnSleep = lockOnSleep;
    }

    // ------------------------------------------------------------------ events

    private JsonNode Poll(long afterSeq)
    {
        long now = NowMs;
        if (_profile == ProfileStates.Ready)
        {
            if (_demoEvents && !_demoScheduled)
            {
                _demoScheduled = true;
                ScheduleDemo(now);
                ScheduleGroupDemo(now);
            }

            List<Scheduled> due = _scheduled.Where(s => s.DueMs <= now).OrderBy(s => s.DueMs).ThenBy(s => s.Order).ToList();
            foreach (Scheduled item in due)
            {
                _scheduled.Remove(item);
                item.Run(item.DueMs);
            }

            if (_node.Running && !string.IsNullOrEmpty(_node.Listening) && now - _lastStatusOkMs >= 2 * Second)
            {
                _lastStatusOkMs = now;
                _node.LastOkMs = now;
                Emit(EventKinds.StatusOk, now, _ => { });
            }
        }

        var list = new JsonArray();
        foreach (RavenEvent e in _events.Where(e => e.Seq > afterSeq))
        {
            JsonNode node = ToNode(e);
            // A group_message's "message" is a GroupMessage (RavenEvent.GroupMessage isn't serialised).
            if (e.GroupMessage is GroupMessage groupMessage && node is JsonObject o) o["message"] = ToNode(groupMessage);
            list.Add(node);
        }
        return new JsonObject { ["seq"] = _seq, ["events"] = list };
    }

    private void ScheduleDemo(long t0)
    {
        Friend ali = FindFriend(AliAddress);
        Friend mina = FindFriend(MinaAddress);
        Friend rena = FindFriend(RenaAddress);

        Schedule(t0 + 1200, t => Incoming(ali, "Great. Can you bring the charger?", t));
        Schedule(t0 + 2600, t =>
        {
            SetPresence(rena, Presences.InTouch, t);
            Incoming(rena, "Hi, it's Rena. Shall we check our security words?", t);
        });
        Schedule(t0 + 6000, t =>
        {
            Message? waiting = mina.Messages.FirstOrDefault(m => m.State == MessageStates.Waiting);
            if (waiting is null) return;
            waiting.Attempts = (waiting.Attempts ?? 1) + 1;
            Emit(EventKinds.Retry, t, e => { e.Id = waiting.Id; e.Address = mina.Contact.Address; e.Attempts = waiting.Attempts; });
        });
        Schedule(t0 + 10500, t =>
        {
            SetPresence(mina, Presences.InTouch, t);
            foreach (Message waiting in mina.Messages.Where(m => m.State == MessageStates.Waiting).ToList())
                Deliver(mina, waiting, LanPath, 640, t);
        });
    }

    private void Incoming(Friend friend, string text, long t)
    {
        var message = new Message
        {
            Id = NextId(),
            Address = friend.Contact.Address,
            Direction = Directions.In,
            Text = text,
            AtMs = t,
            State = MessageStates.Delivered,
            Path = LanPath,
            PathKind = LanKind,
        };
        friend.Messages.Add(message);
        friend.Contact.LastContactMs = t;
        Emit(EventKinds.Incoming, t, e => e.Message = Clone(message));
        CountUnread(friend, t);
    }

    private void SetPresence(Friend friend, string presence, long t)
    {
        if (friend.Contact.Presence == presence) return;
        friend.Contact.Presence = presence;
        if (presence == Presences.InTouch) friend.Contact.LastContactMs = t;
        Emit(EventKinds.Presence, t, e => { e.Address = friend.Contact.Address; e.Presence = presence; });
    }

    private void EmitNode(long t) => Emit(EventKinds.Node, t, e => e.Status = Clone(_node));

    private void Emit(string kind, long atMs, Action<RavenEvent> fill)
    {
        var e = new RavenEvent { Seq = ++_seq, Kind = kind, AtMs = atMs };
        fill(e);
        _events.Add(e);
        if (_events.Count > 500) _events.RemoveRange(0, _events.Count - 500);
    }

    private void Schedule(long dueMs, Action<long> run) => _scheduled.Add(new Scheduled(dueMs, ++_scheduleOrder, run));

    // ------------------------------------------------------------------ demo data

    private void SeedDemoFriends(long now)
    {
        var ali = new Friend(NewContact(AliAddress, "Ali", verified: true, Presences.InTouch, now - 30 * Second, "192.168.1.23:7420"), DemoKey("ali"));
        ali.Messages.Add(new Message { Id = NextId(), Address = AliAddress, Direction = Directions.In, Text = "Are you coming to the meeting?", AtMs = now - 5 * Minute, State = MessageStates.Delivered, Path = LanPath, PathKind = LanKind });
        ali.Messages.Add(new Message { Id = NextId(), Address = AliAddress, Direction = Directions.Out, Text = "Yes, see you there", AtMs = now - 4 * Minute, State = MessageStates.Delivered, Path = LanPath, PathKind = LanKind, Attempts = 1 });
        // Ali's question was read (and answered) before the demo starts.
        ali.ReadUpTo = ali.Messages[^1].Id;

        var mina = new Friend(NewContact(MinaAddress, "Mina", verified: true, Presences.Waiting, now - 26 * Hour, "192.168.1.31:7420"), DemoKey("mina"));
        mina.Messages.Add(new Message { Id = NextId(), Address = MinaAddress, Direction = Directions.Out, Text = "Running late, start without me.", AtMs = now - 2 * Hour, State = MessageStates.Waiting, UntilMs = now + 22 * Hour, Attempts = 1 });

        var rena = new Friend(NewContact(RenaAddress, "Rena", verified: false, Presences.None, null, "192.168.1.40:7420"), DemoKey("rena"), RenaOldCode);

        _friends.Add(ali);
        _friends.Add(mina);
        _friends.Add(rena);
        foreach (Friend f in _friends) RecomputeWaiting(f);
    }

    private static Contact NewContact(string address, string name, bool verified, string presence, long? lastContact, string? lan) => new()
    {
        Address = address,
        Name = name,
        ShortAddress = ShortAddress(address),
        Verified = verified,
        Presence = presence,
        LastContactMs = lastContact,
        AvatarIndex = AvatarRule.IndexFor(address),
        Lan = lan,
    };

    /// <summary>A demo friend's public key: SHA-256("raven-mock/&lt;name&gt;").</summary>
    private static byte[] DemoKey(string name) => SHA256.HashData(Encoding.UTF8.GetBytes("raven-mock/" + name));

    private static void RecomputeWaiting(Friend friend)
    {
        List<Message> waiting = friend.Messages.Where(m => m.Direction == Directions.Out && m.State == MessageStates.Waiting).ToList();
        friend.Contact.Waiting = waiting.Count == 0
            ? null
            : new WaitingInfo { Count = waiting.Count, UntilMs = waiting.Min(m => m.UntilMs ?? long.MaxValue) };
    }

    // ------------------------------------------------------------------ helpers

    private sealed class Friend
    {
        public Friend(Contact contact, byte[] publicKey, string? oldCode = null)
        {
            Contact = contact;
            PublicKey = publicKey;
            OldCode = oldCode;
        }

        public Contact Contact { get; }
        public byte[] PublicKey { get; }

        /// <summary>The friend's old-style code, when the mock knows one (only Rena).</summary>
        public string? OldCode { get; }

        public List<Message> Messages { get; } = new();

        /// <summary>The newest message read (messages.mark_read), as the engine's read marker.</summary>
        public string? ReadUpTo { get; set; }
    }

    private sealed record Scheduled(long DueMs, long Order, Action<long> Run);

    private sealed class MockError : Exception
    {
        public MockError(string code, string message, string detail) : base(message)
        {
            Code = code;
            Detail = detail;
        }

        public string Code { get; }
        public string Detail { get; }
    }

    /// <summary>An unknown operation or bad or missing arguments.</summary>
    private static MockError Bad(string detail)
        => new(RavenErrorCodes.BadRequest, Loc.Get("error.badRequest"), detail);

    private static MockError NotAvailableYet(string op)
        => new(RavenErrorCodes.NotAvailableYet, Loc.Get("error.notAvailableYet"), $"{op}: the privacy pack is not in this release");

    private void RequireReady()
    {
        if (_profile == ProfileStates.New)
            throw new MockError(RavenErrorCodes.NotInitialized, Loc.Get("error.notInitialized"), "profile is new");
        if (_profile == ProfileStates.Locked)
            throw new MockError(RavenErrorCodes.ProfileLocked, Loc.Get("error.profileLocked"), "profile is locked");
    }

    private Friend FindFriend(string address)
        => _friends.FirstOrDefault(f => f.Contact.Address == address)
           ?? throw new MockError(RavenErrorCodes.NotAContact, Loc.Get("error.notAContact"), $"no contact {address}");

    private (Friend, Message) FindMessage(string id)
    {
        foreach (Friend f in _friends)
        {
            Message? m = f.Messages.FirstOrDefault(x => x.Id == id);
            if (m is not null) return (f, m);
        }
        throw new MockError(RavenErrorCodes.Internal, "That message no longer exists.", $"no message {id}");
    }

    private string NextId() => "m" + (++_nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string ShortAddress(string address)
        => address.Length <= 12 ? address : address[..7] + "…" + address[^4..];

    private static JsonNode ToNode<T>(T value)
        => JsonSerializer.SerializeToNode(value, RavenJson.Options) ?? new JsonObject();

    private static JsonObject Obj(params (string Name, object? Value)[] fields)
    {
        var o = new JsonObject();
        foreach ((string name, object? value) in fields)
        {
            o[name] = value switch
            {
                null => null,
                string s => JsonValue.Create(s),
                bool b => JsonValue.Create(b),
                int i => JsonValue.Create(i),
                long l => JsonValue.Create(l),
                _ => JsonValue.Create(value.ToString()),
            };
        }
        return o;
    }

    private static Message Clone(Message m) => new()
    {
        Id = m.Id, Address = m.Address, Direction = m.Direction, Text = m.Text, AtMs = m.AtMs,
        State = m.State, UntilMs = m.UntilMs, Path = m.Path, PathKind = m.PathKind, Attempts = m.Attempts,
    };

    private static NodeStatus Clone(NodeStatus n) => new()
    {
        Running = n.Running, StartedByApp = n.StartedByApp, Pid = n.Pid, Listening = n.Listening,
        Receive = n.Receive, Address = n.Address, LastOkMs = n.LastOkMs, Build = n.Build,
        Carriers = new Carriers { Lan = n.Carriers.Lan, Internet = n.Carriers.Internet, P2p = n.Carriers.P2p, Custody = n.Carriers.Custody, Nearby = n.Carriers.Nearby },
        Lab = new List<string>(n.Lab),
        LabSwitches = new List<string>(n.LabSwitches),
    };

    private static string ReqString(JsonObject a, string name)
        => OptString(a, name) ?? throw Bad($"missing argument '{name}'");

    private static string? OptString(JsonObject a, string name)
        => a[name] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static bool? OptBool(JsonObject a, string name)
        => a[name] is JsonValue v && v.TryGetValue(out bool b) ? b : null;

    private static long? OptLong(JsonObject a, string name)
        => a[name] is JsonValue v && v.TryGetValue(out long l) ? l : null;

    private static int? OptInt(JsonObject a, string name)
        => a[name] is JsonValue v && v.TryGetValue(out int i) ? i : null;
}
#endif
