using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.ViewModels;
using Raven.Windows.Helpers;
using Raven.Windows.Services;
using VirtualKey = Windows.System.VirtualKey;

namespace Raven.Windows.Views;

/// <summary>
/// Raven is locked. Two kinds of lock:
/// <list type="bullet">
/// <item>`raven lock` stopped Raven (profile.state "locked"): Unlock is privacy.unlock.</item>
/// <item>Lock v2 (lock.state "locked"): the background program keeps running without its keys;
/// Unlock is lock.unlock with the Raven passphrase when the profile has one. The passphrase goes
/// only to the engine (and on to the background program) and is cleared from the field after
/// every try. A wrong one, a missing one or too many tries show the engine's words.</item>
/// </list>
/// </summary>
public sealed partial class LockedPage : UserControl
{
    private readonly RavenSession _session;
    private readonly Action _onUnlocked;
    private readonly LockViewModel? _lockV2;

    /// <param name="lockV2">The Lock v2 state when Lock v2 locked Raven; null for a `raven lock`.</param>
    public LockedPage(RavenSession session, Action onUnlocked, LockState? lockV2 = null)
    {
        _session = session;
        _onUnlocked = onUnlocked;
        InitializeComponent();

        if (lockV2 is not null)
        {
            _lockV2 = new LockViewModel(session.Engine);
            bool asks = lockV2.HasPassphrase != false;
            BodyText.Visibility = Visibility.Visible;
            PassphraseBox.Visibility = asks ? Visibility.Visible : Visibility.Collapsed;
            PassphraseNote.Visibility = asks ? Visibility.Visible : Visibility.Collapsed;
            NoPassphraseText.Visibility = asks ? Visibility.Collapsed : Visibility.Visible;
        }

        Loaded += (_, _) =>
        {
            Motion.FadeIn(LockedRoot, CalmMs.LockedFade);
            if (PassphraseBox.Visibility == Visibility.Visible) PassphraseBox.Focus(FocusState.Programmatic);
            else UnlockButton.Focus(FocusState.Programmatic);
        };
    }

    private void OnPassphraseKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        _ = UnlockAsync();
    }

    private void OnUnlock(object sender, RoutedEventArgs e) => _ = UnlockAsync();

    private async Task UnlockAsync()
    {
        if (!UnlockButton.IsEnabled) return;
        UnlockButton.IsEnabled = false;
        try
        {
            if (_lockV2 is not null)
            {
                string passphrase = PassphraseBox.Password;
                PassphraseBox.Password = string.Empty; // never kept on screen after a try
                bool unlocked = await _lockV2.UnlockAsync(passphrase);
                if (_lockV2.State is LockState state) _session.SetLock(state);
                if (unlocked)
                {
                    Motion.FadeOut(LockedRoot, CalmMs.UnlockFade, _onUnlocked);
                    return;
                }
                if (_lockV2.Error is ErrorInfo error)
                    Ui.ShowError(ErrorBar, new RavenException(error.Code, error.Message, error.Detail));
                Motion.Shake(CardShake); // a wrong (or missing) passphrase
                PassphraseBox.Focus(FocusState.Programmatic);
            }
            else
            {
                ProfileState state = await _session.Engine.PrivacyUnlockAsync();
                if (state.State == ProfileStates.Ready) Motion.FadeOut(LockedRoot, CalmMs.UnlockFade, _onUnlocked);
            }
        }
        catch (Exception ex)
        {
            Ui.ShowError(ErrorBar, ex);
        }
        finally
        {
            UnlockButton.IsEnabled = true;
        }
    }
}
