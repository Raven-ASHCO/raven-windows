using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;
using Raven.Windows.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Raven.Windows.Dialogs;

/// <summary>
/// Add a friend, with the step strip "Share yours · Paste theirs · Check words".
/// 1. Share: your invite (public information only) with Copy and an opt-in to include
///    your network address.
/// 2. Paste: your friend's invite or card; once Raven reads it the box collapses to a
///    one-line summary (click it to edit), with a line when the engine left the card's
///    internet or peer-to-peer routes out (card.parse "note"). Then the name you want to use.
/// 3. Check words now, or later (the friend is saved as not verified).
/// The friend's security words are never shown here.
/// </summary>
public sealed class AddFriendDialog : ContentDialog
{
    private enum Step
    {
        Share,
        Paste,
        Added,
    }

    private readonly RavenSession _session;
    private readonly TextBlock[] _stepLabels = new TextBlock[3];
    private readonly InfoBar _errorBar = new() { IsClosable = true, IsOpen = false };

    // 1. Share
    private readonly StackPanel _sharePanel = new() { Spacing = 10 };
    private readonly TextBlock _inviteText;
    private readonly CheckBox _includeAddress = new();
    private readonly Button _copyButton = new();

    // 2. Paste
    private readonly StackPanel _pastePanel = new() { Spacing = 10 };
    private readonly TextBox _pasteBox = new();
    private readonly Button _summaryButton = new();
    private readonly TextBlock _summaryText;
    private readonly TextBlock _ignoredText;
    private readonly StackPanel _ignoredDetails = new();
    private readonly TextBlock _pasteError;
    private readonly StackPanel _namePanel = new() { Spacing = 6 };
    private readonly TextBox _nameBox = new();
    private readonly TextBlock _nameError;

    // 3. Added
    private readonly StackPanel _addedPanel = new() { Spacing = 10 };
    private readonly TextBlock _mustAddBack;

    private readonly DispatcherQueueTimer _parseTimer;
    private Step _step = Step.Share;
    private CardParsed? _parsed;
    private string _invite = string.Empty;
    private int _parseVersion;

    public AddFriendDialog(RavenSession session)
    {
        _session = session;
        Title = Loc.Get("add.title");

        // Step strip
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        string[] stepKeys = { "add.step1", "add.step2", "add.step3" };
        for (int i = 0; i < 3; i++)
        {
            if (i > 0) strip.Children.Add(Ui.Text("·", "RavenSecondaryTextStyle"));
            _stepLabels[i] = Ui.Text($"{i + 1} {Loc.Get(stepKeys[i])}", "RavenSecondaryTextStyle");
            strip.Children.Add(_stepLabels[i]);
        }

        // 1. Share yours
        _sharePanel.Children.Add(Ui.Text(Loc.Get("add.share.body")));
        _inviteText = Ui.Text(string.Empty, "RavenMonoTextStyle");
        var inviteCard = new Border { Style = Ui.Style("RavenCardStyle"), Padding = new Thickness(12), Child = _inviteText };
        _sharePanel.Children.Add(inviteCard);
        _copyButton.Content = Loc.Get("add.share.copy");
        _copyButton.Click += (_, _) => CopyInvite();
        _sharePanel.Children.Add(_copyButton);
        _includeAddress.Visibility = Visibility.Collapsed;
        _includeAddress.Checked += (_, _) => _ = LoadCardAsync(includeAddress: true);
        _includeAddress.Unchecked += (_, _) => _ = LoadCardAsync(includeAddress: false);
        _sharePanel.Children.Add(_includeAddress);
        _sharePanel.Children.Add(Ui.Text(Loc.Get("add.share.public"), "RavenSecondaryTextStyle"));

        // 2. Paste theirs
        _pastePanel.Children.Add(Ui.Text(Loc.Get("add.paste.body")));
        _pasteBox.AcceptsReturn = true;
        _pasteBox.TextWrapping = TextWrapping.Wrap;
        _pasteBox.MinHeight = 80;
        _pasteBox.MaxHeight = 160;
        if (Ui.Resource("RavenMonoFontFamily") is Microsoft.UI.Xaml.Media.FontFamily mono) _pasteBox.FontFamily = mono;
        _pasteBox.TextChanged += (_, _) => ScheduleParse();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_pasteBox, Loc.Get("add.paste.body"));
        _pastePanel.Children.Add(_pasteBox);

        _summaryText = Ui.Text(string.Empty);
        _summaryButton.Content = _summaryText;
        _summaryButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _summaryButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        _summaryButton.Visibility = Visibility.Collapsed;
        _summaryButton.Click += (_, _) => ExpandPaste();
        _pastePanel.Children.Add(_summaryButton);

