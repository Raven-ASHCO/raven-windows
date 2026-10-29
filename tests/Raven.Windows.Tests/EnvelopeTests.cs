using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Tests;

/// <summary>The request/response envelope of desktop/api/API.md.</summary>
[TestClass]
public sealed class EnvelopeTests
{
    [TestMethod]
    public void Request_HasVersionOpAndArgs()
    {
        var args = new JsonObject { ["address"] = "rvn1qxyz", ["limit"] = 10 };
        JsonObject request = JsonNode.Parse(RavenEnvelope.BuildRequest(RavenOps.MessagesHistory, args))!.AsObject();
        Assert.AreEqual(1, (int)request["v"]!);
        Assert.AreEqual("messages.history", (string)request["op"]!);
        Assert.AreEqual(10, (int)request["args"]!["limit"]!);
        Assert.IsNull(args.Parent, "the caller's args object is left reusable");

        JsonObject noArgs = JsonNode.Parse(RavenEnvelope.BuildRequest(RavenOps.AppInfo, null))!.AsObject();
        Assert.AreEqual(0, noArgs["args"]!.AsObject().Count);
    }

    [TestMethod]
    public void Success_ReturnsTheSanitisedResult_AndIgnoresUnknownFields()
    {
        string response = "{\"v\":1,\"ok\":true,\"future\":42,\"result\":{\"contacts\":[{\"name\":\"Ali\\u202Eevil\",\"address\":\"rvn1q\",\"new_field\":true}]}}";
        JsonObject result = RavenEnvelope.ParseResponse(response);
        Assert.AreEqual("Alievil", (string)result["contacts"]![0]!["name"]!);
        Assert.IsNull(result.Parent);
    }

    [TestMethod]
    public void Failure_BecomesRavenException_WithMessageAndDetail()
    {
        string response = RavenEnvelope.Failure(RavenErrorCodes.CardInvalid, "This isn't a Raven invite.\u200B", "no address");
        RavenException ex = Assert.ThrowsExactly<RavenException>(() => RavenEnvelope.ParseResponse(response));
        Assert.AreEqual(RavenErrorCodes.CardInvalid, ex.Code);
        Assert.AreEqual("This isn't a Raven invite.", ex.Message);
        Assert.AreEqual("no address", ex.Detail);
    }

