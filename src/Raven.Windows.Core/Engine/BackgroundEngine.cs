using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Raven.Windows.Core.Engine;

/// <summary>
/// Keeps every engine call off the UI thread. API.md: <c>raven_call</c> is thread-safe but
/// may block (node.start can take a few seconds), so the app never calls it on the UI thread.
/// <para>
/// Calls run on the thread pool one after another, in the order the app made them, so the
/// engine sees the same order as a direct call would (for example two quick settings.set).
/// events.poll never blocks and runs beside that queue, so a slow node.start does not hold
/// up the event pump. Callers just <c>await</c>: the continuation comes back on the caller's
/// synchronization context, which for the window is the UI thread's DispatcherQueue.
/// </para>
/// </summary>
public sealed class BackgroundEngine : IRavenEngine
{
    private readonly IRavenEngine _inner;
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    public BackgroundEngine(IRavenEngine inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>The engine that does the work (MockRaven or RavenClientEngine).</summary>
    public IRavenEngine Inner => _inner;

    /// <inheritdoc />
    public Task<JsonObject> CallAsync(string op, JsonObject? args = null, CancellationToken cancellationToken = default)
    {
        if (op == RavenOps.EventsPoll)
            return Task.Run(() => _inner.CallAsync(op, args, cancellationToken), cancellationToken);

        lock (_gate)
        {
            Task<JsonObject> call = _tail.ContinueWith(
                _ => _inner.CallAsync(op, args, cancellationToken),
                CancellationToken.None,
                TaskContinuationOptions.DenyChildAttach,
                TaskScheduler.Default).Unwrap();
            // The next call waits for this one whatever its outcome (success, error, cancelled).
            _tail = call.ContinueWith(
                static _ => { },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return call;
        }
    }
}
