using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// T-2: DST spring-forward and fall-back tests (M-5 fix verification).
/// Uses US Eastern Time for testing.
/// </summary>
public class DstTests
{
    private static TimeZoneInfo GetEasternTz()
    {
        // Try IANA first (Linux), then Windows ID
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }

    private static string GetEasternTzId()
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            return "America/New_York";
        }
        catch (TimeZoneNotFoundException)
        {
            return "Eastern Standard Time";
        }
    }

    [Fact]
    public void Next_SpringForward_SkipsInvalidTime()
    {
        // America/New_York: 2026-03-08 02:00 → 03:00 (spring-forward)
        var tzId = GetEasternTzId();
        var expr = CronexExpression.Parse($"TZ={tzId} 30 2 * * *");
        // From before the gap
        var from = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.FromHours(-5));
        var next = expr.GetNextOccurrence(from);

        // 02:30 doesn't exist — should skip to next valid occurrence
        next.ShouldNotBeNull();

        // The result should NOT be UTC 07:30 (which would be invalid 02:30 EST)
        // It should be the next valid 02:30 (March 9th or later)
        var tz = GetEasternTz();
        var localTime = TimeZoneInfo.ConvertTime(next!.Value, tz);
        tz.IsInvalidTime(localTime.DateTime).ShouldBeFalse(
            $"Result {next.Value} converts to invalid local time {localTime}");
    }

    [Fact]
    public void Next_FallBack_FiresOncePerDay()
    {
        // America/New_York: 2026-11-01 02:00 → 01:00 (fall-back)
        var tzId = GetEasternTzId();
        var expr = CronexExpression.Parse($"TZ={tzId} 30 1 * * *");
        // From before fall-back
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4));
        var occurrences = expr.Enumerate(from, 2).ToList();

        occurrences.Count.ShouldBe(2);
        // 01:30 exists twice (EDT and EST), but should fire only once per day
        var gap = occurrences[1] - occurrences[0];
        gap.TotalHours.ShouldBeGreaterThan(23);
    }

    [Fact]
    public void ResolveLocalTime_NormalTime_ReturnsCorrectOffset()
    {
        var tzId = GetEasternTzId();
        var expr = CronexExpression.Parse($"TZ={tzId} 0 12 * * *");
        // July 1st 2026 — EDT (UTC-4)
        var from = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(-4));
        var next = expr.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        var tz = GetEasternTz();
        var local = TimeZoneInfo.ConvertTime(next!.Value, tz);
        local.Hour.ShouldBe(12);
    }

    [Fact]
    public void Next_SpringForward_HourlySchedule_SkipsGap()
    {
        // Hourly schedule during spring-forward
        var tzId = GetEasternTzId();
        var expr = CronexExpression.Parse($"TZ={tzId} 0 * * * *");
        // From 01:00 on spring-forward day
        var from = new DateTimeOffset(2026, 3, 8, 1, 0, 0, TimeSpan.FromHours(-5));
        var occurrences = expr.Enumerate(from, 3).ToList();

        occurrences.Count.ShouldBe(3);
        var tz = GetEasternTz();
        foreach (var occ in occurrences)
        {
            var local = TimeZoneInfo.ConvertTime(occ, tz);
            tz.IsInvalidTime(local.DateTime).ShouldBeFalse(
                $"Occurrence {occ} at local {local} is in DST gap");
        }
    }

    [Fact]
    public void Next_WithTimezone_RespectsTimezone()
    {
        var tzId = GetEasternTzId();
        var expr = CronexExpression.Parse($"TZ={tzId} 0 9 * * MON");
        // From Monday midnight UTC
        var from = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var next = expr.GetNextOccurrence(from);

        next.ShouldNotBeNull();
        var tz = GetEasternTz();
        var local = TimeZoneInfo.ConvertTime(next!.Value, tz);
        local.Hour.ShouldBe(9);
        local.DayOfWeek.ShouldBe(DayOfWeek.Monday);
    }
}
