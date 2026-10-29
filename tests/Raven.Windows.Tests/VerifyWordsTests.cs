using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;
using Raven.Windows.Core.Words;
#if DEBUG
using System.IO;
using Raven.Windows.Core.Mock;
#endif

namespace Raven.Windows.Tests;

/// <summary>Security-word checks: match / typo / mismatch, completion after 3 letters, blind entry.</summary>
[TestClass]
public sealed class VerifyWordsTests
{
#if DEBUG
    private static readonly string[] MyWords = { "turtle", "frog", "bread", "wolf", "paper", "laptop", "funnel", "mango", "honey", "wagon" };

    private static MockRaven NewEngine() => new(ManualTimeProvider.StartingAt2026(), startReady: true, demoEvents: false);

    [TestMethod]
    public async Task TheFriendsWords_Match_AndVerifyTheFriend()
    {
        MockRaven engine = NewEngine();
        IReadOnlyList<string> renaWords = engine.FriendWordsForTests(MockRaven.RenaAddress);

        Assert.AreEqual(CheckResults.Match, await engine.VerifyCheckWordsAsync(MockRaven.RenaAddress, renaWords));
        Assert.IsTrue((await engine.ContactsListAsync()).Single(c => c.Address == MockRaven.RenaAddress).Verified);
    }

    [TestMethod]
    public async Task TypedForms_ThatResolveToTheSameWords_StillMatch()
    {
        MockRaven engine = NewEngine();
        // Upper case, 3-letter prefixes and plurals are the same words.
        List<string> typed = engine.FriendWordsForTests(MockRaven.AliAddress)
            .Select((w, i) => i % 3 == 0 ? w.ToUpperInvariant() : i % 3 == 1 ? w[..3] : w + "s")
            .ToList();
        Assert.AreEqual(CheckResults.Match, await engine.VerifyCheckWordsAsync(MockRaven.AliAddress, typed));
    }

    [TestMethod]
    public async Task OneMisheardWord_IsATypo_CaughtByTheCheckWord()
    {
        MockRaven engine = NewEngine();
        List<string> words = engine.FriendWordsForTests(MockRaven.AliAddress).ToList();
        for (int position = 0; position < 10; position++)
        {
            var typed = new List<string>(words);
            typed[position] = SecurityWords.List.First(w => w != words[position]);
            Assert.AreEqual(CheckResults.Typo, await engine.VerifyCheckWordsAsync(MockRaven.AliAddress, typed), $"word {position + 1}");
        }
        Assert.IsTrue((await engine.ContactsListAsync()).Single(c => c.Address == MockRaven.AliAddress).Verified, "Ali stays as he was");
    }

    [TestMethod]
    public async Task TwoSwappedWords_AreATypo()
    {
        MockRaven engine = NewEngine();
        List<string> typed = engine.FriendWordsForTests(MockRaven.RenaAddress).ToList();
        if (typed[3] == typed[4]) Assert.Inconclusive("demo words repeat at 4/5");
        (typed[3], typed[4]) = (typed[4], typed[3]);
        Assert.AreEqual(CheckResults.Typo, await engine.VerifyCheckWordsAsync(MockRaven.RenaAddress, typed));
        Assert.IsFalse((await engine.ContactsListAsync()).Single(c => c.Address == MockRaven.RenaAddress).Verified);
    }

    [TestMethod]
    public async Task AnotherKeysWords_AreAMismatch()
    {
        MockRaven engine = NewEngine();
        // My own words are a valid set (the check word fits) but belong to another key.
        Assert.AreEqual(CheckResults.Mismatch, await engine.VerifyCheckWordsAsync(MockRaven.RenaAddress, MyWords));
        Assert.IsFalse((await engine.ContactsListAsync()).Single(c => c.Address == MockRaven.RenaAddress).Verified);
    }

    [TestMethod]
    public async Task NotARavenWord_DoesNotFit()
    {
        MockRaven engine = NewEngine();
        List<string> typed = engine.FriendWordsForTests(MockRaven.RenaAddress).ToList();
        typed[2] = "qqqq";
        Assert.AreEqual(CheckResults.Typo, await engine.VerifyCheckWordsAsync(MockRaven.RenaAddress, typed));
    }

