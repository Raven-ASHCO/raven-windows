using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;

namespace Raven.Windows.Dialogs;

/// <summary>
/// "Quit Raven?": explains that the background program keeps running.
/// Primary = Quit, Secondary = Quit and stop the background program, Close = Stay.
/// </summary>
internal static class QuitDialog
{
    public static ContentDialog Create() => new()
    {
        Title = Loc.Get("quit.title"),
        Content = Ui.Text(Loc.Get("quit.running")),
        PrimaryButtonText = Loc.Get("quit.quit"),
        SecondaryButtonText = Loc.Get("quit.quitAndStop"),
        CloseButtonText = Loc.Get("quit.stay"),
        DefaultButton = ContentDialogButton.Close,
    };
}
