using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;
using Raven.Windows.Services;

namespace Raven.Windows.Dialogs;

/// <summary>
/// Message details: status, why, what you can do (texts from the engine), with
/// Try now / Stop trying (waiting) or Send again (time ran out, not delivered), and the
/// technical text under Details.
/// </summary>
public sealed class MessageDetailsDialog : ContentDialog
{
    private readonly RavenSession _session;
    private readonly Message _message;
    private readonly InfoBar _errorBar = new() { IsClosable = true, IsOpen = false };

    public MessageDetailsDialog(RavenSession session, MessageDetails details, string friendName)
    {
        _session = session;
        _message = details.Message;

        Title = Loc.Format("message.title", ("name", friendName), ("when", RelativeTime.Ago(_message.AtMs, RavenSession.NowMs)));

        var panel = new StackPanel { Spacing = 10, MaxWidth = 480 };
        TextBlock quote = Ui.Text(_message.Text, "RavenSecondaryTextStyle");
        quote.MaxLines = 3;
        quote.TextTrimming = TextTrimming.CharacterEllipsis;
        panel.Children.Add(quote);
        AddIfAny(panel, details.StatusText, "RavenBodyTextStyle");
        AddIfAny(panel, details.WhyText, "RavenSecondaryTextStyle");
        AddIfAny(panel, details.ActionText, "RavenBodyTextStyle");
        panel.Children.Add(_errorBar);
        if (!string.IsNullOrWhiteSpace(details.Detail))
        {
            panel.Children.Add(new Expander
            {
                Header = Loc.Get("message.details"),
                Content = Ui.Text(details.Detail, "RavenMonoTextStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            });
        }
        // The developer fields (ids, raven-node's own words), one more step away.
        if (!string.IsNullOrWhiteSpace(details.Technical))
        {
            panel.Children.Add(new Expander
            {
                Header = Loc.Get("message.technicalDetails"),
                Content = Ui.Text(details.Technical, "RavenMonoTextStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            });
        }
        Content = panel;

        switch (_message.State)
        {
            case MessageStates.Waiting:
                PrimaryButtonText = Loc.Get("message.tryNow");
                SecondaryButtonText = Loc.Get("message.stopTrying");
                break;
            case MessageStates.TimeRanOut:
            case MessageStates.Failed:
            case MessageStates.NotSentMeetFirst:
                PrimaryButtonText = Loc.Get("message.sendAgain");
                break;
        }
        CloseButtonText = Loc.Get("common.close");
        DefaultButton = ContentDialogButton.Close;

        PrimaryButtonClick += (_, args) => RunAction(args, () => _session.Engine.MessagesRetryAsync(_message.Id));
        SecondaryButtonClick += (_, args) => RunAction(args, () => _session.Engine.MessagesStopAsync(_message.Id));
    }

    /// <summary>The message after Try now / Stop trying / Send again, if one was used.</summary>
    public Message? UpdatedMessage { get; private set; }

    private async void RunAction(ContentDialogButtonClickEventArgs args, Func<Task<Message>> action)
    {
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        try
        {
            UpdatedMessage = await action();
        }
        catch (Exception ex)
        {
            args.Cancel = true; // stay open and say what happened
            Ui.ShowError(_errorBar, ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static void AddIfAny(StackPanel panel, string text, string style)
    {
        if (!string.IsNullOrWhiteSpace(text)) panel.Children.Add(Ui.Text(text, style));
    }
}
