using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// Runtime expression update preserving FireCount/LastFired
/// (ISSUE-cronex-20260807-084719-runtime-expression-update).
/// </summary>
public class UpdateTests
{
    [Fact]
    public async Task Update_PreservesFireCount_AcrossMaxLimit()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var count = 0;
        scheduler.Register("test", "* * * * * {max:10}", (ctx, ct) =>
        {
            count++;
            return Task.CompletedTask;
        });

        for (var i = 0; i < 7; i++)
        {
            tp.Advance(TimeSpan.FromMinutes(1));
            await scheduler.TickAsync();
        }
        count.ShouldBe(7);

        // Reload with the same schedule + max — Unregister+Register would reset FireCount to 0 and
        // let this fire 10 more times (17 total). Update must not.
        scheduler.Update("test", "* * * * * {max:10}");

        for (var i = 0; i < 10; i++)
        {
            tp.Advance(TimeSpan.FromMinutes(1));
            await scheduler.TickAsync();
        }

        count.ShouldBe(10); // 7 already fired + only 3 more allowed under the shared max
        scheduler.GetTrigger("test")!.FireCount.ShouldBe(10);
    }

    [Fact]
    public async Task Update_PreservesLastFired()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        scheduler.Register("test", "* * * * *", (ctx, ct) => Task.CompletedTask);
        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync();

        var lastFiredBeforeUpdate = scheduler.GetTrigger("test")!.LastFired;
        lastFiredBeforeUpdate.ShouldNotBeNull();

        scheduler.Update("test", "0 0 * * *"); // change to daily-at-midnight

        scheduler.GetTrigger("test")!.LastFired.ShouldBe(lastFiredBeforeUpdate);
    }

    [Fact]
    public async Task Update_RecomputesNextFireTimeFromNewExpression()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        scheduler.Register("test", "0 9 * * *", (ctx, ct) => Task.CompletedTask);
        var originalNext = scheduler.GetTrigger("test")!.NextFireTime;

        scheduler.Update("test", "0 12 * * *"); // 09:00 -> 12:00

        var updatedNext = scheduler.GetTrigger("test")!.NextFireTime;
        updatedNext.ShouldNotBe(originalNext);
        updatedNext!.Value.Hour.ShouldBe(12);
    }

    [Fact]
    public async Task Update_WithHandlerOverload_ReplacesHandler()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var oldHandlerCalled = false;
        var newHandlerCalled = false;
        scheduler.Register("test", "* * * * *", (ctx, ct) =>
        {
            oldHandlerCalled = true;
            return Task.CompletedTask;
        });

        scheduler.Update("test", "* * * * *", (ctx, ct) =>
        {
            newHandlerCalled = true;
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync();

        oldHandlerCalled.ShouldBeFalse();
        newHandlerCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task Update_WithoutHandlerOverload_KeepsOriginalHandler()
    {
        var tp = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var handlerCalled = false;
        scheduler.Register("test", "0 9 * * *", (ctx, ct) =>
        {
            handlerCalled = true;
            return Task.CompletedTask;
        });

        scheduler.Update("test", "* * * * *"); // schedule-only change

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync();

        handlerCalled.ShouldBeTrue();
    }

    [Fact]
    public void Update_UnknownId_ThrowsInvalidOperationException()
    {
        var scheduler = new CronexScheduler();
        Should.Throw<InvalidOperationException>(() => scheduler.Update("nonexistent", "* * * * *"));
    }

    [Fact]
    public void Update_UnknownId_WithHandlerOverload_ThrowsInvalidOperationException()
    {
        var scheduler = new CronexScheduler();
        Should.Throw<InvalidOperationException>(() =>
            scheduler.Update("nonexistent", "* * * * *", (ctx, ct) => Task.CompletedTask));
    }

    [Fact]
    public void Update_ReturnsSameRegistrationInstance()
    {
        var scheduler = new CronexScheduler();
        var original = scheduler.Register("test", "0 9 * * *", (ctx, ct) => Task.CompletedTask);
        var updated = scheduler.Update("test", "0 12 * * *");

        ReferenceEquals(original, updated).ShouldBeTrue();
        ReferenceEquals(scheduler.GetTrigger("test"), original).ShouldBeTrue();
    }
}
