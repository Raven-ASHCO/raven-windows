using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Core.ViewModels;
using Raven.Windows.Dialogs;
using Raven.Windows.Helpers;
using Raven.Windows.Services;

namespace Raven.Windows.Views;

/// <summary>
/// Network: whether this computer listens and the background program, then every way to reach
/// friends from network.status (<see cref="NetworkViewModel"/>): a state chip (On / Off /
/// Preview / Lab only / Not on this computer), one plain line of what it does and why it is in
/// that state, a switch where network.set allows it, and Details (the CLI's row, the gate and
/// its lab switch). Below: ports, router, relays, proxy, nearby (inactive on Windows), who
/// carries mail both ways, the help-friends flags and hide IP. Turning on a setting that helps
/// friends opens the consent sheet first; a change that waits for the background program
/// offers to restart it; while Internet direct or p2p is on, the Windows Firewall note names
/// the prompt and the ports. In developer builds with lab switches, a banner says so.
/// Calm: the ways are cards (two columns when there is room) with their path family's icon tile
/// and a Calm switch; the radar beside This computer pulses once per real status check (and only
/// then); a switch's knob springs on the person's own toggle. Technical details show only with
/// Settings &gt; Appearance &gt; Show technical details.
/// </summary>
public sealed partial class NetworkPage : UserControl
{
    // Same order as strings "network.lanFor.options".
    private static readonly string[] LanForValues = { "30m", "1h", "2h", "8h", "24h" };

    private readonly RavenSession _session;
    private readonly Action _openCheck;
    private readonly NetworkViewModel _vm;
    private readonly Dictionary<string, WayRowView> _wayViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WayRowView> _protectionViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FlagView> _flagViews = new(StringComparer.Ordinal);
    private AppInfo? _info;
    private Exception? _pageError;
    private bool _rendering;

    /// <param name="openCheck">Shows the Check network page.</param>
    public NetworkPage(RavenSession session, Action openCheck)
    {
        _session = session;
        _openCheck = openCheck;
        _vm = new NetworkViewModel(session.Engine);
        InitializeComponent();

        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
        IReadOnlyList<string> options = Loc.GetArray("network.lanFor.options");
        for (int i = 0; i < options.Count && i < LanForValues.Length; i++)
        {
            string duration = LanForValues[i];
            var item = new MenuFlyoutItem { Text = options[i] };
            item.Click += (_, _) => _ = SetLanForAsync(duration);
            menu.Items.Add(item);
        }
        LanForButton.Flyout = menu;
        Ui.CollapseWhenClosed(LabBanner, ErrorBar, ChangedBar, RestartBar, FirewallBar);
        WaysLabel.Text = Loc.Get("net.ways").ToUpperInvariant();
        ProtectionsLabel.Text = Loc.Get("network.protections").ToUpperInvariant();

        _vm.Changed += Render;
    }

    private static bool Technical => App.Current?.Prefs.ShowTechnical == true;

    /// <summary>status_ok: the radar's two rings go out once (the check really happened).</summary>
    public void Pulse()
    {
        Motion.Radar(RadarRing1, RadarScale1, 0);
        Motion.Radar(RadarRing2, RadarScale2, 350);
        RenderHeartbeat();
    }

    private void RenderHeartbeat()
    {
        (string text, bool ok) = NavPresenter.Heartbeat(_session.Node, _session.NodeDown, _session.LastOkMs, RavenSession.NowMs);
        NodeOkText.Text = text;
        NodeOkText.Style = Ui.Style(ok ? "RavenMetaSuccessTextStyle" : "RavenMetaDangerTextStyle");
        NodeDot.Style = Ui.Style(ok ? "RavenHeartDotStyle" : "RavenHeartDownDotStyle");
    }

    /// <summary>Two columns of way cards when the page is wide enough, one otherwise.</summary>
    private void OnWaysSizeChanged(object sender, SizeChangedEventArgs e) => LayoutWays(e.NewSize.Width);

