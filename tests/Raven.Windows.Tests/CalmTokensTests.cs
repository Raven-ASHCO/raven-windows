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

/// <summary>
/// The Calm design (tokens.json version 2) as the Windows app consumes it: every radius, layout
/// size and type size has its XAML resource in Themes/Raven.xaml, and every Calm duration and
/// curve its C# constant (<see cref="CalmMs"/>, <see cref="MotionCurves"/>).
/// </summary>
[TestClass]
public sealed class CalmTokensTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static JsonDocument Tokens() => JsonDocument.Parse(File.ReadAllText(RepoPaths.DesignFile("tokens.json")));

    private static Dictionary<string, string> Raven()
    {
        string path = Path.Combine(RepoPaths.RequireWindowsRoot(), "src", "Raven.Windows", "Themes", "Raven.xaml");
        return XDocument.Load(path).Root!.Elements()
            .Where(e => e.Attribute(X + "Key") is not null && !e.HasElements)
            .ToDictionary(e => e.Attribute(X + "Key")!.Value, e => e.Value.Trim(), StringComparer.Ordinal);
    }

    private static string Pascal(string name) => char.ToUpperInvariant(name[0]) + name[1..];

    private static double Number(string xaml) => double.Parse(xaml.Split(',')[0], CultureInfo.InvariantCulture);

    [TestMethod]
    public void TokensAreVersionTwo()
    {
        using JsonDocument tokens = Tokens();
        Assert.AreEqual(2, tokens.RootElement.GetProperty("version").GetInt32());
    }

    /// <summary>radius.X is the XAML CornerRadius RavenXRadius (bubble and bubbleTail make the two bubble corners).</summary>
    [TestMethod]
    public void EveryRadius_HasItsResource()
    {
        Dictionary<string, string> xaml = Raven();
        using JsonDocument tokens = Tokens();
        int count = 0;
        foreach (JsonProperty r in tokens.RootElement.GetProperty("radius").EnumerateObject())
        {
            if (r.Name is "bubble" or "bubbleTail") continue; // RavenBubbleIn/OutRadius (ThemeTokensTests)
            string key = "Raven" + Pascal(r.Name) + "Radius";
            Assert.IsTrue(xaml.ContainsKey(key), key);
            Assert.AreEqual(r.Value.GetDouble(), Number(xaml[key]), key);
            count++;
        }
        Assert.IsTrue(count >= 12);
    }

    [TestMethod]
    public void EveryLayoutSize_HasItsResource()
    {
        Dictionary<string, string> xaml = Raven();
        using JsonDocument tokens = Tokens();
        var names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sidebarWidth"] = "RavenSidebarWidth",
            ["sidebarMinWidth"] = "RavenSidebarMinWidth",
            ["settingsNavWidth"] = "RavenSettingsNavWidth",
            ["headerHeight"] = "RavenHeaderHeight",
            ["routeLineHeight"] = "RavenRouteLineHeight",
            ["avatarSize"] = "RavenAvatarSize",
            ["avatarSmall"] = "RavenAvatarSmall",
            ["avatarLarge"] = "RavenAvatarLarge",
            ["presenceDot"] = "RavenPresenceDotSize",
            ["contentMaxWidth"] = "RavenContentMaxWidth",
            ["pageMaxWidth"] = "RavenPageMaxWidth",
            ["settingsMaxWidth"] = "RavenSettingsMaxWidth",
            ["bubbleMaxFraction"] = "RavenBubbleMaxFraction",
            ["chipHeight"] = "RavenChipHeight",
            ["pillHeight"] = "RavenPillHeight",
            ["buttonHeight"] = "RavenButtonHeight",
            ["sendButton"] = "RavenSendButtonSize",
            ["groupRing"] = "RavenGroupRingSize",
        };
        JsonElement layout = tokens.RootElement.GetProperty("layout");
        foreach (JsonProperty p in layout.EnumerateObject())
        {
            switch (p.Name)
            {
                case "minWindow":
                    Assert.AreEqual(p.Value.GetProperty("width").GetDouble(), Number(xaml["RavenMinWindowWidth"]));
                    Assert.AreEqual(p.Value.GetProperty("height").GetDouble(), Number(xaml["RavenMinWindowHeight"]));
                    continue;
                case "switch":
                    Assert.AreEqual(p.Value.GetProperty("width").GetDouble(), Number(xaml["RavenSwitchWidth"]));
                    Assert.AreEqual(p.Value.GetProperty("height").GetDouble(), Number(xaml["RavenSwitchHeight"]));
                    Assert.AreEqual(p.Value.GetProperty("knob").GetDouble(), Number(xaml["RavenSwitchKnobSize"]));
                    continue;
            }
            Assert.IsTrue(names.TryGetValue(p.Name, out string? key), "tokens.json layout." + p.Name + " has no XAML resource");
            Assert.AreEqual(p.Value.GetDouble(), Number(xaml[key!]), key);
        }
    }

    [TestMethod]
    public void CalmTypeRamp_HasItsResources()
    {
        Dictionary<string, string> xaml = Raven();
        using JsonDocument tokens = Tokens();
        JsonElement type = tokens.RootElement.GetProperty("typography");
        var sizes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bubble"] = "RavenBubbleFontSize",
            ["label"] = "RavenLabelFontSize",
            ["caption"] = "RavenCaptionFontSize",
            ["meta"] = "RavenMetaFontSize",
            ["sectionLabel"] = "RavenSectionLabelFontSize",
            ["brand"] = "RavenBrandFontSize",
            ["pageTitle"] = "RavenPageTitleFontSize",
        };
        foreach ((string token, string key) in sizes)
            Assert.AreEqual(type.GetProperty(token).GetProperty("size").GetDouble(), Number(xaml[key]), key);

        // Line heights in XAML are points: size x lineHeight.
        JsonElement bubble = type.GetProperty("bubble");
        Assert.AreEqual(bubble.GetProperty("size").GetDouble() * bubble.GetProperty("lineHeight").GetDouble(),
            Number(xaml["RavenBubbleLineHeight"]), 0.01);
        // Tracking in em becomes XAML CharacterSpacing in 1/1000 em.
        Assert.AreEqual((int)Math.Round(type.GetProperty("sectionLabel").GetProperty("tracking").GetDouble() * 1000),
            int.Parse(xaml["RavenSectionLabelCharacterSpacing"], CultureInfo.InvariantCulture));
        Assert.AreEqual((int)Math.Round(type.GetProperty("pageTitle").GetProperty("tracking").GetDouble() * 1000),
            int.Parse(xaml["RavenPageTitleCharacterSpacing"], CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void CalmDurations_MatchTokens()
    {
        using JsonDocument tokens = Tokens();
        JsonElement calm = tokens.RootElement.GetProperty("motion").GetProperty("calmMs");
        int count = 0;
        foreach (JsonProperty d in calm.EnumerateObject())
        {
            FieldInfo? field = typeof(CalmMs).GetField(Pascal(d.Name), BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(field, $"CalmMs.{Pascal(d.Name)} is missing");
            Assert.AreEqual(d.Value.GetInt32(), (int)field!.GetValue(null)!, d.Name);
            count++;
        }
        Assert.AreEqual(typeof(CalmMs).GetFields(BindingFlags.Public | BindingFlags.Static).Length, count, "no extra CalmMs constants");
    }

    [TestMethod]
    public void CalmCurves_MatchTokens()
    {
        using JsonDocument tokens = Tokens();
        JsonElement curves = tokens.RootElement.GetProperty("motion").GetProperty("curves");
        int count = 0;
        foreach (JsonProperty c in curves.EnumerateObject())
        {
            if (c.Value.ValueKind != JsonValueKind.Array) continue; // the note
            Assert.IsTrue(MotionCurves.ByName.TryGetValue(c.Name, out CubicBezier curve), c.Name);
            double[] v = c.Value.EnumerateArray().Select(e => e.GetDouble()).ToArray();
            Assert.AreEqual(new CubicBezier(v[0], v[1], v[2], v[3]), curve, c.Name);
            count++;
        }
        Assert.AreEqual(MotionCurves.ByName.Count, count);
    }

    [TestMethod]
    public void Curves_StartAtZeroAndEndAtOne()
    {
        foreach ((string name, CubicBezier curve) in MotionCurves.ByName)
        {
            Assert.AreEqual(0, curve.Evaluate(0), 1e-9, name);
            Assert.AreEqual(1, curve.Evaluate(1), 1e-9, name);
            Assert.AreEqual(0, curve.Evaluate(-0.5), 1e-9, name);
            Assert.AreEqual(1, curve.Evaluate(1.5), 1e-9, name);
        }
    }

    /// <summary>The springy curves (y beyond 1) overshoot; ease curves never do.</summary>
    [TestMethod]
    public void SpringyCurves_Overshoot()
    {
        Assert.IsTrue(MotionCurves.Lift.Peak() > 1.0, "lift");
        Assert.IsTrue(MotionCurves.Pop.Peak() > 1.05, "pop");
        Assert.IsTrue(MotionCurves.Knob.Peak() > 1.02, "knob");
        Assert.IsTrue(MotionCurves.Glide.Peak() > 1.0, "glide");
        Assert.AreEqual(1.0, MotionCurves.Arrive.Peak(), 1e-6, "arrive");
        Assert.AreEqual(1.0, MotionCurves.Packet.Peak(), 1e-6, "packet");
        Assert.AreEqual(1.0, MotionCurves.Leave.Peak(), 1e-6, "leave");
    }

    [TestMethod]
    public void Ease_MatchesTheKnownCssValues()
    {
        // CSS "ease" is cubic-bezier(.25, .1, .25, 1): at half the time it is about 80 % there.
        var ease = new CubicBezier(0.25, 0.1, 0.25, 1);
        Assert.AreEqual(0.8024, ease.Evaluate(0.5), 0.002);
        // linear
        var linear = new CubicBezier(0, 0, 1, 1);
        Assert.AreEqual(0.3, linear.Evaluate(0.3), 1e-4);
        // ease-in (leave) is slow at first
        Assert.IsTrue(MotionCurves.Leave.Evaluate(0.2) < 0.1);
    }

    [TestMethod]
    public void Samples_AreEvenInTimeAndEndAtOne()
    {
        IReadOnlyList<(double Time, double Progress)> points = MotionCurves.Lift.Sample(MotionCurves.StepsFor(CalmMs.BubbleLift));
        Assert.AreEqual(MotionCurves.StepsFor(CalmMs.BubbleLift) + 1, points.Count);
        Assert.AreEqual(0, points[0].Time);
        Assert.AreEqual(1, points[^1].Time);
        Assert.AreEqual(1, points[^1].Progress, 1e-9);
        for (int i = 1; i < points.Count; i++) Assert.IsTrue(points[i].Time > points[i - 1].Time);
        Assert.AreEqual(8, MotionCurves.StepsFor(50));
        Assert.AreEqual(48, MotionCurves.StepsFor(5000));
    }

    [TestMethod]
    public void SettingsSections_SlideInTheDirectionOfTravel()
    {
        Assert.AreEqual(1, SlideDirection.Between(0, 3));
        Assert.AreEqual(-1, SlideDirection.Between(4, 1));
        Assert.AreEqual(0, SlideDirection.Between(2, 2));
        Assert.AreEqual(0, SlideDirection.Between(-1, 2), "the first section shown doesn't slide");
    }
}
