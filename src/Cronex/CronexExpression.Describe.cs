using System.Text;

namespace Cronex;

public sealed partial class CronexExpression
{
    private static readonly string[] DowNames =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private static readonly string[] MonthNames =
        ["", "January", "February", "March", "April", "May", "June",
         "July", "August", "September", "October", "November", "December"];

    /// <summary>
    /// Returns a human-readable English description of this expression.
    /// </summary>
    public string Describe()
    {
        var sb = new StringBuilder();

        switch (Kind)
        {
            case ScheduleKind.Cron:
            case ScheduleKind.Alias:
                DescribeCron(sb);
                break;

            case ScheduleKind.Interval:
                DescribeInterval(sb);
                break;

            case ScheduleKind.Once:
                DescribeOnce(sb);
                break;
        }

        // Append timezone
        if (Timezone != null)
            sb.Append($" ({Timezone})");

        // Append options
        AppendOptions(sb);

        return sb.ToString();
    }

    private void DescribeCron(StringBuilder sb)
    {
        var cron = CronSchedule!;

        var minuteValues = GetFieldValues(cron.Minute);
        var hourValues = GetFieldValues(cron.Hour);
        var secondValues = cron.HasSeconds ? GetFieldValues(cron.Second) : null;

        var minuteIsWild = cron.Minute.IsWildcard;
        var hourIsWild = cron.Hour.IsWildcard;
        var minuteIsStep = IsStepPattern(cron.Minute);
        var hourIsStep = IsStepPattern(cron.Hour);

        // Determine time description
        if (minuteIsWild && hourIsWild && !cron.HasSeconds)
        {
            // * * ... → "Every minute"
            sb.Append("Every minute");
        }
        else if (minuteIsStep && hourIsWild && !cron.HasSeconds)
        {
            // */N * ... → "Every N minutes"
            var step = cron.Minute.Entries[0].Step;
            sb.Append($"Every {step} minutes");
        }
        else if (minuteIsWild && hourIsStep && !cron.HasSeconds)
        {
            // * */N ... → "Every N hours"
            var step = cron.Hour.Entries[0].Step;
            sb.Append($"Every {step} hours");
        }
        else if (hourIsWild && !minuteIsWild && !minuteIsStep && minuteValues != null && minuteValues.Count == 1)
        {
            // specific minute, wildcard hour → "Every hour at minute N"
            sb.Append($"Every hour at minute {minuteValues[0]}");
        }
        else if (!hourIsWild && !hourIsStep && hourValues != null && hourValues.Count >= 1)
        {
            // Specific hour(s) with specific minute(s) → "At HH:MM" or list
            var times = new List<string>();
            foreach (var h in hourValues)
            {
                if (minuteValues != null && minuteValues.Count >= 1)
                {
                    foreach (var m in minuteValues)
                    {
                        if (cron.HasSeconds && secondValues != null && secondValues.Count >= 1)
                        {
                            foreach (var s in secondValues)
                            {
                                times.Add($"{h:D2}:{m:D2}:{s:D2}");
                            }
                        }
                        else
                        {
                            times.Add($"{h:D2}:{m:D2}");
                        }
                    }
                }
            }

            if (times.Count > 0)
            {
                // Check for "midnight"
                if (times.Count == 1 && times[0] is "00:00" or "00:00:00")
                {
                    sb.Append("At midnight");
                }
                else
                {
                    sb.Append("At ");
                    sb.Append(JoinList(times));
                }
            }
        }
        else
        {
            // Fallback: just describe what we can
            sb.Append("On schedule");
        }

        // Day-of-month constraints
        AppendDomConstraint(sb, cron);

        // Month constraint
        AppendMonthConstraint(sb, cron);

        // Day-of-week constraints
        AppendDowConstraint(sb, cron);
    }

    private void AppendDomConstraint(StringBuilder sb, CronSchedule cron)
    {
        if (cron.DayOfMonthSpecial.HasValue)
        {
            var special = cron.DayOfMonthSpecial.Value;
            sb.Append(special.Kind switch
            {
                SpecialCronEntryKind.LastDay => " on the last day of the month",
                SpecialCronEntryKind.LastWeekday => " on the last weekday of the month",
                SpecialCronEntryKind.LastDayOffset => $" on {special.Value} days before the last day of the month",
                SpecialCronEntryKind.NearestWeekday => $" on the nearest weekday to day {special.Value}",
                _ => ""
            });
        }
        else if (!cron.DayOfMonth.IsWildcard)
        {
            var domValues = GetFieldValues(cron.DayOfMonth);
            if (domValues != null && domValues.Count > 0)
            {
                sb.Append(" on day ");
                sb.Append(JoinList(domValues.Select(v => v.ToString()).ToList()));
            }
        }
    }

    private void AppendMonthConstraint(StringBuilder sb, CronSchedule cron)
    {
        if (!cron.Month.IsWildcard)
        {
            var monthValues = GetFieldValues(cron.Month);
            if (monthValues != null && monthValues.Count > 0)
            {
                var monthNamesList = monthValues
                    .Where(v => v >= 1 && v <= 12)
                    .Select(v => MonthNames[v])
                    .ToList();
                if (monthNamesList.Count > 0)
                {
                    sb.Append(" of ");
                    sb.Append(JoinList(monthNamesList));
                }
            }
        }
    }

