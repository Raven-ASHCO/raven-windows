using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Helpers;
using Windows.Foundation;

namespace Raven.Windows.Controls;

/// <summary>
/// A round avatar (colour from tokens' avatarRule, the first letter of the name) with, when asked,
/// the presence dot (in touch / waiting / none). Honest motion only: the dot blinks when presence
/// changes, a ring grows once on a retry, and an "in touch" dot breathes slowly (a halo behind it)
/// while that is true and the window is shown. Decorative for Narrator: the row says it all.
/// </summary>
public sealed class Avatar
{
    private readonly Border _circle;
    private readonly TextBlock _initial;
    private readonly Ellipse? _dot;
    private readonly Ellipse? _halo;
    private readonly Ellipse? _ring;
    private readonly ScaleTransform _ringScale = new() { ScaleX = 1, ScaleY = 1 };
    private readonly double _size;
    private Storyboard? _breathing;
    private string _presence = string.Empty;
    private bool _active = true;

    /// <param name="size">tokens.json avatarSize (36), avatarSmall (22) or avatarLarge (40).</param>
    /// <param name="withDot">Show the presence dot (friends in the sidebar and the chat header).</param>
    public Avatar(double size, bool withDot)
    {
        _size = size;
        _initial = new TextBlock();
        _circle = new Border { Child = _initial };
        Root = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        Root.Children.Add(_circle);
        AutomationProperties.SetAccessibilityView(Root, AccessibilityView.Raw);
        if (withDot)
        {
            double dot = size >= 30 ? 11 : 8;
            _halo = new Ellipse
            {
                Style = Ui.Style("RavenHaloStyle"),
                Width = dot + 6,
                Height = dot + 6,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -4, -4),
                Opacity = 0,
                IsHitTestVisible = false,
            };
            _ring = new Ellipse
            {
                Style = Ui.Style("RavenRetryRingStyle"),
                Width = dot,
                Height = dot,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -1, -1),
                Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _ringScale,
                IsHitTestVisible = false,
            };
            _dot = new Ellipse
            {
                Width = dot,
                Height = dot,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -1, -1),
                IsHitTestVisible = false,
            };
            Root.Children.Add(_halo);
            Root.Children.Add(_ring);
            Root.Children.Add(_dot);
        }
    }

    public Grid Root { get; }

    /// <summary>A friend or a member: the colour from the address, the letter from the name.</summary>
    public void Set(string? address, string name)
    {
        int index = AvatarRule.IndexFor(address);
        Apply($"RavenAvatar{index}Style", $"RavenAvatarText{index}Style", AvatarRule.Initial(name));
    }

    /// <summary>You (the sidebar's You row): the accent's soft tint.</summary>
    public void SetYou(string name) => Apply("RavenAvatarYouStyle", "RavenAvatarTextYouStyle", AvatarRule.Initial(name));

    private void Apply(string circleStyle, string textStyle, string initial)
    {
        _circle.Style = Ui.Style(circleStyle);
        _circle.Width = _size;
        _circle.Height = _size;
        _circle.CornerRadius = new CornerRadius(_size / 2);
        _initial.Style = Ui.Style(textStyle);
        _initial.FontSize = _size >= 40 ? 15 : _size >= 30 ? 14 : _size >= 20 ? 10 : 9;
        _initial.Text = initial;
    }

    /// <summary>The presence dot: blinks once when a presence event changed it.</summary>
    public void SetPresence(string presence, bool animate)
    {
        if (_dot is null || presence == _presence) return;
        _presence = presence;
        StopBreathing();
        _dot.Style = Ui.Style(presence switch
        {
            Presences.InTouch => "RavenDotInTouchStyle",
            Presences.Waiting => "RavenDotWaitingStyle",
            _ => "RavenDotNoneStyle",
        });
        // The style sets the token size; the dot keeps this avatar's size.
        _dot.Width = _ring!.Width;
        _dot.Height = _ring.Height;
        if (animate) Motion.Blink(_dot, StartBreathingIfInTouch);
        else StartBreathingIfInTouch();
    }

    /// <summary>retry event: a ring grows once around the dot.</summary>
    public void ShowRetry()
    {
        if (_ring is not null) Motion.RetryRing(_ring, _ringScale);
    }

    /// <summary>Window shown or hidden, or reduced motion changed: the breathing follows.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        StopBreathing();
        StartBreathingIfInTouch();
    }

    private void StartBreathingIfInTouch()
    {
        if (_halo is not null && _active && _breathing is null && _presence == Presences.InTouch) _breathing = Motion.Breathe(_halo);
    }

    private void StopBreathing()
    {
        _breathing?.Stop();
        _breathing = null;
        if (_halo is not null) _halo.Opacity = 0;
        if (_dot is not null) _dot.Opacity = 1;
    }
}
