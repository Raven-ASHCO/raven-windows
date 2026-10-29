using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Presentation;

namespace Raven.Windows.Tests;

/// <summary>The hand-written XAML theme and motion constants follow desktop/design/tokens.json.</summary>
[TestClass]
public sealed class ThemeTokensTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static JsonDocument Tokens() => JsonDocument.Parse(File.ReadAllText(RepoPaths.DesignFile("tokens.json")));

    private static Dictionary<string, string> XamlValues(string fileName)
    {
        string path = Path.Combine(RepoPaths.RequireWindowsRoot(), "src", "Raven.Windows", "Themes", fileName);
        return XDocument.Load(path).Root!.Elements()
            .Where(e => e.Attribute(X + "Key") is not null && !e.HasElements)
            .ToDictionary(e => e.Attribute(X + "Key")!.Value, e => e.Value.Trim(), StringComparer.Ordinal);
    }

    /// <summary>CSS #RRGGBB / #RRGGBBAA to XAML #AARRGGBB.</summary>
    internal static string ToXamlColor(string css)
    {
        string hex = css.TrimStart('#').ToUpperInvariant();
        return hex.Length switch
        {
            6 => "#FF" + hex,
            8 => "#" + hex[6..] + hex[..6],
            _ => throw new FormatException(css),
        };
    }

    [TestMethod]
    public void ColorConversion_MovesAlphaToTheFront()
    {
        Assert.AreEqual("#FF185FA5", ToXamlColor("#185FA5"));
        Assert.AreEqual("#261F1E1C", ToXamlColor("#1F1E1C26"));
    }

    [TestMethod]
    [DataRow("light", "Light.xaml")]
    [DataRow("dark", "Dark.xaml")]
    public void ThemeColours_MatchTokens(string theme, string fileName)
    {
        Dictionary<string, string> xaml = XamlValues(fileName);
        using JsonDocument tokens = Tokens();
        JsonElement color = tokens.RootElement.GetProperty("color");
        int checkedCount = 0;
        foreach (JsonProperty token in color.EnumerateObject())
        {
            if (token.Value.ValueKind != JsonValueKind.Object) continue;
            string key = "Raven" + char.ToUpperInvariant(token.Name[0]) + token.Name[1..] + "Color";
            Assert.IsTrue(xaml.ContainsKey(key), $"{fileName} lacks {key}");
            Assert.AreEqual(ToXamlColor(token.Value.GetProperty(theme).GetString()!), xaml[key], key);
            checkedCount++;
        }

        JsonElement avatars = color.GetProperty("avatar");
        Assert.AreEqual(AvatarRule.Colors, avatars.GetArrayLength());
        for (int i = 0; i < avatars.GetArrayLength(); i++)
        {
            Assert.AreEqual(ToXamlColor(avatars[i].GetProperty("bg").GetProperty(theme).GetString()!), xaml[$"RavenAvatar{i}BackgroundColor"]);
            Assert.AreEqual(ToXamlColor(avatars[i].GetProperty("fg").GetProperty(theme).GetString()!), xaml[$"RavenAvatar{i}ForegroundColor"]);
            checkedCount += 2;
        }
        Assert.IsTrue(checkedCount >= 27);
    }

    [TestMethod]
    public void SpacingRadiiAndLayout_MatchTokens()
    {
        Dictionary<string, string> xaml = XamlValues("Raven.xaml");
        using JsonDocument tokens = Tokens();
        JsonElement root = tokens.RootElement;

        foreach (JsonProperty space in root.GetProperty("spacing").EnumerateObject())
        {
            string key = "RavenSpace" + char.ToUpperInvariant(space.Name[0]) + space.Name[1..];
            Assert.AreEqual(space.Value.GetInt32().ToString(CultureInfo.InvariantCulture), xaml[key], key);
        }

        JsonElement radius = root.GetProperty("radius");
        Assert.AreEqual(radius.GetProperty("control").GetInt32().ToString(CultureInfo.InvariantCulture), xaml["RavenControlRadius"]);
        Assert.AreEqual(radius.GetProperty("card").GetInt32().ToString(CultureInfo.InvariantCulture), xaml["RavenCardRadius"]);
        Assert.AreEqual(radius.GetProperty("chip").GetInt32().ToString(CultureInfo.InvariantCulture), xaml["RavenChipRadius"]);
        int bubble = radius.GetProperty("bubble").GetInt32();
        int tail = radius.GetProperty("bubbleTail").GetInt32();
        Assert.AreEqual($"{bubble},{bubble},{bubble},{tail}", xaml["RavenBubbleInRadius"]);
        Assert.AreEqual($"{bubble},{bubble},{tail},{bubble}", xaml["RavenBubbleOutRadius"]);

        JsonElement layout = root.GetProperty("layout");
        Assert.AreEqual(layout.GetProperty("sidebarWidth").GetInt32().ToString(CultureInfo.InvariantCulture), xaml["RavenSidebarWidth"]);
        Assert.AreEqual(layout.GetProperty("routeLineHeight").GetInt32().ToString(CultureInfo.InvariantCulture), xaml["RavenRouteLineHeight"]);
        Assert.AreEqual(layout.GetProperty("avatarSize").GetInt32().ToString(CultureInfo.InvariantCulture), xaml["RavenAvatarSize"]);
        Assert.AreEqual(layout.GetProperty("presenceDot").GetInt32().ToString(CultureInfo.InvariantCulture), xaml["RavenPresenceDotSize"]);
        Assert.AreEqual(layout.GetProperty("minWindow").GetProperty("width").GetInt32().ToString(CultureInfo.InvariantCulture), xaml["RavenMinWindowWidth"]);
        Assert.AreEqual(layout.GetProperty("minWindow").GetProperty("height").GetInt32().ToString(CultureInfo.InvariantCulture), xaml["RavenMinWindowHeight"]);

        JsonElement type = root.GetProperty("typography");
        Assert.AreEqual(type.GetProperty("body").GetProperty("size").GetInt32().ToString(CultureInfo.InvariantCulture), xaml["RavenBodyFontSize"]);
        Assert.AreEqual(type.GetProperty("small").GetProperty("size").GetInt32().ToString(CultureInfo.InvariantCulture), xaml["RavenSmallFontSize"]);
        Assert.AreEqual(type.GetProperty("title").GetProperty("size").GetInt32().ToString(CultureInfo.InvariantCulture), xaml["RavenTitleFontSize"]);
        Assert.AreEqual(type.GetProperty("display").GetProperty("size").GetInt32().ToString(CultureInfo.InvariantCulture), xaml["RavenDisplayFontSize"]);
        Assert.AreEqual(type.GetProperty("mono").GetProperty("size").GetInt32().ToString(CultureInfo.InvariantCulture), xaml["RavenMonoFontSize"]);
    }

    [TestMethod]
    public void MotionDurations_MatchTokens()
    {
        using JsonDocument tokens = Tokens();
        JsonElement durations = tokens.RootElement.GetProperty("motion").GetProperty("durationsMs");
        int count = 0;
        foreach (JsonProperty d in durations.EnumerateObject())
        {
            string name = char.ToUpperInvariant(d.Name[0]) + d.Name[1..];
            FieldInfo? field = typeof(MotionTokens).GetField(name, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(field, $"MotionTokens.{name} is missing");
            Assert.AreEqual(d.Value.GetInt32(), (int)field!.GetValue(null)!, name);
            count++;
        }
        Assert.AreEqual(11, count);
    }
}
