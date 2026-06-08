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
            if (reg.NextFireTime == null)
                continue;

            if (!reg.Enabled)
            {
                if (now >= reg.NextFireTime.Value)
                    TriggerSkipped?.Invoke(reg.Id, "disabled");
                continue;
            }

            // Calculate effective fire time with stagger offset
            var effectiveFireTime = reg.NextFireTime.Value;
            if (reg.Expression.Options.Stagger.HasValue)
            {
                var staggerOffset = ComputeStaggerOffset(reg.Id, reg.Expression.Options.Stagger.Value.Value);
                effectiveFireTime = effectiveFireTime.Add(staggerOffset);
            }

            // M-2: Apply jitter
            if (reg.Expression.Options.Jitter.HasValue)
            {
                var jitterMs = (long)reg.Expression.Options.Jitter.Value.Value.TotalMilliseconds;
                if (jitterMs > 0)
                {
                    var jitterDelay = TimeSpan.FromMilliseconds(Random.Shared.NextInt64(jitterMs));
                    effectiveFireTime = effectiveFireTime.Add(jitterDelay);
                }
            }

            if (now >= effectiveFireTime)
            {
                // Check max
                if (reg.Expression.Options.Max.HasValue && reg.FireCount >= reg.Expression.Options.Max.Value)
                {
                    TriggerSkipped?.Invoke(reg.Id, "max reached");
                    reg.NextFireTime = null;
                    continue;
                }

                var scheduledTime = reg.NextFireTime.Value;

                // C-3: Set NextFireTime to null before handler to prevent double-fire
                reg.NextFireTime = null;

                // Issue 5: Window check against nominal scheduled time only (not widened by jitter)
                if (reg.Expression.Options.Window.HasValue)
                {
                    var windowEnd = scheduledTime.Add(reg.Expression.Options.Window.Value.Value);
                    if (now > windowEnd)
                    {
                        TriggerSkipped?.Invoke(reg.Id, "window exceeded");
                        reg.NextFireTime = reg.Expression.GetNextOccurrence(scheduledTime);
                        continue;
                    }
                }

                var actualTime = now;

                Interlocked.Increment(ref reg._fireCount);
                reg.LastFired = actualTime;

                var context = new TriggerContext(reg.Id, scheduledTime, actualTime, reg.FireCount, reg.Expression, reg.Metadata);

                TriggerFiring?.Invoke(context);

                try
                {
                    await reg.Handler(context, ct);
                    TriggerCompleted?.Invoke(context);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Issue 2: Restore NextFireTime before propagating cancellation
                    reg.NextFireTime = reg.Expression.GetNextOccurrence(scheduledTime);
                    throw;
                }
                catch (Exception ex)
                {
                    // C-4: Fallback to Trace when no subscribers
                    if (TriggerFailed != null)
                        TriggerFailed.Invoke(context, ex);
                    else
                        System.Diagnostics.Trace.TraceError(
                            "Cronex trigger '{0}' failed: {1}", reg.Id, ex);
                }

                // Calculate next fire time
                reg.NextFireTime = reg.Expression.GetNextOccurrence(scheduledTime);

                // Check if max reached after firing
                if (reg.Expression.Options.Max.HasValue && reg.FireCount >= reg.Expression.Options.Max.Value)
                    reg.NextFireTime = null;
            }
        }
    }

    private async Task TickLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await TickAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, ct);
        }
    }

    /// <summary>
    /// Computes a deterministic stagger offset based on the trigger ID.
    /// The offset is hash(id) % stagger, producing a fixed value for the same ID.
    /// </summary>
    private static TimeSpan ComputeStaggerOffset(string triggerId, TimeSpan stagger)
    {
        var hash = (uint)triggerId.GetHashCode(StringComparison.Ordinal);
        var staggerMs = (long)stagger.TotalMilliseconds;
        if (staggerMs <= 0) return TimeSpan.Zero;
        var offsetMs = (long)(hash % (ulong)staggerMs);
        return TimeSpan.FromMilliseconds(offsetMs);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Issue 1: Atomic disposed guard with Interlocked
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await StopAsync();
    }
}
