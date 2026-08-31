using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// T-3: Concurrency tests for CronexScheduler (C-2 through C-5 fix verification).
/// </summary>
public class ConcurrencyTests
{
    [Fact]
    public async Task Start_DoubleCall_IsIdempotent()
    {
        await using var scheduler = new CronexScheduler();
        scheduler.Register("test", "* * * * *", (ctx, ct) => Task.CompletedTask);

        // Two Start() calls should not create two tick loops
        scheduler.Start();
        scheduler.Start();

        await scheduler.StopAsync();
    }

    [Fact]
    public async Task Start_ConcurrentCalls_OnlyOneLoopCreated()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fireCount = 0;
        scheduler.Register("test", "* * * * *", (ctx, ct) =>
        {
            Interlocked.Increment(ref fireCount);
            return Task.CompletedTask;
        });

        // Launch multiple Start() calls concurrently
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => scheduler.Start()))
            .ToArray();
        await Task.WhenAll(tasks);

        // Advance and tick — should fire at most once
        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        fireCount.ShouldBeLessThanOrEqualTo(1);
        await scheduler.StopAsync();
    }

    [Fact]
    public async Task Register_AfterStart_IsPickedUp()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        scheduler.Start();

        // Register after Start()
        var fired = false;
        scheduler.Register("late", "* * * * *", (ctx, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        fired.ShouldBeTrue();
        await scheduler.StopAsync();
    }

    [Fact]
    public async Task Unregister_DuringTick_IsSafe()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fireCount = 0;
        scheduler.Register("self-remove", "* * * * *", (ctx, ct) =>
        {
            fireCount++;
            scheduler.Unregister("self-remove");
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(1));
        // Should not throw even though trigger unregisters itself during handler
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        fireCount.ShouldBe(1);
        scheduler.GetTrigger("self-remove").ShouldBeNull();
    }

    [Fact]
    public async Task StopAsync_ThenRestart_Works()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fireCount = 0;
        scheduler.Register("test", "* * * * *", (ctx, ct) =>
        {
            fireCount++;
            return Task.CompletedTask;
        });

        scheduler.Start();
        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);
        await scheduler.StopAsync();

        // Restart
        scheduler.Start();
        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);
        await scheduler.StopAsync();

        fireCount.ShouldBe(2);
    }

    [Fact]
    public async Task DisposeAsync_ThenStart_ThrowsObjectDisposed()
    {
        var scheduler = new CronexScheduler();
        await scheduler.DisposeAsync();

        Should.Throw<ObjectDisposedException>(() => scheduler.Start());
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var scheduler = new CronexScheduler();
        scheduler.Register("test", "* * * * *", (ctx, ct) => Task.CompletedTask);
        scheduler.Start();

        await scheduler.DisposeAsync();
        await scheduler.DisposeAsync(); // Should not throw
    }

    [Fact]
    public async Task FailedHandler_NoSubscriber_DoesNotThrow()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        // No TriggerFailed subscriber — C-4: should not swallow silently,
        // but should NOT crash the scheduler either
        scheduler.Register("test", "* * * * *", (ctx, ct) =>
            throw new InvalidOperationException("boom"));

        tp.Advance(TimeSpan.FromMinutes(1));
        // Should not throw
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        // Trigger should still be scheduled for next occurrence
        var trigger = scheduler.GetTrigger("test");
        trigger.ShouldNotBeNull();
        trigger!.FireCount.ShouldBe(1);
    }

    [Fact]
    public async Task CancellationDuringHandler_DoesNotPropagateToTickAsync_ButRestoresNextFireTime()
    {
        // 0.6.0: TickAsync dispatches the handler without awaiting it (scheduler-engine-reliability
        // item (a)), so by the time the handler observes cancellation, TickAsync's caller has
        // already moved on — there is no one left to rethrow to. The old C-4 contract ("cancellation
        // propagates out of TickAsync") is no longer possible under a non-blocking dispatch model;
        // what still holds is that the occurrence isn't lost — NextFireTime advances once the
        // dispatch settles, observable via WaitForIdleAsync.
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        using var cts = new CancellationTokenSource();
        scheduler.Register("test", "* * * * *", async (ctx, ct) =>
        {
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
        });

        tp.Advance(TimeSpan.FromMinutes(1));

        // Should not throw — the cancellation happens inside the dispatched handler, not inline.
        await scheduler.TickAsync(cts.Token);
        await scheduler.WaitForIdleAsync(TestContext.Current.CancellationToken);

        var trigger = scheduler.GetTrigger("test");
        trigger.ShouldNotBeNull();
        trigger!.NextFireTime.ShouldNotBeNull();
        trigger.NextFireTime.ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 2, 0, TimeSpan.Zero));
    }
}
