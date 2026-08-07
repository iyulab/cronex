namespace Cronex;

/// <summary>
/// Represents a parsed standard cron schedule (5-field or 6-field).
/// Contains the individual fields: second, minute, hour, day-of-month, month, day-of-week.
/// </summary>
public sealed class CronSchedule
{
    /// <summary>Second field. Implicit "0" for 5-field expressions.</summary>
    public CronField Second { get; }

    /// <summary>Minute field.</summary>
    public CronField Minute { get; }

    /// <summary>Hour field.</summary>
    public CronField Hour { get; }

    /// <summary>Day-of-month field.</summary>
    public CronField DayOfMonth { get; }

    /// <summary>Month field.</summary>
    public CronField Month { get; }

    /// <summary>Day-of-week field.</summary>
    public CronField DayOfWeek { get; }

    /// <summary>Whether the original expression used 6-field format (with explicit seconds).</summary>
    public bool HasSeconds { get; }

    /// <summary>Special entry for day-of-month (L, W, LW, L-N, NW). Null if standard field.</summary>
    public SpecialCronEntry? DayOfMonthSpecial { get; }

    /// <summary>Special entry for day-of-week (DOWL, DOW#N). Null if standard field.</summary>
    public SpecialCronEntry? DayOfWeekSpecial { get; }

    private CronSchedule(CronField second, CronField minute, CronField hour,
        CronField dayOfMonth, CronField month, CronField dayOfWeek, bool hasSeconds,
        SpecialCronEntry? domSpecial = null, SpecialCronEntry? dowSpecial = null)
    {
        Second = second;
        Minute = minute;
        Hour = hour;
        DayOfMonth = dayOfMonth;
        Month = month;
        DayOfWeek = dayOfWeek;
        HasSeconds = hasSeconds;
        DayOfMonthSpecial = domSpecial;
        DayOfWeekSpecial = dowSpecial;
    }

    /// <summary>
    /// Parses a standard cron expression (5 or 6 whitespace-separated fields).
    /// </summary>
    /// <param name="fields">The whitespace-split field strings.</param>
    /// <exception cref="FormatException">Thrown when the fields are invalid.</exception>
    public static CronSchedule Parse(string[] fields)
    {
        if (!TryParse(fields, out var result, out var error))
            throw new FormatException(error);
        return result;
    }

    /// <summary>
    /// Attempts to parse a standard cron expression from pre-split fields.
    /// </summary>
    public static bool TryParse(string[] fields, out CronSchedule result, out string? error)
    {
        result = null!;
        error = null;

        if (fields.Length != 5 && fields.Length != 6)
        {
            error = $"Expected 5 or 6 fields, got {fields.Length}";
            return false;
        }

        var hasSeconds = fields.Length == 6;
        var offset = hasSeconds ? 1 : 0;

        // Parse second field
        CronField second;
        if (hasSeconds)
        {
            if (!CronField.TryParse(fields[0], CronFieldType.Second, out second, out error))
                return false;
        }
        else
        {
            // Implicit second=0 for 5-field
            second = CronField.Parse("0", CronFieldType.Second);
        }

        if (!CronField.TryParse(fields[offset], CronFieldType.Minute, out var minute, out error))
            return false;
        if (!CronField.TryParse(fields[offset + 1], CronFieldType.Hour, out var hour, out error))
            return false;
        // Day-of-month: check for special characters first
        // Quartz "?" ("don't care") is a synonym for "*" in DOM/DOW — normalize before anything else
        // sees it, so the rest of parsing (including the L/W/etc. special-form checks) never needs
        // to know "?" exists.
        var domRaw = fields[offset + 2] == "?" ? "*" : fields[offset + 2];
        SpecialCronEntry? domSpecial = null;
        CronField dom;

        if (TryParseSpecialDom(domRaw, out var special, out error))
        {
            domSpecial = special;
            dom = CronField.Parse("*", CronFieldType.DayOfMonth); // placeholder wildcard
        }
        else if (error != null)
        {
            return false;
        }
        else if (!CronField.TryParse(domRaw, CronFieldType.DayOfMonth, out dom, out error))
        {
            return false;
        }

        if (!CronField.TryParse(fields[offset + 3], CronFieldType.Month, out var month, out error))
            return false;

        // Day-of-week: check for special characters first (same "?" normalization as day-of-month)
        var dowRaw = fields[offset + 4] == "?" ? "*" : fields[offset + 4];
        SpecialCronEntry? dowSpecial = null;
        CronField dow;

        if (TryParseSpecialDow(dowRaw, out var dowSpec, out error))
        {
            dowSpecial = dowSpec;
            dow = CronField.Parse("*", CronFieldType.DayOfWeek); // placeholder wildcard
        }
        else if (error != null)
        {
            return false;
        }
        else if (!CronField.TryParse(dowRaw, CronFieldType.DayOfWeek, out dow, out error))
        {
            return false;
        }

        result = new CronSchedule(second, minute, hour, dom, month, dow, hasSeconds, domSpecial, dowSpecial);
        return true;
    }

