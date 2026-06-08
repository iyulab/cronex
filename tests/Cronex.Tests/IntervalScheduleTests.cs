using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class IntervalScheduleTests
{
    [Fact]
    public void Parse_FixedInterval()
    {
        var ivl = IntervalSchedule.Parse("30m");
        ivl.Interval.Value.ShouldBe(TimeSpan.FromMinutes(30));
        ivl.IsRange.ShouldBeFalse();
        ivl.MaxInterval.ShouldBeNull();
    }

    [Fact]
    public void Parse_CompoundDuration()
    {
        var ivl = IntervalSchedule.Parse("1h30m");
        ivl.Interval.Value.ShouldBe(TimeSpan.FromMinutes(90));
        ivl.IsRange.ShouldBeFalse();
    }

    [Fact]
    public void Parse_RangeInterval()
    {
        var ivl = IntervalSchedule.Parse("1h-2h");
        ivl.IsRange.ShouldBeTrue();
        ivl.Interval.Value.ShouldBe(TimeSpan.FromHours(1));
        ivl.MaxInterval!.Value.Value.ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Parse_RangeCompound()
    {
        var ivl = IntervalSchedule.Parse("30m-1h30m");
        ivl.IsRange.ShouldBeTrue();
        ivl.Interval.Value.ShouldBe(TimeSpan.FromMinutes(30));
        ivl.MaxInterval!.Value.Value.ShouldBe(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void Parse_Seconds()
    {
        var ivl = IntervalSchedule.Parse("45s");
        ivl.Interval.Value.ShouldBe(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void Parse_Empty_Fails()
    {
        IntervalSchedule.TryParse("", out _, out var error).ShouldBeFalse();
        error.ShouldNotBeNull();
    }

    [Fact]
    public void Parse_InvalidDuration_Fails()
    {
        IntervalSchedule.TryParse("abc", out _, out var error).ShouldBeFalse();
        error!.ShouldContain("invalid duration");
    }

    [Fact]
    public void Parse_RangeMinGreaterThanMax_Fails()
    {
        IntervalSchedule.TryParse("2h-1h", out _, out var error).ShouldBeFalse();
        error!.ShouldContain("min duration must be less than max");
    }

    [Fact]
    public void Parse_RangeEqual_Fails()
    {
        IntervalSchedule.TryParse("1h-1h", out _, out var error).ShouldBeFalse();
        error!.ShouldContain("min duration must be less than max");
    }
}
