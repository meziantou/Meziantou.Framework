namespace Meziantou.Framework.InlineSnapshotTesting;

internal sealed class LineFilterScrubber : LineScrubber
{
    private readonly Func<string, bool> _predicate;

    public LineFilterScrubber(Func<string, bool> predicate)
    {
        _predicate = predicate;
    }

    protected override bool TryScrubLine(ReadOnlySpan<char> line, out string? newLine)
    {
        newLine = null;
        return _predicate(line.ToString());
    }
}
