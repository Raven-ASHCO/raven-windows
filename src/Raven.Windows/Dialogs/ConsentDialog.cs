using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Core.ViewModels;
using Raven.Windows.Helpers;

namespace Raven.Windows.Dialogs;

/// <summary>
/// The consent sheet before a setting the engine needs a yes for is turned on (carry mail for
/// friends, friends carry mine, handoff; API.md, the consent rule): the engine's own question
/// (the words raven asks in a terminal), then what it does, what it costs and what others can
/// and can't see. Only "I agree, turn it on" agrees; Enter and Esc choose "Not now", and
/// nothing is sent until the person agrees.
/// </summary>
internal static class ConsentDialog
{
    /// <summary>True when the person chose "I agree, turn it on".</summary>
    public static async Task<bool> AskAsync(ConsentRequest request, XamlRoot root)
    {
        ConsentTopic topic = request.Topic;
        var panel = new StackPanel { Spacing = 12, MaxWidth = 520 };

        if (!string.IsNullOrWhiteSpace(request.EngineText))
        {
            panel.Children.Add(Heading(Loc.Get("network.consent.engine")));
            TextBlock words = Ui.Text(request.EngineText);
            words.IsTextSelectionEnabled = true;
            panel.Children.Add(new Border { Style = Ui.Style("RavenCardStyle"), Child = words });
        }
        AddSection(panel, Loc.Get("network.consent.does"), topic.Does);
        AddSection(panel, Loc.Get("network.consent.costs"), topic.Costs);
        AddSection(panel, Loc.Get("network.consent.sees"), topic.Sees);
        AddSection(panel, Loc.Get("network.consent.hidden"), topic.Hidden);

        var dialog = new ContentDialog
        {
            Title = topic.Title,
            Content = panel,
            PrimaryButtonText = Loc.Get("network.consent.agree"),
            CloseButtonText = Loc.Get("network.consent.notNow"),
            // Enter never agrees: the person clicks (or tabs to) "I agree, turn it on".
            DefaultButton = ContentDialogButton.Close,
        };
        return await Ui.ShowDialogAsync(dialog, root) == ContentDialogResult.Primary;
    }

    private static void AddSection(StackPanel panel, string title, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var section = new StackPanel { Spacing = 2 };
        section.Children.Add(Heading(title));
        section.Children.Add(Ui.Text(text, "RavenSecondaryTextStyle"));
        panel.Children.Add(section);
    }

    private static TextBlock Heading(string text)
    {
        TextBlock heading = Ui.Text(text);
        heading.FontWeight = FontWeights.SemiBold;
        return heading;
    }
}
