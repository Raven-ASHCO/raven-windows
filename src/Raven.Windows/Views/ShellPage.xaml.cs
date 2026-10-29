using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Dialogs;
using Raven.Windows.Helpers;
using Raven.Windows.Services;

namespace Raven.Windows.Views;

/// <summary>
/// The main window's content: the Calm sidebar (brand and heartbeat, search, You, friends,
/// groups, Add a friend; Network, Security words, Security, Settings) and the chat, group chat,
/// network or settings page. It routes engine events to the rows and the open chat, and owns the
/// 1-second clock for relative times.
/// </summary>
public sealed partial class ShellPage : UserControl
{
    private readonly RavenSession _session;
    private readonly Dictionary<string, FriendRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GroupRow> _groupRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _messageOwners = new(StringComparer.Ordinal);
    private readonly HashSet<string> _waitingIds = new(StringComparer.Ordinal);
    private readonly DispatcherQueueTimer _clock;
    private readonly Avatar _youAvatar = new(36, withDot: false);
    private ChatView? _chat;
    private GroupChatView? _groupChat;
    private NetworkPage? _network;
    private NetworkCheckPage? _check;
    private SecurityPage? _security;
    private SettingsPage? _settings;
    private Me? _me;
    private string? _selectedAddress;
    private string? _selectedGroup;
    private bool _groupsAvailable;
    private bool _refreshQueued;
    private bool _groupsRefreshQueued;
    private bool _selecting;
    private bool _active = true;

    public ShellPage(RavenSession session)
    {
        _session = session;
        InitializeComponent();

        YouAvatarHost.Content = _youAvatar.Root;
        _youAvatar.SetYou(Loc.Get("you.title"));
        FriendsHeaderText.Text = Loc.Get("home.friends").ToUpperInvariant();
        GroupsHeaderText.Text = Loc.Get("home.groups").ToUpperInvariant();

        _clock = DispatcherQueue.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.IsRepeating = true;
        _clock.Tick += (_, _) => OnClock();

        _session.EventReceived += OnEvent;
        _session.NodeChanged += OnNodeChanged;
        _session.SettingsChanged += OnSettingsChanged;
        _session.Resynced += OnResynced;
        Unloaded += (_, _) =>
        {
            _clock.Stop();
            _session.EventReceived -= OnEvent;
            _session.NodeChanged -= OnNodeChanged;
            _session.SettingsChanged -= OnSettingsChanged;
            _session.Resynced -= OnResynced;
        };
    }

    public async Task LoadAsync()
    {
        try
        {
            _me = await _session.Engine.IdentityMeAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        UpdateYou();
        UpdateLabBar();
        UpdateBrand(RavenSession.NowMs);

        await RefreshContactsAsync();
        await RefreshGroupsAsync();
        await LearnWaitingMessagesAsync();

        FriendRow? first = _rows.Values.FirstOrDefault();
        if (first is not null) Select(first.Address);
        else ShowEmptyState();

        _clock.Start();
    }

    /// <summary>Window shown or hidden: the clock and continuous animations follow.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (active) _clock.Start();
        else _clock.Stop();
        foreach (FriendRow row in _rows.Values) row.SetActive(active);
        _chat?.SetActive(active);
        _groupChat?.SetActive(active);
        if (active)
        {
            OnClock();
            ReadOpenChat();
        }
    }

    public void ShowError(Exception ex) => Ui.ShowError(ShellError, ex);

    /// <summary>Settings &gt; Privacy &gt; Erase removed Raven's data (privacy.erase): the window starts over at Welcome.</summary>
    public event Action? Erased;

    /// <summary>Settings &gt; Lock (or Security) locked Raven (Lock v2): the window shows the Locked screen.</summary>
    public event Action<LockState>? Locked;

    /// <summary>Settings &gt; Appearance changed the theme: the window follows.</summary>
    public event Action<ThemeChoice>? ThemeChosen;

    /// <summary>The friends in the sidebar's order with their unread counts (the tray menu lists them).</summary>
    public IReadOnlyList<(Contact Contact, int Unread)> TrayFriends() =>
        _rows.Values
            .OrderBy(r => Nav.MenuItems.IndexOf(r.Item))
            .Select(r => (r.Contact, r.Unread))
            .ToList();

    /// <summary>Shows one friend's chat (a friend's line in the tray menu).</summary>
    public void SelectFriend(string address) => Select(address);

