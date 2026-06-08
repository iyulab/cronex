using System.Globalization;

namespace Cronex;

/// <summary>
/// Represents a parsed @once one-shot schedule.
/// Supports absolute datetime (@once 2025-03-01T09:00:00+09:00) and
/// relative duration (@once +20m).
/// </summary>
public readonly struct OnceSchedule : IEquatable<OnceSchedule>
{
    /// <summary>The resolved absolute fire time.</summary>
    public DateTimeOffset FireAt { get; }

    /// <summary>Whether the original expression was relative (+duration).</summary>
    public bool WasRelative { get; }

    /// <summary>The original relative duration, if the expression was relative. Null for absolute.</summary>
    public CronexDuration? RelativeDuration { get; }

    private OnceSchedule(DateTimeOffset fireAt, bool wasRelative, CronexDuration? relativeDuration)
    {
        FireAt = fireAt;
        WasRelative = wasRelative;
        RelativeDuration = relativeDuration;
    }

    /// <summary>
    /// Parses a once body string (the part after "@once ").
    /// </summary>
    /// <param name="body">The once body (e.g., "2025-03-01T09:00:00+09:00" or "+20m").</param>
    /// <param name="referenceTime">Reference time for relative durations. Defaults to DateTimeOffset.UtcNow if null.</param>
    /// <exception cref="FormatException">Thrown when the format is invalid.</exception>
    public static OnceSchedule Parse(string body, DateTimeOffset? referenceTime = null)
    {
        if (!TryParse(body, out var result, out var error, referenceTime))
            throw new FormatException(error);
        return result;
    }

    /// <summary>
    /// Attempts to parse a once body string.
    /// </summary>
    public static bool TryParse(string body, out OnceSchedule result, out string? error,
        DateTimeOffset? referenceTime = null)
    {
        result = default;
        error = null;

        if (string.IsNullOrWhiteSpace(body))
        {
            error = "once: body is empty";
            return false;
        }

        // Relative: +duration
        if (body.StartsWith('+'))
        {
            var durationPart = body[1..];
            if (!CronexDuration.TryParse(durationPart, out var duration))
            {
                error = $"once: invalid relative duration '+{durationPart}'";
                return false;
            }
            if (duration.Value <= TimeSpan.Zero)
            {
                error = "once: relative duration must be positive";
                return false;
            }

            var refTime = referenceTime ?? DateTimeOffset.UtcNow;
            result = new OnceSchedule(refTime + duration.Value, wasRelative: true, duration);
            return true;
        }

        // Absolute: ISO 8601 datetime
        if (DateTimeOffset.TryParse(body, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            result = new OnceSchedule(dto, wasRelative: false, relativeDuration: null);
            return true;
        }

        error = $"once: invalid datetime format '{body}'";
        return false;
    }

    /// <inheritdoc />
    public bool Equals(OnceSchedule other) => FireAt.Equals(other.FireAt);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is OnceSchedule s && Equals(s);
    /// <inheritdoc />
    public override int GetHashCode() => FireAt.GetHashCode();
    /// <summary>Equality operator.</summary>
    public static bool operator ==(OnceSchedule left, OnceSchedule right) => left.Equals(right);
    /// <summary>Inequality operator.</summary>
    public static bool operator !=(OnceSchedule left, OnceSchedule right) => !left.Equals(right);
}
