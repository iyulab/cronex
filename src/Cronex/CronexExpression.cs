namespace Cronex;

/// <summary>
/// Represents a parsed Cronex expression — the top-level parsed type.
/// Supports standard cron, aliases, intervals, one-shot, timezone, and options.
/// </summary>
public sealed partial class CronexExpression
{
    /// <summary>The original expression string.</summary>
    public string Original { get; }

    /// <summary>The kind of schedule (Cron, Alias, Interval, Once).</summary>
    public ScheduleKind Kind { get; }

    /// <summary>IANA timezone identifier, if specified via TZ= prefix. Null means UTC/local.</summary>
    public string? Timezone { get; }

    /// <summary>TimeZoneInfo resolved from the Timezone property. Null if no TZ= prefix.</summary>
    public TimeZoneInfo? TimeZoneInfo { get; }

    /// <summary>Parsed cron schedule. Non-null when Kind is Cron or Alias.</summary>
    public CronSchedule? CronSchedule { get; }

    /// <summary>Parsed interval schedule. Non-null when Kind is Interval.</summary>
    public IntervalSchedule? IntervalSchedule { get; }

    /// <summary>Parsed once schedule. Non-null when Kind is Once.</summary>
    public OnceSchedule? OnceSchedule { get; }

    /// <summary>Raw options string (content inside {}). Null if no options block.</summary>
    public string? OptionsRaw { get; }

    /// <summary>Parsed options. Non-null even when no options block (empty options).</summary>
    public ScheduleOptions Options { get; }

    private CronexExpression(string original, ScheduleKind kind, string? timezone,
        TimeZoneInfo? tzInfo, CronSchedule? cronSchedule,
        IntervalSchedule? intervalSchedule, OnceSchedule? onceSchedule,
        string? optionsRaw, ScheduleOptions options)
    {
        Original = original;
        Kind = kind;
        Timezone = timezone;
        TimeZoneInfo = tzInfo;
        CronSchedule = cronSchedule;
        IntervalSchedule = intervalSchedule;
        OnceSchedule = onceSchedule;
        OptionsRaw = optionsRaw;
        Options = options;
    }

    /// <summary>
    /// Parses a Cronex expression string.
    /// </summary>
    /// <param name="expression">The expression string to parse.</param>
    /// <param name="referenceTime">Reference time for relative @once expressions. Defaults to DateTimeOffset.UtcNow.</param>
    /// <exception cref="FormatException">Thrown when the expression is invalid.</exception>
    public static CronexExpression Parse(string expression, DateTimeOffset? referenceTime = null)
    {
        if (!TryParse(expression, out var result, out var error, referenceTime))
            throw new FormatException(error);
        return result;
    }

    /// <summary>
    /// Attempts to parse a Cronex expression string.
    /// </summary>
    public static bool TryParse(string expression, out CronexExpression result, out string? error,
        DateTimeOffset? referenceTime = null)
    {
        result = null!;
        error = null;

        TokenizedExpression token;
        try
        {
            token = ExpressionTokenizer.Tokenize(expression);
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }

        // Resolve timezone
        TimeZoneInfo? tzInfo = null;
        if (token.Timezone != null)
        {
            try
            {
                tzInfo = TimeZoneInfo.FindSystemTimeZoneById(token.Timezone);
            }
            catch (TimeZoneNotFoundException)
            {
                error = $"Unknown timezone '{token.Timezone}'";
                return false;
            }
        }

        CronSchedule? cronSchedule = null;
        IntervalSchedule? intervalSchedule = null;
        OnceSchedule? onceSchedule = null;

        switch (token.Kind)
        {
            case ScheduleKind.Cron:
                {
                    var fields = token.Body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (!Cronex.CronSchedule.TryParse(fields, out var cs, out error))
                        return false;
                    cronSchedule = cs;
                    break;
                }

            case ScheduleKind.Alias:
                {
                    if (!CronAlias.TryResolve(token.Body, out var aliasFields))
                    {
                        error = $"Unknown alias '{token.Body}'";
                        return false;
                    }
                    if (!Cronex.CronSchedule.TryParse(aliasFields!, out var cs, out error))
                        return false;
                    cronSchedule = cs;
                    break;
                }

            case ScheduleKind.Interval:
                {
                    // Body is "@every 30m" or "@every 1h-2h"
                    var intervalBody = token.Body;
                    if (intervalBody.StartsWith("@every ", StringComparison.Ordinal))
                        intervalBody = intervalBody[7..];
                    else
                    {
                        error = "every: missing duration after @every";
                        return false;
                    }

                    if (!Cronex.IntervalSchedule.TryParse(intervalBody, out var ivl, out error))
                        return false;
                    intervalSchedule = ivl;
                    break;
                }

            case ScheduleKind.Once:
                {
                    // Body is "@once 2025-..." or "@once +20m"
                    var onceBody = token.Body;
                    if (onceBody.StartsWith("@once ", StringComparison.Ordinal))
                        onceBody = onceBody[6..];
                    else
                    {
                        error = "once: missing value after @once";
                        return false;
                    }

                    if (!Cronex.OnceSchedule.TryParse(onceBody, out var os, out error, referenceTime))
                        return false;
                    onceSchedule = os;
                    break;
                }
        }

        // Parse options block
        if (!ScheduleOptions.TryParse(token.OptionsRaw, out var options, out error))
            return false;

        result = new CronexExpression(expression, token.Kind, token.Timezone,
            tzInfo, cronSchedule, intervalSchedule, onceSchedule, token.OptionsRaw, options);
        return true;
    }

