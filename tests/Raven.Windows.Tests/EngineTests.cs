using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core.Engine;

namespace Raven.Windows.Tests;

/// <summary>
/// Raven's real engine, raven_client.dll (node/crates/raven-client), through
/// <see cref="RavenClientEngine"/>, <see cref="BackgroundEngine"/> and the typed wrappers, as
/// the app calls it. Every call runs on one throwaway profile with the file-backed secret
/// stores, set up by <see cref="EngineLab"/> before the first call in this process: never
/// %USERPROFILE%\.raven, never DPAPI. receive stays off and nothing here starts raven-node.
/// <para>
/// Runs on Windows when the debug build of the library (<c>cargo build -p raven-client</c> in
/// node/) was copied next to the tests; otherwise inconclusive, or failed when
/// RAVEN_REQUIRE_ENGINE_TESTS=1 (CI).
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EngineTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [TestMethod]
    public async Task AppInfo_NamesWindowsAndTheEngineBuild()
    {
        IRavenEngine engine = EngineLab.Engine();
        AppInfo info = await engine.AppInfoAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(info.Version));
        Assert.AreEqual("windows", info.Platform);
        // The debug library: the only one that accepts the file-backed stores.
        Assert.AreEqual("dev", info.Build);
    }

    [TestMethod]
    public async Task NewProfile_GetsAnIdentity_AndIdentityMeShowsIt()
    {
        IRavenEngine engine = EngineLab.Engine();
        EngineLab.Setup setup = await EngineLab.SetUpProfileAsync(engine);

        Assert.AreEqual(ProfileStates.New, setup.First.State, setup.First.Reason);
        Assert.IsFalse(setup.Chosen.Receive, "settings chosen before the identity are kept in memory");

        IdentityCreated created = setup.Created;
        StringAssert.StartsWith(created.Address, "rvn1");
        Assert.AreEqual(10, created.Words.Count, string.Join(" ", created.Words));
        Assert.IsTrue(created.Words.All(w => !string.IsNullOrWhiteSpace(w)));
        Assert.IsFalse(string.IsNullOrWhiteSpace(created.OldCode));

        ProfileState ready = await engine.ProfileStateAsync();
        Assert.AreEqual(ProfileStates.Ready, ready.State);
        Assert.IsTrue(Directory.EnumerateFileSystemEntries(EngineLab.ProfileDir).Any(),
            "the identity went where RAVEN_DATA_DIR points");

        Me me = await engine.IdentityMeAsync();
        Assert.AreEqual(created.Address, me.Address);
        CollectionAssert.AreEqual(created.Words, me.Words);
        Assert.AreEqual(created.OldCode, me.OldCode);
        Assert.AreEqual(EngineLab.DisplayName, me.DisplayName);
        Assert.IsFalse(string.IsNullOrWhiteSpace(me.Card));
        StringAssert.StartsWith(me.Invite, "raven:");

        // A second identity.create keeps the identity.
        IdentityCreated again = await engine.IdentityCreateAsync(null);
        Assert.AreEqual(created.Address, again.Address);

        IReadOnlyList<Contact> contacts = await engine.ContactsListAsync();
        Assert.AreEqual(0, contacts.Count);
    }

    [TestMethod]
    public async Task Settings_RoundTrip_IntoTheProfile()
    {
        IRavenEngine engine = EngineLab.Engine();
        await EngineLab.SetUpProfileAsync(engine);

        RavenSettings set = await engine.SettingsSetAsync(new JsonObject
        {
            ["reduced_motion"] = true,
            ["when_quit"] = WhenQuitOptions.Ask,
            ["keep_chats_days"] = 7,
            ["lock_on_sleep"] = true,
        });
        Assert.IsFalse(set.Receive, "kept from setup");
        Assert.IsTrue(set.ReducedMotion);
        Assert.AreEqual(WhenQuitOptions.Ask, set.WhenQuit);
        Assert.AreEqual((int?)7, set.KeepChatsDays);
        Assert.IsTrue(set.ForgetAddresses, "Raven always forgets unused friends' addresses (privacy pack PP.7)");
        Assert.IsTrue(set.LockOnSleep);
        Assert.AreEqual(EngineLab.DisplayName, set.DisplayName);

        RavenSettings read = await engine.SettingsGetAsync();
        Assert.AreEqual(set.ReducedMotion, read.ReducedMotion);
        Assert.AreEqual(set.WhenQuit, read.WhenQuit);
        Assert.AreEqual(set.KeepChatsDays, read.KeepChatsDays);
        Assert.AreEqual(set.LockOnSleep, read.LockOnSleep);

        // "Always" keeps chats: null on the wire; only the given field changes.
        RavenSettings always = await engine.SettingsSetAsync(new JsonObject { ["keep_chats_days"] = null });
        Assert.IsNull(always.KeepChatsDays);
        Assert.IsTrue(always.ReducedMotion);

        // Written into the profile; the name has its own file (sealed once the profile is) and the
        // chat-keeping time is the privacy pack's setting, so desktop_settings.json names nobody.
        string file = Path.Combine(EngineLab.ProfileDir, "desktop_settings.json");
        JsonObject saved = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        Assert.AreEqual(WhenQuitOptions.Ask, (string?)saved["when_quit"]);
        Assert.IsFalse((bool)saved["receive"]!);
        Assert.IsFalse(saved.ContainsKey("display_name"));
        Assert.IsFalse(saved.ContainsKey("keep_chats_days"));

        // A value outside the contract is refused and changes nothing.
        RavenException refused = await Assert.ThrowsExactlyAsync<RavenException>(
            () => engine.SettingsSetAsync(new JsonObject { ["when_quit"] = "never" }));
        Assert.AreEqual(RavenErrorCodes.BadRequest, refused.Code);
        Assert.AreEqual(WhenQuitOptions.Ask, (await engine.SettingsGetAsync()).WhenQuit);

        // Forgetting addresses can't be turned off.
        RavenException forget = await Assert.ThrowsExactlyAsync<RavenException>(
            () => engine.SettingsSetAsync(new JsonObject { ["forget_addresses"] = false }));
        Assert.AreEqual(RavenErrorCodes.NotAvailableYet, forget.Code);
        Assert.IsTrue((await engine.SettingsGetAsync()).ForgetAddresses);
    }

    [TestMethod]
    public async Task Privacy_StateAndTheActionsTheAppDoesNotOfferYet()
    {
        IRavenEngine engine = EngineLab.Engine();
        // A plain profile (a debug engine without the lab switch), not locked.
        PrivacyState privacy = await engine.PrivacyStateAsync();
        Assert.IsFalse(privacy.Available);
        Assert.IsFalse(privacy.Encrypted);
        Assert.IsFalse(privacy.Locked);

        RavenException ex = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.PrivacyLockAsync());
        Assert.AreEqual(RavenErrorCodes.NotAvailableYet, ex.Code);
        Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message));

        // Unlock clears a `raven lock` (none here) and answers as profile.state.
        ProfileState unlocked = await engine.PrivacyUnlockAsync();
        CollectionAssert.Contains(new[] { ProfileStates.New, ProfileStates.Ready }, unlocked.State);
    }

    /// <summary>
    /// The network ops (API.md, "Network"): every way has a state, LAN direct is on, a held way
    /// is refused and changes nothing, the help-friends flags reach the profile's node policy,
    /// and the doctor, security, Lock and updates views decode. Nothing starts raven-node.
    /// </summary>
    [TestMethod]
    public async Task Network_WaysFlagsAndViews()
    {
        IRavenEngine engine = EngineLab.Engine();
        await EngineLab.SetUpProfileAsync(engine);
        NetworkStatus net = await engine.NetworkStatusAsync();
        Assert.AreEqual(WayStates.On, net.Way("lan")?.State);
        Assert.IsTrue(net.Carriers.Count >= 13 && net.Protections.Count >= 6);
        foreach (NetworkWay w in net.Carriers.Concat(net.Protections))
            Assert.IsFalse(string.IsNullOrWhiteSpace(w.Reason), w.Id);
        if (net.Way("p2p")?.State == WayStates.Preview)
        {
            RavenException held = await Assert.ThrowsExactlyAsync<RavenException>(
                () => engine.NetworkSetAsync(new JsonObject { ["p2p"] = true }));
            Assert.AreEqual(RavenErrorCodes.NotAvailableYet, held.Code);
        }
        NetworkSetResult set = await engine.NetworkSetAsync(new JsonObject { ["store"] = false });
        Assert.AreEqual(1, set.Changes.Count);
        Assert.IsFalse(set.Status.Help.Store);
        StringAssert.Contains(File.ReadAllText(Path.Combine(EngineLab.ProfileDir, "node_policy.json")), "\"store\": false");
        await engine.NetworkSetAsync(new JsonObject { ["store"] = true });
        NetworkCheck check = await engine.NetworkCheckAsync();
        Assert.IsTrue(check.Summary.Any(r => r.Key == "identity"));
        Assert.IsNull(check.Router);
        await engine.SecurityStatusAsync();
        await engine.LockStateAsync();
        Assert.AreEqual("cli_only", (await engine.UpdatesStatusAsync()).Install);
    }

    /// <summary>
    /// The Network, Check and Security screens' view models on the real engine and the throwaway
    /// profile: network.status reads, every way gets a chip and a plain line, the banner follows
    /// the lab switches the debug engine sees, and the other views load without an error.
    /// </summary>
    [TestMethod]
    public async Task NetworkScreens_ReadTheThrowawayProfile()
    {
        IRavenEngine engine = EngineLab.Engine();
        await EngineLab.SetUpProfileAsync(engine);

        var network = new Raven.Windows.Core.ViewModels.NetworkViewModel(engine);
        await network.LoadAsync();
        Assert.IsNull(network.Error, network.Error?.Detail);
        NetworkStatus status = network.Status!;
        Assert.AreEqual(WayStates.On, network.Ways.Single(w => w.Id == "lan").State);
        Assert.IsTrue(network.Ways.Count >= 13 && network.Protections.Count >= 6);
        foreach (var way in network.Ways.Concat(network.Protections))
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(way.StateChip.Text), way.Id);
            Assert.IsFalse(way.StateChip.Text.StartsWith('['), way.Id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(way.Does), way.Id + ": a way this app has no line for");
        }
        // The debug engine lists the RAVEN_LAB_* variables it sees (the tests set a pipe suffix).
        Assert.AreEqual(status.Build == "dev" && status.LabSwitches.Count > 0, network.ShowLabBanner);
        if (network.ShowLabBanner)
            StringAssert.Contains(network.LabBannerText, status.LabSwitches[0]);
        Assert.IsNotNull(network.Ways.Single(w => w.Id == "nearby").Note, "Windows: nearby stays inactive");

        var check = new Raven.Windows.Core.ViewModels.NetworkCheckViewModel(engine);
        await check.RunAsync();
        Assert.IsNull(check.Error, check.Error?.Detail);
        Assert.IsTrue(check.Summary.Count > 0);
        Assert.IsNull(check.RouterLines, "the router is asked only on request");

        var security = new Raven.Windows.Core.ViewModels.SecurityViewModel(engine);
        await security.LoadAsync();
        Assert.IsNull(security.SecurityError, security.SecurityError?.Detail);
        Assert.IsNull(security.UpdatesError, security.UpdatesError?.Detail);
        Assert.IsNull(security.Lock.Error, security.Lock.Error?.Detail);
        StringAssert.Contains(security.InstallText, "raven update install");
    }

    [TestMethod]
    public async Task EventsPoll_RunsBesideCalls_OnTheBackgroundEngine()
    {
        IRavenEngine inner = EngineLab.Engine();
        await EngineLab.SetUpProfileAsync(inner);
        var engine = new BackgroundEngine(inner);

        EventsPage first = await engine.EventsPollAsync(0).WaitAsync(Patience);
        Assert.IsTrue(first.Events.All(e => e.Seq > 0 && !string.IsNullOrEmpty(e.Kind)));

        // A call on the ordered queue and a poll beside it.
        Task<NodeStatus> status = engine.NodeStatusAsync();
        EventsPage second = await engine.EventsPollAsync(first.Seq).WaitAsync(Patience);
        Assert.IsTrue(second.Seq >= first.Seq);
        Assert.IsTrue(second.Events.All(e => e.Seq > first.Seq));

        NodeStatus node = await status.WaitAsync(Patience);
        Assert.IsFalse(node.Running, "no raven-node runs for the lab profile");
        Assert.IsFalse(node.StartedByApp);
    }

    [TestMethod]
    public async Task BadRequests_ComeBackAsRavenExceptions()
    {
        IRavenEngine engine = EngineLab.Engine();
        RavenException ex = await Assert.ThrowsExactlyAsync<RavenException>(() => engine.CallAsync("no.such.op"));
        Assert.AreEqual(RavenErrorCodes.BadRequest, ex.Code);
        Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message));
    }
}

