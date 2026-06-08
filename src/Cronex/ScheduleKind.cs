namespace Cronex;

/// <summary>
/// Identifies the kind of schedule body in a Cronex expression.
/// </summary>
public enum ScheduleKind
{
    /// <summary>Standard 5-field or 6-field cron expression.</summary>
    Cron,
    /// <summary>Alias like @daily, @hourly, @weekly.</summary>
    Alias,
    /// <summary>Interval: @every duration or @every min-max.</summary>
    Interval,
    /// <summary>One-shot: @once datetime or @once +duration.</summary>
    Once
}
