using WebTools.NET.ContentAnalysis.Models;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Quality;

/// <summary>
/// Default implementation of <see cref="ICandidateRegionQualityAssessor"/>.
/// Rejects regions whose combined or average text length indicates UI chrome rather than content.
/// </summary>
public sealed class CandidateRegionQualityAssessor : ICandidateRegionQualityAssessor
{
    private const int MinimumCombinedContentLength = 200;
    private const int MinimumAverageRegionLength = 50;

    /// <summary>
    /// Minimum ratio of word-like characters (letters in sequences of 2+) to total characters.
    /// Decorative Unicode patterns (✳ ⟡ · ◎ ✦) have zero word-like characters;
    /// real text in any script has a high ratio.
    /// </summary>
    private const double MinimumMeaningfulCharRatio = 0.2;

    public bool HasMeaningfulContent(IReadOnlyList<HtmlCandidateRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);

        if (regions.Count == 0)
        {
            return false;
        }

        var combinedLength = 0;
        var meaningfulCharCount = 0;
        foreach (var region in regions)
        {
            var text = region.Text ?? string.Empty;
            combinedLength += text.Length;
            meaningfulCharCount += CountMeaningfulChars(text);
        }

        if (combinedLength < MinimumCombinedContentLength)
        {
            return false;
        }

        var averageLength = combinedLength / regions.Count;
        if (averageLength < MinimumAverageRegionLength)
        {
            return false;
        }

        // Reject decorative symbol noise: real text has a high ratio of letters/digits.
        var meaningfulRatio = (double)meaningfulCharCount / combinedLength;
        return meaningfulRatio >= MinimumMeaningfulCharRatio;
    }

    /// <summary>
    /// Counts characters that belong to word-like sequences (2+ consecutive letters).
    /// Single isolated letters are not counted — they appear in decorative patterns but not in real words.
    /// </summary>
    private static int CountMeaningfulChars(string text)
    {
        var count = 0;
        var letterRun = 0;
        foreach (var c in text)
        {
            if (char.IsLetter(c))
            {
                letterRun++;
            }
            else
            {
                if (letterRun >= 2)
                {
                    count += letterRun;
                }
                letterRun = 0;
            }
        }

        if (letterRun >= 2)
        {
            count += letterRun;
        }

        return count;
    }
}
