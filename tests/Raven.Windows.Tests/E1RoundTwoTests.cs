using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Mock;
using Raven.Windows.Core.Presentation;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Tests;

/// <summary>
/// The second round of E1 fixes in the Windows app: the window inside the work area, honest
/// privacy words, day lines, and Send again / Delete on a message that was not sent.
/// </summary>
[TestClass]
public sealed class E1RoundTwoTests
{
    private const long Now = 1_790_000_000_000;

    [TestMethod]
    public void Window_OpensInsideTheWorkArea()
    {
        // win-c: 1300x875 at 224,224 on a 1920x1020 work area put the message box under the taskbar.
        var area = new WindowPlacement.Rect(0, 0, 1920, 1020);
        WindowPlacement.Rect fit = WindowPlacement.Fit(new WindowPlacement.Rect(224, 224, 1300, 875), area, 700, 600);
        Assert.AreEqual(new WindowPlacement.Rect(224, 145, 1300, 875), fit);
        Assert.IsTrue(fit.Y + fit.Height <= area.Y + area.Height);

        // Larger than a small screen: as large as the work area, at its corner.
        WindowPlacement.Rect small = WindowPlacement.Fit(new WindowPlacement.Rect(50, 50, 1300, 875), new WindowPlacement.Rect(0, 0, 1366, 728), 700, 600);
        Assert.AreEqual(new WindowPlacement.Rect(50, 0, 1300, 728), small);

        // A second monitor to the left (negative coordinates) and a window off its edge.
        WindowPlacement.Rect left = WindowPlacement.Fit(new WindowPlacement.Rect(-200, 900, 800, 600), new WindowPlacement.Rect(-1920, 0, 1920, 1040), 700, 600);
        Assert.AreEqual(new WindowPlacement.Rect(-800, 440, 800, 600), left);
    }

    [TestMethod]
    public void Privacy_SaysWhatIsEncrypted()
    {
        var sealedProfile = new PrivacyState { Available = false, Encrypted = true };
        CollectionAssert.AreEqual(new[] { Loc.Get("settings.stored.encrypted"), Loc.Get("settings.stored.plain") },
            PrivacyPresenter.StoredLines(sealedProfile).ToList());
        var plain = new PrivacyState { Available = false, Encrypted = false };
        CollectionAssert.AreEqual(new[] { Loc.Get("settings.stored.notEncrypted") }, PrivacyPresenter.StoredLines(plain).ToList());
    }

    [TestMethod]
    public void Routes_CountTheMailFriendsTheyList()
    {
        var learned = new LearnedRoutes { MailFriends = 0 };
        var carriers = new ContactCarriers { Told = { new FriendRef { Address = "rvn1qb", Name = "mac-b" } } };
        Assert.AreEqual(1, Raven.Windows.Core.ViewModels.ContactRoutesViewModel.MailFriendCount(learned, carriers));
    }

    [TestMethod]
    public void DayLines_SayTodayYesterdayOrTheDate()
    {
        TimeZoneInfo utc = TimeZoneInfo.Utc;
        const long day = 24 * 3_600_000L;
        Assert.AreEqual(Loc.Get("chat.today"), RelativeTime.Day(Now, Now, utc));
        Assert.AreEqual(Loc.Get("chat.yesterday"), RelativeTime.Day(Now - day, Now, utc));
        StringAssert.Contains(RelativeTime.Day(Now - 5 * day, Now, utc), "-");
        Assert.IsTrue(RelativeTime.DifferentDays(Now - 5 * day, Now, utc));
        Assert.IsFalse(RelativeTime.DifferentDays(Now, Now, utc));
    }

    [TestMethod]
    public async Task AMessageThatWasNotSent_CanBeDeleted()
    {
        var engine = new MockRaven(ManualTimeProvider.StartingAt2026(), startReady: true, demoEvents: false);
        Message sent = await engine.MessagesSendAsync(MockRaven.RenaAddress, "Hello");
        Assert.IsFalse(TickPresenter.CanDelete(sent), "on its way: nothing to delete");
        Message stopped = await engine.MessagesStopAsync(sent.Id);
        Assert.IsTrue(TickPresenter.CanDelete(stopped));

        await engine.MessagesDeleteAsync(sent.Id);
        var history = await engine.MessagesHistoryAsync(MockRaven.RenaAddress, null, 100);
        Assert.IsFalse(history.Any(m => m.Id == sent.Id));

        // Anything else is refused (here: a message still on its way).
        Message onItsWay = await engine.MessagesSendAsync(MockRaven.AliAddress, "Hi");
        RavenException ex = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.MessagesDeleteAsync(onItsWay.Id));
        Assert.AreEqual(RavenErrorCodes.BadRequest, ex.Code);
    }
}
