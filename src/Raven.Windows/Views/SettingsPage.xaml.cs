using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Raven.Windows.Controls;
using Raven.Windows.Core;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Core.ViewModels;
using Raven.Windows.Dialogs;
using Raven.Windows.Helpers;
using Raven.Windows.Services;
using VirtualKey = Windows.System.VirtualKey;
using WinPoint = Windows.Foundation.Point;

namespace Raven.Windows.Views;

/// <summary>
/// Calm Settings: General, Appearance, Privacy, Lock, Network, Updates and About. The selection
/// glides behind the chosen section (curve glide); the old section leaves and the new one slides
/// in in the direction of travel. Every change is one settings.set with only the changed field
/// (or lock.now / network.status / privacy.*); what the engine says is not available stays shown
/// with its honest words ("Preview, not in this release"). The theme and "Show technical details"
/// are this app's own (kept beside the app).
/// </summary>
public sealed partial class SettingsPage : UserControl
{
    // Same order as strings "settings.whenQuit.options", "settings.keepChats.options" and "settings.theme.options".
    private static readonly string[] WhenQuitValues = { WhenQuitOptions.Keep, WhenQuitOptions.StopIfStarted, WhenQuitOptions.Ask };
    private static readonly int?[] KeepChatsValues = { 7, 30, 90, null };
    private static readonly ThemeChoice[] ThemeValues = { ThemeChoice.System, ThemeChoice.Light, ThemeChoice.Dark };

    private const double RowHeight = 38;
    private const double RowSpacing = 2;

    private readonly RavenSession _session;
    private readonly Action _onErased;
    private readonly Action? _openNetwork;
    private readonly Action<LockState> _onLocked;
    private readonly Action<ThemeChoice> _onTheme;
    private readonly Action _openWords;
    private readonly SecurityViewModel _vm;
    private readonly List<(RadioButton Button, TextBlock Label, string Id)> _sections = new();
    private NetworkStatus? _network;
    private AppInfo? _app;
    private Me? _me;
    private string? _current;
    private InfoBar? _error;
    private bool _choosing;

    /// <param name="onErased">Called after privacy.erase succeeded (the window starts over at Welcome).</param>
    /// <param name="openNetwork">Shows the Network page.</param>
    /// <param name="onLocked">Lock now worked: the window shows the Locked screen.</param>
    /// <param name="onTheme">The theme was chosen: the window follows.</param>
    /// <param name="openWords">Shows your security words.</param>
    public SettingsPage(RavenSession session, Action onErased, Action? openNetwork, Action<LockState> onLocked,
        Action<ThemeChoice> onTheme, Action openWords)
    {
        _session = session;
        _onErased = onErased;
        _openNetwork = openNetwork;
        _onLocked = onLocked;
        _onTheme = onTheme;
        _openWords = openWords;
        _vm = new SecurityViewModel(session.Engine);
        InitializeComponent();
        // tokens.json settingsNavWidth (an x:Double resource can't be a GridLength in XAML).
        NavColumn.Width = new GridLength((double)Ui.Resource("RavenSettingsNavWidth")!);
        BuildSectionList();
        _vm.Changed += () =>
        {
            if (_current is SettingsSections.Lock or SettingsSections.Updates) Render(animate: false);
        };
        Loaded += (_, _) => PlaceGlider(animate: false);
        // General shows at once from the cached settings; RefreshAsync then shows the engine's.
        Open(SettingsSections.General);
    }

