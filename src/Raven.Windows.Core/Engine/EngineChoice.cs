using System;
using System.Collections.Generic;
using System.Linq;

namespace Raven.Windows.Core.Engine;

/// <summary>
/// Which engine the app runs on. Release builds always use Raven's engine
/// (<see cref="RavenClientEngine"/>, raven_client.dll); MockRaven is not compiled into them.
/// Debug builds use Raven's engine too, unless the demo is asked for on the command line:
/// <c>--demo</c>, or one of the mock's start states, <c>--mock-ready</c> (skips the first-run
/// screens), <c>--mock-locked</c> (opens on the Locked screen) and <c>--mock-lab</c> (ready,
/// with the network answers of a developer build with lab switches set).
/// </summary>
public static class EngineChoice
{
    public const string DemoArgument = "--demo";
    public const string MockReadyArgument = "--mock-ready";
    public const string MockLockedArgument = "--mock-locked";
    public const string MockLabArgument = "--mock-lab";

    /// <summary>True when the command line asks for the demo engine (MockRaven, Debug builds only).</summary>
    public static bool DemoRequested(IEnumerable<string> args)
        => args.Any(a => Has(a, DemoArgument) || Has(a, MockReadyArgument) || Has(a, MockLockedArgument) || Has(a, MockLabArgument));

    /// <summary>True when <paramref name="args"/> contains <paramref name="argument"/> (any letter case).</summary>
    public static bool Contains(IEnumerable<string> args, string argument)
        => args.Any(a => Has(a, argument));

    private static bool Has(string? arg, string argument)
        => string.Equals(arg?.Trim(), argument, StringComparison.OrdinalIgnoreCase);
}
