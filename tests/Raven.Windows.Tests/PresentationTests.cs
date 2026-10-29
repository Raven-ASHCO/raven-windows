using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Tests;

/// <summary>Sidebar, tick and time texts follow DESIGN.md (honest wording) and tokens.json.</summary>
[TestClass]
public sealed class PresentationTests
{
    private const long Now = 1_790_000_000_000;
    private const long Minute = 60_000;
    private const long Hour = 60 * Minute;
    private const long Day = 24 * Hour;

    [TestMethod]
    public void AvatarRule_IsByteSumModFive()
    {
        Assert.AreEqual(0, AvatarRule.IndexFor(""));
        Assert.AreEqual((('a' + 'b') % 5), AvatarRule.IndexFor("ab"));
        // Values computed independently for the demo addresses.
        Assert.AreEqual(2, AvatarRule.IndexFor("rvn1qd7w39xa2ydg8javvjh4waakpjhkkcjvc"));
        Assert.AreEqual(3, AvatarRule.IndexFor("rvn1qt9aey2hj6th5qmt0e8w6h9msvl7p5p4c"));
        Assert.AreEqual(4, AvatarRule.IndexFor("rvn1qs4zdsm6a9pur3t6llmac0czqdm0unyun"));
    }

    [TestMethod]
    public void Initial_IsTheFirstLetter()
    {
        Assert.AreEqual("A", AvatarRule.Initial("ali"));
        Assert.AreEqual("?", AvatarRule.Initial("\u200B "));
        Assert.AreEqual("\U0001F44D\U0001F3FD", AvatarRule.Initial("\U0001F44D\U0001F3FD thumbs"), "one whole text element");
    }

    [TestMethod]
    public void StatusLine_SaysWhatRavenKnows()
    {
        var waiting = new Contact { Name = "Mina", Verified = true, Waiting = new WaitingInfo { Count = 1, UntilMs = Now + 3_600_000 } };
        StringAssert.StartsWith(ContactPresenter.StatusLine(waiting, Now), "1 waiting \u00B7 until ");

        var unverified = new Contact { Name = "Rena", Verified = false, Presence = Presences.InTouch, LastContactMs = Now };
        Assert.AreEqual(Loc.Get("home.notVerified"), ContactPresenter.StatusLine(unverified, Now));

        var inTouch = new Contact { Name = "Ali", Verified = true, Presence = Presences.InTouch, LastContactMs = Now - 5 * 60_000 };
        StringAssert.StartsWith(ContactPresenter.StatusLine(inTouch, Now), "In touch ");

        var noAddress = new Contact { Name = "Sara", Verified = true, Presence = Presences.NoAddress };
        Assert.AreEqual(Loc.Get("home.noAddress"), ContactPresenter.StatusLine(noAddress, Now));

        var quiet = new Contact { Name = "Omid", Verified = true, Presence = Presences.None };
        Assert.AreEqual(Loc.Get("home.noRecent"), ContactPresenter.StatusLine(quiet, Now));

        // E1: their messages are in the chat, so not "No recent contact".
        var heard = new Contact { Name = "win-c", Verified = true, Presence = Presences.None, LastContactMs = Now - 3 * 3_600_000 };
        StringAssert.StartsWith(ContactPresenter.StatusLine(heard, Now), "Last contact ");
    }

    [TestMethod]
    public void DisplayName_IsSanitisedAndSingleLine()
    {
        Assert.AreEqual("Ali evil", ContactPresenter.DisplayName(new Contact { Name = "Ali\u202E\nevil" }));
        Assert.AreEqual("rvn1q\u2026abcd", ContactPresenter.DisplayName(new Contact { Name = " ", ShortAddress = "rvn1q\u2026abcd" }));
    }

