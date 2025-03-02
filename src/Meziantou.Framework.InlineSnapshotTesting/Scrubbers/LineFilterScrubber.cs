namespace Meziantou.Framework.InlineSnapshotTesting;

internal sealed class LineFilterScrubber : LineScrubber
{
    private readonly Func<string, bool> _predicate;

    public LineFilterScrubber(Func<string, bool> predicate)
    {
        _predicate = predicate;
    }

    protected override string? ScrubLine(string line)
    {
        if (_predicate(line))
            return null;

        return line;
    }
}
