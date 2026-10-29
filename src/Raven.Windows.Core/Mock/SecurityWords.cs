#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Raven.Windows.Core.Mock;

/// <summary>
/// Raven security words v1 (draft), as far as MockRaven needs them: derive a key's 10 words
/// and complete a typed prefix. Port of the stdlib reference prototype
/// (core/protocol/reference/raven_protocol/security_words.py). DEBUG builds only;
/// in release the engine (raven-client) does all of this.
/// <code>
/// digest  = SHA-256("rvn1/security-words/v1" || ed_pub)
/// indexes = digest[0..9] + check, check = xtime(Horner over GF(2^8) of the 9 indexes)
/// </code>
/// </summary>
public static class SecurityWords
{
    public const string ResourceName = "Raven.Mock.security-words-v1.txt";
    public const int DataWords = 9;
    public const int TotalWords = 10;
    public const int MinPrefix = 3;

    private static readonly Lazy<IReadOnlyList<string>> LazyList = new(Load);
    private static readonly Lazy<Dictionary<string, int>> LazyExact = new(() =>
    {
        var exact = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < List.Count; i++) exact[List[i]] = i;
        return exact;
    });

    /// <summary>Domain-separation label of v1 (22 ASCII bytes, no terminator).</summary>
    public static ReadOnlySpan<byte> LabelV1 => "rvn1/security-words/v1"u8;

    /// <summary>The 256 words, index order.</summary>
    public static IReadOnlyList<string> List => LazyList.Value;

    /// <summary>Multiply by 2 in GF(2^8) modulo x^8+x^4+x^3+x+1 (0x11B).</summary>
    public static int XTime(int v)
    {
        v <<= 1;
        return (v & 0x100) != 0 ? v ^ 0x11B : v;
    }

    public static byte CheckIndex(ReadOnlySpan<byte> data)
    {
        int acc = 0;
        foreach (byte d in data) acc = XTime(acc) ^ d;
        return (byte)XTime(acc);
    }

    /// <summary>True when the 10 indexes are consistent with their check word.</summary>
    public static bool CheckOk(ReadOnlySpan<byte> indexes)
    {
        int acc = 0;
        foreach (byte d in indexes) acc = XTime(acc) ^ d;
        return acc == 0;
    }

    public static byte[] DeriveIndexes(ReadOnlySpan<byte> edPublicKey)
    {
        if (edPublicKey.Length != 32) throw new ArgumentException("ed_pub must be 32 bytes", nameof(edPublicKey));
        byte[] input = new byte[LabelV1.Length + 32];
        LabelV1.CopyTo(input);
        edPublicKey.CopyTo(input.AsSpan(LabelV1.Length));
        byte[] digest = SHA256.HashData(input);
        byte[] indexes = new byte[TotalWords];
        digest.AsSpan(0, DataWords).CopyTo(indexes);
        indexes[DataWords] = CheckIndex(indexes.AsSpan(0, DataWords));
        return indexes;
    }

    public static string[] WordsFor(ReadOnlySpan<byte> edPublicKey)
    {
        byte[] indexes = DeriveIndexes(edPublicKey);
        var words = new string[indexes.Length];
        for (int i = 0; i < indexes.Length; i++) words[i] = List[indexes[i]];
        return words;
    }

    /// <summary>
    /// Index of a folded (a-z) token: the exact word, a plural (word+"s"/"es"), or a unique
    /// prefix of at least 3 letters. Null when the token is unknown or still ambiguous.
    /// </summary>
    public static int? Resolve(string folded)
    {
        if (string.IsNullOrEmpty(folded)) return null;
        Dictionary<string, int> exact = LazyExact.Value;
        if (exact.TryGetValue(folded, out int index)) return index;
        foreach (string suffix in new[] { "es", "s" })
        {
            if (folded.EndsWith(suffix, StringComparison.Ordinal)
                && exact.TryGetValue(folded[..^suffix.Length], out int plural))
                return plural;
        }
        if (folded.Length < MinPrefix) return null;
        int found = -1;
        for (int i = 0; i < List.Count; i++)
        {
            if (!List[i].StartsWith(folded, StringComparison.Ordinal)) continue;
            if (found >= 0) return null; // ambiguous (cannot happen at 3+ letters for v1)
            found = i;
        }
        return found >= 0 ? found : null;
    }

    private static IReadOnlyList<string> Load()
    {
        using Stream? stream = typeof(SecurityWords).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing (debug builds embed it, see Raven.Windows.Core.csproj).");
        using var reader = new StreamReader(stream);
        var words = new List<string>(256);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] parts = line.Split('\t');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int i) || i != words.Count)
                throw new InvalidDataException("bad word list row: " + line);
            foreach (char c in parts[1])
                if (c < 'a' || c > 'z') throw new InvalidDataException("bad word list row: " + line);
            words.Add(parts[1]);
        }
        if (words.Count != 256) throw new InvalidDataException("the word list must have 256 rows");
        return words;
    }
}
#endif
