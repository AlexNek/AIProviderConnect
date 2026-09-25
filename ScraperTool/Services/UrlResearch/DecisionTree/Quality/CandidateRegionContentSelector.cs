using System.Text;

using WebTools.NET.ContentAnalysis.Abstractions;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Quality;

/// <summary>
/// Default implementation of <see cref="ICandidateRegionContentSelector"/>.
/// Analyzes HTML for candidate regions, validates their quality, and falls back
/// to full Markdown when regions are low quality or analysis fails.
/// Filters decorative Unicode noise from the output in both paths.
/// </summary>
public sealed class CandidateRegionContentSelector : ICandidateRegionContentSelector
{
    private const int MaxCandidateRegionsForLlm = 3;

    private readonly IHtmlContentAnalyzer _analyzer;
    private readonly ICandidateRegionQualityAssessor _regionQualityAssessor;

    public CandidateRegionContentSelector(
        IHtmlContentAnalyzer analyzer,
        ICandidateRegionQualityAssessor regionQualityAssessor)
    {
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _regionQualityAssessor = regionQualityAssessor ?? throw new ArgumentNullException(nameof(regionQualityAssessor));
    }

    public CandidateRegionContentResult Select(string markdownContent, string? htmlContent, Uri? sourceUri)
    {
        ArgumentNullException.ThrowIfNull(markdownContent);

        var regionCount = 0;

        if (!string.IsNullOrWhiteSpace(htmlContent))
        {
            try
            {
                var analysis = _analyzer.Analyze(htmlContent, sourceUri: sourceUri);
                regionCount = analysis.CandidateRegions.Count;

                var topRegions = analysis.CandidateRegions
                    .Take(MaxCandidateRegionsForLlm)
                    .ToList();

                if (topRegions.Count > 0 && _regionQualityAssessor.HasMeaningfulContent(topRegions))
                {
                    var content = string.Join("\n\n---\n\n", topRegions.Select(r => FilterDecorativeLines(r.Text)));
                    return new CandidateRegionContentResult(content, regionCount);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Analysis failure is non-fatal — fall back to full Markdown.
            }
        }

        return new CandidateRegionContentResult(FilterDecorativeLines(markdownContent), regionCount);
    }

    /// <summary>
    /// Removes lines that consist entirely of decorative Unicode symbols,
    /// spaces, and punctuation — keeping only lines that contain at least one
    /// word-like sequence (2+ consecutive letters).
    /// </summary>
    private static string FilterDecorativeLines(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        var sb = new StringBuilder();
        var lineStart = 0;
        while (lineStart < content.Length)
        {
            var lineEnd = content.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = content.Length;
            }

            if (HasWordLikeContent(content, lineStart, lineEnd))
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }
                sb.Append(content, lineStart, lineEnd - lineStart);
            }

            lineStart = lineEnd + 1;
        }

        return sb.Length > 0 ? sb.ToString() : content;
    }

    private static bool HasWordLikeContent(string text, int start, int end)
    {
        var letterRun = 0;
        for (var i = start; i < end; i++)
        {
            if (char.IsLetter(text[i]))
            {
                letterRun++;
                if (letterRun >= 2)
                {
                    return true;
                }
            }
            else
            {
                letterRun = 0;
            }
        }
        return false;
    }
}
