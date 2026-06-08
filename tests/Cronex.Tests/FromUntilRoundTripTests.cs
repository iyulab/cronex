using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// T-7: from/until datetime ToString round-trip tests (M-3 fix verification).
/// Also covers M-6: From adjustment for @every/@once.
/// </summary>
public class FromUntilRoundTripTests
{
    [Fact]
    public void ToString_RoundTrip_FromDateTime_PreservesTime()
    {
        var opts = ScheduleOptions.Parse("from:2025-06-01T09:00:00Z");
        var str = opts.ToString();
        var rt = ScheduleOptions.Parse(str);
        rt.From!.Value.Hour.ShouldBe(9);
        rt.From!.Value.Minute.ShouldBe(0);
    }

    [Fact]
    public void ToString_RoundTrip_UntilDateTime_PreservesTime()
    {
        var opts = ScheduleOptions.Parse("until:2025-12-31T18:00:00+09:00");
        var str = opts.ToString();
        var rt = ScheduleOptions.Parse(str);
        rt.Until!.Value.Hour.ShouldBe(18);
    }

    [Fact]
    public void ToString_RoundTrip_FromDateOnly_StaysShort()
    {
        var opts = ScheduleOptions.Parse("from:2025-06-01");
        var str = opts.ToString();
        // Should be "from:2025-06-01" without ISO datetime suffix
        str.ShouldBe("from:2025-06-01");
    }

    [Fact]
    public void ToString_RoundTrip_UntilDateOnly_StaysShort()
    {
        var opts = ScheduleOptions.Parse("until:2025-12-31");
        var str = opts.ToString();
        // Should be "until:2025-12-31" without ISO datetime suffix
        str.ShouldBe("until:2025-12-31");
    }

    [Fact]
    public void ToString_RoundTrip_FullExpression_PreservesFromTime()
    {
        var expr = CronexExpression.Parse("0 9 * * * {from:2025-06-01T09:00:00Z}");
        var str = expr.ToString();
        var rt = CronexExpression.Parse(str);
        rt.Options.From!.Value.Hour.ShouldBe(9);
    }

    [Fact]
    public void Next_IntervalWithFrom_DoesNotFireEarly()
    {
        // M-6: @every with From should start exactly at From
        var expr = CronexExpression.Parse("@every 1h {from:2026-06-01T09:00:00Z}");
        var from = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero); // Before From
        var next = expr.GetNextOccurrence(from);

        // First fire should be at From + 1h = 10:00:00, not 09:59:59
        next.ShouldNotBeNull();
        next!.Value.ShouldBe(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Next_OnceBeforeFrom_ReturnsNull()
    {
        // M-6: @once before From should return null (explicit UTC to avoid locale issues)
        var expr = CronexExpression.Parse("@once 2025-05-31T23:59:59Z {from:2025-06-01T00:00:00Z}");
        var from = new DateTimeOffset(2025, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);

        next.ShouldBeNull();
    }

    [Fact]
    public void Next_CronWithFrom_AddsOneSecondOffset()
    {
        // M-6: Cron with From uses AddSeconds(-1) trick for correct Next() behavior
        var expr = CronexExpression.Parse("0 9 * * * {from:2026-01-01}");
        var from = new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        next!.Value.ShouldBeGreaterThanOrEqualTo(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Next_OnceAfterFrom_ReturnsFireAt()
    {
        // @once after From should work normally
        var expr = CronexExpression.Parse("@once 2026-06-15T10:00:00Z {from:2026-06-01}");
        var from = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        next!.Value.ShouldBe(new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero));
    }
}
