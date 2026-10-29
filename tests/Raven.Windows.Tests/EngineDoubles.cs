using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Raven.Windows.Core.Engine;

namespace Raven.Windows.Tests;

/// <summary>
/// Passes every call to another engine and remembers it (op and a copy of its args), so a test
/// can see what a view model sent, and in which order. <see cref="Intercept"/> may answer a
/// call itself (return null to pass it on).
/// </summary>
internal sealed class RecordingEngine : IRavenEngine
{
    private readonly IRavenEngine _inner;
    private readonly object _gate = new();
    private readonly List<(string Op, JsonObject Args)> _calls = new();

    public RecordingEngine(IRavenEngine inner) => _inner = inner;

    public Func<string, JsonObject, Task<JsonObject>?>? Intercept { get; set; }

    public IReadOnlyList<(string Op, JsonObject Args)> Calls
    {
        get
        {
            lock (_gate) return _calls.ToList();
        }
    }

    public IReadOnlyList<string> Ops => Calls.Select(c => c.Op).ToList();

    /// <summary>The args of every call of <paramref name="op"/>, oldest first.</summary>
    public IReadOnlyList<JsonObject> ArgsOf(string op) => Calls.Where(c => c.Op == op).Select(c => c.Args).ToList();

    public Task<JsonObject> CallAsync(string op, JsonObject? args = null, CancellationToken cancellationToken = default)
    {
        JsonObject copy = args is null ? new JsonObject() : args.DeepClone().AsObject();
        lock (_gate) _calls.Add((op, copy));
        Task<JsonObject>? answer = Intercept?.Invoke(op, copy);
        return answer ?? _inner.CallAsync(op, args, cancellationToken);
    }

    /// <summary>An engine error, as the envelope makes it from a failure response.</summary>
    public static Task<JsonObject> Failure(string code, string message, string detail = "")
        => Task.FromException<JsonObject>(new RavenException(code, message, detail));
}

/// <summary>
/// Answers chosen ops with fixed JSON through the real envelope (sanitising included), for
/// answers no mock gives: a newer engine's values, a release build with odd data, failures.
/// </summary>
internal sealed class StubEngine : IRavenEngine
{
    private readonly Dictionary<string, Func<JsonObject, string>> _answers = new(StringComparer.Ordinal);

    public StubEngine Answer(string op, string resultJson)
    {
        _answers[op] = _ => RavenEnvelope.Success(JsonNode.Parse(resultJson));
        return this;
    }

    public StubEngine Fail(string op, string code, string message, string detail = "")
    {
        _answers[op] = _ => RavenEnvelope.Failure(code, message, detail);
        return this;
    }

    public Task<JsonObject> CallAsync(string op, JsonObject? args = null, CancellationToken cancellationToken = default)
    {
        if (!_answers.TryGetValue(op, out Func<JsonObject, string>? answer))
            return Task.FromException<JsonObject>(new RavenException(RavenErrorCodes.BadRequest, "not stubbed", op));
        try
        {
            return Task.FromResult(RavenEnvelope.ParseResponse(answer(args ?? new JsonObject())));
        }
        catch (RavenException ex)
        {
            return Task.FromException<JsonObject>(ex);
        }
    }
}