    /// <summary>
    /// Checks whether a given date/time matches this expression's schedule.
    /// Supports Cron, Alias, and Once kinds.
    /// </summary>
    public bool Matches(DateTime dt)
    {
        if (CronSchedule != null)
            return CronSchedule.Matches(dt);

        return false;
    }

    /// <summary>
    /// Returns the canonical string representation of this expression.
    /// Format: [TZ=timezone] body [{options}]
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>(3);

        if (Timezone != null)
            parts.Add($"TZ={Timezone}");

        parts.Add(GetBodyString());

        var optionsStr = Options.ToString();
        if (!string.IsNullOrEmpty(optionsStr))
            parts.Add($"{{{optionsStr}}}");

        return string.Join(" ", parts);
    }

    private string GetBodyString()
    {
        return Kind switch
        {
            ScheduleKind.Interval when IntervalSchedule.HasValue =>
                IntervalSchedule.Value.IsRange
                    ? $"@every {IntervalSchedule.Value.Interval}-{IntervalSchedule.Value.MaxInterval!.Value}"
                    : $"@every {IntervalSchedule.Value.Interval}",
            ScheduleKind.Once when OnceSchedule.HasValue =>
                $"@once {OnceSchedule.Value.FireAt:O}",
            _ => Original.Contains("TZ=") || Original.Contains('{')
                ? ExpressionTokenizer.Tokenize(Original).Body
                : Original
        };
    }

    /// <summary>
    /// Finds the next occurrence after the given time.
    /// Handles timezone conversion and DST transitions (Vixie Cron semantics).
    /// For @every, returns from + interval (caller must track last fire time).
    /// For @once, returns the fire time if it is after from, otherwise null.
    /// </summary>
    /// <param name="from">The reference time (exclusive).</param>
    /// <returns>The next occurrence as DateTimeOffset, or null if none.</returns>
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset from)
    {
        if (Options.Until.HasValue && from >= Options.Until.Value)
            return null;

        // M-6: Kind-specific From adjustment
        switch (Kind)
        {
            case ScheduleKind.Cron:
            case ScheduleKind.Alias:
                {
                    var adjustedFrom = from;
                    if (Options.From.HasValue && adjustedFrom < Options.From.Value)
                        adjustedFrom = Options.From.Value.AddSeconds(-1); // Cron Next() adds 1 second
                    return GetNextCronOccurrence(adjustedFrom);
                }

            case ScheduleKind.Interval:
                {
                    var adjustedFrom = from;
                    if (Options.From.HasValue && adjustedFrom < Options.From.Value)
                        adjustedFrom = Options.From.Value; // Exact: interval adds from this point
                    return GetNextIntervalOccurrence(adjustedFrom);
                }

            case ScheduleKind.Once:
                {
                    var result = GetNextOnceOccurrence(from);
                    // Apply From constraint after calculation
                    if (result.HasValue && Options.From.HasValue && result.Value < Options.From.Value)
                        return null;
                    return result;
                }

            default:
                return null;
        }
    }

    private DateTimeOffset? GetNextCronOccurrence(DateTimeOffset from)
    {
        if (CronSchedule == null) return null;

        var tz = TimeZoneInfo ?? System.TimeZoneInfo.Utc;

        // Convert from to the schedule's timezone
        var localFrom = TimeZoneInfo.ConvertTime(from, tz);
        var localDt = localFrom.DateTime;

        // Find next in local time
        var nextLocal = CronSchedule.Next(localDt);
        if (nextLocal == null) return null;

        // Convert back — handle DST ambiguity
        var result = ResolveLocalTime(nextLocal.Value, tz);

        // Check if DST spring-forward made the time invalid
        // The CronSchedule.Next already returns valid times in local,
        // but the local time might be invalid (in a DST gap)
        if (tz.IsInvalidTime(nextLocal.Value))
        {
            // Spring-forward: advance to next valid time and re-search
            var adjusted = new DateTimeOffset(nextLocal.Value, tz.GetUtcOffset(nextLocal.Value.AddHours(1)));
            var adjustedLocal = TimeZoneInfo.ConvertTime(adjusted, tz);
            nextLocal = CronSchedule.Next(adjustedLocal.DateTime.AddSeconds(-1));
            if (nextLocal == null) return null;
            result = ResolveLocalTime(nextLocal.Value, tz);
        }

        // Apply until constraint
        if (Options.Until.HasValue && result > Options.Until.Value)
            return null;

        return result;
    }

    private DateTimeOffset? GetNextIntervalOccurrence(DateTimeOffset from)
    {
        if (!IntervalSchedule.HasValue) return null;

        // M-1: Support range intervals with random sampling
        var ivl = IntervalSchedule.Value;
        TimeSpan interval;
        if (ivl.IsRange && ivl.MaxInterval.HasValue)
        {
            var minMs = (long)ivl.Interval.Value.TotalMilliseconds;
            var maxMs = (long)ivl.MaxInterval.Value.Value.TotalMilliseconds;
            interval = TimeSpan.FromMilliseconds(
                minMs + Random.Shared.NextInt64(maxMs - minMs));
        }
        else
        {
            interval = ivl.Interval.Value;
        }

        var next = from + interval;

        if (Options.Until.HasValue && next > Options.Until.Value)
            return null;

        return next;
    }

    private DateTimeOffset? GetNextOnceOccurrence(DateTimeOffset from)
    {
        if (!OnceSchedule.HasValue) return null;
        var fireAt = OnceSchedule.Value.FireAt;

        if (fireAt > from)
            return fireAt;

        return null; // Already past
    }

    /// <summary>
    /// Enumerates occurrences starting from the given time.
    /// Respects from/until/max options.
    /// </summary>
    /// <param name="from">Start time (exclusive).</param>
    /// <param name="count">Maximum number of occurrences to return. 0 means use max option or 1000.</param>
    /// <returns>An enumerable of occurrence times.</returns>
    public IEnumerable<DateTimeOffset> Enumerate(DateTimeOffset from, int count = 0)
    {
        var limit = count > 0 ? count : (Options.Max ?? 1000);
        var yielded = 0;
        var current = from;

        while (yielded < limit)
        {
            var next = GetNextOccurrence(current);
            if (next == null) yield break;

            current = next.Value;
            yielded++;
            yield return current;
        }
    }

    private static DateTimeOffset ResolveLocalTime(DateTime localDt, TimeZoneInfo tz)
    {
        if (tz.IsAmbiguousTime(localDt))
        {
            // Fall-back: use the first occurrence (standard time offset is larger)
            var offsets = tz.GetAmbiguousTimeOffsets(localDt);
            var offset = offsets[0];
            for (var i = 1; i < offsets.Length; i++)
            {
                if (offsets[i] > offset)
                    offset = offsets[i];
            }
            return new DateTimeOffset(localDt, offset);
        }

        if (tz.IsInvalidTime(localDt))
        {
            // Spring-forward gap — convert via UTC to get correct local time
            // Use -2h to be safely before the gap boundary (typical gap is 1h)
            var baseOffset = tz.GetUtcOffset(localDt.AddHours(-2));
            var utcTime = DateTime.SpecifyKind(localDt - baseOffset, DateTimeKind.Utc);
            var correctLocal = System.TimeZoneInfo.ConvertTimeFromUtc(utcTime, tz);
            return new DateTimeOffset(correctLocal, tz.GetUtcOffset(correctLocal));
        }

        return new DateTimeOffset(localDt, tz.GetUtcOffset(localDt));
    }
}
