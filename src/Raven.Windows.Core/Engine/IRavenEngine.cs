using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.Engine;

/// <summary>
/// The one door between the app and Raven's engine (desktop/api/API.md, contract v1).
/// The app never reimplements Raven logic: every question and every action is one
/// <c>{"v":1,"op":...,"args":{...}}</c> request. Typed wrappers live in <see cref="RavenApi"/>.
/// </summary>
public interface IRavenEngine
{
    /// <summary>
    /// Sends one request and returns the <c>result</c> object of a successful response, with
    /// every string already sanitised for display. A failure response (<c>"ok": false</c>)
    /// becomes a <see cref="RavenException"/> carrying the engine's code, its plain-English
    /// message (one next step) and the technical detail.
    /// </summary>
    Task<JsonObject> CallAsync(string op, JsonObject? args = null, CancellationToken cancellationToken = default);
}

/// <summary>An error response from the engine, or a broken response treated as INTERNAL.</summary>
public sealed class RavenException : Exception
{
    public RavenException(string code, string message, string? detail = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = string.IsNullOrEmpty(code) ? RavenErrorCodes.Internal : code;
        Detail = detail ?? string.Empty;
    }

    /// <summary>One of <see cref="RavenErrorCodes"/> (or a newer code this app does not know).</summary>
    public string Code { get; }

    /// <summary>Technical text, shown only under "Details".</summary>
    public string Detail { get; }

    /// <summary>A response this app cannot use (not JSON, wrong version, a secret-looking field...).</summary>
    public static RavenException Protocol(string detail, Exception? inner = null)
        => new(RavenErrorCodes.Internal, Loc.Get("error.badResponse"), detail, inner);
}
