using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// Regression tests for the scheduler-engine-reliability bundle: non-blocking handler dispatch,
/// event-subscriber exception isolation, TriggerCompleted/TriggerFailed misattribution, atomic
/// reentrant claim, and disabled-trigger skip spam.
/// </summary>
public class EngineReliabilityTests
{
    [Fact]
    public async Task ThrowingFiringSubscriber_DoesNotStopOtherTriggersFromFiring()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        scheduler.TriggerFiring += _ => throw new InvalidOperationException("boom from subscriber");

        var otherFired = false;
        scheduler.Register("throws", "* * * * *", (ctx, ct) => Task.CompletedTask);
        scheduler.Register("other", "* * * * *", (ctx, ct) =>
        {
            otherFired = true;
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(1));

        // Should not throw even though a TriggerFiring subscriber does.
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        otherFired.ShouldBeTrue();
    }

    [Fact]
    public async Task ThrowingSkippedSubscriber_DoesNotPreventWindowSkipLogic()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        scheduler.TriggerSkipped += (_, _) => throw new InvalidOperationException("boom from TriggerSkipped");

        var fired = false;
        scheduler.Register("test", "* * * * * {window:30s}", (ctx, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(5));

        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        fired.ShouldBeFalse();
    }

    [Fact]
    public async Task ThrowingTriggerCompletedSubscriber_DoesNotFireTriggerFailed()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var failedFired = false;
        scheduler.TriggerCompleted += _ => throw new InvalidOperationException("boom from TriggerCompleted");
        scheduler.TriggerFailed += (_, _) => failedFired = true;

        // Handler itself succeeds — only the TriggerCompleted subscriber throws.
        scheduler.Register("test", "* * * * *", (ctx, ct) => Task.CompletedTask);

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        failedFired.ShouldBeFalse();
    }

    [Fact]
    public async Task FailingHandler_StillFiresTriggerFailed_NotMisattributedByCompletedSubscriber()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var completedFired = false;
        Exception? capturedEx = null;
        scheduler.TriggerCompleted += _ => completedFired = true;
        scheduler.TriggerFailed += (_, ex) => capturedEx = ex;

        scheduler.Register("test", "* * * * *", (ctx, ct) =>
            throw new InvalidOperationException("handler failed"));

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        completedFired.ShouldBeFalse();
        capturedEx.ShouldNotBeNull();
        capturedEx!.Message.ShouldBe("handler failed");
    }

    [Fact]
    public async Task ConcurrentTickAsync_SameDueTrigger_FiresAtMostOnce()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fireCount = 0;
        scheduler.Register("test", "* * * * *", (ctx, ct) =>
        {
            Interlocked.Increment(ref fireCount);
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(1));

        // Race many concurrent manual ticks against the same due occurrence — before the atomic
        // claim fix, the read-then-null-set gap let more than one of these fire the same occurrence.
        var tasks = Enumerable.Range(0, 20).Select(_ => scheduler.TickAsync()).ToArray();
        await Task.WhenAll(tasks);

        fireCount.ShouldBe(1);
    }

    [Fact]
    public async Task DisabledPastDueTrigger_DoesNotSpamSameBacklogEveryPoll()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var skipCount = 0;
        scheduler.TriggerSkipped += (_, reason) =>
        {
            if (reason == "disabled") skipCount++;
        };

        scheduler.Register("test", "* * * * *", (ctx, ct) => Task.CompletedTask);
        scheduler.SetEnabled("test", false);

        // Fall 5 occurrences behind while disabled, then simulate the automatic loop polling
        // repeatedly without further time advancement — this used to fire TriggerSkipped once per
        // poll (once per second) forever.
        tp.Advance(TimeSpan.FromMinutes(5));
        for (var i = 0; i < 20; i++)
            await scheduler.TickAsync(TestContext.Current.CancellationToken);

        skipCount.ShouldBe(1);
    }

    [Fact]
    public async Task ReEnabling_AfterDisabledBacklog_ResumesFiring()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fired = false;
        scheduler.Register("test", "* * * * *", (ctx, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });
        scheduler.SetEnabled("test", false);

        tp.Advance(TimeSpan.FromMinutes(3));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        scheduler.SetEnabled("test", true);
        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        fired.ShouldBeTrue();
    }

    [Fact]
    public async Task IsRunning_ReflectsStartStopLifecycle()
    {
        await using var scheduler = new CronexScheduler();
        scheduler.IsRunning.ShouldBeFalse();

        scheduler.Start();
        scheduler.IsRunning.ShouldBeTrue();

        await scheduler.StopAsync();
        scheduler.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task SlowHandler_DoesNotDelayOtherTriggersFromFiringOnTime()
    {
        // Item (a) — the reliability bundle's last unresolved defect (0.6.0). Before this fix,
        // TickAsync awaited each trigger's handler in turn: a handler that never completes would
        // have made this `await scheduler.TickAsync(...)` call itself never return, so the second
        // Advance()+TickAsync() below would never run and "fast" would never reach FireCount 2 —
        // this test would hang rather than fail. Under non-blocking dispatch, "slow" not completing
        // has no bearing on "fast" firing on every tick.
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var slowHandlerStarted = new TaskCompletionSource();
        var releaseSlowHandler = new TaskCompletionSource();
        scheduler.Register("slow", "* * * * *", async (ctx, ct) =>
        {
            slowHandlerStarted.SetResult();
            await releaseSlowHandler.Task; // stands in for a still-running 10-minute handler
        });

        var fastFireCount = 0;
        scheduler.Register("fast", "* * * * *", (ctx, ct) =>
        {
            Interlocked.Increment(ref fastFireCount);
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        await slowHandlerStarted.Task; // sanity: "slow" really did start, not skipped
        fastFireCount.ShouldBe(1);

        // "slow" is still awaiting release — "fast" must still fire on its next occurrence.
        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);
        fastFireCount.ShouldBe(2);

        releaseSlowHandler.SetResult();
        await scheduler.WaitForIdleAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WaitForIdleAsync_WaitsForDispatchedHandlerToSettleAndAdvanceNextFireTime()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var completed = false;
        scheduler.TriggerCompleted += _ => completed = true;
        scheduler.Register("test", "* * * * *", async (ctx, ct) =>
        {
            await Task.Yield(); // a real async handoff, unlike this file's usual Task.CompletedTask handlers
        });

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);
        await scheduler.WaitForIdleAsync(TestContext.Current.CancellationToken);

        completed.ShouldBeTrue();
        scheduler.GetTrigger("test")!.NextFireTime.ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 2, 0, TimeSpan.Zero));
    }
}
