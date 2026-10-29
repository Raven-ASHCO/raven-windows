using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Dialogs;
using Raven.Windows.Helpers;
using Raven.Windows.Services;
using Raven.Windows.Views;
using Windows.Graphics;

namespace Raven.Windows;

/// <summary>
/// The one Raven window. It shows Welcome and Setup on first run, then the main shell;
/// closing it hides Raven to the notification area (the background program keeps
/// receiving either way). Quitting follows Settings &gt; "When I quit".
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly RavenSession _session;
    private readonly IntPtr _hwnd;
    private ShellPage? _shell;
    private bool _quitting;

    public MainWindow(RavenSession session)
    {
        _session = session;
        InitializeComponent();

        Title = Loc.Get("app.name");
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        try
        {
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Raven.ico"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Raven: window icon: " + ex.Message);
        }

        ApplyTheme(App.Current?.Prefs.Theme ?? ThemeChoice.System);
        TryMica();

        double scale = NativeWindow.Scale(_hwnd);
        // tokens.json layout.minWindow, in DIPs: physical pixels follow the screen's scale.
        NativeWindow.SetMinimumSize(_hwnd, WindowPlacement.MinWidthDip, WindowPlacement.MinHeightDip);
        PlaceInWorkArea((int)(1040 * scale), (int)(700 * scale), scale);

        AppWindow.Closing += OnClosing;
        VisibilityChanged += (_, e) => OnVisibilityChanged(e.Visible);
    }

    public XamlRoot? DialogRoot => Root.XamlRoot;

    /// <summary>
    /// Settings &gt; Appearance &gt; Theme: System follows Windows; Light and Dark set the window's
    /// theme (dialogs follow it too, see Ui.ShowDialogAsync).
    /// </summary>
    public void ApplyTheme(ThemeChoice theme)
    {
        Root.RequestedTheme = theme switch
        {
            ThemeChoice.Light => ElementTheme.Light,
            ThemeChoice.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    /// <summary>
    /// Mica behind the sidebar where Windows has it (Windows 11) and high contrast is off: the
    /// window's root turns see-through and the sidebar's pane is translucent over it (the pages keep
    /// their own Calm background). Elsewhere the root keeps the Calm background.
    /// </summary>
    private void TryMica()
    {
        try
        {
            if (!Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported()) return;
            if (new global::Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) return;
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            Root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Raven: Mica: " + ex.Message);
        }
    }

    /// <summary>The page shown when Raven was locked: Unlock comes back to it (E1).</summary>
    private string? _pageBeforeLock;

    /// <summary>
    /// Opens the window inside its monitor's work area, never under the taskbar (E1: 1300x875 at
    /// 224,224 on a 1920x1020 work area put the message box 79 px under it).
    /// </summary>
    private void PlaceInWorkArea(int width, int height, double scale)
    {
        try
        {
            DisplayArea display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
            RectInt32 work = display.WorkArea;
            (int minWidth, int minHeight) = WindowPlacement.MinimumPixels(scale);
            WindowPlacement.Rect fit = WindowPlacement.Fit(
                new WindowPlacement.Rect(AppWindow.Position.X, AppWindow.Position.Y, width, height),
                new WindowPlacement.Rect(work.X, work.Y, work.Width, work.Height),
                minWidth, minHeight);
            AppWindow.MoveAndResize(new RectInt32(fit.X, fit.Y, fit.Width, fit.Height));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Raven: window placement: " + ex.Message);
            AppWindow.Resize(new SizeInt32(width, height));
        }
    }

    /// <summary>Asks the engine where we are and opens the matching screen.</summary>
    public async Task StartAsync()
    {
        try
        {
            ProfileState profile = await _session.Engine.ProfileStateAsync();
            try
            {
                await _session.RefreshSettingsAsync();
            }
            catch (RavenException ex)
            {
                Debug.WriteLine($"Raven: settings.get before setup: {ex.Code}");
            }
            await _session.RefreshPrivacyAsync(); // "Lock now" and the privacy settings follow it

            switch (profile.State)
            {
                case ProfileStates.New:
                    ShowWelcome();
                    break;
                case ProfileStates.Ready:
                    // Lock v2 keeps the background program running without its keys: open on
                    // the Locked screen when it is locked.
                    if (await _session.RefreshLockAsync() is { Locked: true } lockV2) ShowLocked(lockV2);
                    else await ShowShellAsync();
                    break;
                case ProfileStates.Locked:
                    ShowLocked();
                    break;
                default:
                    ShowError(new RavenException(RavenErrorCodes.ProfileUnreadable,
                        string.IsNullOrWhiteSpace(profile.Reason) ? Loc.Get("error.badResponse") : profile.Reason,
                        "profile.state = " + profile.State));
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    public void ShowAndActivate()
    {
        bool wasHidden = !AppWindow.IsVisible;
        AppWindow.Show();
        Activate();
        NativeWindow.BringToFront(_hwnd);
        if (wasHidden) OnVisibilityChanged(true); // back to polling every 500 ms (also if VisibilityChanged does not fire)
    }

    /// <summary>True while the main screen (friends and chat) is shown: not Welcome, Setup, Locked or an error.</summary>
    public bool IsShellShown => _shell is not null;

    /// <summary>The friends in the sidebar's order with their unread counts (tray menu).</summary>
    public IReadOnlyList<(Contact Contact, int Unread)> TrayFriends() =>
        _shell?.TrayFriends() ?? Array.Empty<(Contact Contact, int Unread)>();

    /// <summary>A friend's line in the tray menu: show Raven on that chat.</summary>
    public Task OpenFriendAsync(string address)
    {
        ShowAndActivate();
        _shell?.SelectFriend(address);
        return Task.CompletedTask;
    }

    public async Task OpenAddFriendAsync()
    {
        ShowAndActivate();
        if (_shell is not null) await _shell.OpenAddFriendAsync();
    }

    /// <summary>
    /// "Lock now" (tray): Lock v2 when the running background program serves it (it keeps
    /// running without its keys), else the privacy pack's lock when privacy.state offers it.
    /// </summary>
    public async Task LockAsync()
    {
        if (!_session.CanLockV2 && !_session.Privacy.Available) return;
        // Ask first, saying what Lock does and doesn't protect (E1).
        ShowAndActivate();
        if (DialogRoot is null || !await ConfirmLockDialog.AskAsync(_session.LockV2, DialogRoot)) return;
        try
        {
            if (_session.CanLockV2)
            {
                LockState locked = await _session.Engine.LockNowAsync();
                _session.SetLock(locked);
                if (locked.Locked) ShowLocked(locked);
                return;
            }
            ProfileState state = await _session.Engine.PrivacyLockAsync();
            if (state.State == ProfileStates.Locked) ShowLocked();
        }
        catch (Exception ex)
        {
            ShowAndActivate();
            if (_shell is not null) _shell.ShowError(ex);
        }
    }

    /// <summary>
    /// Quit the window app. <paramref name="stopBackground"/> also stops the background
    /// program; otherwise Settings &gt; "When I quit" decides (keep / stop if started here / ask).
    /// </summary>
    public async Task RequestQuitAsync(bool stopBackground)
    {
        if (stopBackground)
        {
            await StopNodeQuietlyAsync();
            App.Current?.ExitApp();
            return;
        }

        switch (_session.Settings.WhenQuit)
        {
            case WhenQuitOptions.Keep:
                App.Current?.ExitApp();
                return;
            case WhenQuitOptions.StopIfStarted:
                if (_session.Node?.StartedByApp == true) await StopNodeQuietlyAsync();
                App.Current?.ExitApp();
                return;
        }

        ShowAndActivate();
        if (DialogRoot is null) return;
        ContentDialogResult choice = await Ui.ShowDialogAsync(QuitDialog.Create(), DialogRoot);
        switch (choice)
        {
            case ContentDialogResult.Primary:
                App.Current?.ExitApp();
                break;
            case ContentDialogResult.Secondary:
                await StopNodeQuietlyAsync();
                App.Current?.ExitApp();
                break;
        }
    }

    /// <summary>Closes the window for good (called while the app exits).</summary>
    public void CloseForExit()
    {
        _quitting = true;
        _shell?.SetActive(false);
        Close();
    }

    // ------------------------------------------------------------------ screens

    private void ShowWelcome()
    {
        _shell = null;
        Host.Content = new WelcomePage(ShowSetup);
    }

    private void ShowSetup()
    {
        Host.Content = new SetupPage(_session, () => _ = ShowShellAsync());
    }

    private async Task ShowShellAsync()
    {
        var shell = new ShellPage(_session);
        shell.Erased += ShowWelcomeAfterErase;
        shell.Locked += state => ShowLocked(state);
        shell.ThemeChosen += ApplyTheme;
        _shell = shell;
        Host.Content = shell;
        await _session.RefreshNodeAsync();
        await shell.LoadAsync();
        // After an unlock: the page that was open before (E1).
        shell.RestorePage(_pageBeforeLock);
        _pageBeforeLock = null;
        _session.SetWindowVisible(AppWindow.IsVisible);
        _session.StartPolling();
    }

    /// <summary>privacy.erase succeeded: Raven is new on this computer again, so start at Welcome.</summary>
    private void ShowWelcomeAfterErase()
    {
        _session.ResetAfterErase();
        _shell?.SetActive(false);
        ShowWelcome();
    }

    /// <param name="lockV2">The Lock v2 state when Lock v2 locked Raven; null for a `raven lock`.</param>
    private void ShowLocked(LockState? lockV2 = null)
    {
        _session.StopPolling();
        _pageBeforeLock = _shell?.CurrentPage ?? _pageBeforeLock;
        _shell?.SetActive(false);
        _shell = null;
        Host.Content = new LockedPage(_session, () => _ = ShowShellAsync(), lockV2);
    }

    private void ShowError(Exception ex)
    {
        _session.StopPolling();
        _shell = null;
        Host.Content = new ErrorPage(ex);
    }

    // ------------------------------------------------------------------ window events

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_quitting) return;
        args.Cancel = true;
        if (App.Current?.HasTray == true)
        {
            // Hide to the notification area; Raven keeps receiving and polls every 5 s.
            sender.Hide();
            OnVisibilityChanged(false);
        }
        else
        {
            _ = RequestQuitAsync(stopBackground: false);
        }
    }

    private void OnVisibilityChanged(bool visible)
    {
        _session.SetWindowVisible(visible);
        _shell?.SetActive(visible);
    }

    private async Task StopNodeQuietlyAsync()
    {
        try
        {
            _session.SetNode(await _session.Engine.NodeStopAsync());
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Raven: node.stop failed: " + ex.Message);
        }
    }
}
