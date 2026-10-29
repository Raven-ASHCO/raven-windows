using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Raven.Windows.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;
using Raven.Windows.Services;
using CoreVirtualKeyStates = Windows.UI.Core.CoreVirtualKeyStates;
using VirtualKey = Windows.System.VirtualKey;
using WinPoint = Windows.Foundation.Point;

namespace Raven.Windows.Views;

/// <summary>
/// A group chat (API.md, "Groups"), Calm: the header (members' avatars, the name, "5 members · a
/// sealed copy for each", Members), the route line ("4 sealed copies, one for each member"), the
/// members' messages with their names (in their avatar colour) and "Passed on by Ali" when another
/// member passed one on, and yours with the ring that fills as members confirm and "Delivered to
/// 3 of 5"; Details shows each member's copy. After you left or were removed the history stays and
/// the composer says so. Only what the engine reports: the receipts are the engine's.
/// </summary>
public sealed class GroupChatView : UserControl
{
    private readonly RavenSession _session;
    private readonly ShellPage _shell;
    private readonly Dictionary<string, GroupBubble> _bubbles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _open = new(StringComparer.Ordinal);
    private readonly Grid _avatars = new() { Height = 26, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _name;
    private readonly TextBlock _subtitle;
    private readonly Button _membersButton;
    private readonly TextBlock _routeLabel;
    private readonly TextBlock _routeEnd;
    private readonly Canvas _packets = new() { Height = 14, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private readonly StackPanel _messages;
    private readonly ScrollViewer _scroller;
    private readonly TextBlock _empty;
    private readonly InfoBar _error = new() { IsClosable = true, IsOpen = false };
    private readonly Border _composerPill;
    private readonly TextBox _composer;
    private readonly Button _send;
    private readonly TextBlock _ended;
    private readonly TextBlock _e2e;
    private readonly Border _toast;
    private readonly TextBlock _toastText;
    private readonly TranslateTransform _toastShift = new();
    private readonly Button _newPill;
    private readonly DispatcherQueueTimer _toastTimer;
    private IReadOnlyDictionary<string, Contact> _contacts = new Dictionary<string, Contact>();
    private List<GroupMember> _members = new();
    private Group? _group;
    private long? _lastAtMs;
    private int _loadVersion;
    private bool _stickToEnd = true;
    private double _bubbleMaxWidth = 420;
    private int _toastVersion;

    public GroupChatView(RavenSession session, ShellPage shell)
    {
        _session = session;
        _shell = shell;

        // ---- header
        _name = Ui.Text(string.Empty, "RavenHeaderNameTextStyle");
        _name.MaxWidth = 320;
        _subtitle = Ui.Text(string.Empty, "RavenCaptionLineTextStyle");
        _subtitle.FontSize = 12.5;
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, MinWidth = 0 };
        titles.Children.Add(_name);
        titles.Children.Add(_subtitle);
        _membersButton = new Button { VerticalAlignment = VerticalAlignment.Center };
        var membersContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        membersContent.Children.Add(new FontIcon { Glyph = "\uE716", FontSize = 13 });
        membersContent.Children.Add(new TextBlock { Text = Loc.Get("group.membersButton") });
        _membersButton.Content = membersContent;
        _membersButton.Click += (_, _) => { if (_group is not null) _ = _shell.OpenGroupMembersAsync(_group.Id); };
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(titles, 1);
        Grid.SetColumn(_membersButton, 2);
        header.Children.Add(_avatars);
        header.Children.Add(titles);
        header.Children.Add(_membersButton);
        var headerBar = new Border { Style = Ui.Style("RavenHeaderBarStyle"), Child = header };
        AutomationProperties.SetAccessibilityView(_avatars, AccessibilityView.Raw);

        // ---- route line: You, the copies, the members
        _routeLabel = Ui.Text(string.Empty, "RavenMetaTextStyle");
        _routeLabel.FontWeight = FontWeights.SemiBold;
        var labelBorder = new Border { Style = Ui.Style("RavenRouteLabelStyle"), Child = _routeLabel };
        var track = new Grid();
        track.Children.Add(new Rectangle { Style = Ui.Style("RavenTrackLineStyle") });
        track.Children.Add(labelBorder);
        track.Children.Add(_packets);
        track.SizeChanged += (_, e) => _packets.Width = e.NewSize.Width;
        _routeEnd = Ui.Text(string.Empty, "RavenMetaTextStyle");
        var route = new Grid { ColumnSpacing = 10 };
        route.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        route.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        route.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock you = Ui.Text(Loc.Get("you.title"), "RavenMetaTextStyle");
        Grid.SetColumn(track, 1);
        Grid.SetColumn(_routeEnd, 2);
        route.Children.Add(you);
        route.Children.Add(track);
        route.Children.Add(_routeEnd);
        var routeBar = new Border { Style = Ui.Style("RavenRouteBarStyle"), Child = route };
        AutomationProperties.SetAccessibilityView(routeBar, AccessibilityView.Raw);

        // ---- messages: one Tab stop for the whole chat (E1), arrow keys move inside
        _messages = new StackPanel
        {
            MaxWidth = (double)Ui.Resource("RavenContentMaxWidth")!,
            Padding = new Thickness(28, 18, 28, 22),
            Spacing = 4,
            TabFocusNavigation = KeyboardNavigationMode.Once,
            XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled,
        };
        _messages.SizeChanged += (_, _) =>
        {
            if (_stickToEnd) _scroller!.ChangeView(null, _scroller.ScrollableHeight, null, disableAnimation: true);
        };
        _scroller = new ScrollViewer
        {
            Content = _messages,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
        };
        _scroller.ViewChanged += (_, e) =>
        {
            if (e.IsIntermediate) return;
            _stickToEnd = _scroller.VerticalOffset >= _scroller.ScrollableHeight - 24;
            if (_stickToEnd) _newPill!.Visibility = Visibility.Collapsed;
        };
        _scroller.SizeChanged += (_, e) =>
        {
            double content = Math.Min(e.NewSize.Width, (double)Ui.Resource("RavenContentMaxWidth")!) - 56;
            _bubbleMaxWidth = Math.Max(160, content * (double)Ui.Resource("RavenBubbleMaxFraction")!);
            foreach (GroupBubble b in _bubbles.Values) b.Bubble.MaxWidth = _bubbleMaxWidth;
            if (_stickToEnd) DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ScrollToEnd);
        };
        _empty = Ui.Text(string.Empty, "RavenSecondaryTextStyle");
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;

        // ---- the toast and the "new messages" pill over the bottom of the messages
        _toastText = Ui.Text(string.Empty, "RavenMetaTextStyle");
        _toastText.FontSize = 12;
        _toastText.MaxWidth = 640;
        AutomationProperties.SetLiveSetting(_toastText, AutomationLiveSetting.Polite);
        _toast = new Border { Style = Ui.Style("RavenToastStyle"), Child = _toastText, Opacity = 0, Visibility = Visibility.Collapsed, RenderTransform = _toastShift };
        var pillContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        pillContent.Children.Add(new FontIcon { Glyph = "\uE74B", FontSize = 11 });
        pillContent.Children.Add(new TextBlock { Text = Loc.Get("chat.newMessages"), FontSize = 12 });
        _newPill = new Button { Content = pillContent, Padding = new Thickness(12, 4, 12, 4), CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
        _newPill.Click += (_, _) => ScrollToEnd();
        var overlay = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) };
        overlay.Children.Add(_newPill);
        overlay.Children.Add(_toast);
        _toastTimer = DispatcherQueue.CreateTimer();
        _toastTimer.Interval = TimeSpan.FromMilliseconds(MotionTokens.ToastVisible);
        _toastTimer.IsRepeating = false;
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            int version = _toastVersion;
            Motion.ToastOut(_toast, () =>
            {
                if (version == _toastVersion) _toast.Visibility = Visibility.Collapsed;
                else _toast.Opacity = 1;
            });
        };

        // ---- composer
        _composer = new TextBox
        {
            MinHeight = 32,
            MaxHeight = 120,
            Padding = new Thickness(0, 6, 0, 6),
            VerticalAlignment = VerticalAlignment.Center,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
        };
        foreach (string key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                     "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
            _composer.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _composer.PreviewKeyDown += OnComposerKeyDown;
        _composer.GotFocus += (_, _) => _composerPill!.Style = Ui.Style("RavenComposerFocusedStyle");
        _composer.LostFocus += (_, _) => _composerPill!.Style = Ui.Style("RavenComposerStyle");
        _send = new Button
        {
            Style = Ui.Style("AccentButtonStyle"),
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(17),
            VerticalAlignment = VerticalAlignment.Bottom,
            Content = new FontIcon { Glyph = "\uE74A", FontSize = 14 },
        };
        AutomationProperties.SetName(_send, Loc.Get("home.send"));
        ToolTipService.SetToolTip(_send, Loc.Get("home.send"));
        _send.Click += (_, _) => _ = SendAsync();
        var composerGrid = new Grid { ColumnSpacing = 10 };
        composerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        composerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_send, 1);
        composerGrid.Children.Add(_composer);
        composerGrid.Children.Add(_send);
        _composerPill = new Border { Style = Ui.Style("RavenComposerStyle"), Child = composerGrid, MaxWidth = (double)Ui.Resource("RavenContentMaxWidth")! };
        _ended = Ui.Text(string.Empty, "RavenCaptionTextStyle");
        _ended.HorizontalAlignment = HorizontalAlignment.Center;
        _ended.TextAlignment = TextAlignment.Center;
        _ended.Visibility = Visibility.Collapsed;
        _e2e = Ui.Text(Loc.Get("chat.e2e.group"), "RavenMetaTertiaryTextStyle");
        _e2e.FontSize = 11;
        var e2eRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        var lockIcon = new FontIcon { Glyph = "\uE72E", Style = Ui.Style("RavenTertiaryIconStyle") };
        AutomationProperties.SetAccessibilityView(lockIcon, AccessibilityView.Raw);
        e2eRow.Children.Add(lockIcon);
        e2eRow.Children.Add(_e2e);
        var bottom = new StackPanel { Padding = new Thickness(22, 8, 22, 14), Spacing = 6 };
        _error.MaxWidth = (double)Ui.Resource("RavenContentMaxWidth")!;
        Ui.CollapseWhenClosed(_error);
        bottom.Children.Add(_error);
        bottom.Children.Add(_composerPill);
        bottom.Children.Add(_ended);
        bottom.Children.Add(e2eRow);

        // ---- the page
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(routeBar, 1);
        Grid.SetRow(_scroller, 2);
        Grid.SetRow(_empty, 2);
        Grid.SetRow(overlay, 2);
        Grid.SetRow(bottom, 3);
        root.Children.Add(headerBar);
        root.Children.Add(routeBar);
        root.Children.Add(_scroller);
        root.Children.Add(_empty);
        root.Children.Add(overlay);
        root.Children.Add(bottom);
        Content = new Border { Style = Ui.Style("RavenPageStyle"), Child = root };
        IsTabStop = false;
    }

    /// <summary>The open group's id.</summary>
    public string? GroupId => _group?.Id;

    // ------------------------------------------------------------------ loading

    public async Task ShowGroupAsync(Group group, IReadOnlyDictionary<string, Contact> contacts)
    {
        int version = ++_loadVersion;
        bool same = _group?.Id == group.Id;
        _group = group;
        _contacts = contacts;
        if (!same)
        {
            _messages.Children.Clear();
            _bubbles.Clear();
            _open.Clear();
            _lastAtMs = null;
            _members = new List<GroupMember>();
            _composer.Text = string.Empty;
            _error.IsOpen = false;
            _newPill.Visibility = Visibility.Collapsed;
        }
        UpdateGroup(group);
        _stickToEnd = true;
        try
        {
            GroupView view = await _session.Engine.GroupsGetAsync(group.Id);
            if (version != _loadVersion) return;
            _members = view.Members;
            UpdateGroup(view.Group);
            IReadOnlyList<GroupMessage> history = await _session.Engine.GroupsHistoryAsync(group.Id, null, 100);
            if (version != _loadVersion) return;
            foreach (GroupMessage m in history.OrderBy(m => m.AtMs)) AddOrUpdate(m, receiptsChanged: false);
        }
        catch (Exception ex)
        {
            Ui.ShowError(_error, ex);
        }
        UpdateEmpty();
        ScrollToEndSoon();
        if (group.IsActive) _composer.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// group_changed or group_message for the open group: the members and the messages again
    /// (new ones rise in; the rings of yours fill as members confirm).
    /// </summary>
    public async Task ReloadAsync(bool fresh)
    {
        if (_group is null) return;
        string id = _group.Id;
        try
        {
            GroupView view = await _session.Engine.GroupsGetAsync(id);
            if (_group?.Id != id) return;
            _members = view.Members;
            UpdateGroup(view.Group);
            IReadOnlyList<GroupMessage> history = await _session.Engine.GroupsHistoryAsync(id, null, 100);
            if (_group?.Id != id) return;
            foreach (GroupMessage m in history.OrderBy(m => m.AtMs))
            {
                bool isNew = !_bubbles.ContainsKey(m.Id);
                GroupBubble bubble = AddOrUpdate(m, receiptsChanged: fresh);
                if (isNew && !m.Mine)
                {
                    if (fresh) Motion.BubbleIn(bubble.Root, bubble.Rise);
                    if (_stickToEnd) ScrollToEnd();
                    else _newPill.Visibility = Visibility.Visible;
                }
            }
            UpdateEmpty();
        }
        catch (RavenException ex)
        {
            Ui.ShowError(_error, ex);
        }
    }

    /// <summary>The group's name, status and whether you can still write to it.</summary>
    public void UpdateGroup(Group group)
    {
        if (_group is not null && _group.Id != group.Id) return;
        _group = group;
        string name = GroupPresenter.DisplayName(group);
        _name.Text = name;
        _subtitle.Text = GroupPresenter.HeaderLine(group);
        _routeLabel.Text = GroupPresenter.RouteLine(group);
        _routeEnd.Text = Loc.Format("group.members", ("n", group.Members));
        _composer.PlaceholderText = GroupPresenter.Composer(group);
        AutomationProperties.SetName(_composer, GroupPresenter.Composer(group));
        _empty.Text = Loc.Format("home.emptyChat", ("name", name));
        bool active = group.IsActive;
        _composerPill.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        _ended.Text = active ? string.Empty : GroupPresenter.StatusLine(group, null);
        _ended.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(_membersButton, Loc.Format("group.membersTitle", ("name", name)));
        RenderAvatars();
    }

    /// <summary>The friends' names and presence changed (contacts.list).</summary>
    public void OnContactsChanged(IReadOnlyDictionary<string, Contact> contacts) => _contacts = contacts;

    public void SetActive(bool active)
    {
        if (!active) _packets.Children.Clear();
    }

    /// <summary>The clock: the times under the bubbles stay as they are; nothing to count down here.</summary>
    public void Tick(long nowMs)
    {
    }

    /// <summary>Shows one toast above the composer for 5 s.</summary>
    public void ShowToast(string text, Tone tone)
    {
        _toastText.Text = text;
        _toastText.Style = Ui.Style(TickView.MetaStyle(tone));
        _toastText.FontSize = 12;
        _toastVersion++;
        bool wasHidden = _toast.Visibility != Visibility.Visible;
        _toast.Visibility = Visibility.Visible;
        if (wasHidden) Motion.ToastIn(_toast, _toastShift);
        else _toast.Opacity = 1;
        try
        {
            AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(_toastText) ?? FrameworkElementAutomationPeer.CreatePeerForElement(_toastText);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        catch (Exception)
        {
            // Narrator still reads it when it gets there.
        }
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void RenderAvatars()
    {
        _avatars.Children.Clear();
        List<GroupMember> shown = _members.Where(m => !m.Me).Take(5).ToList();
        for (int i = 0; i < shown.Count; i++)
        {
            var avatar = new Avatar(22, withDot: false);
            avatar.Set(shown[i].Address, GroupPresenter.MemberName(shown[i]));
            var ring = new Border
            {
                Style = Ui.Style("RavenAvatarRingStyle"),
                CornerRadius = new CornerRadius(13),
                Child = avatar.Root,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(i * 16, 0, 0, 0),
            };
            _avatars.Children.Add(ring);
        }
        _avatars.Width = shown.Count == 0 ? 0 : 26 + (shown.Count - 1) * 16;
    }

    // ------------------------------------------------------------------ bubbles

    private GroupBubble AddOrUpdate(GroupMessage message, bool receiptsChanged)
    {
        if (_bubbles.TryGetValue(message.Id, out GroupBubble? existing))
        {
            existing.Update(message, _open.Contains(message.Id), receiptsChanged);
            return existing;
        }
        var bubble = new GroupBubble(message, _bubbleMaxWidth, id => ToggleDetails(id));
        bubble.Update(message, _open.Contains(message.Id), animate: false);
        _bubbles[message.Id] = bubble;
        if (_lastAtMs is not long last || RelativeTime.DifferentDays(last, message.AtMs))
            _messages.Children.Add(DayLine(message.AtMs));
        if (_lastAtMs is not long newest || message.AtMs >= newest) _lastAtMs = message.AtMs;
        _messages.Children.Add(bubble.Root);
        return bubble;
    }

    private void ToggleDetails(string id)
    {
        if (!_open.Remove(id)) _open.Add(id);
        if (_bubbles.TryGetValue(id, out GroupBubble? bubble)) bubble.Update(bubble.Message, _open.Contains(id), animate: false);
    }

    private static UIElement DayLine(long atMs)
    {
        TextBlock text = Ui.Text(RelativeTime.Day(atMs, RavenSession.NowMs), "RavenMetaTertiaryTextStyle");
        text.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(text, AutomationHeadingLevel.Level3);
        var line = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 14, 0, 8) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var right = new Rectangle { Style = Ui.Style("RavenDayLineStyle") };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(right, 2);
        line.Children.Add(new Rectangle { Style = Ui.Style("RavenDayLineStyle") });
        line.Children.Add(text);
        line.Children.Add(right);
        return line;
    }

    private void UpdateEmpty() => _empty.Visibility = _bubbles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void ScrollToEnd()
    {
        _stickToEnd = true;
        _newPill.Visibility = Visibility.Collapsed;
        _scroller.UpdateLayout();
        _scroller.ChangeView(null, _scroller.ScrollableHeight, null, disableAnimation: !Motion.Enabled);
    }

    private void ScrollToEndSoon()
    {
        ScrollToEnd();
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ScrollToEnd);
    }

    // ------------------------------------------------------------------ composer

    private void OnComposerKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        CoreVirtualKeyStates shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if ((shift & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down) return; // Shift+Enter: new line
        e.Handled = true;
        _ = SendAsync();
    }

    private async Task SendAsync()
    {
        if (_group is null || !_group.IsActive) return;
        string text = _composer.Text.Trim();
        if (text.Length == 0) return;
        _send.IsEnabled = false;
        try
        {
            GroupSent sent = await _session.Engine.GroupsSendAsync(_group.Id, text);
            _composer.Text = string.Empty;
            _error.IsOpen = false;
            if (sent.Message.Group == _group.Id || string.IsNullOrEmpty(sent.Message.Group))
            {
                bool isNew = !_bubbles.ContainsKey(sent.Message.Id);
                GroupBubble bubble = AddOrUpdate(sent.Message, receiptsChanged: false);
                UpdateEmpty();
                ScrollToEnd();
                if (isNew) Lift(bubble);
                SendPacket();
            }
            // Why the copies couldn't be asked to go now (they wait): the engine's words.
            if (!string.IsNullOrWhiteSpace(sent.Note)) ShowToast(TextSanitizer.SingleLine(sent.Note), Tone.Warning);
        }
        catch (Exception ex)
        {
            Ui.ShowError(_error, ex);
        }
        finally
        {
            _send.IsEnabled = true;
            _composer.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>Your bubble lifts out of the composer to its place.</summary>
    private void Lift(GroupBubble bubble)
    {
        if (!Motion.Enabled) return;
        bubble.Bubble.Opacity = 0;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            bubble.Bubble.Opacity = 1;
            try
            {
                WinPoint from = _composer.TransformToVisual(bubble.Bubble).TransformPoint(new WinPoint(0, 0));
                Motion.Lift(bubble.Bubble, bubble.LiftTransform, from.X, from.Y);
            }
            catch (Exception)
            {
                // Not laid out: it simply shows in its place.
            }
        });
    }

    /// <summary>groups.send returned: the copies are on their way (one packet for the group line).</summary>
    private void SendPacket()
    {
        if (!Motion.Enabled) return;
        double width = Math.Max(0, _packets.ActualWidth - 10);
        var dot = new Ellipse { Style = Ui.Style("RavenPacketDotStyle") };
        var transform = new CompositeTransform();
        dot.RenderTransform = transform;
        dot.RenderTransformOrigin = new WinPoint(0.5, 0.5);
        Canvas.SetTop(dot, 2);
        _packets.Children.Add(dot);
        Motion.Packet(dot, transform, 0, width, MotionTokens.RoutePacket, () => _packets.Children.Remove(dot));
    }

    // ------------------------------------------------------------------ one bubble

    /// <summary>A group message: the author's name and avatar (members' messages), or the ring and receipts (yours).</summary>
    private sealed class GroupBubble
    {
        private readonly TextBlock _from;
        private readonly TextBlock _time;
        private readonly TextBlock _passedOn;
        private readonly GroupRing _ring = new();
        private readonly TickView _tick = new();
        private readonly TextBlock _note;
        private readonly HyperlinkButton _disclosure;
        private readonly Border _receipts;
        private readonly Grid _receiptGrid = new() { ColumnSpacing = 10, RowSpacing = 4 };

        public GroupBubble(GroupMessage message, double maxWidth, Action<string> toggle)
        {
            Message = message;
            bool mine = message.Mine;
            TextBlock body = Ui.Text(message.Text, mine ? "RavenBubbleOutTextStyle" : "RavenBubbleInTextStyle");
            Body = body;
            Bubble = new Border
            {
                Style = Ui.Style(mine ? "RavenBubbleOutStyle" : "RavenBubbleInStyle"),
                Child = body,
                MaxWidth = maxWidth,
                RenderTransform = LiftTransform,
                RenderTransformOrigin = new WinPoint(mine ? 1 : 0, 1),
            };
            _from = Ui.Text(string.Empty, $"RavenAvatarText{AvatarRule.IndexFor(message.FromAddress)}Style");
            _from.FontSize = 12;
            _from.Margin = new Thickness(30, 0, 0, 3);
            _time = Ui.Text(RelativeTime.BubbleTime(message.AtMs, RavenSession.NowMs), "RavenMetaTextStyle");
            _passedOn = Ui.Text(string.Empty, "RavenMetaTextStyle");
            _note = Ui.Text(string.Empty, "RavenMetaTextStyle");
            _disclosure = new HyperlinkButton { Padding = new Thickness(2, 0, 2, 0), MinHeight = 0, MinWidth = 0, FontSize = 11.5 };
            _disclosure.Click += (_, _) => toggle(Message.Id);
            _receipts = new Border { Style = Ui.Style("RavenCardStyle"), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(4, 8, 4, 2), Child = _receiptGrid };
            _receiptGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _receiptGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var meta = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(mine ? 4 : 34, 5, 4, 0),
                MinHeight = 20,
                HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };
            meta.Children.Add(_time);
            Root = new StackPanel
            {
                Margin = new Thickness(0, 3, 0, 3),
                HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                RenderTransform = Rise,
            };
            if (mine)
            {
                meta.Children.Add(_ring.Root);
                meta.Children.Add(_tick.Root);
                meta.Children.Add(_note);
                meta.Children.Add(_disclosure);
                Root.Children.Add(Bubble);
                Root.Children.Add(meta);
                Root.Children.Add(_receipts);
            }
            else
            {
                meta.Children.Add(_passedOn);
                var author = new Avatar(22, withDot: false);
                author.Set(message.FromAddress, TextSanitizer.SingleLine(message.FromName));
                var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                author.Root.VerticalAlignment = VerticalAlignment.Bottom;
                line.Children.Add(author.Root);
                line.Children.Add(Bubble);
                Root.Children.Add(_from);
                Root.Children.Add(line);
                Root.Children.Add(meta);
            }
        }

        public GroupMessage Message { get; private set; }
        public StackPanel Root { get; }
        public Border Bubble { get; }
        public TextBlock Body { get; }
        public TranslateTransform Rise { get; } = new();
        public CompositeTransform LiftTransform { get; } = new();

        /// <param name="open">The receipts list shows (Details).</param>
        /// <param name="animate">A receipt just came: the arc that turned green fades in.</param>
        public void Update(GroupMessage message, bool open, bool animate)
        {
            Message = message;
            Body.Text = message.Text;
            if (!message.Mine)
            {
                string from = TextSanitizer.SingleLine(message.FromName);
                _from.Text = from;
                string? via = GroupPresenter.PassedOnBy(message);
                _passedOn.Text = via ?? string.Empty;
                _passedOn.Visibility = via is null ? Visibility.Collapsed : Visibility.Visible;
                AutomationProperties.SetName(Root, from + ": " + message.Text + (via is null ? string.Empty : ", " + via));
                return;
            }
            _ring.Set(GroupPresenter.Segments(message), animate);
            _tick.SetText(GroupPresenter.Tick(message), strong: true);
            string note = GroupPresenter.Note(message);
            _note.Text = note.Length == 0 ? string.Empty : "· " + note;
            _note.Visibility = note.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            _disclosure.Content = Loc.Get(open ? "common.hide" : "message.details");
            _disclosure.Visibility = message.Receipts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(_disclosure, Loc.Get(open ? "common.hide" : "message.details") + ", " + GroupPresenter.AccessibleTick(message));
            AutomationProperties.SetName(Root, message.Text + ", " + GroupPresenter.AccessibleTick(message));
            _receipts.Visibility = open && message.Receipts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (open) RenderReceipts(message);
        }

        private void RenderReceipts(GroupMessage message)
        {
            _receiptGrid.Children.Clear();
            _receiptGrid.RowDefinitions.Clear();
            for (int i = 0; i < message.Receipts.Count; i++)
            {
                GroupReceipt r = message.Receipts[i];
                _receiptGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var who = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                var avatar = new Avatar(22, withDot: false);
                string name = TextSanitizer.SingleLine(r.Name);
                avatar.Set(r.Address, name);
                who.Children.Add(avatar.Root);
                TextBlock nameText = Ui.Text(name, "RavenMetaStrongTextStyle");
                nameText.FontSize = 12;
                who.Children.Add(nameText);
                var state = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                var tick = new TickView();
                TickIcon icon = r.State switch
                {
                    GroupReceiptStates.Delivered => TickIcon.Delivered,
                    GroupReceiptStates.Sent => TickIcon.Sent,
                    GroupReceiptStates.Failed => TickIcon.Failed,
                    _ => TickIcon.Waiting,
                };
                Tone tone = r.State switch
                {
                    GroupReceiptStates.Delivered => Tone.Success,
                    GroupReceiptStates.Failed => Tone.Danger,
                    GroupReceiptStates.Waiting => Tone.Warning,
                    _ => Tone.Secondary,
                };
                string text = GroupPresenter.ReceiptText(r);
                tick.Set(new TickPresenter.Tick(text, tone, icon, text), receipt: false);
                state.Children.Add(tick.Root);
                if (GroupPresenter.ViaMember(r) is string via)
                {
                    var chip = new PathChip();
                    chip.SetText(PathFamily.Friend, via, via);
                    state.Children.Add(chip.Root);
                }
                AutomationProperties.SetName(who, name + ": " + text + (GroupPresenter.ViaMember(r) is string v ? ", " + v : string.Empty));
                Grid.SetRow(who, i);
                Grid.SetRow(state, i);
                Grid.SetColumn(state, 1);
                _receiptGrid.Children.Add(who);
                _receiptGrid.Children.Add(state);
            }
        }
    }
}