    /// <summary>
    /// Checks whether a given date/time matches this cron schedule.
    /// Handles L/W/# special characters via date-aware matching.
    /// Implements Vixie Cron OR semantics for DOM/DOW.
    /// </summary>
    public bool Matches(DateTime dt)
    {
        if (!Second.Matches(dt.Second) || !Minute.Matches(dt.Minute)
            || !Hour.Matches(dt.Hour) || !Month.Matches(dt.Month))
            return false;

        return MatchesDay(dt);
    }

    /// <summary>
    /// Finds the next occurrence of this cron schedule after the given time.
    /// Returns null if no occurrence is found within the search limit (4 years).
    /// </summary>
    /// <param name="from">The reference time (exclusive — search starts after this moment).</param>
    /// <returns>The next matching DateTime, or null if none found within limit.</returns>
    public DateTime? Next(DateTime from)
    {
        // Start from the next second
        var dt = from.AddSeconds(1);
        dt = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Kind);

        var maxYear = from.Year + 4;

        while (dt.Year <= maxYear)
        {
            // Month
            if (!Month.Matches(dt.Month))
            {
                var nextMonth = FindNextValue(Month, dt.Month, 1, 12);
                if (nextMonth <= dt.Month)
                {
                    // Wrap to next year
                    dt = new DateTime(dt.Year + 1, nextMonth, 1, 0, 0, 0, dt.Kind);
                }
                else
                {
                    dt = new DateTime(dt.Year, nextMonth, 1, 0, 0, 0, dt.Kind);
                }
                continue;
            }

            // Day matching — need to check both DOM and DOW
            if (!MatchesDay(dt))
            {
                dt = dt.AddDays(1);
                dt = new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, dt.Kind);
                continue;
            }

