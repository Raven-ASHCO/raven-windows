using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
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
/// The Calm chat with one friend: header (avatar, name, Verified, status and path, the path pill,
/// Routes), the route line, bubbles with the tick and path chip under them, day lines, the away
/// card while messages wait, the toast and the "new messages" pill, and the composer.
/// <para>
/// Honest motion: your bubble lifts out of the composer when you send; a packet travels out on
/// send_started; on receipt the tick says "Delivered" at once (in step with the toast), the check
/// pops and the path chip slides in, and the receipt travels back; on incoming a packet travels
/// in, then the bubble rises and keeps a soft ring for 1.5 s. Events that arrive late (older than
/// 3 s) and reduced motion change only the texts.
/// </para>
/// </summary>
public sealed partial class ChatView : UserControl
{
    private readonly RavenSession _session;
    private readonly ShellPage _shell;
    private readonly Dictionary<string, BubbleView> _bubbles = new(StringComparer.Ordinal);
    private readonly Queue<Action<Action>> _route = new();
    private readonly DispatcherQueueTimer _toastTimer;
    private readonly Avatar _headerAvatar = new(40, withDot: true);
    private readonly PathChip _pathPill = new(pill: true);
    private Contact? _contact;
    private bool _routeBusy;
    private double _bubbleMaxWidth = 420;
    private int _loadVersion;
    private AwayCard? _away;

    /// <summary>
    /// The chat follows the newest message while the person is at the bottom (E1: it opened on
    /// old messages, and a resize left the newest out of view); scrolling up stops that.
    /// </summary>
    private bool _stickToEnd = true;

    /// <summary>How the last message with this friend travelled (the header's pill and the route line show it when they may).</summary>
    private string? _lastPath;
    private string? _lastPathKind;

    /// <summary>The header has room for the path pill beside the name and status.</summary>
    private bool _pillFits = true;

    /// <summary>What the route line's label shows now (it is rebuilt only when this changes).</summary>
    private string? _routeKey;

    /// <summary>The window is shown (the header's dot breathes only then).</summary>
    private bool _active = true;

    /// <summary>The time of the last bubble added at the end: a day line goes before the next one of another day.</summary>
    private long? _lastAtMs;

    public ChatView(RavenSession session, ShellPage shell)
    {
        _session = session;
        _shell = shell;
        InitializeComponent();
        HeaderAvatarHost.Content = _headerAvatar.Root;
        PathPillHost.Content = _pathPill.Root;
        TrackDashed.StrokeDashArray = new DoubleCollection { 3, 2.5 };

        _toastTimer = DispatcherQueue.CreateTimer();
        _toastTimer.Interval = TimeSpan.FromMilliseconds(MotionTokens.ToastVisible);
        _toastTimer.IsRepeating = false;
        _toastTimer.Tick += (_, _) => HideToast();
    }

    public string? Address => _contact?.Address;

    /// <summary>
    /// The newest message of the open chat, shown or arriving (its packet still travelling in):
    /// the chat is read up to it (messages.mark_read), and what came later stays unread.
    /// </summary>
    public string? NewestId => _newest?.Id;

    private Message? _newest;

    private void NoteNewest(Message message)
    {
        if (_newest is null || message.AtMs >= _newest.AtMs) _newest = message;
    }

    private static bool Technical => App.Current?.Prefs.ShowTechnical == true;

    // ------------------------------------------------------------------ contact

    public async Task ShowContactAsync(Contact contact)
    {
        int version = ++_loadVersion;
        bool sameContact = _contact?.Address == contact.Address;
        _contact = contact;
        if (!sameContact)
        {
            ResetRoute();
            Messages.Children.Clear();
            _bubbles.Clear();
            _away = null;
            _newest = null;
            _lastAtMs = null;
            _lastPath = null;
            _lastPathKind = null;
            ErrorBar.IsOpen = false;
            Composer.Text = string.Empty;
            NewMessagesPill.Visibility = Visibility.Collapsed;
        }
        UpdateContact(contact);
        // Opening a chat (after another page, or after Unlock) shows the newest message (E1).
        _stickToEnd = true;

        try
        {
            IReadOnlyList<Message> history = await _session.Engine.MessagesHistoryAsync(contact.Address, null, 100);
            if (version != _loadVersion) return; // another friend was opened meanwhile
            foreach (Message m in history.OrderBy(m => m.AtMs))
            {
                _shell.RegisterMessage(m);
                AddOrUpdate(m);
            }
            Message? last = history.Where(m => !string.IsNullOrEmpty(m.Path)).OrderBy(m => m.AtMs).LastOrDefault();
            SetPath(last?.PathKind, last?.Path);
        }
        catch (Exception ex)
        {
            Ui.ShowError(ErrorBar, ex);
        }

        RenderAway();
        UpdateEmptyState();
        ScrollToEndSoon();
        Tick(RavenSession.NowMs);
        Composer.Focus(FocusState.Programmatic);
    }

