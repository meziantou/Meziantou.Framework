using System.IO;
using System.Text;

namespace Meziantou.Framework.Markdown.Extensions.Tables;

/// <summary>
/// A writer that escapes the pipes written to another writer. A pipe in a cell of a GFM table is always escaped in the source, as
/// the cells are split before their content is parsed, and the backslash is removed before the content is parsed.
/// </summary>
internal sealed class PipeEscapingTextWriter(TextWriter writer) : TextWriter
{
    public override Encoding Encoding => writer.Encoding;

    public override void Write(char value)
    {
        if (value == '|')
        {
            writer.Write('\\');
        }

        writer.Write(value);
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        while (!buffer.IsEmpty)
        {
            var index = buffer.IndexOf('|');
            if (index < 0)
            {
                writer.Write(buffer);
                return;
            }

            writer.Write(buffer[..index]);
            writer.Write("\\|");
            buffer = buffer[(index + 1)..];
        }
    }

    public override void Write(char[] buffer, int index, int count) => Write(buffer.AsSpan(index, count));

    public override void Write(string? value) => Write(value.AsSpan());
}
