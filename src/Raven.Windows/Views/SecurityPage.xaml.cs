using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;
using Raven.Windows.Core.ViewModels;
using Raven.Windows.Dialogs;
using Raven.Windows.Helpers;
using Raven.Windows.Services;

namespace Raven.Windows.Views;

/// <summary>
/// Security: the security notices (security.status, read-only; resolving stays in the
/// terminal) with what the background program counted. Lock and Updates are Settings sections
/// (Calm Settings); a line here says so and opens Settings. While notices are not in this build
/// it says so in plain words.
/// </summary>
public sealed partial class SecurityPage : UserControl
{
    private readonly RavenSession _session;
    private readonly Action _openSettings;
    private readonly SecurityViewModel _vm;

    /// <param name="openSettings">Shows Settings (Lock and Updates live there).</param>
    public SecurityPage(RavenSession session, Action openSettings)
    {
        _session = session;
        _openSettings = openSettings;
        _vm = new SecurityViewModel(session.Engine);
        InitializeComponent();
        _vm.Changed += Render;
        Render();
    }

    public async Task RefreshAsync()
    {
        await _vm.LoadAsync();
        if (_vm.Lock.State is LockState state) _session.SetLock(state);
    }

    private void Render()
    {
        bool busy = _vm.Busy || _vm.Lock.Busy;
        BusyRing.IsActive = busy && Motion.Enabled;
        BusyRing.Visibility = busy && Motion.Enabled ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.IsEnabled = !busy;

        // Security notices
        SecurityText.Text = _vm.SecurityText;
        Show(SecurityLabText, _vm.SecurityLabText);
        ShowError(SecurityErrorBar, _vm.SecurityError);
        NoticesPanel.Children.Clear();
        if (_vm.Security is { Available: true } && _vm.Notices.Count == 0)
            NoticesPanel.Children.Add(Ui.Text(Loc.Get("security.none"), "RavenSecondaryTextStyle"));
        foreach (NoticeView notice in _vm.Notices) NoticesPanel.Children.Add(NoticeCard(notice));
        Show(CountedText, _vm.CountedText);
        Fill(NodePanel, _vm.NodeLines);
        NodeTitle.Visibility = _vm.NodeLines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResolveText.Visibility = _vm.Notices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Border NoticeCard(NoticeView notice)
    {
        var panel = new StackPanel { Spacing = 4 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        TextBlock level = Ui.Text(notice.LevelText, Ui.SmallStyleFor(notice.Tone));
        var chip = new Border { Style = Ui.Style("RavenPillStyle"), Child = level };
        header.Children.Add(chip);
        TextBlock headline = Ui.Text(notice.Headline);
        headline.FontWeight = FontWeights.SemiBold;
        headline.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(headline);
        panel.Children.Add(header);
        if (!string.IsNullOrWhiteSpace(notice.Name)) panel.Children.Add(Ui.Text(notice.Name, "RavenSecondaryTextStyle"));
        if (!string.IsNullOrWhiteSpace(notice.WhatRavenDid))
            panel.Children.Add(Ui.Text(Loc.Format("security.did", ("text", notice.WhatRavenDid))));
        if (!string.IsNullOrWhiteSpace(notice.WhatToDo))
            panel.Children.Add(Ui.Text(Loc.Format("security.todo", ("text", notice.WhatToDo))));
        if (!string.IsNullOrWhiteSpace(notice.When))
            panel.Children.Add(Ui.Text(Loc.Format("security.when", ("when", notice.When)), "RavenSecondaryTextStyle"));
        if (notice.Details.Count > 0)
        {
            var details = new StackPanel { Spacing = 2 };
            foreach (string line in notice.Details) details.Children.Add(Ui.Text(line, "RavenMonoTextStyle"));
            panel.Children.Add(new Expander
            {
                Header = Loc.Get("message.details"),
                Content = details,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            });
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(panel, notice.LevelText + ": " + notice.Headline);
        return new Border { Style = Ui.Style("RavenCardStyle"), Child = panel };
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e) => _openSettings();

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = RefreshAsync();

    private static void ShowError(InfoBar bar, ErrorInfo? error)
    {
        if (error is null)
        {
            bar.IsOpen = false;
            return;
        }
        Ui.ShowError(bar, new RavenException(error.Code, error.Message, error.Detail));
    }

    private static void Show(TextBlock target, string? text)
    {
        target.Text = text ?? string.Empty;
        target.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void Fill(StackPanel panel, IReadOnlyList<string> lines)
    {
        panel.Children.Clear();
        foreach (string line in lines) panel.Children.Add(Ui.Text(line));
        panel.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
