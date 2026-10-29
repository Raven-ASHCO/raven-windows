using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;
using Raven.Windows.Services;

namespace Raven.Windows.Dialogs;

/// <summary>Rename a friend: the name only this computer shows (contacts.rename).</summary>
internal sealed class RenameDialog : ContentDialog
{
    private readonly RavenSession _session;
    private readonly Contact _contact;
    private readonly TextBox _name;
    private readonly InfoBar _error;

    public RenameDialog(RavenSession session, Contact contact)
    {
        _session = session;
        _contact = contact;
        string current = ContactPresenter.DisplayName(contact);
        Title = Loc.Format("contact.rename.title", ("name", current));
        _name = new TextBox { Text = current, MaxLength = 64, SelectionStart = 0, SelectionLength = current.Length };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_name, Loc.Format("contact.rename.title", ("name", current)));
        _error = new InfoBar { IsClosable = false, IsOpen = false };
        var panel = new StackPanel { Spacing = 8, MinWidth = 320 };
        panel.Children.Add(_name);
        panel.Children.Add(Ui.Text(Loc.Format("contact.rename.note", ("name", current)), "RavenSecondaryTextStyle"));
        panel.Children.Add(_error);
        Content = panel;
        PrimaryButtonText = Loc.Get("contact.rename.save");
        CloseButtonText = Loc.Get("common.cancel");
        DefaultButton = ContentDialogButton.Primary;
        PrimaryButtonClick += async (_, args) =>
        {
            ContentDialogButtonClickDeferral deferral = args.GetDeferral();
            try
            {
                args.Cancel = !await SaveAsync();
            }
            finally
            {
                deferral.Complete();
            }
        };
    }

    /// <summary>True once saved (or nothing changed).</summary>
    public bool Renamed { get; private set; }

    private async Task<bool> SaveAsync()
    {
        string name = TextSanitizer.SingleLine(_name.Text);
        if (name.Length == 0) return false;
        if (name == ContactPresenter.DisplayName(_contact)) return true;
        try
        {
            await _session.Engine.ContactsRenameAsync(_contact.Address, name);
            Renamed = true;
            return true;
        }
        catch (System.Exception ex)
        {
            Ui.ShowError(_error, ex);
            return false;
        }
    }
}
