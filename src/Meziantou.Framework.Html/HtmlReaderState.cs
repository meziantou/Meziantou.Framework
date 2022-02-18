#nullable disable
using System.Diagnostics;

namespace Meziantou.Framework.Html;

[DebuggerDisplay("{Line}x{Column}x{Offset} {ParserState} '{RawValue}'")]
public sealed class HtmlReaderState
{
    public HtmlReaderState(HtmlReader reader!!, HtmlParserState rawParserState, string rawValue)
    {
        Reader = reader;
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
    public string RawValue { get; private set; }
    public HtmlParserState RawParserState { get; private set; }

    public HtmlFragmentType FragmentType => (HtmlFragmentType)(int)ParserState;

    public HtmlParserState ParserState
    {
        get
        {
            if (RawParserState == HtmlParserState.TagOpen && RawValue != null && RawValue.StartsWith('/'))
                return HtmlParserState.TagClose;

            return RawParserState;
        }
    }

    public string Value
    {
        get
        {
            if (RawParserState == HtmlParserState.TagOpen && RawValue != null && RawValue.StartsWith('/'))
                return RawValue[1..];

            if (RawValue != null && (RawParserState == HtmlParserState.AttValue || RawParserState == HtmlParserState.AttName) &&
                ((RawValue.StartsWith('\'') && RawValue.EndsWith('\'')) ||
                (RawValue.StartsWith('"') && RawValue.EndsWith('"'))))
            {
                var quote = RawValue[0];
                return RawValue[1..^1].Replace(new string(quote, 2), new string(quote, 1), StringComparison.Ordinal);
            }

            return RawValue;
        }
    }
}
