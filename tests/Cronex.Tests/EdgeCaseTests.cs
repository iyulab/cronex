using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// Edge case and boundary condition tests for comprehensive coverage.
/// </summary>
public class EdgeCaseTests
{
    // --- DOM=31 for months with fewer days ---

    [Fact]
    public void Next_Dom31_SkipsShortMonths()
    {
        // "0 0 31 * *" should skip months with fewer than 31 days
        var sched = CronSchedule.Parse(["0", "0", "31", "*", "*"]);
        var from = new DateTime(2026, 1, 31, 1, 0, 0); // After Jan 31
        var next = sched.Next(from);
        // Feb has 28 days, so skip to March 31
        next.ShouldBe(new DateTime(2026, 3, 31, 0, 0, 0));
    }

    [Fact]
    public void Next_Dom31_SkipsAprilJuneSeptNov()
    {
        var sched = CronSchedule.Parse(["0", "0", "31", "*", "*"]);
        var from = new DateTime(2026, 3, 31, 1, 0, 0); // After March 31
        var next = sched.Next(from);
        // April has 30 days → skip to May 31
        next.ShouldBe(new DateTime(2026, 5, 31, 0, 0, 0));
    }

    [Fact]
    public void Next_Dom30_SkipsFeb()
    {
        var sched = CronSchedule.Parse(["0", "0", "30", "*", "*"]);
        var from = new DateTime(2026, 1, 30, 1, 0, 0); // After Jan 30
        var next = sched.Next(from);
        // Feb has 28 days → skip to March 30
        next.ShouldBe(new DateTime(2026, 3, 30, 0, 0, 0));
    }

    // --- Special cron entries with Next() ---

