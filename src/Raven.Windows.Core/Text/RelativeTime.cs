using System;
using System.Collections.Generic;
using System.Globalization;

namespace Raven.Windows.Core.Text;

/// <summary>
/// Fills the time placeholders of strings.en.json: {when} (relative time, "5 min ago"),
/// {time} (local clock time with this computer's zone, "Tue 14:05 (UTC+1)"; a deadline 6 or
/// more days away with its date, "Sun 1 Nov 17:15 (UTC+1)"), {left} (time left, "about 22 hours") and
/// {secs} (seconds with one decimal). The words come from the "time.*" keys; the rules are
/// the same in the macOS, Windows and Linux apps.
/// </summary>
public static class RelativeTime
{
    private const long MinuteMs = 60_000;
    private const long HourMs = 60 * MinuteMs;
    private const long DayMs = 24 * HourMs;

    /// <summary>A waiting message says "{left} left" instead of "until {time}" below this.</summary>
    public const long SoonMs = 2 * HourMs;

    /// <summary>"now" (under 3 s), "5 s" (under a minute), then like <see cref="Ago"/>; used by the heartbeat ("node ok · {when}").</summary>
    public static string SinceCheck(long thenMs, long nowMs)
    {
        long seconds = Math.Max(0, (nowMs - thenMs) / 1000);
        if (seconds < 3) return Loc.Get("time.now");
        if (seconds < 60) return Loc.Format("time.seconds", ("n", seconds));
        return Ago(thenMs, nowMs);
    }

    /// <summary>"just now", "5 min ago", "3 h ago", "1 day ago", "2 days ago"; used by "In touch {when}".</summary>
    public static string Ago(long thenMs, long nowMs)
    {
        long seconds = Math.Max(0, (nowMs - thenMs) / 1000);
        if (seconds < 60) return Loc.Get("time.justNow");
        long minutes = seconds / 60;
        if (minutes < 60) return Loc.Format("time.minutesAgo", ("n", minutes));
        long hours = minutes / 60;
        if (hours < 24) return Loc.Format("time.hoursAgo", ("n", hours));
        long days = hours / 24;
        return days == 1 ? Loc.Get("time.dayAgo") : Loc.Format("time.daysAgo", ("n", days));
    }

    /// <summary>"about 40 min" (under an hour), "about 1 hour", "about 22 hours", "about 3 days" (from 48 hours).</summary>
    public static string Left(long untilMs, long nowMs)
    {
        long leftMs = Math.Max(0, untilMs - nowMs);
        if (leftMs < HourMs)
        {
            long minutes = Math.Max(1, (leftMs + MinuteMs - 1) / MinuteMs);
            return Loc.Format("time.leftMinutes", ("n", minutes));
        }
        long hours = (leftMs + HourMs / 2) / HourMs;
        if (hours == 1) return Loc.Get("time.leftHour");
        if (hours < 48) return Loc.Format("time.leftHours", ("n", hours));
        return Loc.Format("time.leftDays", ("n", (leftMs + DayMs / 2) / DayMs));
    }

