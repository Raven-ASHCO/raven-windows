using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Tests;

/// <summary>
/// The Calm words and families: path chips and their colours, tick icons, the sidebar's lines,
/// the away card (only what the engine says), the Settings sections and this app's own preferences.
/// </summary>
[TestClass]
public sealed class CalmPresentationTests
{
    private const long Now = 1_790_000_000_000;
    private const long Hour = 3_600_000;

    private static Message Msg(string state, string direction = Directions.Out) => new()
    {
        Id = "m1", Address = "rvn1qfriend", Direction = direction, Text = "hi", AtMs = Now - 60_000, State = state,
    };

    [TestMethod]
    public void PathFamilies_FollowTheTokensPathRule()
    {
        Assert.AreEqual(PathFamily.Lan, CalmPath.Family(PathKinds.Lan));
        Assert.AreEqual(PathFamily.Nearby, CalmPath.Family(PathKinds.Nearby));
        Assert.AreEqual(PathFamily.Tor, CalmPath.Family(PathKinds.Onion));
        Assert.AreEqual(PathFamily.Tor, CalmPath.FamilyOfWay("onion"));
        foreach (string direct in new[] { PathKinds.Internet, PathKinds.P2pHolePunched, PathKinds.WebSocket })
            Assert.AreEqual(PathFamily.Direct, CalmPath.Family(direct), direct);
        foreach (string relay in new[] { PathKinds.P2p, PathKinds.P2pDirect, PathKinds.P2pReversed, PathKinds.P2pRelayed })
            Assert.AreEqual(PathFamily.Relay, CalmPath.Family(relay), relay);
        foreach (string friend in new[] { PathKinds.Friend, PathKinds.Handoff, PathKinds.Letter })
            Assert.AreEqual(PathFamily.Friend, CalmPath.Family(friend), friend);
        Assert.AreEqual(PathFamily.Other, CalmPath.Family(null));
        Assert.AreEqual(PathFamily.Other, CalmPath.Family("a_new_kind"));
    }

    /// <summary>Every path kind this app knows is one the engine reports (API.md, Message path_kind), "onion" included.</summary>
    [TestMethod]
    public void EveryPathKind_IsInTheContract()
    {
        string api = File.ReadAllText(Path.Combine(RepoPaths.RequireWindowsRoot(), "core", "desktop", "api", "API.md"));
        foreach (FieldInfo field in typeof(PathKinds).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            string kind = (string)field.GetValue(null)!;
            StringAssert.Contains(api, kind + " \"", kind);
        }
        StringAssert.Contains(api, "onion \"via Tor\"");
    }

    [TestMethod]
    public void Ways_TakeTheirFamilyColour()
    {
        Assert.AreEqual(PathFamily.Lan, CalmPath.FamilyOfWay("lan"));
        Assert.AreEqual(PathFamily.Nearby, CalmPath.FamilyOfWay("nearby"));
        Assert.AreEqual(PathFamily.Direct, CalmPath.FamilyOfWay("internet"));
        Assert.AreEqual(PathFamily.Relay, CalmPath.FamilyOfWay("p2p"));
        Assert.AreEqual(PathFamily.Friend, CalmPath.FamilyOfWay("custody"));
        Assert.AreEqual(PathFamily.Other, CalmPath.FamilyOfWay("link_knock"));
    }

    [TestMethod]
    public void Chips_SayTheCalmWords_OrTheEnginesWithTechnicalDetails()
    {
        Assert.AreEqual(Loc.Get("chip.lan"), CalmPath.Chip(PathKinds.Lan, "LAN direct"));
        Assert.AreEqual(Loc.Get("chip.nearby"), CalmPath.Chip(PathKinds.Nearby, "Bluetooth nearby"));
        Assert.AreEqual(Loc.Get("chip.friend"), CalmPath.Chip(PathKinds.Friend, "Held by a friend"));
        Assert.AreEqual(Loc.Get("chip.internet"), CalmPath.Chip(PathKinds.Internet, "Internet direct"));
        Assert.AreEqual(Loc.Get("chip.tor"), CalmPath.Chip(PathKinds.Onion, "via Tor"));
        // The rest: the short words the other screens use, or the engine's words for a kind this app doesn't know.
        Assert.AreEqual(Loc.Get("network.path.websocket"), CalmPath.Chip(PathKinds.WebSocket, "p2p · relayed over WebSocket"));
        Assert.AreEqual("Carrier pigeon", CalmPath.Chip("pigeon", "Carrier pigeon"));
        Assert.AreEqual(string.Empty, CalmPath.Chip(null, null));
        // Show technical details: the engine's exact words.
        Assert.AreEqual("p2p · relayed over WebSocket", CalmPath.Chip(PathKinds.WebSocket, "p2p · relayed over WebSocket", technical: true));
        Assert.AreEqual(Loc.Get("chip.lan"), CalmPath.Chip(PathKinds.Lan, null, technical: true), "no words: the chip");
    }

