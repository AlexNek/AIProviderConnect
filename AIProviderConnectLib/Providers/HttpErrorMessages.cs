namespace AIProviderConnect.Providers;

/// <summary>
/// Maps HTTP status codes to user-friendly error messages for AI provider responses.
/// </summary>
internal static class HttpErrorMessages
{
    internal static string GetStatusMessage(int statusCode)
    {
        return statusCode switch
        {
            400 =>
                "The request was rejected — check that the configured model name and base URL are correct.",
            401 => "Authentication failed. Check that your API key is correct.",
            402 => "Payment required. The provider requires billing to be set up before use.",
            403 => "Access denied. Your API key may lack permissions for the requested model.",
            404 =>
                "Endpoint not found. Check that the configured base URL and model name are correct.",
            405 =>
                "The provider rejected the request method. Try a different provider or model.",
            408 => "Request timed out. Check your network connection to the provider.",
            409 => "Conflict — the request conflicts with the provider's current state.",
            415 => "Unsupported request format. The selected AI model may not be compatible.",
            422 => "Invalid request data. Check the configured request values.",
            429 =>
                "Too many requests — the model is rate-limited. Wait a moment and try again, or switch to another model.",
            502 or 503 =>
                "The model is temporarily unavailable (high demand or server error). Wait a moment and try again, or switch to another model.",
            504 =>
                "The model did not respond in time (gateway timeout). Wait a moment and try again, or switch to another model.",
            >= 500 and < 600 =>
                "The provider's server encountered an error. Try again, or switch to another model.",
            _ => $"Unexpected error (code {statusCode}). Check the provider configuration."
        };
    }
}
