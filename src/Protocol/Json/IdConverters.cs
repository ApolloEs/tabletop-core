using System.Text.Json;
using System.Text.Json.Serialization;
using TabletopCore.Engine;

namespace TabletopCore.Protocol.Json;

// The id record structs wrap a single value for type safety in C#; on the
// wire that wrapper is noise ({"Value":"deck"} instead of "deck"). Each
// converter serializes the bare value. Value-position only — if an id ever
// becomes a JSON dictionary key, extend with Read/WriteAsPropertyName then.

internal sealed class PlayerIdConverter : JsonConverter<PlayerId>
{
    public override PlayerId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetInt32());

    public override void Write(Utf8JsonWriter writer, PlayerId value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value.Value);
}

internal sealed class CardInstanceIdConverter : JsonConverter<CardInstanceId>
{
    public override CardInstanceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetInt32());

    public override void Write(Utf8JsonWriter writer, CardInstanceId value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value.Value);
}

internal sealed class ZoneIdConverter : JsonConverter<ZoneId>
{
    public override ZoneId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? throw new JsonException("Zone id cannot be null."));

    public override void Write(Utf8JsonWriter writer, ZoneId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

internal sealed class CardDefinitionIdConverter : JsonConverter<CardDefinitionId>
{
    public override CardDefinitionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? throw new JsonException("Card definition id cannot be null."));

    public override void Write(Utf8JsonWriter writer, CardDefinitionId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