    [TestMethod]
    public void RouteLabel_SaysThePathAndWhatItMeans()
    {
        Assert.AreEqual(Loc.Get("chip.lan") + " · " + Loc.Get("route.note.lan"), CalmPath.RouteLabel(PathKinds.Lan, "LAN direct"));
        Assert.AreEqual(Loc.Get("network.path.p2p"), CalmPath.RouteLabel(PathKinds.P2p, "p2p"));
        Assert.AreEqual(string.Empty, CalmPath.RouteLabel(null, null));
        foreach (string kind in new[] { PathKinds.Nearby, PathKinds.Onion, PathKinds.Internet, PathKinds.P2pRelayed, PathKinds.Friend })
            StringAssert.Contains(CalmPath.RouteLabel(kind, null), " · ", kind);
    }

    [TestMethod]
    public void Ticks_HaveTheirCalmIconAndLabel()
    {
        TickPresenter.Tick sent = TickPresenter.For(Msg(MessageStates.Sent), "Ali", Now);
        Assert.AreEqual(TickIcon.Sent, sent.Icon);
        Assert.AreEqual(Loc.Get("tick.sent"), sent.Label, "the icon replaces the ✓ glyph");
        TickPresenter.Tick delivered = TickPresenter.For(Msg(MessageStates.Delivered), "Ali", Now);
        Assert.AreEqual(TickIcon.Delivered, delivered.Icon);
        Assert.AreEqual(Loc.Get("tick.delivered"), delivered.Label);
        Assert.AreEqual(Tone.Success, delivered.Tone);
        Assert.AreEqual(TickIcon.Sending, TickPresenter.For(Msg(MessageStates.Sending), "Ali", Now).Icon);
        TickPresenter.Tick waiting = TickPresenter.For(Msg(MessageStates.Waiting), "Ali", Now);
        Assert.AreEqual(TickIcon.Waiting, waiting.Icon);
        Assert.AreEqual(waiting.Text, waiting.Label, "no glyph to drop");
        Assert.AreEqual(TickIcon.Ended, TickPresenter.For(Msg(MessageStates.TimeRanOut), "Ali", Now).Icon);
        Assert.AreEqual(TickIcon.Ended, TickPresenter.For(Msg(MessageStates.NotSentMeetFirst), "Ali", Now).Icon);
        Assert.AreEqual(TickIcon.Failed, TickPresenter.For(Msg(MessageStates.Failed), "Ali", Now).Icon);
        Assert.AreEqual(TickIcon.None, TickPresenter.For(Msg("a_new_state"), "Ali", Now).Icon);
    }

    [TestMethod]
    public void WaysOn_CountsWhatTheBackgroundProgramSays()
    {
        Assert.IsNull(NavPresenter.WaysOn(null));
        Assert.IsNull(NavPresenter.WaysOn(new NodeStatus { Running = false }));
        var release = new NodeStatus { Running = true, Carriers = new Carriers { Lan = "on" } };
        Assert.AreEqual(Loc.Get("nav.wayOn"), NavPresenter.WaysOn(release));
        var lab = new NodeStatus { Running = true, Carriers = new Carriers { Lan = "on", Internet = "on", P2p = "on", Custody = "preview", Nearby = "on" } };
        Assert.AreEqual(Loc.Format("nav.waysOn", ("n", 4)), NavPresenter.WaysOn(lab));
    }

