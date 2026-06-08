namespace Cronex;

/// <summary>
/// Represents a parsed @every interval schedule.
/// Supports fixed intervals (@every 30m) and range intervals (@every 1h-2h).
/// </summary>
public readonly struct IntervalSchedule : IEquatable<IntervalSchedule>
{
    /// <summary>The fixed interval duration, or the minimum duration for range intervals.</summary>
    public CronexDuration Interval { get; }

    /// <summary>The maximum duration for range intervals. Null for fixed intervals.</summary>
    public CronexDuration? MaxInterval { get; }

    /// <summary>Whether this is a range interval (@every min-max).</summary>
    public bool IsRange => MaxInterval.HasValue;

    private IntervalSchedule(CronexDuration interval, CronexDuration? maxInterval = null)
    {
        Interval = interval;
        MaxInterval = maxInterval;
    }

    /// <summary>
    /// Parses an interval body string (the part after "@every ").
    /// </summary>
    /// <param name="body">The interval body (e.g., "30m" or "1h-2h").</param>
    /// <exception cref="FormatException">Thrown when the format is invalid.</exception>
    public static IntervalSchedule Parse(string body)
    {
        if (!TryParse(body, out var result, out var error))
            throw new FormatException(error);
        return result;
    }

    /// <summary>
    /// Attempts to parse an interval body string.
    /// </summary>
    public static bool TryParse(string body, out IntervalSchedule result, out string? error)
    {
        result = default;
        error = null;

        if (string.IsNullOrWhiteSpace(body))
        {
            error = "every: duration body is empty";
            return false;
        }

        // Check for range format: duration-duration
        // Need to find the '-' that separates two durations, not part of a duration unit
        // Strategy: try to parse as range first by finding '-' between two valid durations
        var dashIdx = FindRangeDash(body);
        if (dashIdx > 0)
        {
            var minPart = body[..dashIdx];
            var maxPart = body[(dashIdx + 1)..];

            if (!CronexDuration.TryParse(minPart, out var min))
            {
                error = $"every: invalid min duration '{minPart}'";
                return false;
            }
            if (!CronexDuration.TryParse(maxPart, out var max))
            {
                error = $"every: invalid max duration '{maxPart}'";
                return false;
            }
            if (min.Value <= TimeSpan.Zero)
            {
                error = "every: min duration must be positive";
                return false;
            }
            if (max.Value <= TimeSpan.Zero)
            {
                error = "every: max duration must be positive";
                return false;
            }
            if (min.Value >= max.Value)
            {
                error = "every: min duration must be less than max";
                return false;
            }

            result = new IntervalSchedule(min, max);
            return true;
        }

        // Fixed interval
        if (!CronexDuration.TryParse(body, out var interval))
        {
            error = $"every: invalid duration '{body}'";
            return false;
        }
        if (interval.Value <= TimeSpan.Zero)
        {
            error = "every: duration must be positive";
            return false;
        }

        result = new IntervalSchedule(interval);
        return true;
    }

    /// <summary>
    /// Finds the dash that separates min-max in a range expression.
    /// Returns -1 if no range dash is found.
    /// The dash must be preceded by a letter (unit suffix) and followed by a digit.
    /// </summary>
    private static int FindRangeDash(string body)
    {
        for (var i = 1; i < body.Length - 1; i++)
        {
            if (body[i] == '-' && char.IsAsciiLetter(body[i - 1]) && char.IsAsciiDigit(body[i + 1]))
                return i;
        }
        return -1;
    }

    /// <inheritdoc />
    public bool Equals(IntervalSchedule other) =>
        Interval.Equals(other.Interval) && Equals(MaxInterval, other.MaxInterval);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is IntervalSchedule s && Equals(s);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Interval, MaxInterval);
    /// <summary>Equality operator.</summary>
    public static bool operator ==(IntervalSchedule left, IntervalSchedule right) => left.Equals(right);
    /// <summary>Inequality operator.</summary>
    public static bool operator !=(IntervalSchedule left, IntervalSchedule right) => !left.Equals(right);
}
