namespace AIProviderConnect.Constants;

public static class EndpointDefaults
{
    public const string ChatCompletions = "chat/completions";

    public const string Decisions = "alpha/decisions";

    public const string Embeddings = "embeddings";

    public const string Messages = "messages";

    public const string Models = "models";

    public static class KeyQuery
    {
        public const string GenerateContent = "models/{model}:generateContent";

        public const string StreamGenerateContent = "models/{model}:streamGenerateContent";
    }
}
