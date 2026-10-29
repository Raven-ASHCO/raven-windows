using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;
using Windows.ApplicationModel.DataTransfer;

namespace Raven.Windows.Dialogs;

/// <summary>
/// You: your name and fingerprint (as the terminal shows it), your security words (a friend
/// compares them with you) and your card with Copy (a friend adds you from it).
/// </summary>
public sealed class YouDialog : ContentDialog
{
    public YouDialog(Me me, string? settingsName)
    {
        YouPresenter.Lines you = YouPresenter.For(me, settingsName);
        Title = you.Title;

        var panel = new StackPanel { Spacing = 10, MaxWidth = 480 };
        if (you.Fingerprint is string fingerprint)
        {
            TextBlock line = Ui.Text(fingerprint, "RavenBodyTextStyle");
            line.IsTextSelectionEnabled = true;
            panel.Children.Add(line);
        }
        if (me.Words.Count > 0)
        {
            panel.Children.Add(Ui.Text(Loc.Get("you.words"), "RavenSecondaryTextStyle"));
            TextBlock words = Ui.Text(string.Join("  ", me.Words), "RavenMonoTextStyle");
            words.TextWrapping = TextWrapping.Wrap;
            words.IsTextSelectionEnabled = true;
            panel.Children.Add(words);
        }
        string card = TextSanitizer.SingleLine(me.Card);
        if (card.Length > 0)
        {
            panel.Children.Add(Ui.Text(Loc.Get("you.card"), "RavenSecondaryTextStyle"));
            TextBlock cardText = Ui.Text(card, "RavenMonoTextStyle");
            cardText.TextWrapping = TextWrapping.Wrap;
            cardText.MaxLines = 4;
            cardText.IsTextSelectionEnabled = true;
            panel.Children.Add(cardText);
            PrimaryButtonText = Loc.Get("add.share.copy");
            PrimaryButtonClick += (_, args) =>
            {
                args.Cancel = true; // stay open after copying
                var data = new DataPackage();
                data.SetText(card);
                Clipboard.SetContent(data);
                PrimaryButtonText = "✓ " + Loc.Get("add.share.copied");
            };
        }
        Content = panel;
        CloseButtonText = Loc.Get("common.close");
        DefaultButton = ContentDialogButton.Close;
    }
}