    [TestMethod]
    public void Ticks_NeverClaimDeliveryBeforeTheReceipt()
    {
        Assert.AreEqual(Loc.Get("tick.sending"), TickPresenter.For(Msg(MessageStates.Sending), "Ali", Now).Text);
        Assert.AreEqual("\u2713 " + Loc.Get("tick.sent"), TickPresenter.For(Msg(MessageStates.Sent), "Ali", Now).Text);
        TickPresenter.Tick delivered = TickPresenter.For(Msg(MessageStates.Delivered), "Ali", Now);
        Assert.AreEqual("\u2713\u2713 " + Loc.Get("tick.delivered"), delivered.Text);
        Assert.AreEqual(Tone.Success, delivered.Tone, "Calm: a delivered check is green");
        Assert.AreEqual(Loc.Get("tick.timeRanOut"), TickPresenter.For(Msg(MessageStates.TimeRanOut), "Ali", Now).Text);
        Assert.AreEqual("Not sent: Ali isn't reachable. A first message needs you both online.", TickPresenter.For(Msg(MessageStates.NotSentMeetFirst), "Ali", Now).Text);
        Assert.AreEqual(Tone.Danger, TickPresenter.For(Msg(MessageStates.Failed), "Ali", Now).Tone);
        Assert.AreEqual(string.Empty, TickPresenter.For(Msg("a_new_state"), "Ali", Now).Text);

        foreach (string state in new[] { MessageStates.Sending, MessageStates.Sent, MessageStates.Waiting, MessageStates.Failed })
            Assert.IsFalse(TickPresenter.For(Msg(state), "Ali", Now).Text.Contains(Loc.Get("tick.delivered"), StringComparison.Ordinal), state);
    }

    [TestMethod]
    public void WaitingTick_NamesTheFriendAndTheDeadline()
    {
        Message soon = Msg(MessageStates.Waiting);
        soon.UntilMs = Now + 35 * Minute;
        Assert.AreEqual("Waiting for Mina \u00B7 about 35 min left", TickPresenter.For(soon, "Mina", Now).Text);

        Message later = Msg(MessageStates.Waiting);
        later.UntilMs = Now + 22 * Hour;
        StringAssert.StartsWith(TickPresenter.For(later, "Mina", Now).Text, "Waiting for Mina \u00B7 until ");
    }

    /// <summary>E1: "Waiting for win-c · until –": no empty "until" without a deadline.</summary>
    [TestMethod]
    public void WaitingTick_WithoutADeadline_NamesNoTime()
    {
        Message unknown = Msg(MessageStates.Waiting);
        unknown.UntilMs = null;
        Assert.AreEqual("Waiting for win-c", TickPresenter.For(unknown, "win-c", Now).Text);
        unknown.UntilMs = 0;
        Assert.AreEqual("Waiting for win-c", TickPresenter.For(unknown, "win-c", Now).Text);
        Assert.AreEqual("Tried win-c again \u00B7 not reachable yet \u00B7 keeps trying", TickerPresenter.Retry("win-c", 0, Now));
        StringAssert.StartsWith(TickerPresenter.Retry("win-c", Now + 22 * Hour, Now), "Tried win-c again \u00B7 not reachable yet \u00B7 keeps trying until ");
    }

    /// <summary>E1: the main window never said who you are.</summary>
    [TestMethod]
    public void You_NameAndFingerprint()
    {
        var me = new Me { DisplayName = "Sam", OldCode = "KLHQ-uLWW-5iB0", Address = "rvn1x" };
        Assert.AreEqual(new YouPresenter.Lines("You \u00B7 Sam", "Fingerprint KLHQ-uLWW-5iB0"), YouPresenter.For(me, null));
        Assert.AreEqual(new YouPresenter.Lines("You", null), YouPresenter.For(null, null));
    }

    /// <summary>E1: no time on bubbles.</summary>
    [TestMethod]
    public void BubbleTime_IsShort()
    {
        Assert.AreEqual("13:26 (UTC)", RelativeTime.BubbleTime(4 * Day + 13 * Hour + 26 * Minute, 4 * Day + 18 * Hour, TimeZoneInfo.Utc));
        Assert.AreEqual("Mon 13:26 (UTC)", RelativeTime.BubbleTime(4 * Day + 13 * Hour + 26 * Minute, 6 * Day, TimeZoneInfo.Utc));
    }

