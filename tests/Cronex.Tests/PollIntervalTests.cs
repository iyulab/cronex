using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// P-1: Tests for the event-driven poll interval that replaced a fixed 1-second
/// <c>Task.Delay</c> (ISSUE-cronex-20260807-084715-fixed-poll-drift).
/// <see cref="CronexScheduler.ComputeNextPollDelay"/> is `internal` and exercised directly — the
/// automatic loop it drives (<c>Start()</c>) uses real timers even under a fake
/// <see cref="TimeProvider"/> (the base <c>TimeProvider.CreateTimer</c> isn't virtualized by this
/// test suite's minimal <see cref="FakeTimeProvider"/>), so asserting exact computed delays this way
/// is both faster and more precise than driving the real loop with real sleeps.
/// </summary>
public class PollIntervalTests
{
    [Fact]
    public void ComputeNextPollDelay_NoTriggers_ReturnsMaxPollInterval()
    {
        var scheduler = new CronexScheduler(new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        scheduler.ComputeNextPollDelay().ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ComputeNextPollDelay_TriggerFarInFuture_ClampsToMaxPollInterval()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var scheduler = new CronexScheduler(tp);
        scheduler.Register("test", "0 9 * * *", (ctx, ct) => Task.CompletedTask); // hours away

        scheduler.ComputeNextPollDelay().ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ComputeNextPollDelay_OverdueTrigger_ClampsToMinPollInterval()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var scheduler = new CronexScheduler(tp);
        scheduler.Register("test", "* * * * *", (ctx, ct) => Task.CompletedTask);

        // Already past due — a naive `dueTime - now` would be negative.
        tp.Advance(TimeSpan.FromMinutes(5));

        scheduler.ComputeNextPollDelay().ShouldBe(TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public void ComputeNextPollDelay_TriggerImminentlyDue_ReturnsExactRemainingTime()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var scheduler = new CronexScheduler(tp);
        scheduler.Register("test", "* * * * *", (ctx, ct) => Task.CompletedTask); // due at t+60s

        tp.Advance(TimeSpan.FromSeconds(60) - TimeSpan.FromMilliseconds(200));

        scheduler.ComputeNextPollDelay().ShouldBe(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void ComputeNextPollDelay_PicksEarliestAmongMultipleTriggers()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var scheduler = new CronexScheduler(tp);
        scheduler.Register("far", "0 9 * * *", (ctx, ct) => Task.CompletedTask);
        scheduler.Register("near", "* * * * * *", (ctx, ct) => Task.CompletedTask); // 6-field: next second

        scheduler.ComputeNextPollDelay().ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(1));
        scheduler.ComputeNextPollDelay().ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void ComputeNextPollDelay_IncludesStaggerInEffectiveTime()
    {
        // FNV-1a32("test") % 10000 = 3445ms (same golden-value approach as StaggerHashTests).
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var scheduler = new CronexScheduler(tp);
        scheduler.Register("test", "* * * * * {stagger:10s}", (ctx, ct) => Task.CompletedTask);

        // Nominal due time is t+60s; effective time is t+60s+3445ms. Advance to 300ms before the
        // effective time (well past nominal) — if stagger were ignored, the trigger would already
        // look overdue and the delay would clamp to the 10ms floor instead of the exact 300ms left.
        tp.Advance(TimeSpan.FromSeconds(60) + TimeSpan.FromMilliseconds(3445 - 300));

        scheduler.ComputeNextPollDelay().ShouldBe(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task PollLoop_PerSecondTrigger_NoDriftAcrossManyOccurrences()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tp = new FakeTimeProvider(start);
        await using var scheduler = new CronexScheduler(tp);

        var fireTimes = new List<DateTimeOffset>();
        scheduler.Register("test", "* * * * * *", (ctx, ct) => // 6-field: every second
        {
            fireTimes.Add(ctx.ActualTime);
            return Task.CompletedTask;
        });

        // Drive the same poll/advance/tick cadence TickLoopAsync would, without a real timer.
        for (var i = 0; i < 500 && fireTimes.Count < 100; i++)
        {
            tp.Advance(scheduler.ComputeNextPollDelay());
            await scheduler.TickAsync(TestContext.Current.CancellationToken);
        }

        fireTimes.Count.ShouldBe(100);
        for (var i = 0; i < fireTimes.Count; i++)
            fireTimes[i].ShouldBe(start.AddSeconds(i + 1)); // exact — no compounding lateness
    }

    [Fact]
    public async Task PollLoop_CatchesSubSecondWindow_ThatAFixedOneSecondPollWouldMiss()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tp = new FakeTimeProvider(start);
        await using var scheduler = new CronexScheduler(tp);

        var fired = false;
        scheduler.Register("test", "* * * * * * {window:200ms}", (ctx, ct) => // 6-field: every second
        {
            fired = true;
            return Task.CompletedTask;
        });

        // Drive via the computed poll delay (which can go as low as 10ms) instead of a fixed 1s
        // step — a fixed 1s poll would blow straight past this 200ms window every time.
        for (var i = 0; i < 5; i++)
        {
            tp.Advance(scheduler.ComputeNextPollDelay());
            await scheduler.TickAsync(TestContext.Current.CancellationToken);
        }

        fired.ShouldBeTrue();
    }
}
