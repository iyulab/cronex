using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// Golden-value tests locking the stagger offset formula (FNV-1a32(triggerId) % staggerMs) to fixed
/// reference values. `string.GetHashCode()` is randomized per process even via the
/// <c>StringComparison</c> overload, so the old implementation could not pass a test like this —
/// that was the bug (ISSUE-cronex-20260807-084712-stagger-hash-not-deterministic).
/// Reference values computed independently (Python FNV-1a32, UTF-8 bytes) — see cycle-2 log.
/// </summary>
public class StaggerHashTests
{
    // FNV-1a32("my-trigger")   = 1517134970 -> % 30000 = 4970 ms
    // FNV-1a32("same-id")      =  143740591 -> % 30000 = 10591 ms
    // FNV-1a32("a")            = 3826002220 -> % 30000 = 12220 ms
    // FNV-1a32("daily-report") = 3187717319 -> % 30000 = 7319 ms
    [Theory]
    [InlineData("my-trigger", 4970)]
    [InlineData("same-id", 10591)]
    [InlineData("a", 12220)]
    [InlineData("daily-report", 7319)]
    public async Task Stagger_GoldenOffset_MatchesFnv1a32Reference(string triggerId, int expectedOffsetMs)
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tp = new FakeTimeProvider(start);
        await using var scheduler = new CronexScheduler(tp);

        var fired = false;
        scheduler.Register(triggerId, "* * * * * {stagger:30s}", (ctx, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });

        // Nominal fire time is start + 1 minute. One millisecond before the golden offset elapses,
        // it must not have fired yet.
        tp.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromMilliseconds(expectedOffsetMs - 1));
        await scheduler.TickAsync();
        fired.ShouldBeFalse();

        // At (and past) the golden offset, it must fire.
        tp.Advance(TimeSpan.FromMilliseconds(1));
        await scheduler.TickAsync();
        fired.ShouldBeTrue();
    }

    [Fact]
    public async Task Stagger_SameId_ProducesIdenticalOffset_AcrossIndependentInstances()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tp1 = new FakeTimeProvider(start);
        var tp2 = new FakeTimeProvider(start);
        await using var s1 = new CronexScheduler(tp1);
        await using var s2 = new CronexScheduler(tp2);

        DateTimeOffset? fired1 = null, fired2 = null;
        s1.Register("shared-id", "* * * * * {stagger:45s}", (ctx, ct) =>
        {
            fired1 = ctx.ActualTime;
            return Task.CompletedTask;
        });
        s2.Register("shared-id", "* * * * * {stagger:45s}", (ctx, ct) =>
        {
            fired2 = ctx.ActualTime;
            return Task.CompletedTask;
        });

        // Jump to just before the nominal occurrence (t+60s) in one step — stagger only adds delay
        // from there — then advance in 1ms steps to find the exact instant each fires (up to +45s).
        tp1.Advance(TimeSpan.FromSeconds(59));
        tp2.Advance(TimeSpan.FromSeconds(59));
        await s1.TickAsync();
        await s2.TickAsync();

        for (var i = 0; i < 46_000; i++)
        {
            tp1.Advance(TimeSpan.FromMilliseconds(1));
            tp2.Advance(TimeSpan.FromMilliseconds(1));
            await s1.TickAsync();
            await s2.TickAsync();
            if (fired1 != null && fired2 != null) break;
        }

        fired1.ShouldNotBeNull();
        fired2.ShouldNotBeNull();
        fired1.ShouldBe(fired2); // same trigger ID -> same offset, independent of process/instance
    }
}
