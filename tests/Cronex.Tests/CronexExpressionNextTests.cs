using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class CronexExpressionNextTests
{
    // --- Cron ---

    [Fact]
    public void Next_BasicCron()
    {
        var expr = CronexExpression.Parse("*/5 * * * *");
        var from = new DateTimeOffset(2026, 1, 1, 0, 3, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);
        next.ShouldNotBeNull();
        next!.Value.DateTime.ShouldBe(new DateTime(2026, 1, 1, 0, 5, 0));
    }

    [Fact]
    public void Next_WithTimezone()
    {
        var expr = CronexExpression.Parse("TZ=UTC 0 9 * * *");
        var from = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);
        next.ShouldNotBeNull();
        next!.Value.UtcDateTime.ShouldBe(new DateTime(2026, 1, 1, 9, 0, 0));
    }

    [Fact]
    public void Next_Alias()
    {
        var expr = CronexExpression.Parse("@daily");
        var from = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);
        next.ShouldNotBeNull();
        next!.Value.DateTime.ShouldBe(new DateTime(2026, 1, 2, 0, 0, 0));
    }

    // --- Interval ---

    [Fact]
    public void Next_Interval()
    {
        var expr = CronexExpression.Parse("@every 30m");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);
        next.ShouldNotBeNull();
        next!.Value.ShouldBe(from + TimeSpan.FromMinutes(30));
    }

    // --- Once ---

    [Fact]
    public void Next_OnceAbsolute_Future()
    {
        var expr = CronexExpression.Parse("@once 2026-06-01T09:00:00Z");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);
        next.ShouldNotBeNull();
        next!.Value.UtcDateTime.ShouldBe(new DateTime(2026, 6, 1, 9, 0, 0));
    }

    [Fact]
    public void Next_OnceAbsolute_Past()
    {
        var expr = CronexExpression.Parse("@once 2020-01-01T00:00:00Z");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);
        next.ShouldBeNull();
    }

    [Fact]
    public void Next_OnceRelative()
    {
        var refTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var expr = CronexExpression.Parse("@once +20m", refTime);
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);
        next.ShouldNotBeNull();
        next!.Value.ShouldBe(refTime + TimeSpan.FromMinutes(20));
    }

    // --- Options: from/until ---

    [Fact]
    public void Next_FromConstraint()
    {
        var expr = CronexExpression.Parse("0 9 * * * {from:2026-06-01}");
        // from is before the constraint — should jump to June
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);
        next.ShouldNotBeNull();
        next!.Value.DateTime.Month.ShouldBeGreaterThanOrEqualTo(6);
    }

    [Fact]
    public void Next_UntilConstraint()
    {
        var expr = CronexExpression.Parse("0 9 * * * {until:2026-01-02}");
        var from = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);
        next.ShouldBeNull();
    }

    [Fact]
    public void Next_IntervalWithUntil()
    {
        var expr = CronexExpression.Parse("@every 30m {until:2026-01-01}");
        var from = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);
        next.ShouldBeNull();
    }

    // --- Multiple occurrences ---

    [Fact]
    public void Next_ChainedCalls()
    {
        var expr = CronexExpression.Parse("0 0 * * *");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t1 = expr.GetNextOccurrence(from);
        t1!.Value.DateTime.ShouldBe(new DateTime(2026, 1, 2, 0, 0, 0));
        var t2 = expr.GetNextOccurrence(t1.Value);
        t2!.Value.DateTime.ShouldBe(new DateTime(2026, 1, 3, 0, 0, 0));
    }
}
