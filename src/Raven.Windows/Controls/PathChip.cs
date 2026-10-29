using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;
using Windows.Foundation;

namespace Raven.Windows.Controls;

/// <summary>
/// A path chip: the way a message travelled ("LAN direct", "Bluetooth", "via Tor", "Held by a
/// friend"), in its path family's colour on an 11 % tint of it, with its icon. Narrator: "Delivery
/// path: LAN direct". The tooltip shows the engine's own words. A chip that just appeared because
/// of a receipt slides in (<see cref="SlideIn"/>).
/// </summary>
public sealed class PathChip
{
    private readonly Border _border;
    private readonly StackPanel _row;
    private readonly TextBlock _text;
    private readonly CompositeTransform _transform = new();
    private readonly bool _pill;
    private PathFamily? _family;

    /// <param name="pill">The larger pill (the chat header) instead of the chip under a bubble.</param>
    public PathChip(bool pill = false)
    {
        _pill = pill;
        _text = new TextBlock();
        _row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = pill ? 6 : 4, VerticalAlignment = VerticalAlignment.Center };
        _border = new Border
        {
            Child = _row,
            Visibility = Visibility.Collapsed,
            RenderTransform = _transform,
            RenderTransformOrigin = new Point(0, 0.5),
        };
    }

    public UIElement Root => _border;

    /// <summary>Whether it shows (a path is known).</summary>
    public bool Shown => _border.Visibility == Visibility.Visible;

    /// <summary>Shows the path of <paramref name="pathKind"/> / <paramref name="path"/> (hidden when there is none).</summary>
    public void Set(string? pathKind, string? path, bool technical)
    {
        string label = CalmPath.Chip(pathKind, path, technical);
        SetText(CalmPath.Family(pathKind), label, PathPresenter.AccessibleName(pathKind, path), TextSanitizer.SingleLine(path));
    }

    /// <summary>Any words in a family's colour (the header pill while a friend holds the mail, the receipts' "passed on").</summary>
    public void SetText(PathFamily family, string label, string accessibleName, string? tooltip = null)
    {
        if (label.Length == 0)
        {
            _border.Visibility = Visibility.Collapsed;
            return;
        }
        if (_family != family)
        {
            _family = family;
            string name = family.ToString();
            _border.Style = Ui.Style(_pill ? $"RavenPill{name}Style" : $"RavenChip{name}Style");
            _text.Style = Ui.Style($"RavenChip{name}TextStyle");
            if (_pill) _text.FontSize = 12;
            _row.Children.Clear();
            if (Icon(family, _pill ? 13 : 11) is UIElement icon) _row.Children.Add(icon);
            _row.Children.Add(_text);
        }
        _text.Text = label;
        _border.Visibility = Visibility.Visible;
        AutomationProperties.SetName(_border, accessibleName.Length > 0 ? accessibleName : label);
        ToolTipService.SetToolTip(_border, string.IsNullOrEmpty(tooltip) || tooltip == label ? null : tooltip);
    }

    public void Hide() => _border.Visibility = Visibility.Collapsed;

    /// <summary>The chip appeared because of a receipt: it slides in from the left.</summary>
    public void SlideIn() => Motion.ChipSlide(_border, _transform);

    /// <summary>
    /// A family's icon (Segoe Fluent Icons; Tor has no glyph, so two ellipses draw its onion), in
    /// the family's colour; null for "other".
    /// </summary>
    public static UIElement? Icon(PathFamily family, double size, string styleSuffix = "IconStyle")
    {
        string? glyph = family switch
        {
            PathFamily.Lan => "\uE701",
            PathFamily.Nearby => "\uE702",
            PathFamily.Direct => "\uE774",
            PathFamily.Relay => "\uE71B",
            PathFamily.Friend => "\uE715",
            _ => null,
        };
        if (family == PathFamily.Tor)
        {
            var onion = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            onion.Children.Add(new Ellipse { Style = Ui.Style("RavenOnionStyle"), Width = size * 0.82, Height = size, HorizontalAlignment = HorizontalAlignment.Center });
            onion.Children.Add(new Ellipse { Style = Ui.Style("RavenOnionStyle"), Width = size * 0.38, Height = size, HorizontalAlignment = HorizontalAlignment.Center });
            AutomationProperties.SetAccessibilityView(onion, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            return onion;
        }
        if (glyph is null) return null;
        string style = styleSuffix == "IconStyle" ? $"RavenChip{family}IconStyle" : $"RavenWayTile{family}IconStyle";
        var icon = new FontIcon { Glyph = glyph, Style = Ui.Style(style), FontSize = size };
        AutomationProperties.SetAccessibilityView(icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return icon;
    }
}
