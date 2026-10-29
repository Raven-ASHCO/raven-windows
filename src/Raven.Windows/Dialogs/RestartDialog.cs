using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;
using Raven.Windows.Core.ViewModels;
using Raven.Windows.Helpers;

namespace Raven.Windows.Dialogs;

/// <summary>
/// "Restart the background program?" after a change that raven-node applies only when it
/// starts again (network.set restart_needed). Honest about what the app may do: on Windows it
/// stops only a background program it started itself, and never a login service's; then the
/// result says how to restart it by hand. When Internet direct or p2p was switched on, it also
/// names the Windows Firewall prompt that may appear and the ports.
/// </summary>
internal static class RestartDialog
{
    /// <summary>True when the person chose "Restart now".</summary>
    public static async Task<bool> AskAsync(RestartPrompt prompt, IReadOnlyList<string> firewallLines, XamlRoot root)
    {
        var panel = new StackPanel { Spacing = 10, MaxWidth = 480 };
        panel.Children.Add(Ui.Text(Loc.Get("network.restart.body")));

        List<string> effects = prompt.Changes.Select(c => c.Effect).Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        if (effects.Count > 0)
        {
            panel.Children.Add(Ui.Text(Loc.Get("network.restart.waiting"), "RavenSecondaryTextStyle"));
            foreach (string effect in effects) panel.Children.Add(Ui.Text("• " + effect, "RavenSecondaryTextStyle"));
        }

        if (!prompt.StartedByApp) panel.Children.Add(Ui.Text(Loc.Get("network.restart.notStartedHere"), "RavenWarningTextStyle"));

        bool opensPorts = prompt.Changes.Any(c => c.Setting is NetworkSettings.Internet or NetworkSettings.P2p);
        if (opensPorts && firewallLines.Count > 0)
        {
            TextBlock title = Ui.Text(Loc.Get("network.firewall.title"));
            title.FontWeight = FontWeights.SemiBold;
            panel.Children.Add(title);
            foreach (string line in firewallLines) panel.Children.Add(Ui.Text(line, "RavenSecondaryTextStyle"));
        }

        var dialog = new ContentDialog
        {
            Title = Loc.Get("network.restart.title"),
            Content = panel,
            PrimaryButtonText = Loc.Get("network.restart.now"),
            CloseButtonText = Loc.Get("network.restart.later"),
            DefaultButton = ContentDialogButton.Primary,
        };
        return await Ui.ShowDialogAsync(dialog, root) == ContentDialogResult.Primary;
    }
}
