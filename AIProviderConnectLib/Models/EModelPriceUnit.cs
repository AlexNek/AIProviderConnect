using System.Text.Json.Serialization;

namespace AIProviderConnect.Models;

/// <summary>
/// Unit of measure for model pricing.
/// </summary>
[JsonConverter(typeof(EModelPriceUnitJsonConverter))]
public enum EModelPriceUnit
{
    /// <summary>
    /// Price per one million tokens.
    /// </summary>
    Per1M,

    /// <summary>
    /// Price per one thousand tokens.
    /// </summary>
    Per1K
}