    public async Task RefreshAsync()
    {
        try
        {
            await _session.RefreshSettingsAsync();
            await _session.RefreshNodeAsync();
            await _session.RefreshPrivacyAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        try
        {
            _network = await _session.Engine.NetworkStatusAsync();
        }
        catch (RavenException)
        {
            _network = null; // the rows say "–" rather than guess
        }
        try
        {
            _app ??= await _session.Engine.AppInfoAsync();
            _me ??= await _session.Engine.IdentityMeAsync();
        }
        catch (RavenException)
        {
            // About shows what it has.
        }
        await _vm.LoadAsync();
        if (_vm.Lock.State is LockState state) _session.SetLock(state);
        if (_current is null) Open(SettingsSections.General);
        else Render(animate: false);
    }

    // ------------------------------------------------------------------ the section list

    private void BuildSectionList()
    {
        foreach (SettingsSections.Section section in SettingsSections.All)
        {
            var tile = new Border { Style = Ui.Style($"RavenSectionTile{section.Tile}Style") };
            tile.Child = new FontIcon { Glyph = section.Glyph, Style = Ui.Style("RavenOnTileIconStyle") };
            AutomationProperties.SetAccessibilityView(tile, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            TextBlock label = Ui.Text(Loc.Get(section.TitleKey), "RavenCaptionTextStyle");
            label.FontSize = 13.5;
            label.VerticalAlignment = VerticalAlignment.Center;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(tile);
            row.Children.Add(label);
            var button = new RadioButton
            {
                Style = Ui.Style("RavenPlainRadioStyle"),
                GroupName = "ravenSettingsSections",
                Content = row,
                Height = RowHeight,
                Padding = new Thickness(10, 0, 10, 0),
                CornerRadius = new CornerRadius(10),
            };
            AutomationProperties.SetName(button, Loc.Get(section.TitleKey));
            string id = section.Id;
            button.Checked += (_, _) =>
            {
                if (!_choosing) Open(id);
            };
            button.GotFocus += (_, _) =>
            {
                // The arrow keys move between sections and open them.
                if (button.FocusState == FocusState.Keyboard && button.IsChecked != true) button.IsChecked = true;
            };
            _sections.Add((button, label, id));
            SectionList.Children.Add(button);
        }
    }

    /// <summary>Opens a section (Settings &gt; ... from elsewhere too, e.g. the tray's Lock).</summary>
    public void Open(string id)
    {
        int to = SettingsSections.IndexOf(id);
        if (to < 0) return;
        int from = SettingsSections.IndexOf(_current);
        bool first = _current is null;
        _current = id;
        _choosing = true;
        try
        {
            _sections[to].Button.IsChecked = true;
        }
        finally
        {
            _choosing = false;
        }
        for (int i = 0; i < _sections.Count; i++)
        {
            bool on = i == to;
            _sections[i].Label.Style = Ui.Style(on ? "RavenCaptionPrimaryTextStyle" : "RavenCaptionTextStyle");
            _sections[i].Label.FontSize = 13.5;
            _sections[i].Label.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
        PlaceGlider(animate: !first);
        int direction = SlideDirection.Between(from, to);
        if (first || direction == 0)
        {
            Render(animate: false);
            return;
        }
        Motion.SectionOut(SectionHost, SectionShift, direction, () =>
        {
            Render(animate: false);
            SectionScroller.ChangeView(null, 0, null, disableAnimation: true);
            Motion.SectionIn(SectionHost, SectionShift, direction);
        });
    }

    private void PlaceGlider(bool animate)
    {
        int index = SettingsSections.IndexOf(_current);
        if (index < 0) index = 0;
        Motion.Glide(GliderShift, index * (RowHeight + RowSpacing), animate);
    }

    // ------------------------------------------------------------------ building blocks

    private void Render(bool animate)
    {
        _ = animate;
        _error = new InfoBar { IsClosable = true, IsOpen = false };
        Ui.CollapseWhenClosed(_error);
        var page = new StackPanel { Spacing = 0 };
        switch (_current)
        {
            case SettingsSections.Appearance: Appearance(page); break;
            case SettingsSections.Privacy: Privacy(page); break;
            case SettingsSections.Lock: LockSection(page); break;
            case SettingsSections.Network: NetworkSection(page); break;
            case SettingsSections.Updates: Updates(page); break;
            case SettingsSections.About: About(page); break;
            default: General(page); break;
        }
        page.Children.Add(_error);
        SectionHost.Content = page;
    }

    private void ShowError(Exception ex)
    {
        if (_error is not null) Ui.ShowError(_error, ex);
    }

    private static void Heading(StackPanel page, string title, string? lead, UIElement? leadExtra = null)
    {
        TextBlock h = Ui.Text(title, "RavenPageTitleTextStyle");
        h.Margin = new Thickness(0, 0, 0, 4);
        AutomationProperties.SetHeadingLevel(h, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);
        page.Children.Add(h);
        if (lead is not null || leadExtra is not null)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 20), Spacing = 2 };
            if (lead is not null) row.Children.Add(Ui.Text(lead, "RavenCaptionTextStyle"));
            if (leadExtra is not null) row.Children.Add(leadExtra);
            page.Children.Add(row);
        }
        else
        {
            h.Margin = new Thickness(0, 0, 0, 20);
        }
    }

