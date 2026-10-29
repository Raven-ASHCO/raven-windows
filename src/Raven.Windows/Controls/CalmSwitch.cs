using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;

namespace Raven.Windows.Controls;

/// <summary>
/// The Calm switch: a 36 x 22 track that turns green when on, and a knob that springs across
/// (tokens.json switchKnob, curve knob) while the colour fades (switchColor). A ToggleButton under
/// the hood, so Space toggles it, it is one Tab stop and Narrator reads it as a toggle switch with
/// its state. The motion runs only on the person's own toggle; a change from the engine (a
/// refresh) moves the knob without animating it.
/// </summary>
public sealed class CalmSwitch : ToggleButton
{
    private const double KnobOffX = 0;
    private const double KnobOnX = 14; // 36 - 16 - 2 x 3

    private UIElement? _onTrack;
    private readonly TranslateTransform _knob = new();
    private bool _quiet;

    public CalmSwitch()
    {
        Style = Ui.Style("RavenSwitchStyle");
        Checked += (_, _) => OnChanged();
        Unchecked += (_, _) => OnChanged();
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.55;
    }

    /// <summary>The person turned it on or off (not raised for <see cref="SetQuietly"/>).</summary>
    public event Action<CalmSwitch>? Toggled;

    public bool IsOn
    {
        get => IsChecked == true;
        set => IsChecked = value;
    }

    /// <summary>Shows the engine's value without animating and without raising <see cref="Toggled"/>.</summary>
    public void SetQuietly(bool on)
    {
        if (IsOn == on)
        {
            Place(animate: false);
            return;
        }
        _quiet = true;
        try
        {
            IsOn = on;
        }
        finally
        {
            _quiet = false;
        }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _onTrack = GetTemplateChild("OnTrack") as UIElement;
        if (GetTemplateChild("Knob") is UIElement knob) knob.RenderTransform = _knob;
        Place(animate: false);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SwitchPeer(this);

    private void OnChanged()
    {
        Place(animate: !_quiet && IsLoaded);
        if (!_quiet) Toggled?.Invoke(this);
    }

    private void Place(bool animate)
    {
        if (_onTrack is null)
        {
            _knob.X = IsOn ? KnobOnX : KnobOffX;
            return;
        }
        Motion.Knob(_knob, _onTrack, IsOn ? KnobOnX : KnobOffX, IsOn ? 1 : 0, animate);
    }

    /// <summary>Narrator: "Reduced motion, toggle switch, on".</summary>
    private sealed class SwitchPeer : ToggleButtonAutomationPeer
    {
        public SwitchPeer(ToggleButton owner) : base(owner)
        {
        }

        protected override string GetLocalizedControlTypeCore() => Loc.Get("a11y.switch");
    }
}
