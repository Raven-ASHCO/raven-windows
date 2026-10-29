using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Core.ViewModels;
using Raven.Windows.Helpers;
using Raven.Windows.Services;

namespace Raven.Windows.Dialogs;

/// <summary>
/// A friend's routes (contacts.routes / contacts.set_routes): the saved routes with their state
/// chips (LAN, Internet, p2p with its relays, nearby for a verified friend), what the friend
/// told this computer (route hints), who carries the friend's mail, and the changes the app
/// can make: an Internet or p2p route, removing a route, up to two verified mail friends, and
/// hiding this computer's IP address from this friend. Never the friend's security words.
/// </summary>
public sealed class ContactRoutesDialog : ContentDialog
{
    private readonly ContactRoutesViewModel _vm;
    private readonly StackPanel _routes = new() { Spacing = 8 };
    /// <summary>Where Raven reaches the friend on the local network now, and "Use the new address".</summary>
    private readonly StackPanel _lanNow = new() { Spacing = 2 };
    private readonly Button _useLanNow = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _hints;
    private readonly StackPanel _learned = new() { Spacing = 2 };
    private readonly TextBlock _carriersTitle;
    private readonly StackPanel _carriers = new() { Spacing = 2 };
    private readonly TextBox _internet = new() { IsSpellCheckEnabled = false, IsTextPredictionEnabled = false };
    private readonly TextBox _p2p = new() { IsSpellCheckEnabled = false, IsTextPredictionEnabled = false };
    private readonly TextBox _via = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 64,
        IsSpellCheckEnabled = false,
        IsTextPredictionEnabled = false,
    };
    private readonly Button _saveRoutes = new();
    private readonly TextBlock _mailFriendsTitle;
    private readonly StackPanel _custodians = new() { Spacing = 2 };
    private readonly TextBlock _noVerified;
    private readonly Button _saveCustodians = new();
    private readonly ToggleSwitch _hideIp = new() { OnContent = string.Empty, OffContent = string.Empty };
    private readonly InfoBar _errorBar = new() { IsClosable = true, IsOpen = false };
    private readonly InfoBar _savedBar = new() { IsClosable = true, IsOpen = false, Severity = InfoBarSeverity.Success };
    private readonly ProgressRing _ring = new() { Width = 16, Height = 16, IsActive = false, Visibility = Visibility.Collapsed };
    private readonly List<CheckBox> _custodianBoxes = new();
    private bool _rendering;
    private bool _filled;

    public ContactRoutesDialog(RavenSession session, Contact contact)
    {
        _vm = new ContactRoutesViewModel(session.Engine, contact);
        string name = _vm.Name;
        Title = Loc.Format("network.routes.title", ("name", name));

        var panel = new StackPanel { Spacing = 12, MaxWidth = 500 };
        AutomationProperties.SetName(_ring, Loc.Get("network.check.running"));
        panel.Children.Add(_ring);

        panel.Children.Add(Heading(Loc.Get("network.routes.saved")));
        panel.Children.Add(_routes);
        _useLanNow.Content = Loc.Get("network.routes.lanReplace");
        AutomationProperties.SetName(_useLanNow, Loc.Get("network.routes.lanReplace"));
        _useLanNow.Click += (_, _) => _ = ChangeAsync(_vm.UseLanNowAsync);
        panel.Children.Add(_lanNow);
        _hints = Ui.Text(string.Empty, "RavenSecondaryTextStyle");
        panel.Children.Add(_hints);

        panel.Children.Add(Heading(Loc.Format("network.routes.hints", ("name", name))));
        panel.Children.Add(_learned);

        _carriersTitle = Heading(Loc.Format("network.routes.carriers", ("name", name)));
        panel.Children.Add(_carriersTitle);
        panel.Children.Add(_carriers);

        // Change routes
        var edit = new StackPanel { Spacing = 8 };
        _internet.Header = Loc.Get("network.routes.internet");
        AutomationProperties.SetName(_internet, Loc.Get("network.routes.internet"));
        _p2p.Header = Loc.Get("network.routes.p2p");
        AutomationProperties.SetName(_p2p, Loc.Get("network.routes.p2p"));
        _via.Header = Loc.Get("network.routes.relays");
        AutomationProperties.SetName(_via, Loc.Get("network.routes.relays"));
        _saveRoutes.Content = Loc.Get("network.routes.save");
        AutomationProperties.SetName(_saveRoutes, Loc.Get("network.routes.save"));
        _saveRoutes.Click += (_, _) => _ = SaveRoutesAsync();
        edit.Children.Add(_internet);
        edit.Children.Add(_p2p);
        edit.Children.Add(_via);
        edit.Children.Add(_saveRoutes);
        panel.Children.Add(new Expander
        {
            Header = Loc.Get("network.routes.edit"),
            Content = edit,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        });

        // Mail friends
        _mailFriendsTitle = Heading(Loc.Format("network.routes.mailFriends", ("name", name)));
        panel.Children.Add(_mailFriendsTitle);
        _noVerified = Ui.Text(Loc.Get("network.routes.noVerified"), "RavenSecondaryTextStyle");
        panel.Children.Add(_noVerified);
        panel.Children.Add(_custodians);
        _saveCustodians.Content = Loc.Get("network.routes.saveMailFriends");
        AutomationProperties.SetName(_saveCustodians, Loc.Get("network.routes.saveMailFriends"));
        _saveCustodians.Click += (_, _) => _ = SaveMailFriendsAsync();
        panel.Children.Add(_saveCustodians);

        // Hide this computer's IP address from this friend
        string hideIp = Loc.Format("network.routes.hideIp", ("name", name));
        _hideIp.Header = hideIp;
        AutomationProperties.SetName(_hideIp, hideIp);
        _hideIp.Toggled += (_, _) =>
        {
            if (!_rendering) _ = ChangeAsync(() => _vm.SetHideIpAsync(_hideIp.IsOn));
        };
        panel.Children.Add(_hideIp);

        panel.Children.Add(_savedBar);
        panel.Children.Add(_errorBar);
        Content = panel;

        CloseButtonText = Loc.Get("common.close");
        DefaultButton = ContentDialogButton.Close;

        _vm.Changed += RenderState;
        Opened += (_, _) => _ = _vm.LoadAsync();
    }

    /// <summary>Something was saved (the friend list may show it).</summary>
    public bool RoutesChanged { get; private set; }

    private void RenderState()
    {
        _rendering = true;
        try
        {
            bool busy = _vm.Busy;
            _ring.IsActive = busy && Motion.Enabled;
            _ring.Visibility = busy && Motion.Enabled ? Visibility.Visible : Visibility.Collapsed;

            _routes.Children.Clear();
            if (_vm.Routes is not null && _vm.RouteLines.Count == 0)
                _routes.Children.Add(Ui.Text(Loc.Get("network.routes.none"), "RavenSecondaryTextStyle"));
            foreach (RouteLine route in _vm.RouteLines) _routes.Children.Add(RouteRow(route, busy));
            RenderLanNow(busy);

            _hints.Text = _vm.HintsText;
            Fill(_learned, _vm.LearnedLines);
            Fill(_carriers, _vm.CarrierLines);

            if (!_filled && _vm.Routes is not null)
            {
                _internet.Text = _vm.InternetRoute;
                _p2p.Text = _vm.P2pRoute;
                _via.Text = string.Join("\n", _vm.P2pVia);
                _filled = true;
            }
            _saveRoutes.IsEnabled = !busy && _vm.Routes is not null;

            RenderCustodians(busy);

            _hideIp.IsOn = _vm.HideIp;
            _hideIp.IsEnabled = !busy && _vm.Routes is not null;

            if (_vm.Error is ErrorInfo error) Ui.ShowError(_errorBar, new RavenException(error.Code, error.Message, error.Detail));
            else _errorBar.IsOpen = false;
            _savedBar.Message = Loc.Get("network.routes.done");
            _savedBar.IsOpen = _vm.Saved && _vm.Error is null;
        }
        finally
        {
            _rendering = false;
        }
    }

    /// <summary>
    /// The address Raven reaches the friend at now, that the saved one is old (E1: only the old
    /// one was shown while every message went to the new one), and the button that saves it.
    /// </summary>
    private void RenderLanNow(bool busy)
    {
        _lanNow.Children.Clear();
        foreach (string? line in new[] { _vm.LanNowText, _vm.LanStaleText })
        {
            if (string.IsNullOrEmpty(line)) continue;
            TextBlock text = Ui.Text(line, "RavenSecondaryTextStyle");
            text.IsTextSelectionEnabled = true;
            _lanNow.Children.Add(text);
        }
        if (_vm.CanUseLanNow)
        {
            _useLanNow.IsEnabled = !busy;
            _lanNow.Children.Add(_useLanNow);
        }
        _lanNow.Visibility = _lanNow.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Grid RouteRow(RouteLine route, bool busy)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { Spacing = 0 };
        TextBlock label = Ui.Text(route.Label);
        label.FontWeight = FontWeights.SemiBold;
        text.Children.Add(label);
        if (!string.IsNullOrEmpty(route.Value))
        {
            TextBlock value = Ui.Text(route.Value, "RavenMonoTextStyle");
            text.Children.Add(value);
        }
        if (route.Via is string via) text.Children.Add(Ui.Text(via, "RavenSecondaryTextStyle"));
        if (route.Next is string next) text.Children.Add(Ui.Text(next, "RavenSecondaryTextStyle"));
        row.Children.Add(text);

        TextBlock chipText = Ui.Text(route.StateChip.Text, Ui.SmallStyleFor(route.StateChip.Tone));
        AutomationProperties.SetName(chipText, Loc.Format("network.state.chip", ("state", route.StateChip.Text)));
        var chip = new Border { Style = Ui.Style("RavenPillStyle"), Child = chipText, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(chip, 1);
        row.Children.Add(chip);

        if (route.CanRemove)
        {
            var remove = new Button
            {
                Content = Loc.Get("network.routes.remove"),
                IsEnabled = !busy,
                VerticalAlignment = VerticalAlignment.Top,
            };
            AutomationProperties.SetName(remove, Loc.Format("network.routes.removeNamed", ("route", route.Label)));
            string kind = route.Kind;
            remove.Click += (_, _) => _ = ChangeAsync(() => _vm.RemoveRouteAsync(kind));
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
        }
        return row;
    }

    private void RenderCustodians(bool busy)
    {
        IReadOnlyList<CustodianChoice> choices = _vm.CustodianChoices;
        bool sameChoices = _custodianBoxes.Count == choices.Count
            && _custodianBoxes.Zip(choices).All(pair => (string)pair.First.Tag == pair.Second.Address);
        if (!sameChoices)
        {
            _custodians.Children.Clear();
            _custodianBoxes.Clear();
            foreach (CustodianChoice choice in choices)
            {
                var box = new CheckBox { Content = choice.Name, Tag = choice.Address };
                AutomationProperties.SetName(box, choice.Name);
                box.Checked += (_, _) => LimitMailFriends();
                box.Unchecked += (_, _) => LimitMailFriends();
                _custodianBoxes.Add(box);
                _custodians.Children.Add(box);
            }
        }
        foreach ((CheckBox box, CustodianChoice choice) in _custodianBoxes.Zip(choices)) box.IsChecked = choice.Selected;
        LimitMailFriends();
        foreach (CheckBox box in _custodianBoxes) box.IsEnabled = box.IsEnabled && !busy;

        bool any = choices.Count > 0;
        _noVerified.Visibility = any || _vm.Routes is null ? Visibility.Collapsed : Visibility.Visible;
        _saveCustodians.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _saveCustodians.IsEnabled = !busy;
    }

    /// <summary>At most two mail friends: the other boxes wait while two are checked.</summary>
    private void LimitMailFriends()
    {
        int chosen = _custodianBoxes.Count(b => b.IsChecked == true);
        foreach (CheckBox box in _custodianBoxes)
            box.IsEnabled = box.IsChecked == true || chosen < ContactRoutesViewModel.MaxMailFriends;
    }

    private async Task SaveRoutesAsync()
    {
        string[] via = (_via.Text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (await _vm.SaveRoutesAsync(_internet.Text, _p2p.Text, via))
        {
            RoutesChanged = true;
            _filled = false; // show what the engine saved
            RenderState();
        }
    }

    private Task SaveMailFriendsAsync()
    {
        List<string> chosen = _custodianBoxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToList();
        return ChangeAsync(() => _vm.SetMailFriendsAsync(chosen));
    }

    private async Task ChangeAsync(Func<Task<bool>> change)
    {
        if (await change()) RoutesChanged = true;
    }

    private static TextBlock Heading(string text)
    {
        TextBlock heading = Ui.Text(text);
        heading.FontWeight = FontWeights.SemiBold;
        return heading;
    }

    private static void Fill(StackPanel panel, IReadOnlyList<string> lines)
    {
        panel.Children.Clear();
        foreach (string line in lines)
        {
            TextBlock text = Ui.Text(line, "RavenSecondaryTextStyle");
            text.IsTextSelectionEnabled = true;
            panel.Children.Add(text);
        }
    }
}
