namespace AIProviderConnect.Constants;

/// <summary>
/// Canonical discriminator values for <see cref="Models.ContentPart.Type"/>. Shared by the model
/// default and every wire-protocol mapper so the part-type literals are defined in one place.
/// </summary>
/// <remarks>
/// These constants identify the <em>input</em> part type on the shared model and are used for the
/// <c>switch</c> matching in each mapper. A provider's <em>request-schema</em> output type strings
/// (for example the Anthropic image content-block type) are that provider's own wire contract, so
/// they are intentionally left as literals at the emission site even when the text coincides.
/// </remarks>
public static class ContentPartTypes
{
    public const string Text = "text";

    public const string ImageUrl = "image_url";

    public const string Image = "image";
}
