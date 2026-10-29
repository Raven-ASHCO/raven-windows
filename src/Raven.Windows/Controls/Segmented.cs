using System;
using System.Collections.Generic;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Raven.Windows.Helpers;

namespace Raven.Windows.Controls;

/// <summary>
/// The Calm segmented control (Theme: System / Light / Dark; Keep chats for: 7 / 30 / 90 days /
/// Always): radio buttons on a soft track with a thumb that slides and resizes to the chosen one
/// (tokens.json segmentThumb, curve thumb). One Tab stop; the arrow keys move between the
/// options and choose them, as in a radio group; Narrator reads each as a radio button of the
/// group named <c>label</c>.
/// </summary>
public sealed class Segmented : UserControl
{
    private readonly List<RadioButton> _options = new();
    private readonly List<TextBlock> _labels = new();
    private readonly Border _thumb;
    private readonly TranslateTransform _thumbX = new();
    private readonly string _group = "seg" + Guid.NewGuid().ToString("N");
    private bool _quiet;

    public Segmented(string label, IReadOnlyList<string> options, int selected)
    {
        _thumb = new Border
        {
            Style = Ui.Style("RavenSegmentThumbStyle"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            RenderTransform = _thumbX,
            IsHitTestVisible = false,
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            TabFocusNavigation = KeyboardNavigationMode.Once,
            XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled,
        };
        AutomationProperties.SetName(row, label);
        for (int i = 0; i < options.Count; i++)
        {
            TextBlock text = Ui.Text(options[i], "RavenCaptionTextStyle");
            text.TextWrapping = TextWrapping.NoWrap;
            var option = new RadioButton
            {
                Style = Ui.Style("RavenPlainRadioStyle"),
                GroupName = _group,
                Content = text,
                Padding = new Thickness(12, 5, 12, 5),
                CornerRadius = new CornerRadius(7),
                IsChecked = i == selected,
            };
            AutomationProperties.SetName(option, options[i]);
            int index = i;
            option.Checked += (_, _) => OnChosen(index);
            option.GotFocus += (_, _) =>
            {
                // Arrow keys choose as they move, like a radio group.
                if (option.FocusState == FocusState.Keyboard && option.IsChecked != true) option.IsChecked = true;
            };
            option.SizeChanged += (_, _) => PlaceThumb(animate: false);
            _options.Add(option);
            _labels.Add(text);
            row.Children.Add(option);
        }
        var stack = new Grid();
        stack.Children.Add(_thumb);
        stack.Children.Add(row);
        Content = new Border { Style = Ui.Style("RavenSegmentTrackStyle"), Child = stack };
        IsTabStop = false;
        SelectedIndex = selected;
        RenderLabels();
    }

    /// <summary>The person chose another option (not raised for <see cref="SetQuietly"/>).</summary>
    public event Action<int>? SelectionChanged;

    public int SelectedIndex { get; private set; }

    /// <summary>Shows the engine's (or the saved) choice without motion and without raising <see cref="SelectionChanged"/>.</summary>
    public void SetQuietly(int index)
    {
        if (index < 0 || index >= _options.Count) return;
        _quiet = true;
        try
        {
            _options[index].IsChecked = true;
            SelectedIndex = index;
            RenderLabels();
            PlaceThumb(animate: false);
        }
        finally
        {
            _quiet = false;
        }
    }

    private void OnChosen(int index)
    {
        bool changed = index != SelectedIndex;
        SelectedIndex = index;
        RenderLabels();
        PlaceThumb(animate: !_quiet && IsLoaded);
        if (changed && !_quiet) SelectionChanged?.Invoke(index);
    }

    private void RenderLabels()
    {
        for (int i = 0; i < _labels.Count; i++)
        {
            bool on = i == SelectedIndex;
            _labels[i].Style = Ui.Style(on ? "RavenCaptionPrimaryTextStyle" : "RavenCaptionTextStyle");
            _labels[i].FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void PlaceThumb(bool animate)
    {
        if (SelectedIndex < 0 || SelectedIndex >= _options.Count) return;
        double x = 0;
        for (int i = 0; i < SelectedIndex; i++) x += _options[i].ActualWidth;
        double width = _options[SelectedIndex].ActualWidth;
        if (width <= 0) return; // not laid out yet: SizeChanged places it
        Motion.Thumb(_thumb, _thumbX, x, width, animate);
    }
}
