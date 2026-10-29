using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Helpers;
using Windows.Foundation;

namespace Raven.Windows.Controls;

/// <summary>
/// The tick under a message of yours: an icon (a small spinner while sending, an outlined check
/// once sent, a filled green check with the signed receipt, a clock while it waits) and the words.
/// When the receipt arrives the check pops and one ripple ring goes out (honest motion: only on
/// the receipt event itself). With reduced motion the spinner is a still icon.
/// </summary>
public sealed class TickView
{
    private readonly Grid _iconHost;
    private readonly FontIcon _glyph;
    private readonly ProgressRing _spinner;
    private readonly Ellipse _ripple;
    private readonly ScaleTransform _pop = new() { ScaleX = 1, ScaleY = 1 };
    private readonly ScaleTransform _rippleScale = new() { ScaleX = 1, ScaleY = 1 };
    private readonly TextBlock _label;
    private TickIcon _icon = TickIcon.None;

    public TickView()
    {
        _glyph = new FontIcon
        {
            Style = Ui.Style("RavenTickIconStyle"),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _pop,
        };
        _spinner = new ProgressRing { Width = 12, Height = 12, MinWidth = 0, MinHeight = 0, IsActive = false, Visibility = Visibility.Collapsed };
        _ripple = new Ellipse
        {
            Style = Ui.Style("RavenRippleStyle"),
            Width = 14,
            Height = 14,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _rippleScale,
            IsHitTestVisible = false,
        };
        _iconHost = new Grid { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        _iconHost.Children.Add(_ripple);
        _iconHost.Children.Add(_spinner);
        _iconHost.Children.Add(_glyph);
        AutomationProperties.SetAccessibilityView(_iconHost, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        _label = Ui.Text(string.Empty, "RavenMetaTextStyle");
        Root = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        Root.Children.Add(_iconHost);
        Root.Children.Add(_label);
    }

    public StackPanel Root { get; }

    /// <summary>Shows a tick; <paramref name="receipt"/> when a signed receipt made it Delivered just now (the check pops).</summary>
    public void Set(TickPresenter.Tick tick, bool receipt)
    {
        _label.Text = tick.Label;
        _label.Style = Ui.Style(MetaStyle(tick.Tone));
        Root.Visibility = tick.Label.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        SetIcon(tick.Icon, tick.Tone);
        if (receipt && tick.Icon == TickIcon.Delivered)
        {
            Motion.Pop(_pop);
            Motion.Ripple(_ripple, _rippleScale);
        }
    }

    /// <summary>A group message's tick: no icon (the ring is beside it), strong words.</summary>
    public void SetText(string text, bool strong)
    {
        _label.Text = text;
        _label.Style = Ui.Style(strong ? "RavenMetaStrongTextStyle" : "RavenMetaTextStyle");
        _iconHost.Visibility = Visibility.Collapsed;
        Root.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The motion settings changed: the spinner turns or stands still.</summary>
    public void Refresh()
    {
        if (_icon == TickIcon.Sending) ShowSpinner();
    }

    private void SetIcon(TickIcon icon, Tone tone)
    {
        _icon = icon;
        _iconHost.Visibility = icon == TickIcon.None ? Visibility.Collapsed : Visibility.Visible;
        if (icon == TickIcon.Sending)
        {
            ShowSpinner();
            return;
        }
        _spinner.IsActive = false;
        _spinner.Visibility = Visibility.Collapsed;
        _glyph.Visibility = Visibility.Visible;
        _glyph.Glyph = Glyph(icon);
        _glyph.Style = Ui.Style(icon == TickIcon.Delivered ? "RavenTickIconSuccessStyle" : IconStyle(tone));
    }

    private void ShowSpinner()
    {
        bool moving = Motion.Enabled;
        _spinner.IsActive = moving;
        _spinner.Visibility = moving ? Visibility.Visible : Visibility.Collapsed;
        _glyph.Visibility = moving ? Visibility.Collapsed : Visibility.Visible;
        _glyph.Glyph = "\uE895";
        _glyph.Style = Ui.Style("RavenTickIconStyle");
    }

    public static string Glyph(TickIcon icon) => icon switch
    {
        TickIcon.Sent => "\uE930",      // Completed: an outlined check
        TickIcon.Delivered => "\uEC61", // CompletedSolid: a filled check
        TickIcon.Waiting => "\uE823",   // Recent: a clock
        TickIcon.Ended => "\uE7BA",     // Warning
        TickIcon.Failed => "\uE711",    // Cancel
        TickIcon.Sending => "\uE895",   // Sync (still, with reduced motion)
        _ => string.Empty,
    };

    public static string MetaStyle(Tone tone) => tone switch
    {
        Tone.Accent => "RavenMetaAccentTextStyle",
        Tone.Success => "RavenMetaSuccessTextStyle",
        Tone.Warning => "RavenMetaWarningTextStyle",
        Tone.Danger => "RavenMetaDangerTextStyle",
        _ => "RavenMetaTextStyle",
    };

    private static string IconStyle(Tone tone) => tone switch
    {
        Tone.Accent => "RavenTickIconAccentStyle",
        Tone.Success => "RavenTickIconSuccessStyle",
        Tone.Warning => "RavenTickIconWarningStyle",
        Tone.Danger => "RavenTickIconDangerStyle",
        _ => "RavenTickIconStyle",
    };
}

/// <summary>
/// The group ring beside a message of yours: one arc per member it was for, green as that
/// member's Raven confirms it, amber while another member passes it on, grey while it waits or
/// isn't confirmed, red when given up ("Delivered to 3 of 5" beside it). An arc that just turned
/// green fades in (groupArc, 500 ms).
/// </summary>
public sealed class GroupRing
{
    private const double Box = 18;
    private const double Radius = 7;
    private const double GapDegrees = 14;
    private readonly Canvas _canvas = new() { Width = Box, Height = Box, VerticalAlignment = VerticalAlignment.Center };
    private IReadOnlyList<ReceiptTone> _last = Array.Empty<ReceiptTone>();

    public GroupRing()
    {
        AutomationProperties.SetAccessibilityView(_canvas, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
    }

    public UIElement Root => _canvas;

    public void Set(IReadOnlyList<ReceiptTone> segments, bool animate)
    {
        IReadOnlyList<ReceiptTone> before = _last;
        _last = segments;
        _canvas.Children.Clear();
        int n = segments.Count;
        if (n == 0) return;
        double sweep = 360.0 / n;
        for (int i = 0; i < n; i++)
        {
            Microsoft.UI.Xaml.Shapes.Path arc = n == 1
                ? new Microsoft.UI.Xaml.Shapes.Path { Data = new EllipseGeometry { Center = new Point(Box / 2, Box / 2), RadiusX = Radius, RadiusY = Radius } }
                : new Microsoft.UI.Xaml.Shapes.Path { Data = Arc(-90 + i * sweep + GapDegrees / 2, sweep - GapDegrees) };
            arc.Style = Ui.Style(segments[i] switch
            {
                ReceiptTone.Delivered => "RavenRingDeliveredStyle",
                ReceiptTone.PassedOn => "RavenRingPassedOnStyle",
                ReceiptTone.Failed => "RavenRingFailedStyle",
                _ => "RavenRingPendingStyle",
            });
            _canvas.Children.Add(arc);
            bool turned = segments[i] == ReceiptTone.Delivered && i < before.Count && before[i] != ReceiptTone.Delivered;
            if (animate && turned) Motion.FadeIn(arc, CalmMs.GroupArc);
        }
    }

    private static PathGeometry Arc(double startDegrees, double sweepDegrees)
    {
        double a0 = startDegrees * Math.PI / 180;
        double a1 = (startDegrees + sweepDegrees) * Math.PI / 180;
        var start = new Point(Box / 2 + Radius * Math.Cos(a0), Box / 2 + Radius * Math.Sin(a0));
        var end = new Point(Box / 2 + Radius * Math.Cos(a1), Box / 2 + Radius * Math.Sin(a1));
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(Radius, Radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweepDegrees > 180,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
