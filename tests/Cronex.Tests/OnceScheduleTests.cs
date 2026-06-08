using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class OnceScheduleTests
{
    [Fact]
    public void Parse_AbsoluteDatetime()
    {
        var os = OnceSchedule.Parse("2025-03-01T09:00:00+09:00");
        os.WasRelative.ShouldBeFalse();
        os.RelativeDuration.ShouldBeNull();
        os.FireAt.ShouldBe(new DateTimeOffset(2025, 3, 1, 9, 0, 0, TimeSpan.FromHours(9)));
    }

    [Fact]
    public void Parse_AbsoluteUtc()
    {
        var os = OnceSchedule.Parse("2025-12-31T23:59:59Z");
        os.FireAt.Offset.ShouldBe(TimeSpan.Zero);
        os.FireAt.Year.ShouldBe(2025);
    }

    [Fact]
    public void Parse_RelativeDuration()
    {
        var refTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var os = OnceSchedule.Parse("+20m", refTime);
        os.WasRelative.ShouldBeTrue();
        os.RelativeDuration.ShouldNotBeNull();
        os.RelativeDuration!.Value.Value.ShouldBe(TimeSpan.FromMinutes(20));
        os.FireAt.ShouldBe(refTime + TimeSpan.FromMinutes(20));
    }

    [Fact]
    public void Parse_RelativeCompound()
    {
        var refTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var os = OnceSchedule.Parse("+1h30m", refTime);
        os.FireAt.ShouldBe(refTime + TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void Parse_Empty_Fails()
    {
        OnceSchedule.TryParse("", out _, out var error).ShouldBeFalse();
        error.ShouldNotBeNull();
    }

    [Fact]
    public void Parse_InvalidDatetime_Fails()
    {
        OnceSchedule.TryParse("not-a-date", out _, out var error).ShouldBeFalse();
        error!.ShouldContain("invalid datetime");
    }

    [Fact]
    public void Parse_InvalidRelativeDuration_Fails()
    {
        OnceSchedule.TryParse("+xyz", out _, out var error).ShouldBeFalse();
        error!.ShouldContain("invalid relative duration");
    }

    // Integration: via CronexExpression

    [Fact]
    public void CronexExpression_ParsesInterval()
    {
        var expr = CronexExpression.Parse("@every 30m");
        expr.Kind.ShouldBe(ScheduleKind.Interval);
        expr.IntervalSchedule.ShouldNotBeNull();
        expr.IntervalSchedule!.Value.Interval.Value.ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void CronexExpression_ParsesIntervalRange()
    {
        var expr = CronexExpression.Parse("@every 1h-2h");
        expr.IntervalSchedule.ShouldNotBeNull();
        expr.IntervalSchedule!.Value.IsRange.ShouldBeTrue();
    }

    [Fact]
    public void CronexExpression_ParsesOnceAbsolute()
    {
        var expr = CronexExpression.Parse("@once 2025-03-01T09:00:00+09:00");
        expr.Kind.ShouldBe(ScheduleKind.Once);
        expr.OnceSchedule.ShouldNotBeNull();
        expr.OnceSchedule!.Value.WasRelative.ShouldBeFalse();
    }

    [Fact]
    public void CronexExpression_ParsesOnceRelative()
    {
        var refTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var expr = CronexExpression.Parse("@once +20m", refTime);
        expr.OnceSchedule.ShouldNotBeNull();
        expr.OnceSchedule!.Value.WasRelative.ShouldBeTrue();
        expr.OnceSchedule.Value.FireAt.ShouldBe(refTime + TimeSpan.FromMinutes(20));
    }

    [Fact]
    public void CronexExpression_IntervalWithOptions()
    {
        var expr = CronexExpression.Parse("@every 5m {max:10}");
        expr.Kind.ShouldBe(ScheduleKind.Interval);
        expr.IntervalSchedule.ShouldNotBeNull();
        expr.OptionsRaw.ShouldBe("max:10");
    }

    [Fact]
    public void CronexExpression_OnceWithTimezone()
    {
        var expr = CronexExpression.Parse("TZ=UTC @once 2025-03-01T09:00:00Z");
        expr.Timezone.ShouldBe("UTC");
        expr.OnceSchedule.ShouldNotBeNull();
    }
}
