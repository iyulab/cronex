namespace Cronex;

/// <summary>
/// Represents a duration value parsed from a Cronex expression (e.g., "1h30m", "500ms", "30s").
/// Immutable value type.
/// </summary>
public readonly struct CronexDuration : IEquatable<CronexDuration>
{
    /// <summary>The underlying duration as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Value { get; }

    /// <summary>The original string representation. Empty for default instances.</summary>
    public string Original => _original ?? string.Empty;
    private readonly string? _original;

    private CronexDuration(TimeSpan value, string original)
    {
        Value = value;
        _original = original;
    }

    /// <summary>Total milliseconds of the duration.</summary>
    public double TotalMilliseconds => Value.TotalMilliseconds;

    /// <summary>
    /// Parses a duration string. Supports compound units: "1h30m", "500ms", "30s", "2h", "1d".
    /// </summary>
    /// <exception cref="FormatException">Thrown when the string is not a valid duration.</exception>
    public static CronexDuration Parse(ReadOnlySpan<char> input)
    {
        if (!TryParse(input, out var result))
            throw new FormatException($"Invalid duration format: '{input}'");
        return result;
    }

    /// <summary>
    /// Attempts to parse a duration string. Returns false if the format is invalid.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> input, out CronexDuration result)
    {
        result = default;
        if (input.IsEmpty)
            return false;

        var original = input.ToString();
        var totalMs = 0L;
        var pos = 0;
        var parsed = false;

        while (pos < input.Length)
        {
            // Parse numeric part
            var numStart = pos;
            while (pos < input.Length && char.IsAsciiDigit(input[pos]))
                pos++;

            if (pos == numStart)
                return false; // no digits found

            if (!long.TryParse(input[numStart..pos], out var num))
                return false;

            // Parse unit part
            if (pos >= input.Length)
                return false; // trailing number with no unit

            long multiplier;
            if (pos + 1 < input.Length && input[pos] == 'm' && input[pos + 1] == 's')
            {
                multiplier = 1;
                pos += 2;
            }
            else
            {
                multiplier = input[pos] switch
                {
                    's' => 1_000,
                    'm' => 60_000,
                    'h' => 3_600_000,
                    'd' => 86_400_000,
                    _ => -1
                };
                if (multiplier < 0)
                    return false;
                pos++;
            }

            // m-3: Use checked arithmetic to detect overflow
            try
            {
                totalMs = checked(totalMs + num * multiplier);
            }
            catch (OverflowException)
            {
                return false;
            }
            parsed = true;
        }

        if (!parsed || totalMs < 0)
            return false;

        result = new CronexDuration(TimeSpan.FromMilliseconds(totalMs), original);
        return true;
    }

    /// <summary>
    /// Returns the canonical string representation (largest unit first).
    /// </summary>
    public override string ToString()
    {
        var ms = (long)Value.TotalMilliseconds;
        if (ms == 0)
            return "0ms";

        var sb = new System.Text.StringBuilder(16);
        var days = ms / 86_400_000;
        if (days > 0) { sb.Append(days); sb.Append('d'); ms %= 86_400_000; }
        var hours = ms / 3_600_000;
        if (hours > 0) { sb.Append(hours); sb.Append('h'); ms %= 3_600_000; }
        var minutes = ms / 60_000;
        if (minutes > 0) { sb.Append(minutes); sb.Append('m'); ms %= 60_000; }
        var seconds = ms / 1_000;
        if (seconds > 0) { sb.Append(seconds); sb.Append('s'); ms %= 1_000; }
        if (ms > 0) { sb.Append(ms); sb.Append("ms"); }

        return sb.ToString();
    }

    /// <inheritdoc />
    public bool Equals(CronexDuration other) => Value == other.Value;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CronexDuration d && Equals(d);
    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();
    /// <summary>Equality operator.</summary>
    public static bool operator ==(CronexDuration left, CronexDuration right) => left.Equals(right);
    /// <summary>Inequality operator.</summary>
    public static bool operator !=(CronexDuration left, CronexDuration right) => !left.Equals(right);

    /// <summary>Implicit conversion to <see cref="TimeSpan"/>.</summary>
    public static implicit operator TimeSpan(CronexDuration d) => d.Value;
}