    [TestMethod]
    [DataRow("not json", DisplayName = "not JSON")]
    [DataRow("[1,2]", DisplayName = "not an object")]
    [DataRow("{\"ok\":true,\"result\":{}}", DisplayName = "no version")]
    [DataRow("{\"v\":2,\"ok\":true,\"result\":{}}", DisplayName = "newer contract")]
    [DataRow("{\"v\":1,\"ok\":true,\"result\":[]}", DisplayName = "result not an object")]
    [DataRow("", DisplayName = "empty")]
    public void BrokenResponses_AreInternalErrors(string response)
    {
        RavenException ex = Assert.ThrowsExactly<RavenException>(() => RavenEnvelope.ParseResponse(response));
        Assert.AreEqual(RavenErrorCodes.Internal, ex.Code);
        Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Detail));
    }

    [TestMethod]
    [DataRow("seed")]
    [DataRow("private_key")]
    [DataRow("identity_seed_hex")]
    [DataRow("plaintext")]
    [DataRow("recovery_phrase")]
    public void SecretLookingFields_RejectTheWholeResponse(string field)
    {
        string response = "{\"v\":1,\"ok\":true,\"result\":{\"me\":{\"" + field + "\":\"abc\"}}}";
        RavenException ex = Assert.ThrowsExactly<RavenException>(() => RavenEnvelope.ParseResponse(response));
        Assert.AreEqual(RavenErrorCodes.Internal, ex.Code);
        Assert.IsFalse(ex.Detail.Contains("abc"), "the value is never read into the error");
    }

    [TestMethod]
    public void ClientEngine_SendsTheEnvelope_AndReadsTheAnswer()
    {
        string? sent = null;
        var engine = new RavenClientEngine(request =>
        {
            sent = request;
            return "{\"v\":1,\"ok\":true,\"result\":{\"version\":\"0.1.0\",\"build\":\"release\",\"platform\":\"windows\",\"api\":1}}";
        });
        JsonObject result = engine.CallAsync(RavenOps.AppInfo).GetAwaiter().GetResult();
        Assert.AreEqual("windows", (string?)result["platform"]);
        JsonObject request = JsonNode.Parse(sent!)!.AsObject();
        Assert.AreEqual(1, (int)request["v"]!);
        Assert.AreEqual(RavenOps.AppInfo, (string?)request["op"]);

        var refusing = new RavenClientEngine(_ => RavenEnvelope.Failure(RavenErrorCodes.NotAContact, "That friend isn't in your list. Add them first.", "no saved friend"));
        RavenException ex = Assert.ThrowsExactly<RavenException>(
            () => refusing.CallAsync(RavenOps.ContactsRemove, new JsonObject { ["address"] = "rvn1q" }).GetAwaiter().GetResult());
        Assert.AreEqual(RavenErrorCodes.NotAContact, ex.Code);
        Assert.AreEqual("no saved friend", ex.Detail);
    }

    [TestMethod]
    public void ClientEngine_WithoutItsLibrary_FailsWithNotInitialized()
    {
        Exception[] failures =
        {
            new DllNotFoundException("Unable to load DLL 'raven_client'"),
            new EntryPointNotFoundException("raven_call"),
            new BadImageFormatException("wrong architecture"),
        };
        foreach (Exception failure in failures)
        {
            var engine = new RavenClientEngine(_ => throw failure);
            RavenException ex = Assert.ThrowsExactly<RavenException>(() => engine.CallAsync(RavenOps.AppInfo).GetAwaiter().GetResult());
            Assert.AreEqual(RavenErrorCodes.NotInitialized, ex.Code);
            Assert.AreEqual(Loc.Get("error.engineMissing"), ex.Message);
            StringAssert.Contains(ex.Detail, RavenClientEngine.LibraryFileName);
            Assert.AreSame(failure, ex.InnerException);
        }
    }

    [TestMethod]
    public void ClientEngine_ReturnsCancelledTask_WhenCancelledBeforeTheCall()
    {
        bool called = false;
        var engine = new RavenClientEngine(_ => { called = true; return RavenEnvelope.Success(null); });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Task<JsonObject> call = engine.CallAsync(RavenOps.AppInfo, null, cancelled.Token);
        Assert.IsTrue(call.IsCanceled);
        Assert.IsFalse(called);
    }

    [TestMethod]
    public void EngineChoice_DemoOnlyWhenAskedFor()
    {
        Assert.IsFalse(EngineChoice.DemoRequested(new[] { @"C:\Raven\Raven.exe" }));
        Assert.IsFalse(EngineChoice.DemoRequested(Array.Empty<string>()));
        Assert.IsFalse(EngineChoice.DemoRequested(new[] { "Raven.exe", "--demonstrate" }));
        Assert.IsTrue(EngineChoice.DemoRequested(new[] { "Raven.exe", "--demo" }));
        Assert.IsTrue(EngineChoice.DemoRequested(new[] { "Raven.exe", "--Mock-Ready" }));
        Assert.IsTrue(EngineChoice.DemoRequested(new[] { "Raven.exe", "--mock-locked" }));
        Assert.IsTrue(EngineChoice.Contains(new[] { "Raven.exe", "--demo", "--mock-ready" }, EngineChoice.MockReadyArgument));
        Assert.IsFalse(EngineChoice.Contains(new[] { "Raven.exe", "--demo" }, EngineChoice.MockLockedArgument));
    }

    [TestMethod]
    public void BrokenResponses_SayTheAnswerCannotBeRead()
    {
        RavenException ex = Assert.ThrowsExactly<RavenException>(() => RavenEnvelope.ParseResponse("not json"));
        Assert.AreEqual(Loc.Get("error.badResponse"), ex.Message);
    }
}