    /// <summary>Remembers which friend a message belongs to (receipts carry only the id).</summary>
    public void RegisterMessage(Message message)
    {
        if (string.IsNullOrEmpty(message.Id)) return;
        _messageOwners[message.Id] = message.Address;
        if (message.State == MessageStates.Waiting) _waitingIds.Add(message.Id);
    }

    /// <summary>
    /// What the window shows now, so Unlock can come back to it (E1: Unlock opened a chat, not
    /// the page you were on): "network", "check", "security", "settings", "chat:" + address or
    /// "group:" + id.
    /// </summary>
    public string? CurrentPage => Body.Content switch
    {
        NetworkCheckPage => "check",
        NetworkPage => "network",
        SecurityPage => "security",
        SettingsPage => "settings",
        ChatView chat when chat.Address is string address => "chat:" + address,
        GroupChatView group when group.GroupId is string id => GroupRow.TagPrefix + id,
        _ => null,
    };

    /// <summary>Shows the page <see cref="CurrentPage"/> named (nothing when it is gone).</summary>
    public void RestorePage(string? page)
    {
        switch (page)
        {
            case null:
                return;
            case "network":
                Nav.SelectedItem = NetworkItem;
                return;
            case "check":
                Nav.SelectedItem = NetworkItem;
                ShowNetworkCheck();
                return;
            case "security":
                Nav.SelectedItem = SecurityItem;
                return;
            case "settings":
                Nav.SelectedItem = SettingsItem;
                return;
        }
        if (page.StartsWith("chat:", StringComparison.Ordinal)) Select(page["chat:".Length..]);
        else if (page.StartsWith(GroupRow.TagPrefix, StringComparison.Ordinal)) SelectGroup(page[GroupRow.TagPrefix.Length..]);
    }

    // ------------------------------------------------------------------ dialogs

    /// <summary>Rename a friend (contacts.rename): the name only this computer shows.</summary>
    public async Task OpenRenameAsync(Contact contact)
    {
        if (XamlRoot is null) return;
        var dialog = new RenameDialog(_session, contact);
        await Ui.ShowDialogAsync(dialog, XamlRoot);
        if (dialog.Renamed) await RefreshContactsAsync();
    }

    /// <summary>A friend's right-click menu: rename, check words, routes (E1; the terminal app's friend page has them).</summary>
    private MenuFlyout FriendMenu(string address)
    {
        Contact? Current() => _rows.TryGetValue(address, out FriendRow? r) ? r.Contact : null;
        var menu = new MenuFlyout();
        var rename = new MenuFlyoutItem { Text = Loc.Get("contact.rename") };
        rename.Click += (_, _) => { if (Current() is Contact c) _ = OpenRenameAsync(c); };
        var words = new MenuFlyoutItem { Text = Loc.Get("home.banner.notVerified.action") };
        words.Click += (_, _) => { if (Current() is Contact c) _ = OpenCheckWordsAsync(c); };
        var routes = new MenuFlyoutItem { Text = Loc.Get("network.routes.button") };
        routes.Click += (_, _) => { if (Current() is Contact c) _ = OpenRoutesAsync(c); };
        menu.Opening += (_, _) =>
        {
            if (Current() is Contact c) routes.Text = Loc.Format("network.routes.title", ("name", ContactPresenter.DisplayName(c)));
        };
        menu.Items.Add(rename);
        menu.Items.Add(words);
        menu.Items.Add(routes);
        return menu;
    }

    public async Task OpenAddFriendAsync()
    {
        if (XamlRoot is null) return;
        var dialog = new AddFriendDialog(_session);
        await Ui.ShowDialogAsync(dialog, XamlRoot);
        if (dialog.AddedContact is not Contact added) return;

        await RefreshContactsAsync();
        Select(added.Address);
        if (dialog.CheckWordsNext && _rows.TryGetValue(added.Address, out FriendRow? row))
            await OpenCheckWordsAsync(row.Contact);
    }

    public async Task OpenCheckWordsAsync(Contact contact)
    {
        if (XamlRoot is null) return;
        if (!await EnsureMeAsync()) return;
        var dialog = new CheckWordsDialog(_session, contact, _me!.Words, _me.OldCode);
        await Ui.ShowDialogAsync(dialog, XamlRoot);
        await RefreshContactsAsync();
    }