    [TestMethod]
    public void Heartbeat_SaysOkOnlyWhileTheBackgroundProgramAnswers()
    {
        var node = new NodeStatus { Running = true, Listening = "0.0.0.0:7420" };
        (string text, bool ok) = NavPresenter.Heartbeat(node, nodeDown: false, lastOkMs: Now, nowMs: Now);
        Assert.IsTrue(ok);
        Assert.AreEqual(Loc.Format("home.heartbeat.ok", ("when", RelativeTime.SinceCheck(Now, Now))), text);
        Assert.IsFalse(NavPresenter.Heartbeat(node, nodeDown: true, Now, Now).Ok);
        Assert.AreEqual(Loc.Get("home.heartbeat.down"), NavPresenter.Heartbeat(null, false, 0, Now).Text);
        Assert.AreEqual(Loc.Get("net.starting"), NavPresenter.Heartbeat(new NodeStatus { Running = true }, false, 0, Now).Text);
    }

    [TestMethod]
    public void AwayCard_ShowsOnlyWhileAMessageWaits()
    {
        var contact = new Contact { Address = "rvn1qfriend", Name = "Mina", Verified = true, Presence = Presences.Waiting };
        Assert.IsNull(AwayPresenter.For(contact, new[] { Msg(MessageStates.Delivered) }, Now));

        Message waiting = Msg(MessageStates.Waiting);
        waiting.UntilMs = Now + 22 * Hour;
        AwayPresenter.Card? card = AwayPresenter.For(contact, new[] { waiting }, Now);
        Assert.IsNotNull(card);
        Assert.AreEqual(Loc.Format("away.title", ("name", "Mina")), card!.Value.Title);
        Assert.AreEqual(Loc.Format("away.body", ("time", RelativeTime.Deadline(Now + 22 * Hour, Now)), ("name", "Mina")), card.Value.Body);
        Assert.AreEqual(1, card.Value.Count);
        Assert.IsNull(card.Value.Held, "nobody holds it: no friend named");
        Assert.AreEqual("m1", card.Value.OldestId);

        // A friend holds a sealed copy only when the engine's path kind says so.
        waiting.PathKind = PathKinds.Friend;
        Assert.AreEqual(Loc.Format("away.held", ("name", "Mina")), AwayPresenter.For(contact, new[] { waiting }, Now)!.Value.Held);

        // The engine's count, and no deadline known.
        contact.Waiting = new WaitingInfo { Count = 2, UntilMs = 0 };
        Message noTime = Msg(MessageStates.Waiting);
        AwayPresenter.Card many = AwayPresenter.For(contact, new[] { noTime }, Now)!.Value;
        Assert.AreEqual(2, many.Count, "the engine's count");
        Assert.AreEqual(Loc.Format("away.body.noTime", ("name", "Mina")), many.Body);
    }

    [TestMethod]
    public void Settings_HasSevenSectionsInTheCalmOrder()
    {
        CollectionAssert.AreEqual(
            new[] { "general", "appearance", "privacy", "lock", "network", "updates", "about" },
            SettingsSections.All.Select(s => s.Id).ToArray());
        foreach (SettingsSections.Section s in SettingsSections.All)
        {
            Assert.IsTrue(Loc.Has(s.TitleKey), s.TitleKey);
            Assert.AreEqual(1, s.Glyph.Length, s.Id);
        }
        Assert.AreEqual(3, SettingsSections.IndexOf(SettingsSections.Lock));
        Assert.AreEqual(-1, SettingsSections.IndexOf("nope"));
    }

    [TestMethod]
    public void AppPrefs_RoundTripAndSurviveABrokenFile()
    {
        var prefs = new AppPrefs { Theme = ThemeChoice.Dark, ShowTechnical = true };
        AppPrefs back = AppPrefs.Parse(prefs.ToJson());
        Assert.AreEqual(ThemeChoice.Dark, back.Theme);
        Assert.IsTrue(back.ShowTechnical);
        Assert.AreEqual(ThemeChoice.Light, AppPrefs.Parse("{\"theme\":\"light\"}").Theme);
        foreach (string? broken in new[] { null, "", "not json", "[1,2]", "{\"theme\":42}", "{\"theme\":\"purple\"}" })
        {
            AppPrefs p = AppPrefs.Parse(broken);
            Assert.AreEqual(ThemeChoice.System, p.Theme, broken ?? "null");
            Assert.IsFalse(p.ShowTechnical);
        }
    }
}
