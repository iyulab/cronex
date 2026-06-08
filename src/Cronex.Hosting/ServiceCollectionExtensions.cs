using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Cronex.Hosting;

/// <summary>
/// Extension methods for registering Cronex services with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Cronex scheduler services to the specified <see cref="IServiceCollection"/>.
    /// Registers CronexScheduler as a singleton and starts a BackgroundService.
    /// </summary>
    public static IServiceCollection AddCronex(
        this IServiceCollection services,
        Action<CronexBuilder>? configure = null)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton(sp => new CronexScheduler(sp.GetService<TimeProvider>()));
        services.AddHostedService<CronexBackgroundService>();

        if (configure != null)
        {
            var builder = new CronexBuilder(services);
            configure(builder);
        }

        return services;
    }
}

/// <summary>
/// Builder for configuring Cronex scheduler within the DI container.
/// </summary>
public sealed class CronexBuilder
{
    private readonly IServiceCollection _services;

    internal CronexBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Registers a trigger with a handler type resolved via DI.
    /// </summary>
    /// <typeparam name="THandler">The handler type implementing <see cref="ICronexHandler"/>.</typeparam>
    /// <param name="id">Unique trigger identifier.</param>
    /// <param name="expression">The Cronex expression string.</param>
    /// <returns>The builder for chaining.</returns>
    public CronexBuilder AddTrigger<THandler>(string id, string expression)
        where THandler : class, ICronexHandler
    {
        _services.TryAddTransient<THandler>();
        _services.AddSingleton(new TriggerDescriptor(id, expression, typeof(THandler)));
        return this;
    }

    /// <summary>
    /// Registers a trigger with a handler type and a TriggerDefinition.
    /// </summary>
    /// <typeparam name="THandler">The handler type implementing <see cref="ICronexHandler"/>.</typeparam>
    /// <param name="definition">The trigger definition (with metadata).</param>
    /// <returns>The builder for chaining.</returns>
    public CronexBuilder AddTrigger<THandler>(TriggerDefinition definition)
        where THandler : class, ICronexHandler
    {
        _services.TryAddTransient<THandler>();
        _services.AddSingleton(new TriggerDescriptor(definition.Id, definition.Expression,
            typeof(THandler), definition.Enabled, definition.Metadata));
        return this;
    }

    /// <summary>
    /// Registers a trigger with an inline handler delegate.
    /// </summary>
    public CronexBuilder AddTrigger(string id, string expression,
        Func<TriggerContext, CancellationToken, Task> handler)
    {
        _services.AddSingleton(new TriggerDescriptor(id, expression, handler));
        return this;
    }
}

/// <summary>
/// Internal descriptor for a trigger registration. Used to defer registration until the scheduler starts.
/// </summary>
internal sealed class TriggerDescriptor
{
    public string Id { get; }
    public string Expression { get; }
    public Type? HandlerType { get; }
    public Func<TriggerContext, CancellationToken, Task>? InlineHandler { get; }
    public bool Enabled { get; }
    public Dictionary<string, string>? Metadata { get; }

    public TriggerDescriptor(string id, string expression, Type handlerType,
        bool enabled = true, Dictionary<string, string>? metadata = null)
    {
        Id = id;
        Expression = expression;
        HandlerType = handlerType;
        Enabled = enabled;
        Metadata = metadata;
    }

    public TriggerDescriptor(string id, string expression,
        Func<TriggerContext, CancellationToken, Task> inlineHandler)
    {
        Id = id;
        Expression = expression;
        InlineHandler = inlineHandler;
        Enabled = true;
    }
}
