using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.Presentation;

/// <summary>tokens.json "avatarRule": the same friend keeps the same colour on every computer.</summary>
public static class AvatarRule
{
    public const int Colors = 5;

    /// <summary>(sum of the bytes of the friend's Raven address) mod 5.</summary>
    public static int IndexFor(string? address)
    {
        if (string.IsNullOrEmpty(address)) return 0;
        long sum = 0;
        foreach (byte b in Encoding.UTF8.GetBytes(address)) sum += b;
        return (int)(sum % Colors);
    }

    /// <summary>First letter of the (sanitised) name, upper-case; "?" when there is none.</summary>
    public static string Initial(string? name)
    {
        string clean = TextSanitizer.SingleLine(name);
        if (clean.Length == 0) return "?";
        string first = StringInfo.GetNextTextElement(clean, 0);
        return first.ToUpperInvariant();
    }
}

/// <summary>Visual tone of a short status text; each app maps it to its own colours.</summary>
public enum Tone
{
    Secondary,
    Accent,
    Success,
    Warning,
    Danger,
}

/// <summary>How a friend appears in the sidebar.</summary>
public static class ContactPresenter
{
    public static string DisplayName(Contact contact)
    {
        string name = TextSanitizer.SingleLine(contact.Name);
        return name.Length > 0 ? name : TextSanitizer.SingleLine(contact.ShortAddress);
    }

    /// <summary>
    /// The status line under the name. Priority: waiting messages, then "Not verified",
    /// then no address, then the last real contact ("In touch 5 min ago", never "online"),
    /// then "No recent contact".
    /// </summary>
    public static string StatusLine(Contact contact, long nowMs)
    {
        if (contact.Waiting is { Count: > 0 } waiting)
            return Loc.Format("home.waiting", ("n", waiting.Count), ("time", RelativeTime.Deadline(waiting.UntilMs, nowMs)));
        if (!contact.Verified)
            return Loc.Get("home.notVerified");
        if (contact.Presence == Presences.NoAddress)
            return Loc.Get("home.noAddress");
        if (contact.Presence == Presences.InTouch)
            return Loc.Format("home.inTouch", ("when", RelativeTime.Ago(contact.LastContactMs ?? nowMs, nowMs)));
        // Their messages are in the chat: say when, not "No recent contact" (E1).
        if (contact.LastContactMs is long last && last > 0)
            return Loc.Format("home.lastContact", ("when", RelativeTime.Ago(last, nowMs)));
        return Loc.Get("home.noRecent");
    }

    /// <summary>
    /// The path chip in the chat header: how the last message travelled, but not while messages to
    /// the friend wait (E1: "LAN direct" stayed there while the friend was away and every try
    /// failed).
    /// </summary>
    public static string? HeaderPath(Contact contact, string? lastPath)
    {
        if (contact.Presence == Presences.Waiting || contact.Waiting is { Count: > 0 }) return null;
        string path = TextSanitizer.SingleLine(lastPath ?? string.Empty);
        return path.Length > 0 ? path : null;
    }

    /// <summary>Presence for the dot; unknown values from a newer engine show as "none".</summary>
    public static string DotPresence(Contact contact) => contact.Presence switch
    {
        Presences.InTouch => Presences.InTouch,
        Presences.Waiting => Presences.Waiting,
        _ => Presences.None,
    };
}

/// <summary>
/// The chat header at narrow widths: the path pill shows only while the status text beside it keeps
/// some room; the status text trims first, and the Routes button always shows whole (E1).
/// </summary>
public static class HeaderFit
{
    /// <summary>What the status text ("your node: ok · now") keeps at least before the pill goes.</summary>
    public const double MinStatusWidth = 48;

    /// <summary>The space between the pill and the heart.</summary>
    public const double PillGap = 8;

    /// <summary><paramref name="room"/>: the header's width less everything but the pill and the status text.</summary>
    public static bool ShowsPathPill(double room, double pillWidth) => room - pillWidth - PillGap >= MinStatusWidth;
}

/// <summary>The tick text under an outgoing bubble ("Sent, not confirmed yet" until a signed receipt).</summary>
public static class TickPresenter
{
    /// <summary>
    /// <paramref name="Text"/>: what Narrator says and the plain-text form (with ✓ / ✓✓ glyphs);
    /// <paramref name="Icon"/> and <paramref name="Label"/>: the Calm form, an icon beside the words.
    /// </summary>
    public readonly record struct Tick(string Text, Tone Tone, TickIcon Icon = TickIcon.None, string Label = "");

    public static Tick For(Message message, string friendName, long nowMs)
    {
        Tick plain = Plain(message, friendName, nowMs);
        TickIcon icon = message.State switch
        {
            MessageStates.Sending => TickIcon.Sending,
            MessageStates.Sent => TickIcon.Sent,
            MessageStates.Delivered => TickIcon.Delivered,
            MessageStates.Waiting => TickIcon.Waiting,
            MessageStates.TimeRanOut or MessageStates.NotSentMeetFirst => TickIcon.Ended,
            MessageStates.Failed => TickIcon.Failed,
            _ => TickIcon.None,
        };
        // The Calm label drops the ✓ glyphs: the icon says it.
        string label = plain.Text.StartsWith("\u2713\u2713 ", StringComparison.Ordinal) ? plain.Text[3..]
            : plain.Text.StartsWith("\u2713 ", StringComparison.Ordinal) ? plain.Text[2..]
            : plain.Text;
        // Delivered is the success colour in Calm (a filled green check).
        Tone tone = message.State == MessageStates.Delivered ? Tone.Success : plain.Tone;
        return new Tick(plain.Text, tone, icon, label);
    }

