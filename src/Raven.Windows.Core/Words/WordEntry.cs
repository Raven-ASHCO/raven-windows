using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Raven.Windows.Core.Words;

/// <summary>
/// Splits what the user types while a friend reads their security words, following the
/// entry rules of the security-words specification (core/protocol/RAVEN_SECURITY_WORDS_V1.md): Unicode spaces and punctuation separate words, Persian,
/// Arabic-Indic and full-width digits are folded, digit-only tokens (position numbers like
/// "1." or U+06F1) are dropped, zero-width characters inside a word are dropped, and A-Z and
/// full-width letters become a-z. Which word a token is stays the engine's job
/// (verify.word_hint); this class never looks at a friend's words.
/// </summary>
public static class WordEntry
{
    private const string SeparatorChars =
        " \t\r\n\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u202F\u205F\u3000" +
        "-\u2010\u2011\u2012\u2013\u2014\u2015\u2212,;.\u00B7\u2022/|:()[]\"'`";

    private const string InvisibleChars = "\u200B\u200C\u200D\u200E\u200F\u2060\uFEFF";

    public static bool IsSeparator(char c) => SeparatorChars.IndexOf(c) >= 0;

    public static bool IsInvisible(char c) => InvisibleChars.IndexOf(c) >= 0;

    /// <summary>Persian (U+06F0..), Arabic-Indic (U+0660..) and full-width (U+FF10..) digits to ASCII.</summary>
    public static char FoldDigit(char c)
    {
        if (c >= '\u06F0' && c <= '\u06F9') return (char)('0' + (c - '\u06F0'));
        if (c >= '\u0660' && c <= '\u0669') return (char)('0' + (c - '\u0660'));
        if (c >= '\uFF10' && c <= '\uFF19') return (char)('0' + (c - '\uFF10'));
        return c;
    }

    /// <summary>A token in a-z only, or null when it holds anything else (another alphabet).</summary>
    public static string? FoldLatin(string token)
    {
        var sb = new StringBuilder(token.Length);
        foreach (char raw in token)
        {
            if (IsInvisible(raw)) continue;
            char c = raw;
            if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
            else if (c >= '\uFF21' && c <= '\uFF3A') c = (char)(c - 0xFF21 + 'a');
            else if (c >= '\uFF41' && c <= '\uFF5A') c = (char)(c - 0xFF41 + 'a');
            if (c < 'a' || c > 'z') return null;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>All tokens of <paramref name="text"/>, digit-only tokens and empty ones dropped.</summary>
    public static IReadOnlyList<string> SplitTokens(string? text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(text)) return tokens;
        var current = new StringBuilder();
        foreach (char raw in text)
        {
            char c = FoldDigit(raw);
            if (IsSeparator(c))
            {
                Flush(current, tokens);
            }
            else
            {
                current.Append(c);
            }
        }
        Flush(current, tokens);
        return tokens;
    }

    /// <summary>
    /// Splits the entry field: tokens followed by a separator are complete; the trailing
    /// text (no separator after it yet) is still being typed and is returned unchanged.
    /// </summary>
    public static (IReadOnlyList<string> Complete, string Partial) SplitForEntry(string? text)
    {
        if (string.IsNullOrEmpty(text)) return (new List<string>(), string.Empty);
        int lastSeparator = -1;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            if (IsSeparator(FoldDigit(text[i])))
            {
                lastSeparator = i;
                break;
            }
        }
        string done = lastSeparator >= 0 ? text[..(lastSeparator + 1)] : string.Empty;
        string partial = lastSeparator >= 0 ? text[(lastSeparator + 1)..] : text;
        return (SplitTokens(done), partial);
    }

    /// <summary>
    /// The letters to ask the engine about for a token being typed: a-z only, or null when
    /// the token is a position number or holds another alphabet.
    /// </summary>
    public static string? Prefix(string token)
    {
        IReadOnlyList<string> tokens = SplitTokens(token);
        return tokens.Count == 1 ? FoldLatin(tokens[0]) : null;
    }

    /// <summary>
    /// The old letters-and-digits code as the engine compares it: spaces, separators and
    /// invisible characters dropped, Persian, Arabic-Indic and full-width digits folded to
    /// ASCII. Letters keep their case: the old code is case-sensitive.
    /// </summary>
    public static string OldCodeKey(string? code)
    {
        if (string.IsNullOrEmpty(code)) return string.Empty;
        var sb = new StringBuilder(code.Length);
        foreach (char raw in code)
        {
            char c = FoldDigit(raw);
            if (IsSeparator(c) || IsInvisible(c) || char.IsWhiteSpace(c)) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Your own old code as shown to read aloud: groups of 4 separated by spaces, as in the
    /// security-words design ("If4x-36FU-omFi" becomes "If4x 36FU omFi"). Only the display
    /// changes; the engine ignores the spaces when a friend types the code.
    /// </summary>
    public static string GroupOldCode(string? code)
    {
        string key = OldCodeKey(code);
        var sb = new StringBuilder(key.Length + key.Length / 4);
        for (int i = 0; i < key.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(key[i]);
        }
        return sb.ToString();
    }

    private static void Flush(StringBuilder current, List<string> tokens)
    {
        if (current.Length == 0) return;
        string token = current.ToString();
        current.Clear();
        if (token.All(IsInvisible)) return;
        if (token.All(c => (c >= '0' && c <= '9') || IsInvisible(c))) return; // a position number
        tokens.Add(token);
    }
}
