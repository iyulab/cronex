namespace Cronex.Hosting;

/// <summary>
/// Interface for trigger handlers resolved via DI.
/// Implement this interface and register with AddCronex() to handle trigger events.
/// </summary>
public interface ICronexHandler
{
    /// <summary>
    /// Handles a trigger fire event.
    /// </summary>
    /// <param name="context">The trigger context with schedule and metadata information.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    Task HandleAsync(TriggerContext context, CancellationToken cancellationToken);
}
