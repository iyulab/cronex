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

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Register triggers from descriptors
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
