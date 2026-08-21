using System.Diagnostics;

namespace Meziantou.Framework.Html;

[DebuggerDisplay("{Line}x{Column}x{Offset} {ParserState} '{RawValue}'")]
#if HTML_PUBLIC
public
#else
internal
#endif
sealed class HtmlReaderState
{
    public HtmlReaderState(HtmlReader reader, HtmlParserState rawParserState, string? rawValue)
    {
        Reader = reader ?? throw new ArgumentNullException(nameof(reader));
        Line = reader.Line;
        Column = reader.Column;
        Offset = reader.Offset;
        RawValue = rawValue;
        RawParserState = rawParserState;
        QuoteChar = reader.QuoteChar;
    }

    public HtmlReader Reader { get; }
    public char QuoteChar { get; private set; }
    public int Offset { get; private set; }
    public int Line { get; private set; }
    public int Column { get; private set; }
    public string? RawValue { get; private set; }
    public HtmlParserState RawParserState { get; private set; }

    public HtmlFragmentType FragmentType => (HtmlFragmentType)(int)ParserState;

    public HtmlParserState ParserState
    {
        get
        {
            if (RawParserState == HtmlParserState.TagOpen && RawValue is not null && RawValue.StartsWith('/', StringComparison.Ordinal))
                return HtmlParserState.TagClose;

            return RawParserState;
        }
    }

    public string? Value
    {
        get
        {
            if (RawParserState == HtmlParserState.TagOpen && RawValue is not null && RawValue.StartsWith('/', StringComparison.Ordinal))
                return RawValue[1..];

            if (RawValue is not null && RawParserState is HtmlParserState.AttValue or HtmlParserState.AttName &&
                RawValue.Length > 0 && HtmlReader.IsAnyQuote(RawValue[0]))
            {
                var quote = RawValue[0];

                // the closing quote is missing when the document ends in the middle of the value
                var end = RawValue.Length > 1 && RawValue[^1] == quote ? RawValue.Length - 1 : RawValue.Length;
                return RawValue[1..end].Replace(new string(quote, 2), new string(quote, 1), StringComparison.Ordinal);
            }

            return RawValue;
        }
    }
}