    /// <summary>You: your name, fingerprint, security words and card.</summary>
    public async Task OpenYouAsync()
    {
        if (XamlRoot is null) return;
        try
        {
            _me = await _session.Engine.IdentityMeAsync();
            UpdateYou();
        }
        catch (Exception ex)
        {
            if (_me is null)
            {
                ShowError(ex);
                return;
            }
        }
        await Ui.ShowDialogAsync(new YouDialog(_me!, _session.Settings.DisplayName), XamlRoot);
    }

    /// <summary>Security words (sidebar, and Settings &gt; About): your ten words, read aloud one by one.</summary>
    public async Task OpenSecurityWordsAsync()
    {
        if (XamlRoot is null || !await EnsureMeAsync()) return;
        await Ui.ShowDialogAsync(new SecurityWordsDialog(_me!.Words, _me.OldCode), XamlRoot);
    }

    private async Task<bool> EnsureMeAsync()
    {
        if (_me is not null) return true;
        try
        {
            _me = await _session.Engine.IdentityMeAsync();
            UpdateYou();
            return true;
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return false;
        }
    }

    /// <summary>The sidebar's You entry: "You · Sam" and "Fingerprint KLHQ-uLWW-5iB0".</summary>
    private void UpdateYou()
    {
        YouPresenter.Lines you = YouPresenter.For(_me, _session.Settings.DisplayName);
        YouTitle.Text = you.Title;
        YouFingerprint.Text = you.Fingerprint ?? string.Empty;
        YouFingerprint.Visibility = you.Fingerprint is null ? Visibility.Collapsed : Visibility.Visible;
        _youAvatar.SetYou(TextSanitizer.SingleLine(_me?.DisplayName ?? _session.Settings.DisplayName ?? Loc.Get("you.title")));
        string name = you.Fingerprint is null ? you.Title : you.Title + ", " + you.Fingerprint;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(YouItem, name);
        ToolTipService.SetToolTip(YouItem, name);
    }

    /// <summary>The brand row: the heartbeat's words and dot, and how many ways are on beside Network.</summary>
    private void UpdateBrand(long nowMs)
    {
        NodeStatus? node = _session.Node;
        (string text, bool ok) = NavPresenter.Heartbeat(node, _session.NodeDown, _session.LastOkMs, nowMs);
        bool starting = ok && node?.IsStarting == true;
        HeartbeatText.Text = text;
        ToolTipService.SetToolTip(HeartGroup, text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HeartGroup, text);
        HeartDot.Style = Ui.Style(ok ? "RavenHeartDotStyle" : "RavenHeartDownDotStyle");
        HeartDot.Visibility = starting ? Visibility.Collapsed : Visibility.Visible;
        NodeSpinner.IsActive = starting && Motion.Enabled;
        NodeSpinner.Visibility = starting && Motion.Enabled ? Visibility.Visible : Visibility.Collapsed;
        string? ways = NavPresenter.WaysOn(node);
        WaysOnText.Text = ways ?? string.Empty;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(NetworkItem, ways is null ? Loc.Get("nav.network") : Loc.Get("nav.network") + ", " + ways);
    }

    /// <summary>The one-line Lab banner: a developer build whose engine sees lab switches, on every page but Network.</summary>
    private void UpdateLabBar()
    {
        List<string> switches = _session.Node?.LabSwitches ?? new List<string>();
        bool show = switches.Count > 0 && Body.Content is not NetworkPage;
        LabBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        string list = string.Join(", ", switches);
        LabBarText.Text = Loc.Format("network.lab.banner", ("switches", list));
        ToolTipService.SetToolTip(LabBarText, list);
    }

    private void OnLabBarNetwork(object sender, RoutedEventArgs e)
    {
        _selecting = true;
        try
        {
            Nav.SelectedItem = NetworkItem;
        }
        finally
        {
            _selecting = false;
        }
        ClearSelection();
        ShowNetwork();
    }

    /// <summary>A friend's routes, route hints and who carries their mail (contacts.routes).</summary>
    public async Task OpenRoutesAsync(Contact contact)
    {
        if (XamlRoot is null) return;
        var dialog = new ContactRoutesDialog(_session, contact);
        await Ui.ShowDialogAsync(dialog, XamlRoot);
        if (dialog.RoutesChanged) await RefreshContactsAsync();
    }