    /// <summary>
    /// This computer's zone at a moment, as the CLI names it after a time (`ash` ext.rs
    /// zone_label): "UTC", "UTC+1", "UTC-5", "UTC+5:30" (the offset in effect at that moment).
    /// </summary>
    public static string ZoneLabel(TimeSpan offset)
    {
        if (offset == TimeSpan.Zero) return "UTC";
        char sign = offset < TimeSpan.Zero ? '-' : '+';
        TimeSpan abs = offset.Duration();
        int h = (int)abs.TotalHours;
        int m = abs.Minutes;
        return m == 0
            ? "UTC" + sign + h.ToString(CultureInfo.InvariantCulture)
            : "UTC" + sign + h.ToString(CultureInfo.InvariantCulture) + ":" + m.ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>"<paramref name="time"/> (UTC+1)": every clock time names its zone, as in the CLI and the terminal app (E2 r38).</summary>
    private static string Zoned(string time, DateTimeOffset local) => time + " (" + ZoneLabel(local.Offset) + ")";

    /// <summary>
    /// Local clock time like "Tue 14:05 (UTC+1)": the day from "time.weekdays" (Monday first), the
    /// 24-hour clock and this computer's zone.
    /// </summary>
    public static string Clock(long ms, TimeZoneInfo? zone = null)
    {
        DateTimeOffset utc = DateTimeOffset.FromUnixTimeMilliseconds(ms);
        DateTimeOffset local = TimeZoneInfo.ConvertTime(utc, zone ?? TimeZoneInfo.Local);
        string hm = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        IReadOnlyList<string> days = Loc.GetArray("time.weekdays");
        int index = ((int)local.DayOfWeek + 6) % 7; // DayOfWeek counts from Sunday = 0
        return Zoned(index < days.Count ? days[index] + " " + hm : hm, local);
    }

    /// <summary>
    /// The {time} of a deadline seen at <paramref name="nowMs"/>: <see cref="Clock"/> from today to
    /// 5 days ahead (each weekday names one day), else with the date too, "Sun 1 Nov 17:15" (month
    /// from "time.months"), as `raven contact list` says it: a deadline a week away must not read
    /// like today.
    /// </summary>
    public static string Deadline(long untilMs, long nowMs, TimeZoneInfo? zone = null)
    {
        TimeZoneInfo z = zone ?? TimeZoneInfo.Local;
        DateTimeOffset at = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(untilMs), z);
        DateTime today = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(nowMs), z).Date;
        int ahead = (int)(at.Date - today).TotalDays;
        if (ahead >= 0 && ahead <= 5) return Clock(untilMs, z);
        IReadOnlyList<string> days = Loc.GetArray("time.weekdays");
        IReadOnlyList<string> months = Loc.GetArray("time.months");
        int index = ((int)at.DayOfWeek + 6) % 7; // DayOfWeek counts from Sunday = 0
        var parts = new List<string>();
        if (index < days.Count) parts.Add(days[index]);
        parts.Add(at.Day.ToString(CultureInfo.InvariantCulture));
        if (at.Month <= months.Count) parts.Add(months[at.Month - 1]);
        parts.Add(at.ToString("HH:mm", CultureInfo.InvariantCulture));
        return Zoned(string.Join(" ", parts), at);
    }

    /// <summary>The short time on a bubble: "13:26 (UTC+1)" for today, else the day too ("Tue 13:26 (UTC+1)").</summary>
    public static string BubbleTime(long ms, long nowMs, TimeZoneInfo? zone = null)
    {
        TimeZoneInfo z = zone ?? TimeZoneInfo.Local;
        DateTimeOffset at = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(ms), z);
        DateTimeOffset now = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(nowMs), z);
        return at.Date == now.Date ? Zoned(at.ToString("HH:mm", CultureInfo.InvariantCulture), at) : Clock(ms, z);
    }

    /// <summary>
    /// The line between two days of a chat: "Today", "Yesterday", else the weekday and the date
    /// ("Tue 2026-10-20"), as the macOS app says it. No zone: a day line names no time (as in the
    /// terminal app).
    /// </summary>
    public static string Day(long ms, long nowMs, TimeZoneInfo? zone = null)
    {
        TimeZoneInfo z = zone ?? TimeZoneInfo.Local;
        DateTime at = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(ms), z).Date;
        DateTime now = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(nowMs), z).Date;
        if (at == now) return Loc.Get("chat.today");
        if (at == now.AddDays(-1)) return Loc.Get("chat.yesterday");
        IReadOnlyList<string> days = Loc.GetArray("time.weekdays");
        int index = ((int)at.DayOfWeek + 6) % 7;
        string date = at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return index < days.Count ? days[index] + " " + date : date;
    }

    /// <summary>Whether two times fall on different local days (a chat draws a day line between them).</summary>
    public static bool DifferentDays(long aMs, long bMs, TimeZoneInfo? zone = null)
    {
        TimeZoneInfo z = zone ?? TimeZoneInfo.Local;
        return TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(aMs), z).Date
            != TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(bMs), z).Date;
    }

    /// <summary>Seconds with one decimal, e.g. "0.4" for 412 ms.</summary>
    public static string Seconds(long ms) => (ms / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>True when <paramref name="untilMs"/> is less than 2 hours away ("{left} left").</summary>
    public static bool IsSoon(long untilMs, long nowMs) => untilMs - nowMs < SoonMs;
}
