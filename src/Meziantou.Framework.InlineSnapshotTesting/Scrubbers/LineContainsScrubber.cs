namespace Meziantou.Framework.InlineSnapshotTesting;

/// <summary>Removes the lines that contain any of the search texts, in a single pass over the text.</summary>
internal sealed class LineContainsScrubber : LineScrubber
{
    private readonly string[] _searchTexts;
    private readonly StringComparison _stringComparison;

    public LineContainsScrubber(string[] searchTexts, StringComparison stringComparison)
    {
        _searchTexts = searchTexts;
        _stringComparison = stringComparison;
    }

    protected override bool TryScrubLine(ReadOnlySpan<char> line, out string? newLine)
    {
        newLine = null;
        foreach (var searchText in _searchTexts)
        {
            if (line.Contains(searchText, _stringComparison))
                return true;
        }

        return false;
    }
}
