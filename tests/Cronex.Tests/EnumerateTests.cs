using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class EnumerateTests
{
    [Fact]
    public void Enumerate_FixedCount()
    {
        var expr = CronexExpression.Parse("0 0 * * *");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var occurrences = expr.Enumerate(from, 5).ToList();
        occurrences.Count.ShouldBe(5);
        occurrences[0].DateTime.ShouldBe(new DateTime(2026, 1, 2, 0, 0, 0));
        occurrences[4].DateTime.ShouldBe(new DateTime(2026, 1, 6, 0, 0, 0));
    }

    [Fact]
    public void Enumerate_RespectsMax()
    {
        var expr = CronexExpression.Parse("*/5 * * * * {max:3}");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var occurrences = expr.Enumerate(from).ToList();
        occurrences.Count.ShouldBe(3);
    }

    [Fact]
    public void Enumerate_RespectsUntil()
    {
        var expr = CronexExpression.Parse("0 0 * * * {until:2026-01-05}");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var occurrences = expr.Enumerate(from, 100).ToList();
        // Jan 2, 3, 4, 5 (until is end of Jan 5)
        occurrences.Count.ShouldBe(4);
    }

    [Fact]
    public void Enumerate_Interval()
    {
        var expr = CronexExpression.Parse("@every 1h");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var occurrences = expr.Enumerate(from, 3).ToList();
        occurrences.Count.ShouldBe(3);
        occurrences[0].ShouldBe(from + TimeSpan.FromHours(1));
        occurrences[1].ShouldBe(from + TimeSpan.FromHours(2));
        occurrences[2].ShouldBe(from + TimeSpan.FromHours(3));
    }

    [Fact]
    public void Enumerate_Once_SingleOccurrence()
    {
        var expr = CronexExpression.Parse("@once 2026-06-01T09:00:00Z");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var occurrences = expr.Enumerate(from, 10).ToList();
        occurrences.Count.ShouldBe(1);
    }

    [Fact]
    public void Enumerate_CountOverridesMax()
    {
        var expr = CronexExpression.Parse("0 0 * * * {max:100}");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var occurrences = expr.Enumerate(from, 3).ToList();
        occurrences.Count.ShouldBe(3);
    }

    [Fact]
    public void Enumerate_WithFrom()
    {
        var expr = CronexExpression.Parse("0 9 * * * {from:2026-06-01}");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var occurrences = expr.Enumerate(from, 1).ToList();
        occurrences.Count.ShouldBe(1);
        occurrences[0].DateTime.Month.ShouldBeGreaterThanOrEqualTo(6);
    }

    [Fact]
    public void Enumerate_WeekdayOnly()
    {
        var expr = CronexExpression.Parse("0 9 * * MON-FRI");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var occurrences = expr.Enumerate(from, 5).ToList();
        foreach (var occ in occurrences)
        {
            occ.DayOfWeek.ShouldNotBe(DayOfWeek.Saturday);
            occ.DayOfWeek.ShouldNotBe(DayOfWeek.Sunday);
        }
    }

    [Fact]
    public void Enumerate_Empty_WhenPast()
    {
        var expr = CronexExpression.Parse("@once 2020-01-01T00:00:00Z");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var occurrences = expr.Enumerate(from).ToList();
        occurrences.ShouldBeEmpty();
    }
}
