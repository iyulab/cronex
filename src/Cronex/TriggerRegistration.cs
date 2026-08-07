namespace Cronex;

/// <summary>
/// Represents a registered trigger in the scheduler.
/// </summary>
public sealed class TriggerRegistration
{
    /// <summary>Unique trigger identifier.</summary>
    public string Id { get; }

    // Issue 3: volatile fields for thread-safe reads across threads
    private volatile bool _enabled = true;
    private DateTimeOffset? _nextFireTime;
    private DateTimeOffset? _lastFired;
    private TimeSpan? _jitterOffset;
    private CronexExpression _expression;
    private Func<TriggerContext, CancellationToken, Task> _handler;

    /// <summary>
    /// The parsed expression for this trigger. Settable internally by
    /// <see cref="CronexScheduler.Update(string, CronexExpression)"/> — lock-protected like the
    /// other fields that become mutable after construction, since it was originally
    /// construction-only and other members (<see cref="JitterOffset"/>'s draw) read it.
    /// </summary>
    public CronexExpression Expression
    {
        get { lock (this) { return _expression; } }
        internal set { lock (this) { _expression = value; } }
    }

    /// <summary>The handler to invoke when the trigger fires.</summary>
    internal Func<TriggerContext, CancellationToken, Task> Handler
    {
        get { lock (this) { return _handler; } }
        set { lock (this) { _handler = value; } }
    }

    /// <summary>Whether this trigger is enabled.</summary>
    public bool Enabled
    {
        get => _enabled;
        internal set => _enabled = value;
    }

    /// <summary>The last time this trigger fired.</summary>
    public DateTimeOffset? LastFired
    {
        get { lock (this) { return _lastFired; } }
        internal set { lock (this) { _lastFired = value; } }
    }

    /// <summary>
    /// The next scheduled fire time. Setting this to a non-null value redraws
    /// <see cref="JitterOffset"/> once for that occurrence (if the expression has a jitter option) —
    /// every call site that advances the schedule shares this one draw point, so jitter is never
    /// re-rolled on every poll of the same still-pending occurrence.
    /// </summary>
    public DateTimeOffset? NextFireTime
    {
        get { lock (this) { return _nextFireTime; } }
        internal set
        {
            lock (this)
            {
                _nextFireTime = value;
                _jitterOffset = value.HasValue ? DrawJitterOffset() : null;
            }
        }
    }

    /// <summary>
    /// The jitter delay drawn for the current <see cref="NextFireTime"/>, or <see langword="null"/>
    /// if the expression has no jitter option. Fixed for the lifetime of this occurrence — drawn
    /// once when <see cref="NextFireTime"/> is set, not on every tick that observes it.
    /// </summary>
    internal TimeSpan? JitterOffset
    {
        get { lock (this) { return _jitterOffset; } }
    }

    private TimeSpan? DrawJitterOffset()
    {
        var jitter = Expression.Options.Jitter;
        if (!jitter.HasValue)
            return null;

        var jitterMs = (long)jitter.Value.Value.TotalMilliseconds;
        return jitterMs > 0
            ? TimeSpan.FromMilliseconds(Random.Shared.NextInt64(jitterMs))
            : TimeSpan.Zero;
    }

    /// <summary>
    /// Atomically claims this occurrence for firing: succeeds only if <see cref="NextFireTime"/>
    /// still equals <paramref name="expected"/>, and clears it (and its drawn
    /// <see cref="JitterOffset"/>) as part of the same locked operation. Two concurrent callers
    /// racing a read-then-null-set (the previous behavior) could both observe the same due time and
    /// both fire; this collapses the check and the clear into one atomic step so only one caller
    /// wins.
    /// </summary>
    internal bool TryClaim(DateTimeOffset expected)
    {
        lock (this)
        {
            if (_nextFireTime != expected)
                return false;

            _nextFireTime = null;
            _jitterOffset = null;
            return true;
        }
    }

    // C-3: Backing field for Interlocked.Increment
    internal int _fireCount;

    /// <summary>Total number of times this trigger has fired.</summary>
    public int FireCount => Volatile.Read(ref _fireCount);

    /// <summary>Free-form metadata from the TriggerDefinition.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; }

    internal TriggerRegistration(string id, CronexExpression expression,
        Func<TriggerContext, CancellationToken, Task> handler,
        Dictionary<string, string>? metadata = null)
    {
        Id = id;
        _expression = expression;
        _handler = handler;
        Metadata = metadata?.AsReadOnly();
    }
}