    /// <summary>New data for the open friend (verified, name, presence, waiting...).</summary>
    public void UpdateContact(Contact contact)
    {
        if (_contact is not null && _contact.Address != contact.Address) return;
        _contact = contact;
        string name = ContactPresenter.DisplayName(contact);
        NameText.Text = name;
        _headerAvatar.Set(contact.Address, name);
        _headerAvatar.SetPresence(ContactPresenter.DotPresence(contact), animate: false);
        VerifiedText.Text = Loc.Get(contact.Verified ? "home.verified" : "home.notVerified");
        VerifiedText.Style = Ui.Style(contact.Verified ? "RavenMetaSuccessTextStyle" : "RavenMetaWarningTextStyle");
        VerifiedIcon.Visibility = contact.Verified ? Visibility.Visible : Visibility.Collapsed;
        RouteFriend.Text = name;
        string routes = Loc.Format("network.routes.title", ("name", name));
        AutomationProperties.SetName(RoutesButton, routes);
        ToolTipService.SetToolTip(RoutesButton, routes);
        VerifyText.Text = Loc.Format("home.banner.notVerified", ("name", name));
        VerifyBanner.Visibility = contact.Verified ? Visibility.Collapsed : Visibility.Visible;
        Composer.PlaceholderText = Loc.Format("home.composer", ("name", name));
        AutomationProperties.SetName(Composer, Loc.Format("home.composer", ("name", name)));
        E2eText.Text = Loc.Format("chat.e2e", ("name", name));
        EmptyText.Text = Loc.Format("home.emptyChat", ("name", name));
        // Messages to the friend wait (or no longer do): the header's path follows (E1).
        RenderHeader();
        RenderAway();
    }

    // ------------------------------------------------------------------ events (honest motion)

    /// <summary>send_started: a packet travels out along the route line.</summary>
    public void OnSendStarted(string? id, bool fresh)
    {
        if (id is null || !_bubbles.ContainsKey(id)) return;
        if (!fresh || !Motion.Enabled) return;
        EnqueuePacket(PacketKind.Out, MotionTokens.RoutePacket, then: null);
    }

    /// <summary>
    /// receipt: the bubble says "Delivered" on the receipt event itself, in step with the toast
    /// (E1: the bubble came ~0.6 s later); the check pops, the chip slides in, and the signed
    /// receipt travels back along the route line.
    /// </summary>
    public void OnReceipt(RavenEvent e, bool fresh)
    {
        if (e.Id is null || !_bubbles.TryGetValue(e.Id, out BubbleView? bubble)) return;
        if (!string.IsNullOrEmpty(e.Path) || !string.IsNullOrEmpty(e.PathKind)) SetPath(e.PathKind, e.Path);

        bool hadChip = bubble.Chip.Shown;
        bubble.Message.State = MessageStates.Delivered;
        bubble.Message.Path = e.Path ?? bubble.Message.Path;
        bubble.Message.PathKind = e.PathKind ?? bubble.Message.PathKind;
        bubble.Message.UntilMs = null;
        // A late delivery says when it came (E1).
        bubble.Message.DeliveredAtMs = e.DeliveredAtMs ?? bubble.Message.DeliveredAtMs;
        bool animate = fresh && Motion.Enabled;
        RenderTick(bubble, receipt: animate);
        RenderPath(bubble);
        RenderBubbleStyle(bubble);
        if (animate && !hadChip && bubble.Chip.Shown) bubble.Chip.SlideIn();
        RenderAway();
        if (animate) EnqueuePacket(PacketKind.Receipt, MotionTokens.ReceiptBack, then: null);
    }

    /// <summary>incoming: a packet travels in, then the bubble rises and keeps a soft ring for 1.5 s.</summary>
    public void OnIncoming(Message message, bool fresh)
    {
        if (_contact is null || message.Address != _contact.Address) return;
        NoteNewest(message);
        if (!string.IsNullOrEmpty(message.Path) || !string.IsNullOrEmpty(message.PathKind)) SetPath(message.PathKind, message.Path);

        void Show()
        {
            BubbleView view = AddOrUpdate(message);
            UpdateEmptyState();
            if (_stickToEnd) ScrollToEnd();
            else NewMessagesPill.Visibility = Visibility.Visible;
            EmphasiseIncoming(view, animate: fresh);
        }

        if (!fresh || !Motion.Enabled)
        {
            Show();
            return;
        }
        EnqueuePacket(PacketKind.In, MotionTokens.RoutePacket, then: Show);
    }

    /// <summary>
    /// outgoing: a message of yours another program on this profile sent (the terminal app),
    /// shown at once with its path (E1: it appeared only after the chat was rebuilt).
    /// </summary>
    public void OnOutgoing(Message message)
    {
        if (_contact is null || message.Address != _contact.Address) return;
        if (!string.IsNullOrEmpty(message.Path) || !string.IsNullOrEmpty(message.PathKind)) SetPath(message.PathKind, message.Path);
        AddOrUpdate(message);
        UpdateEmptyState();
        if (_stickToEnd) ScrollToEnd();
        else NewMessagesPill.Visibility = Visibility.Visible;
    }

    /// <summary>state: the tick text changes (no motion); the deadline comes with it.</summary>
    public void OnState(string? id, string? state, long? untilMs = null)
    {
        if (id is null || state is null || !_bubbles.TryGetValue(id, out BubbleView? bubble)) return;
        if (state == MessageStates.Delivered) return; // delivery is shown only with its receipt
        bubble.Message.State = state;
        if (untilMs is long until) bubble.Message.UntilMs = until;
        else if (state != MessageStates.Waiting) bubble.Message.UntilMs = null;
        RenderTick(bubble, receipt: false);
        RenderBubbleStyle(bubble);
        RenderAway();
        _ = RefreshMessageAsync(id);
    }

