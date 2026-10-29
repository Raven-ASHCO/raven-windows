using System.Globalization;
using System.Text;

namespace Raven.Windows.Core.Text;

/// <summary>
/// Makes text from the engine (a friend's name, a message, an error) safe to display:
/// it cannot reorder or hide the surrounding UI and cannot draw over it.
/// <list type="bullet">
/// <item>control characters (C0, DEL, C1) are removed; tabs become spaces;</item>
/// <item>every format character (Unicode category Cf) is removed: bidi embeddings,
/// overrides and isolates, LRM/RLM/ALM, zero-width space/joiners, word joiner, BOM, soft
/// hyphen, invisible operators, tag characters;</item>
/// <item>other invisible "blank letters" (Hangul fillers, Khmer inherent vowels, the
/// combining grapheme joiner) are removed;</item>
/// <item>more than <see cref="MaxCombiningMarks"/> combining marks in a row are cut, so
/// stacked marks cannot spill over neighbouring lines;</item>
/// <item>broken UTF-16 (lone surrogates) becomes U+FFFD;</item>
/// <item>CR LF, CR, U+2028 and U+2029 become a newline, or a space in single-line text.</item>
/// </list>
/// </summary>
public static class TextSanitizer
{
    /// <summary>Most combining marks kept after one base character.</summary>
    public const int MaxCombiningMarks = 4;

    /// <summary>Cleans text; newlines are kept when <paramref name="allowNewlines"/> is true.</summary>
    public static string Clean(string? text, bool allowNewlines = true)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);
        int marks = 0;
        bool previousWasCr = false;

        foreach (Rune rune in text.EnumerateRunes()) // ill-formed UTF-16 yields U+FFFD
        {
            int value = rune.Value;

            if (value == '\n' && previousWasCr)
            {
                previousWasCr = false; // the CR already produced the line break
                continue;
            }
            previousWasCr = value == '\r';

            if (value == '\n' || value == '\r' || value == 0x2028 || value == 0x2029)
            {
                sb.Append(allowNewlines ? '\n' : ' ');
                marks = 0;
                continue;
            }

            if (value == '\t')
            {
                sb.Append(' ');
                marks = 0;
                continue;
            }

            if (IsRemoved(rune)) continue;

            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.EnclosingMark)
            {
                if (marks >= MaxCombiningMarks) continue;
                marks++;
            }
            else
            {
                marks = 0;
            }

            sb.Append(rune.ToString());
        }

        return sb.ToString();
    }

    /// <summary>Cleans text for one-line places (names, status lines): no newlines, trimmed.</summary>
    public static string SingleLine(string? text) => Clean(text, allowNewlines: false).Trim();

    /// <summary>True for characters that are always removed.</summary>
    public static bool IsRemoved(Rune rune)
    {
        int value = rune.Value;
        switch (Rune.GetUnicodeCategory(rune))
        {
            case UnicodeCategory.Control: // C0, DEL, C1
            case UnicodeCategory.Format:  // bidi controls, zero-width characters, BOM, soft hyphen, tags
                return true;
        }

        return value switch
        {
            0x034F => true,                 // combining grapheme joiner
            0x115F or 0x1160 => true,       // Hangul choseong/jungseong fillers
            0x3164 or 0xFFA0 => true,       // Hangul fillers
            0x17B4 or 0x17B5 => true,       // Khmer inherent vowels (invisible)
            0x180E => true,                 // Mongolian vowel separator (Cf in newer Unicode)
            >= 0x200B and <= 0x200F => true,// ZWSP, ZWNJ, ZWJ, LRM, RLM
            >= 0x202A and <= 0x202E => true,// LRE, RLE, PDF, LRO, RLO
            >= 0x2060 and <= 0x206F => true,// word joiner, invisible operators, isolates, deprecated format
            0x061C => true,                 // Arabic letter mark
            0xFEFF => true,                 // BOM / zero-width no-break space
            >= 0xFFF9 and <= 0xFFFB => true,// interlinear annotation
            >= 0xE0000 and <= 0xE007F => true, // tag characters
            _ => false,
        };
    }
}
