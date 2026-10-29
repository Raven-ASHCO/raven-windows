using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Raven.Windows.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Core.Words;
using Raven.Windows.Helpers;
using Raven.Windows.Services;
using VirtualKey = Windows.System.VirtualKey;

namespace Raven.Windows.Dialogs;

/// <summary>
/// "Check it's really {name}": you read your 10 words (read-aloud highlight, word 10 is
/// the check word) and type the words your friend reads. Typed words become neutral chips
/// (a chip means "a Raven word", never "correct"); a word completes after 3 letters.
/// There is no per-word right or wrong: only Check gives a result, for all 10 words at
/// once: match, one word doesn't fit (typo) or mismatch (do not verify). After 3
/// mismatches Check is no longer offered. The friend's words are never shown.
/// <para>
/// For a friend with an older Raven, a small link switches to the old code: your old code
/// to read aloud and one plain field for theirs (no per-character marks). The result again
/// comes only from Check (verify.check_old_code): match, which verifies the friend exactly
/// as the words do, or mismatch. Mismatches of both kinds share the limit of 3.
/// </para>
/// </summary>
public sealed class CheckWordsDialog : ContentDialog
{
    private readonly RavenSession _session;
    private readonly Contact _contact;
    private readonly string _name;
    private readonly CheckWordsSession _state = new();
    private readonly Grid _typedGrid = new();
    private readonly TextBlock _intro;
    private readonly TextBlock _progress;
    private readonly TextBox _entry = new();
    private readonly TextBlock _hint;
    private readonly TextBlock _entryNote;
    private readonly StackPanel _result = new() { Spacing = 4 };
    private readonly InfoBar _errorBar = new() { IsClosable = true, IsOpen = false };
    private readonly StackPanel _wordsPanel = new() { Spacing = 8 };
    private readonly StackPanel _codePanel = new() { Spacing = 8 };
    private readonly TextBox _codeBox = new();
    private readonly HyperlinkButton _useOldCodeLink;
    private readonly HyperlinkButton _useWordsLink;
    private string? _pendingHint;
    private bool _updatingEntry;
    private bool _checking;
    private int _entryVersion;

    /// <param name="myWords">Your 10 security words (read aloud to the friend).</param>
    /// <param name="myOldCode">Your old letters-and-digits code (read aloud to a friend with an older Raven).</param>
    public CheckWordsDialog(RavenSession session, Contact contact, IReadOnlyList<string> myWords, string myOldCode)
    {
        _session = session;
        _contact = contact;
        _name = ContactPresenter.DisplayName(contact);

        Title = Loc.Format("check.title", ("name", _name));

        var panel = new StackPanel { Spacing = 8, MinWidth = 460, MaxWidth = 560 };
        _intro = Ui.Text(Loc.Format(_state.IntroKey, ("name", _name)), "RavenSecondaryTextStyle");
        panel.Children.Add(_intro);
        panel.Children.Add(_wordsPanel);
        panel.Children.Add(_codePanel);

        // ---- the 10 words ----
        // Your words
        _wordsPanel.Children.Add(Spaced(Ui.Text(Loc.Format("check.yours", ("name", _name)), "RavenNameTextStyle"), top: 6));
        _wordsPanel.Children.Add(new WordGrid(myWords, readAloud: true));

        // Their words, as typed
        var theirsHeader = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        theirsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        theirsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock theirs = Ui.Text(Loc.Format("check.theirs", ("name", _name)), "RavenNameTextStyle");
        _progress = Ui.Text(string.Empty, "RavenSecondaryTextStyle");
        Grid.SetColumn(_progress, 1);
        theirsHeader.Children.Add(theirs);
        theirsHeader.Children.Add(_progress);
        _wordsPanel.Children.Add(theirsHeader);

        for (int c = 0; c < 5; c++) _typedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _typedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _typedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _wordsPanel.Children.Add(_typedGrid);

        _entry.IsSpellCheckEnabled = false;
        _entry.IsTextPredictionEnabled = false;
        _entry.TextChanged += OnEntryChanged;
        _entry.PreviewKeyDown += OnEntryKeyDown;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_entry, Loc.Format("check.theirs", ("name", _name)));
        _wordsPanel.Children.Add(_entry);

        _hint = Ui.Text(string.Empty, "RavenSecondaryTextStyle");
        _hint.MinHeight = 20;
        _wordsPanel.Children.Add(_hint);
        _entryNote = Ui.Text(string.Empty, "RavenWarningTextStyle");
        _entryNote.Visibility = Visibility.Collapsed;
        _wordsPanel.Children.Add(_entryNote);

        // Friends with an older Raven: a small secondary link, not a second button.
        _useOldCodeLink = Ui.SmallLink(Loc.Get("check.oldCode.link"));
        _useOldCodeLink.Click += (_, _) => SetOldCodeMode(true);
        _wordsPanel.Children.Add(_useOldCodeLink);

