using System.Collections.ObjectModel;

namespace Meziantou.Framework.Html;

#if HTML_PUBLIC
public
#else
internal
#endif
sealed class HtmlReader
{
    private readonly StringBuilder _rawValue = new();
    private string? _currentElement;
    private string? _typeAttribute; // only for <script type=""...> parsing
    private bool _attIsScriptType; // only for <script type=""...> parsing
    private int _eatNext;
    private bool _eof;
    private bool _pendingTagStart; // the '<' that ended a text token belongs to the next token

    internal char QuoteChar { get; set; }
    internal int Line { get; set; } = 1;
    internal int Column { get; set; } = 1;
    internal int Offset { get; set; } = -1;

    public event EventHandler<HtmlReaderParseEventArgs>? Parsing;

    public HtmlReader(TextReader reader)
        : this(reader, options: null)
    {
    }

    public HtmlReader(TextReader reader, HtmlOptions? options)
    {
        TextReader = reader ?? throw new ArgumentNullException(nameof(reader));

        ParserState = HtmlParserState.Text;
        Value = new StringBuilder();
        FirstEncodingErrorOffset = -1;
        Errors = new Collection<HtmlError>();
        Options = options ?? new HtmlOptions();
        State = new HtmlReaderState(this, HtmlParserState.Text, string.Empty);
    }

    public TextReader TextReader { get; }
    public HtmlOptions Options { get; }
    public ICollection<HtmlError> Errors { get; }
    public HtmlReaderState State { get; private set; }
    public int FirstEncodingErrorOffset { get; private set; }
    public HtmlParserState ParserState { get; private set; }
    public StringBuilder Value { get; private set; }

    private Queue<HtmlReaderState> ParserStatesQueue { get; } = new Queue<HtmlReaderState>();

    public bool IsRestartable
    {
        get
        {
            if (TextReader is not StreamReader sr)
                return false;

            return sr.BaseStream?.CanSeek == true;
        }
    }

    public bool Restart()
    {
        if (!IsRestartable)
            throw new InvalidOperationException();

        if (TextReader is not StreamReader sr || sr.BaseStream is null || !sr.BaseStream.CanSeek)
            return false;

        return sr.BaseStream.Seek(0, SeekOrigin.Begin) == 0;
    }

    private void OnParsing(HtmlReaderParseEventArgs e)
    {
        Parsing?.Invoke(this, e);
    }

    private void SetCurrentElement(string? tag)
    {
        if (!string.Equals(_currentElement, tag, StringComparison.Ordinal))
        {
            _currentElement = tag;
            _typeAttribute = null;
            _attIsScriptType = false;
        }
    }

    // Called when a new tag is opened. Unlike SetCurrentElement, this always resets the
    // '<script type="">' tracking, even when the new tag has the same name as the previous one.
    private void StartElement(string tag)
    {
        _currentElement = tag;
        _typeAttribute = null;
        _attIsScriptType = false;
    }

    // Sets the state to enter after the '>' of a start tag has been read.
    private void SetStateAfterTagEnd()
    {
        var readOptions = Options.GetElementReadOptions(_currentElement ?? string.Empty);
        ParserState = (readOptions & HtmlElementReadOptions.InnerRaw) == HtmlElementReadOptions.InnerRaw && !Options.ParseScriptType(_typeAttribute)
            ? HtmlParserState.RawText
            : HtmlParserState.Text;
    }

    private void AdvancePosition(char c, char peek)
    {
        // a line break is "\n", "\r\n" or a lone "\r"
        if (c == '\n' || (c == '\r' && peek != '\n'))
        {
            Line++;
            Column = 1;
        }
        else if (c != '\r')
        {
            Column++;
        }
    }

    // Returns true when the quote just appended to <paramref name="value"/> closes it.
    // A doubled quote ("" or '') is an escaped quote and does not close the value.
    private static bool ClosesQuotedValue(StringBuilder value, char quoteChar)
    {
        var run = 0;
        for (var i = value.Length - 1; i >= 0 && value[i] == quoteChar; i--)
        {
            run++;
        }

        // the opening quote is not part of the content
        if (run == value.Length)
        {
            run--;
        }

        return (run % 2) != 0;
    }