/// <summary>
/// The engine tests' environment. raven_client reads its profile folder and secret-store
/// choice from the process environment on its first call, so <see cref="SetUp"/> runs before
/// any test: a throwaway profile, the file-backed stores, loopback-only listening, and a
/// raven-node pipe of its own (on Windows the pipe is per user, not per profile; the lab
/// suffix, honoured by debug engine builds, keeps this engine off the user's own raven-node).
/// </summary>
[TestClass]
public sealed class EngineLab
{
    public const string RequireVariable = "RAVEN_REQUIRE_ENGINE_TESTS";
    public const string DisplayName = "Desk Test";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _root;
    private static Setup? _setup;

    /// <summary>What the engine said before and while the lab identity was made.</summary>
    public sealed record Setup(ProfileState First, RavenSettings Chosen, IdentityCreated Created);

    /// <summary>The throwaway profile (RAVEN_DATA_DIR).</summary>
    public static string ProfileDir => Path.Combine(_root ?? throw new InvalidOperationException("EngineLab.SetUp did not run"), "profile");

    public static string LibraryPath => Path.Combine(AppContext.BaseDirectory, RavenClientEngine.LibraryFileName);

    [AssemblyInitialize]
    public static void SetUp(TestContext context)
    {
        _ = context;
        foreach (string key in new[] { "RAVEN_IDENTITY_BACKEND", "RAVEN_SESSION_BACKEND", "RAVEN_PREKEY_BACKEND", "RAVEN_CHAT_HISTORY_BACKEND" })
            Environment.SetEnvironmentVariable(key, "locked-file");
        Environment.SetEnvironmentVariable("RAVEN_ALLOW_EPHEMERAL_DATA_DIR", "1");
        Environment.SetEnvironmentVariable("RAVEN_SERVICE_LAN_LISTEN", "127.0.0.1:0");
        Environment.SetEnvironmentVariable("RAVEN_LAB_IPC_PIPE_SUFFIX", "desktop-tests-" + Environment.ProcessId);
        foreach (string key in new[]
        {
            "ASH_DATA_DIR", "RAVEN_PEER", "ASH_LAN_DIAL", "RAVEN_VERBOSE", "ASH_VERBOSE", "RAVEN_LAB_TEST_A",
            "RAVEN_INTERNET_LISTEN", "RAVEN_P2P_LISTEN", "RAVEN_P2P_RELAYS", "RAVEN_P2P_RELAY", "RAVEN_TRACE_LOG",
            // The privacy pack's lab switches (raven-core's PRIVACY_LAB_SWITCHES): a plain profile,
            // whatever the suite runs with (raven-client's own tests cover sealed ones).
            "RAVEN_LAB_AT_REST", "RAVEN_LAB_ERASE_ROOT", "RAVEN_LAB_AT_REST_KEY_FAULT",
            "RAVEN_LAB_RETENTION_GRACE_MS", "RAVEN_LAB_LOGIN_ITEM_ROOT", "RAVEN_LAB_LOGIN_ITEM_TOOLS",
        })
        {
            Environment.SetEnvironmentVariable(key, null);
        }
        _root = Path.Combine(Path.GetTempPath(), $"raven-desktop-tests-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("RAVEN_DATA_DIR", ProfileDir);
    }

    [AssemblyCleanup]
    public static void CleanUp()
    {
        if (_root is null) return;
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // The engine may still hold a file open; the folder is in the temp folder.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The real engine, or an inconclusive (CI: failed) test when its library is missing.</summary>
    public static IRavenEngine Engine()
    {
        string? why = null;
        if (!OperatingSystem.IsWindows())
            why = "The engine tests need Windows (raven_client.dll).";
        else if (!File.Exists(LibraryPath))
            why = $"{RavenClientEngine.LibraryFileName} is not next to the tests: build the engine's debug library in node/ (cargo build -p raven-client), then build the tests again.";
        if (why is not null)
        {
            if (Environment.GetEnvironmentVariable(RequireVariable) == "1") Assert.Fail(why);
            Assert.Inconclusive(why);
        }
        return new RavenClientEngine();
    }

    /// <summary>
    /// Once per test run, whichever test asks first: profile.state of the new profile, a setting
    /// chosen during setup (receive off), then identity.create, as the Setup page does.
    /// </summary>
    public static async Task<Setup> SetUpProfileAsync(IRavenEngine engine)
    {
        await Gate.WaitAsync();
        try
        {
            if (_setup is null)
            {
                ProfileState first = await engine.ProfileStateAsync();
                RavenSettings chosen = await engine.SettingsSetAsync(new JsonObject { ["receive"] = false });
                IdentityCreated created = await engine.IdentityCreateAsync(DisplayName);
                _setup = new Setup(first, chosen, created);
            }
            return _setup;
        }
        finally
        {
            Gate.Release();
        }
    }
}
