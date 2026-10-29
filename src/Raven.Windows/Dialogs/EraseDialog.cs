using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;
using Raven.Windows.Services;

namespace Raven.Windows.Dialogs;

/// <summary>
/// "Erase Raven from this computer?" (offered only when privacy.state says the privacy pack
/// is there): says what is removed, and enables Erase only after the user types "erase".
/// Then privacy.erase; on success the window starts over at Welcome.
/// </summary>
public sealed class EraseDialog : ContentDialog
{
    private readonly RavenSession _session;
    private readonly TextBox _confirm = new();
    private readonly InfoBar _errorBar = new() { IsClosable = true, IsOpen = false };

    public EraseDialog(RavenSession session)
    {
        _session = session;
        Title = Loc.Get("erase.title");

        var panel = new StackPanel { Spacing = 12, MaxWidth = 460 };
        panel.Children.Add(Ui.Text(Loc.Get("erase.body")));
        _confirm.Header = Loc.Get("erase.typeToConfirm");
        _confirm.IsSpellCheckEnabled = false;
        _confirm.IsTextPredictionEnabled = false;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_confirm, Loc.Get("erase.typeToConfirm"));
        _confirm.TextChanged += (_, _) => IsPrimaryButtonEnabled = EraseConfirmation.Matches(_confirm.Text);
        panel.Children.Add(_confirm);
        panel.Children.Add(_errorBar);
        Content = panel;

        PrimaryButtonText = Loc.Get("erase.confirm");
        CloseButtonText = Loc.Get("common.cancel");
        // No default button: Enter in the field never erases; the user clicks Erase.
        DefaultButton = ContentDialogButton.None;
        IsPrimaryButtonEnabled = false;
        PrimaryButtonClick += OnEraseClick;
        Opened += (_, _) => _confirm.Focus(FocusState.Programmatic);
    }

    /// <summary>True when privacy.erase succeeded.</summary>
    public bool Erased { get; private set; }

    private async void OnEraseClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!EraseConfirmation.Matches(_confirm.Text))
        {
            args.Cancel = true;
            return;
        }

        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        try
        {
            await _session.Engine.PrivacyEraseAsync();
            Erased = true;
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
}
