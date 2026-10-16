using System.Diagnostics;

namespace ERP.MES.Desktop.Session;

/// <summary>
/// A clock that only moves forward and is not affected by changes of the system date or time (Decision Log #74), so inactivity can be neither bypassed nor
/// triggered early by changing the Windows clock. Tests supply a fake that they advance by hand, so no test sleeps.
/// </summary>
public interface IMonotonicClock
{
    TimeSpan Elapsed { get; }
}

/// <summary>The real monotonic clock (<see cref="Stopwatch"/>).</summary>
public sealed class StopwatchClock : IMonotonicClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public TimeSpan Elapsed => _stopwatch.Elapsed;
}

/// <summary>
/// A form (or other editor) that can hold changes the user has not saved. A form registers itself with the <see cref="UnsavedWorkRegistry"/> while it is open.
/// <see cref="CanSave"/> is true only where the form genuinely supports saving; Save is never offered for one that does not.
/// </summary>
public interface IUnsavedWork
{
    bool HasUnsavedChanges { get; }

    bool CanSave { get; }

    /// <summary>Saves the changes; false when saving did not succeed (the user stays in the session with the changes intact).</summary>
    Task<bool> SaveAsync(CancellationToken cancellationToken);

    /// <summary>Throws the unsaved changes away.</summary>
    void Discard();
}

public enum UnsavedDecision
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Asks the user what to do with unsaved changes. The text is generic and never reveals what the changes contain. Implemented by a message box in the application.</summary>
public interface IUnsavedWorkPrompt
{
    /// <param name="saveOffered">True only when every form that has unsaved changes can save; otherwise the choices are Discard and Cancel.</param>
    Task<UnsavedDecision> AskAsync(bool saveOffered);
}

/// <summary>Asks for the explicit confirmation needed before another user's unsaved drafts are discarded from the lock screen. The text must not reveal any data. False means cancel.</summary>
public interface IDiscardConfirmation
{
    Task<bool> ConfirmDiscardAsync();
}

/// <summary>The open forms that may hold unsaved changes. Nothing is ever written to disk to support locking: drafts stay in memory.</summary>
public sealed class UnsavedWorkRegistry
{
    private readonly List<IUnsavedWork> _forms = [];

    /// <summary>Registers a form for as long as the returned handle is not disposed.</summary>
    public IDisposable Register(IUnsavedWork form)
    {
        ArgumentNullException.ThrowIfNull(form);
        _forms.Add(form);
        return new Registration(this, form);
    }

    public bool HasUnsavedChanges => _forms.Any(f => f.HasUnsavedChanges);

    /// <summary>Save is offered only when every form with unsaved changes can save.</summary>
    public bool SaveIsOffered => _forms.Where(f => f.HasUnsavedChanges).All(f => f.CanSave);

    /// <summary>
    /// Settles the unsaved changes before a session is left: nothing to settle means go ahead. Otherwise the user decides: Save (every form with changes is saved; if
    /// any save fails the user stays), Discard (the changes are thrown away) or Cancel (nothing happens and the user stays). Returns true when it is fine to go on.
    /// </summary>
    public async Task<bool> ResolveAsync(IUnsavedWorkPrompt prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (!HasUnsavedChanges)
        {
            return true;
        }

        var decision = await prompt.AskAsync(SaveIsOffered);
        switch (decision)
        {
            case UnsavedDecision.Save when SaveIsOffered:
                foreach (var form in _forms.Where(f => f.HasUnsavedChanges).ToList())
                {
                    if (!await form.SaveAsync(cancellationToken))
                    {
                        return false;
                    }
                }

                return true;
            case UnsavedDecision.Discard:
                DiscardAll();
                return true;
            default:
                // Cancel - or Save answered although it was not offered: do nothing.
                return false;
        }
    }

    public void DiscardAll()
    {
        foreach (var form in _forms.Where(f => f.HasUnsavedChanges).ToList())
        {
            form.Discard();
        }
    }

    private sealed class Registration(UnsavedWorkRegistry owner, IUnsavedWork form) : IDisposable
    {
        public void Dispose() => owner._forms.Remove(form);
    }
}
