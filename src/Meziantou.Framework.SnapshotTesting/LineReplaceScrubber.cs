namespace Meziantou.Framework.SnapshotTesting;

internal sealed class LineReplaceScrubber : LineScrubber
{
    private readonly Func<string, string?> _replaceLine;

    public LineReplaceScrubber(Func<string, string?> replaceLine)
    {
        _replaceLine = replaceLine;
    }

    protected override string? ScrubLine(string line)
    {
        return _replaceLine(line);
    }
}
