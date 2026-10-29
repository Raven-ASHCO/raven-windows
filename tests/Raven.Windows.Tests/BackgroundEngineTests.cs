using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Engine;

namespace Raven.Windows.Tests;

/// <summary>
/// API.md: raven_call may block, so the app never calls it on the UI thread. BackgroundEngine
/// runs every call on the thread pool, keeps the order of the calls, and lets events.poll pass
/// a slow call.
/// </summary>
[TestClass]
public sealed class BackgroundEngineTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task ABlockingCall_DoesNotBlockTheCaller_AndKeepsTheOrder()
    {
        using var inner = new BlockingEngine(RavenOps.NodeStart);
        var engine = new BackgroundEngine(inner);

        Task<JsonObject> start = engine.CallAsync(RavenOps.NodeStart);
        Task<JsonObject> status = engine.CallAsync(RavenOps.NodeStatus);
        Assert.IsFalse(start.IsCompleted, "node.start blocks inside the engine, not in the caller");

        // events.poll never blocks and is not held up by the slow call.
        JsonObject poll = await engine.CallAsync(RavenOps.EventsPoll).WaitAsync(Patience);
        Assert.AreEqual(RavenOps.EventsPoll, (string?)poll["op"]);
        Assert.IsFalse(status.IsCompleted, "node.status waits for node.start (calls keep their order)");
        Assert.IsFalse(inner.Started.Contains(RavenOps.NodeStatus));

        inner.Release.Set();
        await Task.WhenAll(start, status).WaitAsync(Patience);
        Assert.AreEqual(RavenOps.NodeStatus, inner.Started.Last());
        Assert.AreEqual(RavenOps.NodeStart, (string?)start.Result["op"]);
    }

    [TestMethod]
    public async Task Errors_ReachTheCaller_AndTheQueueGoesOn()
    {
        using var inner = new BlockingEngine(blockOn: null);
        var engine = new BackgroundEngine(inner);

        RavenException ex = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.CallAsync(BlockingEngine.FailingOp));
        Assert.AreEqual(RavenErrorCodes.Internal, ex.Code);
        JsonObject next = await engine.CallAsync(RavenOps.AppInfo).WaitAsync(Patience);
        Assert.AreEqual(RavenOps.AppInfo, (string?)next["op"]);
    }

    /// <summary>A fake engine whose <c>blockOn</c> operation waits until released (like a slow raven_call).</summary>
    private sealed class BlockingEngine : IRavenEngine, IDisposable
    {
        public const string FailingOp = "test.fail";
        private readonly string? _blockOn;

        public BlockingEngine(string? blockOn) => _blockOn = blockOn;

        public ManualResetEventSlim Release { get; } = new(false);

        public ConcurrentQueue<string> Started { get; } = new();

        public Task<JsonObject> CallAsync(string op, JsonObject? args = null, CancellationToken cancellationToken = default)
        {
            Started.Enqueue(op);
            if (op == _blockOn) Release.Wait(Patience);
            if (op == FailingOp) throw new RavenException(RavenErrorCodes.Internal, "failed on purpose");
            return Task.FromResult(new JsonObject { ["op"] = op });
        }

        public void Dispose() => Release.Dispose();
    }
}