    public async Task OpenMessageDetailsAsync(Message message)
    {
        if (XamlRoot is null) return;
        string name = _rows.TryGetValue(message.Address, out FriendRow? row) ? row.DisplayName : message.Address;
        try
        {
            MessageDetails details = await _session.Engine.MessagesDetailsAsync(message.Id);
            var dialog = new MessageDetailsDialog(_session, details, name);
            await Ui.ShowDialogAsync(dialog, XamlRoot);
            if (dialog.UpdatedMessage is Message updated)
            {
                // Send again of an ended message comes back as a new message (messages.retry
                // "replaces"): it takes the old bubble's place, so the text never shows twice.
                RegisterMessage(updated);
                _chat?.ReplaceMessage(message.Id, updated);
            }
            QueueContactsRefresh();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>New group (sidebar): a name and friends; you become its admin (groups.create).</summary>
    public async Task OpenNewGroupAsync()
    {
        if (XamlRoot is null || !_groupsAvailable) return;
        var dialog = new GroupCreateDialog(_session, _rows.Values.Select(r => r.Contact).ToList());
        await Ui.ShowDialogAsync(dialog, XamlRoot);
        if (dialog.Created is not GroupView made) return;
        await RefreshGroupsAsync();
        SelectGroup(made.Group.Id);
    }

    /// <summary>A group's members (its header): add and remove for the admin, leave for the others.</summary>
    public async Task OpenGroupMembersAsync(string groupId)
    {
        if (XamlRoot is null) return;
        var dialog = new GroupMembersDialog(_session, groupId, _rows.Values.Select(r => r.Contact).ToList());
        await Ui.ShowDialogAsync(dialog, XamlRoot);
        if (dialog.Changed)
        {
            await RefreshGroupsAsync();
            if (_groupChat is not null && _selectedGroup == groupId && _groupRows.TryGetValue(groupId, out GroupRow? row))
                await _groupChat.ShowGroupAsync(row.Group, ContactsByAddress());
        }
    }

    // ------------------------------------------------------------------ navigation

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_selecting) return;
        if (args.SelectedItemContainer is not NavigationViewItem item || item.Tag is not string tag) return;
        switch (tag)
        {
            case "network":
                ClearSelection();
                ShowNetwork();
                break;
            case "security":
                ClearSelection();
                _security ??= new SecurityPage(_session, () => Nav.SelectedItem = SettingsItem);
                Show(_security);
                _ = _security.RefreshAsync();
                break;
            case "settings":
                ClearSelection();
                _settings ??= new SettingsPage(_session, () => Erased?.Invoke(), () => Nav.SelectedItem = NetworkItem,
                    state => Locked?.Invoke(state), theme => ThemeChosen?.Invoke(theme), () => _ = OpenSecurityWordsAsync());
                Show(_settings);
                _ = _settings.RefreshAsync();
                break;
            default:
                if (tag.StartsWith(GroupRow.TagPrefix, StringComparison.Ordinal)) _ = ShowGroupAsync(tag[GroupRow.TagPrefix.Length..]);
                else _ = ShowChatAsync(tag);
                break;
        }
        UpdateLabBar();
    }

    /// <summary>A page or chat the person opened comes in (Calm viewIn), once per change of page.</summary>
    private void Show(UIElement page)
    {
        bool changed = !ReferenceEquals(Body.Content, page);
        Body.Content = page;
        if (changed) Motion.ViewIn(Body, BodyShift);
    }

    private void ClearSelection()
    {
        _selectedAddress = null;
        _selectedGroup = null;
    }

    private void ShowNetwork()
    {
        _network ??= new NetworkPage(_session, ShowNetworkCheck);
        Show(_network);
        UpdateLabBar();
        _ = _network.RefreshAsync();
    }

    /// <summary>Check network (from the Network page); its Back link returns there.</summary>
    private void ShowNetworkCheck()
    {
        _check ??= new NetworkCheckPage(_session, ShowNetwork);
        Show(_check);
        UpdateLabBar();
        _ = _check.ShowAsync();
    }

