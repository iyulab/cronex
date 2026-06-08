namespace Cronex;

/// <summary>
/// Represents a single parsed cron field (e.g., minute, hour, day-of-month).
/// A field contains one or more <see cref="CronFieldEntry"/> elements.
/// </summary>
public sealed class CronField
{
    /// <summary>The entries that make up this field (comma-separated elements).</summary>
    public IReadOnlyList<CronFieldEntry> Entries { get; }

    /// <summary>Whether this field is a wildcard ("*").</summary>
    public bool IsWildcard => Entries.Count == 1 && Entries[0].Kind == CronFieldEntryKind.Wildcard;

    /// <summary>The type of cron field.</summary>
    public CronFieldType FieldType { get; }

    private CronField(CronFieldType fieldType, IReadOnlyList<CronFieldEntry> entries)
    {
        FieldType = fieldType;
        Entries = entries;
    }

    /// <summary>
    /// Parses a single cron field string.
    /// </summary>
    public static CronField Parse(string input, CronFieldType fieldType)
    {
        if (!TryParse(input, fieldType, out var result, out var error))
            throw new FormatException(error);
        return result;
    }

    /// <summary>
    /// Attempts to parse a single cron field string.
    /// </summary>
    public static bool TryParse(string input, CronFieldType fieldType, out CronField result, out string? error)
    {
        result = null!;
        error = null;

        if (string.IsNullOrEmpty(input))
        {
            error = $"{fieldType}: empty field";
            return false;
        }

        var (min, max) = GetRange(fieldType);
        var entries = new List<CronFieldEntry>();

        foreach (var part in input.Split(','))
        {
            if (!TryParseEntry(part, fieldType, min, max, out var entry, out error))
                return false;
            entries.Add(entry);
        }

        result = new CronField(fieldType, entries);
        return true;
    }

    private static bool TryParseEntry(string part, CronFieldType fieldType, int min, int max, out CronFieldEntry entry, out string? error)
    {
        entry = default;
        error = null;

        // Wildcard with optional step: */N
        if (part.StartsWith('*'))
        {
            if (part == "*")
            {
                entry = CronFieldEntry.Wildcard();
                return true;
            }
            if (part.Length > 2 && part[1] == '/')
            {
                if (!TryParseInt(part.AsSpan(2), out var step) || step <= 0)
                {
                    error = $"{fieldType}: step must be positive, got '{part[2..]}'";
                    return false;
                }
                entry = CronFieldEntry.WildcardStep(step);
                return true;
            }
            error = $"{fieldType}: invalid wildcard expression '{part}'";
            return false;
        }

        // Value, Range, or Range/Step
        var stepSep = part.IndexOf('/');
        int? stepVal = null;
        var rangePart = part;

        if (stepSep >= 0)
        {
            if (!TryParseInt(part.AsSpan(stepSep + 1), out var s) || s <= 0)
            {
                error = $"{fieldType}: step must be positive, got '{part[(stepSep + 1)..]}'";
                return false;
            }
            stepVal = s;
            rangePart = part[..stepSep];
        }

        var dashIndex = rangePart.IndexOf('-');
        if (dashIndex >= 0)
        {
            if (!TryResolveValue(rangePart[..dashIndex], fieldType, min, max, out var lo, out error))
                return false;
            if (!TryResolveValue(rangePart[(dashIndex + 1)..], fieldType, min, max, out var hi, out error))
                return false;
            entry = stepVal.HasValue
                ? CronFieldEntry.RangeStep(lo, hi, stepVal.Value)
                : CronFieldEntry.Range(lo, hi);
            return true;
        }

        // Single value
        if (!TryResolveValue(rangePart, fieldType, min, max, out var val, out error))
            return false;

        entry = stepVal.HasValue
            ? CronFieldEntry.RangeStep(val, max, stepVal.Value)
            : CronFieldEntry.Value(val);
        return true;
    }

    private static bool TryResolveValue(string s, CronFieldType fieldType, int min, int max, out int value, out string? error)
    {
        error = null;
        value = 0;

        // Try named values (MON, JAN, etc.)
        if (fieldType == CronFieldType.DayOfWeek && TryParseDowName(s, out value))
            return true;
        if (fieldType == CronFieldType.Month && TryParseMonthName(s, out value))
            return true;

        if (!TryParseInt(s.AsSpan(), out value))
        {
            error = $"{fieldType}: invalid value '{s}'";
            return false;
        }

        // day-of-week: normalize 7 → 0 (both mean Sunday)
        if (fieldType == CronFieldType.DayOfWeek && value == 7)
            value = 0;

        if (value < min || value > max)
        {
            error = $"{fieldType}: value {value} out of range [{min}, {max}]";
            return false;
        }

        return true;
    }

    private static bool TryParseInt(ReadOnlySpan<char> s, out int value)
        => int.TryParse(s, System.Globalization.NumberStyles.None, null, out value);

    internal static bool TryParseDowName(string s, out int value)
    {
        value = s.ToUpperInvariant() switch
        {
            "SUN" => 0,
            "MON" => 1,
            "TUE" => 2,
            "WED" => 3,
            "THU" => 4,
            "FRI" => 5,
            "SAT" => 6,
            _ => -1
        };
        return value >= 0;
    }