            // Hour
            if (!Hour.Matches(dt.Hour))
            {
                var nextHour = FindNextValue(Hour, dt.Hour, 0, 23);
                if (nextHour <= dt.Hour)
                {
                    // Wrap to next day
                    dt = dt.AddDays(1);
                    dt = new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, dt.Kind);
                    continue;
                }
                dt = new DateTime(dt.Year, dt.Month, dt.Day, nextHour, 0, 0, dt.Kind);
                continue;
            }

            // Minute
            if (!Minute.Matches(dt.Minute))
            {
                var nextMinute = FindNextValue(Minute, dt.Minute, 0, 59);
                if (nextMinute <= dt.Minute)
                {
                    // Wrap to next hour
                    dt = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, dt.Kind).AddHours(1);
                    continue;
                }
                dt = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, nextMinute, 0, dt.Kind);
                continue;
            }

            // Second
            if (!Second.Matches(dt.Second))
            {
                var nextSecond = FindNextValue(Second, dt.Second, 0, 59);
                if (nextSecond <= dt.Second)
                {
                    // Wrap to next minute
                    dt = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, dt.Kind).AddMinutes(1);
                    continue;
                }
                dt = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, nextSecond, dt.Kind);
                continue;
            }

            // All fields match
            return dt;
        }

        return null;
    }

    /// <summary>
    /// Checks whether the day (DOM and DOW) matches for the given datetime.
    /// Implements Vixie Cron OR semantics: when both DOM and DOW are non-wildcard,
    /// either match satisfies the condition (OR). When one is wildcard, only the
    /// other is checked.
    /// </summary>
    private bool MatchesDay(DateTime dt)
    {
        var date = DateOnly.FromDateTime(dt);

        // Determine wildcard status (special entries count as non-wildcard)
        var domIsWild = !DayOfMonthSpecial.HasValue && DayOfMonth.IsWildcard;
        var dowIsWild = !DayOfWeekSpecial.HasValue && DayOfWeek.IsWildcard;

        var domMatch = DayOfMonthSpecial.HasValue
            ? DayOfMonthSpecial.Value.Matches(date)
            : DayOfMonth.Matches(dt.Day);

        var dowMatch = DayOfWeekSpecial.HasValue
            ? DayOfWeekSpecial.Value.Matches(date)
            : DayOfWeek.Matches((int)dt.DayOfWeek);

        // Vixie Cron rule: both wildcard → any day; one wild → check the other;
        // both non-wildcard → OR semantics
        if (domIsWild && dowIsWild) return true;
        if (domIsWild) return dowMatch;
        if (dowIsWild) return domMatch;
        return domMatch || dowMatch;
    }

    /// <summary>
    /// Finds the next matching value for a field starting from (but not including) current value.
    /// Wraps around if needed. Returns the next value (may be less than or equal to current if wrapping).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if no matching value exists (should never happen for valid fields).</exception>
    private static int FindNextValue(CronField field, int current, int min, int max)
    {
        // Try values from current+1 to max
        for (var v = current + 1; v <= max; v++)
        {
            if (field.Matches(v))
                return v;
        }
        // Wrap around: try from min
        for (var v = min; v <= current; v++)
        {
            if (field.Matches(v))
                return v;
        }
        // m-2: Unreachable for valid fields, but fail loudly if it happens
        throw new InvalidOperationException(
            $"No matching value found for {field.FieldType} in [{min}, {max}] (current={current})");
    }

    private static bool TryParseSpecialDom(string raw, out SpecialCronEntry? entry, out string? error)
    {
        entry = null;
        error = null;

        if (raw == "L")
        {
            entry = SpecialCronEntry.LastDay();
            return true;
        }
        if (raw == "LW")
        {
            entry = SpecialCronEntry.LastWeekday();
            return true;
        }
        if (raw.StartsWith("L-", StringComparison.Ordinal) && raw.Length > 2)
        {
            if (int.TryParse(raw.AsSpan(2), out var offset) && offset > 0)
            {
                entry = SpecialCronEntry.LastDayOffset(offset);
                return true;
            }
            error = $"DayOfMonth: invalid L-offset '{raw}'";
            return false;
        }
        if (raw.Length >= 2 && raw.EndsWith('W') && char.IsAsciiDigit(raw[0]))
        {
            if (int.TryParse(raw.AsSpan(0, raw.Length - 1), out var day) && day >= 1 && day <= 31)
            {
                entry = SpecialCronEntry.NearestWeekday(day);
                return true;
            }
            error = $"DayOfMonth: invalid NW expression '{raw}'";
            return false;
        }

        // Not a special DOM expression
        return false;
    }

    private static bool TryParseSpecialDow(string raw, out SpecialCronEntry? entry, out string? error)
    {
        entry = null;
        error = null;

        // DOW#N (e.g., MON#2, 1#2)
        var hashIdx = raw.IndexOf('#');
        if (hashIdx > 0 && hashIdx < raw.Length - 1)
        {
            var dowPart = raw[..hashIdx];
            var nthPart = raw[(hashIdx + 1)..];

            if (!ResolveDow(dowPart, out var dow, out error))
                return false;
            if (!int.TryParse(nthPart, out var nth) || nth < 1 || nth > 5)
            {
                error = $"DayOfWeek: invalid nth value '{nthPart}' in '{raw}'";
                return false;
            }
            entry = SpecialCronEntry.NthDowOfMonth(dow, nth);
            return true;
        }

        // DOWL (e.g., 5L, FRIL)
        if (raw.Length >= 2 && raw.EndsWith('L'))
        {
            var dowPart = raw[..^1];
            if (ResolveDow(dowPart, out var dow, out error))
            {
                entry = SpecialCronEntry.LastDowOfMonth(dow);
                return true;
            }
            error = null; // might be a regular value, not an error
        }

        return false;
    }

    private static bool ResolveDow(string s, out int dow, out string? error)
    {
        error = null;
        if (CronField.TryParseDowName(s, out dow))
            return true;
        if (int.TryParse(s, out dow))
        {
            if (dow == 7) dow = 0;
            if (dow >= 0 && dow <= 6)
                return true;
            error = $"DayOfWeek: value {dow} out of range [0, 6]";
            return false;
        }
        error = $"DayOfWeek: invalid value '{s}'";
        return false;
    }
}
