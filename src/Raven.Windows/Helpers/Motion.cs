using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Raven.Windows.Core.Presentation;
using Windows.UI.ViewManagement;

namespace Raven.Windows.Helpers;

/// <summary>
/// The app's only animations, each one a recipe from DESIGN.md "Honest motion" and the Calm
/// motions (tokens.json motion.calmMs, motion.curves). Callers start them only from engine events
/// or from the person's own action (choosing a Settings section, a switch, a segmented control),
/// plus the one-time logo reveal. When Windows "Animation effects" is off or Raven's own "Reduced
/// motion" setting is on, nothing moves: every recipe jumps to its end state and only the texts
/// change.
/// <para>
/// The Calm curves overshoot (a y beyond 1, the springs); XAML's KeySpline can't, so each curve
/// is sampled into linear key frames (<see cref="CubicBezier.Sample"/>).
/// </para>
/// </summary>
public static class Motion
{
    private static readonly UISettings UiSettings = new();

    /// <summary>Raven's own setting (settings.reduced_motion).</summary>
    public static bool AppReducedMotion { get; set; }

    /// <summary>True when animations may run right now.</summary>
    public static bool Enabled
    {
        get
        {
            if (AppReducedMotion) return false;
            try
            {
                return UiSettings.AnimationsEnabled;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>Adds one double animation to <paramref name="storyboard"/>.</summary>
    public static DoubleAnimation Add(Storyboard storyboard, DependencyObject target, string property,
        double from, double to, int durationMs, EasingMode? easing = null, int beginMs = 0)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
            FillBehavior = FillBehavior.Stop,
        };
        if (easing is EasingMode mode) animation.EasingFunction = new CubicEase { EasingMode = mode };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
        return animation;
    }

    /// <summary>
    /// Adds one animation that follows a Calm curve (sampled into linear key frames, so it may
    /// overshoot like a spring). <paramref name="dependent"/> for layout properties (Width).
    /// </summary>
    public static DoubleAnimationUsingKeyFrames AddCurve(Storyboard storyboard, DependencyObject target, string property,
        double from, double to, int durationMs, CubicBezier curve, int beginMs = 0, bool dependent = false)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
            EnableDependentAnimation = dependent,
            FillBehavior = FillBehavior.Stop,
        };
        foreach ((double Time, double Progress) point in curve.Sample(MotionCurves.StepsFor(durationMs)))
        {
            animation.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs * point.Time)),
                Value = from + (to - from) * point.Progress,
            });
        }
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
        return animation;
    }

    /// <summary>Adds key frames at given times (fractions of the duration), linear between them.</summary>
    public static DoubleAnimationUsingKeyFrames AddFrames(Storyboard storyboard, DependencyObject target, string property,
        int durationMs, IReadOnlyList<(double Time, double Value)> frames, int beginMs = 0)
    {
        var animation = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(beginMs), FillBehavior = FillBehavior.Stop };
        foreach ((double Time, double Value) frame in frames)
        {
            animation.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs * frame.Time)),
                Value = frame.Value,
            });
        }
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
        return animation;
    }

    /// <summary>
    /// Starts a storyboard; animation problems never break the UI. Animations don't hold their
    /// end value (FillBehavior Stop): each recipe sets the end value as the element's own value
    /// first, so a later change without motion (a resize, the engine's value) always shows.
    /// </summary>
    public static Storyboard? Run(Storyboard storyboard, Action? completed = null)
    {
        storyboard.FillBehavior = FillBehavior.Stop;
        if (completed is not null) storyboard.Completed += (_, _) => completed();
        try
        {
            storyboard.Begin();
            return storyboard;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Raven: animation failed: " + ex.Message);
            completed?.Invoke();
            return null;
        }
    }

    // ------------------------------------------------------------------ honest motion (engine events)

    /// <summary>Heartbeat (status_ok): the dot grows to 1.35 and back once, 500 ms.</summary>
    public static void Heartbeat(ScaleTransform scale)
    {
        if (!Enabled) return;
        var sb = new Storyboard();
        var frames = new[] { (0.0, 1.0), (0.4, 1.35), (1.0, 1.0) };
        AddFrames(sb, scale, "ScaleX", MotionTokens.HeartbeatPulse, frames);
        AddFrames(sb, scale, "ScaleY", MotionTokens.HeartbeatPulse, frames);
        Run(sb);
    }

    /// <summary>
    /// A glyph travels along the route line (send_started: out, receipt: back, incoming: in).
    /// Leaving eases in, arriving eases out (tokens.json "easing").
    /// </summary>
    public static void Travel(TranslateTransform translate, double from, double to, int durationMs, bool arriving, Action completed)
    {
        if (!Enabled)
        {
            completed();
            return;
        }
        var sb = new Storyboard();
        Add(sb, translate, "X", from, to, durationMs, arriving ? EasingMode.EaseOut : EasingMode.EaseIn);
        Run(sb, completed);
    }

    /// <summary>
    /// A packet (a dot) travels the route line on the packet curve: it fades in over the first
    /// 12 %, and at the end grows and fades out (260 ms) where it arrived.
    /// </summary>
    public static void Packet(UIElement dot, CompositeTransform transform, double from, double to, int durationMs, Action completed)
    {
        if (!Enabled)
        {
            completed();
            return;
        }
        // The end values as the element's own: the animations run from the start to them.
        dot.Opacity = 0;
        transform.TranslateX = to;
        transform.ScaleX = 1;
        transform.ScaleY = 1;
        var travel = new Storyboard();
        AddCurve(travel, transform, "TranslateX", from, to, durationMs, MotionCurves.Packet);
        AddFrames(travel, dot, "Opacity", durationMs, new[] { (0.0, 0.0), (0.12, 1.0), (1.0, 1.0) });
        AddCurve(travel, transform, "ScaleX", 0.6, 1, durationMs, MotionCurves.Arrive);
        AddCurve(travel, transform, "ScaleY", 0.6, 1, durationMs, MotionCurves.Arrive);
        Run(travel, () =>
        {
            var fade = new Storyboard();
            Add(fade, dot, "Opacity", 1, 0, 260, EasingMode.EaseOut);
            Add(fade, transform, "ScaleX", 1, 1.8, 260, EasingMode.EaseOut);
            Add(fade, transform, "ScaleY", 1, 1.8, 260, EasingMode.EaseOut);
            Run(fade, completed);
        });
    }

    /// <summary>Incoming bubble: slides up 10 px and fades in, 350 ms (curve arrive).</summary>
    public static void BubbleIn(UIElement element, TranslateTransform translate)
    {
        if (!Enabled) return;
        var sb = new Storyboard();
        AddCurve(sb, element, "Opacity", 0, 1, MotionTokens.BubbleIn, MotionCurves.Arrive);
        AddCurve(sb, translate, "Y", 10, 0, MotionTokens.BubbleIn, MotionCurves.Arrive);
        Run(sb);
    }

    /// <summary>
    /// Your bubble lifts out of the composer to its place (send): from the composer's position,
    /// scale 0.92 and opacity 0.2, 520 ms on the lift curve.
    /// </summary>
    public static void Lift(UIElement bubble, CompositeTransform transform, double dx, double dy)
    {
        if (!Enabled) return;
        var sb = new Storyboard();
        AddCurve(sb, transform, "TranslateX", dx, 0, CalmMs.BubbleLift, MotionCurves.Lift);
        AddCurve(sb, transform, "TranslateY", dy, 0, CalmMs.BubbleLift, MotionCurves.Lift);
        AddCurve(sb, transform, "ScaleX", 0.92, 1, CalmMs.BubbleLift, MotionCurves.Lift);
        AddCurve(sb, transform, "ScaleY", 0.92, 1, CalmMs.BubbleLift, MotionCurves.Lift);
        AddCurve(sb, bubble, "Opacity", 0.2, 1, CalmMs.BubbleLift, MotionCurves.Arrive);
        Run(sb, () =>
        {
            transform.TranslateX = 0;
            transform.TranslateY = 0;
            transform.ScaleX = 1;
            transform.ScaleY = 1;
            bubble.Opacity = 1;
        });
    }

    /// <summary>The delivered check fills and pops (scale 0.4 to 1, 450 ms on the pop curve) when the signed receipt comes.</summary>
    public static void Pop(ScaleTransform scale)
    {
        if (!Enabled) return;
        var sb = new Storyboard();
        AddCurve(sb, scale, "ScaleX", 0.4, 1, CalmMs.CheckPop, MotionCurves.Pop);
        AddCurve(sb, scale, "ScaleY", 0.4, 1, CalmMs.CheckPop, MotionCurves.Pop);
        Run(sb);
    }

    /// <summary>One ripple ring around the check (scale 0.6 to 2.2, fading), 700 ms.</summary>
    public static void Ripple(UIElement ring, ScaleTransform scale)
    {
        if (!Enabled)
        {
            ring.Opacity = 0;
            return;
        }
        var sb = new Storyboard();
        Add(sb, scale, "ScaleX", 0.6, 2.2, CalmMs.TickRipple, EasingMode.EaseOut);
        Add(sb, scale, "ScaleY", 0.6, 2.2, CalmMs.TickRipple, EasingMode.EaseOut);
        Add(sb, ring, "Opacity", 0.9, 0, CalmMs.TickRipple, EasingMode.EaseOut);
        Run(sb, () => ring.Opacity = 0);
    }

    /// <summary>A path chip slides in from the left (8 px, scale 0.9), 450 ms on the chip curve.</summary>
    public static void ChipSlide(UIElement chip, CompositeTransform transform)
    {
        if (!Enabled) return;
        var sb = new Storyboard();
        AddCurve(sb, transform, "TranslateX", -8, 0, CalmMs.ChipSlide, MotionCurves.Chip);
        AddCurve(sb, transform, "ScaleX", 0.9, 1, CalmMs.ChipSlide, MotionCurves.Chip);
        AddCurve(sb, transform, "ScaleY", 0.9, 1, CalmMs.ChipSlide, MotionCurves.Chip);
        AddCurve(sb, chip, "Opacity", 0, 1, CalmMs.ChipSlide, MotionCurves.Arrive);
        Run(sb);
    }

    /// <summary>Presence changed: the dot blinks once (its colour already changed).</summary>
    public static void Blink(UIElement element, Action? completed = null)
    {
        if (!Enabled)
        {
            completed?.Invoke();
            return;
        }
        // Two storyboards in a row: one storyboard may not animate the same property twice.
        var dim = new Storyboard();
        Add(dim, element, "Opacity", 1, 0.2, CalmMs.PresenceBlink / 4, EasingMode.EaseIn);
        Run(dim, () =>
        {
            var back = new Storyboard();
            Add(back, element, "Opacity", 0.2, 1, CalmMs.PresenceBlink / 4, EasingMode.EaseOut);
            Run(back, () =>
            {
                element.Opacity = 1;
                completed?.Invoke();
            });
        });
    }

    /// <summary>"In touch" dots breathe slowly (3 s cycle) while that state is real: the halo behind the dot.</summary>
    public static Storyboard? Breathe(UIElement element)
    {
        if (!Enabled) return null;
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        DoubleAnimation a = Add(sb, element, "Opacity", 0, 1, MotionTokens.PresenceBreathe / 2);
        a.AutoReverse = true;
        a.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
        return Run(sb);
    }

    /// <summary>Retry: a ring expands once around the friend's dot and fades (1 s).</summary>
    public static void RetryRing(UIElement ring, ScaleTransform scale)
    {
        if (!Enabled) return;
        var sb = new Storyboard();
        Add(sb, scale, "ScaleX", 1, 1.8, MotionTokens.RetryRing, EasingMode.EaseOut);
        Add(sb, scale, "ScaleY", 1, 1.8, MotionTokens.RetryRing, EasingMode.EaseOut);
        Add(sb, ring, "Opacity", 0.9, 0, MotionTokens.RetryRing, EasingMode.EaseOut);
        Run(sb, () => ring.Opacity = 0);
    }

    /// <summary>The Network page's radar: a ring grows and fades once per successful status check.</summary>
    public static void Radar(UIElement ring, ScaleTransform scale, int beginMs)
    {
        if (!Enabled) return;
        var sb = new Storyboard();
        Add(sb, scale, "ScaleX", 0.4, 1.15, CalmMs.RadarPulse, EasingMode.EaseOut, beginMs);
        Add(sb, scale, "ScaleY", 0.4, 1.15, CalmMs.RadarPulse, EasingMode.EaseOut, beginMs);
        AddFrames(sb, ring, "Opacity", CalmMs.RadarPulse, new[] { (0.0, 0.8), (1.0, 0.0) }, beginMs);
        Run(sb, () => ring.Opacity = 0);
    }

    // ------------------------------------------------------------------ the person's own actions

    /// <summary>A page or a chat comes in: opacity 0 and 6 points lower to its place, 320 ms (curve arrive).</summary>
    public static void ViewIn(UIElement view, TranslateTransform translate)
    {
        if (!Enabled)
        {
            view.Opacity = 1;
            translate.Y = 0;
            return;
        }
        var sb = new Storyboard();
        AddCurve(sb, view, "Opacity", 0, 1, CalmMs.ViewIn, MotionCurves.Arrive);
        AddCurve(sb, translate, "Y", 6, 0, CalmMs.ViewIn, MotionCurves.Arrive);
        Run(sb);
    }

    /// <summary>The old Settings section leaves: 10 points in the direction of travel, ease-in, 140 ms.</summary>
    public static void SectionOut(UIElement view, TranslateTransform translate, int direction, Action completed)
    {
        if (!Enabled || direction == 0)
        {
            completed();
            return;
        }
        var sb = new Storyboard();
        AddCurve(sb, view, "Opacity", 1, 0, CalmMs.SectionOut, MotionCurves.Leave);
        AddCurve(sb, translate, "Y", 0, -10 * direction, CalmMs.SectionOut, MotionCurves.Leave);
        Run(sb, completed);
    }

    /// <summary>The new Settings section slides in from 16 points in the direction of travel, 360 ms (curve section).</summary>
    public static void SectionIn(UIElement view, TranslateTransform translate, int direction)
    {
        if (!Enabled || direction == 0)
        {
            view.Opacity = 1;
            translate.Y = 0;
            return;
        }
        var sb = new Storyboard();
        AddCurve(sb, view, "Opacity", 0, 1, CalmMs.SectionIn, MotionCurves.Arrive);
        AddCurve(sb, translate, "Y", 16 * direction, 0, CalmMs.SectionIn, MotionCurves.Section);
        Run(sb, () =>
        {
            view.Opacity = 1;
            translate.Y = 0;
        });
    }

    /// <summary>The selection behind the Settings sections glides to the chosen one (450 ms, curve glide).</summary>
    public static void Glide(TranslateTransform translate, double toY, bool animate)
    {
        if (!animate || !Enabled)
        {
            translate.Y = toY;
            return;
        }
        double fromY = translate.Y;
        translate.Y = toY;
        var sb = new Storyboard();
        AddCurve(sb, translate, "Y", fromY, toY, CalmMs.SettingsGlide, MotionCurves.Glide);
        Run(sb);
    }

    /// <summary>A segmented control's thumb slides and resizes (420 ms, curve thumb).</summary>
    public static void Thumb(FrameworkElement thumb, TranslateTransform translate, double toX, double toWidth, bool animate)
    {
        double fromWidth = double.IsNaN(thumb.Width) ? toWidth : thumb.Width;
        if (!animate || !Enabled)
        {
            translate.X = toX;
            thumb.Width = toWidth;
            return;
        }
        double fromX = translate.X;
        translate.X = toX;
        thumb.Width = toWidth;
        var sb = new Storyboard();
        AddCurve(sb, translate, "X", fromX, toX, CalmMs.SegmentThumb, MotionCurves.Thumb);
        AddCurve(sb, thumb, "Width", fromWidth, toWidth, CalmMs.SegmentThumb, MotionCurves.Thumb, dependent: true);
        Run(sb);
    }

    /// <summary>A switch's knob springs across (300 ms, curve knob) while the green track fades (250 ms).</summary>
    public static void Knob(TranslateTransform knob, UIElement onTrack, double toX, double toOpacity, bool animate)
    {
        if (!animate || !Enabled)
        {
            knob.X = toX;
            onTrack.Opacity = toOpacity;
            return;
        }
        double fromX = knob.X;
        double fromOpacity = onTrack.Opacity;
        knob.X = toX;
        onTrack.Opacity = toOpacity;
        var sb = new Storyboard();
        AddCurve(sb, knob, "X", fromX, toX, CalmMs.SwitchKnob, MotionCurves.Knob);
        Add(sb, onTrack, "Opacity", fromOpacity, toOpacity, CalmMs.SwitchColor, EasingMode.EaseOut);
        Run(sb);
    }

    /// <summary>A toast comes in: 8 points up and fades in, 350 ms (curve toast).</summary>
    public static void ToastIn(UIElement toast, TranslateTransform translate)
    {
        toast.Opacity = 1;
        if (!Enabled)
        {
            translate.Y = 0;
            return;
        }
        var sb = new Storyboard();
        AddCurve(sb, toast, "Opacity", 0, 1, CalmMs.ToastIn, MotionCurves.Arrive);
        AddCurve(sb, translate, "Y", 8, 0, CalmMs.ToastIn, MotionCurves.Toast);
        Run(sb);
    }

    /// <summary>A toast leaves (fades out, 300 ms); then <paramref name="completed"/>.</summary>
    public static void ToastOut(UIElement toast, Action completed)
    {
        if (!Enabled)
        {
            toast.Opacity = 0;
            completed();
            return;
        }
        double from = toast.Opacity;
        toast.Opacity = 0;
        var sb = new Storyboard();
        Add(sb, toast, "Opacity", from, 0, 300, EasingMode.EaseIn);
        Run(sb, completed);
    }

    /// <summary>The lock icon's shackle closes (450 ms, curve shackle), before Raven locks.</summary>
    public static void Shackle(TranslateTransform shackle, Action completed)
    {
        if (!Enabled)
        {
            shackle.Y = 0;
            completed();
            return;
        }
        double from = shackle.Y;
        shackle.Y = 0;
        var sb = new Storyboard();
        AddCurve(sb, shackle, "Y", from, 0, CalmMs.LockShackle, MotionCurves.Shackle);
        Run(sb, completed);
    }

    /// <summary>A wrong passphrase shakes the card (400 ms).</summary>
    public static void Shake(TranslateTransform translate)
    {
        if (!Enabled) return;
        var sb = new Storyboard();
        AddFrames(sb, translate, "X", CalmMs.Shake, new[] { (0.0, 0.0), (0.2, -8.0), (0.4, 8.0), (0.6, -8.0), (0.8, 8.0), (1.0, 0.0) });
        Run(sb, () => translate.X = 0);
    }

    /// <summary>A whole screen fades in (the Locked window: 500 ms; Unlock: 300 ms).</summary>
    public static void FadeIn(UIElement element, int durationMs)
    {
        if (!Enabled)
        {
            element.Opacity = 1;
            return;
        }
        var sb = new Storyboard();
        Add(sb, element, "Opacity", 0, 1, durationMs, EasingMode.EaseOut);
        Run(sb, () => element.Opacity = 1);
    }

    /// <summary>A whole screen fades out (Unlock: 300 ms), then <paramref name="completed"/>.</summary>
    public static void FadeOut(UIElement element, int durationMs, Action completed)
    {
        if (!Enabled)
        {
            completed();
            return;
        }
        var sb = new Storyboard();
        Add(sb, element, "Opacity", element.Opacity, 0, durationMs, EasingMode.EaseIn);
        // The element's own opacity stays: the next screen replaces it right after.
        Run(sb, completed);
    }

    /// <summary>Welcome screen only: the mark fades and scales in once (600 ms).</summary>
    public static void LogoReveal(UIElement logo, ScaleTransform scale, Action completed)
    {
        if (!Enabled)
        {
            logo.Opacity = 1;
            completed();
            return;
        }
        logo.Opacity = 1;
        var sb = new Storyboard();
        Add(sb, logo, "Opacity", 0, 1, MotionTokens.LogoReveal, EasingMode.EaseOut);
        Add(sb, scale, "ScaleX", 0.92, 1, MotionTokens.LogoReveal, EasingMode.EaseOut);
        Add(sb, scale, "ScaleY", 0.92, 1, MotionTokens.LogoReveal, EasingMode.EaseOut);
        Run(sb, completed);
    }

    /// <summary>A text that just changed because of an event fades in (200 ms).</summary>
    public static void FadeIn(UIElement element)
    {
        if (!Enabled)
        {
            element.Opacity = 1;
            return;
        }
        var sb = new Storyboard();
        Add(sb, element, "Opacity", 0, 1, MotionTokens.WordReveal, EasingMode.EaseOut);
        Run(sb);
    }
}
