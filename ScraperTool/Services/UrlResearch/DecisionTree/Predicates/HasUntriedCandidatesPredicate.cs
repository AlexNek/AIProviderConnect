using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Predicates;

/// <summary>
/// Evaluates to true when at least one queued candidate has not been fetched yet in this run.
/// <para>
/// <see cref="HasCandidatesPredicate"/> answers whether the queue holds anything at all, which
/// never becomes false once a scan has filled it: every entry stays in the data store after being
/// tried. A tree that routes on that answer can therefore never take its "no candidates left"
/// branch, so the fallback behind it — re-scan, web search — is unreachable and the run keeps
/// cycling over judged pages until the node budget ends it.
/// </para>
/// <para>
/// Candidates are compared by the page they address, not by the string that queued them, matching
/// how <see cref="Actions.FetchNextCandidateAction"/> steps over pages it already fetched.
/// </para>
/// </summary>
public sealed class HasUntriedCandidatesPredicate : IDecisionPredicate
{
    private readonly ICandidateUrlProvider _candidateUrlProvider;

    public string Key => "hasUntriedCandidates";

    public HasUntriedCandidatesPredicate(ICandidateUrlProvider candidateUrlProvider)
    {
        _candidateUrlProvider = candidateUrlProvider ?? throw new ArgumentNullException(nameof(candidateUrlProvider));
    }

    public bool Evaluate(DecisionPredicateContext context)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        if (context.State.Properties.TryGetValue("visitedUrls", out var visitedObj)
            && visitedObj is string visitedString
            && !string.IsNullOrWhiteSpace(visitedString))
        {
            foreach (var url in visitedString.Split(',', StringSplitOptions.RemoveEmptyEntries))
                visited.Add(CandidateUrlNormalizer.Normalize(url));
        }

        foreach (var candidate in _candidateUrlProvider.GetCandidateUrls(context.Data))
        {
            if (!visited.Contains(CandidateUrlNormalizer.Normalize(candidate)))
                return true;
        }

        return false;
    }
}