    [TestMethod]
    public async Task FewerThanTenWords_AreRefused()
    {
        MockRaven engine = NewEngine();
        RavenException ex = await Assert.ThrowsExactlyAsync<RavenException>(
            () => engine.VerifyCheckWordsAsync(MockRaven.RenaAddress, MyWords.Take(9).ToList()));
        Assert.AreEqual(RavenErrorCodes.WordsIncomplete, ex.Code);
    }

    // ---------------------------------------------------------------- the old code (verify.check_old_code)

    [TestMethod]
    public async Task OldCode_OfTheFriend_Matches_AndVerifiesTheFriend()
    {
        MockRaven engine = NewEngine();
        Assert.AreEqual(CheckResults.Match, await engine.VerifyCheckOldCodeAsync(MockRaven.RenaAddress, MockRaven.RenaOldCode));
        Assert.IsTrue((await engine.ContactsListAsync()).Single(c => c.Address == MockRaven.RenaAddress).Verified);
    }

    [TestMethod]
    public async Task OldCode_IgnoresSpacesSeparatorsAndForeignDigits_ButNotCase()
    {
        MockRaven engine = NewEngine();
        // Persian 7, Arabic-Indic 2 and 9, a full-width 4, spaces and an en dash.
        Assert.AreEqual(CheckResults.Match,
            await engine.VerifyCheckOldCodeAsync(MockRaven.RenaAddress, " Tq\u06F7m \u0662\u0669Xa\u2013hKe\uFF14 "));

        MockRaven fresh = NewEngine();
        Assert.AreEqual(CheckResults.Mismatch, await fresh.VerifyCheckOldCodeAsync(MockRaven.RenaAddress, "tq7m-29xa-hke4"), "the code is case-sensitive");
        Assert.IsFalse((await fresh.ContactsListAsync()).Single(c => c.Address == MockRaven.RenaAddress).Verified, "a mismatch verifies nothing");
        Assert.AreEqual("Tq7m29XahKe4", MockRaven.NormalizeOldCode("Tq7m\u200B-29Xa hKe4"));
    }

    [TestMethod]
    public async Task OldCode_IsShownInGroupsOfFour_AndTheGroupedFormMatches()
    {
        Assert.AreEqual("If4x 36FU omFi", WordEntry.GroupOldCode(MockRaven.MyOldCode));
        Assert.AreEqual("If4x 36FU omFi", WordEntry.GroupOldCode("If4x36FUomFi"));
        Assert.AreEqual("If4x 36FU omFi", WordEntry.GroupOldCode(" If4x - 36FU\u200BomFi "));
        Assert.AreEqual("Tq7m 29Xa hKe", WordEntry.GroupOldCode("Tq7m-29Xa-hKe"));
        Assert.AreEqual(string.Empty, WordEntry.GroupOldCode(null));
        Assert.AreEqual("If4x36FUomFi", WordEntry.OldCodeKey(WordEntry.GroupOldCode(MockRaven.MyOldCode)), "case is kept");

        // A friend types the code as it is shown (groups and spaces): still a match.
        MockRaven engine = NewEngine();
        Assert.AreEqual(CheckResults.Match,
            await engine.VerifyCheckOldCodeAsync(MockRaven.RenaAddress, WordEntry.GroupOldCode(MockRaven.RenaOldCode)));
    }

    [TestMethod]
    public async Task OldCode_OfSomeoneElse_IsAMismatch()
    {
        MockRaven engine = NewEngine();
        Assert.AreEqual(CheckResults.Mismatch, await engine.VerifyCheckOldCodeAsync(MockRaven.RenaAddress, MockRaven.MyOldCode));
        Assert.AreEqual(CheckResults.Mismatch, await engine.VerifyCheckOldCodeAsync(MockRaven.AliAddress, MockRaven.RenaOldCode),
            "the mock knows no old code for Ali");
    }