    private static Tick Plain(Message message, string friendName, long nowMs)
    {
        switch (message.State)
        {
            case MessageStates.Sending:
                return new Tick(Loc.Get("tick.sending"), Tone.Secondary);
            case MessageStates.Sent:
                return new Tick("✓ " + Loc.Get("tick.sent"), Tone.Secondary);
            case MessageStates.Delivered:
                // A late delivery says when it came (E1: one that waited an hour said only "Delivered").
                return message.DeliveredAtMs is long at && at > 0
                    ? new Tick("✓✓ " + Loc.Format("tick.deliveredAt", ("time", RelativeTime.BubbleTime(at, nowMs))), Tone.Accent)
                    : new Tick("✓✓ " + Loc.Get("tick.delivered"), Tone.Accent);
            case MessageStates.Waiting:
                if (message.UntilMs is long until && until > 0)
                {
                    return RelativeTime.IsSoon(until, nowMs)
                        ? new Tick(Loc.Format("tick.waitingSoon", ("name", friendName), ("left", RelativeTime.Left(until, nowMs))), Tone.Warning)
                        : new Tick(Loc.Format("tick.waiting", ("name", friendName), ("time", RelativeTime.Deadline(until, nowMs))), Tone.Warning);
                }
                // No deadline known yet (raven-node's outbox hasn't listed it): no empty "until".
                return new Tick(Loc.Format("tick.waitingNoTime", ("name", friendName)), Tone.Warning);
            case MessageStates.TimeRanOut:
                return new Tick(Loc.Get("tick.timeRanOut"), Tone.Warning);
            case MessageStates.NotSentMeetFirst:
                return new Tick(Loc.Format("tick.notSent.meetFirst", ("name", friendName)), Tone.Warning);
            case MessageStates.Failed:
                return new Tick(Loc.Get("tick.failed"), Tone.Danger);
            default:
                return new Tick(string.Empty, Tone.Secondary); // a state this app does not know yet
        }
    }

    /// <summary>A message of yours that was not sent: Send again and Delete show under its bubble.</summary>
    public static bool CanDelete(Message message) => message.IsOutgoing && message.State is MessageStates.TimeRanOut
        or MessageStates.Failed or MessageStates.NotSentMeetFirst;

    /// <summary>States where the details dialog offers an action (Try now / Stop trying / Send again).</summary>
    public static bool HasActions(Message message) => message.State is MessageStates.Waiting
        or MessageStates.TimeRanOut or MessageStates.Failed or MessageStates.NotSentMeetFirst;
}

/// <summary>Settings &gt; Privacy: what this computer stores encrypted, as it is now (privacy.state "encrypted").</summary>
public static class PrivacyPresenter
{
    public static IReadOnlyList<string> StoredLines(PrivacyState privacy) => privacy.Encrypted
        ? new[] { Loc.Get("settings.stored.encrypted"), Loc.Get("settings.stored.plain") }
        : new[] { Loc.Get("settings.stored.notEncrypted") };
}

/// <summary>The sidebar's You entry: "You · Sam" (or "You") and "Fingerprint KLHQ-uLWW-5iB0".</summary>
public static class YouPresenter
{
    public readonly record struct Lines(string Title, string? Fingerprint);

    public static Lines For(Me? me, string? settingsName)
    {
        string name = TextSanitizer.SingleLine(me?.DisplayName ?? settingsName ?? string.Empty);
        string title = name.Length == 0 ? Loc.Get("you.title") : Loc.Format("you.row", ("name", name));
        string code = TextSanitizer.SingleLine(me?.OldCode ?? string.Empty);
        return new Lines(title, code.Length == 0 ? null : Loc.Format("you.fingerprint", ("code", code)));
    }
}

/// <summary>The one-line ticker ("toast line") under the messages.</summary>
public static class TickerPresenter
{
    /// <summary>"Listening on your Wi-Fi" only when this computer's network is Wi-Fi (E1: a wired computer said Wi-Fi).</summary>
    public static string Quiet(int friends, bool wifi = false)
        => Loc.Format(wifi ? "ticker.quiet" : "ticker.quiet.network", ("n", friends));

    /// <summary>"New message from Ali · LAN direct"; no separator when no path is known (E1: "from mac-a · ").</summary>
    public static string Incoming(string name, string? path)
    {
        string clean = TextSanitizer.SingleLine(path);
        return clean.Length == 0
            ? Loc.Format("ticker.incoming.noPath", ("name", name))
            : Loc.Format("ticker.incoming", ("name", name), ("path", clean));
    }

    public static string Delivered(string name, string? path, long? ms)
        => Loc.Format("ticker.delivered", ("name", name), ("path", path ?? string.Empty), ("secs", RelativeTime.Seconds(ms ?? 0)));

    /// <summary>until_ms is 0 or missing when not known: then the line names no time.</summary>
    public static string Retry(string name, long? untilMs, long nowMs)
        => untilMs is long u && u > 0
            ? Loc.Format("ticker.retry", ("name", name), ("time", RelativeTime.Deadline(u, nowMs)))
            : Loc.Format("ticker.retryNoTime", ("name", name));

    public static string Back(string name) => Loc.Format("ticker.back", ("name", name));
}

/// <summary>The icon beside a tick in the Calm design.</summary>
public enum TickIcon
{
    None,
    /// <summary>A small spinner (still, with reduced motion).</summary>
    Sending,
    /// <summary>An outlined check: it left this computer.</summary>
    Sent,
    /// <summary>A filled check: the friend's signed receipt came.</summary>
    Delivered,
    /// <summary>A clock: it waits.</summary>
    Waiting,
    /// <summary>Time ran out, or not sent (meet first).</summary>
    Ended,
    /// <summary>Not delivered.</summary>
    Failed,
}
