using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;
using Raven.Windows.Services;

namespace Raven.Windows.Dialogs;

/// <summary>
/// New group (groups.create): a name and up to 15 of your friends; you become its admin (only you
/// add and remove members, and you can't leave it). Nothing is sent before Create group.
/// </summary>
internal sealed class GroupCreateDialog : ContentDialog
{
    private readonly RavenSession _session;
    private readonly TextBox _name;
    private readonly List<(CheckBox Box, Contact Contact)> _picks = new();
    private readonly TextBlock _full;
    private readonly InfoBar _error;

    public GroupCreateDialog(RavenSession session, IReadOnlyList<Contact> friends)
    {
        _session = session;
        Title = Loc.Get("group.create.title");
        _name = new TextBox { MaxLength = GroupLimits.MaxNameChars, Header = Loc.Get("group.create.name") };
        AutomationProperties.SetName(_name, Loc.Get("group.create.name"));
        _full = Ui.Text(Loc.Get("group.full"), "RavenCaptionWarningTextStyle");
        _full.Visibility = Visibility.Collapsed;
        _error = new InfoBar { IsClosable = false, IsOpen = false };
        Ui.CollapseWhenClosed(_error);

        var list = new StackPanel { Spacing = 2 };
        foreach (Contact c in GroupPresenter.Addable(friends, Array.Empty<GroupMember>()))
        {
            var box = new CheckBox { Content = ContactPresenter.DisplayName(c) };
            box.Checked += (_, _) => Validate();
            box.Unchecked += (_, _) => Validate();
            _picks.Add((box, c));
            list.Children.Add(box);
        }
        var panel = new StackPanel { Spacing = 10, MinWidth = 340, MaxWidth = 460 };
        panel.Children.Add(Ui.Text(Loc.Get("group.create.note"), "RavenCaptionTextStyle"));
        panel.Children.Add(_name);
        if (_picks.Count == 0)
        {
            panel.Children.Add(Ui.Text(Loc.Get("group.create.noFriends"), "RavenCaptionWarningTextStyle"));
        }
        else
        {
            TextBlock pick = Ui.Text(Loc.Get("group.create.pick"), "RavenLabelTextStyle");
            panel.Children.Add(pick);
            panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            AutomationProperties.SetName(list, Loc.Get("group.create.pick"));
        }
        panel.Children.Add(_full);
        panel.Children.Add(_error);
        Content = panel;
        PrimaryButtonText = Loc.Get("group.create.button");
        CloseButtonText = Loc.Get("common.cancel");
        DefaultButton = ContentDialogButton.Primary;
        IsPrimaryButtonEnabled = false;
        _name.TextChanged += (_, _) => Validate();
        PrimaryButtonClick += async (_, args) =>
        {
            ContentDialogButtonClickDeferral deferral = args.GetDeferral();
            try
            {
                args.Cancel = !await CreateAsync();
            }
            finally
            {
                deferral.Complete();
            }
        };
    }

    /// <summary>The new group, once created.</summary>
    public GroupView? Created { get; private set; }

    private List<string> Chosen() => _picks.Where(p => p.Box.IsChecked == true).Select(p => p.Contact.Address).ToList();

    private void Validate()
    {
        int chosen = Chosen().Count;
        bool tooMany = chosen + 1 > GroupLimits.MaxMembers;
        _full.Visibility = tooMany ? Visibility.Visible : Visibility.Collapsed;
        IsPrimaryButtonEnabled = chosen > 0 && !tooMany && TextSanitizer.SingleLine(_name.Text).Length > 0;
    }

    private async Task<bool> CreateAsync()
    {
        try
        {
            Created = await _session.Engine.GroupsCreateAsync(TextSanitizer.SingleLine(_name.Text), Chosen());
            return true;
        }
        catch (Exception ex)
        {
            Ui.ShowError(_error, ex);
            return false;
        }
    }
}

/// <summary>
/// A group's members (groups.get): you, the admin, members who are not your friends ("not your
/// friend": the group never adds them to your friends). The admin adds friends (groups.add) and
/// removes members (groups.remove); any other member can leave (groups.leave, after a "Leave
/// {name}?" question); the admin can't leave. Joining is automatic.
/// </summary>
internal sealed class GroupMembersDialog : ContentDialog
{
    private readonly RavenSession _session;
    private readonly string _groupId;
    private readonly IReadOnlyList<Contact> _friends;
    private readonly StackPanel _panel = new() { Spacing = 10, MinWidth = 360, MaxWidth = 480 };
    private readonly StackPanel _rows = new() { Spacing = 6 };
    private readonly StackPanel _addPanel = new() { Spacing = 2, Visibility = Visibility.Collapsed };
    private readonly TextBlock _note;
    private readonly InfoBar _error;
    private GroupView? _view;

