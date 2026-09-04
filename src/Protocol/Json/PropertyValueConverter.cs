using System.Text.Json;
using System.Text.Json.Serialization;
using TabletopCore.Engine;

namespace TabletopCore.Protocol.Json;

/// <summary>
/// PropertyValue is a tagged union (None/Int/String/Bool); on the wire each
/// kind maps to the native JSON type — 7, "skip", true, null — so a property
/// bag serializes as {"color":"red","number":7} and a JS client reads it with
/// no decoding layer. JSON's types are the union's types, which is exactly
/// why PropertyValue supports only these kinds.
/// </summary>
internal sealed class PropertyValueConverter : JsonConverter<PropertyValue>
{
    public override PropertyValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Number => reader.TryGetInt32(out int i)
                ? PropertyValue.Of(i)
                : throw new JsonException("Property numbers must be 32-bit integers."),
            JsonTokenType.String => PropertyValue.Of(reader.GetString()!),
            JsonTokenType.True => PropertyValue.Of(true),
            JsonTokenType.False => PropertyValue.Of(false),
            JsonTokenType.Null => PropertyValue.None,
            var other => throw new JsonException($"Token '{other}' is not a valid property value."),
        };

    public override void Write(Utf8JsonWriter writer, PropertyValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case PropertyValue.ValueKind.Int:
                writer.WriteNumberValue(value.AsInt);
                break;
            case PropertyValue.ValueKind.String:
                writer.WriteStringValue(value.AsString);
                break;
            case PropertyValue.ValueKind.Bool:
                writer.WriteBooleanValue(value.AsBool);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
