namespace Cronex;

/// <summary>
/// Represents a special cron entry that requires date context for matching.
/// Used for L (last), W (weekday), # (nth) in day-of-month and day-of-week fields.
/// </summary>
public readonly struct SpecialCronEntry
{
    /// <summary>The type of special entry.</summary>
    public SpecialCronEntryKind Kind { get; }

    /// <summary>Value parameter (e.g., N in L-N, N in NW, day in DOW#N, DOW in DOWL).</summary>
    public int Value { get; }

    /// <summary>Second parameter (e.g., N in DOW#N).</summary>
    public int Param { get; }

    private SpecialCronEntry(SpecialCronEntryKind kind, int value = 0, int param = 0)
    {
        Kind = kind;
        Value = value;
        Param = param;
    }

    /// <summary>L — last day of month.</summary>
    public static SpecialCronEntry LastDay() => new(SpecialCronEntryKind.LastDay);

    /// <summary>LW — last weekday of month.</summary>
    public static SpecialCronEntry LastWeekday() => new(SpecialCronEntryKind.LastWeekday);

    /// <summary>L-N — N days before last day of month.</summary>
    public static SpecialCronEntry LastDayOffset(int offset) => new(SpecialCronEntryKind.LastDayOffset, offset);

    /// <summary>NW — nearest weekday to day N.</summary>
    public static SpecialCronEntry NearestWeekday(int day) => new(SpecialCronEntryKind.NearestWeekday, day);

    /// <summary>DOWL — last DOW of month (e.g., last Friday = 5L).</summary>
    public static SpecialCronEntry LastDowOfMonth(int dow) => new(SpecialCronEntryKind.LastDowOfMonth, dow);

    /// <summary>DOW#N — Nth DOW of month (e.g., 2nd Monday = MON#2).</summary>
    public static SpecialCronEntry NthDowOfMonth(int dow, int nth) => new(SpecialCronEntryKind.NthDowOfMonth, dow, nth);

    /// <summary>
    /// Checks whether the given date matches this special entry.
    /// </summary>
    public bool Matches(DateOnly date)
    {
        return Kind switch
        {
            SpecialCronEntryKind.LastDay => date.Day == DateTime.DaysInMonth(date.Year, date.Month),
            SpecialCronEntryKind.LastWeekday => MatchesLastWeekday(date),
            SpecialCronEntryKind.LastDayOffset => date.Day == DateTime.DaysInMonth(date.Year, date.Month) - Value,
            SpecialCronEntryKind.NearestWeekday => MatchesNearestWeekday(date),
            SpecialCronEntryKind.LastDowOfMonth => MatchesLastDow(date),
            SpecialCronEntryKind.NthDowOfMonth => MatchesNthDow(date),
            _ => false
        };
    }

    private bool MatchesLastWeekday(DateOnly date)
    {
        var lastDay = DateTime.DaysInMonth(date.Year, date.Month);
        var lastDate = new DateOnly(date.Year, date.Month, lastDay);
        var weekday = GetNearestWeekday(lastDate);
        return date == weekday;
    }

    private bool MatchesNearestWeekday(DateOnly date)
    {
        var target = new DateOnly(date.Year, date.Month, Math.Min(Value, DateTime.DaysInMonth(date.Year, date.Month)));
        var weekday = GetNearestWeekday(target);
        return date == weekday;
    }

    private static DateOnly GetNearestWeekday(DateOnly target)
    {
        var dow = target.DayOfWeek;
        if (dow != System.DayOfWeek.Saturday && dow != System.DayOfWeek.Sunday)
            return target;

        var lastDay = DateTime.DaysInMonth(target.Year, target.Month);

        if (dow == System.DayOfWeek.Saturday)
        {
            // Try Friday (day-1), but stay in same month
            if (target.Day > 1)
                return target.AddDays(-1);
            // First of month is Saturday → use Monday (day+2)
            return target.AddDays(2);
        }

        // Sunday
        // Try Monday (day+1), but stay in same month
        if (target.Day < lastDay)
            return target.AddDays(1);
        // Last of month is Sunday → use Friday (day-2)
        return target.AddDays(-2);
    }

    private bool MatchesLastDow(DateOnly date)
    {
        if ((int)date.DayOfWeek != Value)
            return false;
        // Last occurrence: no more of this DOW in the month
        return date.Day + 7 > DateTime.DaysInMonth(date.Year, date.Month);
    }

    private bool MatchesNthDow(DateOnly date)
    {
        if ((int)date.DayOfWeek != Value)
            return false;
        // Nth occurrence: (day-1)/7 + 1 == N
        var occurrence = (date.Day - 1) / 7 + 1;
        return occurrence == Param;
    }
}

/// <summary>Kinds of special cron entries.</summary>
public enum SpecialCronEntryKind
{
    /// <summary>L — last day of month.</summary>
    LastDay,
    /// <summary>LW — last weekday of month.</summary>
    LastWeekday,
    /// <summary>L-N — N days before last day.</summary>
    LastDayOffset,
    /// <summary>NW — nearest weekday to day N.</summary>
    NearestWeekday,
    /// <summary>DOWL — last specific DOW of month.</summary>
    LastDowOfMonth,
    /// <summary>DOW#N — Nth specific DOW of month.</summary>
    NthDowOfMonth
}
