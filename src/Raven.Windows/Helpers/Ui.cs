using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Helpers;

/// <summary>Small UI helpers shared by the screens.</summary>
internal static class Ui
{
    private static readonly SemaphoreSlim DialogGate = new(1, 1);

    /// <summary>A named style from Themes/Raven.xaml.</summary>
    public static Style Style(string key)
    {
        if (Resource(key) is Style style) return style;
        throw new InvalidOperationException($"Style '{key}' is missing from Themes/Raven.xaml.");
    }

    /// <summary>
    /// Looks a key up in the application resources, merged dictionaries included (the
    /// indexer searches them; ContainsKey/TryGetValue may not). Null when missing.
    /// </summary>
    public static object? Resource(string key)
    {
        try
        {
            return Application.Current.Resources[key];
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static TextBlock Text(string text, string styleKey = "RavenBodyTextStyle")
        => new() { Text = text, Style = Style(styleKey) };

    /// <summary>A small secondary text link (for example "Older Raven? Use the old code instead").</summary>
    public static HyperlinkButton SmallLink(string text)
    {
        TextBlock label = Text(text, "RavenSmallAccentTextStyle");
        label.TextTrimming = TextTrimming.None;
        label.TextWrapping = TextWrapping.Wrap;
        var link = new HyperlinkButton
        {
            Content = label,
            Padding = new Thickness(0, 2, 0, 2),
            MinHeight = 0,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(link, text);
        return link;
    }

    /// <summary>Small (11 px) text style for a tone.</summary>
    public static string SmallStyleFor(Tone tone) => tone switch
    {
        Tone.Accent => "RavenSmallAccentTextStyle",
        Tone.Success => "RavenSmallSuccessTextStyle",
        Tone.Warning => "RavenSmallWarningTextStyle",
        Tone.Danger => "RavenSmallDangerTextStyle",
        _ => "RavenSmallTextStyle",
    };

    /// <summary>Body (13 px) text style for a tone.</summary>
    public static string BodyStyleFor(Tone tone) => tone switch
    {
        Tone.Success => "RavenSuccessTextStyle",
        Tone.Warning => "RavenWarningTextStyle",
        Tone.Danger => "RavenDangerTextStyle",
        Tone.Secondary => "RavenSecondaryTextStyle",
        _ => "RavenBodyTextStyle",
    };

    /// <summary>
    /// Shows an error the Raven way: what happened and one next step (the engine's message),
    /// with the technical text one click away under "Details".
    /// </summary>
    public static void ShowError(InfoBar bar, Exception ex)
    {
        string message;
        string detail;
        if (ex is RavenException raven)
        {
            message = raven.Message;
            detail = raven.Detail;
        }
        else
        {
            message = Loc.Get("error.internal");
            detail = ex.GetType().Name + ": " + TextSanitizer.Clean(ex.Message);
        }

        bar.Severity = InfoBarSeverity.Error;
        bar.Title = string.Empty;
        bar.Message = message;
        bar.Content = string.IsNullOrWhiteSpace(detail)
            ? null
            : new Expander
            {
                Header = Loc.Get("message.details"),
                Content = Text(detail, "RavenMonoTextStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8),
            };
        bar.IsOpen = true;
    }

    /// <summary>
    /// A closed InfoBar takes no room in a StackPanel: it is collapsed while closed, so the panel's
    /// spacing isn't added around it (E1: four closed bars under the Lab banner made an empty gap).
    /// </summary>
    public static void CollapseWhenClosed(params InfoBar[] bars)
    {
        foreach (InfoBar bar in bars)
        {
            bar.Visibility = bar.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            bar.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty, (sender, _) =>
            {
                if (sender is InfoBar b) b.Visibility = b.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            });
        }
    }

    /// <summary>
    /// "Technical details": the CLI's own lines, raw counters and commands, collapsed and never
    /// in the plain words above them (E1). Null when there is nothing to show.
    /// </summary>
    public static Expander? TechnicalDetails(IEnumerable<string>? lines)
    {
        var clean = (lines ?? Enumerable.Empty<string>())
            .Select(TextSanitizer.SingleLine)
            .Where(l => l.Length > 0)
            .ToList();
        if (clean.Count == 0) return null;
        TextBlock text = Text(string.Join("\n", clean), "RavenMonoTextStyle");
        text.IsTextSelectionEnabled = true;
        return new Expander
        {
            Header = Loc.Get("message.technicalDetails"),
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
    }

    /// <summary>
    /// Shows a ContentDialog; only one can be open at a time in WinUI, so dialogs queue.
    /// Returns None when the dialog could not be shown.
    /// </summary>
    public static async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog, XamlRoot root)
    {
        await DialogGate.WaitAsync();
        try
        {
            dialog.XamlRoot = root;
            if (Resource("DefaultContentDialogStyle") is Style style) dialog.Style = style;
            // A dialog sits in a popup outside the window's tree: it takes the theme chosen in
            // Settings > Appearance from the window's root.
            if (root.Content is FrameworkElement content) dialog.RequestedTheme = content.RequestedTheme;
            return await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Raven: dialog failed: " + ex.Message);
            return ContentDialogResult.None;
        }
        finally
        {
            DialogGate.Release();
        }
    }

    /// <summary>True while a dialog is open (used to avoid stacking dialogs from the tray).</summary>
    public static bool DialogOpen => DialogGate.CurrentCount == 0;
}