    private bool OnParsing(ref char c, ref char prev, ref char peek, out bool cont)
    {
        var e = new HtmlReaderParseEventArgs(Value, _rawValue)
        {
            Eof = _eof,
            CurrentElement = _currentElement,
            CurrentCharacter = c,
            PreviousCharacter = prev,
            PeekCharacter = peek,
            EatNextCharacters = _eatNext,
            State = ParserState,
        };

        OnParsing(e);
        cont = e.Continue;
        _eof = e.Eof;
        prev = e.PreviousCharacter;
        c = e.CurrentCharacter;
        SetCurrentElement(e.CurrentElement);
        peek = e.PeekCharacter;
        _eatNext = e.EatNextCharacters;
        ParserState = e.State;
        if (e.Cancel)
            return false;

        return true;
    }

    public static bool IsAnyQuote(int character)
    {
        return character is '"' or '\'';
    }

    public static bool IsWhiteSpace(int character)
    {
        return character is 10 or 13 or 32 or 9;
    }

    public HtmlReaderState CreateState(HtmlParserState rawParserState, string? rawValue)
    {
        return new HtmlReaderState(this, rawParserState, rawValue);
    }

    private void PushCurrentState(HtmlParserState fragmentType, string? value)
    {
        PushState(CreateState(fragmentType, value));
    }

    private void PushState(HtmlReaderState state)
    {
        if (state.ParserState == HtmlParserState.AttName)
        {
            _attIsScriptType = state.Value is not null && _currentElement is not null &&
                state.Value.Equals("type", StringComparison.OrdinalIgnoreCase) &&
                _currentElement.Equals("script", StringComparison.OrdinalIgnoreCase);
        }
        else if (_attIsScriptType && state.ParserState is HtmlParserState.AttValue && state.Value is not null)
        {
            _typeAttribute = state.Value;
        }

        ParserStatesQueue.Enqueue(state);
    }

    private void PushCurrentState()
    {
        PushState(CreateState(ParserState, Value.ToString()));
    }

    private void AddError(HtmlErrorType type)
    {
        Errors.Add(new HtmlError(State.Line, State.Column, State.Offset, type));
    }

    public bool Read()
    {
        if (ParserStatesQueue.Count > 0)
        {
            State = ParserStatesQueue.Dequeue();
            return true;
        }

        if (_eof)
            return false;

        DoRead();

        if (ParserStatesQueue.Count > 0)
        {
            State = ParserStatesQueue.Dequeue();
            return true;
        }

        return false;
    }

    private void PushEndOfFile()
    {
        switch (ParserState)
        {
            case HtmlParserState.CDataText:
            case HtmlParserState.Text:
                if (_rawValue.Length > 0)
                {
                    PushCurrentState();
                }

                break;

            case HtmlParserState.TagOpen:
            case HtmlParserState.CommentOpen:
            case HtmlParserState.Atts:
                // _rawValue already contains the leading '<'
                PushCurrentState(HtmlParserState.Text, _rawValue.ToString());
                break;

            case HtmlParserState.RawText:
                if (Value.Length > 0)
                {
                    PushCurrentState(HtmlParserState.Text, Value.ToString());
                }

                break;

            case HtmlParserState.CData:
                PushCurrentState(HtmlParserState.CDataText, Value.ToString());
                break;

            case HtmlParserState.AttName:
                if (string.Equals(_rawValue.ToString().Trim(), ">", StringComparison.Ordinal))
                    break;

                PushCurrentState();
                PushCurrentState(HtmlParserState.AttValue, value: null);
                break;

            case HtmlParserState.AttValue:
                PushCurrentState();
                break;

            case HtmlParserState.TagStart:
                PushCurrentState(HtmlParserState.Text, _rawValue.ToString());
                break;
        }
    }

