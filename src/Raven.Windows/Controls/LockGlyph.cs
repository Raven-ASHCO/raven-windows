using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Raven.Windows.Helpers;

namespace Raven.Windows.Controls;

/// <summary>
/// The Calm lock on a soft tile: a body and a shackle drawn with borders (so the shackle can
/// move). Open, the shackle stands 3.5 points up; <see cref="Close"/> closes it (450 ms, curve
/// shackle) right before Raven locks. Decorative for Narrator.
/// </summary>
public sealed class LockGlyph
{
    private readonly TranslateTransform _shackle = new();

    /// <param name="tile">The tile's size (42 in Settings, 48 on the sheet).</param>
    /// <param name="tone">"Accent", "Success" or "Danger" (the tile and the lock's colour).</param>
    /// <param name="open">The shackle starts open (it closes with <see cref="Close"/>).</param>
    public LockGlyph(double tile, string tone, bool open)
    {
        double s = tile / 42.0;
        var shackle = new Border
        {
            Style = Ui.Style($"RavenLockPart{tone}Style"),
            Width = 11 * s,
            Height = 10 * s,
            BorderThickness = new Thickness(1.8 * s, 1.8 * s, 1.8 * s, 0),
            CornerRadius = new CornerRadius(5.5 * s, 5.5 * s, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 9 * s, 0, 0),
            RenderTransform = _shackle,
        };
        var body = new Border
        {
            Style = Ui.Style($"RavenLockPart{tone}Style"),
            Width = 17 * s,
            Height = 13 * s,
            BorderThickness = new Thickness(1.8 * s),
            CornerRadius = new CornerRadius(3 * s),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 18.5 * s, 0, 0),
        };
        var dot = new Ellipse
        {
            Style = Ui.Style($"RavenLockDot{tone}Style"),
            Width = 2.6 * s,
            Height = 2.6 * s,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 24 * s, 0, 0),
        };
        var lockGrid = new Grid { Width = tile, Height = tile };
        lockGrid.Children.Add(shackle);
        lockGrid.Children.Add(body);
        lockGrid.Children.Add(dot);
        Root = new Border
        {
            Style = Ui.Style($"RavenLockTile{tone}Style"),
            Width = tile,
            Height = tile,
            CornerRadius = new CornerRadius(tile >= 46 ? 14 : 12),
            Child = lockGrid,
        };
        AutomationProperties.SetAccessibilityView(Root, AccessibilityView.Raw);
        _shackle.Y = open ? -3.5 * s : 0;
    }

    public Border Root { get; }

    /// <summary>The shackle closes, then <paramref name="completed"/> (at once with reduced motion).</summary>
    public void Close(Action completed) => Motion.Shackle(_shackle, completed);
}