    [TestMethod]
    public async Task OldCode_BadArguments()
    {
        MockRaven engine = NewEngine();
        RavenException empty = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.VerifyCheckOldCodeAsync(MockRaven.RenaAddress, " - "));
        Assert.AreEqual(RavenErrorCodes.BadRequest, empty.Code);
        RavenException missing = await Assert.ThrowsExactlyAsync<RavenException>(
            () => engine.CallAsync(RavenOps.VerifyCheckOldCode, new JsonObject { ["code"] = MockRaven.RenaOldCode }));
        Assert.AreEqual(RavenErrorCodes.BadRequest, missing.Code);
        RavenException stranger = await Assert.ThrowsExactlyAsync<RavenException>(
            () => engine.VerifyCheckOldCodeAsync("rvn1qnosuchfriend00000000000000000", MockRaven.RenaOldCode));
        Assert.AreEqual(RavenErrorCodes.NotAContact, stranger.Code);
    }

    [TestMethod]
    public async Task WordHint_CompletesAfterThreeLetters()
    {
        MockRaven engine = NewEngine();
        Assert.AreEqual("turtle", await engine.VerifyWordHintAsync("tur"));
        Assert.AreEqual("turtle", await engine.VerifyWordHintAsync("TUR"));
        Assert.AreEqual("turtle", await engine.VerifyWordHintAsync("turtles"));
        Assert.AreEqual("turtle", await engine.VerifyWordHintAsync("\uFF54\uFF55\uFF52"), "full-width letters fold to a-z");
        Assert.IsNull(await engine.VerifyWordHintAsync("tu"), "nothing before 3 letters");
        Assert.IsNull(await engine.VerifyWordHintAsync("zzzq"));
        Assert.IsNull(await engine.VerifyWordHintAsync("\u0442\u0443\u0440"), "Cyrillic letters are not a Raven word");
    }

    [TestMethod]
    public void DerivedWords_MatchTheDesignVector()
    {
        byte[] alice = Convert.FromHexString("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        CollectionAssert.AreEqual(new byte[] { 241, 80, 25, 253, 153, 114, 81, 125, 98, 248 }, SecurityWords.DeriveIndexes(alice));
        CollectionAssert.AreEqual(MyWords, SecurityWords.WordsFor(alice));
    }

    [TestMethod]
    public void CheckIndex_MatchesTheDesignVectors()
    {
        Assert.AreEqual(0, SecurityWords.CheckIndex(new byte[9]));
        Assert.AreEqual(169, SecurityWords.CheckIndex(Enumerable.Repeat((byte)255, 9).ToArray()));
        Assert.AreEqual(44, SecurityWords.CheckIndex(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }));
        Assert.AreEqual(248, SecurityWords.CheckIndex(new byte[] { 241, 80, 25, 253, 153, 114, 81, 125, 98 }));
    }

    [TestMethod]
    public void WordList_IsThePinnedDraftList()
    {
        using Stream stream = typeof(SecurityWords).Assembly.GetManifestResourceStream(SecurityWords.ResourceName)!;
        string sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        // shared-vectors draft wordlist_v1.json "sha256"
        Assert.AreEqual("560cbd462be90ca14769d06c52a0e4afa23ae60152ee880ead38d76fd61ef145", sha);
        Assert.AreEqual(256, SecurityWords.List.Count);
        Assert.AreEqual(256, SecurityWords.List.Select(w => w[..3]).Distinct().Count(), "unique 3-letter prefixes");
    }
