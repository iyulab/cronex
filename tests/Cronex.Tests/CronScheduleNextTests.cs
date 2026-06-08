using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class CronScheduleNextTests
{
    private static CronSchedule Parse(string expr)
    {
        var fields = expr.Split(' ');
        return CronSchedule.Parse(fields);
    }

    [Fact]
    public void Next_EveryMinute()
    {
        var sched = Parse("* * * * *");
        var from = new DateTime(2026, 1, 1, 0, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 1, 1, 0, 1, 0));
    }

    [Fact]
    public void Next_EveryFiveMinutes()
    {
        var sched = Parse("*/5 * * * *");
        var from = new DateTime(2026, 1, 1, 0, 3, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 1, 1, 0, 5, 0));
    }

    [Fact]
    public void Next_SpecificTime()
    {
        var sched = Parse("30 9 * * *");
        var from = new DateTime(2026, 1, 1, 10, 0, 0);
        var next = sched.Next(from);
        // Next occurrence is tomorrow at 09:30
        next.ShouldBe(new DateTime(2026, 1, 2, 9, 30, 0));
    }

    [Fact]
    public void Next_SpecificTime_SameDay()
    {
        var sched = Parse("30 9 * * *");
        var from = new DateTime(2026, 1, 1, 8, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 1, 1, 9, 30, 0));
    }

    [Fact]
    public void Next_WeekdayOnly()
    {
        var sched = Parse("0 9 * * MON-FRI");
        // 2026-01-03 is Saturday
        var from = new DateTime(2026, 1, 3, 0, 0, 0);
        var next = sched.Next(from);
        // Monday 2026-01-05
        next.ShouldBe(new DateTime(2026, 1, 5, 9, 0, 0));
    }

    [Fact]
    public void Next_MonthBoundary()
    {
        var sched = Parse("0 0 1 * *");
        var from = new DateTime(2026, 1, 15, 0, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 2, 1, 0, 0, 0));
    }

    [Fact]
    public void Next_YearBoundary()
    {
        var sched = Parse("0 0 1 1 *");
        var from = new DateTime(2026, 3, 1, 0, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2027, 1, 1, 0, 0, 0));
    }

    [Fact]
    public void Next_WithSeconds()
    {
        var sched = Parse("30 * * * * *");
        var from = new DateTime(2026, 1, 1, 0, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 1, 1, 0, 0, 30));
    }

    [Fact]
    public void Next_ExcludesFrom()
    {
        var sched = Parse("* * * * *");
        var from = new DateTime(2026, 1, 1, 0, 0, 0);
        var next = sched.Next(from);
        // Should not return `from` itself
        next.ShouldBe(new DateTime(2026, 1, 1, 0, 1, 0));
    }

    [Fact]
    public void Next_SpecificDayOfMonth()
    {
        var sched = Parse("0 0 15 * *");
        var from = new DateTime(2026, 1, 20, 0, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 2, 15, 0, 0, 0));
    }

    [Fact]
    public void Next_Feb29_LeapYear()
    {
        var sched = Parse("0 0 29 2 *");
        var from = new DateTime(2026, 1, 1, 0, 0, 0);
        var next = sched.Next(from);
        // Next Feb 29 is 2028
        next.ShouldBe(new DateTime(2028, 2, 29, 0, 0, 0));
    }

    [Fact]
    public void Next_LastDay()
    {
        var sched = CronSchedule.Parse(["0", "0", "L", "*", "*"]);
        var from = new DateTime(2026, 1, 15, 0, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 1, 31, 0, 0, 0));
    }

    [Fact]
    public void Next_LastDay_CrossMonth()
    {
        var sched = CronSchedule.Parse(["0", "0", "L", "*", "*"]);
        var from = new DateTime(2026, 1, 31, 1, 0, 0); // After midnight on Jan 31
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 2, 28, 0, 0, 0));
    }

    [Fact]
    public void Next_MultipleTimes()
    {
        var sched = Parse("0 9 * * *");
        var from = new DateTime(2026, 1, 1, 0, 0, 0);
        var t1 = sched.Next(from);
        t1.ShouldBe(new DateTime(2026, 1, 1, 9, 0, 0));
        var t2 = sched.Next(t1!.Value);
        t2.ShouldBe(new DateTime(2026, 1, 2, 9, 0, 0));
        var t3 = sched.Next(t2!.Value);
        t3.ShouldBe(new DateTime(2026, 1, 3, 9, 0, 0));
    }

    [Fact]
    public void Next_HourWrap()
    {
        var sched = Parse("45 * * * *");
        var from = new DateTime(2026, 1, 1, 0, 50, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 1, 1, 1, 45, 0));
    }

    [Fact]
    public void Next_NthDow_SecondMonday()
    {
        var sched = CronSchedule.Parse(["0", "0", "*", "*", "MON#2"]);
        var from = new DateTime(2026, 3, 1, 0, 0, 0);
        var next = sched.Next(from);
        // 2nd Monday of March 2026 = 9th
        next.ShouldBe(new DateTime(2026, 3, 9, 0, 0, 0));
    }

    [Fact]
    public void Next_ReversedRange_Hours()
    {
        // 22-02 hours → matches 22,23,0,1,2
        var sched = Parse("0 22-2 * * *");
        var from = new DateTime(2026, 1, 1, 20, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 1, 1, 22, 0, 0));
    }
}
