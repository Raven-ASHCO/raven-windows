using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.ViewModels;

/// <summary>One saved route to a friend.</summary>
public sealed record RouteLine(string Kind, string Label, string Value, string? Via, string? Next, Chip StateChip)
{
    /// <summary>contacts.set_routes can clear LAN, Internet and p2p routes (nearby comes with verifying).</summary>
    public bool CanRemove => Kind is "lan" or "internet" or "p2p";
}

/// <summary>A verified friend who could carry this friend's mail.</summary>
public sealed record CustodianChoice(string Address, string Name, bool Selected);

/// <summary>
/// A friend's routes (contacts.routes / contacts.set_routes): the saved routes, what the friend
/// told this computer (route hints), whether this computer's IP is hidden from it, and who
/// carries its mail. Never the friend's security words or old code.
/// </summary>
public sealed class ContactRoutesViewModel
{
    /// <summary>contacts.set_routes takes at most two mail friends.</summary>
    public const int MaxMailFriends = 2;

    private readonly IRavenEngine _engine;
    private readonly Func<long> _nowMs;
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private List<Contact> _friends = new();

    public ContactRoutesViewModel(IRavenEngine engine, Contact contact, Func<long>? nowMs = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        Contact = contact ?? throw new ArgumentNullException(nameof(contact));
        _nowMs = nowMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public event Action? Changed;

    public Contact Contact { get; private set; }
    public string Name => ContactPresenter.DisplayName(Contact);
    public ContactRoutes? Routes { get; private set; }
    public IReadOnlyList<RouteLine> RouteLines { get; private set; } = Array.Empty<RouteLine>();
    public string HintsText { get; private set; } = string.Empty;

    /// <summary>What the friend told this computer about reaching it.</summary>
    public IReadOnlyList<string> LearnedLines { get; private set; } = Array.Empty<string>();

    /// <summary>Who carries the friend's mail: told by the friend, chosen by you, offline delivery.</summary>
    public IReadOnlyList<string> CarrierLines { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<CustodianChoice> CustodianChoices { get; private set; } = Array.Empty<CustodianChoice>();
    public bool HideIp { get; private set; }

    /// <summary>
    /// Where Raven reaches the friend on the local network now ("Raven last reached win-c at
    /// 192.168.0.24:17420 (5 min ago)."), from lan_now; null when not known (E1: only the old saved
    /// address was shown while every message went to the new one).
    /// </summary>
    public string? LanNowText { get; private set; }

    /// <summary>The saved LAN address is another, old one: said plainly.</summary>
    public string? LanStaleText { get; private set; }

    /// <summary>The address in use can be saved: the saved one is old, or none is saved.</summary>
    public bool CanUseLanNow { get; private set; }

    /// <summary>The saved Internet and p2p routes (for the edit fields).</summary>
    public string InternetRoute { get; private set; } = string.Empty;
    public string P2pRoute { get; private set; } = string.Empty;
    public IReadOnlyList<string> P2pVia { get; private set; } = Array.Empty<string>();

    public bool Busy { get; private set; }
    public ErrorInfo? Error { get; private set; }

    /// <summary>The last change was saved (cleared by the next one).</summary>
    public bool Saved { get; private set; }

    public async Task LoadAsync()
    {
        Busy = true;
        Changed?.Invoke();
        try
        {
            try
            {
                _friends = (await _engine.ContactsListAsync()).ToList();
                _names.Clear();
                foreach (Contact c in _friends) _names[c.Address] = ContactPresenter.DisplayName(c);
            }
            catch (RavenException)
            {
                // Without the list, mail friends show by short address and can't be chosen.
                _friends = new List<Contact>();
            }
            Apply(await _engine.ContactsRoutesAsync(Contact.Address));
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ErrorInfo.From(ex);
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Sets the Internet route ("address:port") and/or the p2p route with its relays.</summary>
    public Task<bool> SaveRoutesAsync(string? internet, string? p2p, IReadOnlyList<string>? via)
    {
        var changes = new JsonObject();
        string inet = (internet ?? string.Empty).Trim();
        string peer = (p2p ?? string.Empty).Trim();
        if (inet.Length > 0 && inet != InternetRoute) changes["internet"] = inet;
        List<string> relays = (via ?? Array.Empty<string>()).Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
        bool viaChanged = !relays.SequenceEqual(P2pVia, StringComparer.Ordinal);
        if (peer.Length > 0 && (peer != P2pRoute || viaChanged))
        {
            changes["p2p"] = peer;
            if (relays.Count > 0) changes["via"] = Strings(relays);
        }
        if (changes.Count == 0) return Task.FromResult(true); // nothing new
        return ChangeAsync(changes);
    }

    /// <summary>Saves the address Raven reaches the friend at now as its LAN address (contacts.set_address).</summary>
    public async Task<bool> UseLanNowAsync()
    {
        string addr = Routes?.LanNow?.Addr ?? string.Empty;
        if (Busy || addr.Length == 0) return false;
        Busy = true;
        Saved = false;
        Error = null;
        Changed?.Invoke();
        try
        {
            Contact = await _engine.ContactsSetAddressAsync(Contact.Address, addr);
            Apply(await _engine.ContactsRoutesAsync(Contact.Address));
            Saved = true;
            return true;
        }
        catch (Exception ex)
        {
            Error = ErrorInfo.From(ex);
            return false;
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Removes a saved route ("lan", "internet" or "p2p").</summary>
    public Task<bool> RemoveRouteAsync(string kind)
        => ChangeAsync(new JsonObject { ["clear"] = Strings(new[] { kind }) });

    /// <summary>The friends who carry this friend's mail (at most two verified friends; none clears them).</summary>
    public Task<bool> SetMailFriendsAsync(IReadOnlyList<string> addresses)
        => ChangeAsync(new JsonObject { ["custodians"] = Strings(addresses) });

    /// <summary>Hides this computer's IP address from this one friend.</summary>
    public Task<bool> SetHideIpAsync(bool hide)
        => ChangeAsync(new JsonObject { ["hide_ip"] = hide });

    private async Task<bool> ChangeAsync(JsonObject changes)
    {
        if (Busy) return false;
        Busy = true;
        Saved = false;
        Error = null;
        Changed?.Invoke();
        try
        {
            SetRoutesResult result = await _engine.ContactsSetRoutesAsync(Contact.Address, changes);
            if (!string.IsNullOrEmpty(result.Contact.Address)) Contact = result.Contact;
            Apply(result.Routes);
            Saved = true;
            return true;
        }
        catch (Exception ex)
        {
            Error = ErrorInfo.From(ex);
            return false;
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    private void Apply(ContactRoutes routes)
    {
        Routes = routes;
        HideIp = routes.HideIp;
        RouteLines = routes.Routes.Select(r => new RouteLine(
            r.Kind,
            TextSanitizer.SingleLine(r.Label).Length > 0 ? TextSanitizer.SingleLine(r.Label) : r.Kind,
            r.Value,
            r.Via.Count > 0 ? Loc.Format("network.routes.via", ("relays", string.Join(", ", r.Via))) : null,
            string.IsNullOrWhiteSpace(r.Next) ? null : Loc.Format("network.routes.next", ("id", r.Next)),
            NetworkPresenter.StateChip(r.State))).ToList();
        RouteEntry? inet = routes.Routes.FirstOrDefault(r => r.Kind == "internet");
        RouteEntry? p2p = routes.Routes.FirstOrDefault(r => r.Kind == "p2p");
        InternetRoute = inet?.Value ?? string.Empty;
        P2pRoute = p2p?.Value ?? string.Empty;
        P2pVia = p2p?.Via.ToList() ?? new List<string>();

        string savedLan = routes.Routes.FirstOrDefault(r => r.Kind == "lan")?.Value ?? string.Empty;
        LanNow? lan = routes.LanNow is { Addr.Length: > 0 } now ? now : null;
        LanNowText = lan is null ? null : Loc.Format("network.routes.lanNow", ("name", Name),
            ("addr", TextSanitizer.SingleLine(lan.Addr)), ("when", RelativeTime.Ago(lan.AtMs, _nowMs())));
        LanStaleText = lan is { SavedStale: true } && savedLan.Length > 0
            ? Loc.Format("network.routes.lanStale", ("addr", savedLan), ("name", Name))
            : null;
        CanUseLanNow = lan is not null && (LanStaleText is not null || savedLan.Length == 0);

        HintsText = Loc.Format("network.routes.hints.state", ("state", NetworkPresenter.StateChip(routes.Carriers.Hints).Text));
        LearnedLines = BuildLearned(routes.Learned, routes.Carriers);
        CarrierLines = BuildCarriers(routes.Carriers);

        HashSet<string> listed = routes.Carriers.Listed
            .Select(f => f.Address ?? string.Empty)
            .Where(a => a.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        CustodianChoices = _friends
            .Where(f => f.Verified && !f.Blocked && f.Address != Contact.Address)
            .Select(f => new CustodianChoice(f.Address, ContactPresenter.DisplayName(f), listed.Contains(f.Address)))
            .ToList();
    }

    /// <summary>
    /// How many mail friends the friend told about: the list shown below the count, so the two
    /// never disagree (E1: "mail friends: 0" above "Mail friends ... told you about: mac-b").
    /// </summary>
    public static int MailFriendCount(LearnedRoutes? learned, ContactCarriers? carriers) =>
        carriers is null ? learned?.MailFriends ?? 0 : carriers.Told.Count + carriers.ToldUnknown;

    private List<string> BuildLearned(LearnedRoutes? learned, ContactCarriers? carriers)
    {
        if (learned is null) return new List<string> { Loc.Format("network.routes.hints.none", ("name", Name)) };
        string until = learned.ExpiresAtMs > 0 ? RelativeTime.Deadline(learned.ExpiresAtMs, _nowMs()) : "\u2013";
        var lines = new List<string>
        {
            Loc.Format("network.routes.hints.counts", ("relays", learned.Relays), ("direct", learned.Direct),
                ("mail", MailFriendCount(learned, carriers)), ("time", until)),
        };
        if (!string.IsNullOrWhiteSpace(learned.PeerId))
            // Only a proven ID gets a word: "not proven yet" was nothing the user could act on (E1).
            lines.Add(Loc.Format("network.routes.peerId", ("id", learned.PeerId))
                + (learned.PeerIdProven ? " \u00B7 " + Loc.Get("network.routes.peerId.proven") : ""));
        if (learned.LanPort is int port)
            lines.Add(Loc.Format("network.routes.lanPort", ("port", port.ToString(CultureInfo.InvariantCulture))));
        return lines;
    }

    private List<string> BuildCarriers(ContactCarriers carriers)
    {
        var lines = new List<string>();
        // One line per list, the names joined by ", " (as on macOS and Linux).
        if (carriers.Told.Count > 0)
        {
            IEnumerable<string> told = carriers.Told.Select(f => f.TakesHandoffs == true
                ? Loc.Format("network.routes.takesHandoffs", ("friend", FriendName(f)))
                : FriendName(f));
            lines.Add(Loc.Format("network.routes.told", ("name", Name), ("friends", string.Join(", ", told))));
        }
        if (carriers.ToldUnknown > 0)
            lines.Add(Loc.Format("network.routes.toldUnknown", ("name", Name), ("n", carriers.ToldUnknown)));
        if (carriers.Listed.Count > 0)
        {
            IEnumerable<string> listed = carriers.Listed.Select(f => f.Usable == false
                ? Loc.Format("network.routes.notUsable", ("friend", FriendName(f)))
                : FriendName(f));
            lines.Add(Loc.Format("network.routes.listed", ("name", Name), ("friends", string.Join(", ", listed))));
        }
        if (lines.Count == 0) lines.Add(Loc.Format("network.routes.noCarriers", ("name", Name)));
        lines.AddRange(AwayLines(carriers, Name, _nowMs()));
        if (carriers.ReceiptsStranded) lines.Add(Loc.Format("network.routes.stranded", ("name", Name)));
        return lines;
    }

    /// <summary>
    /// How long a message written now could wait while the friend is away, by the rule the
    /// terminal app and raven contact list use (carriers.away; E1: this panel said "up to 7 days"
    /// where the terminal app said "until tomorrow"), then how friends can carry it too (only when
    /// one can). A friend Raven hasn't connected with yet gets the terminal app's first-message
    /// rule; "meet first" is only for a secure connection that ended. <paramref name="nowMs"/>:
    /// when it is read (a deadline a week away has its date).
    /// </summary>
    public static IReadOnlyList<string> AwayLines(ContactCarriers carriers, string name, long nowMs)
    {
        var lines = new List<string>();
        if (carriers.Away is AwayWait away)
        {
            if (away.FirstMessage == true)
                lines.Add(Loc.Format("network.routes.awayFirstMessage", ("name", name)));
            else if (away.MeetFirst == true)
                lines.Add(Loc.Format("network.routes.awayMeetFirst", ("name", name)));
            else if (away.UntilMs is long until && until > 0)
                lines.Add(Loc.Format("network.routes.away", ("name", name), ("time", RelativeTime.Deadline(until, nowMs))));
        }
        if (carriers.Coverage is Coverage coverage && coverage.Kind is "mutual_friend" or "two_friends"
            && !string.IsNullOrWhiteSpace(coverage.Name))
            lines.Add(Loc.Format("network.routes.coverage", ("coverage", coverage.Name), ("until", coverage.Until)));
        return lines;
    }

    private string FriendName(FriendRef friend)
    {
        if (!string.IsNullOrWhiteSpace(friend.Name)) return TextSanitizer.SingleLine(friend.Name);
        string address = friend.Address ?? string.Empty;
        if (_names.TryGetValue(address, out string? name)) return name;
        return address.Length <= 12 ? address : address[..7] + "…" + address[^4..];
    }

    private static JsonArray Strings(IEnumerable<string> values)
        => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
}