    private void LayoutWays(double width)
    {
        int columns = width >= 600 ? 2 : 1;
        if (WaysPanel.ColumnDefinitions.Count != columns)
        {
            WaysPanel.ColumnDefinitions.Clear();
            for (int c = 0; c < columns; c++) WaysPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        int rows = (WaysPanel.Children.Count + columns - 1) / columns;
        while (WaysPanel.RowDefinitions.Count < rows) WaysPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        while (WaysPanel.RowDefinitions.Count > rows) WaysPanel.RowDefinitions.RemoveAt(WaysPanel.RowDefinitions.Count - 1);
        for (int i = 0; i < WaysPanel.Children.Count; i++)
        {
            if (WaysPanel.Children[i] is not FrameworkElement card) continue;
            Grid.SetRow(card, i / columns);
            Grid.SetColumn(card, i % columns);
        }
    }

    public async Task RefreshAsync()
    {
        _pageError = null;
        try
        {
            await _session.RefreshNodeAsync();
            _info ??= await _session.Engine.AppInfoAsync();
        }
        catch (Exception ex)
        {
            _pageError = ex;
        }
        await _vm.LoadAsync();
        Render();
    }

    // ------------------------------------------------------------------ rendering

    private void Render()
    {
        _rendering = true;
        try
        {
            bool busy = _vm.Busy;
            BusyRing.IsActive = busy && Motion.Enabled;
            BusyRing.Visibility = busy && Motion.Enabled ? Visibility.Visible : Visibility.Collapsed;
            RefreshButton.IsEnabled = !busy;

            RenderNode();
            RenderHeartbeat();
            RenderNotices();

            Fill(ComputerPanel, _vm.ComputerLines, _vm.ComputerTechnical);
            WayRow? lan = _vm.Ways.FirstOrDefault(w => w.Id == "lan");
            LanForButton.Visibility = lan is { HasSwitch: true } ? Visibility.Visible : Visibility.Collapsed;
            LanForButton.IsEnabled = !busy && lan is { SwitchEnabled: true };

            RenderRows(WaysPanel, _wayViews, _vm.Ways, busy, cards: true);
            LayoutWays(WaysPanel.ActualWidth);
            RenderRows(ProtectionsPanel, _protectionViews, _vm.Protections, busy, cards: false);

            Fill(RouterPanel, _vm.RouterLines);
            Fill(RelayPanel, _vm.RelayLines);
            WayRow? p2p = _vm.Ways.FirstOrDefault(w => w.Id == "p2p");
            bool p2pOpen = p2p?.State is WayStates.On or WayStates.Off or WayStates.Lab;
            RelaysBox.IsEnabled = p2pOpen && !busy;
            RelaysButton.IsEnabled = p2pOpen && !busy;
            if (RelaysBox.FocusState == FocusState.Unfocused && string.IsNullOrWhiteSpace(RelaysBox.Text))
                RelaysBox.Text = string.Join("\n", _vm.ManualRelays);
            Fill(ProxyPanel, _vm.ProxyLines, _vm.ProxyTechnical);
            WayRow? proxy = _vm.Ways.FirstOrDefault(w => w.Id == "proxy");
            bool proxyOpen = proxy?.State is WayStates.On or WayStates.Off or WayStates.Lab;
            ProxyBox.IsEnabled = proxyOpen && !busy;
            ProxyButton.IsEnabled = proxyOpen && !busy;
            if (ProxyBox.FocusState == FocusState.Unfocused) ProxyBox.Text = _vm.ProxyAddress;

            NearbyNoteText.Text = _vm.NearbyNote ?? string.Empty;
            NearbyNoteText.Visibility = string.IsNullOrEmpty(_vm.NearbyNote) ? Visibility.Collapsed : Visibility.Visible;
            Fill(NearbyPanel, _vm.NearbyLines, _vm.NearbyTechnical);

            Fill(CarryMinePanel, _vm.CarryMineLines);
            CarryForFriendsSwitch.IsOn = _vm.CarryForFriendsOn;
            CarryForFriendsSwitch.IsEnabled = _vm.CarryForFriendsEnabled && !busy && _vm.Status is not null;
            Fill(CarryForFriendsPanel, _vm.CarryForFriendsLines, _vm.CarryTechnical);
            Fill(LettersPanel, _vm.LettersLeft);
            LettersTitle.Visibility = _vm.LettersLeft.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            RenderFlags(busy);
            HelpAutomaticText.Visibility = _vm.HelpAutomatic ? Visibility.Visible : Visibility.Collapsed;
            HideIpSwitch.IsOn = _vm.HideIp;
            HideIpSwitch.IsEnabled = !busy && _vm.Status is not null;
        }
        finally
        {
            _rendering = false;
        }
    }

    private void RenderNode()
    {
        NodeStatus? node = _session.Node;
        RavenSettings settings = _session.Settings;

        bool starting = node?.IsStarting == true;
        StartingRing.Visibility = starting && Motion.Enabled ? Visibility.Visible : Visibility.Collapsed;
        StartingRing.IsActive = starting && Motion.Enabled;
        if (starting)
        {
            ListeningText.Text = Loc.Get("net.starting");
        }
        else if (node is { Running: true } && !string.IsNullOrEmpty(node.Listening))
        {
            ListeningText.Text = node.IsLocalOnly
                ? Loc.Get("net.localOnly")
                : Loc.Format("net.listening", ("port", node.ListeningPort ?? string.Empty));
        }
        else if (!settings.Receive)
        {
            ListeningText.Text = Loc.Get("net.off");
        }
        else
        {
            ListeningText.Text = Loc.Get("home.heartbeat.down");
        }

        if (node is { Running: true })
        {
            string pid = node.Pid?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?";
            ProcessText.Text = node.StartedByApp
                ? Loc.Format("net.process.app", ("pid", pid))
                : Loc.Format("net.process.earlier", ("pid", pid));
        }
        else
        {
            ProcessText.Text = Loc.Get("net.process.none");
        }

        if (!string.IsNullOrEmpty(node?.Address))
        {
            AddressText.Text = Loc.Format("net.address", ("addr", node.Address));
            AddressText.Visibility = Visibility.Visible;
        }
        else
        {
            AddressText.Visibility = Visibility.Collapsed;
        }

        string build = _info?.Build ?? node?.Build ?? string.Empty;
        string version = _info?.Version ?? string.Empty;
        VersionText.Text = version.Length == 0
            ? Loc.Get("app.name")
            : Loc.Get("app.name") + " · " + Loc.Format("net.version", ("version", version));
        BuildText.Text = build == "release" ? Loc.Get("net.build.release") : Loc.Get("net.build.dev");
    }

    private void RenderNotices()
    {
        LabBanner.Message = _vm.LabBannerText;
        LabBanner.IsOpen = _vm.ShowLabBanner;

        // The consent sheet itself answers CONSENT_NEEDED; other errors show here.
        if (_vm.Error is ErrorInfo error && _vm.PendingConsent is null)
            Ui.ShowError(ErrorBar, new RavenException(error.Code, error.Message, error.Detail));
        else if (_pageError is not null)
            Ui.ShowError(ErrorBar, _pageError);
        else
            ErrorBar.IsOpen = false;

        if (_vm.LastChanges.Count > 0)
        {
            ChangedBar.Message = string.Join("\n", _vm.LastChanges.Select(c => c.Effect).Where(e => !string.IsNullOrWhiteSpace(e)));
            ChangedBar.IsOpen = true;
        }
        else
        {
            ChangedBar.IsOpen = false;
        }

        if (_vm.LastRestart is RestartResult restart)
        {
            RestartBar.Severity = restart.Kind switch
            {
                RestartKind.Restarted => InfoBarSeverity.Success,
                RestartKind.Failed => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Warning,
            };
            RestartBar.Message = restart.Text;
            RestartBar.ActionButton = null;
            RestartBar.IsClosable = true;
            RestartBar.IsOpen = true;
        }
        else if (_vm.RestartPending)
        {
            RestartBar.Severity = InfoBarSeverity.Informational;
            RestartBar.Message = Loc.Get("network.restart.pending");
            var restartNow = new Button { Content = Loc.Get("network.restart.now"), IsEnabled = !_vm.Busy };
            AutomationProperties.SetName(restartNow, Loc.Get("network.restart.now"));
            restartNow.Click += (_, _) => _ = RestartAsync();
            RestartBar.ActionButton = restartNow;
            RestartBar.IsClosable = false; // stays until the background program starts again
            RestartBar.IsOpen = true;
        }
        else
        {
            RestartBar.IsOpen = false;
        }

        var firewall = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
        foreach (string line in _vm.FirewallLines) firewall.Children.Add(Ui.Text(line));
        FirewallBar.Content = firewall;
        FirewallBar.IsOpen = _vm.FirewallLines.Count > 0;
    }

    private void RenderRows(Panel panel, Dictionary<string, WayRowView> views, IReadOnlyList<WayRow> rows, bool busy, bool cards)
    {
        bool sameRows = views.Count == rows.Count && rows.All(r => views.ContainsKey(r.Id));
        if (!sameRows)
        {
            panel.Children.Clear();
            views.Clear();
            foreach (WayRow row in rows)
            {
                var view = new WayRowView(row.Id, cards);
                view.Switch.Toggled += _ => OnWaySwitched(view);
                views[row.Id] = view;
                panel.Children.Add(view.Root);
            }
        }
        foreach (WayRow row in rows) views[row.Id].Update(row, busy);
    }

    private void RenderFlags(bool busy)
    {
        if (_flagViews.Count == 0 && _vm.HelpFlags.Count > 0)
        {
            foreach (FlagRow flag in _vm.HelpFlags)
            {
                var view = new FlagView(flag.Setting);
                view.Switch.Toggled += (_, _) =>
                {
                    if (!_rendering) _ = SetFlagAsync(view.Setting, view.Switch.IsOn);
                };
                _flagViews[flag.Setting] = view;
                HelpPanel.Children.Add(view.Root);
            }
        }
        foreach (FlagRow flag in _vm.HelpFlags)
        {
            if (_flagViews.TryGetValue(flag.Setting, out FlagView? view)) view.Update(flag, enabled: !busy);
        }
    }

    /// <param name="technical">Raw lines and commands: under "Technical details" only (E1).</param>
    private static void Fill(Panel panel, IEnumerable<string> lines, IEnumerable<string>? technical = null)
    {
        panel.Children.Clear();
        foreach (string line in lines)
        {
            TextBlock text = Ui.Text(line);
            text.IsTextSelectionEnabled = true; // addresses and ports can be copied
            panel.Children.Add(text);
        }
        if (Technical && Ui.TechnicalDetails(technical) is Expander details) panel.Children.Add(details);
        panel.Visibility = panel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ changes

    private void OnWaySwitched(WayRowView view)
    {
        if (_rendering || view.Row is null) return;
        _ = SetSwitchAsync(view.Row, view.Switch.IsOn);
    }

    private async Task SetSwitchAsync(WayRow row, bool on) => await FollowUpAsync(await _vm.SetSwitchAsync(row, on));

    private async Task SetFlagAsync(string setting, bool on) => await FollowUpAsync(await _vm.SetFlagAsync(setting, on));

    private async Task SetLanForAsync(string duration) => await FollowUpAsync(await _vm.SetLanForAsync(duration));

    private void OnCarryForFriendsToggled(object sender, RoutedEventArgs e)
    {
        if (!_rendering) _ = SetFlagAsync(NetworkSettings.CarryForFriends, CarryForFriendsSwitch.IsOn);
    }

    private void OnHideIpToggled(object sender, RoutedEventArgs e)
    {
        if (!_rendering) _ = SetFlagAsync(NetworkSettings.HideIp, HideIpSwitch.IsOn);
    }

    private void OnProxyClick(object sender, RoutedEventArgs e) => _ = SetProxyAsync();

    private void OnRelaysClick(object sender, RoutedEventArgs e) => _ = SetRelaysAsync();

    private async Task SetRelaysAsync()
    {
        string[] relays = (RelaysBox.Text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        await FollowUpAsync(await _vm.SetP2pRelaysAsync(relays));
    }

    private async Task SetProxyAsync()
    {
        string text = ProxyBox.Text ?? string.Empty;
        await FollowUpAsync(await _vm.SetProxyAsync(text));
    }

    /// <summary>
    /// After a change: the consent sheet when the setting needs a yes (only then is it sent,
    /// with agreed: true), and the restart prompt when a change waits for the background program.
    /// </summary>
    private async Task FollowUpAsync(SetOutcome outcome)
    {
        if (XamlRoot is XamlRoot root)
        {
            if (outcome == SetOutcome.NeedsConsent && _vm.PendingConsent is ConsentRequest request)
            {
                if (await ConsentDialog.AskAsync(request, root))
                {
                    outcome = await _vm.AgreeAsync();
                }
                else
                {
                    _vm.DeclineConsent();
                    outcome = SetOutcome.Nothing;
                }
            }
            if (outcome == SetOutcome.RestartNeeded && _vm.Restart is RestartPrompt prompt)
            {
                if (await RestartDialog.AskAsync(prompt, _vm.FirewallLines, root)) await RestartAsync();
                else _vm.DismissRestart();
            }
        }
        else if (outcome == SetOutcome.NeedsConsent)
        {
            _vm.DeclineConsent(); // no window to ask in: nothing is sent
        }
        if (outcome is SetOutcome.Changed or SetOutcome.RestartNeeded) await _session.RefreshNodeAsync();
        Render(); // switches show what the engine has
    }

    private async Task RestartAsync()
    {
        RestartResult result = await _vm.RestartNodeAsync();
        if (result.Node is NodeStatus node) _session.SetNode(node);
        else await _session.RefreshNodeAsync();
        Render();
    }

    private void OnNoticeClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        // Only the person's close forgets the notice; the page closes bars itself when they're done.
        if (_rendering || args.Reason != InfoBarCloseReason.CloseButton) return;
        _vm.ClearNotices();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = RefreshAsync();

    private void OnCheckClick(object sender, RoutedEventArgs e) => _openCheck();

    // ------------------------------------------------------------------ row views

    /// <summary>
    /// One way (Calm): the path family's icon tile, the name, the state chip and the switch on the
    /// first line; what it does and why below; "In use now" (with a small green dot) when the
    /// background program says; the Windows note; Technical details (only with Show technical
    /// details). A way is a card; a protection a row in the Protections card.
    /// </summary>
    private sealed class WayRowView
    {
        private readonly TextBlock _name;
        private readonly Border _chip;
        private readonly TextBlock _chipText;
        private readonly TextBlock _line;
        private readonly StackPanel _runningRow;
        private readonly TextBlock _running;
        private readonly TextBlock _note;
        private readonly Border _torChip;
        private readonly TextBlock _torChipText;
        private readonly StackPanel _torLines;
        private readonly Expander _details;
        private readonly StackPanel _detailLines;

        public WayRowView(string id, bool card)
        {
            Id = id;
            var grid = new Grid { ColumnSpacing = 12, RowSpacing = 3 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < 6; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            PathFamily family = CalmPath.FamilyOfWay(id);
            var tile = new Border { Style = Ui.Style($"RavenWayTile{family}Style") };
            if (PathChip.Icon(family, 17, "WayTile") is UIElement icon)
            {
                if (icon is FrameworkElement fe)
                {
                    fe.HorizontalAlignment = HorizontalAlignment.Center;
                    fe.VerticalAlignment = VerticalAlignment.Center;
                }
                tile.Child = icon;
            }
            AutomationProperties.SetAccessibilityView(tile, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            Grid.SetRowSpan(tile, 3);
            if (card) grid.Children.Add(tile);

            _name = Ui.Text(string.Empty, "RavenLabelWrapTextStyle");
            _name.FontSize = 14;
            _name.VerticalAlignment = VerticalAlignment.Center;
            _chipText = Ui.Text(string.Empty, "RavenMetaTextStyle");
            _chipText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            _chip = new Border { Style = Ui.Style("RavenPillStyle"), Child = _chipText };
            // Tor: its state as raven-node reports it, in a chip of its own beside the state chip.
            _torChipText = Ui.Text(string.Empty, "RavenMetaTextStyle");
            _torChipText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            _torChip = new Border { Style = Ui.Style("RavenPillStyle"), Child = _torChipText, Visibility = Visibility.Collapsed };
            var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            title.Children.Add(_name);
            title.Children.Add(_chip);
            title.Children.Add(_torChip);
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            Switch = new CalmSwitch();
            Grid.SetColumn(Switch, 2);
            grid.Children.Add(Switch);

            _line = Ui.Text(string.Empty, "RavenCaptionTextStyle");
            Grid.SetRow(_line, 1);
            Grid.SetColumn(_line, 1);
            Grid.SetColumnSpan(_line, 2);
            grid.Children.Add(_line);

            _running = Ui.Text(string.Empty, "RavenMetaTextStyle");
            _runningRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
            var live = new Microsoft.UI.Xaml.Shapes.Ellipse { Style = Ui.Style("RavenHeartDotStyle") };
            AutomationProperties.SetAccessibilityView(live, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            _runningRow.Children.Add(live);
            _runningRow.Children.Add(_running);
            Grid.SetRow(_runningRow, 2);
            Grid.SetColumn(_runningRow, 1);
            Grid.SetColumnSpan(_runningRow, 2);
            grid.Children.Add(_runningRow);

            _torLines = new StackPanel { Spacing = 2, Visibility = Visibility.Collapsed };
            Grid.SetRow(_torLines, 3);
            Grid.SetColumn(_torLines, 1);
            Grid.SetColumnSpan(_torLines, 2);
            grid.Children.Add(_torLines);

            _note = Ui.Text(string.Empty, "RavenCaptionWarningTextStyle");
            Grid.SetRow(_note, 4);
            Grid.SetColumn(_note, 1);
            Grid.SetColumnSpan(_note, 2);
            grid.Children.Add(_note);

            _detailLines = new StackPanel { Spacing = 2 };
            _details = new Expander
            {
                Header = Loc.Get("message.technicalDetails"),
                Content = _detailLines,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            Grid.SetRow(_details, 5);
            Grid.SetColumn(_details, 1);
            Grid.SetColumnSpan(_details, 2);
            grid.Children.Add(_details);

            if (!card)
            {
                Grid.SetColumn(title, 0);
                Grid.SetColumnSpan(title, 2);
                foreach (FrameworkElement e in new FrameworkElement[] { _line, _runningRow, _torLines, _note, _details })
                {
                    Grid.SetColumn(e, 0);
                    Grid.SetColumnSpan(e, 3);
                }
            }
            Root = card ? new Border { Style = Ui.Style("RavenCardStyle"), Child = grid } : grid;
        }

        public string Id { get; }
        public FrameworkElement Root { get; }
        public CalmSwitch Switch { get; }
        public WayRow? Row { get; private set; }

        public void Update(WayRow row, bool busy)
        {
            Row = row;
            _name.Text = row.Name;

            _chipText.Text = row.StateChip.Text;
            _chipText.Style = Ui.Style(TickView.MetaStyle(row.StateChip.Tone));
            _chipText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            _chip.Style = Ui.Style(row.StateChip.Tone switch
            {
                Tone.Success => "RavenStatSuccessStyle",
                Tone.Warning => "RavenStatWarningStyle",
                _ => "RavenPillStyle",
            });
            AutomationProperties.SetName(_chip, Loc.Format("network.state.chip", ("state", row.StateChip.Text)));

            Switch.Visibility = row.HasSwitch ? Visibility.Visible : Visibility.Collapsed;
            Switch.SetQuietly(row.IsOn);
            Switch.IsEnabled = row.SwitchEnabled && !busy;
            AutomationProperties.SetName(Switch, Loc.Format("network.switch", ("name", row.Name)));

            _line.Text = string.Join(" ", new[] { row.Does, row.Reason }.Where(t => !string.IsNullOrWhiteSpace(t)));
            _running.Text = row.RunningText ?? string.Empty;
            _runningRow.Visibility = string.IsNullOrEmpty(row.RunningText) ? Visibility.Collapsed : Visibility.Visible;
            Show(_note, row.Note);

            _torLines.Children.Clear();
            if (row.Tor is TorView tor)
            {
                _torChipText.Text = tor.Phase.Text;
                _torChipText.Style = Ui.Style(TickView.MetaStyle(tor.Phase.Tone));
                _torChipText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                _torChip.Style = Ui.Style(tor.Phase.Tone switch
                {
                    Tone.Success => "RavenStatSuccessStyle",
                    Tone.Warning => "RavenStatWarningStyle",
                    _ => "RavenPillStyle",
                });
                _torChip.Visibility = Visibility.Visible;
                AutomationProperties.SetName(_torChip, Loc.Format("network.state.chip", ("state", tor.Phase.Text)));
                foreach (string line in tor.Lines)
                {
                    TextBlock t = Ui.Text(line, "RavenCaptionTextStyle");
                    t.IsTextSelectionEnabled = true;
                    _torLines.Children.Add(t);
                }
            }
            else
            {
                _torChip.Visibility = Visibility.Collapsed;
            }
            _torLines.Visibility = _torLines.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            _detailLines.Children.Clear();
            if (!string.IsNullOrWhiteSpace(row.Detail)) _detailLines.Children.Add(Mono(row.Detail));
            if (!string.IsNullOrWhiteSpace(row.Gate)) _detailLines.Children.Add(Mono(Loc.Format("network.gate", ("gate", row.Gate))));
            if (row.LabSwitches.Count > 0)
                _detailLines.Children.Add(Mono(Loc.Format("network.labSwitches", ("switches", string.Join(" ", row.LabSwitches)))));
            _details.Visibility = Technical && _detailLines.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(_details, Loc.Get("message.technicalDetails") + " \u00B7 " + row.Name);
        }

        private static TextBlock Mono(string text) => Ui.Text(text, "RavenMonoTextStyle");

        private static void Show(TextBlock target, string? text)
        {
            target.Text = text ?? string.Empty;
            target.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>A help-friends flag: its switch and what it really does.</summary>
    private sealed class FlagView
    {
        private readonly TextBlock _effect;
        private readonly StackPanel _technical = new();

        public FlagView(string setting)
        {
            Setting = setting;
            Switch = new ToggleSwitch { OnContent = string.Empty, OffContent = string.Empty };
            _effect = Ui.Text(string.Empty, "RavenSecondaryTextStyle");
            Root = new StackPanel { Spacing = 2 };
            Root.Children.Add(Switch);
            Root.Children.Add(_effect);
            Root.Children.Add(_technical);
        }

        public string Setting { get; }
        public StackPanel Root { get; }
        public ToggleSwitch Switch { get; }

        public void Update(FlagRow flag, bool enabled)
        {
            Switch.Header = flag.Label;
            Switch.IsOn = flag.IsOn;
            Switch.IsEnabled = enabled;
            AutomationProperties.SetName(Switch, flag.Label);
            _effect.Text = flag.Effect;
            _effect.Visibility = string.IsNullOrWhiteSpace(flag.Effect) ? Visibility.Collapsed : Visibility.Visible;
            _technical.Children.Clear();
            if (Technical && Ui.TechnicalDetails(flag.Technical is null ? null : new[] { flag.Technical }) is Expander details)
                _technical.Children.Add(details);
            Root.Visibility = flag.Shown ? Visibility.Visible : Visibility.Collapsed;
            Debug.Assert(flag.Setting == Setting);
        }
    }
}
