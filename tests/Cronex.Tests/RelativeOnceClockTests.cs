using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// A relative <c>@once +Nm</c> is resolved against the scheduler's <see cref="TimeProvider"/>, not the wall clock,
/// so a scheduler driven by a fake clock fires it after the fake clock has moved N.
/// </summary>
public class RelativeOnceClockTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Register_RelativeOnce_UsesTheInjectedClock()
    {
        var tp = new FakeTimeProvider(Start);
        await using var scheduler = new CronexScheduler(tp);
        var fired = 0;

        var registration = scheduler.Register("once", "@once +20m", (ctx, ct) =>
        {
            fired++;
            return Task.CompletedTask;
        });

        registration.NextFireTime.ShouldBe(Start.AddMinutes(20));
        tp.Advance(TimeSpan.FromMinutes(30));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        fired.ShouldBe(1);
    }

    [Fact]
    public async Task Register_TriggerDefinition_RelativeOnce_UsesTheInjectedClock()
    {
        var tp = new FakeTimeProvider(Start);
        await using var scheduler = new CronexScheduler(tp);
        var fired = 0;

        var registration = scheduler.Register(
            new TriggerDefinition { Id = "once", Expression = "@once +20m" },
            (ctx, ct) =>
            {
                fired++;
                return Task.CompletedTask;
            });

        registration.NextFireTime.ShouldBe(Start.AddMinutes(20));
        tp.Advance(TimeSpan.FromMinutes(30));
        await scheduler.TickAsync(TestContext.Current.CancellationToken);

        fired.ShouldBe(1);
    }

    [Fact]
    public async Task Update_RelativeOnce_UsesTheInjectedClock()
    {
        var tp = new FakeTimeProvider(Start);
        await using var scheduler = new CronexScheduler(tp);
        scheduler.Register("once", "@every 1h", (ctx, ct) => Task.CompletedTask);
        tp.Advance(TimeSpan.FromMinutes(10));

        var updated = scheduler.Update("once", "@once +20m");

        updated.NextFireTime.ShouldBe(Start.AddMinutes(30));
    }

    [Fact]
    public async Task UpdateWithHandler_RelativeOnce_UsesTheInjectedClock()
    {
        var tp = new FakeTimeProvider(Start);
        await using var scheduler = new CronexScheduler(tp);
        scheduler.Register("once", "@every 1h", (ctx, ct) => Task.CompletedTask);
        tp.Advance(TimeSpan.FromMinutes(10));

        var updated = scheduler.Update("once", "@once +20m", (ctx, ct) => Task.CompletedTask);

        updated.NextFireTime.ShouldBe(Start.AddMinutes(30));
    }

    [Fact]
    public async Task Register_ExplicitReferenceTime_StillWinsOverTheClock()
    {
        var tp = new FakeTimeProvider(Start);
        await using var scheduler = new CronexScheduler(tp);
        var reference = Start.AddHours(5);

        var registration = scheduler.Register("once", "@once +20m", (ctx, ct) => Task.CompletedTask, reference);

        registration.NextFireTime.ShouldBe(reference.AddMinutes(20));
    }
}
