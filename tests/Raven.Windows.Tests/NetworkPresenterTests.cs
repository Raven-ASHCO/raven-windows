using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Tests;

/// <summary>
/// The Network screens' small rules: state chips, the path chip on a message from its
/// path_kind, ports for the firewall note, the Windows nearby note and the consent texts.
/// </summary>
[TestClass]
public sealed class NetworkPresenterTests
{
    [TestMethod]
    public void StateChips_SayEveryWayState()
    {
        Assert.AreEqual(new Chip(Loc.Get("net.on"), Tone.Success), NetworkPresenter.StateChip(WayStates.On));
        Assert.AreEqual(new Chip(Loc.Get("net.wayOff"), Tone.Secondary), NetworkPresenter.StateChip(WayStates.Off));
        Assert.AreEqual(new Chip(Loc.Get("net.preview"), Tone.Warning), NetworkPresenter.StateChip(WayStates.Preview));
        Assert.AreEqual(new Chip(Loc.Get("net.lab"), Tone.Accent), NetworkPresenter.StateChip(WayStates.Lab));
        Assert.AreEqual(new Chip(Loc.Get("net.notOnThisOs"), Tone.Secondary), NetworkPresenter.StateChip(WayStates.NotOnThisOs));
        // A state a newer engine knows: its own word, never a made-up one.
        Assert.AreEqual(new Chip("paused", Tone.Secondary), NetworkPresenter.StateChip("paused"));
    }

    [TestMethod]
    public void EveryWayAndProtection_HasAPlainLine()
    {
        string[] ids =
        {
            "lan", "lan_beacon", "internet", "port_mapping", "p2p", "friend_relay", "websocket", "proxy", "custody",
            "letters", "handoff", "route_hints", "nearby", "random_port", "link_knock", "link_padding", "lock_v2",
            "security_events", "updates_from_friends",
        };
        foreach (string id in ids)
        {
            string line = NetworkPresenter.WhatItDoes(id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(line), id);
            Assert.IsFalse(line.StartsWith('['), id + ": " + line);
        }
        Assert.AreEqual(string.Empty, NetworkPresenter.WhatItDoes("a_newer_way"));
    }

    [TestMethod]
    public void Switches_ChangeTheRightSetting()
    {
        Assert.AreEqual(NetworkSettings.FriendsCarryMine, NetworkPresenter.SettingFor("custody"));
        Assert.AreEqual(NetworkSettings.RelayForFriends, NetworkPresenter.SettingFor("friend_relay"));
        Assert.AreEqual(NetworkSettings.Proxy, NetworkPresenter.SettingFor("proxy"));
        Assert.IsNull(NetworkPresenter.SettingFor("letters"), "letters run by themselves");
        Assert.IsNull(NetworkPresenter.SettingFor("websocket"));
    }

    [TestMethod]
    public void PathChips_ComeFromPathKind()
    {
        Assert.AreEqual("LAN", PathPresenter.Chip(PathKinds.Lan, "LAN direct"));
        Assert.AreEqual("Internet", PathPresenter.Chip(PathKinds.Internet, "Internet direct"));
        Assert.AreEqual("Relay", PathPresenter.Chip(PathKinds.P2pRelayed, "p2p · relayed"));
        Assert.AreEqual("Relay · 443", PathPresenter.Chip(PathKinds.WebSocket, "p2p · relayed over WebSocket"));
        Assert.AreEqual("Friend", PathPresenter.Chip(PathKinds.Friend, "Held by a friend"));
        Assert.AreEqual("Handoff", PathPresenter.Chip(PathKinds.Handoff, "Passed on between friends"));
        Assert.AreEqual("Letter", PathPresenter.Chip(PathKinds.Letter, "Sealed letter with a friend"));
        Assert.AreEqual("Nearby", PathPresenter.Chip(PathKinds.Nearby, "Bluetooth nearby"));

        // Every kind API.md lists has a chip of its own.
        foreach (FieldInfo field in typeof(PathKinds).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            string kind = (string)field.GetValue(null)!;
            string chip = PathPresenter.Chip(kind, null);
            Assert.IsFalse(string.IsNullOrWhiteSpace(chip), kind);
            Assert.IsFalse(chip.StartsWith('['), kind);
        }

        // An older engine without path_kind, or a newer kind: the engine's words.
        Assert.AreEqual("LAN direct", PathPresenter.Chip(null, "LAN direct"));
        Assert.AreEqual("Carrier pigeon", PathPresenter.Chip("pigeon", "Carrier pigeon"));
        Assert.AreEqual(string.Empty, PathPresenter.Chip(null, null));

        Assert.AreEqual("Delivery path: LAN direct", PathPresenter.AccessibleName(PathKinds.Lan, "LAN direct"));
        Assert.AreEqual("Delivery path: Friend", PathPresenter.AccessibleName(PathKinds.Friend, null));
        Assert.AreEqual(string.Empty, PathPresenter.AccessibleName(null, null));
    }

    [TestMethod]
    public void Ports_AreReadFromListenSettings()
    {
        Assert.AreEqual("7422", NetworkPresenter.PortOf("0.0.0.0:7422"));
        Assert.AreEqual("7423", NetworkPresenter.PortOf("7423"));
        Assert.AreEqual("9001", NetworkPresenter.PortOf("[::]:9001"));
        Assert.AreEqual("7500", NetworkPresenter.PortOf("/ip4/0.0.0.0/tcp/7500"));
        Assert.AreEqual("7501", NetworkPresenter.PortOf("/ip4/0.0.0.0/udp/7501/quic-v1"));
        Assert.IsNull(NetworkPresenter.PortOf(null));
        Assert.IsNull(NetworkPresenter.PortOf("auto"));
        Assert.IsNull(NetworkPresenter.PortOf("0.0.0.0:123456"));
    }

