using System;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.Engine;

/// <summary>
/// The real engine: Raven's shared Rust library <c>raven_client.dll</c>
/// (node/crates/raven-client), called through its single C function <c>raven_call()</c>
/// (desktop/api/API.md, "Boundary"; node/crates/raven-client/include/raven_client.h).
/// <para>
/// The library is loaded from the folder of this assembly (next to <c>Raven.exe</c>; the app
/// project copies it there from node/target/release when it has been built). Without it,
/// or with a library of another architecture, every call fails with NOT_INITIALIZED, so the
/// app opens on its error screen instead of crashing.
/// </para>
/// <para>
/// <c>raven_call()</c> is thread-safe but may block (node.start takes a few seconds), so the
/// app wraps this engine in <see cref="BackgroundEngine"/>: calls run on the thread pool and
/// this class simply calls the library on that thread. The engine picks its profile
/// (<c>RAVEN_DATA_DIR</c>, else the CLI's <c>%USERPROFILE%\.raven</c>) on the first call.
/// </para>
/// </summary>
public sealed class RavenClientEngine : IRavenEngine
{
    /// <summary>File name of the engine library next to Raven.exe.</summary>
    public const string LibraryFileName = "raven_client.dll";

    private readonly Func<string, string> _call;

    /// <summary>The engine in raven_client.dll.</summary>
    public RavenClientEngine()
        : this(NativeMethods.Call)
    {
    }

    /// <summary>Tests: the same envelope handling over another <c>raven_call</c>.</summary>
    internal RavenClientEngine(Func<string, string> call)
    {
        _call = call ?? throw new ArgumentNullException(nameof(call));
    }

    public Task<JsonObject> CallAsync(string op, JsonObject? args = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<JsonObject>(cancellationToken);
        try
        {
            string request = RavenEnvelope.BuildRequest(op, args);
            return Task.FromResult(RavenEnvelope.ParseResponse(_call(request)));
        }
        catch (RavenException ex)
        {
            return Task.FromException<JsonObject>(ex);
        }
        catch (DllNotFoundException ex)
        {
            return Task.FromException<JsonObject>(EngineMissing(
                $"{LibraryFileName} was not found next to the app: {ex.Message}", ex));
        }
        catch (EntryPointNotFoundException ex)
        {
            return Task.FromException<JsonObject>(EngineMissing(
                $"{LibraryFileName} has no raven_call/raven_free (not Raven's engine library): {ex.Message}", ex));
        }
        catch (BadImageFormatException ex)
        {
            return Task.FromException<JsonObject>(EngineMissing(
                $"{LibraryFileName} is for another processor architecture than this app ({RuntimeInformation.ProcessArchitecture}): {ex.Message}", ex));
        }
    }

    private static RavenException EngineMissing(string detail, Exception inner)
        => new(RavenErrorCodes.NotInitialized, Loc.Get("error.engineMissing"), detail, inner);

    /// <summary>
    /// P/Invoke declarations for <c>raven_client.h</c>:
    /// <code>
    /// char *raven_call(const char *request_json);   /* returns a JSON response; never NULL */
    /// void  raven_free(char *response);             /* frees a string returned by raven_call */
    /// </code>
    /// Strings cross the boundary as NUL-terminated UTF-8. The response is returned as a raw
    /// pointer (not a marshalled string) because only <c>raven_free</c> may release it. The
    /// library is looked up only in this assembly's folder and the safe system folders, never
    /// in the current directory or on PATH.
    /// </summary>
    internal static class NativeMethods
    {
        private const string Library = "raven_client";
        private const DllImportSearchPath SearchPath = DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories;

        [DllImport(Library, EntryPoint = "raven_call", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(SearchPath)]
        internal static extern IntPtr raven_call([MarshalAs(UnmanagedType.LPUTF8Str)] string requestJson);

        [DllImport(Library, EntryPoint = "raven_free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(SearchPath)]
        internal static extern void raven_free(IntPtr response);

        /// <summary>One request in, one response out; the native string is always freed.</summary>
        internal static string Call(string requestJson)
        {
            IntPtr response = raven_call(requestJson);
            if (response == IntPtr.Zero)
                throw RavenException.Protocol("raven_call returned NULL (the contract says it never does)");
            try
            {
                return Marshal.PtrToStringUTF8(response) ?? string.Empty;
            }
            finally
            {
                raven_free(response);
            }
        }
    }
}
