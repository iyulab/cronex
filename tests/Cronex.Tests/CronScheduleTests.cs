using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class CronScheduleTests
{
    [Fact]
    public void Parse_FiveFields()
    {
        var sched = CronSchedule.Parse(["*/5", "*", "*", "*", "*"]);
        sched.HasSeconds.ShouldBeFalse();
        sched.Minute.Matches(0).ShouldBeTrue();
        sched.Minute.Matches(5).ShouldBeTrue();
        sched.Minute.Matches(3).ShouldBeFalse();
        sched.Second.Matches(0).ShouldBeTrue(); // implicit 0
    }

    [Fact]
    public void Parse_SixFields()
    {
        var sched = CronSchedule.Parse(["30", "0", "*", "*", "*", "*"]);
        sched.HasSeconds.ShouldBeTrue();
        sched.Second.Matches(30).ShouldBeTrue();
        sched.Second.Matches(0).ShouldBeFalse();
    }

    [Fact]
    public void Parse_InvalidFieldCount_Throws()
    {
        Should.Throw<FormatException>(() => CronSchedule.Parse(["*", "*", "*"]));
    }

    [Fact]
    public void Matches_WeekdayMorning()
    {
        // 0 9 * * MON-FRI
        var sched = CronSchedule.Parse(["0", "9", "*", "*", "MON-FRI"]);

        // Monday 9:00:00
        sched.Matches(new DateTime(2026, 3, 2, 9, 0, 0)).ShouldBeTrue();
        // Friday 9:00:00
        sched.Matches(new DateTime(2026, 3, 6, 9, 0, 0)).ShouldBeTrue();
        // Saturday 9:00:00
        sched.Matches(new DateTime(2026, 3, 7, 9, 0, 0)).ShouldBeFalse();
        // Monday 10:00:00
        sched.Matches(new DateTime(2026, 3, 2, 10, 0, 0)).ShouldBeFalse();
    }

    [Fact]
    public void Matches_EveryFiveMinutes()
    {
        var sched = CronSchedule.Parse(["*/5", "*", "*", "*", "*"]);
        sched.Matches(new DateTime(2026, 1, 1, 0, 0, 0)).ShouldBeTrue();
        sched.Matches(new DateTime(2026, 1, 1, 0, 5, 0)).ShouldBeTrue();
        sched.Matches(new DateTime(2026, 1, 1, 0, 3, 0)).ShouldBeFalse();
    }

    [Fact]
    public void Matches_SpecificDays()
    {
        // 30 4 1,15 * *
        var sched = CronSchedule.Parse(["30", "4", "1,15", "*", "*"]);
        sched.Matches(new DateTime(2026, 6, 1, 4, 30, 0)).ShouldBeTrue();
        sched.Matches(new DateTime(2026, 6, 15, 4, 30, 0)).ShouldBeTrue();
        sched.Matches(new DateTime(2026, 6, 2, 4, 30, 0)).ShouldBeFalse();
    }
}
