using System.Text.Json.Serialization;

namespace Cronex;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for <see cref="TriggerDefinition"/>. Use
/// this instead of the default reflection-based <c>JsonSerializer.Serialize/Deserialize</c>
/// overloads under Native AOT or trimming, where reflection-based serialization isn't supported:
/// <code>
/// var json = JsonSerializer.Serialize(definition, TriggerDefinitionJsonContext.Default.TriggerDefinition);
/// var definition = JsonSerializer.Deserialize(json, TriggerDefinitionJsonContext.Default.TriggerDefinition);
/// </code>
/// </summary>
[JsonSerializable(typeof(TriggerDefinition))]
public sealed partial class TriggerDefinitionJsonContext : JsonSerializerContext
{
}