    private static void Label(StackPanel page, string text)
    {
        TextBlock label = Ui.Text(text.ToUpperInvariant(), "RavenSectionLabelTextStyle");
        label.Margin = new Thickness(4, 0, 0, 8);
        AutomationProperties.SetHeadingLevel(label, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        page.Children.Add(label);
    }

    /// <summary>A Calm group: a surface card whose items are separated by fine lines.</summary>
    private static StackPanel Group(StackPanel page)
    {
        var items = new StackPanel();
        page.Children.Add(new Border { Style = Ui.Style("RavenGroupStyle"), Child = items });
        return items;
    }

    /// <summary>One item: the title, the description under it, the control on the right.</summary>
    private static Grid Item(StackPanel group, string title, string? description, UIElement? control, UIElement? titleIcon = null)
    {
        var grid = new Grid { Padding = new Thickness(16, 13, 16, 13), ColumnSpacing = 18, RowSpacing = 3 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (titleIcon is not null) head.Children.Add(titleIcon);
        TextBlock t = Ui.Text(title, "RavenLabelWrapTextStyle");
        t.FontWeight = FontWeights.Medium;
        head.Children.Add(t);
        grid.Children.Add(head);
        if (!string.IsNullOrEmpty(description))
        {
            TextBlock d = Ui.Text(description, "RavenCaptionTextStyle");
            Grid.SetRow(d, 1);
            grid.Children.Add(d);
        }
        if (control is not null)
        {
            // WinUI's Grid.Set* take a FrameworkElement (not a UIElement as in WPF).
            if (control is FrameworkElement fe)
            {
                fe.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(fe, 1);
                Grid.SetRowSpan(fe, 2);
            }
            grid.Children.Add(control);
            if (control is Control c && AutomationProperties.GetName(c).Length == 0) AutomationProperties.SetName(c, title);
        }
        if (group.Children.Count > 0) group.Children.Add(new Border { Height = 1, Style = Ui.Style("RavenSeparatorStyle") });
        group.Children.Add(grid);
        return grid;
    }

    private static Border Stat(string text, Tone tone)
    {
        TextBlock label = Ui.Text(text, TickView.MetaStyle(tone));
        label.FontSize = 12;
        label.FontWeight = FontWeights.SemiBold;
        return new Border { Style = Ui.Style(tone switch
        {
            Tone.Success => "RavenStatSuccessStyle",
            Tone.Warning => "RavenStatWarningStyle",
            _ => "RavenPillStyle",
        }), Child = label };
    }

    private CalmSwitch Switch(bool on, string name, Func<bool, Task> changed, bool enabled = true)
    {
        var sw = new CalmSwitch { IsEnabled = enabled };
        sw.SetQuietly(on);
        AutomationProperties.SetName(sw, name);
        sw.Toggled += s => _ = changed(s.IsOn);
        return sw;
    }

    private async Task SaveAsync(JsonObject partial)
    {
        try
        {
            await _session.UpdateSettingsAsync(partial);
            _error!.IsOpen = false;
        }
        catch (Exception ex)
        {
            ShowError(ex);
            Render(animate: false); // show what the engine really has
        }
    }

    // ------------------------------------------------------------------ General

    private void General(StackPanel page)
    {
        RavenSettings s = _session.Settings;
        Heading(page, Loc.Get("settings.general"), Loc.Get("settings.general.lead"));
        StackPanel group = Group(page);

        var name = new TextBox
        {
            Text = s.DisplayName ?? string.Empty,
            MaxLength = 64,
            Width = 220,
            PlaceholderText = Loc.Get("setup.name.placeholder"),
        };
        AutomationProperties.SetName(name, Loc.Get("settings.name"));
        name.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            _ = SaveNameAsync(name.Text);
        };
        name.LostFocus += (_, _) => _ = SaveNameAsync(name.Text);
        Item(group, Loc.Get("settings.name"), Loc.Get("setup.name.body"), name);

        Item(group, Loc.Get("settings.receive"), Loc.Get("setup.receive.body") + " " + Loc.Get("setup.receive.windows"),
            Switch(s.Receive, Loc.Get("settings.receive"), SaveReceiveAsync));

        Label(page, Loc.Get("settings.whenQuit"));
        StackPanel quit = Group(page);
        var options = new RadioButtons();
        foreach (string option in Loc.GetArray("settings.whenQuit.options")) options.Items.Add(option);
        options.SelectedIndex = Math.Max(0, Array.IndexOf(WhenQuitValues, s.WhenQuit));
        AutomationProperties.SetName(options, Loc.Get("settings.whenQuit"));
        options.SelectionChanged += (_, _) =>
        {
            int i = options.SelectedIndex;
            if (i >= 0 && i < WhenQuitValues.Length && WhenQuitValues[i] != _session.Settings.WhenQuit)
                _ = SaveAsync(new JsonObject { ["when_quit"] = WhenQuitValues[i] });
        };
        Grid item = Item(quit, Loc.Get("settings.whenQuit"), Loc.Get("quit.running"), null);
        item.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        options.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(options, 2);
        Grid.SetColumnSpan(options, 2);
        item.Children.Add(options);
    }

    private async Task SaveNameAsync(string text)
    {
        string name = TextSanitizer.SingleLine(text);
        if (name == (_session.Settings.DisplayName ?? string.Empty)) return;
        await SaveAsync(new JsonObject { ["display_name"] = name.Length == 0 ? null : name });
    }

    private async Task SaveReceiveAsync(bool receive)
    {
        await SaveAsync(new JsonObject { ["receive"] = receive });
        try
        {
            if (receive && _session.Node is not { Running: true })
                _session.SetNode(await _session.Engine.NodeStartAsync());
            else
                await _session.RefreshNodeAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    // ------------------------------------------------------------------ Appearance

    private void Appearance(StackPanel page)
    {
        AppPrefs prefs = App.Current?.Prefs ?? new AppPrefs();
        Heading(page, Loc.Get("settings.appearance"), Loc.Get("settings.appearance.lead"));
        StackPanel group = Group(page);

        var theme = new Segmented(Loc.Get("settings.theme"), Loc.GetArray("settings.theme.options"), Array.IndexOf(ThemeValues, prefs.Theme));
        theme.SelectionChanged += i =>
        {
            if (i < 0 || i >= ThemeValues.Length) return;
            prefs.Theme = ThemeValues[i];
            App.Current?.SavePrefs();
            _onTheme(prefs.Theme);
        };
        Item(group, Loc.Get("settings.theme"), Loc.Get("settings.theme.body"), theme);

        Item(group, Loc.Get("settings.reducedMotion"), Loc.Get("settings.reducedMotion.body"),
            Switch(_session.Settings.ReducedMotion, Loc.Get("settings.reducedMotion"),
                on => SaveAsync(new JsonObject { ["reduced_motion"] = on })));

        Item(group, Loc.Get("settings.details"), Loc.Get("settings.details.body"),
            Switch(prefs.ShowTechnical, Loc.Get("settings.details"), on =>
            {
                prefs.ShowTechnical = on;
                App.Current?.SavePrefs();
                _session.NotifyPrefsChanged();
                return Task.CompletedTask;
            }));

        Label(page, Loc.Get("settings.preview"));
        StackPanel preview = Group(page);
        preview.Children.Add(PreviewRow());
    }

    /// <summary>
    /// A sample bubble of yours (nothing is sent): Play a send shows the motions of a send on it,
    /// started by the person's own click (sending, sent, delivered with the check pop and the chip).
    /// </summary>
    private UIElement PreviewRow()
    {
        TextBlock body = Ui.Text(Loc.Get("settings.preview.text"), "RavenBubbleOutTextStyle");
        var lift = new CompositeTransform();
        var bubble = new Border { Style = Ui.Style("RavenBubbleOutStyle"), Child = body, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = lift, RenderTransformOrigin = new WinPoint(0, 1) };
        var tick = new TickView();
        var chip = new PathChip();
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(4, 5, 0, 0) };
        meta.Children.Add(tick.Root);
        meta.Children.Add(chip.Root);
        var sample = new StackPanel();
        sample.Children.Add(bubble);
        sample.Children.Add(meta);
        var demo = new Message { Direction = Directions.Out, State = MessageStates.Delivered, PathKind = PathKinds.Lan };

        void Show(string state, bool receipt)
        {
            demo.State = state;
            tick.Set(TickPresenter.For(demo, Loc.Get("you.title"), RavenSession.NowMs), receipt);
            if (state == MessageStates.Delivered) chip.Set(PathKinds.Lan, null, technical: false);
            else chip.Hide();
        }
        Show(MessageStates.Delivered, receipt: false);

        var play = new Button { Content = Loc.Get("settings.preview.play"), VerticalAlignment = VerticalAlignment.Center };
        play.Click += (_, _) =>
        {
            Show(MessageStates.Sending, receipt: false);
            Motion.Lift(bubble, lift, 0, 26);
            After(450, () => Show(MessageStates.Sent, receipt: false));
            After(950, () =>
            {
                Show(MessageStates.Delivered, receipt: Motion.Enabled);
                if (Motion.Enabled) chip.SlideIn();
            });
        };
        var right = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(play);
        TextBlock note = Ui.Text(Loc.Get("settings.preview.note"), "RavenMetaTextStyle");
        note.TextWrapping = TextWrapping.Wrap;
        note.MaxWidth = 180;
        right.Children.Add(note);
        var grid = new Grid { Padding = new Thickness(20, 18, 20, 18), ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(right, 1);
        grid.Children.Add(sample);
        grid.Children.Add(right);
        AutomationProperties.SetAccessibilityView(sample, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return grid;
    }

    private void After(int ms, Action action)
    {
        Microsoft.UI.Dispatching.DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(Motion.Enabled ? ms : 1);
        timer.IsRepeating = false;
        timer.Tick += (t, _) =>
        {
            t.Stop();
            action();
        };
        timer.Start();
    }

    // ------------------------------------------------------------------ Privacy

    private void Privacy(StackPanel page)
    {
        RavenSettings s = _session.Settings;
        PrivacyState privacy = _session.Privacy;
        Heading(page, Loc.Get("settings.privacy"), Loc.Get("settings.privacy.lead"));

        Label(page, Loc.Get("settings.stored.title"));
        StackPanel stored = Group(page);
        var lines = new StackPanel { Padding = new Thickness(16, 14, 16, 14), Spacing = 10 };
        foreach (string line in PrivacyPresenter.StoredLines(privacy))
        {
            bool plain = line == Loc.Get("settings.stored.plain") || line == Loc.Get("settings.stored.notEncrypted");
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new FontIcon { Glyph = plain ? "\uE785" : "\uE72E", Style = Ui.Style(plain ? "RavenTickIconStyle" : "RavenTickIconSuccessStyle"), FontSize = 16, VerticalAlignment = VerticalAlignment.Top });
            TextBlock text = Ui.Text(line, "RavenCaptionTextStyle");
            text.FontSize = 13;
            text.MaxWidth = 560;
            row.Children.Add(text);
            lines.Children.Add(row);
        }
        stored.Children.Add(lines);

        StackPanel group = Group(page);
        UIElement encrypt;
        if (privacy.Encrypted) encrypt = Stat(Loc.Get("net.on"), Tone.Success);
        else if (!privacy.Available) encrypt = Stat(Loc.Get("net.preview"), Tone.Warning);
        else
        {
            var button = new Button { Content = Loc.Get("settings.encrypt") };
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                try
                {
                    await _session.Engine.PrivacyEncryptAsync();
                }
                catch (Exception ex)
                {
                    ShowError(ex);
                }
                await _session.RefreshPrivacyAsync();
                Render(animate: false); // show what the engine really has
            };
            encrypt = button;
        }
        Item(group, Loc.Get("settings.encrypt"), null, encrypt);

        var keep = new Segmented(Loc.Get("settings.keepChats"), Loc.GetArray("settings.keepChats.options"), Math.Max(0, Array.IndexOf(KeepChatsValues, s.KeepChatsDays)));
        keep.SelectionChanged += i =>
        {
            if (i < 0 || i >= KeepChatsValues.Length) return;
            int? days = KeepChatsValues[i];
            _ = SaveAsync(new JsonObject { ["keep_chats_days"] = days is int d ? JsonValue.Create(d) : null });
        };
        Item(group, Loc.Get("settings.keepChats"), Loc.Get("settings.keepChats.body"), keep);

        Item(group, Loc.Get("settings.forget"), null,
            Switch(s.ForgetAddresses, Loc.Get("settings.forget"), on => SaveAsync(new JsonObject { ["forget_addresses"] = on })));

        StackPanel erase = Group(page);
        var eraseButton = new Button { Content = Ui.Text(Loc.Get("settings.erase.button"), "RavenDangerButtonTextStyle"), IsEnabled = privacy.Available };
        AutomationProperties.SetName(eraseButton, Loc.Get("settings.erase"));
        eraseButton.Click += async (_, _) =>
        {
            if (!_session.Privacy.Available || XamlRoot is null) return;
            var dialog = new EraseDialog(_session);
            await Ui.ShowDialogAsync(dialog, XamlRoot);
            if (dialog.Erased) _onErased();
        };
        string eraseBody = Loc.Get("erase.body") + (privacy.Available ? string.Empty : " " + Loc.Get("net.preview"));
        Item(erase, Loc.Get("settings.erase"), eraseBody, eraseButton);
    }

    // ------------------------------------------------------------------ Lock

    private void LockSection(StackPanel page)
    {
        LockViewModel vm = _vm.Lock;
        Heading(page, Loc.Get("lock.section"), Loc.Get("lock.v2.body"));

        StackPanel row = Group(page);
        bool locked = vm.IsLocked;
        var glyph = new LockGlyph(42, locked ? "Accent" : "Success", open: !locked);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        TextBlock title = Ui.Text(Loc.Get(locked ? "lock.isLocked" : "lock.isOpen"), "RavenLabelWrapTextStyle");
        title.FontSize = 14;
        texts.Children.Add(title);
        string sub = vm.State is null ? string.Empty : vm.CanLockNow || vm.IsLocked ? Loc.Get("lock.nothingLocked") : vm.StatusText;
        if (locked) sub = string.Empty;
        if (sub.Length > 0) texts.Children.Add(Ui.Text(sub, "RavenCaptionTextStyle"));
        if (vm.LabText is string lab) texts.Children.Add(Ui.Text(lab, "RavenMetaAccentTextStyle"));
        var lockNow = new Button
        {
            Content = Loc.Get("lock.nowEllipsis"),
            Style = Ui.Style("AccentButtonStyle"),
            IsEnabled = vm.CanLockNow && !vm.Busy,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(lockNow, Loc.Get("lock.now"));
        lockNow.Click += (_, _) => _ = LockNowAsync();
        var lockRow = new Grid { Padding = new Thickness(16), ColumnSpacing = 14 };
        lockRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lockRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lockRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(lockNow, 2);
        lockRow.Children.Add(glyph.Root);
        lockRow.Children.Add(texts);
        lockRow.Children.Add(lockNow);
        row.Children.Add(lockRow);
        if (vm.Error is ErrorInfo error)
        {
            var bar = new InfoBar { IsClosable = false };
            Ui.ShowError(bar, new RavenException(error.Code, error.Message, error.Detail));
            row.Children.Add(bar);
        }

        StackPanel group = Group(page);
        Item(group, Loc.Get("settings.lockOnSleep"), Loc.Get("settings.lockOnSleep.body"),
            Switch(_session.Settings.LockOnSleep, Loc.Get("settings.lockOnSleep"), on => SaveAsync(new JsonObject { ["lock_on_sleep"] = on })));

        string passNote = vm.State?.HasPassphrase switch
        {
            true => Loc.Get("lock.v2.passphraseNote"),
            false => Loc.Get("lock.noPassphrase"),
            _ => Loc.Get("lock.passphrase.notInApp"),
        };
        UIElement? passStat = vm.State?.HasPassphrase switch
        {
            true => Stat(Loc.Get("lock.passphrase.set"), Tone.Success),
            false => Stat(Loc.Get("lock.passphrase.none"), Tone.Warning),
            _ => null,
        };
        Grid pass = Item(group, Loc.Get("lock.passphrase"), passNote + " " + Loc.Get("lock.passphrase.notInApp"), passStat);
        if (Ui.TechnicalDetails(new[] { Loc.Get("lock.passphraseTerminal") }) is Expander details)
        {
            pass.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(details, 2);
            Grid.SetColumnSpan(details, 2);
            pass.Children.Add(details);
        }
    }

    /// <summary>Lock now…: the sheet says what Lock does and doesn't protect (E1); its shackle closes, then Raven locks.</summary>
    private async Task LockNowAsync()
    {
        if (XamlRoot is null || !await ConfirmLockDialog.AskAsync(_vm.Lock.State, XamlRoot)) return;
        if (await _vm.Lock.LockNowAsync() && _vm.Lock.State is LockState state)
        {
            _session.SetLock(state);
            _onLocked(state);
        }
    }

    // ------------------------------------------------------------------ Network

    private void NetworkSection(StackPanel page)
    {
        var open = new HyperlinkButton { Content = Loc.Get("network.page.open"), Padding = new Thickness(0, 2, 0, 2) };
        open.Click += (_, _) => _openNetwork?.Invoke();
        Heading(page, Loc.Get("settings.network"), Loc.Get("settings.network.note"), open);

        // network.status's states, read from the running background program: the same words as the
        // Network page (E1: "Preview" here while the node ran them).
        Label(page, Loc.Get("net.ways"));
        StackPanel ways = Group(page);
        if (_network is null)
        {
            // network.status didn't answer: the rows say "\u2013" rather than guess.
            foreach (string key in new[] { "net.lan", "net.internet", "net.p2p", "net.custody" })
                Item(ways, Loc.Get(key), null, Stat("\u2013", Tone.Secondary));
        }
        else
        {
            foreach (NetworkWay way in _network.Carriers) WayItem(ways, way);
        }
        if (_network is { Protections.Count: > 0 })
        {
            Label(page, Loc.Get("network.protections"));
            StackPanel protections = Group(page);
            foreach (NetworkWay way in _network.Protections) WayItem(protections, way);
        }
    }

    private static void WayItem(StackPanel group, NetworkWay way)
    {
        Chip chip = NetworkPresenter.StateChip(way.State);
        string name = TextSanitizer.SingleLine(way.Name);
        string? line = way.Running == true && way.State == WayStates.On ? Loc.Get("network.running.yes") : null;
        UIElement? icon = PathChip.Icon(CalmPath.FamilyOfWay(way.Id), 13);
        Grid item = Item(group, name, line ?? NetworkPresenter.WhatItDoes(way.Id), Stat(chip.Text, chip.Tone), icon);
        AutomationProperties.SetName(item, name + ", " + Loc.Format("network.state.chip", ("state", chip.Text)));
    }

    // ------------------------------------------------------------------ Updates

    private void Updates(StackPanel page)
    {
        Heading(page, Loc.Get("updates.title"), Loc.Get("updates.intro"));
        StackPanel group = Group(page);
        string version = _app is null ? string.Empty : Loc.Format("net.version", ("version", TextSanitizer.SingleLine(_app.Version)));
        string build = Loc.Get(_session.Node?.Build == "dev" || _app?.Build == "dev" ? "net.build.dev" : "net.build.release");
        Item(group, Loc.Get("updates.thisVersion"), version.Length == 0 ? build : version + " · " + build, null);

        UpdatesStatus? u = _vm.Updates;
        UIElement status = u is null ? Stat("\u2013", Tone.Secondary)
            : u.FromFriends ? Stat(Loc.Get("net.on"), Tone.Success)
            : u.Available || u.Bundles ? Stat(Loc.Get("net.wayOff"), Tone.Secondary)
            : Stat(Loc.Get("net.preview"), Tone.Warning);
        Grid friends = Item(group, Loc.Get("updates.fromFriends"), _vm.UpdatesText, status);
        var extra = new StackPanel { Spacing = 2 };
        if (_vm.UpdatesLabText is string lab) extra.Children.Add(Ui.Text(lab, "RavenMetaAccentTextStyle"));
        foreach (string line in _vm.UpdatesLines) extra.Children.Add(Ui.Text(line, "RavenCaptionTextStyle"));
        if (_vm.Offer.Count > 0)
        {
            extra.Children.Add(Ui.Text(Loc.Get("updates.offer"), "RavenLabelTextStyle"));
            foreach (string line in _vm.Offer) extra.Children.Add(Ui.Text(line, "RavenCaptionTextStyle"));
        }
        if (extra.Children.Count > 0)
        {
            friends.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            extra.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(extra, 2);
            Grid.SetColumnSpan(extra, 2);
            friends.Children.Add(extra);
        }

        // Plain words; the engine's install command only under Technical details (E1).
        Grid install = Item(group, Loc.Get("updates.install"), null, null);
        if (u is not null && _vm.InstallText != Loc.Get("updates.install") && Ui.TechnicalDetails(new[] { _vm.InstallText }) is Expander details)
        {
            details.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(details, 1);
            install.Children.Add(details);
        }
        if (_vm.UpdatesError is ErrorInfo error)
        {
            var bar = new InfoBar { IsClosable = false };
            Ui.ShowError(bar, new RavenException(error.Code, error.Message, error.Detail));
            page.Children.Add(bar);
        }
    }

    // ------------------------------------------------------------------ About

    private void About(StackPanel page)
    {
        StackPanel card = Group(page);
        var about = new StackPanel { Padding = new Thickness(16, 26, 16, 22), Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        var mark = new Image
        {
            Width = 74,
            Height = 74,
            Margin = new Thickness(0, 0, 0, 8),
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/RavenMark.png")),
        };
        AutomationProperties.SetAccessibilityView(mark, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        about.Children.Add(mark);
        TextBlock name = Ui.Text(Loc.Get("app.name"), "RavenTitleTextStyle");
        name.FontSize = 20;
        name.HorizontalAlignment = HorizontalAlignment.Center;
        about.Children.Add(name);
        foreach (string line in new[]
                 {
                     Loc.Get("app.tagline"),
                     _app is null ? string.Empty : Loc.Format("net.version", ("version", TextSanitizer.SingleLine(_app.Version))),
                     Loc.Get("about.platforms") + " · " + AppIdentity.Id,
                 })
        {
            if (line.Length == 0) continue;
            TextBlock t = Ui.Text(line, "RavenCaptionTextStyle");
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.TextAlignment = TextAlignment.Center;
            about.Children.Add(t);
        }
        card.Children.Add(about);

        StackPanel group = Group(page);
        Item(group, Loc.Get("about.openSource"), Loc.Get("about.openSource.body"), null);
        string fingerprint = TextSanitizer.SingleLine(_me?.OldCode ?? string.Empty);
        var words = new Button { Content = Loc.Get("nav.securityWords") };
        words.Click += (_, _) => _openWords();
        Item(group, Loc.Get("about.fingerprint"), fingerprint.Length == 0 ? null : Loc.Format("you.fingerprint", ("code", fingerprint)), words);
    }
}
