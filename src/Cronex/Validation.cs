namespace Cronex;

/// <summary>
/// Result of validating a Cronex expression.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>Whether the expression is valid (no errors).</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>Validation errors.</summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    /// <summary>Validation warnings.</summary>
    public IReadOnlyList<ValidationWarning> Warnings { get; }

    internal ValidationResult(List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        Errors = errors.AsReadOnly();
        Warnings = warnings.AsReadOnly();
    }

    /// <summary>Returns a valid result with no errors or warnings.</summary>
    public static ValidationResult Valid() => new([], []);
}

/// <summary>
/// A validation error with structured code and message.
/// </summary>
public sealed class ValidationError
{
    /// <summary>Error code (e.g., "E001").</summary>
    public string Code { get; }

    /// <summary>Field that caused the error (e.g., "hour").</summary>
    public string? Field { get; }

    /// <summary>Human-readable error message.</summary>
    public string Message { get; }

    /// <summary>The problematic value.</summary>
    public string? Value { get; }

    /// <summary>Character position within the expression where the error occurs (0-based). Null if unknown.</summary>
    public int? Position { get; }

    internal ValidationError(string code, string? field, string message, string? value = null, int? position = null)
    {
        Code = code;
        Field = field;
        Message = message;
        Value = value;
        Position = position;
    }

    /// <inheritdoc />
    public override string ToString() => $"[{Code}] {Message}";
}

/// <summary>
/// A validation warning.
/// </summary>
public sealed class ValidationWarning
{
    /// <summary>Warning code (e.g., "E022").</summary>
    public string Code { get; }

    /// <summary>Field that triggered the warning.</summary>
    public string? Field { get; }

    /// <summary>Human-readable warning message.</summary>
    public string Message { get; }

    internal ValidationWarning(string code, string? field, string message)
    {
        Code = code;
        Field = field;
        Message = message;
    }

    /// <inheritdoc />
    public override string ToString() => $"[{Code}] {Message}";
}
