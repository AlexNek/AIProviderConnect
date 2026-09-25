using System.Text;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Formatting;

/// <summary>
/// Default implementation of <see cref="IStringListFormatter"/>.
/// </summary>
public sealed class StringListFormatter : IStringListFormatter
{
    public string FormatSummary(IReadOnlyList<string> items, int maxItems, int maxItemLength)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (maxItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxItems), "Maximum item count must be greater than zero.");
        }

        if (maxItemLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxItemLength), "Maximum item length must be greater than zero.");
        }

        if (items.Count == 0)
        {
            return "0 items";
        }

        var truncatedItems = items.Select(item => Truncate(item, maxItemLength)).ToList();
        var displayedItems = SelectDisplayedItems(truncatedItems, maxItems);

        var builder = new StringBuilder();
        builder.Append(items.Count).Append(items.Count == 1 ? " item" : " items");

        if (displayedItems.Count > 0)
        {
            builder.Append(": ").Append(string.Join(", ", displayedItems));
            if (items.Count > displayedItems.Count)
            {
                builder.Append(" ...");
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> SelectDisplayedItems(IReadOnlyList<string> items, int maxItems)
    {
        if (items.Count <= maxItems)
        {
            return items;
        }

        var headCount = maxItems / 2;
        var tailCount = maxItems - headCount;
        var result = new List<string>(maxItems);

        for (var index = 0; index < headCount && index < items.Count; index++)
        {
            result.Add(items[index]);
        }

        for (var index = Math.Max(headCount, items.Count - tailCount); index < items.Count; index++)
        {
            result.Add(items[index]);
        }

        return result;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        const string Marker = "...";
        var keepLength = Math.Max(0, maxLength - Marker.Length);
        return keepLength == 0 ? Marker : string.Concat(value.AsSpan(0, keepLength), Marker);
    }
}
