using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIProviderConnect.Models;

/// <summary>
/// JSON converter that maps provider protocol strings (e.g. "openaicompatible")
/// to the <see cref="EProviderProtocol"/> enum during deserialization.
/// </summary>
public sealed class EProviderProtocolJsonConverter : JsonConverter<EProviderProtocol>
{
    public override EProviderProtocol Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (value is null)
            throw new JsonException("Protocol value must not be null.");

        return ProviderProtocolMapper.FromJson(value);
    }

    public override void Write(
        Utf8JsonWriter writer,
        EProviderProtocol value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(ProviderProtocolMapper.ToJson(value));
    }
}
