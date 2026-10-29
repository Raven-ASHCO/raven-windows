using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;

namespace Raven.Windows.Controls;

/// <summary>
/// One friend in the sidebar (Calm): avatar (colour from tokens' avatarRule) with the presence
/// dot, the name with "Not verified" beside it when so, the status line (amber while messages
/// wait) and the unread badge. Animations only on events: the dot blinks when presence changes, a
/// ring grows once on a retry, and an "in touch" dot breathes slowly.
/// </summary>
public sealed class FriendRow
{
    private readonly Avatar _avatar = new(36, withDot: true);
    private readonly TextBlock _name;
    private readonly TextBlock _unverified;
    private readonly TextBlock _status;
    private readonly InfoBadge _badge;

    public FriendRow(Contact contact, long nowMs)
    {
        Address = contact.Address;

        _name = Ui.Text(string.Empty, "RavenLabelTextStyle");
        _unverified = Ui.Text(Loc.Get("home.notVerified"), "RavenMetaWarningTextStyle");
        _unverified.FontSize = 10.5;
        _unverified.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        nameRow.Children.Add(_name);
        nameRow.Children.Add(_unverified);

        _status = Ui.Text(string.Empty, "RavenCaptionLineTextStyle");
        _status.FontSize = 12;
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        texts.Children.Add(nameRow);
        texts.Children.Add(_status);

        _badge = new InfoBadge { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };

        var root = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 6, 4, 6) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_avatar.Root, 0);
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(_badge, 2);
        root.Children.Add(_avatar.Root);
        root.Children.Add(texts);
        root.Children.Add(_badge);

        Item = new NavigationViewItem { Content = root, Tag = contact.Address };
        Update(contact, nowMs, animate: false);
    }

    public string Address { get; }

    public NavigationViewItem Item { get; }

    public Contact Contact { get; private set; } = new();

    public string DisplayName => ContactPresenter.DisplayName(Contact);

    public int Unread { get; private set; }

    /// <summary>Shows the latest contact data; <paramref name="animate"/> only for a presence event.</summary>
    public void Update(Contact contact, long nowMs, bool animate)
    {
        Contact = contact;
        _avatar.Set(contact.Address, DisplayName);
        _name.Text = DisplayName;
        _unverified.Visibility = contact.Verified ? Visibility.Collapsed : Visibility.Visible;
        RefreshStatus(nowMs);
        SetPresence(ContactPresenter.DotPresence(contact), animate);
    }

    /// <summary>Re-renders the time-dependent status line ("In touch 5 min ago").</summary>
    public void RefreshStatus(long nowMs)
    {
        string line = ContactPresenter.StatusLine(Contact, nowMs);
        // "Not verified" stands beside the name; the line says the rest (Calm sidebar).
        if (!Contact.Verified && line == Loc.Get("home.notVerified")) line = NotVerifiedLine(nowMs);
        _status.Text = line;
        _status.Style = Ui.Style(Contact.Waiting is { Count: > 0 } ? "RavenCaptionLineWarningTextStyle" : "RavenCaptionLineTextStyle");
        _status.FontSize = 12;
        UpdateAutomationName();
    }

    /// <summary>A friend not verified yet: the contact line the status would have had otherwise.</summary>
    private string NotVerifiedLine(long nowMs)
    {
        var verified = new Contact
        {
            Address = Contact.Address, Name = Contact.Name, ShortAddress = Contact.ShortAddress, Verified = true,
            Presence = Contact.Presence, LastContactMs = Contact.LastContactMs, Waiting = Contact.Waiting,
        };
        return ContactPresenter.StatusLine(verified, nowMs);
    }

    public void SetUnread(int count)
    {
        Unread = count;
        _badge.Value = count;
        _badge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateAutomationName();
    }

    /// <summary>presence event: the dot changes colour with a short blink.</summary>
    public void SetPresence(string presence, bool animate) => _avatar.SetPresence(presence, animate);

    /// <summary>retry event: a ring grows once around the dot.</summary>
    public void ShowRetry() => _avatar.ShowRetry();

    /// <summary>Stops or restarts the continuous breathing (window hidden, reduced motion).</summary>
    public void SetActive(bool active) => _avatar.SetActive(active);

    private void UpdateAutomationName()
    {
        string name = DisplayName + (Contact.Verified ? string.Empty : ", " + Loc.Get("home.notVerified")) + ", " + _status.Text;
        if (Unread > 0) name += ", " + Loc.Format("home.newFrom", ("n", Unread), ("name", DisplayName));
        AutomationProperties.SetName(Item, name);
    }
}
