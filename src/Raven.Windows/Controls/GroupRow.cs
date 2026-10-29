using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;

namespace Raven.Windows.Controls;

/// <summary>
/// One group in the sidebar (Calm): up to three members' avatars stacked, the group's name, "5
/// members · 3 in touch now" (or that you left or were removed) and the unread badge. Its tag is
/// "group:" + the group's id.
/// </summary>
public sealed class GroupRow
{
    public const string TagPrefix = "group:";

    private readonly Grid _stack = new() { Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _name;
    private readonly TextBlock _status;
    private readonly InfoBadge _badge;
    private string _stackKey = string.Empty;

    public GroupRow(Group group)
    {
        Id = group.Id;
        _name = Ui.Text(string.Empty, "RavenLabelTextStyle");
        _status = Ui.Text(string.Empty, "RavenCaptionLineTextStyle");
        _status.FontSize = 12;
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        texts.Children.Add(_name);
        texts.Children.Add(_status);
        _badge = new InfoBadge { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(_stack, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);

        var root = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 6, 4, 6) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(_badge, 2);
        root.Children.Add(_stack);
        root.Children.Add(texts);
        root.Children.Add(_badge);

        Item = new NavigationViewItem { Content = root, Tag = TagPrefix + group.Id };
        Update(group, null, null);
    }

    public string Id { get; }

    public NavigationViewItem Item { get; }

    public Group Group { get; private set; } = new();

    /// <summary>The members as groups.get last said (null until then).</summary>
    public IReadOnlyList<GroupMember>? Members { get; private set; }

    public string DisplayName => GroupPresenter.DisplayName(Group);

    /// <param name="members">groups.get's members, when known (the stacked avatars).</param>
    /// <param name="inTouch">Members who are your friends and in touch now, when known.</param>
    public void Update(Group group, IReadOnlyList<GroupMember>? members, int? inTouch)
    {
        Group = group;
        if (members is not null) Members = members;
        _name.Text = DisplayName;
        _status.Text = GroupPresenter.StatusLine(group, inTouch);
        _badge.Value = group.Unread;
        _badge.Visibility = group.Unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderStack();
        string name = DisplayName + ", " + _status.Text;
        if (group.Unread > 0) name += ", " + Loc.Format("group.newIn", ("n", group.Unread), ("name", DisplayName));
        AutomationProperties.SetName(Item, name);
    }

    /// <summary>The badge goes at once when the group is read here (groups.mark_read).</summary>
    public void ClearUnread()
    {
        Group.Unread = 0;
        Update(Group, null, null);
    }

    private void RenderStack()
    {
        List<GroupMember> shown = (Members ?? new List<GroupMember>()).Where(m => !m.Me).Take(3).ToList();
        string key = string.Join("|", shown.Select(m => m.Address + m.Name)) + "|" + Group.Name;
        if (key == _stackKey) return;
        _stackKey = key;
        _stack.Children.Clear();
        if (shown.Count == 0)
        {
            var single = new Avatar(36, withDot: false);
            single.Set(Group.Id, DisplayName);
            _stack.Children.Add(single.Root);
            return;
        }
        // Three small avatars in a triangle (Calm "stack"), each with a ring in the sidebar's colour.
        (double X, double Y)[] spots = { (0, 1), (15, 1), (7.5, 15) };
        for (int i = 0; i < shown.Count; i++)
        {
            var avatar = new Avatar(21, withDot: false);
            avatar.Set(shown[i].Address, GroupPresenter.MemberName(shown[i]));
            var ring = new Border
            {
                Style = Ui.Style("RavenAvatarRingStyle"),
                CornerRadius = new CornerRadius(12.5),
                Child = avatar.Root,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(spots[i].X - 2, spots[i].Y - 2, 0, 0),
            };
            _stack.Children.Add(ring);
        }
    }
}
