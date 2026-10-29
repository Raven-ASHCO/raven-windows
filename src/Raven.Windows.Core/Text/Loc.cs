using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Raven.Windows.Core.Text;

/// <summary>
/// Every user-facing text, read at runtime from desktop/design/strings.en.json (embedded
/// in this assembly as "Raven.Strings.en.json"). English only.
/// <para>
/// A key that is not in the file shows as <c>[key]</c> instead of invented copy, so a gap is
/// visible; StringKeysTests fails the build when the code uses a key the file does not have.
/// </para>
/// </summary>
public static class Loc
{
    public const string ResourceName = "Raven.Strings.en.json";

    private static readonly Lazy<StringTable> Table = new(() => StringTable.LoadEmbedded());

    /// <summary>All keys with a single text.</summary>
    public static IReadOnlyCollection<string> Keys => Table.Value.Strings.Keys;

    /// <summary>All keys with a list of texts (for example "settings.keepChats.options").</summary>
    public static IReadOnlyCollection<string> ArrayKeys => Table.Value.Arrays.Keys;

    public static bool Has(string key) => Table.Value.Strings.ContainsKey(key) || Table.Value.Arrays.ContainsKey(key);

    /// <summary>The text for <paramref name="key"/>, or <c>[key]</c> when the key is missing.</summary>
    public static string Get(string key)
    {
        if (Table.Value.Strings.TryGetValue(key, out string? text)) return text;
        Debug.WriteLine($"Raven: missing string key '{key}' (add it to desktop/design/strings.en.json)");
        return "[" + key + "]";
    }

    /// <summary>
    /// The text for <paramref name="key"/> with <c>{name}</c> placeholders replaced. Values are
    /// inserted as given (they are already sanitised); an unknown placeholder stays visible.
    /// </summary>
    public static string Format(string key, params (string Name, object? Value)[] values)
    {
        string text = Get(key);
        foreach ((string name, object? value) in values)
        {
            string replacement = value switch
            {
                null => string.Empty,
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty,
            };
            text = text.Replace("{" + name + "}", replacement, StringComparison.Ordinal);
        }
        return text;
    }

    /// <summary>The list for <paramref name="key"/>; empty (and logged) when missing.</summary>
    public static IReadOnlyList<string> GetArray(string key)
    {
        if (Table.Value.Arrays.TryGetValue(key, out IReadOnlyList<string>? list)) return list;
        Debug.WriteLine($"Raven: missing string list '{key}' (add it to desktop/design/strings.en.json)");
        return Array.Empty<string>();
    }

    internal sealed class StringTable
    {
        public Dictionary<string, string> Strings { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IReadOnlyList<string>> Arrays { get; } = new(StringComparer.Ordinal);

        public static StringTable LoadEmbedded()
        {
            using Stream? stream = typeof(Loc).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
                throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing (see Raven.Windows.Core.csproj).");
            return Parse(stream);
        }

        public static StringTable Parse(Stream json)
        {
            var table = new StringTable();
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Name.StartsWith('_')) continue; // "_note" and other comments
                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.String:
                        table.Strings[property.Name] = property.Value.GetString() ?? string.Empty;
                        break;
                    case JsonValueKind.Array:
                        var list = new List<string>();
                        foreach (JsonElement item in property.Value.EnumerateArray())
                            list.Add(item.GetString() ?? string.Empty);
                        table.Arrays[property.Name] = list;
                        break;
                }
            }
            return table;
        }
    }
}
