using System.Text.Json.Serialization;

namespace Cronex;

/// <summary>
/// A JSON-serializable trigger definition.
/// Separates the trigger spec (expression, metadata) from the runtime handler (delegate).
/// External systems create TriggerDefinitions; consuming apps bind handlers.
/// </summary>
public sealed class TriggerDefinition
{
    /// <summary>Unique trigger identifier.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The Cronex expression string.</summary>
    [JsonPropertyName("expression")]
    public required string Expression { get; init; }

    /// <summary>Whether the trigger is enabled. Defaults to true.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>Free-form key-value metadata passed through to TriggerContext.</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }
}
