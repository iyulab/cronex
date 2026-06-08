using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class CronAliasTests
{
    [Theory]
    [InlineData("@yearly", "0", "0", "1", "1", "*")]
    [InlineData("@annually", "0", "0", "1", "1", "*")]
    [InlineData("@monthly", "0", "0", "1", "*", "*")]
    [InlineData("@weekly", "0", "0", "*", "*", "0")]
    [InlineData("@daily", "0", "0", "*", "*", "*")]
    [InlineData("@midnight", "0", "0", "*", "*", "*")]
    [InlineData("@hourly", "0", "*", "*", "*", "*")]
    public void TryResolve_KnownAliases(string alias, string m, string h, string dom, string mon, string dow)
    {
        CronAlias.TryResolve(alias, out var fields).ShouldBeTrue();
        fields.ShouldNotBeNull();
        fields!.Length.ShouldBe(5);
        fields[0].ShouldBe(m);
        fields[1].ShouldBe(h);
        fields[2].ShouldBe(dom);
        fields[3].ShouldBe(mon);
        fields[4].ShouldBe(dow);
    }

    [Theory]
    [InlineData("@DAILY")]
    [InlineData("@Daily")]
    public void TryResolve_CaseInsensitive(string alias)
    {
        CronAlias.TryResolve(alias, out var fields).ShouldBeTrue();
        fields.ShouldNotBeNull();
    }

    [Fact]
    public void TryResolve_Unknown_ReturnsFalse()
    {
        CronAlias.TryResolve("@biweekly", out var fields).ShouldBeFalse();
        fields.ShouldBeNull();
    }
}
