using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class CronexExpressionTests
{
    // --- Cron parsing ---

    [Fact]
    public void Parse_StandardCron_5Field()
    {
        var expr = CronexExpression.Parse("*/5 * * * *");
        expr.Kind.ShouldBe(ScheduleKind.Cron);
        expr.Timezone.ShouldBeNull();
        expr.TimeZoneInfo.ShouldBeNull();
        expr.CronSchedule.ShouldNotBeNull();
        expr.OptionsRaw.ShouldBeNull();
        expr.Matches(new DateTime(2026, 1, 1, 0, 5, 0)).ShouldBeTrue();
        expr.Matches(new DateTime(2026, 1, 1, 0, 3, 0)).ShouldBeFalse();
    }

    [Fact]
    public void Parse_StandardCron_6Field()
    {
        var expr = CronexExpression.Parse("30 */5 * * * *");
        expr.Kind.ShouldBe(ScheduleKind.Cron);
        expr.CronSchedule.ShouldNotBeNull();
        expr.CronSchedule!.HasSeconds.ShouldBeTrue();
    }

    // --- Timezone ---

    [Fact]
    public void Parse_WithTimezone()
    {
        var expr = CronexExpression.Parse("TZ=UTC 0 9 * * MON-FRI");
        expr.Timezone.ShouldBe("UTC");
        expr.TimeZoneInfo.ShouldNotBeNull();
        expr.TimeZoneInfo!.Id.ShouldBe("UTC");
        expr.Kind.ShouldBe(ScheduleKind.Cron);
    }

    [Fact]
    public void Parse_InvalidTimezone_Fails()
    {
        CronexExpression.TryParse("TZ=Fake/Zone 0 0 * * *", out _, out var error)
            .ShouldBeFalse();
        error!.ShouldContain("Unknown timezone");
    }

    // --- Aliases ---

    [Theory]
    [InlineData("@daily")]
    [InlineData("@midnight")]
    [InlineData("@hourly")]
    public void Parse_Alias(string alias)
    {
        var expr = CronexExpression.Parse(alias);
        expr.Kind.ShouldBe(ScheduleKind.Alias);
        expr.CronSchedule.ShouldNotBeNull();
        // Verify the alias resolves correctly via matching
        if (alias is "@daily" or "@midnight")
        {
            expr.Matches(new DateTime(2026, 1, 1, 0, 0, 0)).ShouldBeTrue();
            expr.Matches(new DateTime(2026, 1, 1, 1, 0, 0)).ShouldBeFalse();
        }
    }

    [Theory]
    [InlineData("@yearly")]
    [InlineData("@annually")]
    public void Parse_Alias_Yearly(string alias)
    {
        var expr = CronexExpression.Parse(alias);
        expr.Kind.ShouldBe(ScheduleKind.Alias);
        // Jan 1 00:00
        expr.Matches(new DateTime(2026, 1, 1, 0, 0, 0)).ShouldBeTrue();
        expr.Matches(new DateTime(2026, 2, 1, 0, 0, 0)).ShouldBeFalse();
    }

    [Fact]
    public void Parse_Alias_Monthly()
    {
        var expr = CronexExpression.Parse("@monthly");
        expr.Matches(new DateTime(2026, 3, 1, 0, 0, 0)).ShouldBeTrue();
        expr.Matches(new DateTime(2026, 3, 2, 0, 0, 0)).ShouldBeFalse();
    }

    [Fact]
    public void Parse_Alias_Weekly()
    {
        var expr = CronexExpression.Parse("@weekly");
        // Sunday 00:00
        expr.Matches(new DateTime(2026, 1, 4, 0, 0, 0)).ShouldBeTrue();  // Sunday
        expr.Matches(new DateTime(2026, 1, 5, 0, 0, 0)).ShouldBeFalse(); // Monday
    }

    [Fact]
    public void Parse_UnknownAlias_Fails()
    {
        CronexExpression.TryParse("@biweekly", out _, out var error)
            .ShouldBeFalse();
        error!.ShouldContain("Unknown alias");
    }

    // --- Aliases with TZ and options ---

    [Fact]
    public void Parse_AliasWithTimezoneAndOptions()
    {
        var expr = CronexExpression.Parse("TZ=UTC @daily {jitter:5m}");
        expr.Kind.ShouldBe(ScheduleKind.Alias);
        expr.Timezone.ShouldBe("UTC");
        expr.OptionsRaw.ShouldBe("jitter:5m");
        expr.CronSchedule.ShouldNotBeNull();
    }

    // --- Options block ---

    [Fact]
    public void Parse_WithOptions()
    {
        var expr = CronexExpression.Parse("0 9 * * * {jitter:30s, until:2025-12-31}");
        expr.OptionsRaw.ShouldBe("jitter:30s, until:2025-12-31");
        expr.Kind.ShouldBe(ScheduleKind.Cron);
    }

    // --- Interval/Once (kind detection, parsing deferred to Cycle 06) ---

    [Fact]
    public void Parse_Interval_Kind()
    {
        var expr = CronexExpression.Parse("@every 30m");
        expr.Kind.ShouldBe(ScheduleKind.Interval);
        expr.CronSchedule.ShouldBeNull();
    }

    [Fact]
    public void Parse_Once_Kind()
    {
        var expr = CronexExpression.Parse("@once 2025-03-01T09:00:00+09:00");
        expr.Kind.ShouldBe(ScheduleKind.Once);
        expr.CronSchedule.ShouldBeNull();
    }

    // --- Error handling ---

    [Fact]
    public void Parse_Empty_Throws()
    {
        Should.Throw<FormatException>(() => CronexExpression.Parse(""));
    }

    [Fact]
    public void TryParse_InvalidCron_ReturnsFalse()
    {
        CronexExpression.TryParse("invalid cron", out _, out var error)
            .ShouldBeFalse();
        error.ShouldNotBeNull();
    }
}
