using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// Regression tests for the scheduler-engine-reliability bundle: event-subscriber exception
/// isolation, TriggerCompleted/TriggerFailed misattribution, atomic reentrant claim, and
/// disabled-trigger skip spam.
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
}
