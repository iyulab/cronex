using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class ExpressionTokenizerTests
{
    [Fact]
    public void Tokenize_SimpleCron()
    {
        var t = ExpressionTokenizer.Tokenize("*/5 * * * *");
        t.Kind.ShouldBe(ScheduleKind.Cron);
        t.Timezone.ShouldBeNull();
        t.Body.ShouldBe("*/5 * * * *");
        t.OptionsRaw.ShouldBeNull();
    }

    [Fact]
    public void Tokenize_WithTimezone()
    {
        var t = ExpressionTokenizer.Tokenize("TZ=Asia/Seoul 0 9 * * MON-FRI");
        t.Timezone.ShouldBe("Asia/Seoul");
        t.Body.ShouldBe("0 9 * * MON-FRI");
        t.Kind.ShouldBe(ScheduleKind.Cron);
    }

    [Fact]
    public void Tokenize_WithOptions()
    {
        var t = ExpressionTokenizer.Tokenize("0 9 * * * {jitter:30s, until:2025-12-31}");
        t.Body.ShouldBe("0 9 * * *");
        t.OptionsRaw.ShouldBe("jitter:30s, until:2025-12-31");
        t.Kind.ShouldBe(ScheduleKind.Cron);
    }

    [Fact]
    public void Tokenize_FullExpression()
    {
        var t = ExpressionTokenizer.Tokenize("TZ=UTC 0 9 * * MON-FRI {jitter:30s}");
        t.Timezone.ShouldBe("UTC");
        t.Body.ShouldBe("0 9 * * MON-FRI");
        t.OptionsRaw.ShouldBe("jitter:30s");
    }

    [Fact]
    public void Tokenize_EveryInterval()
    {
        var t = ExpressionTokenizer.Tokenize("@every 30m");
        t.Kind.ShouldBe(ScheduleKind.Interval);
        t.Body.ShouldBe("@every 30m");
    }

    [Fact]
    public void Tokenize_Once()
    {
        var t = ExpressionTokenizer.Tokenize("@once 2025-03-01T09:00:00+09:00");
        t.Kind.ShouldBe(ScheduleKind.Once);
        t.Body.ShouldBe("@once 2025-03-01T09:00:00+09:00");
    }

    [Fact]
    public void Tokenize_OnceRelative()
    {
        var t = ExpressionTokenizer.Tokenize("@once +20m");
        t.Kind.ShouldBe(ScheduleKind.Once);
        t.Body.ShouldBe("@once +20m");
    }

    [Fact]
    public void Tokenize_Alias()
    {
        var t = ExpressionTokenizer.Tokenize("@daily");
        t.Kind.ShouldBe(ScheduleKind.Alias);
        t.Body.ShouldBe("@daily");
    }

    [Fact]
    public void Tokenize_AliasWithTimezoneAndOptions()
    {
        var t = ExpressionTokenizer.Tokenize("TZ=Asia/Seoul @daily {jitter:5m}");
        t.Timezone.ShouldBe("Asia/Seoul");
        t.Body.ShouldBe("@daily");
        t.OptionsRaw.ShouldBe("jitter:5m");
        t.Kind.ShouldBe(ScheduleKind.Alias);
    }

    [Fact]
    public void Tokenize_Empty_Throws()
    {
        Should.Throw<FormatException>(() => ExpressionTokenizer.Tokenize(""));
    }

    [Fact]
    public void Tokenize_TrailingContentAfterOptions_Throws()
    {
        // m-7: Content after closing brace should be rejected
        var ex = Should.Throw<FormatException>(
            () => ExpressionTokenizer.Tokenize("0 9 * * * {jitter:30s} extra"));
        ex.Message.ShouldContain("Unexpected content after options block");
    }

    [Fact]
    public void Tokenize_UnmatchedClosingBrace_Throws()
    {
        var ex = Should.Throw<FormatException>(
            () => ExpressionTokenizer.Tokenize("0 9 * * * }"));
        ex.Message.ShouldContain("Unmatched closing brace");
    }

    [Fact]
    public void Tokenize_UnmatchedOpeningBrace_Throws()
    {
        var ex = Should.Throw<FormatException>(
            () => ExpressionTokenizer.Tokenize("0 9 * * * {jitter:30s"));
        ex.Message.ShouldContain("Unmatched opening brace");
    }

    [Fact]
    public void Tokenize_TzOnly_Throws()
    {
        // TZ= prefix without schedule body
        Should.Throw<FormatException>(
            () => ExpressionTokenizer.Tokenize("TZ=UTC"));
    }
}
