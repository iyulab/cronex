namespace Cronex;

/// <summary>
/// Resolves cron aliases (@daily, @hourly, etc.) to their 5-field cron equivalents.
/// </summary>
internal static class CronAlias
{
    /// <summary>
    /// Tries to resolve a cron alias to its cron fields.
    /// </summary>
    /// <param name="alias">The alias string (e.g., "@daily").</param>
    /// <param name="fields">The resolved 5-field cron string array, or null if not recognized.</param>
    /// <returns>True if the alias was recognized.</returns>
    internal static bool TryResolve(string alias, out string[]? fields)
    {
        fields = alias.ToUpperInvariant() switch
        {
            "@YEARLY" or "@ANNUALLY" => ["0", "0", "1", "1", "*"],
            "@MONTHLY" => ["0", "0", "1", "*", "*"],
            "@WEEKLY" => ["0", "0", "*", "*", "0"],
            "@DAILY" or "@MIDNIGHT" => ["0", "0", "*", "*", "*"],
            "@HOURLY" => ["0", "*", "*", "*", "*"],
            _ => null,
        };
        return fields != null;
    }
}
