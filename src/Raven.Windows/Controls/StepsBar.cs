using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;

namespace Raven.Windows.Controls;

/// <summary>
/// Setup's steps (Calm): Your name, Receive messages, Your identity, Security words. A step done
/// has a green check, the current one its number on the accent, the bar between two done steps is
/// green. Narrator reads the current one as "Your identity, 3/4".
/// </summary>
public sealed class StepsBar
{
    private static readonly string[] Keys = { "setup.step.name", "setup.step.receive", "setup.step.identity", "setup.step.words" };

    private readonly List<(Border Circle, TextBlock Mark, TextBlock Label)> _steps = new();
    private readonly List<Rectangle> _bars = new();

    public StepsBar()
    {
        Root = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 10) };
        for (int i = 0; i < Keys.Length; i++)
        {
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (i < Keys.Length - 1) Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 12 });
            var mark = new TextBlock();
            var circle = new Border { Child = mark };
            TextBlock label = Ui.Text(Loc.Get(Keys[i]), "RavenMetaTertiaryTextStyle");
            var step = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            step.Children.Add(circle);
            step.Children.Add(label);
            Grid.SetColumn(step, i * 2);
            Root.Children.Add(step);
            _steps.Add((circle, mark, label));
            if (i < Keys.Length - 1)
            {
                var bar = new Rectangle { Style = Ui.Style("RavenStepBarStyle") };
                Grid.SetColumn(bar, i * 2 + 1);
                Root.Children.Add(bar);
                _bars.Add(bar);
            }
        }
        AutomationProperties.SetName(Root, string.Join(", ", System.Array.ConvertAll(Keys, Loc.Get)));
        Set(0);
    }

    public Grid Root { get; }

    /// <summary>The current step, 0 to 3.</summary>
    public void Set(int current)
    {
        for (int i = 0; i < _steps.Count; i++)
        {
            (Border circle, TextBlock mark, TextBlock label) = _steps[i];
            bool done = i < current;
            bool now = i == current;
            circle.Style = Ui.Style(done ? "RavenStepDoneStyle" : now ? "RavenStepNowStyle" : "RavenStepLaterStyle");
            mark.Style = Ui.Style(done || now ? "RavenStepOnTextStyle" : "RavenStepTextStyle");
            mark.Text = done ? "✓" : (i + 1).ToString(CultureInfo.InvariantCulture);
            label.Style = Ui.Style(now ? "RavenMetaStrongTextStyle" : "RavenMetaTertiaryTextStyle");
            label.FontWeight = now ? FontWeights.SemiBold : FontWeights.Normal;
            if (now) AutomationProperties.SetName(circle.Parent as UIElement ?? circle, Loc.Get(Keys[i]) + ", " + (i + 1).ToString(CultureInfo.InvariantCulture) + "/" + Keys.Length.ToString(CultureInfo.InvariantCulture));
        }
        for (int i = 0; i < _bars.Count; i++) _bars[i].Style = Ui.Style(i < current ? "RavenStepBarDoneStyle" : "RavenStepBarStyle");
    }
}
