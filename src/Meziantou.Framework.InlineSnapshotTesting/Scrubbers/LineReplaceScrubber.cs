namespace Meziantou.Framework.InlineSnapshotTesting;

internal sealed class LineReplaceScrubber : LineScrubber
{
    private readonly Func<string, string?> _predicate;

    public LineReplaceScrubber(Func<string, string?> predicate)
    {
        _predicate = predicate;
    }

    protected override string? ScrubLine(string line)
    {
        return _predicate(line);
    }
}
