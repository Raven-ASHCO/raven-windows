using System;
using System.Threading.Tasks;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.ViewModels;

/// <summary>
/// Lock v2 (lock.state / lock.now / lock.unlock): the background program keeps running and
/// forgets its keys; Unlock gives them back (with the Raven passphrase when the profile has
/// one; the passphrase goes only to the background program and is never kept here). Where
/// the background program doesn't serve it, both answer NOT_AVAILABLE_YET naming raven lock.
/// </summary>
public sealed class LockViewModel
{
    private readonly IRavenEngine _engine;

    public LockViewModel(IRavenEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public event Action? Changed;

    public LockState? State { get; private set; }
    public bool Busy { get; private set; }
    public ErrorInfo? Error { get; private set; }

    /// <summary>What unlock said (the background program's own words).</summary>
    public string? Note { get; private set; }

    public bool IsLocked => State?.Locked == true;

    /// <summary>Lock now works: this build has Lock v2 and the running background program serves it.</summary>
    public bool CanLockNow => State is { Available: true, Supported: true, Locked: false };

    /// <summary>Show the passphrase field unless the engine says the profile has none.</summary>
    public bool AsksPassphrase => State?.HasPassphrase != false;

    /// <summary>One plain sentence for the Lock section.</summary>
    public string StatusText => State switch
    {
        null => string.Empty,
        { Available: false } => Loc.Get("lock.notAvailable"),
        { Locked: true } => Loc.Get("lock.isLocked"),
        { Supported: false } => Loc.Get("lock.notSupported"),
        _ => Loc.Get("lock.ready"),
    };

    /// <summary>"On for testing only" when a lab switch opened Lock v2.</summary>
    public string? LabText => State?.Lab == true ? Loc.Get("network.labOnly") : null;

    public async Task LoadAsync()
    {
        Busy = true;
        Changed?.Invoke();
        try
        {
            State = await _engine.LockStateAsync();
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ErrorInfo.From(ex);
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>lock.now; true when Raven is locked afterwards.</summary>
    public async Task<bool> LockNowAsync()
    {
        if (Busy) return false;
        Busy = true;
        Error = null;
        Note = null;
        Changed?.Invoke();
        try
        {
            LockState state = await _engine.LockNowAsync();
            State = state;
            return state.Locked;
        }
        catch (Exception ex)
        {
            Error = ErrorInfo.From(ex);
            return false;
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// lock.unlock; true when Raven is unlocked afterwards. A wrong passphrase
    /// (PASSPHRASE_WRONG), a missing one (PASSPHRASE_NEEDED) or too many tries (UNLOCK_WAIT)
    /// come back as <see cref="Error"/> with the engine's words.
    /// </summary>
    public async Task<bool> UnlockAsync(string? passphrase)
    {
        if (Busy) return false;
        Busy = true;
        Error = null;
        Note = null;
        Changed?.Invoke();
        try
        {
            string? given = string.IsNullOrEmpty(passphrase) ? null : passphrase;
            LockState state = await _engine.LockUnlockAsync(given);
            State = state;
            Note = string.IsNullOrWhiteSpace(state.Note) ? null : state.Note;
            return !state.Locked;
        }
        catch (Exception ex)
        {
            Error = ErrorInfo.From(ex);
            return false;
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }
}
