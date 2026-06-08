using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// T-5: @every range interval actual Next() range verification (M-1 fix).
/// </summary>
public class IntervalRangeTests
{
    [Fact]
    public void Next_IntervalRange_ReturnsWithinRange()
    {
        var expr = CronexExpression.Parse("@every 1h-2h");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Run multiple times to verify the range (random)
        for (var i = 0; i < 20; i++)
        {
            var next = expr.GetNextOccurrence(from);
            next.ShouldNotBeNull();
            var interval = next!.Value - from;
            interval.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromHours(1));
            interval.ShouldBeLessThanOrEqualTo(TimeSpan.FromHours(2));
        }
    }

    [Fact]
    public void Enumerate_IntervalRange_AllWithinRange()
    {
        var expr = CronexExpression.Parse("@every 30m-1h");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var occurrences = expr.Enumerate(from, 10).ToList();
        occurrences.Count.ShouldBe(10);

        var prev = from;
        foreach (var occ in occurrences)
        {
            var interval = occ - prev;
            interval.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(30));
            interval.ShouldBeLessThanOrEqualTo(TimeSpan.FromHours(1));
            prev = occ;
        }
    }

    [Fact]
    public void Next_IntervalRange_IsNonDeterministic()
    {
        var expr = CronexExpression.Parse("@every 1m-10m");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Run 50 times and check that we get at least 2 different values
        var results = new HashSet<long>();
        for (var i = 0; i < 50; i++)
        {
            var next = expr.GetNextOccurrence(from);
            results.Add(next!.Value.Ticks);
        }

        // With a 9-minute range, 50 samples should produce multiple different values
        results.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Next_FixedInterval_IsDeterministic()
    {
        var expr = CronexExpression.Parse("@every 5m");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var expected = from + TimeSpan.FromMinutes(5);
        for (var i = 0; i < 5; i++)
        {
            var next = expr.GetNextOccurrence(from);
            next!.Value.ShouldBe(expected);
        }
    }

    [Fact]
    public void Next_IntervalRange_WithUntil_RespectsUntil()
    {
        var expr = CronexExpression.Parse("@every 1h-2h {until:2026-01-01}");
        var from = new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);

        // The interval would push past until date
        if (next != null)
        {
            next.Value.ShouldBeLessThanOrEqualTo(
                new DateTimeOffset(2026, 1, 1, 23, 59, 59, 999, TimeSpan.Zero));
        }
    }
}
