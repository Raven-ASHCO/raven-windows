using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Raven.Windows.Tests;

/// <summary>
/// The Calm screens apply named styles from code (Ui.Style("...")), so a theme change updates
/// them; a missing key would throw the first time a screen opens. These tests read the sources:
/// every style the code asks for is in Themes/Raven.xaml, and every theme brush the XAML uses is in
/// the light, dark and high-contrast dictionaries.
/// </summary>
[TestClass]
public sealed class CalmStylesTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>WinUI's own resources the app uses by name.</summary>
    private static readonly HashSet<string> WinUiKeys = new(StringComparer.Ordinal) { "AccentButtonStyle", "DefaultContentDialogStyle" };

    private static string Themes => Path.Combine(RepoPaths.RequireWindowsRoot(), "src", "Raven.Windows", "Themes");

    private static HashSet<string> TopLevelKeys(string file, bool stylesOnly)
    {
        XElement root = XDocument.Load(Path.Combine(Themes, file)).Root!;
        return root.Elements()
            .Where(e => e.Attribute(X + "Key") is not null && (!stylesOnly || e.Name.LocalName == "Style"))
            .Select(e => e.Attribute(X + "Key")!.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> HighContrastKeys()
    {
        XElement root = XDocument.Load(Path.Combine(Themes, "Raven.xaml")).Root!;
        XElement hc = root.Descendants().First(e => e.Name.LocalName == "ResourceDictionary" && (string?)e.Attribute(X + "Key") == "HighContrast");
        return hc.Elements().Select(e => (string?)e.Attribute(X + "Key")).Where(k => k is not null).Select(k => k!).ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> Sources(string pattern)
        => Directory.EnumerateFiles(Path.Combine(RepoPaths.RequireWindowsRoot(), "src"), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    [TestMethod]
    public void EveryStyleTheCodeAsksFor_IsInRavenXaml()
    {
        HashSet<string> styles = TopLevelKeys("Raven.xaml", stylesOnly: true);
        // Every string the code names a Raven style with: Ui.Style("..."), Ui.Text(text, "..."),
        // the style tables (TickView.MetaStyle...), interpolated ones ($"RavenChip{family}Style").
        var literal = new Regex("(\\$?)\"(Raven[A-Za-z0-9{}.()]*Style)\"", RegexOptions.Compiled);
        var call = new Regex("Ui\\.Style\\(\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        var missing = new List<string>();
        int checkedCount = 0;
        foreach (string file in Sources("*.cs"))
        {
            string text = File.ReadAllText(file);
            foreach (Match m in literal.Matches(text))
            {
                string name = m.Groups[2].Value;
                checkedCount++;
                if (m.Groups[1].Value.Length == 0)
                {
                    if (!styles.Contains(name)) missing.Add($"{name} ({Path.GetFileName(file)})");
                    continue;
                }
                // An interpolated name: its fixed parts must match some style.
                var shape = new Regex("^" + Regex.Replace(Regex.Escape(name), "\\\\\\{[^}]*}", "\\w+") + "$");
                if (!styles.Any(s => shape.IsMatch(s))) missing.Add($"{name} ({Path.GetFileName(file)})");
            }
            foreach (Match m in call.Matches(text))
            {
                string name = m.Groups[1].Value;
                if (!name.StartsWith("Raven", StringComparison.Ordinal) && !WinUiKeys.Contains(name)) missing.Add($"{name} ({Path.GetFileName(file)})");
            }
        }
        Assert.IsTrue(checkedCount > 100, "the scan found the code's styles");
        Assert.AreEqual(0, missing.Count, "Styles missing from Themes/Raven.xaml: " + string.Join(", ", missing));
    }

    [TestMethod]
    public void EveryFamilyStyle_IsThereForEachPathFamily()
    {
        HashSet<string> styles = TopLevelKeys("Raven.xaml", stylesOnly: true);
        foreach (string family in Enum.GetNames(typeof(Raven.Windows.Core.Presentation.PathFamily)))
        {
            foreach (string pattern in new[] { "RavenChip{0}Style", "RavenChip{0}TextStyle", "RavenChip{0}IconStyle", "RavenPill{0}Style", "RavenWayTile{0}Style", "RavenWayTile{0}IconStyle" })
            {
                string key = string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, family);
                Assert.IsTrue(styles.Contains(key), key);
            }
        }
        foreach (Raven.Windows.Core.Presentation.SettingsSections.Section section in Raven.Windows.Core.Presentation.SettingsSections.All)
            Assert.IsTrue(styles.Contains($"RavenSectionTile{section.Tile}Style"), section.Tile);
        foreach (string tone in new[] { "Accent", "Success", "Danger" })
        {
            Assert.IsTrue(styles.Contains($"RavenLockTile{tone}Style"), tone);
            Assert.IsTrue(styles.Contains($"RavenLockPart{tone}Style"), tone);
            Assert.IsTrue(styles.Contains($"RavenLockDot{tone}Style"), tone);
        }
        for (int i = 0; i < 5; i++)
        {
            Assert.IsTrue(styles.Contains($"RavenAvatar{i}Style"));
            Assert.IsTrue(styles.Contains($"RavenAvatarText{i}Style"));
        }
    }

    /// <summary>A ThemeResource missing from one theme would fail only in that theme (or in high contrast).</summary>
    [TestMethod]
    public void EveryThemeBrush_IsInEveryTheme()
    {
        HashSet<string> light = TopLevelKeys("Light.xaml", stylesOnly: false);
        HashSet<string> dark = TopLevelKeys("Dark.xaml", stylesOnly: false);
        HashSet<string> hc = HighContrastKeys();
        var use = new Regex("\\{ThemeResource (Raven\\w+)\\}", RegexOptions.Compiled);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in Sources("*.xaml"))
        {
            foreach (Match m in use.Matches(File.ReadAllText(file)))
            {
                string key = m.Groups[1].Value;
                if (!light.Contains(key)) missing.Add(key + " (Light)");
                if (!dark.Contains(key)) missing.Add(key + " (Dark)");
                if (!hc.Contains(key)) missing.Add(key + " (HighContrast)");
            }
        }
        Assert.AreEqual(0, missing.Count, string.Join(", ", missing));
    }

    /// <summary>The light and dark files define the same keys (one theme can't lack what the other has).</summary>
    [TestMethod]
    public void LightAndDark_HaveTheSameKeys()
    {
        HashSet<string> light = TopLevelKeys("Light.xaml", stylesOnly: false);
        HashSet<string> dark = TopLevelKeys("Dark.xaml", stylesOnly: false);
        // SystemAccentColor's shades differ by name between the themes (Dark1..3 / Light1..3).
        light.RemoveWhere(k => k.StartsWith("SystemAccentColor", StringComparison.Ordinal));
        dark.RemoveWhere(k => k.StartsWith("SystemAccentColor", StringComparison.Ordinal));
        CollectionAssert.AreEquivalent(light.ToList(), dark.ToList());
    }
}
