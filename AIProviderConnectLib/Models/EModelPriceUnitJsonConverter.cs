using System.Text.Json.Serialization;

namespace AIProviderConnect.Models;

/// <summary>
/// JSON converter for <see cref="EModelPriceUnit"/> that serializes as string
/// and rejects integer values.
/// </summary>
public sealed class EModelPriceUnitJsonConverter : JsonStringEnumConverter<EModelPriceUnit>
{
    public EModelPriceUnitJsonConverter() : base(namingPolicy: null, allowIntegerValues: false)
    {
    }
}
