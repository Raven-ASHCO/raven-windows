using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;
using Raven.Windows.Core.ViewModels;
using Raven.Windows.Helpers;
using Raven.Windows.Services;

namespace Raven.Windows.Views;

/// <summary>
/// Check network (network.check): raven doctor's summary in its order and its next step, this
/// computer's network lines with Raven's plain sentence first and the doctor's words below,
/// and the router check only when the person asks (a read-only question to the router that
/// opens nothing and saves nothing; up to about 4 s).
/// </summary>
public sealed partial class NetworkCheckPage : UserControl
{
    private readonly NetworkCheckViewModel _vm;
    private readonly Action _back;
    private bool _ran;

    /// <param name="back">Back to the Network page.</param>
    public NetworkCheckPage(RavenSession session, Action back)
    {
        _back = back;
        _vm = new NetworkCheckViewModel(session.Engine);
        InitializeComponent();
        _vm.Changed += Render;
        Render();
    }

    /// <summary>Runs the check the first time the page is shown (not the router).</summary>
    public Task ShowAsync()
    {
        if (_ran) return Task.CompletedTask;
        _ran = true;
        return _vm.RunAsync();
    }

    private void Render()
    {
        bool busy = _vm.Busy;
        BusyRing.IsActive = busy && Motion.Enabled;
        BusyRing.Visibility = busy && Motion.Enabled ? Visibility.Visible : Visibility.Collapsed;
        BusyText.Text = !busy ? string.Empty : Loc.Get(_vm.AskingRouter ? "network.check.router.asking" : "network.check.running");
        AgainButton.IsEnabled = !busy;
        RouterButton.IsEnabled = !busy;

        if (_vm.Error is ErrorInfo error) Ui.ShowError(ErrorBar, new RavenException(error.Code, error.Message, error.Detail));
        else ErrorBar.IsOpen = false;

        SummaryPanel.Children.Clear();
        foreach (CheckLine line in _vm.Summary) SummaryPanel.Children.Add(Selectable(line.Title, "RavenBodyTextStyle"));
        NextStepText.Text = _vm.NextStep.Length == 0 ? string.Empty : Loc.Format("network.check.nextStep", ("step", _vm.NextStep));
        NextStepText.Visibility = _vm.NextStep.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        FillLines(LinesPanel, _vm.Lines, _vm.Technical);
        if (_vm.RouterLines is IReadOnlyList<CheckLine> router) FillLines(RouterPanel, router, _vm.RouterTechnical);
        else
        {
            RouterPanel.Children.Clear();
            RouterPanel.Children.Add(Ui.Text(Loc.Get("network.check.routerNotAsked"), "RavenSecondaryTextStyle"));
        }
    }

    /// <summary>Plain sentences; the raw lines and the CLI's words (commands included) only under Technical details (E1).</summary>
    private static void FillLines(StackPanel panel, IReadOnlyList<CheckLine> lines, IReadOnlyList<string> technical)
    {
        panel.Children.Clear();
        foreach (CheckLine line in lines) panel.Children.Add(Selectable(line.Title, "RavenBodyTextStyle"));
        if (Ui.TechnicalDetails(technical) is Expander details) panel.Children.Add(details);
    }

    private static TextBlock Selectable(string text, string style)
    {
        TextBlock block = Ui.Text(text, style);
        block.IsTextSelectionEnabled = true;
        return block;
    }

    private void OnAgainClick(object sender, RoutedEventArgs e) => _ = _vm.RunAsync();

    private void OnRouterClick(object sender, RoutedEventArgs e) => _ = _vm.RunAsync(router: true);

    private void OnBackClick(object sender, RoutedEventArgs e) => _back();
}
