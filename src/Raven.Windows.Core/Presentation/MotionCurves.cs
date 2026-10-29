using System;
using System.Collections.Generic;

namespace Raven.Windows.Core.Presentation;

/// <summary>
/// A CSS cubic-bezier(x1, y1, x2, y2) timing curve (tokens.json motion.curves). The x values stay
/// in 0..1; a y outside 0..1 overshoots, which is how the Calm springs feel. XAML's KeySpline
/// can't overshoot, so the app samples the curve into linear key frames (<see cref="Sample"/>).
/// </summary>
public readonly record struct CubicBezier(double X1, double Y1, double X2, double Y2)
{
    /// <summary>Progress (0 at the start, 1 at the end, possibly beyond 1 on the way) at time fraction <paramref name="t"/>.</summary>
    public double Evaluate(double t)
    {
        if (double.IsNaN(t) || t <= 0) return 0;
        if (t >= 1) return 1;
        double u = SolveForX(t);
        return Coordinate(u, Y1, Y2);
    }

    /// <summary>
    /// <paramref name="steps"/> + 1 points (time fraction, progress) from 0 to 1, evenly spaced in
    /// time: the key frames of an animation that follows this curve.
    /// </summary>
    public IReadOnlyList<(double Time, double Progress)> Sample(int steps)
    {
        steps = Math.Max(1, steps);
        var points = new List<(double, double)>(steps + 1);
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            points.Add((t, Evaluate(t)));
        }
        return points;
    }

    /// <summary>The largest progress the curve reaches (above 1 for an overshooting curve).</summary>
    public double Peak()
    {
        double peak = 0;
        foreach ((double Time, double Progress) point in Sample(200)) peak = Math.Max(peak, point.Progress);
        return peak;
    }

    /// <summary>One coordinate of the curve at parameter u, with the end points fixed at 0 and 1.</summary>
    private static double Coordinate(double u, double p1, double p2)
    {
        double v = 1 - u;
        return 3 * v * v * u * p1 + 3 * v * u * u * p2 + u * u * u;
    }

    private static double Slope(double u, double p1, double p2)
    {
        double v = 1 - u;
        return 3 * v * v * p1 + 6 * v * u * (p2 - p1) + 3 * u * u * (1 - p2);
    }

    /// <summary>The curve parameter whose x is <paramref name="x"/> (Newton, then bisection).</summary>
    private double SolveForX(double x)
    {
        double u = x;
        for (int i = 0; i < 8; i++)
        {
            double error = Coordinate(u, X1, X2) - x;
            if (Math.Abs(error) < 1e-7) return u;
            double slope = Slope(u, X1, X2);
            if (Math.Abs(slope) < 1e-6) break;
            u -= error / slope;
        }
        double lo = 0, hi = 1;
        u = x;
        for (int i = 0; i < 60; i++)
        {
            double value = Coordinate(u, X1, X2);
            if (Math.Abs(value - x) < 1e-7) break;
            if (value < x) lo = u;
            else hi = u;
            u = (lo + hi) / 2;
        }
        return u;
    }
}

/// <summary>tokens.json motion.curves (ThemeTokensTests compares them).</summary>
public static class MotionCurves
{
    /// <summary>Your bubble lifts out of the composer.</summary>
    public static readonly CubicBezier Lift = new(0.2, 0.9, 0.25, 1.12);

    /// <summary>A page, a chat or an incoming bubble arrives.</summary>
    public static readonly CubicBezier Arrive = new(0.2, 0.8, 0.2, 1.0);

    /// <summary>The delivered check pops.</summary>
    public static readonly CubicBezier Pop = new(0.3, 1.6, 0.5, 1.0);

    /// <summary>A path chip slides in.</summary>
    public static readonly CubicBezier Chip = new(0.2, 0.9, 0.3, 1.15);

    /// <summary>The selection behind the Settings sections glides.</summary>
    public static readonly CubicBezier Glide = new(0.3, 1.25, 0.5, 1.0);

    /// <summary>A segmented control's thumb slides.</summary>
    public static readonly CubicBezier Thumb = new(0.3, 1.3, 0.5, 1.0);

    /// <summary>A switch's knob springs across.</summary>
    public static readonly CubicBezier Knob = new(0.3, 1.4, 0.5, 1.0);

    /// <summary>A sheet rises.</summary>
    public static readonly CubicBezier Sheet = new(0.2, 0.9, 0.3, 1.1);

    /// <summary>A toast comes in.</summary>
    public static readonly CubicBezier Toast = new(0.2, 0.9, 0.3, 1.2);

    /// <summary>A Settings section slides in.</summary>
    public static readonly CubicBezier Section = new(0.2, 0.9, 0.3, 1.05);

    /// <summary>A packet travels the route line.</summary>
    public static readonly CubicBezier Packet = new(0.45, 0.05, 0.25, 1.0);

    /// <summary>The lock's shackle closes.</summary>
    public static readonly CubicBezier Shackle = new(0.5, 0.0, 0.3, 1.4);

    /// <summary>Something leaves (ease-in).</summary>
    public static readonly CubicBezier Leave = new(0.42, 0.0, 1.0, 1.0);

    /// <summary>Every curve by its tokens.json name.</summary>
    public static IReadOnlyDictionary<string, CubicBezier> ByName { get; } = new Dictionary<string, CubicBezier>(StringComparer.Ordinal)
    {
        ["lift"] = Lift,
        ["arrive"] = Arrive,
        ["pop"] = Pop,
        ["chip"] = Chip,
        ["glide"] = Glide,
        ["thumb"] = Thumb,
        ["knob"] = Knob,
        ["sheet"] = Sheet,
        ["toast"] = Toast,
        ["section"] = Section,
        ["packet"] = Packet,
        ["shackle"] = Shackle,
        ["leave"] = Leave,
    };

    /// <summary>How many key frames an animation of <paramref name="durationMs"/> gets (about one per 16 ms, 8 at least).</summary>
    public static int StepsFor(int durationMs) => Math.Clamp(durationMs / 16, 8, 48);
}

/// <summary>
/// tokens.json motion.calmMs: the Calm motions' durations (ThemeTokensTests compares every
/// value). Each is started by a real event or by the person's own action (DESIGN.md, "Honest
/// motion"); with Windows "Animation effects" off or Raven's Reduced motion on nothing moves.
/// </summary>
public static class CalmMs
{
    public const int BubbleLift = 520;
    public const int CheckPop = 450;
    public const int TickRipple = 700;
    public const int ChipSlide = 450;
    public const int GroupArc = 500;
    public const int ViewIn = 320;
    public const int SectionOut = 140;
    public const int SectionIn = 360;
    public const int SettingsGlide = 450;
    public const int SegmentThumb = 420;
    public const int SwitchKnob = 300;
    public const int SwitchColor = 250;
    public const int RadioDot = 300;
    public const int SheetIn = 450;
    public const int DialogIn = 420;
    public const int ScrimFade = 350;
    public const int ToastIn = 350;
    public const int PresenceBlink = 600;
    public const int IncomingHighlight = 1500;
    public const int LockShackle = 450;
    public const int LockedFade = 500;
    public const int UnlockFade = 300;
    public const int Shake = 400;
    public const int Hover = 180;
    public const int Press = 120;
    public const int RadarPulse = 2000;
}

/// <summary>
/// Which way a Settings section slides in: down the list (+1) when the new section is below the
/// old one, up (-1) when above; the first section shown doesn't slide (0).
/// </summary>
public static class SlideDirection
{
    public static int Between(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || toIndex < 0 || fromIndex == toIndex) return 0;
        return toIndex > fromIndex ? 1 : -1;
    }
}
