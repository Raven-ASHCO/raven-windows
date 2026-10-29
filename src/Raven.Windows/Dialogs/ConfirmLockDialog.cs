using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;

namespace Raven.Windows.Dialogs;

/// <summary>
/// "Lock Raven now?": what Lock does and doesn't protect, before it locks (E1: one click
/// locked, and what Lock doesn't protect showed only on the Locked screen).
/// </summary>
internal static class ConfirmLockDialog
{
    /// <summary>The lines it says: what it does, what it doesn't protect, who can unlock it.</summary>
    public static IReadOnlyList<string> Lines(LockState? state)
    {
        var lines = new List<string> { Loc.Get("lock.confirm.does"), Loc.Get("lock.confirm.doesNot") };
        if (state?.HasPassphrase == true) lines.Add(Loc.Get("lock.v2.passphraseNote"));
        else if (state?.HasPassphrase == false) lines.Add(Loc.Get("lock.noPassphrase"));
        return lines;
    }

    /// <summary>
    /// True when the person chose Lock. The sheet shows the Calm lock; on Lock its shackle closes
    /// (450 ms, curve shackle; at once with reduced motion) before the sheet goes and Raven locks.
    /// </summary>
    public static async Task<bool> AskAsync(LockState? state, XamlRoot root)
    {
        var glyph = new LockGlyph(48, "Accent", open: true);
        glyph.Root.HorizontalAlignment = HorizontalAlignment.Left;
        var panel = new StackPanel { Spacing = 10, MaxWidth = 460 };
        panel.Children.Add(glyph.Root);
        foreach (string line in Lines(state)) panel.Children.Add(Ui.Text(line, "RavenCaptionTextStyle"));
        var dialog = new ContentDialog
        {
            Title = Loc.Get("lock.confirm.title"),
            Content = panel,
            PrimaryButtonText = Loc.Get("lock.confirm.lock"),
            CloseButtonText = Loc.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            ContentDialogButtonClickDeferral deferral = args.GetDeferral();
            glyph.Close(() => deferral.Complete());
        };
        return await Ui.ShowDialogAsync(dialog, root) == ContentDialogResult.Primary;
    }
}
