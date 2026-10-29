using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Tests;

/// <summary>The notification-area menu matches the macOS and Linux menus; the icon breathes only while listening.</summary>
[TestClass]
public sealed class TrayMenuTests
{
    private const long Now = 1_790_000_000_000;

    private static NodeStatus Listening(string? address = "0.0.0.0:7420") =>
        new() { Running = true, Receive = true, Listening = address };

    private static IReadOnlyList<(Contact Contact, int Unread)> Friends() => new List<(Contact, int)>
    {
        (new Contact { Address = "rvn1qali", Name = "Ali", Verified = true, Presence = Presences.InTouch, LastContactMs = Now - 5 * 60_000 }, 0),
        (new Contact { Address = "rvn1qrena", Name = "Rena", Verified = false, Presence = Presences.None }, 2),
    };

    [TestMethod]
    public void Menu_ListsStatusFriendsAndActions_InTheSharedOrder()
    {
        IReadOnlyList<TrayMenuItem> items = TrayMenu.Build(Listening(), nodeDown: false, ready: true, Friends(), privacyAvailable: false, Now);
        // The first line asks this computer whether it is on Wi-Fi (a CI runner is on a cable):
        // StatusLines_SayWhatRavenKnows covers both wordings.
        string listening = TrayMenu.StatusLine(Listening());
        CollectionAssert.Contains(new[] { "Raven is listening on Wi-Fi", "Raven is listening on your network" }, listening);
        CollectionAssert.AreEqual(new[]
        {
            listening,
            "♥ background program ok",
            "Ali · In touch 5 min ago",
            "2 new from Rena",
            "Open Raven",
            "Add a friend…",
            "Lock now",
            "Quit window (keeps receiving)",
            "Quit and stop the background program",
        }, items.Select(i => i.Text).ToArray());

        // Status lines are not clickable; friends open their chat; Lock waits for the privacy pack.
        Assert.IsNull(items[0].Command);
        Assert.IsFalse(items[0].Enabled);
        Assert.IsNull(items[1].Command);
        Assert.AreEqual(TrayCommand.OpenFriend, items[2].Command);
        Assert.AreEqual("rvn1qali", items[2].Address);
        Assert.IsTrue(items[2].SeparatorBefore, "a line between the status and the friends");
        Assert.IsFalse(items[3].SeparatorBefore);
        Assert.AreEqual("rvn1qrena", items[3].Address);
        Assert.IsTrue(items[4].SeparatorBefore);
        Assert.AreEqual(TrayCommand.Lock, items[6].Command);
        Assert.IsFalse(items[6].Enabled, "Lock now is greyed out until privacy.state says available");
        Assert.IsTrue(items[7].SeparatorBefore);
        Assert.AreEqual(TrayCommand.QuitAll, items[8].Command);
    }

    [TestMethod]
    public void Menu_BeforeSetup_OffersOpenAndQuitOnly()
    {
        IReadOnlyList<TrayMenuItem> items = TrayMenu.Build(null, nodeDown: false, ready: false, Friends(), privacyAvailable: true, Now);
        Assert.AreEqual(Loc.Get("tray.notListening"), items[0].Text);
        Assert.AreEqual(Loc.Get("home.heartbeat.down"), items[1].Text);
        Assert.IsFalse(items.Any(i => i.Command == TrayCommand.OpenFriend), "no friends before the main screen");
        CollectionAssert.AreEqual(
            new TrayCommand?[] { TrayCommand.Open, TrayCommand.QuitWindow, TrayCommand.QuitAll },
            items.Where(i => i.Enabled && i.Command is not null).Select(i => i.Command).ToArray());
    }

    [TestMethod]
    public void Menu_LockWorksOnceThePrivacyPackIsThere()
    {
        IReadOnlyList<TrayMenuItem> items = TrayMenu.Build(Listening(), nodeDown: false, ready: true, Friends(), privacyAvailable: true, Now);
        Assert.IsTrue(items.Single(i => i.Command == TrayCommand.Lock).Enabled);
    }

    [TestMethod]
    public void StatusLines_SayWhatRavenKnows()
    {
        Assert.AreEqual("Raven is listening on Wi-Fi", TrayMenu.StatusLine(Listening(), wifi: true));
        Assert.AreEqual("Raven is listening on your network", TrayMenu.StatusLine(Listening(), wifi: false), "a cable is not Wi-Fi (E1)");
        Assert.AreEqual("Raven isn't receiving", TrayMenu.StatusLine(Listening("127.0.0.1:7420")), "this computer only");
        Assert.AreEqual("Raven isn't receiving", TrayMenu.StatusLine(Listening(null)));
        Assert.AreEqual("Raven isn't receiving", TrayMenu.StatusLine(null));
        IReadOnlyList<TrayMenuItem> down = TrayMenu.Build(Listening(), nodeDown: true, ready: true, Friends(), false, Now);
        Assert.AreEqual(Loc.Get("home.heartbeat.down"), down[1].Text);
    }

    [TestMethod]
    public void FriendLine_IsTheMacOSLine()
    {
        var mina = new Contact { Name = "Mina", Verified = true, Waiting = new WaitingInfo { Count = 1, UntilMs = Now + 3_600_000 } };
        StringAssert.StartsWith(TrayMenu.FriendLine(mina, 0, Now), "Mina · 1 waiting · until ");
        Assert.AreEqual("1 new from Mina", TrayMenu.FriendLine(mina, 1, Now));
    }

    [TestMethod]
    public void Icon_BreathesOnlyWhileListening_AndNeverWithMotionOff()
    {
        Assert.IsTrue(TrayBreath.ShouldBreathe(Listening(), motionEnabled: true));
        Assert.IsFalse(TrayBreath.ShouldBreathe(Listening(), motionEnabled: false), "Animation effects off or Reduced motion on");
        Assert.IsFalse(TrayBreath.ShouldBreathe(Listening(null), motionEnabled: true), "starting, not listening yet");
        Assert.IsFalse(TrayBreath.ShouldBreathe(null, motionEnabled: true));

        // 100%, 80%, 60%, 80%, then again; frame 0 when still.
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 1, 0, 1 }, Enumerable.Range(0, 6).Select(s => TrayBreath.FrameAt(s, breathing: true)).ToArray());
        Assert.AreEqual(0, TrayBreath.FrameAt(2, breathing: false));
        Assert.AreEqual(1000, TrayBreath.StepMs, "one step per second, like the macOS menu bar mark");
    }

    [TestMethod]
    public void BreathingFrames_AreInTheAssets()
    {
        string assets = Path.Combine(RepoPaths.RequireWindowsRoot(), "src", "Raven.Windows", "Assets");
        foreach (string file in TrayBreath.FrameFiles)
            Assert.IsTrue(File.Exists(Path.Combine(assets, file)), file);
        Assert.AreEqual("RavenTray.ico", TrayBreath.FrameFiles[0], "frame 0 is the normal icon");
    }
}
