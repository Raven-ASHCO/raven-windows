using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.Engine;

/// <summary>JSON settings shared by every engine call.</summary>
public static class RavenJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
    };
}

/// <summary>
/// The request/response envelope of contract v1, shared by <c>MockRaven</c> and
/// <see cref="RavenClientEngine"/> so both go through exactly the same parsing.
/// </summary>
public static class RavenEnvelope
{
    /// <summary>Contract version this app speaks.</summary>
    public const int Version = 1;

    // API.md: "The engine refuses to put fields named like seed, private_key, plaintext or
    // recovery in a response". The app checks again (defence in depth) and refuses the
    // whole response if one appears; the value is never read or shown.
    private static readonly string[] SecretFieldMarkers =
    {
        "seed", "private_key", "privatekey", "secret_key", "plaintext", "recovery",
    };

    /// <summary>Builds <c>{"v":1,"op":op,"args":{...}}</c>.</summary>
    public static string BuildRequest(string op, JsonObject? args)
    {
        if (string.IsNullOrWhiteSpace(op)) throw new ArgumentException("op is required", nameof(op));
        JsonObject a = args is null ? new JsonObject() : args.Parent is null ? args : args.DeepClone().AsObject();
        var request = new JsonObject
        {
            ["v"] = Version,
            ["op"] = op,
            ["args"] = a,
        };
        string json = request.ToJsonString(RavenJson.Options);
        request.Remove("args"); // give the caller's object back without a parent
        return json;
    }

    /// <summary>Builds a success response (used by the mock).</summary>
    public static string Success(JsonNode? result)
    {
        var response = new JsonObject
        {
            ["v"] = Version,
            ["ok"] = true,
            ["result"] = result ?? new JsonObject(),
        };
        return response.ToJsonString(RavenJson.Options);
    }

    /// <summary>Builds a failure response (used by the mock).</summary>
    public static string Failure(string code, string message, string? detail = null)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrEmpty(detail)) error["detail"] = detail;
        var response = new JsonObject { ["v"] = Version, ["ok"] = false, ["error"] = error };
        return response.ToJsonString(RavenJson.Options);
    }

    /// <summary>
    /// Parses a response. Returns the sanitised <c>result</c> object (detached, owned by the
    /// caller) or throws <see cref="RavenException"/>.
    /// </summary>
    public static JsonObject ParseResponse(string? responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson)) throw RavenException.Protocol("empty response");

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(responseJson);
        }
        catch (JsonException ex)
        {
            throw RavenException.Protocol("response is not JSON: " + ex.Message, ex);
        }

        if (root is not JsonObject envelope) throw RavenException.Protocol("response is not a JSON object");

        if (envelope["v"] is not JsonValue versionValue || !versionValue.TryGetValue(out int version))
            throw RavenException.Protocol("response has no contract version");
        if (version != Version)
            throw RavenException.Protocol($"response uses contract v{version}; this app speaks v{Version}");

        string? secretField = FindSecretField(envelope);
        if (secretField is not null)
            throw RavenException.Protocol($"response contained a field named like a secret ({secretField}); it was discarded unread");

        bool ok = envelope["ok"] is JsonValue okValue && okValue.TryGetValue(out bool okFlag) && okFlag;
        if (!ok)
        {
            JsonObject? error = envelope["error"] as JsonObject;
            string code = ReadString(error, "code") ?? RavenErrorCodes.Internal;
            string message = TextSanitizer.Clean(ReadString(error, "message"));
            string detail = TextSanitizer.Clean(ReadString(error, "detail"));
            if (string.IsNullOrWhiteSpace(message)) message = Loc.Get("error.badResponse");
            throw new RavenException(TextSanitizer.SingleLine(code), message, detail);
        }

        JsonNode? resultNode = envelope["result"];
        if (resultNode is null) return new JsonObject();
        if (resultNode is not JsonObject result) throw RavenException.Protocol("result is not a JSON object");

        envelope.Remove("result");
        SanitizeStrings(result);
        return result;
    }

    /// <summary>Returns the first property name that looks like a secret, or null.</summary>
    internal static string? FindSecretField(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    string name = property.Key.ToLowerInvariant();
                    if (SecretFieldMarkers.Any(marker => name.Contains(marker, StringComparison.Ordinal)))
                        return TextSanitizer.SingleLine(property.Key);
                    string? inner = FindSecretField(property.Value);
                    if (inner is not null) return inner;
                }
                return null;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    string? inner = FindSecretField(item);
                    if (inner is not null) return inner;
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Sanitises every string value in place (API.md: text for display is sanitised by the
    /// engine; the app does it again so a friend's name or message cannot draw over the UI).
    /// Newlines are kept here; single-line places use <see cref="TextSanitizer.SingleLine"/>.
    /// </summary>
    internal static void SanitizeStrings(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (string key in obj.Select(p => p.Key).ToList())
                {
                    JsonNode? value = obj[key];
                    if (value is JsonValue v && v.TryGetValue(out string? s))
                        obj[key] = JsonValue.Create(TextSanitizer.Clean(s));
                    else
                        SanitizeStrings(value);
                }
                break;
            case JsonArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    JsonNode? value = array[i];
                    if (value is JsonValue v && v.TryGetValue(out string? s))
                        array[i] = JsonValue.Create(TextSanitizer.Clean(s));
                    else
                        SanitizeStrings(value);
                }
                break;
        }
    }

    private static string? ReadString(JsonObject? obj, string name)
        => obj?[name] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
}