    private void AppendDowConstraint(StringBuilder sb, CronSchedule cron)
    {
        if (cron.DayOfWeekSpecial.HasValue)
        {
            var special = cron.DayOfWeekSpecial.Value;
            switch (special.Kind)
            {
                case SpecialCronEntryKind.LastDowOfMonth:
                    sb.Append($" on the last {DowNames[special.Value]}");
                    break;
                case SpecialCronEntryKind.NthDowOfMonth:
                    sb.Append($" on the {Ordinal(special.Param)} {DowNames[special.Value]}");
                    break;
            }
        }
        else if (!cron.DayOfWeek.IsWildcard)
        {
            // Check if it's a range entry
            var entries = cron.DayOfWeek.Entries;
            if (entries.Count == 1 && entries[0].Kind == CronFieldEntryKind.Range)
            {
                var entry = entries[0];
                sb.Append($", {DowNames[entry.Low]} through {DowNames[entry.High]}");
            }
            else
            {
                var dowValues = GetFieldValues(cron.DayOfWeek);
                if (dowValues != null && dowValues.Count > 0)
                {
                    var dowNamesList = dowValues
                        .Where(v => v >= 0 && v <= 6)
                        .Select(v => DowNames[v])
                        .ToList();
                    if (dowNamesList.Count > 0)
                    {
                        sb.Append(" on ");
                        sb.Append(JoinList(dowNamesList));
                    }
                }
            }
        }
    }

    private void DescribeInterval(StringBuilder sb)
    {
        if (!IntervalSchedule.HasValue) return;

        var ivl = IntervalSchedule.Value;
        if (ivl.IsRange)
        {
            sb.Append($"Every {ivl.Interval} to {ivl.MaxInterval!.Value} (randomized)");
        }
        else
        {
            sb.Append($"Every {ivl.Interval}");
        }
    }

    private void DescribeOnce(StringBuilder sb)
    {
        if (!OnceSchedule.HasValue) return;

        var once = OnceSchedule.Value;
        if (once.WasRelative && once.RelativeDuration.HasValue)
        {
            sb.Append($"Once, {once.RelativeDuration.Value} from reference time");
        }
        else
        {
            var fireAt = once.FireAt;
            sb.Append($"Once at {fireAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC");
        }
    }

    private void AppendOptions(StringBuilder sb)
    {
        var parts = new List<string>();

        if (Options.Jitter.HasValue)
            parts.Add($"with up to {Options.Jitter.Value} jitter");
        if (Options.Stagger.HasValue)
            parts.Add($"with {Options.Stagger.Value} stagger");
        if (Options.Window.HasValue)
            parts.Add($"within {Options.Window.Value} window");
        if (Options.From.HasValue)
            parts.Add($"from {Options.From.Value:yyyy-MM-dd}");
        if (Options.Until.HasValue)
            parts.Add($"until {Options.Until.Value:yyyy-MM-dd}");
        if (Options.Max.HasValue)
            parts.Add($"max {Options.Max.Value} executions");
        if (Options.Tags is { Count: > 0 })
            parts.Add($"tagged {string.Join(", ", Options.Tags)}");

        if (parts.Count > 0)
        {
            sb.Append(", ");
            sb.Append(string.Join(", ", parts));
        }
    }

    /// <summary>
    /// Enumerates all concrete values matched by the field entries.
    /// Returns null if the field has complex patterns that can't be easily enumerated.
    /// </summary>
    private static List<int>? GetFieldValues(CronField field)
    {
        var (min, max) = CronField.GetRange(field.FieldType);
        var values = new SortedSet<int>();

        foreach (var entry in field.Entries)
        {
            switch (entry.Kind)
            {
                case CronFieldEntryKind.Value:
                    values.Add(entry.Low);
                    break;
                case CronFieldEntryKind.Range:
                    if (entry.Low <= entry.High)
                    {
                        for (var i = entry.Low; i <= entry.High; i++)
                            values.Add(i);
                    }
                    else
                    {
                        // Wrapped range
                        for (var i = entry.Low; i <= max; i++)
                            values.Add(i);
                        for (var i = min; i <= entry.High; i++)
                            values.Add(i);
                    }
                    break;
                case CronFieldEntryKind.WildcardStep:
                    for (var i = min; i <= max; i += entry.Step)
                        values.Add(i);
                    break;
                case CronFieldEntryKind.RangeStep:
                    if (entry.Low <= entry.High)
                    {
                        for (var i = entry.Low; i <= entry.High; i += entry.Step)
                            values.Add(i);
                    }
                    else
                    {
                        var v = entry.Low;
                        while (v <= max)
                        {
                            values.Add(v);
                            v += entry.Step;
                        }
                        v = v - max - 1 + min;
                        while (v <= entry.High)
                        {
                            values.Add(v);
                            v += entry.Step;
                        }
                    }
                    break;
                case CronFieldEntryKind.Wildcard:
                    // Return null for wildcard — caller should check IsWildcard first
                    return null;
                default:
                    return null;
            }
        }

        return [.. values];
    }

    /// <summary>
    /// Checks if the field is a single WildcardStep entry (*/N pattern).
    /// </summary>
    private static bool IsStepPattern(CronField field)
    {
        return field.Entries.Count == 1 && field.Entries[0].Kind == CronFieldEntryKind.WildcardStep;
    }

    /// <summary>
    /// Joins a list of strings with commas and "and" for the last item.
    /// </summary>
    private static string JoinList(IList<string> items)
    {
        return items.Count switch
        {
            0 => "",
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1]
        };
    }

    /// <summary>
    /// Returns ordinal string for a number (1st, 2nd, 3rd, 4th, 5th).
    /// </summary>
    private static string Ordinal(int n)
    {
        var suffix = (n % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (n % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            }
        };
        return $"{n}{suffix}";
    }
}