        // ---- the old code (hidden until the link is used) ----
        _codePanel.Visibility = Visibility.Collapsed;
        _codePanel.Children.Add(Spaced(Ui.Text(Loc.Format("check.oldCode.yours", ("name", _name)), "RavenNameTextStyle"), top: 6));
        TextBlock myCode = Ui.Text(WordEntry.GroupOldCode(myOldCode), "RavenMonoTextStyle");
        myCode.IsTextSelectionEnabled = true;
        _codePanel.Children.Add(myCode);
        _codePanel.Children.Add(Spaced(Ui.Text(Loc.Format("check.oldCode.theirs", ("name", _name)), "RavenNameTextStyle"), top: 6));
        _codeBox.PlaceholderText = Loc.Get("check.oldCode.field");
        _codeBox.IsSpellCheckEnabled = false;
        _codeBox.IsTextPredictionEnabled = false;
        _codeBox.MaxLength = 64;
        if (Ui.Resource("RavenMonoFontFamily") is Microsoft.UI.Xaml.Media.FontFamily mono) _codeBox.FontFamily = mono;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_codeBox, Loc.Get("check.oldCode.field"));
        _codeBox.TextChanged += OnCodeChanged;
        _codePanel.Children.Add(_codeBox);
        _useWordsLink = Ui.SmallLink(Loc.Get("check.oldCode.useWords"));
        _useWordsLink.Click += (_, _) => SetOldCodeMode(false);
        _codePanel.Children.Add(_useWordsLink);

        panel.Children.Add(_result);
        panel.Children.Add(_errorBar);
        Content = panel;

        PrimaryButtonText = Loc.Get("check.verify");
        CloseButtonText = Loc.Get("check.later");
        // No default button: Enter completes a word, it never submits (a paste never submits either).
        DefaultButton = ContentDialogButton.None;
        PrimaryButtonClick += OnCheckClick;
        Opened += (_, _) => _entry.Focus(FocusState.Programmatic);

        RenderTyped();
    }

    /// <summary>True when the engine said "match" (the friend is now verified).</summary>
    public bool Verified { get; private set; }

    // ------------------------------------------------------------------ words or old code

    private void SetOldCodeMode(bool oldCode)
    {
        if (_checking) return;
        _state.SetOldCodeMode(oldCode);
        _wordsPanel.Visibility = _state.OldCodeMode ? Visibility.Collapsed : Visibility.Visible;
        _codePanel.Visibility = _state.OldCodeMode ? Visibility.Visible : Visibility.Collapsed;
        _intro.Text = Loc.Format(_state.IntroKey, ("name", _name));
        _errorBar.IsOpen = false;
        RenderTyped();
        if (_state.OldCodeMode) _codeBox.Focus(FocusState.Programmatic);
        else _entry.Focus(FocusState.Programmatic);
    }

    private void OnCodeChanged(object sender, TextChangedEventArgs e)
    {
        _state.SetOldCode(_codeBox.Text);
        UpdateControls();
        if (_state.Outcome == CheckOutcome.None) _result.Children.Clear(); // editing clears the last result
    }

    // ------------------------------------------------------------------ typing

    private async void OnEntryChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingEntry) return;
        int version = ++_entryVersion;
        _entryNote.Visibility = Visibility.Collapsed;

        (IReadOnlyList<string> complete, string partial) = WordEntry.SplitForEntry(_entry.Text);

        if (complete.Count > 0)
        {
            // Words followed by a space or punctuation (typed or pasted): complete them in order.
            // The words are applied only after every lookup finished for this exact text, so a
            // newer keystroke (which bumps the version) never adds the same word twice.
            string? failed = null;
            var rest = new List<string>();
            var resolved = new List<string>();
            foreach (string token in complete)
            {
                bool full = !_state.CanType || _state.Words.Count + resolved.Count >= CheckWordsSession.WordCount;
                if (failed is not null || full)
                {
                    rest.Add(token);
                    continue;
                }
                string? word = await HintAsync(token);
                if (version != _entryVersion) return;
                if (word is null)
                {
                    failed = token;
                    rest.Add(token);
                    continue;
                }
                resolved.Add(word);
            }
            foreach (string word in resolved) _state.TryAdd(word);
            RenderTyped();

            string remaining = string.Join(" ", rest);
            if (partial.Length > 0) remaining = remaining.Length > 0 ? remaining + " " + partial : partial;
            SetEntryText(remaining);
            if (failed is not null) ShowNotARavenWord();
            partial = rest.Count > 0 ? string.Empty : partial;
        }

        await UpdateHintAsync(partial, version);
    }

    private async Task UpdateHintAsync(string partial, int version)
    {
        _pendingHint = null;
        _hint.Text = string.Empty;
        string? prefix = WordEntry.Prefix(partial);
        if (prefix is null || prefix.Length < 3) return; // completes after 3 letters

        string? word = await HintAsync(partial);
        if (version != _entryVersion) return;
        if (word is null)
        {
            ShowNotARavenWord();
            return;
        }
        _pendingHint = word;
        _hint.Text = "→ " + word;
    }

    private void OnEntryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true; // Enter completes the word; it never submits
            AcceptHint();
        }
        else if (e.Key == VirtualKey.Back && _entry.Text.Length == 0)
        {
            e.Handled = true;
            if (_state.RemoveLast()) RenderTyped();
        }
    }

    private void AcceptHint()
    {
        if (_pendingHint is null || !_state.CanType) return;
        _state.TryAdd(_pendingHint);
        _pendingHint = null;
        _hint.Text = string.Empty;
        SetEntryText(string.Empty);
        RenderTyped();
    }

    private async Task<string?> HintAsync(string token)
    {
        try
        {
            return await _session.Engine.VerifyWordHintAsync(token);
        }
        catch (Exception ex)
        {
            Ui.ShowError(_errorBar, ex);
            return null;
        }
    }

    private void ShowNotARavenWord()
    {
        _entryNote.Text = Loc.Format("check.notARavenWord", ("name", _name));
        _entryNote.Visibility = Visibility.Visible;
    }

    private void SetEntryText(string text)
    {
        _updatingEntry = true;
        try
        {
            _entry.Text = text;
            _entry.SelectionStart = text.Length;
        }
        finally
        {
            _updatingEntry = false;
        }
    }

    /// <summary>Neutral chips for typed words, empty slots for the rest, and "n/10".</summary>
    private void RenderTyped()
    {
        _typedGrid.Children.Clear();
        for (int i = 0; i < CheckWordsSession.WordCount; i++)
        {
            string number = (i + 1).ToString(CultureInfo.InvariantCulture);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(Ui.Text(number, "RavenSmallTextStyle"));
            Border chip;
            if (i < _state.Words.Count)
            {
                row.Children.Add(Ui.Text(_state.Words[i], "RavenChipTextStyle"));
                chip = new Border { Style = Ui.Style("RavenChipStyle"), Child = row };
                ToolTipService.SetToolTip(chip, Loc.Get("check.ravenWord"));
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chip, number + " " + _state.Words[i] + ", " + Loc.Get("check.ravenWord"));
            }
            else
            {
                chip = new Border { Style = Ui.Style("RavenChipEmptyStyle"), Child = row };
            }
            Grid.SetRow(chip, i / 5);
            Grid.SetColumn(chip, i % 5);
            _typedGrid.Children.Add(chip);
        }

        _progress.Text = Loc.Format("check.progress", ("n", _state.Words.Count));
        UpdateControls();
        if (_state.Outcome == CheckOutcome.None) _result.Children.Clear();
    }

    /// <summary>What can be typed or pressed now (nothing more after a match or after 3 mismatches).</summary>
    private void UpdateControls()
    {
        bool open = !_state.IsFinal && !_checking;
        _entry.IsEnabled = open; // Backspace still works at 10/10
        _codeBox.IsEnabled = open;
        _useOldCodeLink.IsEnabled = open;
        _useWordsLink.IsEnabled = open;
        Visibility links = _state.IsFinal ? Visibility.Collapsed : Visibility.Visible;
        _useOldCodeLink.Visibility = links;
        _useWordsLink.Visibility = links;
        IsPrimaryButtonEnabled = _state.CanCheck && !_checking;
    }

    // ------------------------------------------------------------------ check

    private void OnCheckClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; // the result is shown in the dialog
        if (!_state.CanCheck) return;
        _ = CheckAsync(args.GetDeferral());
    }

    private async Task CheckAsync(ContentDialogButtonClickDeferral deferral)
    {
        _checking = true;
        UpdateControls();
        _result.Children.Clear();
        if (!_state.OldCodeMode) _result.Children.Add(Ui.Text(Loc.Get("check.checking"), "RavenSecondaryTextStyle"));
        try
        {
            string engineResult = _state.OldCodeMode
                ? await _session.Engine.VerifyCheckOldCodeAsync(_contact.Address, _state.OldCode.Trim())
                : await _session.Engine.VerifyCheckWordsAsync(_contact.Address, _state.Words);
            CheckOutcome outcome = _state.Record(engineResult);
            RenderResult(outcome);
            if (outcome == CheckOutcome.Match)
            {
                Verified = true;
                CloseButtonText = Loc.Get("common.close");
            }
            _errorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            _result.Children.Clear();
            Ui.ShowError(_errorBar, ex);
        }
        finally
        {
            deferral.Complete();
            _checking = false;
            UpdateControls();
        }
    }

    private void RenderResult(CheckOutcome outcome)
    {
        _result.Children.Clear();
        (string[] keys, Tone tone) = _state.CurrentResultTexts();
        for (int i = 0; i < keys.Length; i++)
        {
            string text = Loc.Format(keys[i], ("name", _name));
            if (i == 0 && outcome == CheckOutcome.Match) text = "✓ " + text;
            TextBlock line = Ui.Text(text, i == 0 ? Ui.BodyStyleFor(tone) : "RavenSecondaryTextStyle");
            if (i == 0) line.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            _result.Children.Add(line);
        }
    }

    private static FrameworkElement Spaced(FrameworkElement element, double top)
    {
        element.Margin = new Thickness(0, top, 0, 0);
        return element;
    }
}
