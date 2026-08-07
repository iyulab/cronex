using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// Quartz "?" ("don't care") wildcard synonym for day-of-month/day-of-week
/// (ISSUE-cronex-20260807-084717-quartz-question-mark-support).
/// </summary>
public class QuestionMarkTests
{
    [Fact]
    public void QuestionMark_InDayOfMonth_SameNextOccurrenceAsWildcard()
    {
        var withQuestion = CronexExpression.Parse("0 0 12 ? * MON");
        var withWildcard = CronexExpression.Parse("0 0 12 * * MON");

        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        withQuestion.GetNextOccurrence(from).ShouldBe(withWildcard.GetNextOccurrence(from));
    }

    [Fact]
    public void QuestionMark_InDayOfWeek_SameNextOccurrenceAsWildcard()
    {
        var withQuestion = CronexExpression.Parse("0 0 12 15 * ?");
        var withWildcard = CronexExpression.Parse("0 0 12 15 * *");

        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        withQuestion.GetNextOccurrence(from).ShouldBe(withWildcard.GetNextOccurrence(from));
    }

    [Fact]
    public void QuestionMark_BothDomAndDow_ParsesAsAllWildcards()
    {
        var expr = CronexExpression.Parse("0 0 12 ? * ?");
        var wildcard = CronexExpression.Parse("0 0 12 * * *");

        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        expr.GetNextOccurrence(from).ShouldBe(wildcard.GetNextOccurrence(from));
    }

    [Fact]
    public void Validate_QuestionMarkInDom_NoErrors()
    {
        var result = ExpressionValidator.Validate("0 0 12 ? * MON");
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_QuestionMarkInDow_NoErrors()
    {
        var result = ExpressionValidator.Validate("0 0 12 15 * ?");
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_QuestionMarkNeverTriggersE019()
    {
        // "?" is a wildcard — always matches some valid day, never calendar-impossible.
        var result = ExpressionValidator.Validate("0 0 ? 2 *");
        result.Errors.ShouldNotContain(e => e.Code == "E019");
    }
}
