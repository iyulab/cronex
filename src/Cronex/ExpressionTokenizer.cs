namespace Cronex;

/// <summary>
/// Splits a Cronex expression string into its structural parts:
/// timezone prefix, schedule body, and options block.
/// </summary>
internal static class ExpressionTokenizer
{
    /// <summary>
    /// Tokenizes a raw Cronex expression string.
    /// </summary>
    internal static TokenizedExpression Tokenize(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new FormatException("Expression cannot be empty");

        var span = expression.AsSpan().Trim();
        string? timezone = null;
        string? optionsRaw = null;

        // Extract TZ= prefix
        if (span.StartsWith("TZ=", StringComparison.Ordinal))
        {
            var tzEnd = span.IndexOf(' ');
            if (tzEnd < 0)
                throw new FormatException("TZ= prefix must be followed by a schedule body");
            timezone = span[3..tzEnd].ToString();
            span = span[(tzEnd + 1)..].TrimStart();
        }

        // Extract options block {...}
        // m-7: Also detect content after closing brace
        var braceOpen = -1;
        var braceClose = -1;
        for (var i = span.Length - 1; i >= 0; i--)
        {
            if (span[i] == '}')
            {
                braceClose = i;
                // Find matching open brace
                for (var j = i - 1; j >= 0; j--)
                {
                    if (span[j] == '{')
                    {
                        braceOpen = j;
                        break;
                    }
                }
                break;
            }
        }

        if (braceClose >= 0 && braceOpen >= 0)
        {
            // Check for trailing content after }
            var trailing = span[(braceClose + 1)..].Trim();
            if (trailing.Length > 0)
                throw new FormatException($"Unexpected content after options block: '{trailing}'");

            optionsRaw = span[(braceOpen + 1)..braceClose].ToString().Trim();
            span = span[..braceOpen].TrimEnd();
        }
        else if (braceClose >= 0 && braceOpen < 0)
        {
            throw new FormatException("Unmatched closing brace '}'");
        }
        else if (span.Contains('{'))
        {
            throw new FormatException("Unmatched opening brace '{'");
        }

        var body = span.ToString();

        // Determine schedule kind
        ScheduleKind kind;
        if (body.StartsWith("@every ", StringComparison.Ordinal) || body == "@every")
            kind = ScheduleKind.Interval;
        else if (body.StartsWith("@once ", StringComparison.Ordinal) || body == "@once")
            kind = ScheduleKind.Once;
        else if (body.StartsWith('@'))
            kind = ScheduleKind.Alias;
        else
            kind = ScheduleKind.Cron;

        return new TokenizedExpression(expression, timezone, body, optionsRaw, kind);
    }
}

/// <summary>
/// Result of tokenizing a Cronex expression string.
/// </summary>
internal readonly record struct TokenizedExpression(
    string Original,
    string? Timezone,
    string Body,
    string? OptionsRaw,
    ScheduleKind Kind
);