    private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer == AddFriendItem) _ = OpenAddFriendAsync();
        else if (args.InvokedItemContainer == YouItem) _ = OpenYouAsync();
        else if (args.InvokedItemContainer == WordsItem) _ = OpenSecurityWordsAsync();
        else if (args.InvokedItemContainer == NewGroupItem) _ = OpenNewGroupAsync();
        // Network is still selected while Check network shows: invoking it goes back.
        else if (args.InvokedItemContainer == NetworkItem && Body.Content is NetworkCheckPage) ShowNetwork();
    }

    /// <summary>A narrow window keeps the chat room: the sidebar takes tokens' sidebarMinWidth below 760.</summary>
    private void OnNavSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double wanted = e.NewSize.Width < 760 ? (double)Ui.Resource("RavenSidebarMinWidth")! : (double)Ui.Resource("RavenSidebarWidth")!;
        if (Math.Abs(Nav.OpenPaneLength - wanted) > 0.5) Nav.OpenPaneLength = wanted;
    }

    private void Select(string address)
    {
        if (!_rows.TryGetValue(address, out FriendRow? row)) return;
        _selecting = true;
        try
        {
            Nav.SelectedItem = row.Item;
        }
        finally
        {
            _selecting = false;
        }
        _ = ShowChatAsync(address);
    }

    private void SelectGroup(string id)
    {
        if (!_groupRows.TryGetValue(id, out GroupRow? row)) return;
        _selecting = true;
        try
        {
            Nav.SelectedItem = row.Item;
        }
        finally
        {
            _selecting = false;
        }
        _ = ShowGroupAsync(id);
    }

    private async Task ShowChatAsync(string address)
    {
        if (!_rows.TryGetValue(address, out FriendRow? row)) return;
        _selectedAddress = address;
        _selectedGroup = null;
        _chat ??= new ChatView(_session, this);
        Show(_chat);
        UpdateLabBar();
        await _chat.ShowContactAsync(row.Contact);
        ReadOpenChat();
    }

    private async Task ShowGroupAsync(string id)
    {
        if (!_groupRows.TryGetValue(id, out GroupRow? row)) return;
        _selectedGroup = id;
        _selectedAddress = null;
        _groupChat ??= new GroupChatView(_session, this);
        Show(_groupChat);
        UpdateLabBar();
        await _groupChat.ShowGroupAsync(row.Group, ContactsByAddress());
        ReadOpenChat();
    }

    /// <summary>
    /// The chat view is the page shown now: the same object, said as such (CS0252: the page was
    /// compared with the chat view through the object's equality operator).
    /// </summary>
    private bool ShowsChat => _chat is not null && Body.Content is ChatView shown && ReferenceEquals(shown, _chat);

    private bool ShowsGroup => _groupChat is not null && Body.Content is GroupChatView shown && ReferenceEquals(shown, _groupChat);

    /// <summary>
    /// The open chat is on screen (the window shows it): it counts as read up to its newest
    /// message, here and in the terminal app (messages.mark_read, groups.mark_read). The badge
    /// goes at once; a mark that fails leaves the engine's count, which the next refresh shows again.
    /// </summary>
    private void ReadOpenChat()
    {
        if (!_active) return;
        if (_groupChat is not null && ShowsGroup && _selectedGroup is string groupId
            && _groupChat.GroupId == groupId && _groupRows.TryGetValue(groupId, out GroupRow? groupRow) && groupRow.Group.Unread > 0)
        {
            groupRow.ClearUnread();
            _ = MarkGroupReadAsync(groupId);
            return;
        }
        if (_chat is null || !ShowsChat || _selectedAddress is not string address) return;
        if (_chat.Address != address || !_rows.TryGetValue(address, out FriendRow? row) || row.Unread == 0) return;
        if (_chat.NewestId is not string newest) return;
        row.SetUnread(0);
        _ = MarkReadAsync(address, newest);
    }

    private async Task MarkReadAsync(string address, string upTo)
    {
        try
        {
            await _session.Engine.MessagesMarkReadAsync(address, upTo);
        }
        catch (RavenException)
        {
            // The engine's count stays; the next contacts refresh shows it.
        }
    }

    private async Task MarkGroupReadAsync(string id)
    {
        try
        {
            await _session.Engine.GroupsMarkReadAsync(id);
        }
        catch (RavenException)
        {
            // The engine's count stays; the next groups refresh shows it.
        }
    }

    private void ShowEmptyState()
    {
        var panel = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(Ui.Text(Loc.Get("home.noFriends"), "RavenSecondaryTextStyle"));
        var add = new Button
        {
            Content = Loc.Get("home.noFriends.action"),
            Style = Ui.Style("AccentButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        add.Click += (_, _) => _ = OpenAddFriendAsync();
        panel.Children.Add(add);
        Body.Content = panel;
    }

    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ApplySearch();
    }

    private void ApplySearch()
    {
        string query = SearchBox.Text?.Trim() ?? string.Empty;
        foreach (FriendRow row in _rows.Values)
        {
            bool match = query.Length == 0 || row.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase);
            row.Item.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
        }
        foreach (GroupRow row in _groupRows.Values)
        {
            bool match = query.Length == 0 || row.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase);
            row.Item.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ------------------------------------------------------------------ contacts

    private IReadOnlyDictionary<string, Contact> ContactsByAddress()
        => _rows.Values.ToDictionary(r => r.Address, r => r.Contact, StringComparer.Ordinal);

    private async Task RefreshContactsAsync()
    {
        IReadOnlyList<Contact> contacts;
        try
        {
            contacts = await _session.Engine.ContactsListAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return;
        }

        long now = RavenSession.NowMs;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int insertAt = Nav.MenuItems.IndexOf(FriendsHeader) + 1;
        foreach (Contact contact in contacts)
        {
            seen.Add(contact.Address);
            if (_rows.TryGetValue(contact.Address, out FriendRow? row))
            {
                row.Update(contact, now, animate: false);
            }
            else
            {
                row = new FriendRow(contact, now);
                row.Item.ContextFlyout = FriendMenu(contact.Address);
                row.SetActive(_active);
                _rows[contact.Address] = row;
            }
            // Counted by the engine: read here or in the terminal app, and kept across restarts.
            row.SetUnread(contact.Unread);

            // Keep the engine's order in the pane.
            int current = Nav.MenuItems.IndexOf(row.Item);
            if (current != insertAt)
            {
                _selecting = true;
                try
                {
                    if (current >= 0) Nav.MenuItems.RemoveAt(current);
                    Nav.MenuItems.Insert(insertAt, row.Item);
                }
                finally
                {
                    _selecting = false;
                }
            }
            insertAt++;

            if (contact.Address == _selectedAddress) _chat?.UpdateContact(contact);
        }

        foreach (string gone in _rows.Keys.Where(a => !seen.Contains(a)).ToList())
        {
            Nav.MenuItems.Remove(_rows[gone].Item);
            _rows.Remove(gone);
        }

        ApplySearch();
        if (_rows.Count == 0 && Body.Content is not (NetworkPage or NetworkCheckPage or SecurityPage or SettingsPage or GroupChatView)) ShowEmptyState();
        UpdateGroupPresence();
        _groupChat?.OnContactsChanged(ContactsByAddress());
        ReadOpenChat();
    }

    /// <summary>
    /// groups.list: the GROUPS section shows only when the engine has groups (available: a lab
    /// build; a release build and an older engine show none).
    /// </summary>
    private async Task RefreshGroupsAsync()
    {
        GroupsList list;
        try
        {
            list = await _session.Engine.GroupsListAsync();
        }
        catch (RavenException)
        {
            list = new GroupsList(); // an engine without groups
        }
        _groupsAvailable = list.Available;
        Visibility section = list.Available ? Visibility.Visible : Visibility.Collapsed;
        GroupsHeader.Visibility = section;
        NewGroupItem.Visibility = section;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int insertAt = Nav.MenuItems.IndexOf(GroupsHeader) + 1;
        foreach (Group group in list.Available ? list.Groups : new List<Group>())
        {
            seen.Add(group.Id);
            bool known = _groupRows.TryGetValue(group.Id, out GroupRow? row);
            if (!known)
            {
                row = new GroupRow(group);
                _groupRows[group.Id] = row;
            }
            bool needMembers = row!.Members is null || row.Members.Count != group.Members;
            row.Update(group, null, null);
            if (needMembers) _ = LoadGroupMembersAsync(group.Id);

            int current = Nav.MenuItems.IndexOf(row.Item);
            if (current != insertAt)
            {
                _selecting = true;
                try
                {
                    if (current >= 0) Nav.MenuItems.RemoveAt(current);
                    Nav.MenuItems.Insert(insertAt, row.Item);
                }
                finally
                {
                    _selecting = false;
                }
            }
            insertAt++;
            if (group.Id == _selectedGroup) _groupChat?.UpdateGroup(group);
        }
        foreach (string gone in _groupRows.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            Nav.MenuItems.Remove(_groupRows[gone].Item);
            _groupRows.Remove(gone);
        }
        UpdateGroupPresence();
        ApplySearch();
        ReadOpenChat();
    }

    private async Task LoadGroupMembersAsync(string id)
    {
        try
        {
            GroupView view = await _session.Engine.GroupsGetAsync(id);
            if (!_groupRows.TryGetValue(id, out GroupRow? row)) return;
            row.Update(view.Group, view.Members, GroupPresenter.InTouch(view.Members, ContactsByAddress()));
        }
        catch (RavenException)
        {
            // The row keeps its name and count; the next change tries again.
        }
    }

    /// <summary>"3 in touch now" follows the friends' presence (members who are your friends).</summary>
    private void UpdateGroupPresence()
    {
        IReadOnlyDictionary<string, Contact> contacts = ContactsByAddress();
        foreach (GroupRow row in _groupRows.Values)
        {
            if (row.Members is IReadOnlyList<GroupMember> members)
                row.Update(row.Group, null, GroupPresenter.InTouch(members, contacts));
        }
    }

    /// <summary>
    /// The engine dropped events (events.poll answered reset): read the node status, the friends,
    /// the groups and the open chat's history again, so nothing that happened meanwhile is missing
    /// (API.md, "Events").
    /// </summary>
    private void OnResynced() => _ = ReloadAfterResetAsync();

    private async Task ReloadAfterResetAsync()
    {
        await _session.RefreshNodeAsync();
        await RefreshContactsAsync();
        await RefreshGroupsAsync();
        if (_chat is not null && ShowsChat && _selectedAddress is not null
            && _rows.TryGetValue(_selectedAddress, out FriendRow? row))
        {
            await _chat.ShowContactAsync(row.Contact);
        }
        if (_groupChat is not null && ShowsGroup && _selectedGroup is not null
            && _groupRows.TryGetValue(_selectedGroup, out GroupRow? groupRow))
        {
            await _groupChat.ShowGroupAsync(groupRow.Group, ContactsByAddress());
        }
    }

    /// <summary>Learns the ids of waiting messages so a later receipt can say "{name} is back".</summary>
    private async Task LearnWaitingMessagesAsync()
    {
        foreach (FriendRow row in _rows.Values.Where(r => r.Contact.Waiting is { Count: > 0 }).ToList())
        {
            try
            {
                foreach (Message m in await _session.Engine.MessagesHistoryAsync(row.Address, null, 100))
                    RegisterMessage(m);
            }
            catch (RavenException)
            {
                // Without it the ticker says "Delivered to ..." instead of "... is back".
            }
        }
    }

    private void QueueContactsRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        DispatcherQueue.TryEnqueue(async () =>
        {
            _refreshQueued = false;
            await RefreshContactsAsync();
        });
    }

    private void QueueGroupsRefresh()
    {
        if (_groupsRefreshQueued) return;
        _groupsRefreshQueued = true;
        DispatcherQueue.TryEnqueue(async () =>
        {
            _groupsRefreshQueued = false;
            await RefreshGroupsAsync();
        });
    }

    // ------------------------------------------------------------------ engine events

    /// <summary>A toast on whichever chat is open (friend or group).</summary>
    private void Toast(string text, Tone tone)
    {
        if (ShowsGroup) _groupChat!.ShowToast(text, tone);
        else _chat?.ShowToast(text, tone);
    }

    private void OnEvent(RavenEvent e)
    {
        long now = RavenSession.NowMs;
        bool fresh = now - e.AtMs < MotionTokens.StaleEventMs;

        switch (e.Kind)
        {
            case EventKinds.StatusOk:
                if (fresh) Motion.Heartbeat(HeartScale);
                UpdateBrand(now);
                // The Network page's radar answers the same real status check.
                if (fresh && Body.Content is NetworkPage pulsing) pulsing.Pulse();
                break;

            case EventKinds.SendStarted:
                _chat?.OnSendStarted(e.Id, fresh);
                break;

            case EventKinds.Receipt:
                OnReceipt(e, fresh);
                QueueContactsRefresh();
                break;

            case EventKinds.Incoming when e.Message is not null:
                OnIncoming(e.Message, fresh);
                QueueContactsRefresh();
                break;

            case EventKinds.Outgoing when e.Message is not null:
                // Yours, sent by another program on this profile (the terminal app): shown at once (E1).
                RegisterMessage(e.Message);
                if (e.Message.Address == _selectedAddress && _chat is not null && ShowsChat)
                    _chat.OnOutgoing(e.Message);
                QueueContactsRefresh();
                break;

            case EventKinds.Retry:
                if (e.Address is not null && _rows.TryGetValue(e.Address, out FriendRow? retried))
                {
                    if (fresh) retried.ShowRetry();
                    if (e.Address == _selectedAddress) _chat?.ShowRetry(fresh);
                    Toast(TickerPresenter.Retry(retried.DisplayName, retried.Contact.Waiting?.UntilMs, RavenSession.NowMs), Tone.Warning);
                }
                QueueContactsRefresh();
                break;

            case EventKinds.Presence:
                if (e.Address is not null && e.Presence is not null && _rows.TryGetValue(e.Address, out FriendRow? changed))
                    changed.SetPresence(e.Presence switch
                    {
                        Presences.InTouch => Presences.InTouch,
                        Presences.Waiting => Presences.Waiting,
                        _ => Presences.None,
                    }, animate: fresh);
                QueueContactsRefresh();
                break;

            case EventKinds.State:
                if (e.Id is not null && e.State == MessageStates.Waiting) _waitingIds.Add(e.Id);
                _chat?.OnState(e.Id, e.State, e.UntilMs);
                QueueContactsRefresh();
                break;

            case EventKinds.Unread:
                // Counted by the engine: a message came, or the chat was read here or in the terminal app.
                if (e.Address is not null && e.Unread is int count && _rows.TryGetValue(e.Address, out FriendRow? counted))
                {
                    counted.SetUnread(count);
                    ReadOpenChat();
                }
                break;

            case EventKinds.Node:
                _chat?.OnNode();
                UpdateBrand(now);
                if (Body.Content is NetworkPage network) _ = network.RefreshAsync();
                if (Body.Content is SecurityPage security) _ = security.RefreshAsync();
                break;

            case EventKinds.GroupMessage when e.Group is not null:
                OnGroupMessage(e.Group, e.GroupMessage, fresh);
                QueueGroupsRefresh();
                break;

            case EventKinds.GroupChanged when e.Group is not null:
                // Members, status, unread or a receipt changed: the row, and the open group's chat.
                if (_groupChat is not null && ShowsGroup && _selectedGroup == e.Group) _ = _groupChat.ReloadAsync(fresh);
                if (_groupRows.TryGetValue(e.Group, out GroupRow? changedGroup) && changedGroup.Members is not null)
                    _ = LoadGroupMembersAsync(e.Group);
                QueueGroupsRefresh();
                break;
        }
    }

    private void OnReceipt(RavenEvent e, bool fresh)
    {
        if (e.Id is null) return;
        _chat?.OnReceipt(e, fresh);

        if (!_messageOwners.TryGetValue(e.Id, out string? address) || !_rows.TryGetValue(address, out FriendRow? row)) return;
        bool wasWaiting = _waitingIds.Remove(e.Id);
        string text = wasWaiting
            ? TickerPresenter.Back(row.DisplayName)
            : TickerPresenter.Delivered(row.DisplayName, e.Path, e.Ms);
        Toast(text, Tone.Success);
    }

    private void OnIncoming(Message message, bool fresh)
    {
        RegisterMessage(message);
        if (!_rows.TryGetValue(message.Address, out FriendRow? row)) return;

        // The badge is the engine's (the unread event that follows, and the contacts refresh).
        if (message.Address == _selectedAddress && _chat is not null && ShowsChat)
            _chat.OnIncoming(message, fresh);
        Toast(TickerPresenter.Incoming(row.DisplayName, message.Path), Tone.Accent);
    }

    private void OnGroupMessage(string groupId, GroupMessage? message, bool fresh)
    {
        string groupName = _groupRows.TryGetValue(groupId, out GroupRow? row) ? row.DisplayName : groupId;
        if (_groupChat is not null && ShowsGroup && _selectedGroup == groupId)
        {
            _ = _groupChat.ReloadAsync(fresh);
        }
        if (message is not null && !message.Mine)
            Toast(Loc.Format("group.incoming", ("name", TextSanitizer.SingleLine(message.FromName)), ("group", groupName)), Tone.Accent);
    }

    private void OnNodeChanged()
    {
        UpdateLabBar();
        UpdateBrand(RavenSession.NowMs);
        _chat?.OnNode();
    }

    private void OnSettingsChanged()
    {
        // Reduced motion may have changed: restart or stop the continuous animations.
        foreach (FriendRow row in _rows.Values) row.SetActive(_clock.IsRunning);
        _chat?.OnMotionChanged();
        UpdateYou();
    }

    private void OnClock()
    {
        long now = RavenSession.NowMs;
        foreach (FriendRow row in _rows.Values) row.RefreshStatus(now);
        UpdateBrand(now);
        _chat?.Tick(now);
        _groupChat?.Tick(now);
    }
}
