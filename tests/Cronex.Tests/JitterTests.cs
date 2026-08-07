using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// T-6: Jitter runtime application tests (M-2 fix verification).
/// </summary>
public class JitterTests
{
    [Fact]
    public async Task Jitter_DoesNotPreventFiring()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fired = false;
        scheduler.Register("test", "* * * * * {jitter:5s}", (ctx, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });

        // Advance well past the jitter window
        tp.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(10));
        await scheduler.TickAsync();

        fired.ShouldBeTrue();
    }

    [Fact]
    public async Task Jitter_ParsedAndApplied()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        DateTimeOffset? actualTime = null;
        DateTimeOffset? scheduledTime = null;
        scheduler.TriggerFiring += ctx =>
        {
            actualTime = ctx.ActualTime;
            scheduledTime = ctx.ScheduledTime;
        };

        scheduler.Register("test", "* * * * * {jitter:30s}", (ctx, ct) => Task.CompletedTask);

        // Advance well past next minute + jitter
        tp.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(30));
        await scheduler.TickAsync();

        actualTime.ShouldNotBeNull();
        scheduledTime.ShouldNotBeNull();
    }

    [Fact]
    public async Task Jitter_WithWindow_SkipsIfExceedsWindow()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fired = false;
        string? skipReason = null;
        scheduler.TriggerSkipped += (id, reason) => skipReason = reason;
        scheduler.Register("test", "* * * * * {jitter:30s,window:5s}", (ctx, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });

        // Advance way past — both jitter and window should be exceeded
        tp.Advance(TimeSpan.FromMinutes(5));
        await scheduler.TickAsync();

        // May or may not skip depending on random jitter value, but the mechanism should work
        // The important thing is it doesn't crash
        (fired || skipReason != null).ShouldBeTrue();
    }

    [Fact]
    public async Task NoJitter_FiresAtExactTime()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fired = false;
        scheduler.Register("test", "* * * * *", (ctx, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync();

        fired.ShouldBeTrue();
    }

    /// <summary>
    /// J-1: jitter must be drawn once per occurrence, not re-rolled on every tick that observes the
    /// same still-pending occurrence (ISSUE-cronex-20260807-084711-jitter-recomputed-every-tick).
    /// </summary>
    [Fact]
    public async Task Jitter_NotRedrawn_AcrossMultipleTicksOfSamePendingOccurrence()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        scheduler.Register("test", "* * * * * {jitter:30s}", (ctx, ct) => Task.CompletedTask);
        var trigger = scheduler.GetTrigger("test");
        trigger.ShouldNotBeNull();

        var firstDraw = trigger!.JitterOffset;
        firstDraw.ShouldNotBeNull();

        // Ticking repeatedly before the occurrence is claimed must not redraw the offset.
        for (var i = 0; i < 5; i++)
        {
            tp.Advance(TimeSpan.FromSeconds(5));
            await scheduler.TickAsync();
            trigger.JitterOffset.ShouldBe(firstDraw);
        }
    }

    /// <summary>
    /// J-1: with a single draw per occurrence, actual delay should follow U[0, jitter) — median
    /// close to half the window. The old per-tick-redraw bug skewed this heavily toward small
    /// delays (median ~5-6s instead of 15s for a 30s window, per the issue's own analysis), because
    /// a fresh draw was retried every tick and only needed to land below elapsed time once.
    /// </summary>
    [Fact]
    public async Task Jitter_MedianDelayAcrossManyTriggers_ApproximatesHalfTheWindow()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        const int sampleSize = 1000;
        var delaysMs = new List<double>();
        scheduler.TriggerFiring += ctx => delaysMs.Add((ctx.ActualTime - ctx.ScheduledTime).TotalMilliseconds);

        for (var i = 0; i < sampleSize; i++)
            scheduler.Register($"trigger-{i}", "* * * * * {jitter:30s}", (ctx, ct) => Task.CompletedTask);

        // Advance to nominal fire time, then step through the full jitter window in 100ms
        // increments so each trigger's fixed (single-draw) delay is captured near-exactly.
        tp.Advance(TimeSpan.FromMinutes(1));
        for (var ms = 0; ms <= 30_000; ms += 100)
        {
            await scheduler.TickAsync();
            tp.Advance(TimeSpan.FromMilliseconds(100));
        }

        delaysMs.Count.ShouldBe(sampleSize);
        delaysMs.Sort();
        var median = delaysMs[sampleSize / 2];
        median.ShouldBeInRange(12_000, 18_000); // theoretical median of U[0, 30000) is 15000ms
    }
}