        // The card's internet or peer-to-peer routes the engine left out: a plain line, and the
        // engine's own words under Technical details when those are shown.
        _ignoredText = Ui.Text(string.Empty, "RavenSecondaryTextStyle");
        _ignoredText.TextWrapping = TextWrapping.Wrap;
        _ignoredText.Visibility = Visibility.Collapsed;
        _pastePanel.Children.Add(_ignoredText);
        _ignoredDetails.Visibility = Visibility.Collapsed;
        _pastePanel.Children.Add(_ignoredDetails);

        _pasteError = Ui.Text(string.Empty, "RavenDangerTextStyle");
        _pasteError.Visibility = Visibility.Collapsed;
        _pastePanel.Children.Add(_pasteError);

        _namePanel.Visibility = Visibility.Collapsed;
        _namePanel.Children.Add(Ui.Text(Loc.Get("add.name.title")));
        _nameBox.MaxLength = 64;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_nameBox, Loc.Get("add.name.title"));
        // Made before the handler that hides it (CS8602: the handler read it before it was set).
        _nameError = Ui.Text(string.Empty, "RavenDangerTextStyle");
        _nameError.Visibility = Visibility.Collapsed;
        _nameBox.TextChanged += (_, _) =>
        {
            _nameError.Visibility = Visibility.Collapsed;
            UpdateButtons();
        };
        _namePanel.Children.Add(_nameBox);
        _namePanel.Children.Add(Ui.Text(Loc.Get("add.name.note"), "RavenSecondaryTextStyle"));
        _namePanel.Children.Add(_nameError);
        _pastePanel.Children.Add(_namePanel);

        // 3. Added: check words now or later
        _mustAddBack = Ui.Text(string.Empty);
        _addedPanel.Children.Add(_mustAddBack);

        var content = new StackPanel { Spacing = 8, MinWidth = 420, MaxWidth = 520 };
        content.Children.Add(strip);
        content.Children.Add(_sharePanel);
        content.Children.Add(_pastePanel);
        content.Children.Add(_addedPanel);
        content.Children.Add(_errorBar);
        Content = content;

        _parseTimer = DispatcherQueue.CreateTimer();
        _parseTimer.Interval = TimeSpan.FromMilliseconds(350);
        _parseTimer.IsRepeating = false;
        _parseTimer.Tick += (_, _) => _ = ParseAsync();

        DefaultButton = ContentDialogButton.Primary;
        PrimaryButtonClick += OnPrimaryClick;
        SecondaryButtonClick += OnSecondaryClick;
        Opened += (_, _) => _ = LoadCardAsync(includeAddress: false);

        ShowStep(Step.Share);
    }

    /// <summary>The friend that was added (saved as not verified), or null.</summary>
    public Contact? AddedContact { get; private set; }

    /// <summary>True when the user chose "Check words" at the end.</summary>
    public bool CheckWordsNext { get; private set; }

    // ------------------------------------------------------------------ steps

    private void ShowStep(Step step)
    {
        _step = step;
        _sharePanel.Visibility = step == Step.Share ? Visibility.Visible : Visibility.Collapsed;
        _pastePanel.Visibility = step == Step.Paste ? Visibility.Visible : Visibility.Collapsed;
        _addedPanel.Visibility = step == Step.Added ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < _stepLabels.Length; i++)
        {
            bool current = i == (int)step;
            _stepLabels[i].Style = Ui.Style(current ? "RavenNameTextStyle" : "RavenSecondaryTextStyle");
        }

        switch (step)
        {
            case Step.Share:
                PrimaryButtonText = Loc.Get("common.next");
                SecondaryButtonText = string.Empty;
                CloseButtonText = Loc.Get("common.cancel");
                break;
            case Step.Paste:
                PrimaryButtonText = Loc.Get("add.save");
                SecondaryButtonText = Loc.Get("common.back");
                CloseButtonText = Loc.Get("common.cancel");
                _pasteBox.Focus(FocusState.Programmatic);
                break;
            case Step.Added:
                PrimaryButtonText = Loc.Get("home.banner.notVerified.action");
                SecondaryButtonText = string.Empty;
                CloseButtonText = Loc.Get("check.later");
                break;
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        IsPrimaryButtonEnabled = _step switch
        {
            Step.Paste => _parsed is not null && TextSanitizer.SingleLine(_nameBox.Text).Length > 0,
            _ => true,
        };
    }

    private void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        switch (_step)
        {
            case Step.Share:
                args.Cancel = true;
                ShowStep(Step.Paste);
                break;
            case Step.Paste:
                args.Cancel = true;
                _ = AddAsync(args.GetDeferral());
                break;
            case Step.Added:
                CheckWordsNext = true; // the dialog closes; the shell opens Check words
                break;
        }
    }

    private void OnSecondaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_step == Step.Paste)
        {
            args.Cancel = true;
            ShowStep(Step.Share);
        }
    }

    // ------------------------------------------------------------------ 1. share

    private async Task LoadCardAsync(bool includeAddress)
    {
        try
        {
            CardMade card = await _session.Engine.CardMakeAsync(includeAddress);
            _invite = card.Invite;
            _inviteText.Text = card.Invite;
            _copyButton.Content = Loc.Get("add.share.copy");

            string? addr = card.AddressIncluded ?? NodeAddress();
            if (addr is not null)
            {
                _includeAddress.Content = Loc.Format("add.share.includeAddress", ("addr", addr));
                _includeAddress.Visibility = Visibility.Visible;
            }
            _errorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            Ui.ShowError(_errorBar, ex);
        }
    }

    private string? NodeAddress()
    {
        NodeStatus? node = _session.Node;
        if (node is null || string.IsNullOrEmpty(node.Address) || node.ListeningPort is null || node.IsLocalOnly) return null;
        return node.Address + ":" + node.ListeningPort;
    }

    private void CopyInvite()
    {
        if (string.IsNullOrEmpty(_invite)) return;
        var data = new DataPackage();
        data.SetText(_invite);
        Clipboard.SetContent(data);
        _copyButton.Content = "✓ " + Loc.Get("add.share.copied");
    }

    // ------------------------------------------------------------------ 2. paste

    private void ScheduleParse()
    {
        _parseTimer.Stop();
        _parseTimer.Start();
    }

    private async Task ParseAsync()
    {
        string text = _pasteBox.Text.Trim();
        int version = ++_parseVersion;
        _pasteError.Visibility = Visibility.Collapsed;
        if (text.Length == 0)
        {
            _parsed = null;
            UpdateButtons();
            return;
        }

        try
        {
            CardParsed parsed = await _session.Engine.CardParseAsync(text);
            if (version != _parseVersion) return;
            _parsed = parsed;
            _summaryText.Text = parsed.HasAddress
                ? Loc.Format("add.paste.summary", ("short", parsed.ShortAddress))
                : Loc.Format("add.paste.summaryNoAddress", ("short", parsed.ShortAddress));
            _pasteBox.Visibility = Visibility.Collapsed;
            _summaryButton.Visibility = Visibility.Visible;
            ShowIgnoredRoutes(parsed);
            _namePanel.Visibility = Visibility.Visible;
            _nameBox.Focus(FocusState.Programmatic);
        }
        catch (RavenException ex)
        {
            if (version != _parseVersion) return;
            _parsed = null;
            ShowIgnoredRoutes(null);
            _pasteError.Text = ex.Message;
            _pasteError.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Ui.ShowError(_errorBar, ex);
        }
        UpdateButtons();
    }

    /// <summary>The line about the card's routes the engine left out (none: hidden).</summary>
    private void ShowIgnoredRoutes(CardParsed? parsed)
    {
        string? line = parsed is null ? null : CardPresenter.IgnoredRoutesLine(parsed);
        _ignoredText.Text = line ?? string.Empty;
        _ignoredText.Visibility = line is null ? Visibility.Collapsed : Visibility.Visible;
        _ignoredDetails.Children.Clear();
        if (line is not null && App.Current?.Prefs.ShowTechnical == true
            && CardPresenter.EngineNote(parsed!) is string note
            && Ui.TechnicalDetails(new[] { note }) is Expander details)
        {
            _ignoredDetails.Children.Add(details);
        }
        _ignoredDetails.Visibility = _ignoredDetails.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ExpandPaste()
    {
        _parsed = null;
        ShowIgnoredRoutes(null);
        _summaryButton.Visibility = Visibility.Collapsed;
        _pasteBox.Visibility = Visibility.Visible;
        _namePanel.Visibility = Visibility.Collapsed;
        _pasteBox.Focus(FocusState.Programmatic);
        UpdateButtons();
    }

    private async Task AddAsync(ContentDialogButtonClickDeferral deferral)
    {
        try
        {
            string name = TextSanitizer.SingleLine(_nameBox.Text);
            Contact contact = await _session.Engine.ContactsAddAsync(_pasteBox.Text.Trim(), name);
            AddedContact = contact;
            _mustAddBack.Text = Loc.Format("add.mustAddBack", ("name", ContactNameOr(contact, name)));
            _errorBar.IsOpen = false;
            ShowStep(Step.Added);
        }
        catch (RavenException ex) when (ex.Code == RavenErrorCodes.NameClash)
        {
            _nameError.Text = ex.Message;
            _nameError.Visibility = Visibility.Visible;
        }
        catch (RavenException ex) when (ex.Code == RavenErrorCodes.CardInvalid)
        {
            ExpandPaste();
            _pasteError.Text = ex.Message;
            _pasteError.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Ui.ShowError(_errorBar, ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static string ContactNameOr(Contact contact, string fallback)
    {
        string name = TextSanitizer.SingleLine(contact.Name);
        return name.Length > 0 ? name : fallback;
    }
}