    /// <summary>
    /// A deadline's {time} (E1 round 4: "keeps trying until Sun 17:15" read on a Sunday looked like
    /// now): short from today to 5 days ahead, with the date from 6 days ahead and for a past one,
    /// as `raven contact list` says it.
    /// </summary>
    [TestMethod]
    public void Deadline_HasTheDateFromSixDaysAhead()
    {
        const long sunday = 1_792_948_500_000; // 2026-10-25 17:15 UTC, a Sunday
        TimeZoneInfo utc = TimeZoneInfo.Utc;
        Assert.AreEqual("Sun 17:15 (UTC)", RelativeTime.Deadline(sunday, sunday, utc));
        Assert.AreEqual("Mon 17:15 (UTC)", RelativeTime.Deadline(sunday + Day, sunday, utc));
        Assert.AreEqual("Fri 17:15 (UTC)", RelativeTime.Deadline(sunday + 5 * Day, sunday, utc));
        Assert.AreEqual("Sat 31 Oct 17:15 (UTC)", RelativeTime.Deadline(sunday + 6 * Day, sunday, utc));
        Assert.AreEqual("Sun 1 Nov 17:15 (UTC)", RelativeTime.Deadline(sunday + 7 * Day, sunday, utc));
        Assert.AreEqual("Sat 24 Oct 17:15 (UTC)", RelativeTime.Deadline(sunday - Day, sunday, utc));
        // Days, not hours: 23:59 five days on is still short, 00:01 on the sixth is not.
        Assert.AreEqual("Fri 23:59 (UTC)", RelativeTime.Deadline(sunday + 5 * Day + 6 * Hour + 44 * Minute, sunday, utc));
        Assert.AreEqual("Sat 31 Oct 00:01 (UTC)", RelativeTime.Deadline(sunday + 5 * Day + 6 * Hour + 46 * Minute, sunday, utc));
        Assert.AreEqual(12, Loc.GetArray("time.months").Count);
    }

    /// <summary>Every clock time names this computer's zone, as the CLI and the terminal app do (E2 r38); a day line names none.</summary>
    [TestMethod]
    public void ClockTimes_NameTheZone()
    {
        Assert.AreEqual("UTC", RelativeTime.ZoneLabel(TimeSpan.Zero));
        Assert.AreEqual("UTC+1", RelativeTime.ZoneLabel(TimeSpan.FromHours(1)));
        Assert.AreEqual("UTC-5", RelativeTime.ZoneLabel(TimeSpan.FromHours(-5)));
        Assert.AreEqual("UTC+5:30", RelativeTime.ZoneLabel(new TimeSpan(5, 30, 0)));
        Assert.AreEqual("UTC-3:30", RelativeTime.ZoneLabel(-new TimeSpan(3, 30, 0)));
        TimeZoneInfo plusOne = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
        TimeZoneInfo minus330 = TimeZoneInfo.CreateCustomTimeZone("Test-3:30", -new TimeSpan(3, 30, 0), "Test-3:30", "Test-3:30");
        long at = 4 * Day + 13 * Hour + 26 * Minute; // Mon 1970-01-05 13:26 UTC
        Assert.AreEqual("14:26 (UTC+1)", RelativeTime.BubbleTime(at, at, plusOne));
        Assert.AreEqual("Mon 14:26 (UTC+1)", RelativeTime.Clock(at, plusOne));
        Assert.AreEqual("Tue 14:26 (UTC+1)", RelativeTime.Deadline(at + Day, at, plusOne));
        Assert.AreEqual("09:56 (UTC-3:30)", RelativeTime.BubbleTime(at, at, minus330));
        Assert.AreEqual(Loc.Get("chat.today"), RelativeTime.Day(at, at, plusOne));
    }

