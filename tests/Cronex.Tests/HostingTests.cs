using Cronex.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Cronex.Tests;

/// <summary>
/// T-1: Cronex.Hosting tests — DI registration and metadata flow.
/// </summary>
public class HostingTests
{
    [Fact]
    public void AddCronex_RegistersSchedulerAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddCronex();
        var sp = services.BuildServiceProvider();

        var scheduler1 = sp.GetRequiredService<CronexScheduler>();
        var scheduler2 = sp.GetRequiredService<CronexScheduler>();

        scheduler1.ShouldNotBeNull();
        scheduler1.ShouldBeSameAs(scheduler2); // Singleton
    }

    [Fact]
    public void AddCronex_WithConfigure_RegistersTriggers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCronex(c =>
        {
            c.AddTrigger<TestHandler>("test-1", "* * * * *");
        });

        var sp = services.BuildServiceProvider();
        var scheduler = sp.GetRequiredService<CronexScheduler>();
        scheduler.ShouldNotBeNull();
    }

    [Fact]
    public void AddTrigger_TypedHandler_RegistersInDI()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCronex(c =>
        {
            c.AddTrigger<TestHandler>("typed-test", "@every 1m");
        });

        var sp = services.BuildServiceProvider();
        var handler = sp.GetService<TestHandler>();
        handler.ShouldNotBeNull();
    }

    [Fact]
    public void AddTrigger_WithDefinition_PreservesMetadata()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCronex(c =>
        {
            c.AddTrigger<TestHandler>(new TriggerDefinition
            {
                Id = "meta-test",
                Expression = "@every 1m",
                Metadata = new() { ["key"] = "value", ["env"] = "test" }
            });
        });

        var sp = services.BuildServiceProvider();
        var scheduler = sp.GetRequiredService<CronexScheduler>();
        scheduler.ShouldNotBeNull();
    }

    [Fact]
    public void AddTrigger_InlineHandler_RegistersDescriptor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCronex(c =>
        {
            c.AddTrigger("inline-test", "0 9 * * *",
                (ctx, ct) => Task.CompletedTask);
        });

        var sp = services.BuildServiceProvider();
        var scheduler = sp.GetRequiredService<CronexScheduler>();
        scheduler.ShouldNotBeNull();
    }

    [Fact]
    public void AddCronex_MultipleTriggers_AllRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCronex(c =>
        {
            c.AddTrigger<TestHandler>("t1", "* * * * *")
             .AddTrigger<TestHandler>("t2", "@daily")
             .AddTrigger("t3", "@every 5m", (ctx, ct) => Task.CompletedTask);
        });

        var sp = services.BuildServiceProvider();
        var scheduler = sp.GetRequiredService<CronexScheduler>();
        scheduler.ShouldNotBeNull();
    }

    [Fact]
    public void AddCronex_NoConfigure_StillRegistersScheduler()
    {
        var services = new ServiceCollection();
        services.AddCronex();
        var sp = services.BuildServiceProvider();

        var scheduler = sp.GetRequiredService<CronexScheduler>();
        scheduler.ShouldNotBeNull();
        scheduler.GetTriggers().Count.ShouldBe(0);
    }

    /// <summary>
    /// Closes the gap that let a real regression through: every test above only checks DI wiring
    /// (registrations exist), never that the hosted service actually runs. Registration used to
    /// happen inside <c>ExecuteAsync</c>, which <c>BackgroundService.StartAsync</c> does not
    /// guarantee has progressed by the time it returns — a caller awaiting <c>StartAsync</c> could
    /// observe zero registered triggers. This asserts the trigger is live the instant
    /// <c>StartAsync</c>'s task completes, with no extra delay.
    /// </summary>
    [Fact]
    public async Task StartAsync_RegistersTriggersAndStartsScheduler_BeforeReturning()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCronex(c => c.AddTrigger<TestHandler>("start-test", "@every 1m"));
        var sp = services.BuildServiceProvider();

        var hosted = sp.GetServices<IHostedService>().Single();
        var scheduler = sp.GetRequiredService<CronexScheduler>();

        await hosted.StartAsync(CancellationToken.None);

        scheduler.GetTriggers().Count.ShouldBe(1);
        scheduler.IsRunning.ShouldBeTrue();

        await hosted.StopAsync(CancellationToken.None);
    }
}

/// <summary>Test handler for DI-based trigger registration tests.</summary>
public class TestHandler : ICronexHandler
{
    public Task HandleAsync(TriggerContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
