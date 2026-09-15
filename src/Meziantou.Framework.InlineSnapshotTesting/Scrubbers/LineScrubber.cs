namespace Meziantou.Framework.InlineSnapshotTesting;

internal abstract class LineScrubber : Scrubber
{
    public sealed override string Scrub(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastEndOfLineLength = 0;
        foreach (var (line, eol) in ScrubberUtilities.EnumerateLines(text))
        {
            var newLine = ScrubLine(line);
            if (newLine is not null)
            {
                sb.Append(newLine);
                sb.Append(eol);
                lastEndOfLineLength = eol.Length;
            }
            else if (eol.IsEmpty)
            {
                // Removing the last line must also remove the line break that separated it from the previous line.
                // Otherwise the text would end with a line break it did not have. A text that ends with a line break
                // has no line without one, so its last line break is kept.
                sb.Length -= lastEndOfLineLength;
            }
        }

        return sb.ToString();
    }

    protected virtual string? ScrubLine(ReadOnlySpan<char> line) => ScrubLine(line.ToString());
    protected virtual string? ScrubLine(string line) => line;
}
