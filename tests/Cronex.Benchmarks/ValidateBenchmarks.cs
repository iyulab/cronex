using BenchmarkDotNet.Attributes;

namespace Cronex.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class ValidateBenchmarks
{
    [Benchmark(Description = "Validate: simple cron")]
    public ValidationResult SimpleCron() => ExpressionValidator.Validate("*/5 * * * *");

    [Benchmark(Description = "Validate: full expression")]
    public ValidationResult FullExpression() => ExpressionValidator.Validate("TZ=UTC 0 9 * * MON-FRI {jitter:30s, max:100}");

    [Benchmark(Description = "Validate: invalid")]
    public ValidationResult Invalid() => ExpressionValidator.Validate("60 25 32 13 8");

    [Benchmark(Description = "Validate: alias")]
    public ValidationResult Alias() => ExpressionValidator.Validate("@daily");

    [Benchmark(Description = "Validate: interval")]
    public ValidationResult Interval() => ExpressionValidator.Validate("@every 30m");
}
