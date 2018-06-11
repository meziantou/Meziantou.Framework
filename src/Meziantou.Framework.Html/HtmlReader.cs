using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace Meziantou.Framework.Html
{
    public class HtmlReader
    {
        private readonly StringBuilder _rawValue = new StringBuilder();
        private string _currentElement;
        private string _typeAttribute; // only for <script type=""...> parsing
        private bool _attIsScriptType; // only for <script type=""...> parsing
        private readonly Queue<HtmlReaderState> _parserStatesQueue = new Queue<HtmlReaderState>();
        private int _eatNext;
        private bool _eof;

        internal char QuoteChar;
        internal int Line = 1;
        internal int Column = 1;
        internal int Offset = -1;

        public event EventHandler<HtmlReaderParseEventArgs> Parsing;

        public HtmlReader(TextReader reader)
            : this(reader, null)
        {
        }

        public HtmlReader(TextReader reader, HtmlOptions options)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            ParserState = HtmlParserState.Text;
            Value = new StringBuilder();

            FirstEncodingErrorOffset = -1;
            Errors = new Collection<HtmlError>();
            Options = options ?? new HtmlOptions();
            TextReader = reader;
        }

        public TextReader TextReader { get; }
        public HtmlOptions Options { get; }
        public virtual ICollection<HtmlError> Errors { get; }
        public virtual HtmlReaderState State { get; private set; }
        public virtual int FirstEncodingErrorOffset { get; private set; }
        public HtmlParserState ParserState { get; protected set; }
        public StringBuilder Value { get; private set; }

        protected Queue<HtmlReaderState> ParserStatesQueue => _parserStatesQueue;

        public virtual bool IsRestartable
        {
            get
            {
                var sr = TextReader as StreamReader;
                if (sr == null)
                    return false;

                return sr.BaseStream?.CanSeek == true;
            }
        }

        public virtual bool Restart()
        {
            if (!IsRestartable)
                throw new InvalidOperationException();

            var sr = TextReader as StreamReader;
            if (sr == null || sr.BaseStream == null || !sr.BaseStream.CanSeek)
                return false;

            return sr.BaseStream.Seek(0, SeekOrigin.Begin) == 0;
        }

        protected virtual void OnParsing(object sender, HtmlReaderParseEventArgs e)
        {
            Parsing?.Invoke(sender, e);
        }

        private void SetCurrentElement(string tag)
        {
            if (_currentElement != tag)
            {
                _currentElement = tag;
                _typeAttribute = null;
            }
        }

        private bool OnParsing(ref char c, ref char prev, ref char peek, out bool cont)
        {
            var e = new HtmlReaderParseEventArgs(Value, _rawValue);
            e.Eof = _eof;
            e.CurrentElement = _currentElement;
            e.CurrentCharacter = c;
            e.PreviousCharacter = prev;
            e.PeekCharacter = peek;
            e.EatNextCharacters = _eatNext;
            e.State = ParserState;
            OnParsing(this, e);
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

        public virtual bool IsAnyQuote(int character)
        {
            return character == '"' || character == '\'';
        }

        public virtual bool IsWhiteSpace(int character)
        {
            return character == 10 || character == 13 || character == 32 || character == 9;
        }

        public virtual HtmlReaderState CreateState(HtmlParserState rawParserState, string rawValue)
        {
            return new HtmlReaderState(this, rawParserState, rawValue);
        }

        protected virtual void PushCurrentState(HtmlParserState fragmentType, string value)
        {
            PushState(CreateState(fragmentType, value));
        }

        protected virtual void PushState(HtmlReaderState state)
        {
            if (state.ParserState == HtmlParserState.AttName)
            {
                _attIsScriptType = state.Value != null && _currentElement != null &&
                    state.Value.Equals("type", StringComparison.OrdinalIgnoreCase) &&
                    _currentElement.Equals("script", StringComparison.OrdinalIgnoreCase);
            }
            else if (_attIsScriptType && state.ParserState == HtmlParserState.AttValue && state.Value != null)
            {
                _typeAttribute = state.Value;
            }
            _parserStatesQueue.Enqueue(state);
        }

        protected virtual void PushCurrentState()
        {
            PushState(CreateState(ParserState, Value.ToString()));
        }

        protected virtual void AddError(HtmlErrorType type)
        {
            Errors.Add(new HtmlError(State.Line, State.Column, State.Offset, type));
        }

        public virtual bool Read()
        {
            if (_parserStatesQueue.Count > 0)
            {
                State = _parserStatesQueue.Dequeue();
                return true;
            }

            if (_eof)
                return false;

            DoRead();

            if (_parserStatesQueue.Count > 0)
            {
                State = _parserStatesQueue.Dequeue();
                return true;
            }

            return false;
        }

        protected virtual void PushEndOfFile()
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
                    PushCurrentState(HtmlParserState.Text, "<" + _rawValue);
                    break;

                case HtmlParserState.Atts:
                    PushCurrentState(HtmlParserState.Text, _rawValue.ToString());
                    break;

                case HtmlParserState.AttName:
                    if (_rawValue.ToString().Trim() == ">")
                        break;

                    PushCurrentState();
                    PushCurrentState(HtmlParserState.AttValue, null);
                    break;

                case HtmlParserState.AttValue:
                    PushCurrentState();
                    break;

                case HtmlParserState.TagStart:
                    PushCurrentState(HtmlParserState.Text, "<");
                    break;
            }
        }

        protected virtual void DoRead()
        {
            _rawValue.Length = 0;
            Value.Length = 0;

            char c = char.MaxValue;
            while (true)
            {
                char prev = c;
                c = (char)TextReader.Read();

                if (_eatNext > 0)
                {
                    _eatNext--;
                    continue;
                }

                var peek = (char)TextReader.Peek();

                if (!OnParsing(ref c, ref prev, ref peek, out bool cont))
                    return;

                if (cont)
                    continue;

                if (c == char.MaxValue)
                {
                    _eof = true;
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
                if (c == '\n')
                {
                    Line++;
                    Column = 1;
                }
                else
                {
                    if (c != '\r')
                    {
                        Column++;
                    }
                }

                switch (ParserState)
                {
                    case HtmlParserState.Text:
                        if (c == '<')
                        {
                            if (Value.Length == 0)
                            {
                                if (peek == '>')
                                {
                                    PushCurrentState(HtmlParserState.Text, _rawValue.ToString());
                                    return;
                                }
                                ParserState = HtmlParserState.TagStart;
                            }
                            else
                            {
                                PushCurrentState();
                                ParserState = HtmlParserState.TagStart;
                                return;
                            }
                        }
                        else
                        {
                            Value.Append(c);
                        }
                        break;

                    case HtmlParserState.RawText:
                        if (((c == '>') || (IsWhiteSpace(c))) && (Value.Length >= (_currentElement.Length + 2)) &&
                            (Value[Value.Length - _currentElement.Length - 2] == '<') &&
                            (Value[Value.Length - _currentElement.Length - 1] == '/') &&
                            (Value.ToString(Value.Length - _currentElement.Length, _currentElement.Length).EqualsIgnoreCase(_currentElement)))
                        {
                            string rawText = Value.ToString(0, Value.Length - _currentElement.Length - 2);
                            PushCurrentState(HtmlParserState.Text, rawText);
                            if (c == '>')
                            {
                                PushCurrentState(HtmlParserState.TagClose, _currentElement);
                                ParserState = HtmlParserState.Text;
                                return;
                            }
                            ParserState = HtmlParserState.Atts;
                            return;
                        }

                        Value.Append(c);
                        break;

                    case HtmlParserState.CData:
                        if (c == '>' && Value.Length >= 2 && Value[Value.Length - 2] == ']' && Value[Value.Length - 1] == ']')
                        {
                            string rawText = Value.ToString(0, Value.Length - 2);
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

                        if (IsWhiteSpace(c))
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
                            Value = new StringBuilder(c + _rawValue.ToString());
                            ParserState = HtmlParserState.Text;
                            continue;
                        }

                        if (c == '>')
                        {
                            SetCurrentElement(Value.ToString());
                            PushCurrentState();
                            PushCurrentState(HtmlParserState.TagEnd, _currentElement);
                            if ((Options.GetElementReadOptions(_currentElement) & HtmlElementReadOptions.InnerRaw) == HtmlElementReadOptions.InnerRaw)
                            {
                                // no need to check for <script type='' ..> here
                                ParserState = HtmlParserState.RawText;
                            }
                            else
                            {
                                ParserState = HtmlParserState.Text;
                            }
                            return;
                        }

                        if (c == '/' && peek == '>')
                        {
                            SetCurrentElement(Value.ToString());
                            PushCurrentState();
                            PushCurrentState(HtmlParserState.TagEndClose, _currentElement);
                            ParserState = HtmlParserState.Text;
                            _eatNext = 1;
                            return;
                        }

                        if (IsWhiteSpace(c))
                        {
                            SetCurrentElement(Value.ToString());
                            PushCurrentState();
                            ParserState = HtmlParserState.Atts;
                            return;
                        }

                        Value.Append(c);

                        if (Value.ToString() == "!--")
                        {
                            Value.Length = 0;
                            ParserState = HtmlParserState.CommentOpen;
                        }
                        else if (Value.ToString() == "![CDATA[")
                        {
                            Value.Length = 0;
                            ParserState = HtmlParserState.CData;
                        }
                        break;

                    case HtmlParserState.CommentOpen:
                        if (c == '>' && Value.Length > 2 && Value[Value.Length - 1] == '-' && Value[Value.Length - 2] == '-')
                        {
                            PushCurrentState(HtmlParserState.CommentClose, Value.Remove(Value.Length - 2, 2).ToString());
                            ParserState = HtmlParserState.Text;
                            return;
                        }

                        Value.Append(c);
                        break;

                    case HtmlParserState.Atts:
                        if (c == '>')
                        {
                            PushCurrentState(HtmlParserState.TagEnd, _currentElement);
                            if ((Options.GetElementReadOptions(_currentElement) & HtmlElementReadOptions.InnerRaw) == HtmlElementReadOptions.InnerRaw)
                            {
                                if (Options.ParseScriptType(_typeAttribute))
                                {
                                    ParserState = HtmlParserState.Text;
                                }
                                else
                                {
                                    ParserState = HtmlParserState.RawText;
                                }
                            }
                            else
                            {
                                ParserState = HtmlParserState.Text;
                            }
                            return;
                        }

                        if (c == '/' && peek == '>')
                        {
                            PushCurrentState(HtmlParserState.TagEndClose, _currentElement);
                            ParserState = HtmlParserState.Text;
                            _eatNext = 1;
                            return;
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
                            if (c == QuoteChar && peek != QuoteChar && prev != QuoteChar)
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
                                PushCurrentState(HtmlParserState.AttValue, null);
                                PushCurrentState(HtmlParserState.TagEnd, _currentElement);
                                if ((Options.GetElementReadOptions(_currentElement) & HtmlElementReadOptions.InnerRaw) == HtmlElementReadOptions.InnerRaw)
                                {
                                    if (Options.ParseScriptType(_typeAttribute))
                                    {
                                        ParserState = HtmlParserState.Text;
                                    }
                                    else
                                    {
                                        ParserState = HtmlParserState.RawText;
                                    }
                                }
                                else
                                {
                                    ParserState = HtmlParserState.Text;
                                }
                                return;
                            }

                            if (c == '/' && peek == '>')
                            {
                                PushCurrentState();
                                PushCurrentState(HtmlParserState.AttValue, null);
                                PushCurrentState(HtmlParserState.TagEndClose, _currentElement);
                                ParserState = HtmlParserState.Text;
                                _eatNext = 1;
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
                            PushCurrentState(HtmlParserState.AttValue, null);
                            PushCurrentState(HtmlParserState.TagEnd, _currentElement);
                            if ((Options.GetElementReadOptions(_currentElement) & HtmlElementReadOptions.InnerRaw) == HtmlElementReadOptions.InnerRaw)
                            {
                                if (Options.ParseScriptType(_typeAttribute))
                                {
                                    ParserState = HtmlParserState.Text;
                                }
                                else
                                {
                                    ParserState = HtmlParserState.RawText;
                                }
                            }
                            else
                            {
                                ParserState = HtmlParserState.Text;
                            }
                            return;
                        }

                        if (c == '/' && peek == '>')
                        {
                            PushCurrentState();
                            PushCurrentState(HtmlParserState.AttValue, null);
                            PushCurrentState(HtmlParserState.TagEndClose, _currentElement);
                            ParserState = HtmlParserState.Text;
                            _eatNext = 1;
                            return;
                        }

                        if (!IsWhiteSpace(c))
                        {
                            // send a null attribute
                            PushCurrentState(HtmlParserState.AttValue, null);

                            ParserState = HtmlParserState.AttName;
                            Value.Append(c);
                            break;
                        }
                        break;

                    case HtmlParserState.AttValue:
                        if (Value.Length == 0) // first char?
                        {
                            if (!IsWhiteSpace(c))
                            {
                                if (IsAnyQuote(c))
                                {
                                    // quoted
                                    QuoteChar = c;
                                }
                                else
                                {
                                    // not quoted
                                    QuoteChar = '\0';
                                }
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
                                if (c == QuoteChar && peek != QuoteChar && (prev != QuoteChar || Value.Length == 2)) // test "" or ''
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
                                    if ((Options.GetElementReadOptions(_currentElement) & HtmlElementReadOptions.InnerRaw) == HtmlElementReadOptions.InnerRaw)
                                    {
                                        if (Options.ParseScriptType(_typeAttribute))
                                        {
                                            ParserState = HtmlParserState.Text;
                                        }
                                        else
                                        {
                                            ParserState = HtmlParserState.RawText;
                                        }
                                    }
                                    else
                                    {
                                        ParserState = HtmlParserState.Text;
                                    }
                                    return;
                                }

                                if (c == '/' && peek == '>')
                                {
                                    PushCurrentState();
                                    PushCurrentState(HtmlParserState.TagEndClose, _currentElement);
                                    ParserState = HtmlParserState.Text;
                                    _eatNext = 1;
                                    return;
                                }

                                if (IsWhiteSpace(c))
                                {
                                    PushCurrentState();
                                    ParserState = HtmlParserState.Atts;
                                    return;
                                }
                                Value.Append(c);
                            }
                        }
                        break;
                }
            }
        }
    }
}
