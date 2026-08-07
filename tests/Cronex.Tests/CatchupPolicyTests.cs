using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// MF-1: Misfire/catchup policy tests (ISSUE-cronex-20260807-084716-misfire-catchup-policy).
/// A per-minute trigger left unticked for 5 minutes has 5 missed occurrences by the time the next
/// tick happens; these tests pin down what each `catchup` policy does with that backlog.
/// </summary>
public class CatchupPolicyTests
{
    [Fact]
    public async Task Default_NoCatchupOption_FiresEveryMissedOccurrence_OnePerTick()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fireCount = 0;
        scheduler.Register("test", "* * * * *", (ctx, ct) =>
        {
            fireCount++;
            return Task.CompletedTask;
        });

        // Fall 5 occurrences behind, then tick 5 times without further advancing — matches the
        // pre-existing "all" behavior exactly (regression guard for the default).
        tp.Advance(TimeSpan.FromMinutes(5));
        for (var i = 0; i < 5; i++)
            await scheduler.TickAsync();

        fireCount.ShouldBe(5);
    }

    [Fact]
    public async Task CatchupSkip_DiscardsBacklog_FiresNothingForMissedOccurrences()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fireCount = 0;
        string? skipReason = null;
        scheduler.TriggerSkipped += (_, reason) => skipReason = reason;
        scheduler.Register("test", "* * * * * {catchup:skip}", (ctx, ct) =>
        {
            fireCount++;
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(5));
        for (var i = 0; i < 5; i++)
            await scheduler.TickAsync();

        fireCount.ShouldBe(0);
        skipReason.ShouldBe("catchup skip");
    }

    [Fact]
    public async Task CatchupSkip_ResumesNormallyAtNextOccurrenceAfterBacklog()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fireCount = 0;
        scheduler.Register("test", "* * * * * {catchup:skip}", (ctx, ct) =>
        {
            fireCount++;
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(5)); // backlog of 5
        await scheduler.TickAsync(); // discards the backlog, jumps NextFireTime to t+6m
        fireCount.ShouldBe(0);

        tp.Advance(TimeSpan.FromMinutes(1)); // now t+6m — the next real occurrence
        await scheduler.TickAsync();
        fireCount.ShouldBe(1);
    }

    [Fact]
    public async Task CatchupOnce_FiresExactlyOnce_ForTheMostRecentMissedOccurrence()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tp = new FakeTimeProvider(start);
        await using var scheduler = new CronexScheduler(tp);

        var fireCount = 0;
        DateTimeOffset? lastScheduledTime = null;
        scheduler.Register("test", "* * * * * {catchup:once}", (ctx, ct) =>
        {
            fireCount++;
            lastScheduledTime = ctx.ScheduledTime;
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(5)); // occurrences at +1m..+5m are all missed
        for (var i = 0; i < 5; i++)
            await scheduler.TickAsync();

        fireCount.ShouldBe(1); // only the latest, not all 5
        lastScheduledTime.ShouldBe(start.AddMinutes(5));
    }

    [Fact]
    public async Task InvalidCatchupOption_ErrorE016()
    {
        var result = ExpressionValidator.Validate("* * * * * {catchup:bogus}");
        result.Errors.ShouldContain(e => e.Code == "E016");
    }

    [Fact]
    public void ScheduleOptions_CatchupRoundTrips()
    {
        var options = ScheduleOptions.Parse("catchup:skip");
        options.Catchup.ShouldBe(CatchupPolicy.Skip);
        options.ToString().ShouldBe("catchup:skip");
    }

    [Fact]
    public async Task WhenNotBehind_CatchupPolicyHasNoEffect()
    {
        // Only one occurrence due at a time — "all" vs. "skip" vs. "once" should be indistinguishable.
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fired = false;
        scheduler.Register("test", "* * * * * {catchup:skip}", (ctx, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(1)); // exactly one occurrence due, no backlog
        await scheduler.TickAsync();

        fired.ShouldBeTrue();
    }
}
