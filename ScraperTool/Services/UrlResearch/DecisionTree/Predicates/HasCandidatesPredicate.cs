using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Predicates;

/// <summary>
/// Evaluates to true when the candidate queue in the data store is non-empty.
/// </summary>
public sealed class HasCandidatesPredicate : IDecisionPredicate
{
    private readonly ICandidateUrlProvider _candidateUrlProvider;

    public string Key => "hasCandidates";

    public HasCandidatesPredicate(ICandidateUrlProvider candidateUrlProvider)
    {
        _candidateUrlProvider = candidateUrlProvider ?? throw new ArgumentNullException(nameof(candidateUrlProvider));
    }

    public bool Evaluate(DecisionPredicateContext context)
    {
        return _candidateUrlProvider.GetCandidateUrls(context.Data).Count > 0;
    }
}