#endif

    // ---------------------------------------------------------------- the dialog's state (all builds)

    [TestMethod]
    public void OldCodeMode_HasItsOwnIntro()
    {
        var session = new CheckWordsSession();
        Assert.AreEqual("check.body", session.IntroKey);
        session.SetOldCodeMode(true);
        Assert.AreEqual("check.oldCode.intro", session.IntroKey);
        string intro = Loc.Format(session.IntroKey, ("name", "Rena"));
        StringAssert.Contains(intro, "old code");
        StringAssert.Contains(intro, "Rena");
        Assert.IsFalse(intro.Contains("your own words", StringComparison.Ordinal));
        session.SetOldCodeMode(false);
        Assert.AreEqual("check.body", session.IntroKey);
    }

    [TestMethod]
    public void Session_GivesAResultOnlyForTenWords()
    {
        var session = new CheckWordsSession();
        for (int i = 0; i < 9; i++) Assert.IsTrue(session.TryAdd("word" + i));
        Assert.IsFalse(session.CanCheck);
        Assert.IsTrue(session.TryAdd("word9"));
        Assert.IsTrue(session.CanCheck);
        Assert.IsFalse(session.TryAdd("eleven"), "a set has 10 words");
        Assert.AreEqual(CheckOutcome.None, session.Outcome);
    }

    [TestMethod]
    public void Session_MapsEngineResults()
    {
        var session = FullSession();
        Assert.AreEqual(CheckOutcome.Typo, session.Record(CheckResults.Typo));
        Assert.AreEqual(CheckOutcome.Mismatch, session.Record(CheckResults.Mismatch));
        Assert.AreEqual(CheckOutcome.Typo, session.Record("something-new"), "an unknown answer is never a match");
        Assert.AreEqual(CheckOutcome.Match, session.Record(CheckResults.Match));
        Assert.IsFalse(session.CanCheck);
        Assert.IsFalse(session.RemoveLast(), "a verified set is final");
    }

    [TestMethod]
    public void Session_StopsAfterThreeMismatches()
    {
        var session = FullSession();
        Assert.AreEqual(CheckOutcome.Mismatch, session.Record(CheckResults.Mismatch));
        Assert.AreEqual(CheckOutcome.Mismatch, session.Record(CheckResults.Mismatch));
        Assert.AreEqual(CheckOutcome.Stopped, session.Record(CheckResults.Mismatch));
        Assert.IsFalse(session.CanCheck);
        Assert.IsFalse(session.CanType);
    }

    [TestMethod]
    public void Session_EditingClearsTheLastResult()
    {
        var session = FullSession();
        session.Record(CheckResults.Typo);
        Assert.IsTrue(session.RemoveLast());
        Assert.AreEqual(CheckOutcome.None, session.Outcome);
        Assert.AreEqual(9, session.Words.Count);
    }

    [TestMethod]
    public void ResultTexts_Exist()
    {
        foreach (CheckOutcome outcome in Enum.GetValues<CheckOutcome>())
        {
            foreach (bool oldCode in new[] { false, true })
            {
                foreach (string key in CheckWordsSession.ResultTexts(outcome, oldCode).Keys)
                    Assert.IsTrue(Loc.Has(key), key);
            }
        }
        CollectionAssert.AreEqual(new[] { "check.match", "check.match.save" }, CheckWordsSession.ResultTexts(CheckOutcome.Match).Keys);
        CollectionAssert.AreEqual(new[] { "check.typo", "check.typo.action" }, CheckWordsSession.ResultTexts(CheckOutcome.Typo).Keys);
        CollectionAssert.AreEqual(new[] { "check.mismatch" }, CheckWordsSession.ResultTexts(CheckOutcome.Mismatch).Keys);
        CollectionAssert.AreEqual(new[] { "check.match", "check.match.save" }, CheckWordsSession.ResultTexts(CheckOutcome.Match, oldCode: true).Keys);
        CollectionAssert.AreEqual(new[] { "check.oldCode.mismatch" }, CheckWordsSession.ResultTexts(CheckOutcome.Mismatch, oldCode: true).Keys);
        CollectionAssert.AreEqual(new[] { "check.oldCode.mismatch", "check.stop" }, CheckWordsSession.ResultTexts(CheckOutcome.Stopped, oldCode: true).Keys);
    }

    [TestMethod]
    public void Session_OldCode_IsOneFieldWithMatchOrMismatchOnly()
    {
        var session = new CheckWordsSession();
        session.TryAdd("turtle");
        session.SetOldCodeMode(true);
        Assert.IsFalse(session.CanCheck, "an empty code cannot be checked");
        session.SetOldCode("   ");
        Assert.IsFalse(session.CanCheck);
        session.SetOldCode("Tq7m 29Xa hKe4");
        Assert.IsTrue(session.CanCheck, "the code needs no 10 words");

        Assert.AreEqual(CheckOutcome.Mismatch, session.Record(CheckResults.Typo), "the old code has no typo result");
        CollectionAssert.AreEqual(new[] { "check.oldCode.mismatch" }, session.CurrentResultTexts().Keys);
        session.SetOldCode("Tq7m 29Xa hKe5");
        Assert.AreEqual(CheckOutcome.None, session.Outcome, "editing clears the last result");

        Assert.AreEqual(CheckOutcome.Mismatch, session.Record(CheckResults.Mismatch));
        session.SetOldCodeMode(false);
        Assert.AreEqual(CheckOutcome.None, session.Outcome, "switching clears the result");
        Assert.AreEqual(1, session.Words.Count, "the typed words are kept");
        session.SetOldCodeMode(true);
        Assert.AreEqual("Tq7m 29Xa hKe5", session.OldCode, "the typed code is kept");

        Assert.AreEqual(CheckOutcome.Stopped, session.Record(CheckResults.Mismatch), "words and code share the limit of 3");
        Assert.IsFalse(session.CanCheck);
        session.SetOldCodeMode(false);
        Assert.IsTrue(session.OldCodeMode, "nothing changes after Check was withdrawn");
    }

    [TestMethod]
    public void Session_OldCodeMatch_IsFinal()
    {
        var session = new CheckWordsSession();
        session.SetOldCodeMode(true);
        session.SetOldCode("Tq7m-29Xa-hKe4");
        Assert.AreEqual(CheckOutcome.Match, session.Record(CheckResults.Match));
        Assert.IsTrue(session.IsFinal);
        Assert.IsFalse(session.CanCheck);
        session.SetOldCode("other");
        Assert.AreEqual("Tq7m-29Xa-hKe4", session.OldCode);
    }

    // ---------------------------------------------------------------- typed input (all builds)

    [TestMethod]
    public void Entry_DropsPositionNumbersAndSplitsOnPunctuation()
    {
        CollectionAssert.AreEqual(new[] { "turtle", "frog", "bread" }, WordEntry.SplitTokens("1. turtle 2. frog, 3) bread").ToList());
        CollectionAssert.AreEqual(new[] { "turtle", "frog" }, WordEntry.SplitTokens("\u06F1 turtle \u0662 frog").ToList(), "Persian and Arabic-Indic digits");
        CollectionAssert.AreEqual(new[] { "turtle", "frog" }, WordEntry.SplitTokens("turtle\u3000frog\u2014").ToList());
        Assert.AreEqual(0, WordEntry.SplitTokens("  1 2 3 ").Count);
    }

    [TestMethod]
    public void Entry_FoldsLatinAndDropsInvisibleCharacters()
    {
        Assert.AreEqual("turtle", WordEntry.FoldLatin("TUR\u200BTLE"));
        Assert.AreEqual("tur", WordEntry.FoldLatin("\uFF54\uFF55\uFF52"));
        Assert.IsNull(WordEntry.FoldLatin("\u0442\u0443\u0440"));
        Assert.IsNull(WordEntry.FoldLatin("caf\u00E9"), "accents are not folded");
    }

    [TestMethod]
    public void Entry_SeparatesCompleteWordsFromTheOneBeingTyped()
    {
        (IReadOnlyList<string> complete, string partial) = WordEntry.SplitForEntry("turtle frog br");
        CollectionAssert.AreEqual(new[] { "turtle", "frog" }, complete.ToList());
        Assert.AreEqual("br", partial);

        (complete, partial) = WordEntry.SplitForEntry("turtle ");
        CollectionAssert.AreEqual(new[] { "turtle" }, complete.ToList());
        Assert.AreEqual(string.Empty, partial);

        Assert.AreEqual("tur", WordEntry.Prefix("Tur"));
        Assert.IsNull(WordEntry.Prefix("12"), "a position number");
    }

    private static CheckWordsSession FullSession()
    {
        var session = new CheckWordsSession();
        for (int i = 0; i < 10; i++) session.TryAdd("word" + i);
        return session;
    }
}