    public GroupMembersDialog(RavenSession session, string groupId, IReadOnlyList<Contact> friends)
    {
        _session = session;
        _groupId = groupId;
        _friends = friends;
        _note = Ui.Text(string.Empty, "RavenCaptionTextStyle");
        _error = new InfoBar { IsClosable = true, IsOpen = false };
        Ui.CollapseWhenClosed(_error);
        _panel.Children.Add(_note);
        _panel.Children.Add(new ScrollViewer { Content = _rows, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _panel.Children.Add(_addPanel);
        _panel.Children.Add(_error);
        Content = _panel;
        Title = Loc.Get("group.membersButton");
        CloseButtonText = Loc.Get("common.close");
        DefaultButton = ContentDialogButton.Close;
        Loaded += (_, _) => _ = LoadAsync();
        PrimaryButtonClick += async (_, args) =>
        {
            ContentDialogButtonClickDeferral deferral = args.GetDeferral();
            try
            {
                args.Cancel = !await PrimaryAsync();
            }
            finally
            {
                deferral.Complete();
            }
        };
    }

    /// <summary>The group's members or status changed here.</summary>
    public bool Changed { get; private set; }

    private bool _leaving;

    private async Task LoadAsync()
    {
        try
        {
            Render(await _session.Engine.GroupsGetAsync(_groupId));
        }
        catch (Exception ex)
        {
            Ui.ShowError(_error, ex);
        }
    }

    private void Render(GroupView view)
    {
        _view = view;
        Group group = view.Group;
        string name = GroupPresenter.DisplayName(group);
        Title = Loc.Format("group.membersTitle", ("name", name));
        _note.Text = !group.IsActive
            ? GroupPresenter.StatusLine(group, null)
            : group.Admin
                ? Loc.Get("group.youAdmin")
                : Loc.Format("group.adminNote", ("name", TextSanitizer.SingleLine(group.AdminName ?? string.Empty)));
        _rows.Children.Clear();
        foreach (GroupMember m in view.Members.OrderByDescending(m => m.Me).ThenByDescending(m => m.Admin).ThenBy(m => GroupPresenter.MemberName(m), StringComparer.CurrentCultureIgnoreCase))
            _rows.Children.Add(Row(group, m));

        // Add friends (the admin, while there is room): your friends who aren't members yet.
        _addPanel.Children.Clear();
        _addPanel.Visibility = Visibility.Collapsed;
        if (GroupPresenter.CanManage(group))
        {
            IReadOnlyList<Contact> addable = GroupPresenter.Addable(_friends, view.Members);
            _addPanel.Children.Add(Ui.Text(Loc.Get("group.add"), "RavenLabelTextStyle"));
            if (addable.Count == 0)
            {
                _addPanel.Children.Add(Ui.Text(Loc.Get("group.addNone"), "RavenCaptionTextStyle"));
            }
            else if (GroupPresenter.Room(view.Members.Count) == 0)
            {
                _addPanel.Children.Add(Ui.Text(Loc.Get("group.full"), "RavenCaptionWarningTextStyle"));
            }
            else
            {
                foreach (Contact c in addable)
                {
                    var box = new CheckBox { Content = ContactPresenter.DisplayName(c), Tag = c.Address };
                    box.Checked += (_, _) => UpdatePrimary();
                    box.Unchecked += (_, _) => UpdatePrimary();
                    _addPanel.Children.Add(box);
                }
            }
            _addPanel.Visibility = Visibility.Visible;
        }
        UpdatePrimary();
    }

    private UIElement Row(Group group, GroupMember member)
    {
        var avatar = new Avatar(22, withDot: false);
        string name = GroupPresenter.MemberName(member);
        avatar.Set(member.Address, name);
        var tags = new List<string>();
        if (member.Admin) tags.Add(Loc.Get("group.admin"));
        if (!member.Me && !member.Friend) tags.Add(Loc.Get("group.notFriend"));
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Ui.Text(name, "RavenLabelTextStyle"));
        if (tags.Count > 0) texts.Children.Add(Ui.Text(string.Join(" · ", tags), "RavenMetaTextStyle"));
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(texts, 1);
        row.Children.Add(avatar.Root);
        row.Children.Add(texts);
        if (GroupPresenter.CanManage(group) && !member.Me)
        {
            var remove = new Button { Content = Loc.Get("group.remove") };
            AutomationProperties.SetName(remove, Loc.Get("group.remove") + " " + name);
            remove.Click += (_, _) => _ = RemoveAsync(member.Address);
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
        }
        AutomationProperties.SetName(row, tags.Count == 0 ? name : name + ", " + string.Join(", ", tags));
        return row;
    }

    /// <summary>The admin's Add (friends ticked), or a member's Leave group; the admin never gets Leave.</summary>
    private void UpdatePrimary()
    {
        Group? group = _view?.Group;
        if (group is not null && GroupPresenter.CanManage(group))
        {
            int ticked = _addPanel.Children.OfType<CheckBox>().Count(b => b.IsChecked == true);
            PrimaryButtonText = Loc.Get("group.addButton");
            IsPrimaryButtonEnabled = ticked > 0 && ticked <= GroupPresenter.Room(_view!.Members.Count);
            return;
        }
        if (group is not null && GroupPresenter.CanLeave(group))
        {
            PrimaryButtonText = Loc.Get("group.leave");
            IsPrimaryButtonEnabled = true;
            return;
        }
        PrimaryButtonText = string.Empty; // no primary button
    }

    private async Task<bool> PrimaryAsync()
    {
        if (_view is null) return false;
        Group group = _view.Group;
        try
        {
            if (GroupPresenter.CanManage(group))
            {
                List<string> adding = _addPanel.Children.OfType<CheckBox>().Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToList();
                if (adding.Count == 0) return false;
                Render(await _session.Engine.GroupsAddAsync(_groupId, adding));
                Changed = true;
                return false; // stay open: the list shows the new members
            }
            if (GroupPresenter.CanLeave(group))
            {
                if (!_leaving)
                {
                    // Ask once in place: what leaving means, then the same button leaves.
                    _leaving = true;
                    _note.Text = Loc.Format("group.leave.title", ("name", GroupPresenter.DisplayName(group))) + " " + Loc.Get("group.leave.body");
                    _note.Style = Ui.Style("RavenCaptionWarningTextStyle");
                    return false;
                }
                await _session.Engine.GroupsLeaveAsync(_groupId);
                Changed = true;
                return true;
            }
        }
        catch (Exception ex)
        {
            Ui.ShowError(_error, ex);
        }
        return false;
    }

    private async Task RemoveAsync(string member)
    {
        try
        {
            Render(await _session.Engine.GroupsRemoveAsync(_groupId, member));
            Changed = true;
        }
        catch (Exception ex)
        {
            Ui.ShowError(_error, ex);
        }
    }
}

/// <summary>
/// Your security words (sidebar, Settings &gt; About): the ten words in tiles, word 10 marked as
/// the check word, Read aloud stepping through them, the old code for older Ravens.
/// </summary>
internal sealed class SecurityWordsDialog : ContentDialog
{
    public SecurityWordsDialog(IReadOnlyList<string> words, string oldCode)
    {
        Title = Loc.Get("words.title");
        var grid = new WordGrid(words, readAloud: true, startStepping: false);
        var panel = new StackPanel { Spacing = 12, MinWidth = 520, MaxWidth = 640 };
        panel.Children.Add(Ui.Text(Loc.Get("words.body"), "RavenCaptionTextStyle"));
        panel.Children.Add(grid);
        var fine = new StackPanel { Spacing = 4 };
        fine.Children.Add(Ui.Text(Loc.Get("words.check"), "RavenCaptionTextStyle"));
        fine.Children.Add(Ui.Text(Loc.Get("words.never"), "RavenCaptionTextStyle"));
        string code = Raven.Windows.Core.Words.WordEntry.GroupOldCode(oldCode);
        if (code.Length > 0) fine.Children.Add(Ui.Text(Loc.Format("words.oldCode", ("code", code)), "RavenMonoTextStyle"));
        panel.Children.Add(fine);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        CloseButtonText = Loc.Get("common.done");
        DefaultButton = ContentDialogButton.Close;
    }
}
