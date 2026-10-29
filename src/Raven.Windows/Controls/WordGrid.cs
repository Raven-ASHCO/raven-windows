using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;
using Raven.Windows.Helpers;

namespace Raven.Windows.Controls;

/// <summary>
/// Your 10 security words as Calm tiles in two rows of five: the number, the word, and on word 10
/// the CHECK tag. Read aloud steps a highlight through them (800 ms each) and the line beside the
/// button says "Say: {word}"; clicking a tile says that word. Right after your identity was
/// created the tiles come in one by one (200 ms apart). With reduced motion nothing steps or moves
/// by itself; a click still highlights a word.
/// </summary>
public sealed class WordGrid : UserControl
{
    private readonly IReadOnlyList<string> _words;
    private readonly Border[] _tiles;
    private readonly TextBlock[] _numbers;
    private readonly TextBlock[] _labels;
    private readonly TextBlock?[] _tags;
    private readonly TranslateTransform[] _raise;
    private readonly TextBlock _say;
    private readonly bool _startStepping;
    private DispatcherQueueTimer? _timer;
    private int _highlight = -1;

    /// <param name="readAloud">Show Read aloud and the "Say:" line.</param>
    /// <param name="startStepping">Start reading aloud by itself once shown (Setup), else on the button.</param>
    /// <param name="reveal">The tiles come in one by one (right after the identity was made).</param>
    public WordGrid(IReadOnlyList<string> words, bool readAloud, bool startStepping = true, bool reveal = false)
    {
        _words = words;
        _startStepping = readAloud && startStepping;
        _tiles = new Border[words.Count];
        _numbers = new TextBlock[words.Count];
        _labels = new TextBlock[words.Count];
        _tags = new TextBlock?[words.Count];
        _raise = new TranslateTransform[words.Count];

        var grid = new Grid { Margin = new Thickness(-4, 0, -4, 0) };
        for (int c = 0; c < 5; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int rows = (words.Count + 4) / 5;
        for (int r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < words.Count; i++)
        {
            string number = (i + 1).ToString(CultureInfo.InvariantCulture);
            bool check = i == words.Count - 1;
            _numbers[i] = Ui.Text(number, "RavenWordNumberTextStyle");
            _labels[i] = Ui.Text(words[i], "RavenWordTextStyle");
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(_numbers[i]);
            if (check)
            {
                TextBlock tag = Ui.Text(Loc.Get("words.checkLabel").ToUpperInvariant(), "RavenWordCheckTagTextStyle");
                Grid.SetColumn(tag, 1);
                top.Children.Add(tag);
                _tags[i] = tag;
            }
            var stack = new StackPanel { Spacing = 0 };
            stack.Children.Add(top);
            stack.Children.Add(_labels[i]);

            _raise[i] = new TranslateTransform();
            var tile = new Border
            {
                Style = Ui.Style(check ? "RavenWordTileCheckStyle" : "RavenWordTileStyle"),
                Child = stack,
                RenderTransform = _raise[i],
            };
            AutomationProperties.SetName(tile, number + " " + words[i] + (check ? ", " + Loc.Get("words.checkLabel") : string.Empty));
            int index = i;
            tile.Tapped += (_, _) =>
            {
                StopStepping();
                Highlight(index);
            };

            Grid.SetRow(tile, i / 5);
            Grid.SetColumn(tile, i % 5);
            grid.Children.Add(tile);
            _tiles[i] = tile;
            if (reveal) tile.Opacity = 0;
        }

        _say = Ui.Text(string.Empty, "RavenSecondaryTextStyle");
        _say.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetLiveSetting(_say, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(grid);
        if (readAloud)
        {
            var read = new Button { Content = Loc.Get("words.readAloud") };
            read.Click += (_, _) => StartStepping(fromButton: true);
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, MinHeight = 32 };
            line.Children.Add(read);
            line.Children.Add(_say);
            panel.Children.Add(line);
        }
        Content = panel;

        Loaded += (_, _) =>
        {
            if (reveal) Reveal();
            if (_startStepping) StartStepping(fromButton: false);
        };
        Unloaded += (_, _) => StopStepping();
    }

    /// <summary>The tiles come in one by one, 200 ms apart (only right after the identity was created).</summary>
    private void Reveal()
    {
        for (int i = 0; i < _tiles.Length; i++)
        {
            Border tile = _tiles[i];
            if (!Motion.Enabled)
            {
                tile.Opacity = 1;
                continue;
            }
            int delay = i * MotionTokens.WordReveal;
            DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, delay));
            timer.IsRepeating = false;
            timer.Tick += (t, _) =>
            {
                t.Stop();
                tile.Opacity = 1;
                Motion.BubbleIn(tile, (TranslateTransform)tile.RenderTransform);
            };
            timer.Start();
        }
    }

    private void StartStepping(bool fromButton)
    {
        if (_words.Count == 0) return;
        StopStepping();
        if (!Motion.Enabled)
        {
            // Reduced motion: nothing steps by itself; the button says the first word.
            if (fromButton) Highlight(0);
            return;
        }
        Highlight(0);
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(MotionTokens.ReadAloudStep);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) =>
        {
            if (!Motion.Enabled)
            {
                StopStepping();
                return;
            }
            Highlight((_highlight + 1) % _words.Count);
        };
        _timer.Start();
    }

    private void StopStepping()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void Highlight(int index)
    {
        if (_highlight >= 0 && _highlight < _tiles.Length) Paint(_highlight, said: false);
        _highlight = index;
        Paint(index, said: true);
        string word = _words[index];
        _say.Text = index == _words.Count - 1
            ? Loc.Format("words.say", ("word", word)) + " (" + Loc.Get("words.checkLabel") + ")"
            : Loc.Format("words.say", ("word", word));
    }

    private void Paint(int i, bool said)
    {
        bool check = i == _words.Count - 1;
        _tiles[i].Style = Ui.Style(said ? "RavenWordTileSayStyle" : check ? "RavenWordTileCheckStyle" : "RavenWordTileStyle");
        _numbers[i].Style = Ui.Style(said ? "RavenWordSayNumberTextStyle" : "RavenWordNumberTextStyle");
        _labels[i].Style = Ui.Style(said ? "RavenWordSayTextStyle" : "RavenWordTextStyle");
        if (_tags[i] is TextBlock tag) tag.Style = Ui.Style(said ? "RavenWordCheckTagSayTextStyle" : "RavenWordCheckTagTextStyle");
        _raise[i].Y = said && Motion.Enabled ? -2 : 0;
    }
}
