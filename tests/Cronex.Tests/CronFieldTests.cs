using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class CronFieldTests
{
    [Fact]
    public void Wildcard_MatchesAll()
    {
        var field = CronField.Parse("*", CronFieldType.Minute);
        field.IsWildcard.ShouldBeTrue();
        field.Matches(0).ShouldBeTrue();
        field.Matches(30).ShouldBeTrue();
        field.Matches(59).ShouldBeTrue();
    }

    [Fact]
    public void WildcardStep_MatchesMultiples()
    {
        var field = CronField.Parse("*/5", CronFieldType.Minute);
        field.Matches(0).ShouldBeTrue();
        field.Matches(5).ShouldBeTrue();
        field.Matches(55).ShouldBeTrue();
        field.Matches(3).ShouldBeFalse();
        field.Matches(58).ShouldBeFalse();
    }

    [Fact]
    public void SingleValue_MatchesExact()
    {
        var field = CronField.Parse("30", CronFieldType.Minute);
        field.Matches(30).ShouldBeTrue();
        field.Matches(0).ShouldBeFalse();
        field.Matches(31).ShouldBeFalse();
    }

    [Fact]
    public void Range_MatchesInclusive()
    {
        var field = CronField.Parse("9-17", CronFieldType.Hour);
        field.Matches(8).ShouldBeFalse();
        field.Matches(9).ShouldBeTrue();
        field.Matches(13).ShouldBeTrue();
        field.Matches(17).ShouldBeTrue();
        field.Matches(18).ShouldBeFalse();
    }

    [Fact]
    public void ReversedRange_WrapsAround()
    {
        // 23-01 means 23,0,1
        var field = CronField.Parse("23-1", CronFieldType.Hour);
        field.Matches(23).ShouldBeTrue();
        field.Matches(0).ShouldBeTrue();
        field.Matches(1).ShouldBeTrue();
        field.Matches(22).ShouldBeFalse();
        field.Matches(2).ShouldBeFalse();
    }

    [Fact]
    public void RangeWithStep()
    {
        var field = CronField.Parse("0-30/10", CronFieldType.Minute);
        field.Matches(0).ShouldBeTrue();
        field.Matches(10).ShouldBeTrue();
        field.Matches(20).ShouldBeTrue();
        field.Matches(30).ShouldBeTrue();
        field.Matches(5).ShouldBeFalse();
        field.Matches(31).ShouldBeFalse();
    }

    [Fact]
    public void CommaSeparatedList()
    {
        var field = CronField.Parse("1,15", CronFieldType.DayOfMonth);
        field.Matches(1).ShouldBeTrue();
        field.Matches(15).ShouldBeTrue();
        field.Matches(2).ShouldBeFalse();
    }

    [Fact]
    public void DayOfWeek_NamedValues()
    {
        var field = CronField.Parse("MON-FRI", CronFieldType.DayOfWeek);
        field.Matches(1).ShouldBeTrue();  // MON
        field.Matches(5).ShouldBeTrue();  // FRI
        field.Matches(0).ShouldBeFalse(); // SUN
        field.Matches(6).ShouldBeFalse(); // SAT
    }

    [Fact]
    public void DayOfWeek_Seven_NormalizedToZero()
    {
        // Both 0 and 7 mean Sunday
        var field = CronField.Parse("7", CronFieldType.DayOfWeek);
        field.Matches(0).ShouldBeTrue(); // normalized to 0
    }

    [Fact]
    public void Month_NamedValues()
    {
        var field = CronField.Parse("JAN,JUN,DEC", CronFieldType.Month);
        field.Matches(1).ShouldBeTrue();
        field.Matches(6).ShouldBeTrue();
        field.Matches(12).ShouldBeTrue();
        field.Matches(2).ShouldBeFalse();
    }

    [Fact]
    public void OutOfRange_ThrowsFormatException()
    {
        Should.Throw<FormatException>(() => CronField.Parse("25", CronFieldType.Hour));
    }

    [Fact]
    public void InvalidStep_ThrowsFormatException()
    {
        Should.Throw<FormatException>(() => CronField.Parse("*/0", CronFieldType.Minute));
    }

    [Fact]
    public void ReversedRange_DayOfWeek_FriToMon()
    {
        // FRI-MON = FRI,SAT,SUN,MON
        var field = CronField.Parse("FRI-MON", CronFieldType.DayOfWeek);
        field.Matches(5).ShouldBeTrue();  // FRI
        field.Matches(6).ShouldBeTrue();  // SAT
        field.Matches(0).ShouldBeTrue();  // SUN
        field.Matches(1).ShouldBeTrue();  // MON
        field.Matches(2).ShouldBeFalse(); // TUE
        field.Matches(4).ShouldBeFalse(); // THU
    }
}
