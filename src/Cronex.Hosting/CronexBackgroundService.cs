using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cronex.Hosting;

/// <summary>
/// BackgroundService that runs the CronexScheduler as a hosted service.
/// Registers triggers from TriggerDescriptors on startup.
/// </summary>
internal sealed class CronexBackgroundService : BackgroundService
{
    private readonly CronexScheduler _scheduler;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEnumerable<TriggerDescriptor> _descriptors;
    private readonly ILogger<CronexBackgroundService> _logger;

    public CronexBackgroundService(
        CronexScheduler scheduler,
        IServiceProvider serviceProvider,
        IEnumerable<TriggerDescriptor> descriptors,
        ILogger<CronexBackgroundService> logger)
    {
        _scheduler = scheduler;
        _serviceProvider = serviceProvider;
        _descriptors = descriptors;
        _logger = logger;
    }

    /// <summary>
    /// Registers triggers and starts the scheduler synchronously, before delegating to
    /// <see cref="BackgroundService"/>'s own <c>StartAsync</c> to kick off the run-until-stopped
    /// loop in <see cref="ExecuteAsync"/>. Doing this here — rather than at the top of
    /// <see cref="ExecuteAsync"/> — matters: <c>BackgroundService.StartAsync</c> does not guarantee
    /// <c>ExecuteAsync</c>'s synchronous prefix has run by the time it returns (it returns
    /// <c>Task.CompletedTask</c> the moment <c>ExecuteAsync</c> hasn't finished, which for an
    /// infinite-running service is immediately). A caller awaiting <c>StartAsync</c> to know
    /// "triggers are registered and the scheduler is running" would otherwise race the actual
    /// registration.
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var desc in _descriptors)
        {
            Func<TriggerContext, CancellationToken, Task> handler;

            if (desc.InlineHandler != null)
            {
                handler = desc.InlineHandler;
            }
            else if (desc.HandlerType != null)
            {
                var handlerType = desc.HandlerType;
                handler = async (ctx, ct) =>
                {
                    using var scope = _serviceProvider.CreateScope();
                    var h = (ICronexHandler)scope.ServiceProvider.GetRequiredService(handlerType);
                    await h.HandleAsync(ctx, ct);
                };
            }
            else
            {
                continue;
            }

            // M-4: Use TriggerDefinition overload to preserve metadata
            var definition = new TriggerDefinition
            {
                Id = desc.Id,
                Expression = desc.Expression,
                Enabled = desc.Enabled,
                Metadata = desc.Metadata
            };
            _scheduler.Register(definition, handler);
            _logger.LogDebug("Registered trigger '{TriggerId}' with expression '{Expression}'", desc.Id, desc.Expression);
        }

        _logger.LogInformation("Cronex scheduler started with {Count} trigger(s)", _scheduler.GetTriggers().Count);
        _scheduler.Start();

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }

        await _scheduler.StopAsync();
        _logger.LogInformation("Cronex scheduler stopped");
    }
}
