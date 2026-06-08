namespace Cronex;

/// <summary>
/// Context passed to trigger handlers when a trigger fires.
/// </summary>
public sealed class TriggerContext
{
    /// <summary>The trigger ID.</summary>
    public string TriggerId { get; }

    /// <summary>The scheduled fire time (before jitter/stagger adjustments).</summary>
    public DateTimeOffset ScheduledTime { get; }

    /// <summary>The actual fire time (after jitter/stagger adjustments).</summary>
    public DateTimeOffset ActualTime { get; }

    /// <summary>The fire count (1-based: 1 for first fire, 2 for second, etc.).</summary>
    public int FireCount { get; }

    /// <summary>The parsed expression.</summary>
    public CronexExpression Expression { get; }

    /// <summary>Free-form metadata from the TriggerDefinition. Empty if not provided.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new Dictionary<string, string>().AsReadOnly();

    internal TriggerContext(string triggerId, DateTimeOffset scheduledTime,
        DateTimeOffset actualTime, int fireCount, CronexExpression expression,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        TriggerId = triggerId;
        ScheduledTime = scheduledTime;
        ActualTime = actualTime;
        FireCount = fireCount;
        Expression = expression;
        Metadata = metadata ?? EmptyMetadata;
    }
}
