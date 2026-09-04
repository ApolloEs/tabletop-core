using System.Text.Json;
using System.Text.Json.Serialization;
using TabletopCore.Protocol.Json;

namespace TabletopCore.Protocol;

/// <summary>
/// The one JsonSerializerOptions for everything that crosses the wire —
/// plugged into SignalR on the server, used verbatim by the console client
/// and the tests. There is deliberately no second configuration anywhere:
/// if two sides ever disagree about the wire, it can only be a version skew,
/// never a settings skew.
/// </summary>
public static class WireJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = WireTypeResolver.Create(),
            // JS clients build objects whose key order we shouldn't rely on;
            // accept the "type" discriminator in any position.
            AllowOutOfOrderMetadataProperties = true,
            // Absent-vs-null is not a distinction this protocol uses, so nulls
            // (a hidden zone's cards, an unowned zone's owner) are simply
            // omitted and clients test presence.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new PlayerIdConverter());
        options.Converters.Add(new CardInstanceIdConverter());
        options.Converters.Add(new ZoneIdConverter());
        options.Converters.Add(new CardDefinitionIdConverter());
        options.Converters.Add(new PropertyValueConverter());
        // Every enum as a camelCase string ("red", "ownerOnly", "reversed"):
        // self-describing payloads, and reordering enum members is not a
        // silent wire break the way numeric enums would make it.
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly();
        return options;
    }
}
