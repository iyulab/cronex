using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class ScheduleOptionsTests
{
    [Fact]
    public void Parse_Jitter()
    {
        var opts = ScheduleOptions.Parse("jitter:30s");
        opts.Jitter.ShouldNotBeNull();
        opts.Jitter!.Value.Value.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Parse_Stagger()
    {
        var opts = ScheduleOptions.Parse("stagger:3m");
        opts.Stagger.ShouldNotBeNull();
        opts.Stagger!.Value.Value.ShouldBe(TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void Parse_Window()
    {
        var opts = ScheduleOptions.Parse("window:5m");
        opts.Window.ShouldNotBeNull();
        opts.Window!.Value.Value.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Parse_Max()
    {
        var opts = ScheduleOptions.Parse("max:10");
        opts.Max.ShouldBe(10);
    }

    [Fact]
    public void Parse_Tag_Single()
    {
        var opts = ScheduleOptions.Parse("tag:report");
        opts.Tags.ShouldNotBeNull();
        opts.Tags!.Count.ShouldBe(1);
        opts.Tags[0].ShouldBe("report");
    }

    [Fact]
    public void Parse_Tag_Multiple()
    {
        var opts = ScheduleOptions.Parse("tag:report+daily");
        opts.Tags.ShouldNotBeNull();
        opts.Tags!.Count.ShouldBe(2);
        opts.Tags[0].ShouldBe("report");
        opts.Tags[1].ShouldBe("daily");
    }

    [Fact]
    public void Parse_FromDate()
    {
        var opts = ScheduleOptions.Parse("from:2025-06-01");
        opts.From.ShouldNotBeNull();
        opts.From!.Value.Year.ShouldBe(2025);
        opts.From.Value.Month.ShouldBe(6);
        opts.From.Value.Day.ShouldBe(1);
    }

    [Fact]
    public void Parse_UntilDate()
    {
        var opts = ScheduleOptions.Parse("until:2025-12-31");
        opts.Until.ShouldNotBeNull();
        opts.Until!.Value.Year.ShouldBe(2025);
        opts.Until.Value.Month.ShouldBe(12);
        opts.Until.Value.Day.ShouldBe(31);
        // date-only until should be end of day
        opts.Until.Value.Hour.ShouldBe(23);
        opts.Until.Value.Minute.ShouldBe(59);
    }

    [Fact]
    public void Parse_FromDateTime()
    {
        var opts = ScheduleOptions.Parse("from:2025-06-01T09:00:00Z");
        opts.From.ShouldNotBeNull();
        opts.From!.Value.Hour.ShouldBe(9);
    }

    [Fact]
    public void Parse_Multiple()
    {
        var opts = ScheduleOptions.Parse("jitter:30s, until:2025-12-31");
        opts.Jitter.ShouldNotBeNull();
        opts.Until.ShouldNotBeNull();
    }

    [Fact]
    public void Parse_Complex()
    {
        var opts = ScheduleOptions.Parse("jitter:5m, stagger:3m, window:10m, max:100, tag:health-check");
        opts.Jitter!.Value.Value.ShouldBe(TimeSpan.FromMinutes(5));
        opts.Stagger!.Value.Value.ShouldBe(TimeSpan.FromMinutes(3));
        opts.Window!.Value.Value.ShouldBe(TimeSpan.FromMinutes(10));
        opts.Max.ShouldBe(100);
        opts.Tags!.Count.ShouldBe(1);
        opts.Tags[0].ShouldBe("health-check");
    }

    [Fact]
    public void Parse_Empty_ReturnsDefaults()
    {
        var opts = ScheduleOptions.Parse("");
        opts.Jitter.ShouldBeNull();
        opts.Max.ShouldBeNull();
        opts.Tags.ShouldBeNull();
    }

    [Fact]
    public void Parse_UnknownOption_Fails()
    {
        ScheduleOptions.TryParse("foo:bar", out _, out var error).ShouldBeFalse();
        error!.ShouldContain("unknown option");
    }

    [Fact]
    public void Parse_InvalidMax_Fails()
    {
        ScheduleOptions.TryParse("max:abc", out _, out var error).ShouldBeFalse();
        error!.ShouldContain("invalid max");
    }

    [Fact]
    public void Parse_MaxZero_Fails()
    {
        ScheduleOptions.TryParse("max:0", out _, out var error).ShouldBeFalse();
        error!.ShouldContain("invalid max");
    }

    [Fact]
    public void ToString_RoundTrip()
    {
        var opts = ScheduleOptions.Parse("jitter:30s, max:10, tag:report+daily");
        var str = opts.ToString();
        str.ShouldContain("jitter:30s");
        str.ShouldContain("max:10");
        str.ShouldContain("tag:report+daily");
    }

    // Integration: via CronexExpression

    [Fact]
    public void CronexExpression_OptionsAreParsed()
    {
        var expr = CronexExpression.Parse("0 9 * * * {jitter:30s, until:2025-12-31}");
        expr.Options.Jitter.ShouldNotBeNull();
        expr.Options.Until.ShouldNotBeNull();
    }

    [Fact]
    public void CronexExpression_NoOptions_EmptyOptions()
    {
        var expr = CronexExpression.Parse("0 9 * * *");
        expr.Options.ShouldNotBeNull();
        expr.Options.Jitter.ShouldBeNull();
    }

    [Fact]
    public void CronexExpression_InvalidOptions_Fails()
    {
        CronexExpression.TryParse("0 9 * * * {foo:bar}", out _, out var error)
            .ShouldBeFalse();
        error!.ShouldContain("unknown option");
    }
}
