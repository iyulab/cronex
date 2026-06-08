using BenchmarkDotNet.Attributes;

namespace Cronex.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class ParseBenchmarks
{
    [Benchmark(Description = "Parse: simple cron")]
    public CronexExpression SimpleCron() => CronexExpression.Parse("*/5 * * * *");

    [Benchmark(Description = "Parse: complex cron")]
    public CronexExpression ComplexCron() => CronexExpression.Parse("0 9 * * MON-FRI");

    [Benchmark(Description = "Parse: 6-field cron")]
    public CronexExpression SixFieldCron() => CronexExpression.Parse("30 */5 * * * *");

    [Benchmark(Description = "Parse: with timezone")]
    public CronexExpression WithTimezone() => CronexExpression.Parse("TZ=UTC 0 9 * * MON-FRI");

    [Benchmark(Description = "Parse: with options")]
    public CronexExpression WithOptions() => CronexExpression.Parse("0 9 * * * {jitter:30s, max:100}");

    [Benchmark(Description = "Parse: full expression")]
    public CronexExpression FullExpression() => CronexExpression.Parse("TZ=UTC 0 9 * * MON-FRI {jitter:30s, max:100, tag:report}");

    [Benchmark(Description = "Parse: alias @daily")]
    public CronexExpression Alias() => CronexExpression.Parse("@daily");

    [Benchmark(Description = "Parse: @every interval")]
    public CronexExpression Interval() => CronexExpression.Parse("@every 30m");

    [Benchmark(Description = "Parse: @every range")]
    public CronexExpression IntervalRange() => CronexExpression.Parse("@every 1h-2h");

    [Benchmark(Description = "Parse: @once absolute")]
    public CronexExpression OnceAbsolute() => CronexExpression.Parse("@once 2026-06-01T09:00:00Z");

    [Benchmark(Description = "Parse: special L")]
    public CronexExpression SpecialL() => CronexExpression.Parse("0 0 L * *");

    [Benchmark(Description = "Parse: special W")]
    public CronexExpression SpecialW() => CronexExpression.Parse("0 0 15W * *");

    [Benchmark(Description = "Parse: special #")]
    public CronexExpression SpecialHash() => CronexExpression.Parse("0 0 * * MON#2");
}
