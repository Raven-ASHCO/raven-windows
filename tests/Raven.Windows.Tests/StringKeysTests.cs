using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Tests;

/// <summary>
/// Every text comes from desktop/design/strings.en.json: every key the app uses (in C# and
/// in XAML) exists there, and the table loads as expected. There is no fallback table.
/// </summary>
[TestClass]
public sealed class StringKeysTests
{
    private static readonly Regex CsKeyLiteral = new("\"([a-z][A-Za-z0-9]*(?:\\.[A-Za-z0-9]+)+)\"", RegexOptions.Compiled);
    private static readonly Regex XamlKey = new(@"\{h:Str Key=([A-Za-z0-9.]+)\}", RegexOptions.Compiled);

    [TestMethod]
    public void Table_LoadsTextsAndLists()
    {
        Assert.IsTrue(Loc.Keys.Count > 100);
        Assert.AreEqual("Raven", Loc.Get("app.name"));
        Assert.IsFalse(Loc.Has("_note"), "comment keys are skipped");
        Assert.AreEqual(4, Loc.GetArray("settings.keepChats.options").Count);
        Assert.AreEqual(3, Loc.GetArray("settings.whenQuit.options").Count);
        foreach (string key in Loc.Keys) Assert.IsFalse(string.IsNullOrWhiteSpace(Loc.Get(key)), key);
    }

    [TestMethod]
    public void MissingKey_ShowsTheKeyNotInventedText()
    {
        Assert.AreEqual("[no.such.key]", Loc.Get("no.such.key"));
        Assert.AreEqual(0, Loc.GetArray("no.such.list").Count);
    }

    [TestMethod]
    public void Format_ReplacesNamedPlaceholders()
    {
        Assert.AreEqual("In touch 5 min ago", Loc.Format("home.inTouch", ("when", "5 min ago")));
        Assert.AreEqual("2 waiting \u00B7 until Tue 14:05", Loc.Format("home.waiting", ("n", 2), ("time", "Tue 14:05")));
        Assert.AreEqual("Message {name}", Loc.Format("home.composer"), "an unfilled placeholder stays visible");
    }

    [TestMethod]
    public void EveryKeyTheAppUses_ExistsInTheSharedStrings()
    {
        string root = RepoPaths.RequireWindowsRoot();
        var known = new HashSet<string>(Loc.Keys.Concat(Loc.ArrayKeys), StringComparer.Ordinal);
        var prefixes = new HashSet<string>(known.Select(k => k.Split('.')[0]), StringComparer.Ordinal);
        HashSet<string> operations = typeof(RavenOps).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetValue(null)!).ToHashSet(StringComparer.Ordinal);

        var used = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;
            string text;
            IEnumerable<string> keys;
            if (file.EndsWith(".cs", StringComparison.Ordinal))
            {
                text = File.ReadAllText(file);
                keys = CsKeyLiteral.Matches(text).Select(m => m.Groups[1].Value)
                    .Where(k => prefixes.Contains(k.Split('.')[0]) && !operations.Contains(k));
            }
            else if (file.EndsWith(".xaml", StringComparison.Ordinal))
            {
                text = File.ReadAllText(file);
                keys = XamlKey.Matches(text).Select(m => m.Groups[1].Value);
            }
            else
            {
                continue;
            }
            foreach (string key in keys) used[key] = Path.GetFileName(file);
        }

        Assert.IsTrue(used.Count > 100, "the scan found the app's keys");
        string[] unknown = used.Keys.Where(k => !known.Contains(k))
            .Select(k => $"{k} ({used[k]})").ToArray();
        Assert.AreEqual(0, unknown.Length, "Keys used but not in desktop/design/strings.en.json: " + string.Join(", ", unknown));
    }

    [TestMethod]
    public void NoFallbackTable_IsLeft()
    {
        Assert.IsFalse(File.Exists(Path.Combine(RepoPaths.RequireWindowsRoot(), "MissingStrings.md")),
            "every text lives in desktop/design/strings.en.json");
    }

    [TestMethod]
    public void TimeAndErrorTexts_AreThere()
    {
        Assert.AreEqual(7, Loc.GetArray("time.weekdays").Count);
        Assert.AreEqual("Mon", Loc.GetArray("time.weekdays")[0], "the week starts on Monday");
        foreach (string key in new[] { "error.notAvailableYet", "error.badRequest", "error.badResponse", "error.internal", "error.engineMissing" })
            Assert.IsTrue(Loc.Has(key), key);
    }
}