    [TestMethod]
    public void FirewallNote_NamesThePortsOnlyWhileInternetOrP2pIsOn()
    {
        NetworkStatus off = Status("""{"build":"dev","carriers":[{"id":"internet","state":"off","setting":false},{"id":"p2p","state":"off","setting":false}]}""");
        Assert.AreEqual(0, NetworkPresenter.FirewallLines(off).Count);

        NetworkStatus on = Status("""
            {"build":"dev","internet_listen":"0.0.0.0:9000","p2p_listen":"7500",
             "carriers":[{"id":"internet","state":"lab","setting":true},{"id":"p2p","state":"lab","setting":true}]}
            """);
        var lines = NetworkPresenter.FirewallLines(on);
        Assert.AreEqual(Loc.Get("network.firewall.prompt"), lines[0]);
        CollectionAssert.Contains(lines.ToList(), "Internet direct: TCP port 9000");
        CollectionAssert.Contains(lines.ToList(), "Peer-to-peer: TCP and UDP port 7500");
        Assert.AreEqual(Loc.Get("network.firewall.manual"), lines[lines.Count - 1]);

        // p2p alone, on its default port.
        NetworkStatus p2p = Status("""{"build":"dev","carriers":[{"id":"p2p","state":"lab","setting":true}]}""");
        CollectionAssert.Contains(NetworkPresenter.FirewallLines(p2p).ToList(), "Peer-to-peer: TCP and UDP port 7423");
        Assert.IsFalse(NetworkPresenter.FirewallLines(p2p).Any(l => l.StartsWith("Internet direct", System.StringComparison.Ordinal)));
    }

    [TestMethod]
    public void NearbyNote_SaysWindowsKeepsItInactive()
    {
        NetworkStatus dev = Status("""{"build":"dev","nearby":{"state":"live","on_this_os":true,"address_privacy":"public"}}""");
        Assert.AreEqual(Loc.Get("network.nearby.windows"), NetworkPresenter.WindowsNearbyNote(dev));
        // The lab switch's name is for developers: Technical details only (E1).
        Assert.AreEqual(Loc.Get("network.nearby.windowsLab"), NetworkPresenter.WindowsNearbyLab(dev));

        NetworkStatus release = Status("""{"build":"release","nearby":{"state":"held","on_this_os":true}}""");
        Assert.AreEqual(Loc.Get("network.nearby.windows"), NetworkPresenter.WindowsNearbyNote(release), "no lab hint in a release build");
        Assert.IsNull(NetworkPresenter.WindowsNearbyLab(release));

        // Once the background program reports a random address (row N7w settled), the note goes.
        NetworkStatus random = Status("""{"build":"dev","nearby":{"state":"live","on_this_os":true,"address_privacy":"random"}}""");
        Assert.IsNull(NetworkPresenter.WindowsNearbyNote(random));
    }

    [TestMethod]
    public void ConsentText_DropsTheTerminalsQuestionMark()
    {
        Assert.AreEqual("Use friends as mailboxes?\nThey can't read it.",
            NetworkPresenter.ConsentText("Use friends as mailboxes?\nThey can't read it.\nTurn on? [y/N] "));
        Assert.AreEqual("Hold sealed messages?\n- Only verified friends.",
            NetworkPresenter.ConsentText("Hold sealed messages?\r\n- Only verified friends.\r\n"));
        Assert.AreEqual(string.Empty, NetworkPresenter.ConsentText(null));
    }

    [TestMethod]
    public void ConsentTopics_AreTheEnginesThree()
    {
        foreach (string setting in new[] { NetworkSettings.CarryForFriends, NetworkSettings.FriendsCarryMine, NetworkSettings.Handoff })
        {
            ConsentTopic? topic = ConsentTopics.For(setting);
            Assert.IsNotNull(topic, setting);
            Assert.IsTrue(topic!.EngineNeedsConsent, setting);
            foreach (string text in new[] { topic.Title, topic.Does, topic.Costs, topic.Sees, topic.Hidden })
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(text), setting);
                Assert.IsFalse(text.StartsWith('['), setting + ": " + text);
            }
        }
        // The consent rule (API.md): lending a relay and the bridge show their effect instead.
        Assert.IsNull(ConsentTopics.For(NetworkSettings.RelayForFriends));
        Assert.IsNull(ConsentTopics.For(NetworkSettings.Bridge));
        Assert.IsNull(ConsentTopics.For(NetworkSettings.Store));
        Assert.IsNull(ConsentTopics.For(NetworkSettings.HideIp));
        Assert.IsNull(ConsentTopics.For(NetworkSettings.Lan));
        Assert.AreEqual(Loc.Get("network.consent.generic.title"), ConsentTopics.Generic("future").Title);
    }

    [TestMethod]
    public void Sizes_AreShort()
    {
        Assert.AreEqual("512 bytes", NetworkPresenter.Size(512));
        Assert.AreEqual("4.1 KB", NetworkPresenter.Size(4096));
        Assert.AreEqual("1.5 MB", NetworkPresenter.Size(1_500_000));
    }

    private static NetworkStatus Status(string json)
        => JsonNode.Parse(json)!.Deserialize<NetworkStatus>(RavenJson.Options)!;
}
