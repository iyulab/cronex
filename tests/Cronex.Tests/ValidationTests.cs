using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class ValidationTests
{
    [Fact]
    public void Valid_StandardCron()
    {
        var result = ExpressionValidator.Validate("*/5 * * * *");
        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Valid_FullExpression()
    {
        var result = ExpressionValidator.Validate("TZ=UTC 0 9 * * MON-FRI {jitter:30s}");
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Valid_Alias()
    {
        var result = ExpressionValidator.Validate("@daily");
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Valid_Interval()
    {
        var result = ExpressionValidator.Validate("@every 30m");
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Valid_Once()
    {
        var result = ExpressionValidator.Validate("@once 2026-01-01T00:00:00Z");
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Valid_OnceRelative()
    {
        var result = ExpressionValidator.Validate("@once +20m");
        result.IsValid.ShouldBeTrue();
    }

    // --- Error codes ---

    [Fact]
    public void E010_Empty()
    {
        var result = ExpressionValidator.Validate("");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E010");
    }

    [Fact]
    public void E010_WrongFieldCount()
    {
        var result = ExpressionValidator.Validate("* * *");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E010");
    }

    [Fact]
    public void E011_InvalidTimezone()
    {
        var result = ExpressionValidator.Validate("TZ=Fake/Zone 0 0 * * *");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E011");
    }

    [Fact]
    public void E001_SecondOutOfRange()
    {
        var result = ExpressionValidator.Validate("60 * * * * *");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E001");
    }

    [Fact]
    public void E002_MinuteOutOfRange()
    {
        var result = ExpressionValidator.Validate("60 * * * *");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E002");
    }

    [Fact]
    public void E003_HourOutOfRange()
    {
        var result = ExpressionValidator.Validate("0 25 * * *");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E003");
    }

    [Fact]
    public void E004_DomOutOfRange()
    {
        var result = ExpressionValidator.Validate("0 0 32 * *");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E004");
    }

    [Fact]
    public void E005_MonthOutOfRange()
    {
        var result = ExpressionValidator.Validate("0 0 1 13 *");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E005");
    }

    [Fact]
    public void E006_DowOutOfRange()
    {
        var result = ExpressionValidator.Validate("0 0 * * 8");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E006");
    }

    [Fact]
    public void E007_StepNotPositive()
    {
        var result = ExpressionValidator.Validate("*/0 * * * *");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E007");
    }

    [Fact]
    public void E012_InvalidOnceDate()
    {
        var result = ExpressionValidator.Validate("@once not-a-date");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E012");
    }

    [Fact]
    public void E013_InvalidIntervalDuration()
    {
        var result = ExpressionValidator.Validate("@every abc");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E013");
    }

    [Fact]
    public void E014_IntervalMinGreaterThanMax()
    {
        var result = ExpressionValidator.Validate("@every 2h-1h");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E014");
    }

    [Fact]
    public void E015_UnknownOption()
    {
        var result = ExpressionValidator.Validate("0 0 * * * {foo:bar}");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E015");
    }

    [Fact]
    public void E017_OnceRelativeNotPositive()
    {
        var result = ExpressionValidator.Validate("@once +abc");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E017");
    }

    [Fact]
    public void E020_FromAfterUntil()
    {
        var result = ExpressionValidator.Validate("0 0 * * * {from:2026-12-31, until:2026-01-01}");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E020");
    }

    [Fact]
    public void E016_InvalidOptionValue()
    {
        var result = ExpressionValidator.Validate("0 0 * * * {max:abc}");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E016");
    }

    // --- Special fields are valid ---

    [Fact]
    public void Valid_SpecialDom_L()
    {
        var result = ExpressionValidator.Validate("0 0 L * *");
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Valid_SpecialDow_Hash()
    {
        var result = ExpressionValidator.Validate("0 0 * * MON#2");
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Valid_SpecialDom_W()
    {
        var result = ExpressionValidator.Validate("0 0 15W * *");
        result.IsValid.ShouldBeTrue();
    }

    // --- Unknown alias ---

    [Fact]
    public void E010_UnknownAlias()
    {
        var result = ExpressionValidator.Validate("@biweekly");
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "E010");
    }
}
