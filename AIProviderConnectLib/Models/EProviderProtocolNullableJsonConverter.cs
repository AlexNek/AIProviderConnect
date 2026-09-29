using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIProviderConnect.Models;

/// <summary>
/// JSON converter that maps a nullable provider protocol string (e.g. "openaicompatible")
/// to a nullable <see cref="EProviderProtocol"/> during deserialization, mapping a <c>null</c> token
/// to <c>null</c> and delegating every other token to <see cref="ProviderProtocolMapper"/>.
/// </summary>
public sealed class EProviderProtocolNullableJsonConverter : JsonConverter<EProviderProtocol?>
{
    public override EProviderProtocol? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var value = reader.GetString();
        if (value is null)
        {
            return null;
        }

        return ProviderProtocolMapper.FromJson(value);
    }

    public override void Write(
        Utf8JsonWriter writer,
        EProviderProtocol? value,
        JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(ProviderProtocolMapper.ToJson(value.Value));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
