using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class SpecialCronEntryTests
{
    [Fact]
    public void LastDay_MatchesLastDayOfMonth()
    {
        var entry = SpecialCronEntry.LastDay();
        entry.Matches(new DateOnly(2026, 1, 31)).ShouldBeTrue();
        entry.Matches(new DateOnly(2026, 2, 28)).ShouldBeTrue();
        entry.Matches(new DateOnly(2024, 2, 29)).ShouldBeTrue(); // leap year
        entry.Matches(new DateOnly(2026, 1, 30)).ShouldBeFalse();
    }

    [Fact]
    public void LastDayOffset_L3()
    {
        var entry = SpecialCronEntry.LastDayOffset(3);
        // January: 31-3 = 28
        entry.Matches(new DateOnly(2026, 1, 28)).ShouldBeTrue();
        entry.Matches(new DateOnly(2026, 1, 31)).ShouldBeFalse();
        // February (non-leap): 28-3 = 25
        entry.Matches(new DateOnly(2026, 2, 25)).ShouldBeTrue();
    }

    [Fact]
    public void NearestWeekday_15W()
    {
        var entry = SpecialCronEntry.NearestWeekday(15);

        // 2026-03-15 is Sunday → nearest weekday is Monday 16th
        new DateOnly(2026, 3, 15).DayOfWeek.ShouldBe(System.DayOfWeek.Sunday);
        entry.Matches(new DateOnly(2026, 3, 16)).ShouldBeTrue();
        entry.Matches(new DateOnly(2026, 3, 15)).ShouldBeFalse();

        // 2026-08-15 is Saturday → nearest weekday is Friday 14th
        new DateOnly(2026, 8, 15).DayOfWeek.ShouldBe(System.DayOfWeek.Saturday);
        entry.Matches(new DateOnly(2026, 8, 14)).ShouldBeTrue();

        // 2026-04-15 is Wednesday → exact match
        new DateOnly(2026, 4, 15).DayOfWeek.ShouldBe(System.DayOfWeek.Wednesday);
        entry.Matches(new DateOnly(2026, 4, 15)).ShouldBeTrue();
    }

    [Fact]
    public void NearestWeekday_1W_Saturday()
    {
        // 2026-08-01 is Saturday → can't go Friday (prev month), go to Monday 3rd
        new DateOnly(2026, 8, 1).DayOfWeek.ShouldBe(System.DayOfWeek.Saturday);
        var entry = SpecialCronEntry.NearestWeekday(1);
        entry.Matches(new DateOnly(2026, 8, 3)).ShouldBeTrue();
    }

    [Fact]
    public void LastWeekday_LW()
    {
        var entry = SpecialCronEntry.LastWeekday();

        // 2026-01-31 is Saturday → nearest weekday is Friday 30th
        new DateOnly(2026, 1, 31).DayOfWeek.ShouldBe(System.DayOfWeek.Saturday);
        entry.Matches(new DateOnly(2026, 1, 30)).ShouldBeTrue();
        entry.Matches(new DateOnly(2026, 1, 31)).ShouldBeFalse();

        // 2026-05-31 is Sunday → nearest weekday is Friday 29th
        new DateOnly(2026, 5, 31).DayOfWeek.ShouldBe(System.DayOfWeek.Sunday);
        entry.Matches(new DateOnly(2026, 5, 29)).ShouldBeTrue();
    }

    [Fact]
    public void LastDowOfMonth_LastFriday()
    {
        var entry = SpecialCronEntry.LastDowOfMonth(5); // Friday

        // 2026-01: last Friday is 30th
        entry.Matches(new DateOnly(2026, 1, 30)).ShouldBeTrue();
        entry.Matches(new DateOnly(2026, 1, 23)).ShouldBeFalse(); // also Friday but not last
    }

    [Fact]
    public void NthDowOfMonth_SecondMonday()
    {
        var entry = SpecialCronEntry.NthDowOfMonth(1, 2); // 2nd Monday

        // 2026-03: Mon 2, 9, 16, 23, 30 → 2nd Monday = 9th
        entry.Matches(new DateOnly(2026, 3, 9)).ShouldBeTrue();
        entry.Matches(new DateOnly(2026, 3, 2)).ShouldBeFalse(); // 1st Monday
        entry.Matches(new DateOnly(2026, 3, 16)).ShouldBeFalse(); // 3rd Monday
    }

    // Integration: parsing via CronSchedule
    [Fact]
    public void CronSchedule_ParsesLastDay()
    {
        var sched = CronSchedule.Parse(["0", "0", "L", "*", "*"]);
        sched.DayOfMonthSpecial.ShouldNotBeNull();
        sched.DayOfMonthSpecial.Value.Kind.ShouldBe(SpecialCronEntryKind.LastDay);
        sched.Matches(new DateTime(2026, 1, 31, 0, 0, 0)).ShouldBeTrue();
        sched.Matches(new DateTime(2026, 1, 30, 0, 0, 0)).ShouldBeFalse();
    }

    [Fact]
    public void CronSchedule_ParsesNthDow()
    {
        // MON#2 — second Monday
        var sched = CronSchedule.Parse(["0", "0", "*", "*", "MON#2"]);
        sched.DayOfWeekSpecial.ShouldNotBeNull();
        sched.DayOfWeekSpecial.Value.Kind.ShouldBe(SpecialCronEntryKind.NthDowOfMonth);
        sched.Matches(new DateTime(2026, 3, 9, 0, 0, 0)).ShouldBeTrue();
    }

    [Fact]
    public void CronSchedule_ParsesLastDow()
    {
        // 5L — last Friday
        var sched = CronSchedule.Parse(["0", "0", "*", "*", "5L"]);
        sched.DayOfWeekSpecial.ShouldNotBeNull();
        sched.DayOfWeekSpecial.Value.Kind.ShouldBe(SpecialCronEntryKind.LastDowOfMonth);
        sched.Matches(new DateTime(2026, 1, 30, 0, 0, 0)).ShouldBeTrue();
    }

    [Fact]
    public void CronSchedule_ParsesNearestWeekday()
    {
        // 15W
        var sched = CronSchedule.Parse(["0", "0", "15W", "*", "*"]);
        sched.DayOfMonthSpecial.ShouldNotBeNull();
        sched.DayOfMonthSpecial.Value.Kind.ShouldBe(SpecialCronEntryKind.NearestWeekday);
    }
}
