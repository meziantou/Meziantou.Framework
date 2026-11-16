namespace Meziantou.Framework.FileConversionTool;

/// <summary>A set of 1-based page numbers, such as <c>1-3,7,10-</c>.</summary>
internal sealed class PageRange
{
    private readonly (int Start, int End)[] _ranges;

    private PageRange((int Start, int End)[] ranges)
    {
        _ranges = ranges;
    }

    /// <summary>Gets the range that contains every page.</summary>
    public static PageRange All { get; } = new([(1, int.MaxValue)]);

    /// <summary>Parses a comma-separated list of pages (<c>7</c>) and ranges (<c>1-3</c>, <c>10-</c> for the pages from 10 to the end).</summary>
    /// <exception cref="FormatException">The value is not a valid list of pages.</exception>
    public static PageRange Parse(string value)
    {
        var ranges = new List<(int Start, int End)>();
        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries))
        {
            if (part.Length is 0)
                throw new FormatException($"The page range '{value}' contains an empty item");

            var dash = part.IndexOf('-', StringComparison.Ordinal);
            if (dash < 0)
            {
                var page = ParsePage(part, value);
                ranges.Add((page, page));
                continue;
            }

            var start = ParsePage(part[..dash].Trim(), value);
            var endText = part[(dash + 1)..].Trim();
            var end = endText.Length is 0 ? int.MaxValue : ParsePage(endText, value);
            if (end < start)
                throw new FormatException($"The range '{part}' of the page range '{value}' ends before it starts");

            ranges.Add((start, end));
        }

        return new PageRange([.. ranges]);
    }

    public bool Contains(int page)
    {
        foreach (var (start, end) in _ranges)
        {
            if (page >= start && page <= end)
                return true;
        }

        return false;
    }

    /// <summary>Gets the number of pages of the range among <paramref name="pageCount"/> pages.</summary>
    public int Count(int pageCount)
    {
        var count = 0;
        for (var page = 1; page <= pageCount; page++)
        {
            if (Contains(page))
            {
                count++;
            }
        }

        return count;
    }

    private static int ParsePage(string text, string value)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var page) || page < 1)
            throw new FormatException($"'{text}' in the page range '{value}' is not a page number (1 or more)");

        return page;
    }
}
