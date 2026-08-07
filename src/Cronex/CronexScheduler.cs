using System.Collections.Concurrent;

namespace Cronex;

/// <summary>
/// The core scheduler that manages trigger registrations and fires them on schedule.
/// Uses TimeProvider for testability.
/// </summary>
public sealed class CronexScheduler : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, TriggerRegistration> _triggers = new();
    private readonly TimeProvider _timeProvider;
    private CancellationTokenSource? _cts;
    private Task? _tickLoop;
    private int _started; // 0 = stopped, 1 = running (C-2: atomic guard)
    private int _disposed; // Issue 1: int for Interlocked thread safety

    /// <summary>
    /// Creates a new scheduler with the specified TimeProvider.
    /// </summary>
    /// <param name="timeProvider">The time provider for getting current time. Defaults to system time.</param>
    public CronexScheduler(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Fired when a trigger is about to execute.</summary>
    public event Action<TriggerContext>? TriggerFiring;

    /// <summary>Fired when a trigger handler completes successfully.</summary>
    public event Action<TriggerContext>? TriggerCompleted;

    /// <summary>Fired when a trigger handler throws an exception.</summary>
    public event Action<TriggerContext, Exception>? TriggerFailed;

    /// <summary>Fired when a trigger occurrence is skipped (window exceeded, disabled, etc.).</summary>
    public event Action<string, string>? TriggerSkipped;

    /// <summary>
    /// Fired when the internal tick loop catches an unexpected error while ticking. The loop keeps
    /// running after this — it is a signal to observers, not a shutdown notice. A subscriber
    /// throwing from any event above no longer stops the loop; this event is how you find out it
    /// happened.
    /// </summary>
    public event Action<Exception>? SchedulerFaulted;

    /// <summary>Whether the automatic tick loop (started via <see cref="Start"/>) is currently running.</summary>
    public bool IsRunning => Volatile.Read(ref _started) == 1;

    /// <summary>
    /// Registers a trigger with the scheduler.
    /// </summary>
    /// <param name="id">Unique identifier for this trigger.</param>
    /// <param name="expression">The Cronex expression string.</param>
    /// <param name="handler">The async handler to invoke when the trigger fires.</param>
    /// <param name="referenceTime">Reference time for relative @once expressions.</param>
    /// <returns>The trigger registration.</returns>
    public TriggerRegistration Register(string id, string expression,
        Func<TriggerContext, CancellationToken, Task> handler,
        DateTimeOffset? referenceTime = null)
    {
        var expr = CronexExpression.Parse(expression, referenceTime);
        return Register(id, expr, handler);
    }

    /// <summary>
    /// Registers a trigger with a pre-parsed expression.
    /// </summary>
    public TriggerRegistration Register(string id, CronexExpression expression,
        Func<TriggerContext, CancellationToken, Task> handler)
    {
        var reg = new TriggerRegistration(id, expression, handler);
        var now = _timeProvider.GetUtcNow();
        reg.NextFireTime = expression.GetNextOccurrence(now);

        if (!_triggers.TryAdd(id, reg))
            throw new InvalidOperationException($"Trigger '{id}' is already registered");

        return reg;
    }

    /// <summary>
    /// Registers a trigger from a TriggerDefinition (JSON-serializable spec).
    /// The definition provides id, expression, enabled, and metadata.
    /// The handler is bound by the consuming application.
    /// </summary>
    public TriggerRegistration Register(TriggerDefinition definition,
        Func<TriggerContext, CancellationToken, Task> handler)
    {
        var expr = CronexExpression.Parse(definition.Expression);
        var reg = new TriggerRegistration(definition.Id, expr, handler, definition.Metadata);
        var now = _timeProvider.GetUtcNow();
        reg.NextFireTime = expr.GetNextOccurrence(now);
        reg.Enabled = definition.Enabled;

        if (!_triggers.TryAdd(definition.Id, reg))
            throw new InvalidOperationException($"Trigger '{definition.Id}' is already registered");

        return reg;
    }

    /// <summary>
    /// Unregisters a trigger by ID.
    /// </summary>
    /// <returns>True if the trigger was found and removed.</returns>
    public bool Unregister(string id)
    {
        return _triggers.TryRemove(id, out _);
    }

    /// <summary>
    /// Gets a registered trigger by ID.
    /// </summary>
    public TriggerRegistration? GetTrigger(string id)
    {
        return _triggers.TryGetValue(id, out var reg) ? reg : null;
    }

    /// <summary>
    /// Gets all registered triggers.
    /// </summary>
    public IReadOnlyCollection<TriggerRegistration> GetTriggers()
    {
        return _triggers.Values.ToArray();
    }

    /// <summary>
    /// Enables or disables a trigger.
    /// </summary>
    public void SetEnabled(string id, bool enabled)
    {
        if (_triggers.TryGetValue(id, out var reg))
            reg.Enabled = enabled;
    }

    /// <summary>
    /// Starts the scheduler tick loop.
    /// </summary>
    public void Start()
    {
        // Issue 1: Use Volatile.Read for thread-safe disposed check
        if (Volatile.Read(ref _disposed) == 1)
            throw new ObjectDisposedException(nameof(CronexScheduler));

        // C-2: Atomic guard — only one tick loop can be created
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            return;

        _cts = new CancellationTokenSource();
        _tickLoop = TickLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Stops the scheduler.
    /// </summary>
    public async Task StopAsync()
    {
        // Issue 4: Atomic guard — only one caller proceeds to tear down
        if (Interlocked.CompareExchange(ref _started, 0, 1) != 1)
            return;

        var cts = _cts;
        var tickLoop = _tickLoop;
        _cts = null;
        _tickLoop = null;

        if (cts == null) return;

        try
        {
            await cts.CancelAsync();
            if (tickLoop != null)
                await tickLoop;
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        finally
        {
            cts.Dispose();
        }
    }

    /// <summary>
    /// Manually ticks the scheduler — checks all triggers and fires those that are due.
    /// Useful for testing with a controlled TimeProvider.
    /// </summary>
    public async Task TickAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();

        foreach (var reg in _triggers.Values)
        {
            var nextFireTime = reg.NextFireTime;
            if (nextFireTime == null)
                continue;

            if (!reg.Enabled)
            {
                if (now >= nextFireTime.Value)
                {
                    SafeInvoke(() => TriggerSkipped?.Invoke(reg.Id, "disabled"), nameof(TriggerSkipped));
                    // E-1: Fast-forward past every occurrence already missed while disabled, not
                    // just the one just observed — otherwise a trigger disabled for a long stretch
                    // re-reports "disabled" once per missed occurrence as later ticks each walk one
                    // step at a time, instead of catching up to `now` in a single tick.
                    var advanced = reg.Expression.GetNextOccurrence(nextFireTime.Value);
                    while (advanced.HasValue && advanced.Value <= now)
                        advanced = reg.Expression.GetNextOccurrence(advanced.Value);
                    reg.NextFireTime = advanced;
                }
                continue;
            }

            // Calculate effective fire time with stagger offset
            var effectiveFireTime = nextFireTime.Value;
            if (reg.Expression.Options.Stagger.HasValue)
            {
                var staggerOffset = ComputeStaggerOffset(reg.Id, reg.Expression.Options.Stagger.Value.Value);
                effectiveFireTime = effectiveFireTime.Add(staggerOffset);
            }

            // M-2 / J-1: Apply jitter — drawn once when NextFireTime was set (TriggerRegistration),
            // not re-rolled here on every tick that observes the same still-pending occurrence.
            if (reg.JitterOffset.HasValue)
                effectiveFireTime = effectiveFireTime.Add(reg.JitterOffset.Value);

            if (now < effectiveFireTime)
                continue;

            // Check max
            if (reg.Expression.Options.Max.HasValue && reg.FireCount >= reg.Expression.Options.Max.Value)
            {
                // D-1: claim before reporting so a concurrent TickAsync call can't also observe
                // and report the same terminal transition.
                if (reg.TryClaim(nextFireTime.Value))
                    SafeInvoke(() => TriggerSkipped?.Invoke(reg.Id, "max reached"), nameof(TriggerSkipped));
                continue;
            }

            // D-1: Atomically claim this occurrence. Two concurrent TickAsync calls (e.g. a manual
            // call racing the automatic loop) can both observe the same due NextFireTime; only the
            // one that wins the compare-and-swap proceeds, so the trigger fires at most once.
            if (!reg.TryClaim(nextFireTime.Value))
                continue;

            var scheduledTime = nextFireTime.Value;

            // MF-1: Misfire/catchup policy — decide what to do when we're behind schedule (a later
            // occurrence is also already due). Default (All, or unset) fires every missed occurrence
            // one per tick, unchanged from before this option existed.
            var catchup = reg.Expression.Options.Catchup ?? CatchupPolicy.All;
            if (catchup != CatchupPolicy.All)
            {
                var latestDue = scheduledTime;
                var nextAfterLatest = reg.Expression.GetNextOccurrence(latestDue);
                while (nextAfterLatest.HasValue && nextAfterLatest.Value <= now)
                {
                    latestDue = nextAfterLatest.Value;
                    nextAfterLatest = reg.Expression.GetNextOccurrence(latestDue);
                }

                if (latestDue != scheduledTime)
                {
                    if (catchup == CatchupPolicy.Skip)
                    {
                        SafeInvoke(() => TriggerSkipped?.Invoke(reg.Id, "catchup skip"), nameof(TriggerSkipped));
                        reg.NextFireTime = nextAfterLatest;
                        continue;
                    }

                    // Once: fire only the most recent missed occurrence; the rest of the backlog
                    // between the original scheduledTime and latestDue is discarded, not fired.
                    scheduledTime = latestDue;
                }
            }

            // Issue 5: Window check against nominal scheduled time only (not widened by jitter)
            if (reg.Expression.Options.Window.HasValue)
            {
                var windowEnd = scheduledTime.Add(reg.Expression.Options.Window.Value.Value);
                if (now > windowEnd)
                {
                    SafeInvoke(() => TriggerSkipped?.Invoke(reg.Id, "window exceeded"), nameof(TriggerSkipped));
                    reg.NextFireTime = reg.Expression.GetNextOccurrence(scheduledTime);
                    continue;
                }
            }

            var actualTime = now;

            Interlocked.Increment(ref reg._fireCount);
            reg.LastFired = actualTime;

            var context = new TriggerContext(reg.Id, scheduledTime, actualTime, reg.FireCount, reg.Expression, reg.Metadata);

            SafeInvoke(() => TriggerFiring?.Invoke(context), nameof(TriggerFiring));

            Exception? handlerException = null;
            try
            {
                await reg.Handler(context, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Issue 2: Restore NextFireTime before propagating cancellation
                reg.NextFireTime = reg.Expression.GetNextOccurrence(scheduledTime);
                throw;
            }
            catch (Exception ex)
            {
                handlerException = ex;
            }

            // C-1: TriggerCompleted/TriggerFailed now reflect only the handler's own outcome — a
            // subscriber throwing from either no longer gets misattributed as a handler failure.
            if (handlerException == null)
            {
                SafeInvoke(() => TriggerCompleted?.Invoke(context), nameof(TriggerCompleted));
            }
            else if (TriggerFailed != null)
            {
                // C-4: Fallback to Trace when no subscribers
                SafeInvoke(() => TriggerFailed.Invoke(context, handlerException), nameof(TriggerFailed));
            }
            else
            {
                System.Diagnostics.Trace.TraceError(
                    "Cronex trigger '{0}' failed: {1}", reg.Id, handlerException);
            }

            // Calculate next fire time
            reg.NextFireTime = reg.Expression.GetNextOccurrence(scheduledTime);

            // Check if max reached after firing
            if (reg.Expression.Options.Max.HasValue && reg.FireCount >= reg.Expression.Options.Max.Value)
                reg.NextFireTime = null;
        }
    }

    private async Task TickLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await TickAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // B-1: A tick failure (including an event subscriber that escaped SafeInvoke, or a
                // defect in Cronex itself) must not silently kill the loop task — that leaves the
                // scheduler permanently stopped with _started still 1, so Start() is a no-op and
                // nobody observes it. Report and keep ticking.
                SafeInvoke(() => SchedulerFaulted?.Invoke(ex), nameof(SchedulerFaulted));
            }

            try
            {
                await Task.Delay(ComputeNextPollDelay(), _timeProvider, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Poll interval floor — avoids a busy loop when a trigger is imminently due.</summary>
    private static readonly TimeSpan MinPollInterval = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Poll interval ceiling — bounds how stale the "next due time" snapshot can get, so a newly
    /// registered or re-enabled trigger is never picked up more than this long after the fact.
    /// </summary>
    private static readonly TimeSpan MaxPollInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// P-1: Computes how long the tick loop should sleep before its next poll, based on the nearest
    /// upcoming effective fire time (nominal occurrence + stagger + jitter) across all registered
    /// triggers, clamped to [<see cref="MinPollInterval"/>, <see cref="MaxPollInterval"/>]. Replaces
    /// a fixed 1-second delay, which capped sub-second `window`/`jitter` precision at 1 second and
    /// let "1s + handler time" compound into growing lateness with no correction.
    /// </summary>
    internal TimeSpan ComputeNextPollDelay()
    {
        var now = _timeProvider.GetUtcNow();
        DateTimeOffset? earliest = null;

        foreach (var reg in _triggers.Values)
        {
            var nextFireTime = reg.NextFireTime;
            if (nextFireTime == null)
                continue;

            var effectiveFireTime = nextFireTime.Value;
            if (reg.Expression.Options.Stagger.HasValue)
                effectiveFireTime = effectiveFireTime.Add(
                    ComputeStaggerOffset(reg.Id, reg.Expression.Options.Stagger.Value.Value));
            if (reg.JitterOffset.HasValue)
                effectiveFireTime = effectiveFireTime.Add(reg.JitterOffset.Value);

            if (earliest == null || effectiveFireTime < earliest.Value)
                earliest = effectiveFireTime;
        }

        if (earliest == null)
            return MaxPollInterval;

        var wait = earliest.Value - now;
        if (wait < MinPollInterval) return MinPollInterval;
        if (wait > MaxPollInterval) return MaxPollInterval;
        return wait;
    }

    /// <summary>
    /// Invokes an event delegate, isolating the tick loop from subscriber exceptions. A throwing
    /// subscriber is a bug in the consumer's code, not a reason to stop scheduling every trigger.
    /// </summary>
    private static void SafeInvoke(Action invoke, string eventName)
    {
        try
        {
            invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError(
                "Cronex event subscriber for '{0}' threw: {1}", eventName, ex);
        }
    }

    /// <summary>
    /// Computes a deterministic stagger offset based on the trigger ID.
    /// The offset is hash(id) % stagger, producing a fixed value for the same ID.
    /// </summary>
    private static TimeSpan ComputeStaggerOffset(string triggerId, TimeSpan stagger)
    {
        var hash = Fnv1a32(triggerId);
        var staggerMs = (long)stagger.TotalMilliseconds;
        if (staggerMs <= 0) return TimeSpan.Zero;
        var offsetMs = (long)(hash % (ulong)staggerMs);
        return TimeSpan.FromMilliseconds(offsetMs);
    }

    /// <summary>
    /// FNV-1a over the trigger ID's UTF-8 bytes. <c>string.GetHashCode()</c> — even the
    /// <c>StringComparison</c> overload — is randomized per process (Marvin32 with a random seed;
    /// the overload only changes comparison rules, not the seed), so it cannot back a "same ID,
    /// same offset across restarts and machines" contract. FNV-1a has no seed: same bytes in,
    /// same value out, everywhere.
    /// </summary>
    private static uint Fnv1a32(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= prime;
        }
        return hash;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Issue 1: Atomic disposed guard with Interlocked
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await StopAsync();
    }
}
