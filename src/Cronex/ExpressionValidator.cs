namespace Cronex;

/// <summary>
/// Validates Cronex expressions and returns structured error/warning lists.
/// </summary>
public static class ExpressionValidator
{
    /// <summary>
    /// Validates a Cronex expression string, returning structured errors and warnings.
    /// Unlike TryParse, this collects all issues rather than failing on the first one.
    /// </summary>
    public static ValidationResult Validate(string expression)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        if (string.IsNullOrWhiteSpace(expression))
        {
            errors.Add(new("E010", null, "expression is empty"));
            return new ValidationResult(errors, warnings);
        }

        // Tokenize
        TokenizedExpression token;
        try
        {
            token = ExpressionTokenizer.Tokenize(expression);
        }
        catch (FormatException ex)
        {
            errors.Add(new("E010", null, ex.Message));
            return new ValidationResult(errors, warnings);
        }

        // Timezone validation (E011)
        if (token.Timezone != null)
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(token.Timezone);
            }
            catch (TimeZoneNotFoundException)
            {
                errors.Add(new("E011", "timezone", $"unknown timezone '{token.Timezone}'", token.Timezone));
            }
        }

        // Schedule body validation
        switch (token.Kind)
        {
            case ScheduleKind.Cron:
                ValidateCron(token.Body, errors);
                break;
            case ScheduleKind.Alias:
                ValidateAlias(token.Body, errors);
                break;
            case ScheduleKind.Interval:
                ValidateInterval(token.Body, errors);
                break;
            case ScheduleKind.Once:
                ValidateOnce(token.Body, errors);
                break;
        }

        // Compute schedule interval for E022/E025 warnings
        TimeSpan? scheduleInterval = null;
        if (token.Kind == ScheduleKind.Interval)
        {
            var intervalBody = token.Body;
            if (intervalBody.StartsWith("@every ", StringComparison.Ordinal))
                intervalBody = intervalBody[7..];
            if (IntervalSchedule.TryParse(intervalBody, out var sched, out _))
                scheduleInterval = sched.Interval.Value;
        }

        // Options validation
        if (token.OptionsRaw != null)
            ValidateOptions(token.OptionsRaw, errors, warnings, scheduleInterval);

        return new ValidationResult(errors, warnings);
    }

    private static void ValidateCron(string body, List<ValidationError> errors)
    {
        var fields = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5 && fields.Length != 6)
        {
            errors.Add(new("E010", null, $"expected 5 or 6 fields, got {fields.Length}", fields.Length.ToString()));
            return;
        }

        var hasSeconds = fields.Length == 6;
        var offset = hasSeconds ? 1 : 0;

        if (hasSeconds)
            ValidateField(fields[0], CronFieldType.Second, "E001", errors);

        ValidateField(fields[offset], CronFieldType.Minute, "E002", errors);
        ValidateField(fields[offset + 1], CronFieldType.Hour, "E003", errors);

        // DOM — might be special (L, W)
        var domRaw = fields[offset + 2];
        if (!IsSpecialDom(domRaw))
            ValidateField(domRaw, CronFieldType.DayOfMonth, "E004", errors);

        ValidateField(fields[offset + 3], CronFieldType.Month, "E005", errors);

        // DOW — might be special (#, L)
        var dowRaw = fields[offset + 4];
        if (!IsSpecialDow(dowRaw))
            ValidateField(dowRaw, CronFieldType.DayOfWeek, "E006", errors);
    }

    private static void ValidateField(string raw, CronFieldType fieldType, string errorCode, List<ValidationError> errors)
    {
        if (!CronField.TryParse(raw, fieldType, out _, out var error))
        {
            var fieldName = fieldType.ToString().ToLowerInvariant();
            // Map to the correct error code based on the type of error
            var code = error != null && error.Contains("step") ? "E007" : errorCode;
            errors.Add(new(code, fieldName, error ?? $"invalid {fieldName} field", raw));
        }
    }

    private static bool IsSpecialDom(string raw) =>
        raw is "L" or "LW" || raw.StartsWith("L-", StringComparison.Ordinal)
        || (raw.Length >= 2 && raw.EndsWith('W') && char.IsAsciiDigit(raw[0]));

    private static bool IsSpecialDow(string raw) =>
        raw.Contains('#') || (raw.Length >= 2 && raw.EndsWith('L') && !raw.All(char.IsAsciiDigit));

    private static void ValidateAlias(string body, List<ValidationError> errors)
    {
        if (!CronAlias.TryResolve(body, out _))
            errors.Add(new("E010", null, $"unknown alias '{body}'", body));
    }

    private static void ValidateInterval(string body, List<ValidationError> errors)
    {
        var intervalBody = body;
        if (intervalBody.StartsWith("@every ", StringComparison.Ordinal))
            intervalBody = intervalBody[7..];
        else
        {
            errors.Add(new("E013", "interval", "missing duration after @every"));
            return;
        }

        if (!IntervalSchedule.TryParse(intervalBody, out _, out var error))
        {
            var code = error != null && error.Contains("min duration must be less than max") ? "E014" : "E013";
            errors.Add(new(code, "interval", error ?? "invalid interval", intervalBody));
        }
    }

    private static void ValidateOnce(string body, List<ValidationError> errors)
    {
        var onceBody = body;
        if (onceBody.StartsWith("@once ", StringComparison.Ordinal))
            onceBody = onceBody[6..];
        else
        {
            errors.Add(new("E012", "once", "missing value after @once"));
            return;
        }

        if (onceBody.StartsWith('+'))
        {
            if (!CronexDuration.TryParse(onceBody.AsSpan(1), out var d) || d.Value <= TimeSpan.Zero)
                errors.Add(new("E017", "once", $"relative duration must be positive", onceBody));
        }
        else if (!DateTimeOffset.TryParse(onceBody, null, System.Globalization.DateTimeStyles.None, out _))
        {
            errors.Add(new("E012", "once", $"invalid datetime format '{onceBody}'", onceBody));
        }
    }

    private static void ValidateOptions(string optionsRaw, List<ValidationError> errors, List<ValidationWarning> warnings, TimeSpan? scheduleInterval = null)
    {
        if (!ScheduleOptions.TryParse(optionsRaw, out var options, out var error))
        {
            // Determine error code
            var code = "E015";
            if (error != null)
            {
                if (error.Contains("unknown option")) code = "E015";
                else if (error.Contains("invalid max") || error.Contains("invalid jitter")
                    || error.Contains("invalid stagger") || error.Contains("invalid window")
                    || error.Contains("invalid from") || error.Contains("invalid until"))
                    code = "E016";
            }
            errors.Add(new(code, "options", error ?? "invalid options", optionsRaw));
            return;
        }

        // Logic validations
        if (options.From.HasValue && options.Until.HasValue && options.From >= options.Until)
            errors.Add(new("E020", "options", "'from' must be before 'until'"));

        if (options.Window.HasValue && options.Window.Value.Value <= TimeSpan.Zero)
            errors.Add(new("E023", "options.window", "must be positive"));

        if (options.Stagger.HasValue && options.Stagger.Value.Value <= TimeSpan.Zero)
            errors.Add(new("E024", "options.stagger", "must be positive"));

        // E022/E025: jitter/stagger vs schedule interval warnings
        if (scheduleInterval.HasValue && scheduleInterval.Value > TimeSpan.Zero)
        {
            if (options.Jitter.HasValue && options.Jitter.Value.Value > scheduleInterval.Value * 0.5)
                warnings.Add(new("E022", "options.jitter",
                    $"jitter {options.Jitter.Value} exceeds 50% of schedule interval"));

            if (options.Stagger.HasValue && options.Stagger.Value.Value > scheduleInterval.Value)
                warnings.Add(new("E025", "options.stagger",
                    $"stagger {options.Stagger.Value} exceeds schedule interval"));
        }

        // m-6: Duplicate tags warning
        if (options.Tags is { Count: > 0 })
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in options.Tags)
            {
                if (!seen.Add(tag))
                    warnings.Add(new("W001", "options.tag", $"duplicate tag '{tag}'"));
            }
        }
    }
}
