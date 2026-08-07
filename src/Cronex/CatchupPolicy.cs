namespace Cronex;

/// <summary>
/// Governs how a trigger handles occurrences that were missed while the scheduler wasn't ticking
/// them (a tick loop that was stopped, or a handler that was still running when several nominal
/// occurrences elapsed). Set via the <c>catchup</c> option, e.g. <c>{catchup:skip}</c>.
/// </summary>
public enum CatchupPolicy
{
    /// <summary>
    /// Fire every missed occurrence, one per tick, in order. This is the default — same behavior
    /// as if <c>catchup</c> were never specified.
    /// </summary>
    All,

    /// <summary>Discard the entire missed backlog without firing any of it; resume at the next occurrence strictly after now.</summary>
    Skip,

    /// <summary>Fire exactly once, for the most recent missed occurrence, then resume normally — the rest of the backlog is discarded.</summary>
    Once
}
