using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;

namespace Raven.Windows.Views;

/// <summary>
/// First launch: the Raven mark fades and scales in once (the only decorative motion),
/// then the R A V E N wordmark and the tagline, which types itself. With reduced motion
/// everything is simply there.
/// </summary>
public sealed partial class WelcomePage : UserControl
{
    private const int TaglineCharMs = 30; // the mockup's typing speed

    private readonly Action _onStart;
    private DispatcherQueueTimer? _typing;

    public WelcomePage(Action onStart)
    {
        _onStart = onStart;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => _typing?.Stop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        string wordmark = Loc.Get("app.name").ToUpperInvariant();
        string tagline = Loc.Get("app.tagline");
        AutomationHelper(wordmark, tagline);

        if (!Motion.Enabled)
        {
            Logo.Opacity = 1;
            Wordmark.Text = wordmark;
            Tagline.Text = tagline;
            StartButton.Focus(FocusState.Programmatic);
            return;
        }

        Motion.LogoReveal(Logo, LogoScale, () =>
        {
            Wordmark.Text = wordmark;
            Motion.FadeIn(Wordmark);
            TypeTagline(tagline);
        });
        StartButton.Focus(FocusState.Programmatic);
    }

    private void TypeTagline(string tagline)
    {
        int shown = 0;
        _typing = DispatcherQueue.CreateTimer();
        _typing.Interval = TimeSpan.FromMilliseconds(TaglineCharMs);
        _typing.IsRepeating = true;
        _typing.Tick += (timer, _) =>
        {
            shown++;
            Tagline.Text = tagline[..Math.Min(shown, tagline.Length)];
            if (shown >= tagline.Length) timer.Stop();
        };
        _typing.Start();
    }

    /// <summary>Screen readers get the full texts at once, not the typing.</summary>
    private void AutomationHelper(string wordmark, string tagline)
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Wordmark, wordmark);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Tagline, tagline);
    }

    private void OnStartClick(object sender, RoutedEventArgs e) => _onStart();
}
