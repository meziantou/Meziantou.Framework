namespace Meziantou.Framework.InlineSnapshotTesting;

internal abstract class LineScrubber : Scrubber
{
    public sealed override string Scrub(string text)
    {
        // The result is built only once a line changes, so a text that no line changes is returned without a copy.
        StringBuilder? sb = null;
        var position = 0;
        var lastEndOfLineLength = 0;
        foreach (var (line, eol) in ScrubberUtilities.EnumerateLines(text))
        {
            var lineStart = position;
            position += line.Length + eol.Length;
            if (!TryScrubLine(line, out var newLine))
            {
                sb?.Append(line).Append(eol);
                lastEndOfLineLength = eol.Length;
                continue;
            }

            sb ??= new StringBuilder(text.Length).Append(text, 0, lineStart);
            if (newLine is not null)
            {
                sb.Append(newLine).Append(eol);
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

        return sb?.ToString() ?? text;
    }

    /// <summary>Returns <see langword="false"/> to keep the line unchanged. Otherwise, <paramref name="newLine"/> replaces the line, or removes it when <see langword="null"/>.</summary>
    protected abstract bool TryScrubLine(ReadOnlySpan<char> line, out string? newLine);
}
