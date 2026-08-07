using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class ExpressionValidatorTests
{
    [Fact]
    public void Validate_DuplicateTags_WarnsW001()
    {
        // m-6: Duplicate tags within a single tag: option (+ separated)
        var result = ExpressionValidator.Validate("0 9 * * * {tag:foo+bar+foo}");
        result.Warnings.ShouldContain(w => w.Code == "W001" && w.Message.Contains("foo"));
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_NoDuplicateTags_NoWarnings()
    {
        var result = ExpressionValidator.Validate("0 9 * * * {tag:foo+bar}");
        result.Warnings.ShouldBeEmpty();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_EmptyExpression_ErrorE010()
    {
        var result = ExpressionValidator.Validate("");
        result.Errors.ShouldContain(e => e.Code == "E010");
    }

    [Fact]
    public void Validate_UnknownAlias_ErrorE010()
    {
        var result = ExpressionValidator.Validate("@bogus");
        result.Errors.ShouldContain(e => e.Code == "E010");
    }

    [Fact]
    public void Validate_InvalidCronFieldCount_ErrorE010()
    {
        var result = ExpressionValidator.Validate("* *");
        result.Errors.ShouldContain(e => e.Code == "E010");
    }

    [Fact]
    public void Validate_UnmatchedBrace_ErrorE010()
    {
        // m-7: Structural tokenizer errors surface as E010
        var result = ExpressionValidator.Validate("0 9 * * * {jitter:30s");
        result.Errors.ShouldContain(e => e.Code == "E010");
    }

    [Fact]
    public void Validate_ValidCron_NoErrors()
    {
        var result = ExpressionValidator.Validate("0 9 * * MON-FRI");
        result.Errors.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_FromAfterUntil_ErrorE020()
    {
        var result = ExpressionValidator.Validate(
            "0 9 * * * {from:2025-12-31, until:2025-01-01}");
        result.Errors.ShouldContain(e => e.Code == "E020");
    }

    [Fact]
    public void Validate_JitterExceedsHalfInterval_WarnsE022()
    {
        // @every 10m with jitter:6m → 6m > 50% of 10m → E022 warning
        var result = ExpressionValidator.Validate("@every 10m {jitter:6m}");
        result.Errors.ShouldBeEmpty();
        result.Warnings.ShouldContain(w => w.Code == "E022");
    }

    [Fact]
    public void Validate_JitterWithinHalfInterval_NoWarning()
    {
        // @every 10m with jitter:4m → 4m < 50% of 10m → no warning
        var result = ExpressionValidator.Validate("@every 10m {jitter:4m}");
        result.Errors.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_StaggerExceedsInterval_WarnsE025()
    {
        // @every 10m with stagger:15m → 15m > 10m → E025 warning
        var result = ExpressionValidator.Validate("@every 10m {stagger:15m}");
        result.Errors.ShouldBeEmpty();
        result.Warnings.ShouldContain(w => w.Code == "E025");
    }

    [Fact]
    public void Validate_StaggerWithinInterval_NoWarning()
    {
        // @every 10m with stagger:5m → 5m < 10m → no warning
        var result = ExpressionValidator.Validate("@every 10m {stagger:5m}");
        result.Errors.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void ValidationError_HasPositionProperty()
    {
        // spec §5.4: ValidationError.Position is nullable int
        var result = ExpressionValidator.Validate("* * * * * * * *");
        result.Errors.Count.ShouldBeGreaterThan(0);
        // Position is null by default (not yet computed)
        result.Errors[0].Position.ShouldBeNull();
    }

    // E018: @once absolute time already in the past (ISSUE-cronex-20260807-090000-once-past-time-silent)

    [Fact]
    public void Validate_OncePastAbsoluteTime_ErrorE018()
    {
        var referenceTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var result = ExpressionValidator.Validate("@once 2020-01-01T00:00:00Z", referenceTime);
        result.Errors.ShouldContain(e => e.Code == "E018");
    }

    [Fact]
    public void Validate_OnceFutureAbsoluteTime_NoError()
    {
        var referenceTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var result = ExpressionValidator.Validate("@once 2030-01-01T00:00:00Z", referenceTime);
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_OnceExactlyAtReferenceTime_ErrorE018()
    {
        // "right now" is treated as already-past — by the time Register() runs it will be.
        var referenceTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var result = ExpressionValidator.Validate("@once 2026-01-01T00:00:00Z", referenceTime);
        result.Errors.ShouldContain(e => e.Code == "E018");
    }

    [Fact]
    public void Validate_OnceRelativeDuration_NeverErrorsE018()
    {
        // Relative +duration is always future-relative by construction (E017 already guards
        // non-positive durations) — E018 only applies to the absolute-datetime form.
        var result = ExpressionValidator.Validate("@once +5m");
        result.Errors.ShouldNotContain(e => e.Code == "E018");
    }

    [Fact]
    public void Validate_OnceNoReferenceTime_UsesUtcNow()
    {
        // No referenceTime supplied -> defaults to DateTimeOffset.UtcNow (same convention as
        // OnceSchedule.TryParse), so a clearly-past absolute time is still caught.
        var result = ExpressionValidator.Validate("@once 2000-01-01T00:00:00Z");
        result.Errors.ShouldContain(e => e.Code == "E018");
    }

    // E019: calendar-impossible day-of-month/month combination
    // (ISSUE-cronex-20260807-084718-impossible-expression-validation)

    [Fact]
    public void Validate_Feb30_ErrorE019()
    {
        var result = ExpressionValidator.Validate("0 0 30 2 *");
        result.Errors.ShouldContain(e => e.Code == "E019");
    }

    [Fact]
    public void Validate_April31_ErrorE019()
    {
        var result = ExpressionValidator.Validate("0 0 31 4 *"); // April has 30 days
        result.Errors.ShouldContain(e => e.Code == "E019");
    }

    [Fact]
    public void Validate_Feb29_NoError_ValidInLeapYears()
    {
        var result = ExpressionValidator.Validate("0 0 29 2 *");
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_Jan31_NoError()
    {
        var result = ExpressionValidator.Validate("0 0 31 1 *"); // January has 31 days
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_DayOfMonthWildcard_NeverErrorsE019()
    {
        var result = ExpressionValidator.Validate("0 0 * 2 *");
        result.Errors.ShouldNotContain(e => e.Code == "E019");
    }

    [Fact]
    public void Validate_LastDayOfMonthSpecial_NeverErrorsE019()
    {
        // "L" (special DOM) resolves dynamically per calendar month — never impossible.
        var result = ExpressionValidator.Validate("0 0 L 2 *");
        result.Errors.ShouldNotContain(e => e.Code == "E019");
    }

    [Fact]
    public void Validate_MonthListWithOneImpossibleDay_ErrorE019Only_WhenAllImpossible()
    {
        // Day 31 across Apr/Jun/Sep/Nov (all 30-day months) — impossible for all of them.
        var result = ExpressionValidator.Validate("0 0 31 4,6,9,11 *");
        result.Errors.ShouldContain(e => e.Code == "E019");
    }

    [Fact]
    public void Validate_MonthListWithOnePossibleDay_NoError()
    {
        // Day 31 across Jan (31 days) and Apr (30 days) — possible in January, so not flagged.
        var result = ExpressionValidator.Validate("0 0 31 1,4 *");
        result.Errors.ShouldNotContain(e => e.Code == "E019");
    }
}
