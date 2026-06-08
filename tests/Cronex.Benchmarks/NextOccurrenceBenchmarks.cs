using BenchmarkDotNet.Attributes;

namespace Cronex.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class NextOccurrenceBenchmarks
{
    private CronexExpression _simpleCron = null!;
    private CronexExpression _complexCron = null!;
    private CronexExpression _withTz = null!;
    private CronexExpression _specialL = null!;
    private CronexExpression _specialW = null!;
    private CronexExpression _specialHash = null!;
    private CronexExpression _interval = null!;
    private CronexExpression _onceFuture = null!;
    private CronexExpression _alias = null!;

    private DateTimeOffset _from;

    [GlobalSetup]
    public void Setup()
    {
        _simpleCron = CronexExpression.Parse("*/5 * * * *");
        _complexCron = CronexExpression.Parse("0 9 * * MON-FRI");
        _withTz = CronexExpression.Parse("TZ=UTC 0 9 * * MON-FRI");
        _specialL = CronexExpression.Parse("0 0 L * *");
        _specialW = CronexExpression.Parse("0 0 15W * *");
        _specialHash = CronexExpression.Parse("0 0 * * MON#2");
        _interval = CronexExpression.Parse("@every 30m");
        _onceFuture = CronexExpression.Parse("@once 2026-06-01T09:00:00Z");
        _alias = CronexExpression.Parse("@daily");

        _from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    [Benchmark(Description = "Next: simple cron")]
    public DateTimeOffset? SimpleCron() => _simpleCron.GetNextOccurrence(_from);

    [Benchmark(Description = "Next: complex cron")]
    public DateTimeOffset? ComplexCron() => _complexCron.GetNextOccurrence(_from);

    [Benchmark(Description = "Next: with timezone")]
    public DateTimeOffset? WithTimezone() => _withTz.GetNextOccurrence(_from);

    [Benchmark(Description = "Next: special L")]
    public DateTimeOffset? SpecialL() => _specialL.GetNextOccurrence(_from);

    [Benchmark(Description = "Next: special W")]
    public DateTimeOffset? SpecialW() => _specialW.GetNextOccurrence(_from);

    [Benchmark(Description = "Next: special #")]
    public DateTimeOffset? SpecialHash() => _specialHash.GetNextOccurrence(_from);

    [Benchmark(Description = "Next: @every interval")]
    public DateTimeOffset? Interval() => _interval.GetNextOccurrence(_from);

    [Benchmark(Description = "Next: @once future")]
    public DateTimeOffset? OnceFuture() => _onceFuture.GetNextOccurrence(_from);

    [Benchmark(Description = "Next: @daily alias")]
    public DateTimeOffset? Alias() => _alias.GetNextOccurrence(_from);
}
