namespace Meziantou.Framework.InlineSnapshotTesting;

internal sealed class LineReplaceScrubber : LineScrubber
{
    private readonly Func<string, string?> _predicate;

    public LineReplaceScrubber(Func<string, string?> predicate)
    {
        _predicate = predicate;
    }

    protected override bool TryScrubLine(ReadOnlySpan<char> line, out string? newLine)
    {
        newLine = _predicate(line.ToString());
        return newLine is null || !line.SequenceEqual(newLine);
    }
}