    /// <summary>retry: Raven tried the friend again (the away card's ring goes out once).</summary>
    public void ShowRetry(bool fresh)
    {
        if (fresh) _away?.ShowRetry();
    }

    /// <summary>A message changed outside an event (retry, stop): show the engine's copy.</summary>
    public void UpdateMessage(Message message)
    {
        if (_bubbles.ContainsKey(message.Id)) AddOrUpdate(message);
        RenderAway();
    }

    /// <summary>
    /// Try now / Send again on message <paramref name="oldId"/>: the same bubble shows the engine's
    /// answer. Send again of an ended message comes back with a new id ("replaces"); the bubble is
    /// kept under the new id, so the chat never shows the text twice.
    /// </summary>
    public void ReplaceMessage(string oldId, Message message)
    {
        if (oldId != message.Id && _bubbles.TryGetValue(oldId, out BubbleView? old))
        {
            _bubbles.Remove(oldId);
            if (_bubbles.TryGetValue(message.Id, out BubbleView? dup))
            {
                Messages.Children.Remove(dup.Root); // an event showed the new one first
                _bubbles.Remove(message.Id);
            }
            _bubbles[message.Id] = old;
        }
        UpdateMessage(message);
    }

    /// <summary>node: nothing in this view depends on it now but the relative times.</summary>
    public void OnNode() => Tick(RavenSession.NowMs);

    /// <summary>Reduced motion or the technical details changed: spinners and chips follow.</summary>
    public void OnMotionChanged()
    {
        foreach (BubbleView bubble in _bubbles.Values)
        {
            bubble.Tick?.Refresh();
            RenderPath(bubble);
        }
        _headerAvatar.SetActive(_active);
    }

    /// <summary>Shows one toast above the composer for 5 s (tokens.json toastVisible), with a polite Narrator announcement.</summary>
    public void ShowToast(string text, Tone tone)
    {
        ToastText.Text = text;
        ToastText.Style = Ui.Style(TickView.MetaStyle(tone));
        ToastText.FontSize = 12;
        ToastIcon.Glyph = tone switch
        {
            Tone.Success => TickView.Glyph(TickIcon.Delivered),
            Tone.Warning => TickView.Glyph(TickIcon.Waiting),
            _ => "\uE715",
        };
        ToastIcon.Style = Ui.Style(tone switch
        {
            Tone.Success => "RavenTickIconSuccessStyle",
            Tone.Warning => "RavenTickIconWarningStyle",
            Tone.Accent => "RavenTickIconAccentStyle",
            _ => "RavenTickIconStyle",
        });
        _toastVersion++;
        bool wasHidden = Toast.Visibility != Visibility.Visible;
        Toast.Visibility = Visibility.Visible;
        if (wasHidden) Motion.ToastIn(Toast, ToastShift);
        else Toast.Opacity = 1;
        Announce(ToastText);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        int version = _toastVersion;
        // A toast shown again while this one fades stays (only the faded one collapses).
        Motion.ToastOut(Toast, () =>
        {
            if (version == _toastVersion) Toast.Visibility = Visibility.Collapsed;
            else Toast.Opacity = 1;
        });
    }

    private int _toastVersion;