    [Fact]
    public void Next_NearestWeekday_SaturdayToFriday()
    {
        // 15W — nearest weekday to 15th
        // 2026-08-15 is Saturday → should match Friday 14th
        var sched = CronSchedule.Parse(["0", "0", "15W", "*", "*"]);
        var from = new DateTime(2026, 8, 1, 0, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 8, 14, 0, 0, 0));
    }

    [Fact]
    public void Next_NearestWeekday_SundayToMonday()
    {
        // 15W — nearest weekday to 15th
        // 2026-02-15 is Sunday → should match Monday 16th
        var sched = CronSchedule.Parse(["0", "0", "15W", "*", "*"]);
        var from = new DateTime(2026, 2, 1, 0, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 2, 16, 0, 0, 0));
    }

    [Fact]
    public void Next_NearestWeekday_AlreadyWeekday()
    {
        // 15W in a month where 15th is already a weekday
        // 2026-01-15 is Thursday
        var sched = CronSchedule.Parse(["0", "0", "15W", "*", "*"]);
        var from = new DateTime(2026, 1, 1, 0, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 1, 15, 0, 0, 0));
    }

    [Fact]
    public void Next_LastWeekday()
    {
        // LW — last weekday of month
        // Jan 2026: 31st is Saturday → last weekday is Friday 30th
        var sched = CronSchedule.Parse(["0", "0", "LW", "*", "*"]);
        var from = new DateTime(2026, 1, 1, 0, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 1, 30, 0, 0, 0));
    }

    [Fact]
    public void Next_LastDayOffset()
    {
        // L-3 — 3 days before last day
        // Jan has 31 days → L-3 = 28th
        var sched = CronSchedule.Parse(["0", "0", "L-3", "*", "*"]);
        var from = new DateTime(2026, 1, 1, 0, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 1, 28, 0, 0, 0));
    }

    [Fact]
    public void Next_LastDayOffset_CrossMonth()
    {
        // L-3 in Feb: 28 days → L-3 = 25th
        var sched = CronSchedule.Parse(["0", "0", "L-3", "*", "*"]);
        var from = new DateTime(2026, 2, 1, 0, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 2, 25, 0, 0, 0));
    }

    [Fact]
    public void Next_LastDowOfMonth()
    {
        // 5L — last Friday of month
        // Jan 2026: 30th is Friday
        var sched = CronSchedule.Parse(["0", "0", "*", "*", "5L"]);
        var from = new DateTime(2026, 1, 1, 0, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 1, 30, 0, 0, 0));
    }

    [Fact]
    public void Next_NthDow_FirstMonday()
    {
        // MON#1 — first Monday
        // Feb 2026: first Monday is 2nd
        var sched = CronSchedule.Parse(["0", "0", "*", "*", "MON#1"]);
        var from = new DateTime(2026, 2, 1, 0, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 2, 2, 0, 0, 0));
    }

    [Fact]
    public void Next_NthDow_FifthOccurrence_SkipsMonth()
    {
        // MON#5 — 5th Monday
        // Jan 2026 has no 5th Monday (4 Mondays: 5,12,19,26) → skip to Feb
        // Feb 2026 has no 5th Monday → skip to March
        // Mar 2026: Mondays are 2,9,16,23,30 → 5th Monday is 30th
        var sched = CronSchedule.Parse(["0", "0", "*", "*", "MON#5"]);
        var from = new DateTime(2026, 1, 1, 0, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 3, 30, 0, 0, 0));
    }

    // --- ToString round-trip ---

    [Theory]
    [InlineData("*/5 * * * *")]
    [InlineData("0 9 * * MON-FRI")]
    [InlineData("@daily")]
    [InlineData("@every 30m")]
    [InlineData("@every 1h-2h")]
    public void ToString_RoundTrip_Parseable(string input)
    {
        var expr = CronexExpression.Parse(input);
        var str = expr.ToString();
        // Should be parseable again
        var roundTripped = CronexExpression.Parse(str);
        roundTripped.Kind.ShouldBe(expr.Kind);
    }

    [Fact]
    public void ToString_RoundTrip_WithTimezoneAndOptions()
    {
        var expr = CronexExpression.Parse("TZ=UTC 0 9 * * MON-FRI {jitter:30s, max:10}");
        var str = expr.ToString();
        var rt = CronexExpression.Parse(str);
        rt.Timezone.ShouldBe("UTC");
        rt.Options.Jitter.ShouldNotBeNull();
        rt.Options.Max.ShouldBe(10);
    }

    // --- Enumerate edge cases ---

    [Fact]
    public void Enumerate_WithMaxOption()
    {
        var expr = CronexExpression.Parse("*/5 * * * * {max:3}");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var results = expr.Enumerate(from).ToList();
        results.Count.ShouldBe(3);
    }

    [Fact]
    public void Enumerate_CountOverridesMax()
    {
        var expr = CronexExpression.Parse("*/5 * * * * {max:100}");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var results = expr.Enumerate(from, 2).ToList();
        results.Count.ShouldBe(2);
    }

    [Fact]
    public void Enumerate_Once_SingleResult()
    {
        var expr = CronexExpression.Parse("@once 2026-06-01T09:00:00Z");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var results = expr.Enumerate(from).ToList();
        results.Count.ShouldBe(1);
        results[0].UtcDateTime.ShouldBe(new DateTime(2026, 6, 1, 9, 0, 0));
    }

    // --- Validation: multiple errors ---

    [Fact]
    public void Validate_MultipleErrors()
    {
        var result = ExpressionValidator.Validate("60 25 32 13 8");
        result.IsValid.ShouldBeFalse();
        result.Errors.Count.ShouldBeGreaterThan(1);
    }

    // --- Parser edge cases ---

    [Fact]
    public void Parse_WhitespaceInExpression_Trimmed()
    {
        var expr = CronexExpression.Parse("  */5 * * * *  ");
        expr.Kind.ShouldBe(ScheduleKind.Cron);
    }

    [Fact]
    public void Parse_NullExpression_Throws()
    {
        Should.Throw<FormatException>(() => CronexExpression.Parse(""));
    }

    [Fact]
    public void TryParse_NullExpression_ReturnsFalse()
    {
        CronexExpression.TryParse("", out _, out var error).ShouldBeFalse();
        error.ShouldNotBeNull();
    }

    // --- Interval edge cases ---

    [Fact]
    public void Interval_VerySmall_1Second()
    {
        var expr = CronexExpression.Parse("@every 1s");
        expr.IntervalSchedule.ShouldNotBeNull();
        expr.IntervalSchedule!.Value.Interval.Value.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Interval_LargeDuration_Days()
    {
        var expr = CronexExpression.Parse("@every 7d");
        expr.IntervalSchedule.ShouldNotBeNull();
        expr.IntervalSchedule!.Value.Interval.Value.ShouldBe(TimeSpan.FromDays(7));
    }

    // --- Once edge cases ---

    [Fact]
    public void Once_ExactlyAtFrom_ReturnsNull()
    {
        // @once fire time == from → should return null (exclusive)
        var fireAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        var expr = CronexExpression.Parse($"@once {fireAt:O}");
        var next = expr.GetNextOccurrence(fireAt);
        next.ShouldBeNull();
    }

    [Fact]
    public void Once_JustBeforeFrom_ReturnsFireAt()
    {
        var fireAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        var expr = CronexExpression.Parse($"@once {fireAt:O}");
        var from = fireAt.AddSeconds(-1);
        var next = expr.GetNextOccurrence(from);
        next.ShouldNotBeNull();
        next!.Value.ShouldBe(fireAt);
    }

    // --- Cron with 6-field seconds ---

    [Fact]
    public void Next_SixField_EverySecond()
    {
        var sched = CronSchedule.Parse(["*", "*", "*", "*", "*", "*"]);
        var from = new DateTime(2026, 1, 1, 0, 0, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 1, 1, 0, 0, 1));
    }

    [Fact]
    public void Next_SixField_Every15Seconds()
    {
        var sched = CronSchedule.Parse(["*/15", "*", "*", "*", "*", "*"]);
        var from = new DateTime(2026, 1, 1, 0, 0, 10);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2026, 1, 1, 0, 0, 15));
    }

    // --- Reversed range edge cases ---

    [Fact]
    public void Next_ReversedRange_DOW_FriToMon()
    {
        // 5-1 → FRI(5),SAT(6),SUN(0),MON(1)
        var sched = CronSchedule.Parse(["0", "0", "*", "*", "5-1"]);
        // 2026-01-07 is Wednesday
        var from = new DateTime(2026, 1, 7, 0, 0, 0);
        var next = sched.Next(from);
        // Next Friday is Jan 9
        next.ShouldBe(new DateTime(2026, 1, 9, 0, 0, 0));
    }

    [Fact]
    public void Next_ReversedRange_Months_OctToMar()
    {
        // 10-3 → Oct, Nov, Dec, Jan, Feb, Mar
        var sched = CronSchedule.Parse(["0", "0", "1", "10-3", "*"]);
        var from = new DateTime(2026, 4, 1, 0, 0, 0); // April
        var next = sched.Next(from);
        // Next match is Oct 1
        next.ShouldBe(new DateTime(2026, 10, 1, 0, 0, 0));
    }

    // --- Year boundary wrap ---

    [Fact]
    public void Next_Dec31_WrapsToJan()
    {
        var sched = CronSchedule.Parse(["0", "0", "*", "*", "*"]);
        var from = new DateTime(2026, 12, 31, 23, 59, 0);
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2027, 1, 1, 0, 0, 0));
    }

    // --- Leap year: Feb 29 ---

    [Fact]
    public void Next_Feb29_LeapYearBoundary()
    {
        var sched = CronSchedule.Parse(["0", "0", "29", "2", "*"]);
        var from = new DateTime(2027, 1, 1, 0, 0, 0);
        // 2028 is a leap year
        var next = sched.Next(from);
        next.ShouldBe(new DateTime(2028, 2, 29, 0, 0, 0));
    }

    [Fact]
    public void Matches_Feb29_NonLeapYear()
    {
        var sched = CronSchedule.Parse(["0", "0", "29", "2", "*"]);
        // 2026 is not a leap year — Feb 29 doesn't exist
        sched.Matches(new DateTime(2026, 2, 28, 0, 0, 0)).ShouldBeFalse();
    }

    // --- ScheduleOptions edge cases ---

    [Fact]
    public void Options_NegativeMax_Fails()
    {
        ScheduleOptions.TryParse("max:-1", out _, out var error).ShouldBeFalse();
        error.ShouldNotBeNull();
    }

    [Fact]
    public void Options_DuplicateKey_LastWins()
    {
        // Duplicate keys: last value should take effect
        var opts = ScheduleOptions.Parse("max:5, max:10");
        opts.Max.ShouldBe(10);
    }

    [Fact]
    public void Options_EmptyTag_Parsed()
    {
        var opts = ScheduleOptions.Parse("tag:single");
        opts.Tags.ShouldNotBeNull();
        opts.Tags!.Count.ShouldBe(1);
        opts.Tags[0].ShouldBe("single");
    }
}
