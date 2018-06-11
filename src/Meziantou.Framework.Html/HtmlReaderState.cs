using System;
using System.Diagnostics;

namespace Meziantou.Framework.Html
{
    [DebuggerDisplay("{Line}x{Column}x{Offset} {ParserState} '{RawValue}'")]
    public class HtmlReaderState
    {
        public HtmlReaderState(HtmlReader reader, HtmlParserState rawParserState, string rawValue)
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
        public virtual char QuoteChar { get; protected set; }
        public virtual int Offset { get; protected set; }
        public virtual int Line { get; protected set; }
        public virtual int Column { get; protected set; }
        public virtual string RawValue { get; protected set; }
        public virtual HtmlParserState RawParserState { get; protected set; }

        public virtual HtmlFragmentType FragmentType
        {
            get
            {
                return (HtmlFragmentType)(int)ParserState;
            }
        }

        public virtual HtmlParserState ParserState
        {
            get
            {
                if (RawParserState == HtmlParserState.TagOpen && RawValue != null && RawValue.StartsWith('/'))
                    return HtmlParserState.TagClose;

                return RawParserState;
            }
        }

        public virtual string Value
        {
            get
            {
                if (RawParserState == HtmlParserState.TagOpen && RawValue != null && RawValue.StartsWith('/'))
                    return RawValue.Substring(1);

                if (RawValue != null && (RawParserState == HtmlParserState.AttValue || RawParserState == HtmlParserState.AttName) &&
                    ((RawValue.StartsWith('\'') && RawValue.EndsWith('\'')) ||
                    (RawValue.StartsWith('"') && RawValue.EndsWith('"'))))
                {
                    char quote = RawValue[0];
                    return RawValue.Substring(1, RawValue.Length - 2).Replace(quote + quote.ToString(), quote.ToString());
                }

                return RawValue;
            }
        }
    }
}
