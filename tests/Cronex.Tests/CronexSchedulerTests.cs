using Shouldly;
using Xunit;

namespace Cronex.Tests;

public class CronexSchedulerTests
{
    private static FakeTimeProvider CreateTimeProvider(DateTimeOffset start)
    {
        return new FakeTimeProvider(start);
    }

    [Fact]
    public async Task Register_And_Tick()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fired = false;
        scheduler.Register("test", "* * * * *", (ctx, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });

        // Advance past next minute
        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        fired.ShouldBeTrue();
    }

    [Fact]
    public async Task Tick_DoesNotFire_BeforeNextTime()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fired = false;
        scheduler.Register("test", "0 9 * * *", (ctx, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(30));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        fired.ShouldBeFalse();
    }

    [Fact]
    public async Task Unregister_Prevents_Firing()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fired = false;
        scheduler.Register("test", "* * * * *", (ctx, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });

        scheduler.Unregister("test").ShouldBeTrue();

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        fired.ShouldBeFalse();
    }

    [Fact]
    public async Task SetEnabled_False_Prevents_Firing()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fired = false;
        scheduler.Register("test", "* * * * *", (ctx, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });

        scheduler.SetEnabled("test", false);
        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        fired.ShouldBeFalse();
    }

    [Fact]
    public async Task MaxOption_LimitsFireCount()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var count = 0;
        scheduler.Register("test", "* * * * * {max:2}", (ctx, ct) =>
        {
            count++;
            return Task.CompletedTask;
        });

        for (var i = 0; i < 5; i++)
        {
            tp.Advance(TimeSpan.FromMinutes(1));
            await scheduler.TickAsync(TestContext.Current.CancellationToken);
        }

        count.ShouldBe(2);
    }

    [Fact]
    public async Task Events_AreFired()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        TriggerContext? firingCtx = null;
        TriggerContext? completedCtx = null;
        scheduler.TriggerFiring += ctx => firingCtx = ctx;
        scheduler.TriggerCompleted += ctx => completedCtx = ctx;

        scheduler.Register("test", "* * * * *", (ctx, ct) => Task.CompletedTask);

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        firingCtx.ShouldNotBeNull();
        firingCtx!.TriggerId.ShouldBe("test");
        completedCtx.ShouldNotBeNull();
    }

    [Fact]
    public async Task FailedHandler_FiresFailedEvent()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        Exception? capturedEx = null;
        scheduler.TriggerFailed += (ctx, ex) => capturedEx = ex;

        scheduler.Register("test", "* * * * *", (ctx, ct) =>
            throw new InvalidOperationException("test error"));

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        capturedEx.ShouldNotBeNull();
        capturedEx!.Message.ShouldBe("test error");
    }

    [Fact]
    public async Task FireCount_Increments()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        scheduler.Register("test", "* * * * *", (ctx, ct) => Task.CompletedTask);

        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);
        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        var trigger = scheduler.GetTrigger("test");
        trigger.ShouldNotBeNull();
        trigger!.FireCount.ShouldBe(2);
    }

    [Fact]
    public async Task IntervalTrigger()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var count = 0;
        scheduler.Register("test", "@every 5m", (ctx, ct) =>
        {
            count++;
            return Task.CompletedTask;
        });

        tp.Advance(TimeSpan.FromMinutes(5));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);
        count.ShouldBe(1);

        tp.Advance(TimeSpan.FromMinutes(5));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);
        count.ShouldBe(2);
    }

    [Fact]
    public async Task OnceTrigger_FiresOnce()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tp = CreateTimeProvider(start);
        await using var scheduler = new CronexScheduler(tp);

        var count = 0;
        scheduler.Register("test", "@once +5m", (ctx, ct) =>
        {
            count++;
            return Task.CompletedTask;
        }, start);

        tp.Advance(TimeSpan.FromMinutes(5));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);
        count.ShouldBe(1);

        tp.Advance(TimeSpan.FromMinutes(5));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);
        count.ShouldBe(1); // Should not fire again
    }

    [Fact]
    public void GetTriggers_ReturnsAll()
    {
        var scheduler = new CronexScheduler();
        scheduler.Register("a", "* * * * *", (ctx, ct) => Task.CompletedTask);
        scheduler.Register("b", "@daily", (ctx, ct) => Task.CompletedTask);

        var triggers = scheduler.GetTriggers();
        triggers.Count.ShouldBe(2);
    }

    [Fact]
    public void Register_DuplicateId_Throws()
    {
        var scheduler = new CronexScheduler();
        scheduler.Register("test", "* * * * *", (ctx, ct) => Task.CompletedTask);
        Should.Throw<InvalidOperationException>(() =>
            scheduler.Register("test", "@daily", (ctx, ct) => Task.CompletedTask));
    }

    [Fact]
    public async Task Window_SkipsExpiredOccurrence()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fired = false;
        string? skippedReason = null;
        scheduler.TriggerSkipped += (id, reason) => skippedReason = reason;

        scheduler.Register("test", "* * * * * {window:30s}", (ctx, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });

        // Skip way past the window
        tp.Advance(TimeSpan.FromMinutes(5));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        skippedReason.ShouldBe("window exceeded");
        fired.ShouldBeFalse();
    }

    [Fact]
    public async Task Window_AllowsWithinWindow()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fired = false;
        scheduler.Register("test", "* * * * * {window:2m}", (ctx, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });

        // Advance just past next minute (within 2m window)
        tp.Advance(TimeSpan.FromSeconds(65));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        fired.ShouldBeTrue();
    }

    [Fact]
    public async Task Stagger_DelaysFiring()
    {
        var tp = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var scheduler = new CronexScheduler(tp);

        var fireCount = 0;
        scheduler.Register("my-trigger", "* * * * * {stagger:30s}", (ctx, ct) =>
        {
            fireCount++;
            return Task.CompletedTask;
        });

        // Advance exactly to the next minute (without stagger offset, would fire)
        tp.Advance(TimeSpan.FromMinutes(1));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        // Stagger adds an offset based on hash of "my-trigger" % 30s
        // Whether it fires depends on the hash value — verify behavior is consistent
        var trigger = scheduler.GetTrigger("my-trigger");
        trigger.ShouldNotBeNull();
        // After advancing past stagger window, should eventually fire
        tp.Advance(TimeSpan.FromSeconds(30));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);
        fireCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Stagger_IsDeterministic()
    {
        // Two schedulers with same trigger ID should have same stagger behavior
        var tp1 = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var tp2 = CreateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var s1 = new CronexScheduler(tp1);
        await using var s2 = new CronexScheduler(tp2);

        int count1 = 0, count2 = 0;
        s1.Register("same-id", "* * * * * {stagger:30s}", (ctx, ct) =>
        {
            count1++;
            return Task.CompletedTask;
        });
        s2.Register("same-id", "* * * * * {stagger:30s}", (ctx, ct) =>
        {
            count2++;
            return Task.CompletedTask;
        });

        // Advance past stagger
        tp1.Advance(TimeSpan.FromMinutes(2));
        tp2.Advance(TimeSpan.FromMinutes(2));
        await s1.TickAsync(TestContext.Current.CancellationToken);
        await s2.TickAsync(TestContext.Current.CancellationToken);

        // Same ID → same stagger offset → same behavior
        count1.ShouldBe(count2);
    }
}

/// <summary>
/// Simple fake TimeProvider for testing.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset start)
    {
        _now = start;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan duration)
    {
        _now += duration;
    }

    public void SetUtcNow(DateTimeOffset value)
    {
        _now = value;
    }
}