    private static void Announce(FrameworkElement element)
    {
        try
        {
            AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        catch (Exception)
        {
            // Narrator still reads the text when it gets there.
        }
    }

    /// <summary>The 1-second clock: relative times only, no motion.</summary>
    public void Tick(long nowMs)
    {
        // "Waiting for Mina · about 22 hours left" counts down.
        foreach (BubbleView bubble in _bubbles.Values)
        {
            if (bubble.Message.State == MessageStates.Waiting) RenderTick(bubble, receipt: false);
        }
        RenderHeader();
    }

    public void SetActive(bool active)
    {
        _active = active;
        if (!active) ResetRoute();
        _headerAvatar.SetActive(active);
    }

    // ------------------------------------------------------------------ header and route line

    /// <summary>How the last message travelled (the header's pill, the subtitle and the route line).</summary>
    private void SetPath(string? pathKind, string? path)
    {
        if (string.IsNullOrEmpty(pathKind) && string.IsNullOrEmpty(path)) return;
        _lastPath = path;
        _lastPathKind = pathKind;
        RenderHeader();
    }

    /// <summary>
    /// The header's status and path, the path pill and the route line. While messages to the friend
    /// wait, the last path is not claimed (E1: "LAN direct" stayed while the friend was away and every
    /// try failed): the pill then says a friend holds the mail only when a waiting message's path
    /// kind says so.
    /// </summary>
    private void RenderHeader()
    {
        if (_contact is null) return;
        long now = RavenSession.NowMs;
        string status = ContactPresenter.StatusLine(_contact, now);
        bool waiting = _contact.Presence == Presences.Waiting || _contact.Waiting is { Count: > 0 };
        bool inTouch = _contact.Presence == Presences.InTouch && !waiting;
        string? pathWords = ContactPresenter.HeaderPath(_contact, CalmPath.Chip(_lastPathKind, _lastPath, Technical));
        SubtitleText.Text = inTouch && pathWords is not null ? status + " · " + pathWords : status;
        SubtitleText.Style = Ui.Style(waiting ? "RavenCaptionLineWarningTextStyle" : "RavenCaptionLineTextStyle");
        SubtitleText.FontSize = 12.5;

        BubbleView? held = waiting ? _bubbles.Values.FirstOrDefault(b => b.Message.IsOutgoing && b.Message.State == MessageStates.Waiting
            && CalmPath.Family(b.Message.PathKind) == PathFamily.Friend) : null;
        if (held is not null) _pathPill.Set(held.Message.PathKind, held.Message.Path, Technical);
        else if (pathWords is not null && _lastPathKind is not null) _pathPill.Set(_lastPathKind, _lastPath, Technical);
        else if (pathWords is not null) _pathPill.SetText(PathFamily.Other, pathWords, PathPresenter.AccessibleName(null, _lastPath));
        else _pathPill.Hide();
        PathPillHost.Visibility = _pillFits ? Visibility.Visible : Visibility.Collapsed;

        RenderRoute(waiting, held, inTouch);
        FitHeader();
    }

    private void RenderRoute(bool waiting, BubbleView? held, bool inTouch)
    {
        string? kind = held?.Message.PathKind ?? (waiting ? null : _lastPathKind);
        string? path = held?.Message.Path ?? (waiting ? null : _lastPath);
        string label = waiting && held is null
            ? (_contact is null ? string.Empty : Loc.Format("tick.waitingNoTime", ("name", ContactPresenter.DisplayName(_contact))))
            : CalmPath.RouteLabel(kind, path);
        TrackSolid.Visibility = inTouch ? Visibility.Visible : Visibility.Collapsed;
        TrackDashed.Visibility = inTouch ? Visibility.Collapsed : Visibility.Visible;
        string key = label + "|" + kind + "|" + waiting + "|" + (held is null);
        if (key == _routeKey) return; // the clock re-renders every second: rebuild only on a change
        _routeKey = key;
        RouteLabelRow.Children.Clear();
        if (label.Length == 0)
        {
            RouteLabel.Visibility = Visibility.Collapsed;
            return;
        }
        PathFamily family = waiting && held is null ? PathFamily.Other : CalmPath.Family(kind);
        if (PathChip.Icon(family, 12) is UIElement icon) RouteLabelRow.Children.Add(icon);
        TextBlock text = Ui.Text(label, $"RavenChip{family}TextStyle");
        text.FontSize = 11.5;
        if (waiting && held is null) text.Style = Ui.Style("RavenMetaWarningTextStyle");
        RouteLabelRow.Children.Add(text);
        RouteLabel.Visibility = Visibility.Visible;
    }

    private void OnTrackSizeChanged(object sender, SizeChangedEventArgs e)
    {
        TrackDashed.X2 = e.NewSize.Width;
        PacketCanvas.Width = e.NewSize.Width;
    }

    /// <summary>
    /// The header got narrower or wider, or the name changed: the pill shows only while the name and
    /// status keep some room; Routes always shows whole (E1).
    /// </summary>
    private void OnHeaderSizeChanged(object sender, SizeChangedEventArgs e) => FitHeader();

    private void FitHeader()
    {
        if (!_pathPill.Shown)
        {
            PathPillHost.Visibility = Visibility.Collapsed;
            return;
        }
        _pathPill.Root.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        double fixedWidth = HeaderGrid.Padding.Left + HeaderGrid.Padding.Right + HeaderAvatarHost.ActualWidth
            + RoutesButton.ActualWidth + 3 * HeaderGrid.ColumnSpacing;
        // The name block keeps 72 points beside the status text's minimum (HeaderFit.MinStatusWidth).
        bool fits = HeaderFit.ShowsPathPill(HeaderGrid.ActualWidth - fixedWidth - 72, _pathPill.Root.DesiredSize.Width);
        _pillFits = fits;
        PathPillHost.Visibility = fits ? Visibility.Visible : Visibility.Collapsed;
    }

    private enum PacketKind
    {
        Out,
        Receipt,
        In,
    }

    /// <summary>One packet on the route line at a time, in the order the events came.</summary>
    private void EnqueuePacket(PacketKind kind, int durationMs, Action? then)
    {
        _route.Enqueue(done =>
        {
            double width = Math.Max(0, PacketCanvas.ActualWidth);
            FrameworkElement dot;
            if (kind == PacketKind.Receipt)
            {
                dot = new Border { Style = Ui.Style("RavenReceiptDotStyle"), Child = new FontIcon { Style = Ui.Style("RavenReceiptGlyphStyle") } };
            }
            else
            {
                dot = new Ellipse { Style = Ui.Style("RavenPacketDotStyle") };
            }
            double size = kind == PacketKind.Receipt ? 14 : 10;
            var transform = new CompositeTransform();
            dot.RenderTransform = transform;
            dot.RenderTransformOrigin = new WinPoint(0.5, 0.5);
            Canvas.SetTop(dot, (14 - size) / 2);
            PacketCanvas.Children.Add(dot);
            double end = Math.Max(0, width - size);
            double from = kind == PacketKind.Out ? 0 : end;
            double to = kind == PacketKind.Out ? end : 0;
            Motion.Packet(dot, transform, from, to, durationMs, () =>
            {
                PacketCanvas.Children.Remove(dot);
                then?.Invoke();
                done();
            });
        });
        RunNextTravel();
    }

    private void RunNextTravel()
    {
        if (_routeBusy || _route.Count == 0) return;
        _routeBusy = true;
        Action<Action> next = _route.Dequeue();
        next(() =>
        {
            _routeBusy = false;
            RunNextTravel();
        });
    }

    /// <summary>Drops queued travels (another chat opened, window hidden); texts are already right.</summary>
    private void ResetRoute()
    {
        _route.Clear();
        _routeBusy = false;
        PacketCanvas.Children.Clear();
    }

    // ------------------------------------------------------------------ bubbles

    private BubbleView AddOrUpdate(Message message)
    {
        NoteNewest(message);
        if (_bubbles.TryGetValue(message.Id, out BubbleView? existing))
        {
            existing.Message = message;
            existing.Body.Text = message.Text;
            existing.TimeText.Text = RelativeTime.BubbleTime(message.AtMs, RavenSession.NowMs);
            RenderTick(existing, receipt: false);
            RenderPath(existing);
            RenderBubbleStyle(existing);
            return existing;
        }

        var view = new BubbleView(message);
        if (message.IsOutgoing)
        {
            view.Tick = new TickView();
            // The tick opens the message's details (how it travels, Try now, Stop trying...).
            view.TickButton = new HyperlinkButton
            {
                Content = view.Tick.Root,
                Padding = new Thickness(2, 0, 2, 0),
                MinHeight = 0,
                MinWidth = 0,
            };
            view.TickButton.Click += (_, _) => _ = _shell.OpenMessageDetailsAsync(view.Message);
            view.Meta.Children.Add(view.TickButton);
            var again = new HyperlinkButton { Content = Loc.Get("message.sendAgain"), Padding = new Thickness(2, 0, 2, 0), MinHeight = 0 };
            again.Click += (_, _) => _ = RetryAsync(view);
            var delete = new HyperlinkButton { Content = Loc.Get("message.delete"), Padding = new Thickness(2, 0, 2, 0), MinHeight = 0 };
            delete.Click += (_, _) => _ = DeleteAsync(view);
            view.Actions.Children.Add(again);
            view.Actions.Children.Add(delete);
        }
        view.Meta.Children.Add(view.Chip.Root);
        view.Bubble.MaxWidth = _bubbleMaxWidth;

        _bubbles[message.Id] = view;
        // A line between days: "Today", "Yesterday", "Tue 2026-10-20".
        if (_lastAtMs is not long last || RelativeTime.DifferentDays(last, message.AtMs))
            Append(DayLine(message.AtMs));
        if (_lastAtMs is not long newest || message.AtMs >= newest) _lastAtMs = message.AtMs;
        Append(view.Root);
        RenderTick(view, receipt: false);
        RenderPath(view);
        RenderBubbleStyle(view);
        UpdateEmptyState(); // never "Say hello!" behind a bubble
        return view;
    }

    /// <summary>Adds at the end of the chat, above the away card when it shows.</summary>
    private void Append(UIElement element)
    {
        if (_away is not null && Messages.Children.Contains(_away.Root))
            Messages.Children.Insert(Messages.Children.IndexOf(_away.Root), element);
        else
            Messages.Children.Add(element);
    }

    private void RenderTick(BubbleView view, bool receipt)
    {
        if (view.Tick is null || view.TickButton is null || _contact is null) return;
        TickPresenter.Tick tick = TickPresenter.For(view.Message, ContactPresenter.DisplayName(_contact), RavenSession.NowMs);
        view.Tick.Set(tick, receipt);
        view.TickButton.Visibility = string.IsNullOrEmpty(tick.Text) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(view.TickButton, tick.Text);
        view.Actions.Visibility = TickPresenter.CanDelete(view.Message) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A waiting message of yours is an outline (Calm "pending"); every other one of yours the gradient.</summary>
    private static void RenderBubbleStyle(BubbleView view)
    {
        if (!view.Message.IsOutgoing) return;
        bool waiting = view.Message.State == MessageStates.Waiting;
        view.Bubble.Style = Ui.Style(waiting ? "RavenBubbleOutWaitingStyle" : "RavenBubbleOutStyle");
        view.Body.Style = Ui.Style(waiting ? "RavenBubbleOutWaitingTextStyle" : "RavenBubbleOutTextStyle");
    }

    private static UIElement DayLine(long atMs)
    {
        TextBlock text = Ui.Text(RelativeTime.Day(atMs, RavenSession.NowMs), "RavenMetaTertiaryTextStyle");
        text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(text, AutomationHeadingLevel.Level3);
        var line = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 14, 0, 8) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new Rectangle { Style = Ui.Style("RavenDayLineStyle") };
        var right = new Rectangle { Style = Ui.Style("RavenDayLineStyle") };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(right, 2);
        line.Children.Add(left);
        line.Children.Add(text);
        line.Children.Add(right);
        return line;
    }

    /// <summary>Send again: the engine sends the same text as a new message, in this bubble's place.</summary>
    private async Task RetryAsync(BubbleView view)
    {
        string oldId = view.Message.Id;
        try
        {
            Message again = await _session.Engine.MessagesRetryAsync(oldId);
            _shell.RegisterMessage(again);
            ReplaceMessage(oldId, again);
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            Ui.ShowError(ErrorBar, ex);
        }
    }

    /// <summary>Delete: a message that was not sent leaves the chat for good (messages.delete).</summary>
    private async Task DeleteAsync(BubbleView view)
    {
        string id = view.Message.Id;
        try
        {
            await _session.Engine.MessagesDeleteAsync(id);
            Messages.Children.Remove(view.Root);
            _bubbles.Remove(id);
            if (_newest?.Id == id) _newest = _bubbles.Values.Select(b => b.Message).OrderBy(m => m.AtMs).LastOrDefault();
            UpdateEmptyState();
            RenderAway();
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            Ui.ShowError(ErrorBar, ex);
        }
    }

    /// <summary>The path chip: the Calm words in the path's colour; hidden without a path.</summary>
    private static void RenderPath(BubbleView view)
    {
        // A message of yours shows its path once it has gone (not while sending or sent, not confirmed yet).
        if (view.Message.IsOutgoing && view.Message.State is MessageStates.Sending or MessageStates.Sent)
        {
            view.Chip.Hide();
            return;
        }
        view.Chip.Set(view.Message.PathKind, view.Message.Path, Technical);
    }

    private void EmphasiseIncoming(BubbleView view, bool animate)
    {
        if (animate) Motion.BubbleIn(view.Root, view.Rise);
        view.Bubble.Style = Ui.Style("RavenBubbleInFreshStyle");
        DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(CalmMs.IncomingHighlight);
        timer.IsRepeating = false;
        timer.Tick += (t, _) =>
        {
            view.Bubble.Style = Ui.Style("RavenBubbleInStyle");
            t.Stop();
        };
        timer.Start();
    }

    private async Task RefreshMessageAsync(string id)
    {
        // Keep until_ms and attempts in step with the engine after a state change.
        if (_contact is null) return;
        try
        {
            IReadOnlyList<Message> history = await _session.Engine.MessagesHistoryAsync(_contact.Address, null, 100);
            Message? fresh = history.FirstOrDefault(m => m.Id == id);
            if (fresh is not null && _bubbles.TryGetValue(id, out BubbleView? view) && fresh.State != MessageStates.Delivered)
            {
                view.Message = fresh;
                RenderTick(view, receipt: false);
                RenderPath(view);
                RenderBubbleStyle(view);
                RenderAway();
            }
        }
        catch (RavenException)
        {
            // The tick already shows the state from the event.
        }
    }

    // ------------------------------------------------------------------ the away card

    /// <summary>The away card under the messages while a message to the friend waits (only what the engine says).</summary>
    private void RenderAway()
    {
        if (_contact is null) return;
        AwayPresenter.Card? card = AwayPresenter.For(_contact, _bubbles.Values.Select(b => b.Message), RavenSession.NowMs);
        if (card is not AwayPresenter.Card shown)
        {
            if (_away is not null) Messages.Children.Remove(_away.Root);
            _away = null;
            return;
        }
        if (_away is null)
        {
            _away = new AwayCard(this);
            Messages.Children.Add(_away.Root);
        }
        _away.Render(_contact, shown);
    }

    private async Task AwayTryNowAsync(string id)
    {
        try
        {
            Message again = await _session.Engine.MessagesRetryAsync(id);
            _shell.RegisterMessage(again);
            ReplaceMessage(id, again);
            _away?.ShowRetry();
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            Ui.ShowError(ErrorBar, ex);
        }
    }

    private async Task AwayStopAsync(string id)
    {
        try
        {
            Message stopped = await _session.Engine.MessagesStopAsync(id);
            UpdateMessage(stopped);
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            Ui.ShowError(ErrorBar, ex);
        }
    }

    private async Task<MessageDetails?> AwayDetailsAsync(string id)
    {
        try
        {
            return await _session.Engine.MessagesDetailsAsync(id);
        }
        catch (Exception ex)
        {
            Ui.ShowError(ErrorBar, ex);
            return null;
        }
    }

    /// <summary>
    /// "Mina is away. Your message waits.": the engine's deadline, a friend holding a sealed copy
    /// only when its path kind says so, Try now (messages.retry), Details (the engine's own status,
    /// why and what to do, from messages.details) and Stop trying (messages.stop) for the oldest
    /// waiting message.
    /// </summary>
    private sealed class AwayCard
    {
        private readonly ChatView _owner;
        private readonly TextBlock _title;
        private readonly TextBlock _body;
        private readonly TextBlock _held;
        private readonly Avatar _you = new(30, withDot: false);
        private readonly Avatar _friend = new(30, withDot: true);
        private readonly FontIcon _envelope;
        private readonly StackPanel _details = new() { Spacing = 4, Visibility = Visibility.Collapsed };
        private readonly Button _detailsButton;
        private string? _id;

        public AwayCard(ChatView owner)
        {
            _owner = owner;
            _title = Ui.Text(string.Empty, "RavenLabelWrapTextStyle");
            _title.FontSize = 14.5;
            _body = Ui.Text(string.Empty, "RavenCaptionTextStyle");
            _held = Ui.Text(string.Empty, "RavenCaptionTextStyle");
            var texts = new StackPanel { Spacing = 4, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
            texts.Children.Add(_title);
            texts.Children.Add(_body);
            texts.Children.Add(_held);

            _you.SetYou(Loc.Get("you.title"));
            _envelope = new FontIcon { Glyph = "\uE715", Style = Ui.Style("RavenChipFriendIconStyle"), FontSize = 14 };
            var trio = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAccessibilityView(trio, AccessibilityView.Raw);
            trio.Children.Add(_you.Root);
            trio.Children.Add(new Rectangle { Style = Ui.Style("RavenTrackLineStyle"), Width = 28 });
            trio.Children.Add(_envelope);
            var dashed = new Line { Style = Ui.Style("RavenTrackDashedStyle"), X1 = 0, X2 = 28, StrokeDashArray = new DoubleCollection { 3, 2.5 } };
            trio.Children.Add(dashed);
            _friend.Root.Opacity = 0.55;
            trio.Children.Add(_friend.Root);

            var tryNow = new Button { Content = Loc.Get("message.tryNow") };
            tryNow.Click += (_, _) => { if (_id is not null) _ = _owner.AwayTryNowAsync(_id); };
            _detailsButton = new Button { Content = Loc.Get("message.details") };
            _detailsButton.Click += (_, _) => _ = ToggleDetailsAsync();
            var stop = new HyperlinkButton { Content = Loc.Get("message.stopTrying") };
            stop.Click += (_, _) => { if (_id is not null) _ = _owner.AwayStopAsync(_id); };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            actions.Children.Add(tryNow);
            actions.Children.Add(_detailsButton);
            actions.Children.Add(stop);

            var grid = new Grid { ColumnSpacing = 18, RowSpacing = 14 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(trio, 1);
            Grid.SetRow(actions, 1);
            Grid.SetColumnSpan(actions, 2);
            Grid.SetRow(_details, 2);
            Grid.SetColumnSpan(_details, 2);
            grid.Children.Add(texts);
            grid.Children.Add(trio);
            grid.Children.Add(actions);
            grid.Children.Add(_details);
            Root = new Border { Style = Ui.Style("RavenAwayCardStyle"), Child = grid };
        }

        public Border Root { get; }

        public void Render(Contact contact, AwayPresenter.Card card)
        {
            if (_id != card.OldestId)
            {
                _details.Visibility = Visibility.Collapsed;
                _details.Children.Clear();
            }
            _id = card.OldestId;
            _title.Text = card.Title;
            _body.Text = card.Body;
            _held.Text = card.Held ?? string.Empty;
            _held.Visibility = card.Held is null ? Visibility.Collapsed : Visibility.Visible;
            _envelope.Visibility = card.Held is null ? Visibility.Collapsed : Visibility.Visible;
            string name = ContactPresenter.DisplayName(contact);
            _friend.Set(contact.Address, name);
            _friend.SetPresence(Presences.Waiting, animate: false);
            AutomationProperties.SetName(Root, card.Title + " " + card.Body + (card.Held is null ? string.Empty : " " + card.Held));
        }

        public void ShowRetry() => _friend.ShowRetry();

        private async Task ToggleDetailsAsync()
        {
            if (_details.Visibility == Visibility.Visible)
            {
                _details.Visibility = Visibility.Collapsed;
                return;
            }
            if (_id is null || await _owner.AwayDetailsAsync(_id) is not MessageDetails details) return;
            _details.Children.Clear();
            Add("away.status", details.StatusText);
            Add("away.why", details.WhyText);
            Add("away.todo", details.ActionText);
            _details.Visibility = _details.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Add(string labelKey, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            TextBlock line = Ui.Text(Loc.Get(labelKey) + ". " + text, "RavenCaptionTextStyle");
            line.IsTextSelectionEnabled = true;
            _details.Children.Add(line);
        }
    }

    // ------------------------------------------------------------------ scrolling

    private void UpdateEmptyState()
    {
        EmptyText.Visibility = _bubbles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ScrollToEnd()
    {
        _stickToEnd = true;
        NewMessagesPill.Visibility = Visibility.Collapsed;
        Scroller.UpdateLayout();
        Scroller.ChangeView(null, Scroller.ScrollableHeight, null, disableAnimation: !Motion.Enabled);
    }

    /// <summary>Now, and again once the page is laid out (a chat shown after another page has no height yet).</summary>
    private void ScrollToEndSoon()
    {
        ScrollToEnd();
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ScrollToEnd);
    }

    private void OnScrollerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double content = Math.Min(e.NewSize.Width, (double)Ui.Resource("RavenContentMaxWidth")!) - 56;
        _bubbleMaxWidth = Math.Max(160, content * (double)Ui.Resource("RavenBubbleMaxFraction")!);
        foreach (BubbleView view in _bubbles.Values) view.Bubble.MaxWidth = _bubbleMaxWidth;
        // After a resize the newest message stays in view (E1).
        if (_stickToEnd) DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ScrollToEnd);
    }

    /// <summary>The messages grew (new bubbles, wrapped text): follow them while at the bottom.</summary>
    private void OnMessagesSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_stickToEnd) Scroller.ChangeView(null, Scroller.ScrollableHeight, null, disableAnimation: true);
    }