    private void DoRead()
    {
        _rawValue.Length = 0;
        if (_pendingTagStart)
        {
            // the '<' was consumed while flushing the preceding text token: it belongs to this token
            _rawValue.Append('<');
            _pendingTagStart = false;
        }

        Value.Length = 0;

        var c = char.MaxValue;
        while (true)
        {
            var prev = c;
            var read = TextReader.Read();
            var eof = read < 0;
            c = eof ? char.MaxValue : (char)read;

            var peekRead = TextReader.Peek();
            var peek = peekRead < 0 ? char.MaxValue : (char)peekRead;

            if (_eatNext > 0)
            {
                _eatNext--;
                if (!eof)
                {
                    Offset++;
                    AdvancePosition(c, peek);
                }

                continue;
            }

            _eof = eof;
            if (!OnParsing(ref c, ref prev, ref peek, out var cont))
                return;

            if (cont)
                continue;

            if (_eof)
            {
                PushEndOfFile();
                return;
            }

            _rawValue.Append(c);
            Offset++;
            if (c == 65533)
            {
                FirstEncodingErrorOffset = Offset;
                Column++;
                continue;
            }

            AdvancePosition(c, peek);

            switch (ParserState)
            {
                case HtmlParserState.Text:
                    if (c == '<')
                    {
                        if (Value.Length == 0)
                        {
                            ParserState = HtmlParserState.TagStart;
                        }
                        else
                        {
                            PushCurrentState();
                            ParserState = HtmlParserState.TagStart;
                            _pendingTagStart = true;
                            return;
                        }
                    }
                    else
                    {
                        Value.Append(c);
                    }

                    break;

                case HtmlParserState.RawText:
                    if (_currentElement is not null &&
                        ((c == '>') || IsWhiteSpace(c)) && (Value.Length >= (_currentElement.Length + 2)) &&
                        (Value[Value.Length - _currentElement.Length - 2] == '<') &&
                        (Value[Value.Length - _currentElement.Length - 1] == '/') &&
                        Value.ToString(Value.Length - _currentElement.Length, _currentElement.Length).EqualsIgnoreCase(_currentElement))
                    {
                        var rawText = Value.ToString(0, Value.Length - _currentElement.Length - 2);
                        PushCurrentState(HtmlParserState.Text, rawText);
                        PushCurrentState(HtmlParserState.TagClose, _currentElement);
                        if (c == '>')
                        {
                            ParserState = HtmlParserState.Text;
                            return;
                        }

                        // '</script foo>': the end tag is already closed, parse and discard what remains
                        StartElement("/" + _currentElement);
                        ParserState = HtmlParserState.Atts;
                        return;
                    }

                    Value.Append(c);
                    break;

                case HtmlParserState.CData:
                    if (c == '>' && Value.Length >= 2 && Value[^2] == ']' && Value[^1] == ']')
                    {
                        var rawText = Value.ToString(0, Value.Length - 2);
                        PushCurrentState(HtmlParserState.CDataText, rawText);
                        ParserState = HtmlParserState.Text;
                        return;
                    }

                    Value.Append(c);
                    break;

                case HtmlParserState.TagStart:
                    if (c == '<')
                    {
                        AddError(HtmlErrorType.TagNotClosed);
                        Value = new StringBuilder(_rawValue.ToString());
                        ParserState = HtmlParserState.Text;
                        continue;
                    }

                    // '<' is only the start of a tag when followed by a tag name, an end tag,
                    // a markup declaration ('<!') or a processing instruction ('<?').
                    // Anything else ('a < b', '1<2', '</ b') is text.
                    if ((!char.IsAsciiLetter(c) && c is not '/' and not '!' and not '?') ||
                        (c == '/' && !char.IsAsciiLetter(peek)))
                    {
                        Value = new StringBuilder(_rawValue.ToString());
                        ParserState = HtmlParserState.Text;
                        continue;
                    }

                    ParserState = HtmlParserState.TagOpen;
                    Value.Append(c);
                    break;

                case HtmlParserState.TagOpen:
                    if (c == '<')
                    {
                        AddError(HtmlErrorType.TagNotClosed);
                        Value = new StringBuilder(_rawValue.ToString());
                        ParserState = HtmlParserState.Text;
                        continue;
                    }

                    if (c == '>')
                    {
                        StartElement(Value.ToString());
                        PushCurrentState();
                        PushCurrentState(HtmlParserState.TagEnd, _currentElement);
                        SetStateAfterTagEnd();
                        return;
                    }

                    if (c == '/')
                    {
                        StartElement(Value.ToString());
                        PushCurrentState();
                        if (peek == '>')
                        {
                            PushCurrentState(HtmlParserState.TagEndClose, _currentElement);
                            ParserState = HtmlParserState.Text;
                            _eatNext = 1;
                            return;
                        }

                        // '<br/ >': the solidus is not part of the tag name
                        ParserState = HtmlParserState.Atts;
                        return;
                    }

                    if (IsWhiteSpace(c))
                    {
                        StartElement(Value.ToString());
                        PushCurrentState();
                        ParserState = HtmlParserState.Atts;
                        return;
                    }

                    Value.Append(c);

                    if (string.Equals(Value.ToString(), "!--", StringComparison.Ordinal))
                    {
                        Value.Length = 0;
                        ParserState = HtmlParserState.CommentOpen;
                    }
                    else if (string.Equals(Value.ToString(), "![CDATA[", StringComparison.Ordinal))
                    {
                        Value.Length = 0;
                        ParserState = HtmlParserState.CData;
                    }

                    break;

                case HtmlParserState.CommentOpen:
                    if (c == '>')
                    {
                        // '<!-->' and '<!--->' are empty comments (abrupt closing)
                        if (Value.Length == 0 || (Value.Length == 1 && Value[0] == '-'))
                        {
                            PushCurrentState(HtmlParserState.CommentClose, string.Empty);
                            ParserState = HtmlParserState.Text;
                            return;
                        }

                        // a comment ends with '-->' or with '--!>'
                        var closingLength = 0;
                        if (Value.Length >= 2 && Value[^1] == '-' && Value[^2] == '-')
                        {
                            closingLength = 2;
                        }
                        else if (Value.Length >= 3 && Value[^1] == '!' && Value[^2] == '-' && Value[^3] == '-')
                        {
                            closingLength = 3;
                        }

                        if (closingLength > 0)
                        {
                            PushCurrentState(HtmlParserState.CommentClose, Value.ToString(0, Value.Length - closingLength));
                            ParserState = HtmlParserState.Text;
                            return;
                        }
                    }

                    Value.Append(c);
                    break;

                case HtmlParserState.Atts:
                    if (c == '>')
                    {
                        PushCurrentState(HtmlParserState.TagEnd, _currentElement);
                        SetStateAfterTagEnd();
                        return;
                    }

                    if (c == '/')
                    {
                        if (peek == '>')
                        {
                            PushCurrentState(HtmlParserState.TagEndClose, _currentElement);
                            ParserState = HtmlParserState.Text;
                            _eatNext = 1;
                            return;
                        }

                        // a solidus that does not close the tag is not an attribute name
                        break;
                    }

                    if (!IsWhiteSpace(c))
                    {
                        Value.Length = 0;
                        Value.Append(c);
                        ParserState = HtmlParserState.AttName;
                        break;
                    }

                    break;

                case HtmlParserState.AttName:
                    // quoted named are essentially useful for !DOCTYPE tags
                    if (Value.Length == 1) // first char?
                    {
                        if (IsAnyQuote(Value[0]))
                        {
                            // quoted
                            QuoteChar = Value[0];
                        }
                        else
                        {
                            // not quoted
                            QuoteChar = '\0';
                        }
                    }

                    // quoted name?
                    if (IsAnyQuote(QuoteChar))
                    {
                        Value.Append(c);
                        // check escaped quote
                        if (c == QuoteChar && peek != QuoteChar && ClosesQuotedValue(Value, QuoteChar))
                        {
                            PushCurrentState();
                            ParserState = HtmlParserState.Atts;
                            return;
                        }
                    }
                    else
                    {
                        if (c == '=')
                        {
                            PushCurrentState();
                            ParserState = HtmlParserState.AttValue;
                            return;
                        }

                        if (c == '>')
                        {
                            PushCurrentState();
                            PushCurrentState(HtmlParserState.AttValue, value: null);
                            PushCurrentState(HtmlParserState.TagEnd, _currentElement);
                            SetStateAfterTagEnd();
                            return;
                        }

                        if (c == '/')
                        {
                            PushCurrentState();
                            PushCurrentState(HtmlParserState.AttValue, value: null);
                            if (peek == '>')
                            {
                                PushCurrentState(HtmlParserState.TagEndClose, _currentElement);
                                ParserState = HtmlParserState.Text;
                                _eatNext = 1;
                                return;
                            }

                            // a solidus that does not close the tag is not part of the attribute name
                            ParserState = HtmlParserState.Atts;
                            return;
                        }

                        if (IsWhiteSpace(c))
                        {
                            PushCurrentState();
                            ParserState = HtmlParserState.AttAssign;
                            return;
                        }

                        Value.Append(c);
                    }

                    break;

                case HtmlParserState.AttAssign:
                    if (c == '=')
                    {
                        ParserState = HtmlParserState.AttValue;
                        break;
                    }

                    if (c == '>')
                    {
                        PushCurrentState();
                        PushCurrentState(HtmlParserState.AttValue, value: null);
                        PushCurrentState(HtmlParserState.TagEnd, _currentElement);
                        SetStateAfterTagEnd();
                        return;
                    }

                    if (c == '/')
                    {
                        PushCurrentState();
                        PushCurrentState(HtmlParserState.AttValue, value: null);
                        if (peek == '>')
                        {
                            PushCurrentState(HtmlParserState.TagEndClose, _currentElement);
                            ParserState = HtmlParserState.Text;
                            _eatNext = 1;
                            return;
                        }

                        ParserState = HtmlParserState.Atts;
                        return;
                    }

                    if (!IsWhiteSpace(c))
                    {
                        // send a null attribute
                        PushCurrentState(HtmlParserState.AttValue, value: null);

                        ParserState = HtmlParserState.AttName;
                        Value.Append(c);
                        break;
                    }

                    break;

                case HtmlParserState.AttValue:
                    if (Value.Length == 0) // first char?
                    {
                        if (c == '>')
                        {
                            // '<a b=>' declares an empty value and ends the tag
                            PushCurrentState();
                            PushCurrentState(HtmlParserState.TagEnd, _currentElement);
                            SetStateAfterTagEnd();
                            return;
                        }

                        if (!IsWhiteSpace(c))
                        {
                            // a quote as the first character starts a quoted value
                            QuoteChar = IsAnyQuote(c) ? c : '\0';
                            Value.Append(c);
                        }
                        // else skip whitespaces
                    }
                    else
                    {
                        // quoted value?
                        if (IsAnyQuote(QuoteChar))
                        {
                            Value.Append(c);
                            // check escaped quote
                            if (c == QuoteChar && peek != QuoteChar && ClosesQuotedValue(Value, QuoteChar))
                            {
                                PushCurrentState();
                                ParserState = HtmlParserState.Atts;
                                return;
                            }
                        }
                        else
                        {
                            if (c == '>')
                            {
                                PushCurrentState();
                                PushCurrentState(HtmlParserState.TagEnd, _currentElement);
                                SetStateAfterTagEnd();
                                return;
                            }

                            if (IsWhiteSpace(c))
                            {
                                PushCurrentState();
                                ParserState = HtmlParserState.Atts;
                                return;
                            }

                            // a solidus is part of an unquoted value ('<a href=foo/>' is href="foo/")
                            Value.Append(c);
                        }
                    }

                    break;
            }
        }
    }
}
