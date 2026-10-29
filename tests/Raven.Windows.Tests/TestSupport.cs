using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]

namespace Raven.Windows.Tests;

/// <summary>A clock the tests move by hand (MockRaven schedules its demo events on it).</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset start) => _now = start;

    public static ManualTimeProvider StartingAt2026() => new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

    public long NowMs => _now.ToUnixTimeMilliseconds();

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(int milliseconds) => _now = _now.AddMilliseconds(milliseconds);
}

/// <summary>Finds files of the source tree (this repository and the shared design in the core/ submodule) from the test output folder.</summary>
internal static class RepoPaths
{
    /// <summary>The repository root (the folder that holds Raven.Windows.sln), or null when not found.</summary>
    public static string? WindowsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Raven.Windows.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    public static string RequireWindowsRoot()
    {
        string? root = WindowsRoot();
        if (root is null) Assert.Inconclusive("Source tree not found next to the test output (Raven.Windows.sln).");
        return root!;
    }

    public static string DesignFile(string name) => Path.Combine(RequireWindowsRoot(), "core", "desktop", "design", name);
}
