using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// T-4: DOM/DOW OR semantics tests (C-1 fix verification).
/// Standard Vixie Cron: when both DOM and DOW are non-wildcard, match on either (OR).
/// </summary>
public class DomDowOrSemanticsTests
{
    [Fact]
    public void Next_DomAndDow_BothNonWild_UsesOrSemantics()
    {
        // "0 0 15 * FRI" = 15th of month OR every Friday
        var expr = CronexExpression.Parse("0 0 15 * FRI");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        var dt = next!.Value;
        // Should match either 15th or a Friday — whichever comes first
        // 2026-01-02 is a Friday; 2026-01-15 is a Thursday
        // So first match should be 2026-01-02 (Friday)
        dt.Day.ShouldBe(2);
        dt.DayOfWeek.ShouldBe(DayOfWeek.Friday);
    }

    [Fact]
    public void Next_DomAndDow_OrSemantics_DomComesFirst()
    {
        // "0 0 3 * FRI" = 3rd of month OR every Friday
        var expr = CronexExpression.Parse("0 0 3 * FRI");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        var dt = next!.Value;
        // 2026-01-02 is Friday, 2026-01-03 is Saturday
        // First match: 2026-01-02 (Friday)
        dt.Day.ShouldBe(2);
    }

    [Fact]
    public void Next_DomAndDow_OrSemantics_MultipleOccurrences()
    {
        // "0 0 1 * MON" = 1st of month OR every Monday
        var expr = CronexExpression.Parse("0 0 1 * MON");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var occurrences = expr.Enumerate(from, 5).ToList();
        occurrences.Count.ShouldBe(5);

        // Each occurrence should be either the 1st or a Monday
        foreach (var occ in occurrences)
        {
            var isFirst = occ.Day == 1;
            var isMonday = occ.DayOfWeek == DayOfWeek.Monday;
            (isFirst || isMonday).ShouldBeTrue(
                $"Expected 1st or Monday but got {occ:yyyy-MM-dd} ({occ.DayOfWeek})");
        }
    }

    [Fact]
    public void Matches_DomAndDow_BothNonWild_OrSemantics()
    {
        var cron = CronSchedule.Parse(["0", "0", "15", "*", "5"]); // 15th OR Friday
        var friday = new DateTime(2026, 1, 2, 0, 0, 0); // Friday, not 15th
        cron.Matches(friday).ShouldBeTrue();

        var fifteenth = new DateTime(2026, 1, 15, 0, 0, 0); // 15th, Thursday
        cron.Matches(fifteenth).ShouldBeTrue();

        var neither = new DateTime(2026, 1, 3, 0, 0, 0); // Saturday, not 15th
        cron.Matches(neither).ShouldBeFalse();
    }

    [Fact]
    public void Next_DomWild_DowNonWild_ChecksDowOnly()
    {
        // "0 0 * * MON" = every Monday (DOM wildcard)
        var expr = CronexExpression.Parse("0 0 * * MON");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        next!.Value.DayOfWeek.ShouldBe(DayOfWeek.Monday);
    }

    [Fact]
    public void Next_DomNonWild_DowWild_ChecksDomOnly()
    {
        // "0 0 15 * *" = every 15th (DOW wildcard)
        var expr = CronexExpression.Parse("0 0 15 * *");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        next!.Value.Day.ShouldBe(15);
    }

    [Fact]
    public void Next_SpecialDom_WildDow_DomOnly()
    {
        // "0 0 L * *" = last day of month (DOW wildcard)
        var expr = CronexExpression.Parse("0 0 L * *");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        next!.Value.Day.ShouldBe(31); // Jan has 31 days
    }

    [Fact]
    public void Next_WildDom_SpecialDow_DowOnly()
    {
        // "0 0 * * MON#2" = second Monday of month (DOM wildcard)
        var expr = CronexExpression.Parse("0 0 * * MON#2");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        next!.Value.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        // Should be the 2nd Monday of January 2026 = Jan 12
        next!.Value.Day.ShouldBe(12);
    }

    [Fact]
    public void Matches_BothWild_MatchesAnyDay()
    {
        var cron = CronSchedule.Parse(["0", "0", "*", "*", "*"]);
        cron.Matches(new DateTime(2026, 1, 1, 0, 0, 0)).ShouldBeTrue();
        cron.Matches(new DateTime(2026, 6, 15, 0, 0, 0)).ShouldBeTrue();
    }
}
