using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class ToStringTests
{
    [Fact]
    public void ToString_SimpleCron()
    {
        var expr = CronexExpression.Parse("*/5 * * * *");
        expr.ToString().ShouldBe("*/5 * * * *");
    }

    [Fact]
    public void ToString_WithTimezone()
    {
        var expr = CronexExpression.Parse("TZ=UTC 0 9 * * MON-FRI");
        var str = expr.ToString();
        str.ShouldStartWith("TZ=UTC");
        str.ShouldContain("0 9 * * MON-FRI");
    }

    [Fact]
    public void ToString_WithOptions()
    {
        var expr = CronexExpression.Parse("0 9 * * * {jitter:30s, max:10}");
        var str = expr.ToString();
        str.ShouldContain("0 9 * * *");
        str.ShouldContain("jitter:30s");
        str.ShouldContain("max:10");
    }

    [Fact]
    public void ToString_Interval()
    {
        var expr = CronexExpression.Parse("@every 30m");
        expr.ToString().ShouldBe("@every 30m");
    }

    [Fact]
    public void ToString_IntervalRange()
    {
        var expr = CronexExpression.Parse("@every 1h-2h");
        expr.ToString().ShouldBe("@every 1h-2h");
    }

    [Fact]
    public void ToString_Alias()
    {
        var expr = CronexExpression.Parse("@daily");
        expr.ToString().ShouldBe("@daily");
    }

    [Fact]
    public void ToString_Once()
    {
        var expr = CronexExpression.Parse("@once 2025-03-01T09:00:00+09:00");
        var str = expr.ToString();
        str.ShouldStartWith("@once");
        // Should contain the ISO datetime
        str.ShouldContain("2025-03-01");
    }

    [Fact]
    public void ToString_FullExpression()
    {
        var expr = CronexExpression.Parse("TZ=UTC 0 9 * * MON-FRI {jitter:30s}");
        var str = expr.ToString();
        str.ShouldStartWith("TZ=UTC");
        str.ShouldContain("0 9 * * MON-FRI");
        str.ShouldContain("jitter:30s");
    }

    [Fact]
    public void ScheduleOptions_ToString_Empty()
    {
        var opts = new ScheduleOptions();
        opts.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void ScheduleOptions_ToString_AllOptions()
    {
        var opts = ScheduleOptions.Parse("jitter:5m, stagger:3m, window:10m, max:100, tag:a+b");
        var str = opts.ToString();
        str.ShouldContain("jitter:5m");
        str.ShouldContain("stagger:3m");
        str.ShouldContain("window:10m");
        str.ShouldContain("max:100");
        str.ShouldContain("tag:a+b");
    }
}
