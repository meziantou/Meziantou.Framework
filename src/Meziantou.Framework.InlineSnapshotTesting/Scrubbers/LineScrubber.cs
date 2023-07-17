using System.Text;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.InlineSnapshotTesting;

internal abstract class LineScrubber : Scrubber
{
    public sealed override string Scrub(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var (line, eol) in StringUtils.EnumerateLines(text))
        {
            var newLine = ScrubLine(line);
            if (newLine != null)
            {
                sb.Append(newLine);
                sb.Append(eol);
            }
        }

        return sb.ToString();
    }

    protected virtual string? ScrubLine(ReadOnlySpan<char> line) => ScrubLine(line.ToString());
    protected virtual string? ScrubLine(string line) => line;
}
