using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Tests;

/// <summary>Text from the engine cannot reorder, hide or draw over the UI.</summary>
[TestClass]
public sealed class SanitizerTests
{
    [TestMethod]
    public void PlainText_IsUnchanged()
    {
        Assert.AreEqual("Are you coming to the meeting?", TextSanitizer.Clean("Are you coming to the meeting?"));
        // Latin with accents, CJK ideographs, an emoji with a skin-tone modifier.
        const string mixed = "na\u00EFve caf\u00E9, \u4E00\u4E8C\u4E09, \U0001F44D\U0001F3FD";
        Assert.AreEqual(mixed, TextSanitizer.Clean(mixed));
        const string arabicLetters = "\u0627\u0628\u062A"; // alef, beh, teh
        Assert.AreEqual(arabicLetters, TextSanitizer.Clean(arabicLetters), "right-to-left letters stay; only bidi controls go");
        Assert.AreEqual(string.Empty, TextSanitizer.Clean(null));
    }

    [TestMethod]
    [DataRow("Ali\u202Egnp.exe", "Alignp.exe", DisplayName = "right-to-left override")]
    [DataRow("a\u202Ab\u202Bc\u202Cd\u202De", "abcde", DisplayName = "embeddings and pop")]
    [DataRow("a\u2066b\u2067c\u2068d\u2069e", "abcde", DisplayName = "isolates")]
    [DataRow("a\u200Eb\u200Fc\u061Cd", "abcd", DisplayName = "LRM, RLM, ALM")]
    [DataRow("a\u200Bb\u200Cc\u200Dd\u2060e\uFEFFf", "abcdef", DisplayName = "zero-width characters")]
    [DataRow("so\u00ADft", "soft", DisplayName = "soft hyphen")]
    [DataRow("x\u0000y\u0007z\u001Bw\u007Fv\u0085u", "xyzwvu", DisplayName = "C0, DEL and C1 controls")]
    [DataRow("a\u3164b\u115Fc", "abc", DisplayName = "Hangul fillers")]
    public void InvisibleAndControlCharacters_AreRemoved(string input, string expected)
    {
        Assert.AreEqual(expected, TextSanitizer.Clean(input));
    }

    [TestMethod]
    public void TagCharacters_AreRemoved()
    {
        Assert.AreEqual("hi", TextSanitizer.Clean("h\U000E0041\U000E0042i"));
    }

    [TestMethod]
    public void Newlines_AreNormalised_OrFlattenedForSingleLines()
    {
        Assert.AreEqual("one\ntwo\nthree\nfour", TextSanitizer.Clean("one\r\ntwo\rthree\u2028four"));
        Assert.AreEqual("one two", TextSanitizer.SingleLine("  one\ntwo  "));
        Assert.AreEqual("a b", TextSanitizer.Clean("a\tb"));
    }

    [TestMethod]
    public void StackedMarks_AreCut()
    {
        string zalgo = "e" + new string('\u0301', 20);
        Assert.AreEqual("e" + new string('\u0301', TextSanitizer.MaxCombiningMarks), TextSanitizer.Clean(zalgo));
        Assert.AreEqual("e\u0301\u0302", TextSanitizer.Clean("e\u0301\u0302"), "a few marks are normal text");
    }

    [TestMethod]
    public void BrokenUtf16_BecomesReplacementCharacter()
    {
        Assert.AreEqual("a\uFFFDb", TextSanitizer.Clean("a\uD800b"));
    }
}