    [TestMethod]
    public void TimeFormats()
    {
        Assert.AreEqual("0.4", RelativeTime.Seconds(412));
        Assert.AreEqual("Thu 00:00 (UTC)", RelativeTime.Clock(0, TimeZoneInfo.Utc), "1970-01-01 was a Thursday");
        Assert.AreEqual("Sun 23:59 (UTC)", RelativeTime.Clock(3 * Day + 23 * Hour + 59 * Minute, TimeZoneInfo.Utc), "1970-01-04 was a Sunday");
        Assert.AreEqual("Mon 00:00 (UTC)", RelativeTime.Clock(4 * Day, TimeZoneInfo.Utc));
        Assert.IsTrue(RelativeTime.IsSoon(Now + Hour, Now));
        Assert.IsFalse(RelativeTime.IsSoon(Now + 2 * Hour, Now));
        Assert.IsFalse(RelativeTime.IsSoon(Now + 22 * Hour, Now));
    }

    /// <summary>The same rules as the macOS and Linux apps.</summary>
    [TestMethod]
    public void TimePhrases_FollowTheSharedRules()
    {
        Assert.AreEqual("now", RelativeTime.SinceCheck(Now - 2_000, Now));
        Assert.AreEqual("7 s", RelativeTime.SinceCheck(Now - 7_000, Now));
        Assert.AreEqual("2 min ago", RelativeTime.SinceCheck(Now - 2 * Minute, Now));

        Assert.AreEqual("just now", RelativeTime.Ago(Now - 59_000, Now));
        Assert.AreEqual("just now", RelativeTime.Ago(Now + 5_000, Now), "clock skew is not the future");
        Assert.AreEqual("5 min ago", RelativeTime.Ago(Now - 5 * Minute, Now));
        Assert.AreEqual("3 h ago", RelativeTime.Ago(Now - 3 * Hour - 59 * Minute, Now));
        Assert.AreEqual("1 day ago", RelativeTime.Ago(Now - 47 * Hour, Now));
        Assert.AreEqual("2 days ago", RelativeTime.Ago(Now - 2 * Day, Now));

        Assert.AreEqual("about 1 min", RelativeTime.Left(Now, Now));
        Assert.AreEqual("about 1 min", RelativeTime.Left(Now - Hour, Now), "a past deadline is not negative");
        Assert.AreEqual("about 35 min", RelativeTime.Left(Now + 34 * Minute + 1_000, Now));
        Assert.AreEqual("about 1 hour", RelativeTime.Left(Now + Hour, Now));
        Assert.AreEqual("about 1 hour", RelativeTime.Left(Now + Hour + 29 * Minute, Now));
        Assert.AreEqual("about 2 hours", RelativeTime.Left(Now + Hour + 30 * Minute, Now));
        Assert.AreEqual("about 22 hours", RelativeTime.Left(Now + 22 * Hour - 10 * Minute, Now));
        Assert.AreEqual("about 47 hours", RelativeTime.Left(Now + 47 * Hour, Now));
        Assert.AreEqual("about 3 days", RelativeTime.Left(Now + 3 * Day, Now));
    }

    [TestMethod]
    public void Ticker_UsesTheSharedTexts()
    {
        Assert.AreEqual("Delivered to Mina via LAN direct in 0.6 s \u00B7 receipt signed by Mina", TickerPresenter.Delivered("Mina", "LAN direct", 640));
        Assert.AreEqual("New message from Ali \u00B7 LAN direct", TickerPresenter.Incoming("Ali", "LAN direct"));
        Assert.AreEqual("Mina is back \u00B7 your waiting message was delivered", TickerPresenter.Back("Mina"));
        Assert.AreEqual("Listening on your Wi-Fi \u00B7 3 friends \u00B7 all quiet", TickerPresenter.Quiet(3, wifi: true));
        Assert.AreEqual("Listening on your network \u00B7 3 friends \u00B7 all quiet", TickerPresenter.Quiet(3, wifi: false));
        // No separator with nothing after it (E1: "New message from mac-a · ").
        Assert.AreEqual("New message from mac-a", TickerPresenter.Incoming("mac-a", ""));
        Assert.AreEqual("New message from mac-a", TickerPresenter.Incoming("mac-a", null));
    }

    private static Message Msg(string state) => new() { Id = "m1", Address = "rvn1q", Direction = Directions.Out, Text = "hi", AtMs = Now, State = state };
}
