using BenchmarkDotNet.Attributes;

namespace Cronex.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class EnumerateBenchmarks
{
    private CronexExpression _simpleCron = null!;
    private CronexExpression _complexCron = null!;
    private CronexExpression _interval = null!;
    private DateTimeOffset _from;

    [GlobalSetup]
    public void Setup()
    {
        _simpleCron = CronexExpression.Parse("*/5 * * * *");
        _complexCron = CronexExpression.Parse("0 9 * * MON-FRI");
        _interval = CronexExpression.Parse("@every 30m");
        _from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    [Benchmark(Description = "Enumerate 10: simple cron")]
    public int SimpleCron10() => _simpleCron.Enumerate(_from, 10).Count();

    [Benchmark(Description = "Enumerate 100: simple cron")]
    public int SimpleCron100() => _simpleCron.Enumerate(_from, 100).Count();

    [Benchmark(Description = "Enumerate 10: weekday cron")]
    public int ComplexCron10() => _complexCron.Enumerate(_from, 10).Count();

    [Benchmark(Description = "Enumerate 100: weekday cron")]
    public int ComplexCron100() => _complexCron.Enumerate(_from, 100).Count();

    [Benchmark(Description = "Enumerate 100: @every interval")]
    public int Interval100() => _interval.Enumerate(_from, 100).Count();
}
