using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class CronexDurationTests
{
    [Theory]
    [InlineData("30s", 30_000)]
    [InlineData("5m", 300_000)]
    [InlineData("2h", 7_200_000)]
    [InlineData("1d", 86_400_000)]
    [InlineData("500ms", 500)]
    [InlineData("1h30m", 5_400_000)]
    [InlineData("1d2h30m15s500ms", 95_415_500)]
    public void Parse_ValidDurations(string input, long expectedMs)
    {
        var d = CronexDuration.Parse(input);
        d.TotalMilliseconds.ShouldBe(expectedMs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("30")]
    [InlineData("30x")]
    [InlineData("m")]
    public void TryParse_InvalidDurations_ReturnsFalse(string input)
    {
        CronexDuration.TryParse(input, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("1h30m", "1h30m")]
    [InlineData("90m", "1h30m")]
    [InlineData("3600s", "1h")]
    [InlineData("500ms", "500ms")]
    [InlineData("86400000ms", "1d")]
    public void ToString_CanonicalForm(string input, string expected)
    {
        var d = CronexDuration.Parse(input);
        d.ToString().ShouldBe(expected);
    }

    [Fact]
    public void Equality_SameValue()
    {
        var a = CronexDuration.Parse("1h30m");
        var b = CronexDuration.Parse("90m");
        (a == b).ShouldBeTrue();
        a.Equals(b).ShouldBeTrue();
    }

    [Fact]
    public void ImplicitConversion_ToTimeSpan()
    {
        var d = CronexDuration.Parse("2h");
        TimeSpan ts = d;
        ts.ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public void TryParse_Overflow_ReturnsFalse()
    {
        // m-3: Overflow should return false, not produce garbage
        CronexDuration.TryParse("999999999999999999d", out _).ShouldBeFalse();
    }

    [Fact]
    public void Default_Struct_HasEmptyOriginal()
    {
        // m-9: default struct should not have null Original
        var d = default(CronexDuration);
        d.Original.ShouldNotBeNull();
        d.Original.ShouldBe(string.Empty);
        d.Value.ShouldBe(TimeSpan.Zero);
        d.ToString().ShouldBe("0ms");
    }
}
