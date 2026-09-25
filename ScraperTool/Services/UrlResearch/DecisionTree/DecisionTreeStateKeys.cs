namespace ScraperTool.Services.UrlResearch.DecisionTree;

/// <summary>
/// Centralizes the state-property keys used by decision-tree actions and predicates
/// to communicate through <see cref="AiCleverness.Models.DecisionTree.DecisionState"/>.
/// Avoids magic-string duplication across action classes.
/// </summary>
internal static class DecisionTreeStateKeys
{
    /// <summary>The URL of the last page fetched by a candidate-fetch action.</summary>
    public const string LastFetchedUrl = "lastFetchedUrl";

    /// <summary>The HTTP status code from the last probe or fetch.</summary>
    public const string LastHttpStatus = "lastHttpStatus";

    /// <summary>The version-prefixed base URL recorded when the probe confirms a working API.</summary>
    public const string VerifiedWinnerUrl = "verifiedWinnerUrl";

    /// <summary>Total number of models found by a probe or extraction action.</summary>
    public const string ModelCount = "modelCount";

    /// <summary>Comma-separated sample of model IDs found by a probe.</summary>
    public const string ModelIds = "modelIds";

    /// <summary>Set to true when the probe confirms a real API that requires authentication.</summary>
    public const string IsAuthGated = "isAuthGated";
}
