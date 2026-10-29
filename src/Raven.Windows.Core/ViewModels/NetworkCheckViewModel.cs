using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.ViewModels;

/// <summary>
/// One line of the check: Raven's plain sentence when it has one (<see cref="Title"/>), with the
/// doctor's own words ("key: value") as the smaller line.
/// </summary>
public sealed record CheckLine(string Title, string? Words)
{
    public static CheckLine Of(CheckRow row)
    {
        string words = row.Key.Length > 0 ? row.Key + ": " + row.Value : row.Value;
        return string.IsNullOrWhiteSpace(row.Plain) ? new CheckLine(words, null) : new CheckLine(row.Plain!, words);
    }
}

/// <summary>
/// Check network (network.check): raven doctor's summary in its order, its network lines with
/// Raven's plain sentences, and on request the router check (a read-only question to this
/// computer's router that maps nothing and stores nothing; up to about 4 s).
/// </summary>
public sealed class NetworkCheckViewModel
{
    private readonly IRavenEngine _engine;

    public NetworkCheckViewModel(IRavenEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public event Action? Changed;

    public NetworkCheck? Result { get; private set; }

    /// <summary>The doctor's summary rows as "label: value".</summary>
    public IReadOnlyList<CheckLine> Summary { get; private set; } = Array.Empty<CheckLine>();
    public string NextStep { get; private set; } = string.Empty;
    /// <summary>Raven's plain sentences for the doctor's lines (only those it has one for, each once).</summary>
    public IReadOnlyList<CheckLine> Lines { get; private set; } = Array.Empty<CheckLine>();

    /// <summary>Under "Technical details": the doctor's own lines, then the CLI's summary and next step (E1).</summary>
    public IReadOnlyList<string> Technical { get; private set; } = Array.Empty<string>();

    /// <summary>The router's answers in plain sentences; null until the router was asked.</summary>
    public IReadOnlyList<CheckLine>? RouterLines { get; private set; }

    /// <summary>The router's own lines, for "Technical details".</summary>
    public IReadOnlyList<string> RouterTechnical { get; private set; } = Array.Empty<string>();

    /// <summary>The plain sentences of <paramref name="rows"/>, each once; the raw lines never show here.</summary>
    public static IReadOnlyList<CheckLine> PlainLines(IEnumerable<CheckRow> rows) =>
        rows.Select(r => TextSanitizer.SingleLine(r.Plain))
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Select(p => new CheckLine(p, null))
            .ToList();

    /// <summary>A doctor line as it printed it ("key: value").</summary>
    public static string Raw(CheckRow row) => row.Key.Length > 0 ? row.Key + ": " + row.Value : row.Value;

    public bool Busy { get; private set; }

    /// <summary>True while the router is being asked (the slow part).</summary>
    public bool AskingRouter { get; private set; }
    public ErrorInfo? Error { get; private set; }

    /// <summary>Runs the check; <paramref name="router"/> also asks the router.</summary>
    public async Task RunAsync(bool router = false)
    {
        if (Busy) return;
        Busy = true;
        AskingRouter = router;
        Error = null;
        Changed?.Invoke();
        try
        {
            NetworkCheck check = await _engine.NetworkCheckAsync(router);
            Result = check;
            Summary = check.Summary
                .Select(r => new CheckLine(TextSanitizer.SingleLine(r.Label.Length > 0 ? r.Label : r.Key) + ": " + r.Value, null))
                .ToList();
            NextStep = check.NextStep;
            Lines = PlainLines(check.Rows);
            Technical = check.Rows.Select(Raw).Concat(check.Technical ?? new List<string>()).ToList();
            // A check without the router keeps the router's last answer on screen.
            if (check.Router is not null)
            {
                RouterLines = PlainLines(check.Router);
                RouterTechnical = check.Router.Select(Raw).ToList();
            }
        }
        catch (Exception ex)
        {
            Error = ErrorInfo.From(ex);
        }
        finally
        {
            Busy = false;
            AskingRouter = false;
            Changed?.Invoke();
        }
    }
}
