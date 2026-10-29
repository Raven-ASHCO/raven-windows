using System.Collections.Generic;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Presentation;

namespace Raven.Windows.Core.Words;

/// <summary>Outcome of one "Check" press in the Check words dialog.</summary>
public enum CheckOutcome
{
    /// <summary>Nothing checked yet.</summary>
    None,
    /// <summary>The 10 words (or the old code) are the friend's; the engine saved the friend as verified.</summary>
    Match,
    /// <summary>The check word caught a misheard or swapped word: read again.</summary>
    Typo,
    /// <summary>Real Raven words (or a code), but not the friend's: do not verify.</summary>
    Mismatch,
    /// <summary>Too many mismatches: Check is no longer offered.</summary>
    Stopped,
}

/// <summary>
/// State of the Check words dialog, without any UI. It only holds what the user typed:
/// it never knows the friend's words, marks no word right or wrong, and gives a result
/// only when the engine has compared all 10 words (security-words design, "Blind entry").
/// <para>
/// For a friend with an older Raven the dialog can switch to the old letters-and-digits
/// code (<see cref="OldCodeMode"/>): one plain text, no per-character marks, and only match
/// or mismatch at the end (verify.check_old_code). Mismatches of both kinds count towards the
/// same limit.
/// </para>
/// </summary>
public sealed class CheckWordsSession
{
    public const int WordCount = 10;

    /// <summary>After this many mismatches the dialog stops offering Check (blind-entry rule 4).</summary>
    public const int MaxMismatches = 3;

    private readonly List<string> _words = new();

    /// <summary>Words typed so far, each already completed by the engine (neutral chips).</summary>
    public IReadOnlyList<string> Words => _words;

    public int Mismatches { get; private set; }

    public CheckOutcome Outcome { get; private set; } = CheckOutcome.None;

    /// <summary>True while the dialog checks the old code instead of the 10 words.</summary>
    public bool OldCodeMode { get; private set; }

    /// <summary>The old code as typed (sent to the engine as it is; the engine normalises it).</summary>
    public string OldCode { get; private set; } = string.Empty;

    public bool IsComplete => _words.Count == WordCount;

    /// <summary>True once the friend is verified or Check was withdrawn (nothing more to type or check).</summary>
    public bool IsFinal => Outcome is CheckOutcome.Match or CheckOutcome.Stopped;

    public bool CanType => !IsComplete && !IsFinal;

    public bool CanCheck => !IsFinal && (OldCodeMode ? OldCode.Trim().Length > 0 : IsComplete);

    /// <summary>The intro above the check for the current mode (string key).</summary>
    public string IntroKey => IntroKeyFor(OldCodeMode);

    /// <summary>How to compare: the 10 words ("check.body") or the old code ("check.oldCode.intro").</summary>
    public static string IntroKeyFor(bool oldCode) => oldCode ? "check.oldCode.intro" : "check.body";

    /// <summary>
    /// Switches between the 10 words and the old code. The typed words and code are kept;
    /// a shown result is cleared (it belonged to the other mode).
    /// </summary>
    public void SetOldCodeMode(bool oldCode)
    {
        if (IsFinal || OldCodeMode == oldCode) return;
        OldCodeMode = oldCode;
        ClearResultAfterEdit();
    }

    /// <summary>The old code field changed.</summary>
    public void SetOldCode(string? text)
    {
        if (IsFinal) return;
        string value = text ?? string.Empty;
        if (value == OldCode) return;
        OldCode = value;
        ClearResultAfterEdit();
    }

    /// <summary>Adds a completed word; false when 10 words are already there.</summary>
    public bool TryAdd(string word)
    {
        if (!CanType || string.IsNullOrWhiteSpace(word)) return false;
        _words.Add(word);
        ClearResultAfterEdit();
        return true;
    }

    /// <summary>Removes the last word (Backspace in an empty field).</summary>
    public bool RemoveLast()
    {
        if (_words.Count == 0 || IsFinal) return false;
        _words.RemoveAt(_words.Count - 1);
        ClearResultAfterEdit();
        return true;
    }

    /// <summary>Starts again with no words (after "read the words again").</summary>
    public void Clear()
    {
        if (IsFinal) return;
        _words.Clear();
        ClearResultAfterEdit();
    }

    /// <summary>
    /// Records the engine's answer: "match" | "typo" | "mismatch" for the words,
    /// "match" | "mismatch" for the old code.
    /// </summary>
    public CheckOutcome Record(string engineResult)
    {
        if (OldCodeMode)
        {
            // The old code has no check word, so there is no "typo": anything but a match is a mismatch.
            Outcome = engineResult == CheckResults.Match ? CheckOutcome.Match : CountMismatch();
            return Outcome;
        }

        Outcome = engineResult switch
        {
            CheckResults.Match => CheckOutcome.Match,
            CheckResults.Typo => CheckOutcome.Typo,
            CheckResults.Mismatch => CountMismatch(),
            _ => CheckOutcome.Typo, // an answer this app does not know is never treated as a match
        };
        return Outcome;
    }

    /// <summary>The string keys to show for the current outcome and mode, and their tone.</summary>
    public (string[] Keys, Tone Tone) CurrentResultTexts() => ResultTexts(Outcome, OldCodeMode);

    /// <summary>The string keys to show for <paramref name="outcome"/> of the 10 words, and their tone.</summary>
    public static (string[] Keys, Tone Tone) ResultTexts(CheckOutcome outcome) => ResultTexts(outcome, oldCode: false);

    /// <summary>The string keys to show for <paramref name="outcome"/>, and their tone.</summary>
    public static (string[] Keys, Tone Tone) ResultTexts(CheckOutcome outcome, bool oldCode)
    {
        string mismatch = oldCode ? "check.oldCode.mismatch" : "check.mismatch";
        return outcome switch
        {
            CheckOutcome.Match => (new[] { "check.match", "check.match.save" }, Tone.Success),
            CheckOutcome.Typo => (new[] { "check.typo", "check.typo.action" }, Tone.Warning),
            CheckOutcome.Mismatch => (new[] { mismatch }, Tone.Danger),
            CheckOutcome.Stopped => (new[] { mismatch, "check.stop" }, Tone.Danger),
            _ => (System.Array.Empty<string>(), Tone.Secondary),
        };
    }

    private CheckOutcome CountMismatch() => ++Mismatches >= MaxMismatches ? CheckOutcome.Stopped : CheckOutcome.Mismatch;

    private void ClearResultAfterEdit()
    {
        if (Outcome is CheckOutcome.Typo or CheckOutcome.Mismatch) Outcome = CheckOutcome.None;
    }
}