    /// <summary>The person scrolled: follow new messages only while at the bottom; the pill goes at the bottom.</summary>
    private void OnScrollerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate) return;
        _stickToEnd = Scroller.VerticalOffset >= Scroller.ScrollableHeight - 24;
        if (_stickToEnd) NewMessagesPill.Visibility = Visibility.Collapsed;
    }

    private void OnNewMessagesClick(object sender, RoutedEventArgs e) => ScrollToEnd();

    // ------------------------------------------------------------------ composer

    private void OnComposerFocus(object sender, RoutedEventArgs e) => ComposerPill.Style = Ui.Style("RavenComposerFocusedStyle");

    private void OnComposerBlur(object sender, RoutedEventArgs e) => ComposerPill.Style = Ui.Style("RavenComposerStyle");

    private void OnComposerKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        CoreVirtualKeyStates shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if ((shift & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down) return; // Shift+Enter: new line
        e.Handled = true;
        _ = SendAsync();
    }

    private void OnSendClick(object sender, RoutedEventArgs e) => _ = SendAsync();

    private async Task SendAsync()
    {
        if (_contact is null) return;
        string text = Composer.Text.Trim();
        if (text.Length == 0) return;

        SendButton.IsEnabled = false;
        EmptyText.Visibility = Visibility.Collapsed; // the first message is on its way
        try
        {
            Message sent = await _session.Engine.MessagesSendAsync(_contact.Address, text);
            Composer.Text = string.Empty;
            ErrorBar.IsOpen = false;
            _shell.RegisterMessage(sent);
            if (sent.Address == _contact.Address)
            {
                bool isNew = !_bubbles.ContainsKey(sent.Id);
                BubbleView view = AddOrUpdate(sent);
                UpdateEmptyState();
                ScrollToEnd();
                if (isNew) LiftFromComposer(view);
            }
        }
        catch (Exception ex)
        {
            Ui.ShowError(ErrorBar, ex);
            UpdateEmptyState();
        }
        finally
        {
            SendButton.IsEnabled = true;
            Composer.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>Your bubble lifts out of the composer to its place, once the chat has scrolled to it.</summary>
    private void LiftFromComposer(BubbleView view)
    {
        if (!Motion.Enabled) return;
        view.Bubble.Opacity = 0;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            view.Bubble.Opacity = 1;
            try
            {
                WinPoint from = Composer.TransformToVisual(view.Bubble).TransformPoint(new WinPoint(0, 0));
                Motion.Lift(view.Bubble, view.Lift, from.X, from.Y);
            }
            catch (Exception)
            {
                // Not laid out: it simply shows in its place.
            }
        });
    }

    private void OnCheckWords(object sender, RoutedEventArgs e)
    {
        if (_contact is not null) _ = _shell.OpenCheckWordsAsync(_contact);
    }

    private void OnRoutesClick(object sender, RoutedEventArgs e)
    {
        if (_contact is not null) _ = _shell.OpenRoutesAsync(_contact);
    }

    // ------------------------------------------------------------------ bubble view

    private sealed class BubbleView
    {
        public BubbleView(Message message)
        {
            Message = message;
            bool outgoing = message.IsOutgoing;
            Body = Ui.Text(message.Text, outgoing ? "RavenBubbleOutTextStyle" : "RavenBubbleInTextStyle");
            Bubble = new Border
            {
                Style = Ui.Style(outgoing ? "RavenBubbleOutStyle" : "RavenBubbleInStyle"),
                Child = Body,
                RenderTransform = Lift,
                RenderTransformOrigin = new WinPoint(outgoing ? 1 : 0, 1),
            };
            TimeText = Ui.Text(RelativeTime.BubbleTime(message.AtMs, RavenSession.NowMs), "RavenMetaTextStyle");
            Meta = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(4, 5, 4, 0),
                MinHeight = 20,
                HorizontalAlignment = outgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };
            Meta.Children.Add(TimeText);
            // Send again and Delete under a message of yours that was not sent (E1).
            Actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Visibility = Visibility.Collapsed,
            };
            Root = new StackPanel
            {
                Spacing = 0,
                Margin = new Thickness(0, 3, 0, 3),
                HorizontalAlignment = outgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                RenderTransform = Rise,
            };
            Root.Children.Add(Bubble);
            Root.Children.Add(Meta);
            Root.Children.Add(Actions);
        }

        public Message Message { get; set; }
        public StackPanel Root { get; }
        public Border Bubble { get; }
        public TextBlock Body { get; }
        public TextBlock TimeText { get; }
        public StackPanel Meta { get; }
        public StackPanel Actions { get; }
        public PathChip Chip { get; } = new();
        public TranslateTransform Rise { get; } = new();
        public CompositeTransform Lift { get; } = new();
        public TickView? Tick { get; set; }
        public HyperlinkButton? TickButton { get; set; }
    }
}
