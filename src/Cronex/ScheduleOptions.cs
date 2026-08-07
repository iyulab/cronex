using System.Globalization;

namespace Cronex;

/// <summary>
/// Parsed options from the {} block in a Cronex expression.
/// </summary>
public sealed class ScheduleOptions
{
    /// <summary>Jitter duration: random delay [0, jitter) per execution.</summary>
    public CronexDuration? Jitter { get; init; }

    /// <summary>Stagger duration: deterministic fixed offset [0, stagger) based on trigger ID.</summary>
    public CronexDuration? Stagger { get; init; }

    /// <summary>Window duration: execution allowed only within window after scheduled time.</summary>
    public CronexDuration? Window { get; init; }

    /// <summary>Start date/time: occurrences before this are ignored.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>End date/time: occurrences after this are ignored.</summary>
    public DateTimeOffset? Until { get; init; }

    /// <summary>Maximum execution count.</summary>
    public int? Max { get; init; }

    /// <summary>Metadata tags. Multiple tags separated by + in expression.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Misfire/catchup policy for occurrences missed while the scheduler wasn't ticking (or a
    /// handler was still running). Defaults to <see cref="CatchupPolicy.All"/> when unset.</summary>
    public CatchupPolicy? Catchup { get; init; }

    /// <summary>
    /// Parses an options raw string (the content inside {}).
    /// </summary>
    /// <exception cref="FormatException">Thrown when the options format is invalid.</exception>
    public static ScheduleOptions Parse(string optionsRaw)
    {
        if (!TryParse(optionsRaw, out var result, out var error))
            throw new FormatException(error);
        return result;
    }

    /// <summary>
    /// Attempts to parse an options raw string.
    /// </summary>
    public static bool TryParse(string? optionsRaw, out ScheduleOptions result, out string? error)
    {
        result = new ScheduleOptions();
        error = null;

        if (string.IsNullOrWhiteSpace(optionsRaw))
            return true;

        CronexDuration? jitter = null;
        CronexDuration? stagger = null;
        CronexDuration? window = null;
        DateTimeOffset? from = null;
        DateTimeOffset? until = null;
        int? max = null;
        List<string>? tags = null;
        CatchupPolicy? catchup = null;

        // Split by comma, then parse each key:value pair
        var pairs = optionsRaw.Split(',');
        foreach (var pair in pairs)
        {
            var trimmed = pair.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx <= 0)
            {
                error = $"options: invalid key-value pair '{trimmed}'";
                return false;
            }

            var key = trimmed[..colonIdx].Trim();
            var value = trimmed[(colonIdx + 1)..].Trim();

            switch (key)
            {
                case "jitter":
                    if (!CronexDuration.TryParse(value, out var j))
                    {
                        error = $"options: invalid jitter duration '{value}'";
                        return false;
                    }
                    jitter = j;
                    break;

                case "stagger":
                    if (!CronexDuration.TryParse(value, out var s))
                    {
                        error = $"options: invalid stagger duration '{value}'";
                        return false;
                    }
                    stagger = s;
                    break;

                case "window":
                    if (!CronexDuration.TryParse(value, out var w))
                    {
                        error = $"options: invalid window duration '{value}'";
                        return false;
                    }
                    window = w;
                    break;

                case "from":
                    if (!TryParseDateTime(value, out var f))
                    {
                        error = $"options: invalid from date '{value}'";
                        return false;
                    }
                    from = f;
                    break;

                case "until":
                    if (!TryParseDateTime(value, out var u))
                    {
                        error = $"options: invalid until date '{value}'";
                        return false;
                    }
                    // date-only until means end of day
                    if (value.Length <= 10 && !value.Contains('T'))
                    {
                        var uv = u!.Value;
                        u = new DateTimeOffset(uv.Date.AddDays(1).AddMilliseconds(-1), uv.Offset);
                    }
                    until = u;
                    break;

                case "max":
                    if (!int.TryParse(value, out var m) || m <= 0)
                    {
                        error = $"options: invalid max value '{value}'";
                        return false;
                    }
                    max = m;
                    break;

                case "tag":
                    tags = [.. value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
                    if (tags.Count == 0)
                    {
                        error = $"options: empty tag value";
                        return false;
                    }
                    break;

                case "catchup":
                    catchup = value switch
                    {
                        "all" => CatchupPolicy.All,
                        "skip" => CatchupPolicy.Skip,
                        "once" => CatchupPolicy.Once,
                        _ => null
                    };
                    if (catchup == null)
                    {
                        error = $"options: invalid catchup value '{value}'";
                        return false;
                    }
                    break;

                default:
                    error = $"options: unknown option '{key}'";
                    return false;
            }
        }

        result = new ScheduleOptions
        {
            Jitter = jitter,
            Stagger = stagger,
            Window = window,
            From = from,
            Until = until,
            Max = max,
            Tags = tags?.AsReadOnly(),
            Catchup = catchup
        };
        return true;
    }

    private static bool TryParseDateTime(string value, out DateTimeOffset? result)
    {
        result = null;

        // Try ISO 8601 datetime
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            result = dto;
            return true;
        }

        // Try date-only (YYYY-MM-DD)
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var dateOnly))
        {
            result = new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the options as a formatted string for use in an expression.
    /// </summary>
    public override string ToString()
    {
        // spec §6.1: options sorted by key alphabetical order
        var parts = new List<string>();

        if (Catchup.HasValue)
            parts.Add($"catchup:{Catchup.Value.ToString().ToLowerInvariant()}");
        if (From.HasValue)
        {
            var f = From.Value;
            // M-3: date-only (midnight at any offset) → short format; otherwise full ISO 8601
            parts.Add(f.TimeOfDay == TimeSpan.Zero
                ? $"from:{f:yyyy-MM-dd}"
                : $"from:{f:O}");
        }
        if (Jitter.HasValue)
            parts.Add($"jitter:{Jitter.Value}");
        if (Max.HasValue)
            parts.Add($"max:{Max.Value}");
        if (Stagger.HasValue)
            parts.Add($"stagger:{Stagger.Value}");
        if (Tags is { Count: > 0 })
            parts.Add($"tag:{string.Join('+', Tags)}");
        if (Until.HasValue)
        {
            var u = Until.Value;
            // M-3: date-only until (23:59:59.999) → short format; otherwise full ISO 8601
            parts.Add(u.Hour == 23 && u.Minute == 59 && u.Second == 59
                ? $"until:{u:yyyy-MM-dd}"
                : $"until:{u:O}");
        }
        if (Window.HasValue)
            parts.Add($"window:{Window.Value}");

        return string.Join(", ", parts);
    }
}
