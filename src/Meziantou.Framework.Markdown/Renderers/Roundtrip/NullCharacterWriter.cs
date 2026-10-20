using System.IO;
using System.Text;
using Meziantou.Framework.Markdown.Helpers;

namespace Meziantou.Framework.Markdown.Renderers.Roundtrip;

/// <summary>
/// Writes a document parsed from a text with NUL characters, which the parser replaces with U+FFFD (CommonMark 2.3, Insecure
/// characters). As long as the text written is the parsed text, a U+FFFD written where the text has a NUL is written as a
/// NUL. Once the text written differs from it (the document was changed), the rest is written as is, so that a U+FFFD never
/// becomes a NUL at a position that does not come from the text.
/// </summary>
internal sealed class NullCharacterWriter : TextWriter
{
    private readonly string _text;

    // The position in the text of the next character written, or -1 once the text written differs from it
    private int _position;

    public NullCharacterWriter(TextWriter innerWriter, string text)
        : base(innerWriter.FormatProvider)
    {
        InnerWriter = innerWriter;
        _text = text;
        NewLine = innerWriter.NewLine;
    }

    public TextWriter InnerWriter { get; }

    public override Encoding Encoding => InnerWriter.Encoding;

    public override void Write(char value) => Write(new ReadOnlySpan<char>(in value));

    public override void Write(char[] buffer, int index, int count) => Write(buffer.AsSpan(index, count));

    public override void Write(string? value) => Write(value.AsSpan());

    public override void Write(ReadOnlySpan<char> buffer)
    {
        while (_position >= 0 && !buffer.IsEmpty)
        {
            var expected = _text.AsSpan(_position, Math.Min(buffer.Length, _text.Length - _position));
            var length = buffer.CommonPrefixLength(expected);
            InnerWriter.Write(buffer[..length]);
            _position += length;
            buffer = buffer[length..];
            if (buffer.IsEmpty)
                return;

            if (length < expected.Length && buffer[0] is CharHelper.ReplacementChar && expected[length] is '\0')
            {
                InnerWriter.Write('\0');
                _position++;
                buffer = buffer[1..];
            }
            else
            {
                _position = -1;
            }
        }

        InnerWriter.Write(buffer);
    }

    public override void Flush() => InnerWriter.Flush();
}
