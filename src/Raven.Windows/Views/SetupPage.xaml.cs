using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Raven.Windows.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;
using Raven.Windows.Core.Words;
using Raven.Windows.Helpers;
using Raven.Windows.Services;
using VirtualKey = Windows.System.VirtualKey;

namespace Raven.Windows.Views;

/// <summary>
/// First-run setup: name (optional) → receive messages? → creating identity → your
/// security words. Every step is the engine's work; this page only asks and shows.
/// </summary>
public sealed partial class SetupPage : UserControl
{
    private readonly RavenSession _session;
    private readonly Action _onDone;
    private readonly StepsBar _steps = new();
    private string? _name;

    public SetupPage(RavenSession session, Action onDone)
    {
        _session = session;
        _onDone = onDone;
        InitializeComponent();
        StepsHost.Content = _steps.Root;
        Loaded += (_, _) => NameBox.Focus(FocusState.Programmatic);
    }

    private void Show(StackPanel step)
    {
        NameStep.Visibility = step == NameStep ? Visibility.Visible : Visibility.Collapsed;
        ReceiveStep.Visibility = step == ReceiveStep ? Visibility.Visible : Visibility.Collapsed;
        CreatingStep.Visibility = step == CreatingStep ? Visibility.Visible : Visibility.Collapsed;
        WordsStep.Visibility = step == WordsStep ? Visibility.Visible : Visibility.Collapsed;
        _steps.Set(step == NameStep ? 0 : step == ReceiveStep ? 1 : step == CreatingStep ? 2 : 3);
    }

    private void OnNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            OnNameNext(sender, new RoutedEventArgs());
        }
    }

    private void OnNameNext(object sender, RoutedEventArgs e)
    {
        string name = TextSanitizer.SingleLine(NameBox.Text);
        _name = name.Length == 0 ? null : name;
        Show(ReceiveStep);
        ReceiveYes.Focus(FocusState.Programmatic);
    }

    private void OnReceiveYes(object sender, RoutedEventArgs e) => _ = CreateIdentityAsync(receive: true);

    private void OnReceiveNo(object sender, RoutedEventArgs e) => _ = CreateIdentityAsync(receive: false);

    private async Task CreateIdentityAsync(bool receive)
    {
        ErrorBar.IsOpen = false;
        Show(CreatingStep);
        CreatingRing.IsActive = Motion.Enabled; // a spinner only while motion is allowed; the text says it anyway

        try
        {
            IdentityCreated created = await _session.Engine.IdentityCreateAsync(_name);
            var settings = new JsonObject { ["receive"] = receive };
            if (_name is not null) settings["display_name"] = _name;
            await _session.UpdateSettingsAsync(settings);

            // Starting the background program is when Windows may ask about the firewall.
            _session.SetNode(await _session.Engine.NodeStartAsync());

            Me me = await _session.Engine.IdentityMeAsync();
            ShowWords(me.Words.Count == 10 ? me.Words : created.Words, string.IsNullOrEmpty(me.OldCode) ? created.OldCode : me.OldCode);
        }
        catch (Exception ex)
        {
            Show(ReceiveStep);
            Ui.ShowError(ErrorBar, ex);
        }
        finally
        {
            CreatingRing.IsActive = false;
        }
    }

    private void ShowWords(System.Collections.Generic.IReadOnlyList<string> words, string oldCode)
    {
        // The identity was just made: the words come in one by one, then Read aloud starts.
        WordsHost.Content = new WordGrid(words, readAloud: true, startStepping: true, reveal: true);
        OldCode.Text = Loc.Format("words.oldCode", ("code", WordEntry.GroupOldCode(oldCode)));
        Show(WordsStep);
        DoneButton.Focus(FocusState.Programmatic);
    }

    private void OnDone(object sender, RoutedEventArgs e) => _onDone();
}