    internal static bool TryParseMonthName(string s, out int value)
    {
        value = s.ToUpperInvariant() switch
        {
            "JAN" => 1,
            "FEB" => 2,
            "MAR" => 3,
            "APR" => 4,
            "MAY" => 5,
            "JUN" => 6,
            "JUL" => 7,
            "AUG" => 8,
            "SEP" => 9,
            "OCT" => 10,
            "NOV" => 11,
            "DEC" => 12,
            _ => -1
        };
        return value >= 0;
    }

    /// <summary>
    /// Returns the valid value range for a field type.
    /// </summary>
    public static (int Min, int Max) GetRange(CronFieldType fieldType) => fieldType switch
    {
        CronFieldType.Second => (0, 59),
        CronFieldType.Minute => (0, 59),
        CronFieldType.Hour => (0, 23),
        CronFieldType.DayOfMonth => (1, 31),
        CronFieldType.Month => (1, 12),
        CronFieldType.DayOfWeek => (0, 6),
        _ => throw new ArgumentOutOfRangeException(nameof(fieldType))
    };

    /// <summary>
    /// Checks whether a given value matches any entry in this field.
    /// </summary>
    public bool Matches(int value)
    {
        var (min, max) = GetRange(FieldType);
        foreach (var entry in Entries)
        {
            if (entry.Matches(value, min, max))
                return true;
        }
        return false;
    }
}

/// <summary>Identifies which cron field this is.</summary>
public enum CronFieldType
{
    /// <summary>Second field (0-59).</summary>
    Second,
    /// <summary>Minute field (0-59).</summary>
    Minute,
    /// <summary>Hour field (0-23).</summary>
    Hour,
    /// <summary>Day-of-month field (1-31).</summary>
    DayOfMonth,
    /// <summary>Month field (1-12).</summary>
    Month,
    /// <summary>Day-of-week field (0-6, 0=Sunday).</summary>
    DayOfWeek
}

/// <summary>The kind of a cron field entry.</summary>
public enum CronFieldEntryKind
{
    /// <summary>Wildcard: *</summary>
    Wildcard,
    /// <summary>Wildcard with step: */N</summary>
    WildcardStep,
    /// <summary>Single value: N</summary>
    Value,
    /// <summary>Range: N-M</summary>
    Range,
    /// <summary>Range with step: N-M/S</summary>
    RangeStep
}

/// <summary>
/// A single element within a cron field (between commas).
/// </summary>
public readonly struct CronFieldEntry
{
    /// <summary>The kind of entry.</summary>
    public CronFieldEntryKind Kind { get; }

    /// <summary>Start value (or single value).</summary>
    public int Low { get; }

    /// <summary>End value (for ranges).</summary>
    public int High { get; }

    /// <summary>Step value (for step expressions).</summary>
    public int Step { get; }

    private CronFieldEntry(CronFieldEntryKind kind, int low = 0, int high = 0, int step = 1)
    {
        Kind = kind;
        Low = low;
        High = high;
        Step = step;
    }

    /// <summary>Creates a wildcard (*) entry matching all values.</summary>
    internal static CronFieldEntry Wildcard() => new(CronFieldEntryKind.Wildcard);
    /// <summary>Creates a wildcard-step (*/N) entry matching every Nth value.</summary>
    internal static CronFieldEntry WildcardStep(int step) => new(CronFieldEntryKind.WildcardStep, step: step);
    /// <summary>Creates a single-value entry matching exactly the given value.</summary>
    internal static CronFieldEntry Value(int value) => new(CronFieldEntryKind.Value, value);
    /// <summary>Creates a range (low-high) entry matching all values in the range.</summary>
    internal static CronFieldEntry Range(int low, int high) => new(CronFieldEntryKind.Range, low, high);
    /// <summary>Creates a range-step (low-high/step) entry matching every Nth value in the range.</summary>
    internal static CronFieldEntry RangeStep(int low, int high, int step) => new(CronFieldEntryKind.RangeStep, low, high, step);

    /// <summary>
    /// Checks whether the given value matches this entry.
    /// Supports reversed ranges (e.g., 23-01 wraps around).
    /// </summary>
    internal bool Matches(int value, int fieldMin, int fieldMax)
    {
        return Kind switch
        {
            CronFieldEntryKind.Wildcard => true,
            CronFieldEntryKind.WildcardStep => (value - fieldMin) % Step == 0,
            CronFieldEntryKind.Value => value == Low,
            CronFieldEntryKind.Range => IsInRange(value, Low, High, fieldMin, fieldMax),
            CronFieldEntryKind.RangeStep => IsInRangeStep(value, Low, High, Step, fieldMin, fieldMax),
            _ => false
        };
    }

    private static bool IsInRange(int value, int low, int high, int fieldMin, int fieldMax)
    {
        if (low <= high)
            return value >= low && value <= high;
        // Reversed range: wraps around (e.g., 23-01 → 23,0,1)
        return value >= low || value <= high;
    }

    private static bool IsInRangeStep(int value, int low, int high, int step, int fieldMin, int fieldMax)
    {
        if (low <= high)
        {
            if (value < low || value > high)
                return false;
            return (value - low) % step == 0;
        }
        // Reversed range with step
        var range = (fieldMax - low + 1) + (high - fieldMin + 1);
        var normalizedValue = value >= low
            ? value - low
            : (fieldMax - low + 1) + (value - fieldMin);
        return normalizedValue >= 0 && normalizedValue < range && normalizedValue % step == 0;
    }
}
