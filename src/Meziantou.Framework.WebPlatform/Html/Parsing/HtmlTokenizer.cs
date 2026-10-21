using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Html.Parsing;

internal enum TokenizerState : byte
{
    Data,
    Rcdata,
    Rawtext,
    ScriptData,
    Plaintext,
    TagOpen,
    EndTagOpen,
    TagName,
    RcdataLessThanSign,
    RcdataEndTagOpen,
    RcdataEndTagName,
    RawtextLessThanSign,
    RawtextEndTagOpen,
    RawtextEndTagName,
    ScriptDataLessThanSign,
    ScriptDataEndTagOpen,
    ScriptDataEndTagName,
    ScriptDataEscapeStart,
    ScriptDataEscapeStartDash,
    ScriptDataEscaped,
    ScriptDataEscapedDash,
    ScriptDataEscapedDashDash,
    ScriptDataEscapedLessThanSign,
    ScriptDataEscapedEndTagOpen,
    ScriptDataEscapedEndTagName,
    ScriptDataDoubleEscapeStart,
    ScriptDataDoubleEscaped,
    ScriptDataDoubleEscapedDash,
    ScriptDataDoubleEscapedDashDash,
    ScriptDataDoubleEscapedLessThanSign,
    ScriptDataDoubleEscapeEnd,
    BeforeAttributeName,
    AttributeName,
    AfterAttributeName,
    BeforeAttributeValue,
    AttributeValueDoubleQuoted,
    AttributeValueSingleQuoted,
    AttributeValueUnquoted,
    AfterAttributeValueQuoted,
    SelfClosingStartTag,
    BogusComment,
    MarkupDeclarationOpen,
    CommentStart,
    CommentStartDash,
    Comment,
    CommentLessThanSign,
    CommentLessThanSignBang,
    CommentLessThanSignBangDash,
    CommentLessThanSignBangDashDash,
    CommentEndDash,
    CommentEnd,
    CommentEndBang,
    Doctype,
    BeforeDoctypeName,
    DoctypeName,
    AfterDoctypeName,
    AfterDoctypePublicKeyword,
    BeforeDoctypePublicIdentifier,
    DoctypePublicIdentifierDoubleQuoted,
    DoctypePublicIdentifierSingleQuoted,
    AfterDoctypePublicIdentifier,
    BetweenDoctypePublicAndSystemIdentifiers,
    AfterDoctypeSystemKeyword,
    BeforeDoctypeSystemIdentifier,
    DoctypeSystemIdentifierDoubleQuoted,
    DoctypeSystemIdentifierSingleQuoted,
    AfterDoctypeSystemIdentifier,
    BogusDoctype,
    CdataSection,
    CdataSectionBracket,
    CdataSectionEnd,
    ProcessingInstructionOpen,
    ProcessingInstructionTarget,
    AfterProcessingInstructionTarget,
    ProcessingInstructionData,
    ProcessingInstructionQuestionable,
    CharacterReference,
    NamedCharacterReference,
    AmbiguousAmpersand,
    NumericCharacterReference,
    HexadecimalCharacterReferenceStart,
    HexadecimalCharacterReference,
    DecimalCharacterReference,
    NumericCharacterReferenceEnd,
}

/// <summary>
/// The HTML tokenizer (https://html.spec.whatwg.org/multipage/parsing.html#tokenization).
/// The input is preprocessed on the fly (newline normalization). Consecutive character tokens are merged into runs.
/// </summary>
internal sealed class HtmlTokenizer
{
    private const int Eof = -1;

    private static readonly SearchValues<char> DataStopChars = SearchValues.Create("&<\0\r");
    private static readonly SearchValues<char> RawTextStopChars = SearchValues.Create("<\0\r");
    private static readonly SearchValues<char> PlainTextStopChars = SearchValues.Create("\0\r");
    private static readonly SearchValues<char> DoubleQuotedStopChars = SearchValues.Create("\"&\0\r");
    private static readonly SearchValues<char> SingleQuotedStopChars = SearchValues.Create("'&\0\r");
    private static readonly SearchValues<char> UnquotedStopChars = SearchValues.Create("\t\n\f\r &>\0\"'<=`");
    private static readonly SearchValues<char> TagNameStopChars = SearchValues.Create("\t\n\f\r />\0ABCDEFGHIJKLMNOPQRSTUVWXYZ");
    private static readonly SearchValues<char> AttributeNameStopChars = SearchValues.Create("\t\n\f\r />=\0\"'<ABCDEFGHIJKLMNOPQRSTUVWXYZ");
    private static readonly SearchValues<char> CommentStopChars = SearchValues.Create("<-\0\r");

    // Characters that can produce input stream errors; used by the fast paths when errors are reported
    private static readonly SearchValues<char> InputStreamErrorChars = SearchValues.Create(CreateInputStreamErrorChars());

    private static string CreateInputStreamErrorChars()
    {
        var builder = new StringBuilder();
        for (var c = 0x01; c <= 0x1F; c++)
        {
            if (c is not ('\t' or '\n' or '\f' or '\r'))
            {
                builder.Append((char)c);
            }
        }

        for (var c = 0x7F; c <= 0x9F; c++)
        {
            builder.Append((char)c);
        }

        for (var c = 0xD800; c <= 0xDFFF; c++)
        {
            builder.Append((char)c);
        }

        for (var c = 0xFDD0; c <= 0xFDEF; c++)
        {
            builder.Append((char)c);
        }

        builder.Append((char)0xFFFE).Append((char)0xFFFF);
        return builder.ToString();
    }

    private string _input;
    private readonly Action<HtmlParseErrorInfo>? _errorHandler;
    private readonly NameTable _nameTable;
    private int _position;
    private int _tagStart;

    // The tokenizer stops at _limit: the end of the input, or the insertion point while it processes the characters
    // inserted by document.write() (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#document-write-steps).
    // When it can stop before the end of file (_canSuspend), a token that is not complete at the limit is tokenized again
    // from its start when more characters are inserted (see NextTokenSuspendable).
    private int _limit;

    // The position of the character that follows a carriage return consumed at the limit of the input, or -1
    private int _lineFeedSkipPosition = -1;
    private int? _stopFromEnd;
    private bool _eofInserted = true;
    private bool _canSuspend;
    private ResumePoint _resumePoint;

    private TokenizerState _state;
    private TokenizerState _returnState;
    private bool _reconsume;
    private int _current;

    // Pending character run
    private char[] _characters = new char[256];
    private int _characterCount;

    // Pending non-character token, emitted after the character run
    private HtmlTokenType _pendingTokenType;

    // Current tag token
    private readonly StringBuilder _tagName = new();
    private bool _isEndTag;
    private bool _selfClosing;
    private readonly List<HtmlTokenAttribute> _attributes = [];
    private readonly StringBuilder _attributeName = new();
    private readonly StringBuilder _attributeValue = new();
    private bool _hasCurrentAttribute;
    private bool _currentAttributeIsDuplicate;

    /// <summary>Whether the current tag token had a duplicate-attribute parse error (https://w3c.github.io/webappsec-csp/#is-element-nonceable).</summary>
    private bool _tagHadDuplicateAttribute;
    private int _streamErrorsReportedUpTo;
    private string? _lastStartTagName;

    // Current comment, DOCTYPE, processing instruction token
    private readonly StringBuilder _commentData = new();
    private readonly StringBuilder _doctypeName = new();
    private readonly StringBuilder _doctypePublicId = new();
    private readonly StringBuilder _doctypeSystemId = new();
    private bool _doctypeNameMissing;
    private bool _doctypePublicIdMissing;
    private bool _doctypeSystemIdMissing;
    private bool _forceQuirks;
    private readonly StringBuilder _temporaryBuffer = new();
    private readonly StringBuilder _piData = new();
    private string _piTarget = "";
    private long _characterReferenceCode;

    /// <summary>The names and the values shared by the tokens (and the texts of the tree builder).</summary>
    public NameTable NameTable => _nameTable;

    public HtmlTokenizer(string input, Action<HtmlParseErrorInfo>? errorHandler, NameTable nameTable)
    {
        _input = input;
        _errorHandler = errorHandler;
        _nameTable = nameTable;
        UpdateLimit();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Dynamic markup insertion (https://html.spec.whatwg.org/multipage/parsing.html#the-input-byte-stream)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets or sets whether the end of the input is the end of the file. It is false for a script-created parser
    /// (<c>document.open()</c>) until <c>document.close()</c> inserts the explicit "EOF" character: the tokenizer waits
    /// for more characters at the end of the input.
    /// </summary>
    public bool EofInserted
    {
        get => _eofInserted;
        set
        {
            _eofInserted = value;
            UpdateLimit();
        }
    }

    /// <summary>
    /// Gets or sets the position (from the end of the input) where the tokenizer stops, or null to tokenize until the end
    /// of the input: the insertion point while the parser processes the characters inserted by <c>document.write()</c>.
    /// </summary>
    public int? StopFromEnd
    {
        get => _stopFromEnd;
        set
        {
            _stopFromEnd = value;
            UpdateLimit();
        }
    }

    /// <summary>Gets the position of the next input character, from the end of the input (the insertion point after a script end tag).</summary>
    public int PositionFromEnd => _input.Length - _position;

    /// <summary>Gets the characters of the input stream that are not consumed yet (the input of a speculative parser).</summary>
    public string RemainingInput => _input[_position..];

    /// <summary>
    /// Gets the index in the input of the '&lt;' of the last markup (a tag, a comment, a DOCTYPE...) that does not start in an
    /// escaped script (used by the encoding prescan): the tokenizer may have consumed a tag when it returns the characters
    /// before it.
    /// </summary>
    public int TagStartPosition => _tagStart;

    /// <summary>Gets whether a token was consumed after the characters returned by the last call of <see cref="NextToken"/> (it is returned by the next call).</summary>
    public bool HasPendingToken => _pendingTokenType != HtmlTokenType.None;

    /// <summary>Gets the input stream (the characters inserted by <c>document.write()</c> included).</summary>
    public string Input => _input;

    /// <summary>
    /// Gets the offset in <see cref="Input"/> of the parse errors of the tree builder: the last character of the current token,
    /// or the end of the input for the end-of-file token.
    /// </summary>
    public int CurrentTokenEndOffset => TokenType switch
    {
        HtmlTokenType.EndOfFile => _position,

        // The tokenizer consumed the markup after the characters: they end before its '<'
        HtmlTokenType.Characters when _pendingTokenType is not (HtmlTokenType.None or HtmlTokenType.EndOfFile) => Math.Max(0, _tagStart - 1),
        _ => Math.Max(0, _position - 1),
    };

    // The line and the start of the line of a position of the input, counted incrementally (the characters before the
    // position of the tokenizer do not change)
    private int _lineCountPosition;
    private int _lineCountLine = 1;
    private int _lineCountLineStart;

    /// <summary>
    /// Gets the line (1-based) and the column (0-based, in UTF-16 code units) of the next input character, e.g. the start of
    /// the source text of a script whose start tag was just tokenized.
    /// </summary>
    public (int Line, int Column) GetCurrentLineAndColumn()
    {
        var position = Math.Min(_position, _input.Length);
        if (position < _lineCountPosition)
        {
            _lineCountPosition = 0;
            _lineCountLine = 1;
            _lineCountLineStart = 0;
        }

        // Fast path: without CR, the lines end at the LF characters
        var slice = _input.AsSpan(_lineCountPosition, position - _lineCountPosition);
        if (!slice.Contains('\r'))
        {
            var lineFeedCount = slice.Count('\n');
            if (lineFeedCount > 0)
            {
                _lineCountLine += lineFeedCount;
                _lineCountLineStart = _lineCountPosition + slice.LastIndexOf('\n') + 1;
            }

            _lineCountPosition = position;
            return (_lineCountLine, position - _lineCountLineStart);
        }

        for (var i = _lineCountPosition; i < position; i++)
        {
            var c = _input[i];
            if (c == '\n' || (c == '\r' && (i + 1 >= _input.Length || _input[i + 1] != '\n')))
            {
                _lineCountLine++;
                _lineCountLineStart = i + 1;
            }
        }

        _lineCountPosition = position;
        return (_lineCountLine, position - _lineCountLineStart);
    }

    /// <summary>Gets whether the last call of <see cref="NextToken"/> stopped at the limit without a token (it returned <see cref="HtmlTokenType.None"/>).</summary>
    public bool IsWaitingForInput { get; private set; }

    /// <summary>Inserts characters in the input stream, at a position given from the end of the input (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#document-write-steps).</summary>
    public void Insert(int positionFromEnd, string text)
    {
        _input = _input.Insert(_input.Length - positionFromEnd, text);
        UpdateLimit();
    }

    /// <summary>Replaces the characters of the input that are not consumed yet (the parser changes the encoding of the rest of the input).</summary>
    public void ReplaceRemainingInput(string text)
    {
        _input = string.Concat(_input.AsSpan(0, _position), text);
        UpdateLimit();
    }

    private void UpdateLimit()
    {
        _limit = _input.Length - (_stopFromEnd ?? 0);
        _canSuspend = _stopFromEnd is not null || !_eofInserted;

        // https://html.spec.whatwg.org/multipage/parsing.html#preprocessing-the-input-stream: a line feed that follows a
        // carriage return is ignored, also when they are inserted separately
        if (_lineFeedSkipPosition >= 0 && _lineFeedSkipPosition == _position && _position < _limit)
        {
            if (_input[_position] == '\n')
            {
                _position++;
            }

            _lineFeedSkipPosition = -1;
        }
    }

    /// <summary>Thrown when the tokenizer reaches its limit before the end of the file: the current token is tokenized again later.</summary>
#pragma warning disable CA1032, CA1064 // Private control flow exception, never exposed
    private sealed class SuspendException : Exception
    {
        public static readonly SuspendException Instance = new();
    }
#pragma warning restore CA1032, CA1064

    /// <summary>The state of the tokenizer at a token boundary (or in a text state), restored when the tokenizer suspends.</summary>
    private struct ResumePoint
    {
        public int Position;
        public TokenizerState State;
        public TokenizerState ReturnState;
        public bool Reconsume;
        public int Current;
        public int CharacterCount;
        public int StreamErrorsReportedUpTo;
    }

    private void SaveResumePoint(int position, TokenizerState state)
    {
        _resumePoint = new ResumePoint
        {
            Position = position,
            State = state,
            ReturnState = _returnState,
            Reconsume = _reconsume,
            Current = _current,
            CharacterCount = _characterCount,
            StreamErrorsReportedUpTo = _streamErrorsReportedUpTo,
        };
    }

    private void RestoreResumePoint()
    {
        _position = _resumePoint.Position;
        _state = _resumePoint.State;
        _returnState = _resumePoint.ReturnState;
        _reconsume = _resumePoint.Reconsume;
        _current = _resumePoint.Current;
        _characterCount = _resumePoint.CharacterCount;
        _streamErrorsReportedUpTo = _resumePoint.StreamErrorsReportedUpTo;
    }

    private static bool IsTextState(TokenizerState state) => state is TokenizerState.Data or TokenizerState.Rcdata or TokenizerState.Rawtext or TokenizerState.ScriptData or TokenizerState.Plaintext;

    /// <summary>Stops the tokenizer when it needs characters after its limit (see <see cref="NextTokenSuspendable"/>).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Suspend() => throw SuspendException.Instance;

    /// <summary>Used by the tree builder to check whether the adjusted current node is in the HTML namespace (for CDATA sections).</summary>
    public Func<bool>? IsAdjustedCurrentNodeInForeignContent { get; set; }

    public TokenizerState State
    {
        get => _state;
        set => _state = value;
    }

    /// <summary>Sets the name of the last start tag emitted (used by the tests and by the fragment case).</summary>
    public string? LastStartTagName
    {
        get => _lastStartTagName;
        set => _lastStartTagName = value;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Current token (valid after NextToken returns)
    // ----------------------------------------------------------------------------------------------------------------

    public HtmlTokenType TokenType { get; private set; }

    public ReadOnlySpan<char> Characters => _emittedCharacters.AsSpan(0, _emittedCharacterCount);

    private char[] _emittedCharacters = new char[256];
    private int _emittedCharacterCount;

    public HtmlTagToken? Tag { get; private set; }

    public string CommentData { get; private set; } = "";

    public string? DoctypeName { get; private set; }

    public string? DoctypePublicId { get; private set; }

    public string? DoctypeSystemId { get; private set; }

    public bool DoctypeForceQuirks { get; private set; }

    public string ProcessingInstructionTarget { get; private set; } = "";

    public string ProcessingInstructionData { get; private set; } = "";

    // Token emitted by the state machine, waiting for the character run to be returned first
    private HtmlTagToken? _pendingTag;

    /// <summary>A tag token released by the tree builder, reused for the next tag.</summary>
    private HtmlTagToken? _freeTag;
    private string _pendingComment = "";
    private string? _pendingDoctypeName;
    private string? _pendingDoctypePublicId;
    private string? _pendingDoctypeSystemId;
    private bool _pendingDoctypeForceQuirks;
    private string _pendingPiTarget = "";
    private string _pendingPiData = "";

    /// <summary>
    /// Runs the state machine until a token is available. Returns <see cref="HtmlTokenType.None"/> when the tokenizer stops
    /// at its limit before the end of the file (see <see cref="StopFromEnd"/> and <see cref="EofInserted"/>).
    /// </summary>
    public HtmlTokenType NextToken()
    {
        IsWaitingForInput = false;
        if (_pendingTokenType != HtmlTokenType.None)
        {
            ReturnPendingToken();
            return TokenType;
        }

        if (_canSuspend)
            return NextTokenSuspendable();

        while (_pendingTokenType == HtmlTokenType.None)
        {
            Step();
        }

        return ReturnToken();
    }

    /// <summary>
    /// <see cref="NextToken"/> when the tokenizer can stop before the end of the file. The state is saved at the token
    /// boundaries and in the text states; when the tokenizer needs a character after its limit, the state is restored (the
    /// characters of the text states are returned), and the rest is tokenized again when more characters are inserted.
    /// </summary>
    private HtmlTokenType NextTokenSuspendable()
    {
        SaveResumePoint(_position, _state);
        try
        {
            while (_pendingTokenType == HtmlTokenType.None)
            {
                var state = _state;
                if (IsTextState(state))
                {
                    SaveResumePoint(_position, state);
                }

                Step();

                // Leaving a text state (e.g. '<' in the data state): the characters before it can be returned
                if (IsTextState(state) && !IsTextState(_state) && !_reconsume && _pendingTokenType == HtmlTokenType.None)
                {
                    SaveResumePoint(_position - 1, state);
                }
            }
        }
        catch (SuspendException)
        {
            RestoreResumePoint();
            if (_characterCount > 0)
            {
                ReturnCharacters();
                return TokenType;
            }

            IsWaitingForInput = true;
            TokenType = HtmlTokenType.None;
            return HtmlTokenType.None;
        }

        return ReturnToken();
    }

    private HtmlTokenType ReturnToken()
    {
        if (_characterCount > 0)
        {
            // Return the character run first; the other token is returned by the next call
            ReturnCharacters();
            return TokenType;
        }

        ReturnPendingToken();
        return TokenType;
    }

    private void ReturnCharacters()
    {
        (_emittedCharacters, _characters) = (_characters, _emittedCharacters);
        _emittedCharacterCount = _characterCount;
        _characterCount = 0;
        TokenType = HtmlTokenType.Characters;
    }

    private void ReturnPendingToken()
    {
        TokenType = _pendingTokenType;
        _pendingTokenType = HtmlTokenType.None;
        switch (TokenType)
        {
            case HtmlTokenType.StartTag:
            case HtmlTokenType.EndTag:
                Tag = _pendingTag;
                _pendingTag = null;
                break;
            case HtmlTokenType.Comment:
                CommentData = _pendingComment;
                break;
            case HtmlTokenType.Doctype:
                DoctypeName = _pendingDoctypeName;
                DoctypePublicId = _pendingDoctypePublicId;
                DoctypeSystemId = _pendingDoctypeSystemId;
                DoctypeForceQuirks = _pendingDoctypeForceQuirks;
                break;
            case HtmlTokenType.ProcessingInstruction:
                ProcessingInstructionTarget = _pendingPiTarget;
                ProcessingInstructionData = _pendingPiData;
                break;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Input stream
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Consumes the next input character (with newline normalization). Returns <see cref="Eof"/> at the end.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Consume()
    {
        if (_reconsume)
        {
            _reconsume = false;
            return _current;
        }

        if (_position >= _limit)
        {
            if (_canSuspend)
            {
                Suspend();
            }

            _current = Eof;
            return Eof;
        }

        int c = _input[_position++];
        if (c == '\r')
        {
            if (_position < _limit)
            {
                if (_input[_position] == '\n')
                {
                    _position++;
                }
            }
            else
            {
                // The next character is not inserted yet (e.g. document.write("\r") then document.write("\n")): a line
                // feed inserted there is ignored
                _lineFeedSkipPosition = _position;
            }

            c = '\n';
        }
        else if (_errorHandler is not null && (c >= 0x7F || (c < 0x20 && c is not ('\t' or '\n' or '\f' or '\0'))))
        {
            ReportInputStreamError(c);
        }

        _current = c;
        return c;
    }

    /// <summary>Reports the errors of the preprocessing of the input stream (https://html.spec.whatwg.org/multipage/parsing.html#preprocessing-the-input-stream).</summary>
    private void ReportInputStreamError(int c)
    {
        var offset = _position - 1;
        if (offset < _streamErrorsReportedUpTo)
            return;

        if (char.IsHighSurrogate((char)c))
        {
            if (_position < _input.Length && char.IsLowSurrogate(_input[_position]))
            {
                var codePoint = char.ConvertToUtf32((char)c, _input[_position]);
                if ((codePoint & 0xFFFE) == 0xFFFE)
                {
                    _errorHandler!(new HtmlParseErrorInfo("noncharacter-in-input-stream", _input, offset));
                }

                return;
            }

            _errorHandler!(new HtmlParseErrorInfo("surrogate-in-input-stream", _input, offset));
            return;
        }

        if (char.IsLowSurrogate((char)c))
        {
            if (offset > 0 && char.IsHighSurrogate(_input[offset - 1]))
                return;

            _errorHandler!(new HtmlParseErrorInfo("surrogate-in-input-stream", _input, offset));
            return;
        }

        if (c is (>= 0xFDD0 and <= 0xFDEF) or 0xFFFE or 0xFFFF)
        {
            _errorHandler!(new HtmlParseErrorInfo("noncharacter-in-input-stream", _input, offset));
            return;
        }

        if (c is ((>= 0x01 and <= 0x1F) and not ('\t' or '\n' or '\f' or '\r')) or (>= 0x7F and <= 0x9F))
        {
            _errorHandler!(new HtmlParseErrorInfo("control-character-in-input-stream", _input, offset));
        }
    }

    /// <summary>Reconsumes the current input character in the given state.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Reconsume(TokenizerState state)
    {
        _reconsume = true;
        _state = state;
    }

    /// <summary>Returns true when the next characters (not yet consumed) match <paramref name="value"/>.</summary>
    private bool NextCharactersAre(string value, bool ignoreCase)
    {
        if (_reconsume)
            return false;

        if (_limit - _position < value.Length)
        {
            if (_canSuspend)
            {
                Suspend();
            }

            return false;
        }

        var span = _input.AsSpan(_position, value.Length);
        return ignoreCase ? Ascii.EqualsIgnoreCase(span, value) : span.SequenceEqual(value);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Emitting
    // ----------------------------------------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EmitCharacter(char c)
    {
        if (_characterCount == _characters.Length)
        {
            Array.Resize(ref _characters, _characters.Length * 2);
        }

        _characters[_characterCount++] = c;
    }

    private void EmitCharacter(int codePoint)
    {
        if (codePoint < 0x10000)
        {
            EmitCharacter((char)codePoint);
            return;
        }

        Span<char> buffer = stackalloc char[2];
        new Rune(codePoint).EncodeToUtf16(buffer);
        EmitCharacter(buffer[0]);
        EmitCharacter(buffer[1]);
    }

    private void EmitCharacters(ReadOnlySpan<char> value)
    {
        if (_characterCount + value.Length > _characters.Length)
        {
            Array.Resize(ref _characters, Math.Max(_characters.Length * 2, _characterCount + value.Length));
        }

        value.CopyTo(_characters.AsSpan(_characterCount));
        _characterCount += value.Length;
    }

    private void EmitCharacters(StringBuilder value)
    {
        foreach (var chunk in value.GetChunks())
        {
            EmitCharacters(chunk.Span);
        }
    }

    private void EmitEndOfFile()
    {
        _pendingTokenType = HtmlTokenType.EndOfFile;
    }

    private void EmitComment()
    {
        _pendingComment = _commentData.ToString();
        _pendingTokenType = HtmlTokenType.Comment;
    }

    private void EmitDoctype()
    {
        _pendingDoctypeName = _doctypeNameMissing ? null : _doctypeName.ToString();
        _pendingDoctypePublicId = _doctypePublicIdMissing ? null : _doctypePublicId.ToString();
        _pendingDoctypeSystemId = _doctypeSystemIdMissing ? null : _doctypeSystemId.ToString();
        _pendingDoctypeForceQuirks = _forceQuirks;
        _pendingTokenType = HtmlTokenType.Doctype;
    }

    private void EmitProcessingInstruction()
    {
        _pendingPiTarget = _piTarget;
        _pendingPiData = _piData.ToString();
        _pendingTokenType = HtmlTokenType.ProcessingInstruction;
    }

    private void EmitTag()
    {
        FinishAttribute();
        var name = _nameTable.GetTagName(_tagName, out var tagId);
        var attributes = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_attributes);
        if (_isEndTag)
        {
            if (attributes.Length > 0)
            {
                ReportError("end-tag-with-attributes");
            }

            if (_selfClosing)
            {
                ReportError("end-tag-with-trailing-solidus");
            }

            _pendingTokenType = HtmlTokenType.EndTag;
        }
        else
        {
            _lastStartTagName = name;
            _pendingTokenType = HtmlTokenType.StartTag;
        }

        if (_freeTag is { } tag)
        {
            _freeTag = null;
            tag.Name = name;
            tag.TagId = tagId;
            tag.SetAttributes(attributes);
            tag.SelfClosing = _selfClosing;
            tag.SelfClosingAcknowledged = false;
            _pendingTag = tag;
        }
        else
        {
            _pendingTag = new HtmlTagToken(name, tagId, attributes.IsEmpty ? [] : attributes.ToArray(), _selfClosing);
        }

        _pendingTag.HadDuplicateAttribute = _tagHadDuplicateAttribute;
    }

    /// <summary>
    /// Called by the tree builder when it processed a tag token it does not keep: the token is reused for the next tag. A
    /// token is not released while it is processed, so a nested parse (e.g. document.write() from a script) uses other tokens.
    /// </summary>
    public void ReleaseTag(HtmlTagToken tag) => _freeTag = tag;

    private void CreateTagToken(bool isEndTag)
    {
        _isEndTag = isEndTag;
        _tagName.Clear();
        _selfClosing = false;
        _attributes.Clear();
        _hasCurrentAttribute = false;
        _tagHadDuplicateAttribute = false;
    }

    /// <summary>When leaving the attribute name state, the attribute is removed if another attribute has the same name.</summary>
    private void LeaveAttributeName()
    {
        _currentAttributeIsDuplicate = false;
        foreach (var attribute in _attributes)
        {
            if (_attributeName.Equals(attribute.Name))
            {
                ReportError("duplicate-attribute");
                _currentAttributeIsDuplicate = true;
                return;
            }
        }
    }

    private void StartAttribute()
    {
        FinishAttribute();
        _currentAttributeIsDuplicate = false;
        _attributeName.Clear();
        _attributeValue.Clear();
        _hasCurrentAttribute = true;
    }

    /// <summary>Adds the current attribute to the token, unless an attribute with the same name exists (duplicate-attribute).</summary>
    private void FinishAttribute()
    {
        if (!_hasCurrentAttribute)
            return;

        _hasCurrentAttribute = false;
        if (_currentAttributeIsDuplicate)
        {
            _tagHadDuplicateAttribute = true;
            return;
        }

        var name = _nameTable.GetAttributeName(_attributeName);

        // The classes repeat a lot in a document (unlike e.g. the URLs): their values are shared
        _attributes.Add(new HtmlTokenAttribute(name, ReferenceEquals(name, "class") ? _nameTable.GetAttributeValue(_attributeValue) : _attributeValue.ToString()));
    }

    private bool IsAppropriateEndTag() => _lastStartTagName is not null && _tagName.Equals(_lastStartTagName);

    /// <summary>Reports an error at the current input character (or at the end of the input for EOF).</summary>
    private void ReportError(string code)
    {
        if (_errorHandler is null)
            return;

        var offset = _current == Eof && !_reconsume ? _position : Math.Max(0, _position - 1);
        _errorHandler(new HtmlParseErrorInfo(code, _input, offset));
    }

    /// <summary>Reports an error at the next input character (used by the states that look ahead and by character references).</summary>
    private void ReportErrorAtNextCharacter(string code)
    {
        _errorHandler?.Invoke(new HtmlParseErrorInfo(code, _input, _reconsume && _current != Eof ? Math.Max(0, _position - 1) : _position));
    }

    private void CreateDoctypeToken()
    {
        _doctypeName.Clear();
        _doctypePublicId.Clear();
        _doctypeSystemId.Clear();
        _doctypeNameMissing = true;
        _doctypePublicIdMissing = true;
        _doctypeSystemIdMissing = true;
        _forceQuirks = false;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Character references
    // ----------------------------------------------------------------------------------------------------------------

    private bool IsConsumedAsPartOfAnAttribute => _returnState is TokenizerState.AttributeValueDoubleQuoted or TokenizerState.AttributeValueSingleQuoted or TokenizerState.AttributeValueUnquoted;

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#flush-code-points-consumed-as-a-character-reference</summary>
    private void FlushCodePointsConsumedAsCharacterReference()
    {
        if (IsConsumedAsPartOfAnAttribute)
        {
            _attributeValue.Append(_temporaryBuffer);
        }
        else
        {
            EmitCharacters(_temporaryBuffer);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // State machine
    // ----------------------------------------------------------------------------------------------------------------

    private void Step()
    {
        switch (_state)
        {
            case TokenizerState.Data:
            {
                if (!_reconsume)
                {
                    // Fast path: copy the characters that do not need special handling
                    var remaining = _input.AsSpan(_position, _limit - _position);
                    var index = FindStop(remaining, DataStopChars);
                    if (index < 0)
                    {
                        index = remaining.Length;
                    }

                    if (index > 0)
                    {
                        EmitCharacters(remaining[..index]);
                        _position += index;
                        return;
                    }
                }

                var c = Consume();
                switch (c)
                {
                    case '&':
                        _returnState = TokenizerState.Data;
                        _state = TokenizerState.CharacterReference;
                        break;
                    case '<':
                        _tagStart = _position - 1;
                        _state = TokenizerState.TagOpen;
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        EmitCharacter('\0');
                        break;
                    case Eof:
                        EmitEndOfFile();
                        break;
                    default:
                        EmitCharacter((char)c);
                        break;
                }

                break;
            }

            case TokenizerState.Rcdata:
            {
                if (!_reconsume)
                {
                    var remaining = _input.AsSpan(_position, _limit - _position);
                    var index = FindStop(remaining, DataStopChars);
                    if (index < 0)
                    {
                        index = remaining.Length;
                    }

                    if (index > 0)
                    {
                        EmitCharacters(remaining[..index]);
                        _position += index;
                        return;
                    }
                }

                var c = Consume();
                switch (c)
                {
                    case '&':
                        _returnState = TokenizerState.Rcdata;
                        _state = TokenizerState.CharacterReference;
                        break;
                    case '<':
                        _tagStart = _position - 1;
                        _state = TokenizerState.RcdataLessThanSign;
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        EmitCharacter('\U0000FFFD');
                        break;
                    case Eof:
                        EmitEndOfFile();
                        break;
                    default:
                        EmitCharacter((char)c);
                        break;
                }

                break;
            }

            case TokenizerState.Rawtext:
            case TokenizerState.ScriptData:
            {
                if (!_reconsume)
                {
                    var remaining = _input.AsSpan(_position, _limit - _position);
                    var index = FindStop(remaining, RawTextStopChars);
                    if (index < 0)
                    {
                        index = remaining.Length;
                    }

                    if (index > 0)
                    {
                        EmitCharacters(remaining[..index]);
                        _position += index;
                        return;
                    }
                }

                var c = Consume();
                switch (c)
                {
                    case '<':
                        _tagStart = _position - 1;
                        _state = _state == TokenizerState.Rawtext ? TokenizerState.RawtextLessThanSign : TokenizerState.ScriptDataLessThanSign;
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        EmitCharacter('\U0000FFFD');
                        break;
                    case Eof:
                        EmitEndOfFile();
                        break;
                    default:
                        EmitCharacter((char)c);
                        break;
                }

                break;
            }

            case TokenizerState.Plaintext:
            {
                if (!_reconsume)
                {
                    var remaining = _input.AsSpan(_position, _limit - _position);
                    var index = FindStop(remaining, PlainTextStopChars);
                    if (index < 0)
                    {
                        index = remaining.Length;
                    }

                    if (index > 0)
                    {
                        EmitCharacters(remaining[..index]);
                        _position += index;
                        return;
                    }
                }

                var c = Consume();
                switch (c)
                {
                    case '\0':
                        ReportError("unexpected-null-character");
                        EmitCharacter('\U0000FFFD');
                        break;
                    case Eof:
                        EmitEndOfFile();
                        break;
                    default:
                        EmitCharacter((char)c);
                        break;
                }

                break;
            }

            case TokenizerState.TagOpen:
            {
                var c = Consume();
                switch (c)
                {
                    case '!':
                        _state = TokenizerState.MarkupDeclarationOpen;
                        break;
                    case '/':
                        _state = TokenizerState.EndTagOpen;
                        break;
                    case '?':
                        _temporaryBuffer.Clear();
                        _state = TokenizerState.ProcessingInstructionOpen;
                        break;
                    case Eof:
                        ReportError("eof-before-tag-name");
                        EmitCharacter('<');
                        EmitEndOfFile();
                        break;
                    default:
                        if (char.IsAsciiLetter((char)c))
                        {
                            CreateTagToken(isEndTag: false);
                            Reconsume(TokenizerState.TagName);
                        }
                        else
                        {
                            ReportError("invalid-first-character-of-tag-name");
                            EmitCharacter('<');
                            Reconsume(TokenizerState.Data);
                        }

                        break;
                }

                break;
            }

            case TokenizerState.EndTagOpen:
            {
                var c = Consume();
                if (c != Eof && char.IsAsciiLetter((char)c))
                {
                    CreateTagToken(isEndTag: true);
                    Reconsume(TokenizerState.TagName);
                }
                else if (c == '>')
                {
                    ReportError("missing-end-tag-name");
                    _state = TokenizerState.Data;
                }
                else if (c == Eof)
                {
                    ReportError("eof-before-tag-name");
                    EmitCharacter('<');
                    EmitCharacter('/');
                    EmitEndOfFile();
                }
                else
                {
                    ReportError("invalid-first-character-of-tag-name");
                    _commentData.Clear();
                    Reconsume(TokenizerState.BogusComment);
                }

                break;
            }

            case TokenizerState.TagName:
            {
                if (!_reconsume)
                {
                    // Fast path: append the name characters that do not need special handling
                    var remaining = _input.AsSpan(_position, _limit - _position);
                    var index = FindStop(remaining, TagNameStopChars);
                    if (index < 0)
                    {
                        index = remaining.Length;
                    }

                    if (index > 0)
                    {
                        _tagName.Append(remaining[..index]);
                        _position += index;
                        return;
                    }
                }

                var c = Consume();
                switch (c)
                {
                    case '\t' or '\n' or '\f' or ' ':
                        _state = TokenizerState.BeforeAttributeName;
                        break;
                    case '/':
                        _state = TokenizerState.SelfClosingStartTag;
                        break;
                    case '>':
                        _state = TokenizerState.Data;
                        EmitTag();
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        _tagName.Append('\U0000FFFD');
                        break;
                    case Eof:
                        ReportError("eof-in-tag");
                        EmitEndOfFile();
                        break;
                    default:
                        _tagName.Append(ToAsciiLower((char)c));
                        break;
                }

                break;
            }

            case TokenizerState.RcdataLessThanSign:
                LessThanSign(TokenizerState.RcdataEndTagOpen, TokenizerState.Rcdata);
                break;

            case TokenizerState.RcdataEndTagOpen:
                EndTagOpen(TokenizerState.RcdataEndTagName, TokenizerState.Rcdata);
                break;

            case TokenizerState.RcdataEndTagName:
                EndTagName(TokenizerState.Rcdata);
                break;

            case TokenizerState.RawtextLessThanSign:
                LessThanSign(TokenizerState.RawtextEndTagOpen, TokenizerState.Rawtext);
                break;

            case TokenizerState.RawtextEndTagOpen:
                EndTagOpen(TokenizerState.RawtextEndTagName, TokenizerState.Rawtext);
                break;

            case TokenizerState.RawtextEndTagName:
                EndTagName(TokenizerState.Rawtext);
                break;

            case TokenizerState.ScriptDataLessThanSign:
            {
                var c = Consume();
                if (c == '/')
                {
                    _temporaryBuffer.Clear();
                    _state = TokenizerState.ScriptDataEndTagOpen;
                }
                else if (c == '!')
                {
                    _state = TokenizerState.ScriptDataEscapeStart;
                    EmitCharacter('<');
                    EmitCharacter('!');
                }
                else
                {
                    EmitCharacter('<');
                    Reconsume(TokenizerState.ScriptData);
                }

                break;
            }

            case TokenizerState.ScriptDataEndTagOpen:
                EndTagOpen(TokenizerState.ScriptDataEndTagName, TokenizerState.ScriptData);
                break;

            case TokenizerState.ScriptDataEndTagName:
                EndTagName(TokenizerState.ScriptData);
                break;

            case TokenizerState.ScriptDataEscapeStart:
            {
                var c = Consume();
                if (c == '-')
                {
                    _state = TokenizerState.ScriptDataEscapeStartDash;
                    EmitCharacter('-');
                }
                else
                {
                    Reconsume(TokenizerState.ScriptData);
                }

                break;
            }

            case TokenizerState.ScriptDataEscapeStartDash:
            {
                var c = Consume();
                if (c == '-')
                {
                    _state = TokenizerState.ScriptDataEscapedDashDash;
                    EmitCharacter('-');
                }
                else
                {
                    Reconsume(TokenizerState.ScriptData);
                }

                break;
            }

            case TokenizerState.ScriptDataEscaped:
            {
                var c = Consume();
                switch (c)
                {
                    case '-':
                        _state = TokenizerState.ScriptDataEscapedDash;
                        EmitCharacter('-');
                        break;
                    case '<':
                        _state = TokenizerState.ScriptDataEscapedLessThanSign;
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        EmitCharacter('\U0000FFFD');
                        break;
                    case Eof:
                        ReportError("eof-in-script-html-comment-like-text");
                        EmitEndOfFile();
                        break;
                    default:
                        EmitCharacter((char)c);
                        break;
                }

                break;
            }

            case TokenizerState.ScriptDataEscapedDash:
            {
                var c = Consume();
                switch (c)
                {
                    case '-':
                        _state = TokenizerState.ScriptDataEscapedDashDash;
                        EmitCharacter('-');
                        break;
                    case '<':
                        _state = TokenizerState.ScriptDataEscapedLessThanSign;
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        _state = TokenizerState.ScriptDataEscaped;
                        EmitCharacter('\U0000FFFD');
                        break;
                    case Eof:
                        ReportError("eof-in-script-html-comment-like-text");
                        EmitEndOfFile();
                        break;
                    default:
                        _state = TokenizerState.ScriptDataEscaped;
                        EmitCharacter((char)c);
                        break;
                }

                break;
            }

            case TokenizerState.ScriptDataEscapedDashDash:
            {
                var c = Consume();
                switch (c)
                {
                    case '-':
                        EmitCharacter('-');
                        break;
                    case '<':
                        _state = TokenizerState.ScriptDataEscapedLessThanSign;
                        break;
                    case '>':
                        _state = TokenizerState.ScriptData;
                        EmitCharacter('>');
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        _state = TokenizerState.ScriptDataEscaped;
                        EmitCharacter('\U0000FFFD');
                        break;
                    case Eof:
                        ReportError("eof-in-script-html-comment-like-text");
                        EmitEndOfFile();
                        break;
                    default:
                        _state = TokenizerState.ScriptDataEscaped;
                        EmitCharacter((char)c);
                        break;
                }

                break;
            }

            case TokenizerState.ScriptDataEscapedLessThanSign:
            {
                var c = Consume();
                if (c == '/')
                {
                    _temporaryBuffer.Clear();
                    _state = TokenizerState.ScriptDataEscapedEndTagOpen;
                }
                else if (c != Eof && char.IsAsciiLetter((char)c))
                {
                    _temporaryBuffer.Clear();
                    EmitCharacter('<');
                    Reconsume(TokenizerState.ScriptDataDoubleEscapeStart);
                }
                else
                {
                    EmitCharacter('<');
                    Reconsume(TokenizerState.ScriptDataEscaped);
                }

                break;
            }

            case TokenizerState.ScriptDataEscapedEndTagOpen:
                EndTagOpen(TokenizerState.ScriptDataEscapedEndTagName, TokenizerState.ScriptDataEscaped);
                break;

            case TokenizerState.ScriptDataEscapedEndTagName:
                EndTagName(TokenizerState.ScriptDataEscaped);
                break;

            case TokenizerState.ScriptDataDoubleEscapeStart:
            {
                var c = Consume();
                if (c is '\t' or '\n' or '\f' or ' ' or '/' or '>')
                {
                    _state = _temporaryBuffer.Equals("script") ? TokenizerState.ScriptDataDoubleEscaped : TokenizerState.ScriptDataEscaped;
                    EmitCharacter((char)c);
                }
                else if (c != Eof && char.IsAsciiLetter((char)c))
                {
                    _temporaryBuffer.Append(ToAsciiLower((char)c));
                    EmitCharacter((char)c);
                }
                else
                {
                    Reconsume(TokenizerState.ScriptDataEscaped);
                }

                break;
            }

            case TokenizerState.ScriptDataDoubleEscaped:
            {
                var c = Consume();
                switch (c)
                {
                    case '-':
                        _state = TokenizerState.ScriptDataDoubleEscapedDash;
                        EmitCharacter('-');
                        break;
                    case '<':
                        _state = TokenizerState.ScriptDataDoubleEscapedLessThanSign;
                        EmitCharacter('<');
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        EmitCharacter('\U0000FFFD');
                        break;
                    case Eof:
                        ReportError("eof-in-script-html-comment-like-text");
                        EmitEndOfFile();
                        break;
                    default:
                        EmitCharacter((char)c);
                        break;
                }

                break;
            }

            case TokenizerState.ScriptDataDoubleEscapedDash:
            {
                var c = Consume();
                switch (c)
                {
                    case '-':
                        _state = TokenizerState.ScriptDataDoubleEscapedDashDash;
                        EmitCharacter('-');
                        break;
                    case '<':
                        _state = TokenizerState.ScriptDataDoubleEscapedLessThanSign;
                        EmitCharacter('<');
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        _state = TokenizerState.ScriptDataDoubleEscaped;
                        EmitCharacter('\U0000FFFD');
                        break;
                    case Eof:
                        ReportError("eof-in-script-html-comment-like-text");
                        EmitEndOfFile();
                        break;
                    default:
                        _state = TokenizerState.ScriptDataDoubleEscaped;
                        EmitCharacter((char)c);
                        break;
                }

                break;
            }

            case TokenizerState.ScriptDataDoubleEscapedDashDash:
            {
                var c = Consume();
                switch (c)
                {
                    case '-':
                        EmitCharacter('-');
                        break;
                    case '<':
                        _state = TokenizerState.ScriptDataDoubleEscapedLessThanSign;
                        EmitCharacter('<');
                        break;
                    case '>':
                        _state = TokenizerState.ScriptData;
                        EmitCharacter('>');
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        _state = TokenizerState.ScriptDataDoubleEscaped;
                        EmitCharacter('\U0000FFFD');
                        break;
                    case Eof:
                        ReportError("eof-in-script-html-comment-like-text");
                        EmitEndOfFile();
                        break;
                    default:
                        _state = TokenizerState.ScriptDataDoubleEscaped;
                        EmitCharacter((char)c);
                        break;
                }

                break;
            }

            case TokenizerState.ScriptDataDoubleEscapedLessThanSign:
            {
                var c = Consume();
                if (c == '/')
                {
                    _temporaryBuffer.Clear();
                    _state = TokenizerState.ScriptDataDoubleEscapeEnd;
                    EmitCharacter('/');
                }
                else
                {
                    Reconsume(TokenizerState.ScriptDataDoubleEscaped);
                }

                break;
            }

            case TokenizerState.ScriptDataDoubleEscapeEnd:
            {
                var c = Consume();
                if (c is '\t' or '\n' or '\f' or ' ' or '/' or '>')
                {
                    _state = _temporaryBuffer.Equals("script") ? TokenizerState.ScriptDataEscaped : TokenizerState.ScriptDataDoubleEscaped;
                    EmitCharacter((char)c);
                }
                else if (c != Eof && char.IsAsciiLetter((char)c))
                {
                    _temporaryBuffer.Append(ToAsciiLower((char)c));
                    EmitCharacter((char)c);
                }
                else
                {
                    Reconsume(TokenizerState.ScriptDataDoubleEscaped);
                }

                break;
            }

            case TokenizerState.BeforeAttributeName:
            {
                var c = Consume();
                switch (c)
                {
                    case '\t' or '\n' or '\f' or ' ':
                        break;
                    case '/' or '>' or Eof:
                        Reconsume(TokenizerState.AfterAttributeName);
                        break;
                    case '=':
                        ReportError("unexpected-equals-sign-before-attribute-name");
                        StartAttribute();
                        _attributeName.Append('=');
                        _state = TokenizerState.AttributeName;
                        break;
                    default:
                        StartAttribute();
                        Reconsume(TokenizerState.AttributeName);
                        break;
                }

                break;
            }

            case TokenizerState.AttributeName:
            {
                if (!_reconsume)
                {
                    var remaining = _input.AsSpan(_position, _limit - _position);
                    var index = FindStop(remaining, AttributeNameStopChars);
                    if (index < 0)
                    {
                        index = remaining.Length;
                    }

                    if (index > 0)
                    {
                        _attributeName.Append(remaining[..index]);
                        _position += index;
                        return;
                    }
                }

                var c = Consume();
                switch (c)
                {
                    case '\t' or '\n' or '\f' or ' ' or '/' or '>' or Eof:
                        Reconsume(TokenizerState.AfterAttributeName);
                        LeaveAttributeName();
                        break;
                    case '=':
                        _state = TokenizerState.BeforeAttributeValue;
                        LeaveAttributeName();
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        _attributeName.Append('\U0000FFFD');
                        break;
                    case '"' or '\'' or '<':
                        ReportError("unexpected-character-in-attribute-name");
                        _attributeName.Append((char)c);
                        break;
                    default:
                        _attributeName.Append(ToAsciiLower((char)c));
                        break;
                }

                break;
            }

            case TokenizerState.AfterAttributeName:
            {
                var c = Consume();
                switch (c)
                {
                    case '\t' or '\n' or '\f' or ' ':
                        break;
                    case '/':
                        _state = TokenizerState.SelfClosingStartTag;
                        break;
                    case '=':
                        _state = TokenizerState.BeforeAttributeValue;
                        break;
                    case '>':
                        _state = TokenizerState.Data;
                        EmitTag();
                        break;
                    case Eof:
                        ReportError("eof-in-tag");
                        EmitEndOfFile();
                        break;
                    default:
                        StartAttribute();
                        Reconsume(TokenizerState.AttributeName);
                        break;
                }

                break;
            }

            case TokenizerState.BeforeAttributeValue:
            {
                var c = Consume();
                switch (c)
                {
                    case '\t' or '\n' or '\f' or ' ':
                        break;
                    case '"':
                        _state = TokenizerState.AttributeValueDoubleQuoted;
                        break;
                    case '\'':
                        _state = TokenizerState.AttributeValueSingleQuoted;
                        break;
                    case '>':
                        ReportError("missing-attribute-value");
                        _state = TokenizerState.Data;
                        EmitTag();
                        break;
                    default:
                        Reconsume(TokenizerState.AttributeValueUnquoted);
                        break;
                }

                break;
            }

            case TokenizerState.AttributeValueDoubleQuoted:
            case TokenizerState.AttributeValueSingleQuoted:
            {
                var quote = _state == TokenizerState.AttributeValueDoubleQuoted ? '"' : '\'';
                if (!_reconsume)
                {
                    var remaining = _input.AsSpan(_position, _limit - _position);
                    var index = FindStop(remaining, quote == '"' ? DoubleQuotedStopChars : SingleQuotedStopChars);
                    if (index < 0)
                    {
                        index = remaining.Length;
                    }

                    if (index > 0)
                    {
                        _attributeValue.Append(remaining[..index]);
                        _position += index;
                        return;
                    }
                }

                var c = Consume();
                if (c == quote)
                {
                    _state = TokenizerState.AfterAttributeValueQuoted;
                }
                else if (c == '&')
                {
                    _returnState = _state;
                    _state = TokenizerState.CharacterReference;
                }
                else if (c == '\0')
                {
                    ReportError("unexpected-null-character");
                    _attributeValue.Append('\U0000FFFD');
                }
                else if (c == Eof)
                {
                    ReportError("eof-in-tag");
                    EmitEndOfFile();
                }
                else
                {
                    _attributeValue.Append((char)c);
                }

                break;
            }

            case TokenizerState.AttributeValueUnquoted:
            {
                if (!_reconsume)
                {
                    var remaining = _input.AsSpan(_position, _limit - _position);
                    var index = FindStop(remaining, UnquotedStopChars);
                    if (index < 0)
                    {
                        index = remaining.Length;
                    }

                    if (index > 0)
                    {
                        _attributeValue.Append(remaining[..index]);
                        _position += index;
                        return;
                    }
                }

                var c = Consume();
                switch (c)
                {
                    case '\t' or '\n' or '\f' or ' ':
                        _state = TokenizerState.BeforeAttributeName;
                        break;
                    case '&':
                        _returnState = TokenizerState.AttributeValueUnquoted;
                        _state = TokenizerState.CharacterReference;
                        break;
                    case '>':
                        _state = TokenizerState.Data;
                        EmitTag();
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        _attributeValue.Append('\U0000FFFD');
                        break;
                    case '"' or '\'' or '<' or '=' or '`':
                        ReportError("unexpected-character-in-unquoted-attribute-value");
                        _attributeValue.Append((char)c);
                        break;
                    case Eof:
                        ReportError("eof-in-tag");
                        EmitEndOfFile();
                        break;
                    default:
                        _attributeValue.Append((char)c);
                        break;
                }

                break;
            }

            case TokenizerState.AfterAttributeValueQuoted:
            {
                var c = Consume();
                switch (c)
                {
                    case '\t' or '\n' or '\f' or ' ':
                        _state = TokenizerState.BeforeAttributeName;
                        break;
                    case '/':
                        _state = TokenizerState.SelfClosingStartTag;
                        break;
                    case '>':
                        _state = TokenizerState.Data;
                        EmitTag();
                        break;
                    case Eof:
                        ReportError("eof-in-tag");
                        EmitEndOfFile();
                        break;
                    default:
                        ReportError("missing-whitespace-between-attributes");
                        Reconsume(TokenizerState.BeforeAttributeName);
                        break;
                }

                break;
            }

            case TokenizerState.SelfClosingStartTag:
            {
                var c = Consume();
                switch (c)
                {
                    case '>':
                        _selfClosing = true;
                        _state = TokenizerState.Data;
                        EmitTag();
                        break;
                    case Eof:
                        ReportError("eof-in-tag");
                        EmitEndOfFile();
                        break;
                    default:
                        ReportError("unexpected-solidus-in-tag");
                        Reconsume(TokenizerState.BeforeAttributeName);
                        break;
                }

                break;
            }

            case TokenizerState.BogusComment:
            {
                var c = Consume();
                switch (c)
                {
                    case '>':
                        _state = TokenizerState.Data;
                        EmitComment();
                        break;
                    case Eof:
                        EmitComment();
                        EmitEndOfFileAfterPendingToken();
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        _commentData.Append('\U0000FFFD');
                        break;
                    default:
                        _commentData.Append((char)c);
                        break;
                }

                break;
            }

            case TokenizerState.MarkupDeclarationOpen:
            {
                if (NextCharactersAre("--", ignoreCase: false))
                {
                    _position += 2;
                    _commentData.Clear();
                    _state = TokenizerState.CommentStart;
                }
                else if (NextCharactersAre("DOCTYPE", ignoreCase: true))
                {
                    _position += 7;
                    _state = TokenizerState.Doctype;
                }
                else if (NextCharactersAre("[CDATA[", ignoreCase: false))
                {
                    _position += 7;
                    if (IsAdjustedCurrentNodeInForeignContent?.Invoke() == true)
                    {
                        _state = TokenizerState.CdataSection;
                    }
                    else
                    {
                        ReportError("cdata-in-html-content");
                        _commentData.Clear();
                        _commentData.Append("[CDATA[");
                        _state = TokenizerState.BogusComment;
                    }
                }
                else
                {
                    // Input stream errors of the next character are reported before this error, as the input stream is preprocessed first
                    if (_errorHandler is not null && _position < _limit)
                    {
                        var next = _input[_position];
                        _position++;
                        ReportInputStreamError(next);
                        _position--;
                        _streamErrorsReportedUpTo = _position + 1;
                    }

                    ReportErrorAtNextCharacter("incorrectly-opened-comment");
                    _commentData.Clear();
                    _state = TokenizerState.BogusComment;
                }

                break;
            }

            case TokenizerState.CommentStart:
            {
                var c = Consume();
                if (c == '-')
                {
                    _state = TokenizerState.CommentStartDash;
                }
                else if (c == '>')
                {
                    ReportError("abrupt-closing-of-empty-comment");
                    _state = TokenizerState.Data;
                    EmitComment();
                }
                else
                {
                    Reconsume(TokenizerState.Comment);
                }

                break;
            }

            case TokenizerState.CommentStartDash:
            {
                var c = Consume();
                switch (c)
                {
                    case '-':
                        _state = TokenizerState.CommentEnd;
                        break;
                    case '>':
                        ReportError("abrupt-closing-of-empty-comment");
                        _state = TokenizerState.Data;
                        EmitComment();
                        break;
                    case Eof:
                        ReportError("eof-in-comment");
                        EmitComment();
                        EmitEndOfFileAfterPendingToken();
                        break;
                    default:
                        _commentData.Append('-');
                        Reconsume(TokenizerState.Comment);
                        break;
                }

                break;
            }

            case TokenizerState.Comment:
            {
                if (!_reconsume)
                {
                    var remaining = _input.AsSpan(_position, _limit - _position);
                    var index = FindStop(remaining, CommentStopChars);
                    if (index < 0)
                    {
                        index = remaining.Length;
                    }

                    if (index > 0)
                    {
                        _commentData.Append(remaining[..index]);
                        _position += index;
                        return;
                    }
                }

                var c = Consume();
                switch (c)
                {
                    case '<':
                        _commentData.Append('<');
                        _state = TokenizerState.CommentLessThanSign;
                        break;
                    case '-':
                        _state = TokenizerState.CommentEndDash;
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        _commentData.Append('\U0000FFFD');
                        break;
                    case Eof:
                        ReportError("eof-in-comment");
                        EmitComment();
                        EmitEndOfFileAfterPendingToken();
                        break;
                    default:
                        _commentData.Append((char)c);
                        break;
                }

                break;
            }

            case TokenizerState.CommentLessThanSign:
            {
                var c = Consume();
                if (c == '!')
                {
                    _commentData.Append('!');
                    _state = TokenizerState.CommentLessThanSignBang;
                }
                else if (c == '<')
                {
                    _commentData.Append('<');
                }
                else
                {
                    Reconsume(TokenizerState.Comment);
                }

                break;
            }

            case TokenizerState.CommentLessThanSignBang:
            {
                var c = Consume();
                if (c == '-')
                {
                    _state = TokenizerState.CommentLessThanSignBangDash;
                }
                else
                {
                    Reconsume(TokenizerState.Comment);
                }

                break;
            }

            case TokenizerState.CommentLessThanSignBangDash:
            {
                var c = Consume();
                if (c == '-')
                {
                    _state = TokenizerState.CommentLessThanSignBangDashDash;
                }
                else
                {
                    Reconsume(TokenizerState.CommentEndDash);
                }

                break;
            }

            case TokenizerState.CommentLessThanSignBangDashDash:
            {
                var c = Consume();
                if (c is '>' or Eof)
                {
                    Reconsume(TokenizerState.CommentEnd);
                }
                else
                {
                    ReportError("nested-comment");
                    Reconsume(TokenizerState.CommentEnd);
                }

                break;
            }

            case TokenizerState.CommentEndDash:
            {
                var c = Consume();
                switch (c)
                {
                    case '-':
                        _state = TokenizerState.CommentEnd;
                        break;
                    case Eof:
                        ReportError("eof-in-comment");
                        EmitComment();
                        EmitEndOfFileAfterPendingToken();
                        break;
                    default:
                        _commentData.Append('-');
                        Reconsume(TokenizerState.Comment);
                        break;
                }

                break;
            }

            case TokenizerState.CommentEnd:
            {
                var c = Consume();
                switch (c)
                {
                    case '>':
                        _state = TokenizerState.Data;
                        EmitComment();
                        break;
                    case '!':
                        _state = TokenizerState.CommentEndBang;
                        break;
                    case '-':
                        _commentData.Append('-');
                        break;
                    case Eof:
                        ReportError("eof-in-comment");
                        EmitComment();
                        EmitEndOfFileAfterPendingToken();
                        break;
                    default:
                        _commentData.Append("--");
                        Reconsume(TokenizerState.Comment);
                        break;
                }

                break;
            }

            case TokenizerState.CommentEndBang:
            {
                var c = Consume();
                switch (c)
                {
                    case '-':
                        _commentData.Append("--!");
                        _state = TokenizerState.CommentEndDash;
                        break;
                    case '>':
                        ReportError("incorrectly-closed-comment");
                        _state = TokenizerState.Data;
                        EmitComment();
                        break;
                    case Eof:
                        ReportError("eof-in-comment");
                        EmitComment();
                        EmitEndOfFileAfterPendingToken();
                        break;
                    default:
                        _commentData.Append("--!");
                        Reconsume(TokenizerState.Comment);
                        break;
                }

                break;
            }

            case TokenizerState.Doctype:
            {
                var c = Consume();
                switch (c)
                {
                    case '\t' or '\n' or '\f' or ' ':
                        _state = TokenizerState.BeforeDoctypeName;
                        break;
                    case '>':
                        Reconsume(TokenizerState.BeforeDoctypeName);
                        break;
                    case Eof:
                        ReportError("eof-in-doctype");
                        CreateDoctypeToken();
                        _forceQuirks = true;
                        EmitDoctype();
                        EmitEndOfFileAfterPendingToken();
                        break;
                    default:
                        ReportError("missing-whitespace-before-doctype-name");
                        Reconsume(TokenizerState.BeforeDoctypeName);
                        break;
                }

                break;
            }

            case TokenizerState.BeforeDoctypeName:
            {
                var c = Consume();
                switch (c)
                {
                    case '\t' or '\n' or '\f' or ' ':
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        CreateDoctypeToken();
                        _doctypeNameMissing = false;
                        _doctypeName.Append('\U0000FFFD');
                        _state = TokenizerState.DoctypeName;
                        break;
                    case '>':
                        ReportError("missing-doctype-name");
                        CreateDoctypeToken();
                        _forceQuirks = true;
                        _state = TokenizerState.Data;
                        EmitDoctype();
                        break;
                    case Eof:
                        ReportError("eof-in-doctype");
                        CreateDoctypeToken();
                        _forceQuirks = true;
                        EmitDoctype();
                        EmitEndOfFileAfterPendingToken();
                        break;
                    default:
                        CreateDoctypeToken();
                        _doctypeNameMissing = false;
                        _doctypeName.Append(ToAsciiLower((char)c));
                        _state = TokenizerState.DoctypeName;
                        break;
                }

                break;
            }

            case TokenizerState.DoctypeName:
            {
                var c = Consume();
                switch (c)
                {
                    case '\t' or '\n' or '\f' or ' ':
                        _state = TokenizerState.AfterDoctypeName;
                        break;
                    case '>':
                        _state = TokenizerState.Data;
                        EmitDoctype();
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        _doctypeName.Append('\U0000FFFD');
                        break;
                    case Eof:
                        ReportError("eof-in-doctype");
                        _forceQuirks = true;
                        EmitDoctype();
                        EmitEndOfFileAfterPendingToken();
                        break;
                    default:
                        _doctypeName.Append(ToAsciiLower((char)c));
                        break;
                }

                break;
            }

            case TokenizerState.AfterDoctypeName:
            {
                var c = Consume();
                switch (c)
                {
                    case '\t' or '\n' or '\f' or ' ':
                        break;
                    case '>':
                        _state = TokenizerState.Data;
                        EmitDoctype();
                        break;
                    case Eof:
                        ReportError("eof-in-doctype");
                        _forceQuirks = true;
                        EmitDoctype();
                        EmitEndOfFileAfterPendingToken();
                        break;
                    default:
                        // The current input character is the first of the six characters
                        if (MatchesKeywordFromCurrentCharacter("PUBLIC"))
                        {
                            _position += 5;
                            _state = TokenizerState.AfterDoctypePublicKeyword;
                        }
                        else if (MatchesKeywordFromCurrentCharacter("SYSTEM"))
                        {
                            _position += 5;
                            _state = TokenizerState.AfterDoctypeSystemKeyword;
                        }
                        else
                        {
                            ReportError("invalid-character-sequence-after-doctype-name");
                            _forceQuirks = true;
                            Reconsume(TokenizerState.BogusDoctype);
                        }

                        break;
                }

                break;
            }

            case TokenizerState.AfterDoctypePublicKeyword:
                AfterDoctypeKeyword(isPublic: true);
                break;

            case TokenizerState.BeforeDoctypePublicIdentifier:
                BeforeDoctypeIdentifier(isPublic: true);
                break;

            case TokenizerState.DoctypePublicIdentifierDoubleQuoted:
                DoctypeIdentifierQuoted(isPublic: true, '"');
                break;

            case TokenizerState.DoctypePublicIdentifierSingleQuoted:
                DoctypeIdentifierQuoted(isPublic: true, '\'');
                break;

            case TokenizerState.AfterDoctypePublicIdentifier:
            {
                var c = Consume();
                switch (c)
                {
                    case '\t' or '\n' or '\f' or ' ':
                        _state = TokenizerState.BetweenDoctypePublicAndSystemIdentifiers;
                        break;
                    case '>':
                        _state = TokenizerState.Data;
                        EmitDoctype();
                        break;
                    case '"':
                        ReportError("missing-whitespace-between-doctype-public-and-system-identifiers");
                        _doctypeSystemIdMissing = false;
                        _state = TokenizerState.DoctypeSystemIdentifierDoubleQuoted;
                        break;
                    case '\'':
                        ReportError("missing-whitespace-between-doctype-public-and-system-identifiers");
                        _doctypeSystemIdMissing = false;
                        _state = TokenizerState.DoctypeSystemIdentifierSingleQuoted;
                        break;
                    case Eof:
                        ReportError("eof-in-doctype");
                        _forceQuirks = true;
                        EmitDoctype();
                        EmitEndOfFileAfterPendingToken();
                        break;
                    default:
                        ReportError("missing-quote-before-doctype-system-identifier");
                        _forceQuirks = true;
                        Reconsume(TokenizerState.BogusDoctype);
                        break;
                }

                break;
            }

            case TokenizerState.BetweenDoctypePublicAndSystemIdentifiers:
            {
                var c = Consume();
                switch (c)
                {
                    case '\t' or '\n' or '\f' or ' ':
                        break;
                    case '>':
                        _state = TokenizerState.Data;
                        EmitDoctype();
                        break;
                    case '"':
                        _doctypeSystemIdMissing = false;
                        _state = TokenizerState.DoctypeSystemIdentifierDoubleQuoted;
                        break;
                    case '\'':
                        _doctypeSystemIdMissing = false;
                        _state = TokenizerState.DoctypeSystemIdentifierSingleQuoted;
                        break;
                    case Eof:
                        ReportError("eof-in-doctype");
                        _forceQuirks = true;
                        EmitDoctype();
                        EmitEndOfFileAfterPendingToken();
                        break;
                    default:
                        ReportError("missing-quote-before-doctype-system-identifier");
                        _forceQuirks = true;
                        Reconsume(TokenizerState.BogusDoctype);
                        break;
                }

                break;
            }

            case TokenizerState.AfterDoctypeSystemKeyword:
                AfterDoctypeKeyword(isPublic: false);
                break;

            case TokenizerState.BeforeDoctypeSystemIdentifier:
                BeforeDoctypeIdentifier(isPublic: false);
                break;

            case TokenizerState.DoctypeSystemIdentifierDoubleQuoted:
                DoctypeIdentifierQuoted(isPublic: false, '"');
                break;

            case TokenizerState.DoctypeSystemIdentifierSingleQuoted:
                DoctypeIdentifierQuoted(isPublic: false, '\'');
                break;

            case TokenizerState.AfterDoctypeSystemIdentifier:
            {
                var c = Consume();
                switch (c)
                {
                    case '\t' or '\n' or '\f' or ' ':
                        break;
                    case '>':
                        _state = TokenizerState.Data;
                        EmitDoctype();
                        break;
                    case Eof:
                        ReportError("eof-in-doctype");
                        _forceQuirks = true;
                        EmitDoctype();
                        EmitEndOfFileAfterPendingToken();
                        break;
                    default:
                        ReportError("unexpected-character-after-doctype-system-identifier");
                        Reconsume(TokenizerState.BogusDoctype);
                        break;
                }

                break;
            }

            case TokenizerState.BogusDoctype:
            {
                var c = Consume();
                switch (c)
                {
                    case '>':
                        _state = TokenizerState.Data;
                        EmitDoctype();
                        break;
                    case '\0':
                        ReportError("unexpected-null-character");
                        break;
                    case Eof:
                        EmitDoctype();
                        EmitEndOfFileAfterPendingToken();
                        break;
                    default:
                        break;
                }

                break;
            }

            case TokenizerState.CdataSection:
            {
                var c = Consume();
                switch (c)
                {
                    case ']':
                        _state = TokenizerState.CdataSectionBracket;
                        break;
                    case Eof:
                        ReportError("eof-in-cdata");
                        EmitEndOfFile();
                        break;
                    default:
                        EmitCharacter((char)c);
                        break;
                }

                break;
            }

            case TokenizerState.CdataSectionBracket:
            {
                var c = Consume();
                if (c == ']')
                {
                    _state = TokenizerState.CdataSectionEnd;
                }
                else
                {
                    EmitCharacter(']');
                    Reconsume(TokenizerState.CdataSection);
                }

                break;
            }

            case TokenizerState.CdataSectionEnd:
            {
                var c = Consume();
                if (c == ']')
                {
                    EmitCharacter(']');
                }
                else if (c == '>')
                {
                    _state = TokenizerState.Data;
                }
                else
                {
                    EmitCharacter(']');
                    EmitCharacter(']');
                    Reconsume(TokenizerState.CdataSection);
                }

                break;
            }

            case TokenizerState.ProcessingInstructionOpen:
            {
                var c = Consume();
                if (c == Eof)
                {
                    ReportError("eof-in-processing-instruction");
                    EmitEndOfFile();
                }
                else if (char.IsAsciiLetter((char)c) || c == '_')
                {
                    Reconsume(TokenizerState.ProcessingInstructionTarget);
                }
                else
                {
                    ReportError("invalid-first-character-of-processing-instruction-target");
                    ConvertTemporaryBufferToComment();
                    Reconsume(TokenizerState.BogusComment);
                }

                break;
            }

            case TokenizerState.ProcessingInstructionTarget:
            {
                var c = Consume();
                if (c is '\t' or '\n' or '\f' or ' ' or '?' or '>')
                {
                    var target = _temporaryBuffer.ToString();
                    if (Ascii.EqualsIgnoreCase(target, "xml") || Ascii.EqualsIgnoreCase(target, "xml-stylesheet"))
                    {
                        ReportError("disallowed-processing-instruction-target");
                        ConvertTemporaryBufferToComment();
                        Reconsume(TokenizerState.BogusComment);
                    }
                    else
                    {
                        _piTarget = target;
                        _piData.Clear();
                        Reconsume(TokenizerState.AfterProcessingInstructionTarget);
                    }
                }
                else if (c != Eof && (char.IsAsciiLetterOrDigit((char)c) || c is '-' or '_'))
                {
                    _temporaryBuffer.Append((char)c);
                }
                else if (c == Eof)
                {
                    ReportError("eof-in-processing-instruction");
                    EmitEndOfFile();
                }
                else
                {
                    ReportError("invalid-processing-instruction-target");
                    ConvertTemporaryBufferToComment();
                    Reconsume(TokenizerState.BogusComment);
                }

                break;
            }

            case TokenizerState.AfterProcessingInstructionTarget:
            {
                var c = Consume();
                if (c is not ('\t' or '\n' or '\f' or ' '))
                {
                    Reconsume(TokenizerState.ProcessingInstructionData);
                }

                break;
            }

            case TokenizerState.ProcessingInstructionData:
            {
                var c = Consume();
                switch (c)
                {
                    case '?':
                        _state = TokenizerState.ProcessingInstructionQuestionable;
                        break;
                    case '>':
                        _state = TokenizerState.Data;
                        EmitProcessingInstruction();
                        break;
                    case Eof:
                        ReportError("eof-in-processing-instruction");
                        EmitEndOfFile();
                        break;
                    default:
                        _piData.Append((char)c);
                        break;
                }

                break;
            }

            case TokenizerState.ProcessingInstructionQuestionable:
            {
                var c = Consume();
                switch (c)
                {
                    case '>':
                        _state = TokenizerState.Data;
                        EmitProcessingInstruction();
                        break;
                    case Eof:
                        ReportError("eof-in-processing-instruction");
                        EmitEndOfFile();
                        break;
                    default:
                        _piData.Append('?');
                        Reconsume(TokenizerState.ProcessingInstructionData);
                        break;
                }

                break;
            }

            case TokenizerState.CharacterReference:
            {
                _temporaryBuffer.Clear();
                _temporaryBuffer.Append('&');
                var c = Consume();
                if (c != Eof && char.IsAsciiLetterOrDigit((char)c))
                {
                    Reconsume(TokenizerState.NamedCharacterReference);
                }
                else if (c == '#')
                {
                    _temporaryBuffer.Append('#');
                    _state = TokenizerState.NumericCharacterReference;
                }
                else
                {
                    FlushCodePointsConsumedAsCharacterReference();
                    Reconsume(_returnState);
                }

                break;
            }

            case TokenizerState.NamedCharacterReference:
                NamedCharacterReference();
                break;

            case TokenizerState.AmbiguousAmpersand:
            {
                var c = Consume();
                if (c != Eof && char.IsAsciiLetterOrDigit((char)c))
                {
                    if (IsConsumedAsPartOfAnAttribute)
                    {
                        _attributeValue.Append((char)c);
                    }
                    else
                    {
                        EmitCharacter((char)c);
                    }
                }
                else if (c == ';')
                {
                    ReportError("unknown-named-character-reference");
                    Reconsume(_returnState);
                }
                else
                {
                    Reconsume(_returnState);
                }

                break;
            }

            case TokenizerState.NumericCharacterReference:
            {
                _characterReferenceCode = 0;
                var c = Consume();
                if (c is 'x' or 'X')
                {
                    _temporaryBuffer.Append((char)c);
                    _state = TokenizerState.HexadecimalCharacterReferenceStart;
                }
                else if (c is >= '0' and <= '9')
                {
                    Reconsume(TokenizerState.DecimalCharacterReference);
                }
                else
                {
                    ReportError("absence-of-digits-in-numeric-character-reference");
                    FlushCodePointsConsumedAsCharacterReference();
                    Reconsume(_returnState);
                }

                break;
            }

            case TokenizerState.HexadecimalCharacterReferenceStart:
            {
                var c = Consume();
                if (c != Eof && char.IsAsciiHexDigit((char)c))
                {
                    Reconsume(TokenizerState.HexadecimalCharacterReference);
                }
                else
                {
                    ReportError("absence-of-digits-in-numeric-character-reference");
                    FlushCodePointsConsumedAsCharacterReference();
                    Reconsume(_returnState);
                }

                break;
            }

            case TokenizerState.HexadecimalCharacterReference:
            {
                var c = Consume();
                if (c is >= '0' and <= '9')
                {
                    AddToCharacterReferenceCode(16, c - '0');
                }
                else if (c is >= 'A' and <= 'F')
                {
                    AddToCharacterReferenceCode(16, c - 'A' + 10);
                }
                else if (c is >= 'a' and <= 'f')
                {
                    AddToCharacterReferenceCode(16, c - 'a' + 10);
                }
                else if (c == ';')
                {
                    _state = TokenizerState.NumericCharacterReferenceEnd;
                }
                else
                {
                    ReportError("missing-semicolon-after-character-reference");
                    Reconsume(TokenizerState.NumericCharacterReferenceEnd);
                }

                break;
            }

            case TokenizerState.DecimalCharacterReference:
            {
                var c = Consume();
                if (c is >= '0' and <= '9')
                {
                    AddToCharacterReferenceCode(10, c - '0');
                }
                else if (c == ';')
                {
                    _state = TokenizerState.NumericCharacterReferenceEnd;
                }
                else
                {
                    ReportError("missing-semicolon-after-character-reference");
                    Reconsume(TokenizerState.NumericCharacterReferenceEnd);
                }

                break;
            }

            case TokenizerState.NumericCharacterReferenceEnd:
                NumericCharacterReferenceEnd();
                break;

            default:
                throw new InvalidOperationException($"Unknown tokenizer state {_state}");
        }
    }

    /// <summary>Emits EOF after the token that was just emitted (e.g. a comment emitted at the end of the input).</summary>
    private void EmitEndOfFileAfterPendingToken()
    {
        // The pending token is returned first; the end-of-file token is produced by the next call as the state
        // machine consumes EOF again in the data state.
        _state = TokenizerState.Data;
        _reconsume = false;
        _position = _limit;
    }

    private bool MatchesKeywordFromCurrentCharacter(string keyword)
    {
        // The current input character has been consumed; compare it and the following characters
        if (_reconsume || _current == Eof || !Ascii.EqualsIgnoreCase(stackalloc char[] { (char)_current }, keyword.AsSpan(0, 1)))
            return false;

        if (_limit - _position < keyword.Length - 1)
        {
            if (_canSuspend)
            {
                Suspend();
            }

            return false;
        }

        return Ascii.EqualsIgnoreCase(_input.AsSpan(_position, keyword.Length - 1), keyword.AsSpan(1));
    }

    private void AfterDoctypeKeyword(bool isPublic)
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                _state = isPublic ? TokenizerState.BeforeDoctypePublicIdentifier : TokenizerState.BeforeDoctypeSystemIdentifier;
                break;
            case '"':
                ReportError(isPublic ? "missing-whitespace-after-doctype-public-keyword" : "missing-whitespace-after-doctype-system-keyword");
                SetDoctypeIdentifierNotMissing(isPublic);
                _state = isPublic ? TokenizerState.DoctypePublicIdentifierDoubleQuoted : TokenizerState.DoctypeSystemIdentifierDoubleQuoted;
                break;
            case '\'':
                ReportError(isPublic ? "missing-whitespace-after-doctype-public-keyword" : "missing-whitespace-after-doctype-system-keyword");
                SetDoctypeIdentifierNotMissing(isPublic);
                _state = isPublic ? TokenizerState.DoctypePublicIdentifierSingleQuoted : TokenizerState.DoctypeSystemIdentifierSingleQuoted;
                break;
            case '>':
                ReportError(isPublic ? "missing-doctype-public-identifier" : "missing-doctype-system-identifier");
                _forceQuirks = true;
                _state = TokenizerState.Data;
                EmitDoctype();
                break;
            case Eof:
                ReportError("eof-in-doctype");
                _forceQuirks = true;
                EmitDoctype();
                EmitEndOfFileAfterPendingToken();
                break;
            default:
                ReportError(isPublic ? "missing-quote-before-doctype-public-identifier" : "missing-quote-before-doctype-system-identifier");
                _forceQuirks = true;
                Reconsume(TokenizerState.BogusDoctype);
                break;
        }
    }

    private void BeforeDoctypeIdentifier(bool isPublic)
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                break;
            case '"':
                SetDoctypeIdentifierNotMissing(isPublic);
                _state = isPublic ? TokenizerState.DoctypePublicIdentifierDoubleQuoted : TokenizerState.DoctypeSystemIdentifierDoubleQuoted;
                break;
            case '\'':
                SetDoctypeIdentifierNotMissing(isPublic);
                _state = isPublic ? TokenizerState.DoctypePublicIdentifierSingleQuoted : TokenizerState.DoctypeSystemIdentifierSingleQuoted;
                break;
            case '>':
                ReportError(isPublic ? "missing-doctype-public-identifier" : "missing-doctype-system-identifier");
                _forceQuirks = true;
                _state = TokenizerState.Data;
                EmitDoctype();
                break;
            case Eof:
                ReportError("eof-in-doctype");
                _forceQuirks = true;
                EmitDoctype();
                EmitEndOfFileAfterPendingToken();
                break;
            default:
                ReportError(isPublic ? "missing-quote-before-doctype-public-identifier" : "missing-quote-before-doctype-system-identifier");
                _forceQuirks = true;
                Reconsume(TokenizerState.BogusDoctype);
                break;
        }
    }

    private void SetDoctypeIdentifierNotMissing(bool isPublic)
    {
        if (isPublic)
        {
            _doctypePublicIdMissing = false;
            _doctypePublicId.Clear();
        }
        else
        {
            _doctypeSystemIdMissing = false;
            _doctypeSystemId.Clear();
        }
    }

    private void DoctypeIdentifierQuoted(bool isPublic, char quote)
    {
        var c = Consume();
        var identifier = isPublic ? _doctypePublicId : _doctypeSystemId;
        if (c == quote)
        {
            _state = isPublic ? TokenizerState.AfterDoctypePublicIdentifier : TokenizerState.AfterDoctypeSystemIdentifier;
        }
        else if (c == '\0')
        {
            ReportError("unexpected-null-character");
            identifier.Append('\U0000FFFD');
        }
        else if (c == '>')
        {
            ReportError(isPublic ? "abrupt-doctype-public-identifier" : "abrupt-doctype-system-identifier");
            _forceQuirks = true;
            _state = TokenizerState.Data;
            EmitDoctype();
        }
        else if (c == Eof)
        {
            ReportError("eof-in-doctype");
            _forceQuirks = true;
            EmitDoctype();
            EmitEndOfFileAfterPendingToken();
        }
        else
        {
            identifier.Append((char)c);
        }
    }

    private void LessThanSign(TokenizerState endTagOpenState, TokenizerState textState)
    {
        var c = Consume();
        if (c == '/')
        {
            _temporaryBuffer.Clear();
            _state = endTagOpenState;
        }
        else
        {
            EmitCharacter('<');
            Reconsume(textState);
        }
    }

    private void EndTagOpen(TokenizerState endTagNameState, TokenizerState textState)
    {
        var c = Consume();
        if (c != Eof && char.IsAsciiLetter((char)c))
        {
            CreateTagToken(isEndTag: true);
            Reconsume(endTagNameState);
        }
        else
        {
            EmitCharacter('<');
            EmitCharacter('/');
            Reconsume(textState);
        }
    }

    private void EndTagName(TokenizerState textState)
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ' when IsAppropriateEndTag():
                _state = TokenizerState.BeforeAttributeName;
                return;
            case '/' when IsAppropriateEndTag():
                _state = TokenizerState.SelfClosingStartTag;
                return;
            case '>' when IsAppropriateEndTag():
                _state = TokenizerState.Data;
                EmitTag();
                return;
        }

        if (c != Eof && char.IsAsciiLetter((char)c))
        {
            _tagName.Append(ToAsciiLower((char)c));
            _temporaryBuffer.Append((char)c);
            return;
        }

        EmitCharacter('<');
        EmitCharacter('/');
        EmitCharacters(_temporaryBuffer);
        Reconsume(textState);
    }

    private void ConvertTemporaryBufferToComment()
    {
        _commentData.Clear();
        _commentData.Append('?');
        _commentData.Append(_temporaryBuffer);
    }

    private void AddToCharacterReferenceCode(int radix, int digit)
    {
        // Saturate to avoid overflows; any value above 0x10FFFF is handled the same way
        if (_characterReferenceCode <= 0x10FFFF)
        {
            _characterReferenceCode = (_characterReferenceCode * radix) + digit;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#named-character-reference-state</summary>
    private void NamedCharacterReference()
    {
        // The current input character (an ASCII alphanumeric) is reconsumed: start matching from it
        Consume();
        var start = _position - 1;
        var node = 0;
        var matchedLength = 0;
        var matchedNode = -1;
        var firstChildren = NamedCharacterReferences.NodeFirstChild;
        var childCounts = NamedCharacterReferences.NodeChildCount;
        var characters = NamedCharacterReferences.NodeCharacter;
        for (var i = start; i - start < NamedCharacterReferences.LongestName; i++)
        {
            if (i >= _limit)
            {
                // A longer name may follow the limit
                if (_canSuspend)
                {
                    Suspend();
                }

                break;
            }

            var c = _input[i];
            var firstChild = firstChildren[node];
            var childCount = childCounts[node];
            var next = -1;
            for (var child = firstChild; child < firstChild + childCount; child++)
            {
                if (characters[child] == c)
                {
                    next = child;
                    break;
                }

                if (characters[child] > c)
                    break;
            }

            if (next < 0)
                break;

            node = next;
            if (NamedCharacterReferences.NodeValueLength[node] > 0)
            {
                matchedNode = node;
                matchedLength = i - start + 1;
            }
        }

        if (matchedNode < 0)
        {
            // No match: nothing is consumed except the ampersand
            _position = start;
            FlushCodePointsConsumedAsCharacterReference();
            _state = TokenizerState.AmbiguousAmpersand;
            return;
        }

        var name = _input.AsSpan(start, matchedLength);
        _position = start + matchedLength;
        var lastMatchedIsSemicolon = name[^1] == ';';
        if (IsConsumedAsPartOfAnAttribute && !lastMatchedIsSemicolon && _canSuspend && _position >= _limit)
        {
            Suspend();
        }

        if (IsConsumedAsPartOfAnAttribute && !lastMatchedIsSemicolon && _position < _limit && (_input[_position] == '=' || char.IsAsciiLetterOrDigit(_input[_position])))
        {
            _temporaryBuffer.Append(name);
            FlushCodePointsConsumedAsCharacterReference();
            _state = _returnState;
            return;
        }

        if (!lastMatchedIsSemicolon)
        {
            ReportErrorAtNextCharacter("missing-semicolon-after-character-reference");
        }

        _temporaryBuffer.Clear();
        _temporaryBuffer.Append(NamedCharacterReferences.Values.AsSpan(NamedCharacterReferences.NodeValueOffset[matchedNode], NamedCharacterReferences.NodeValueLength[matchedNode]));
        FlushCodePointsConsumedAsCharacterReference();
        _state = _returnState;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#numeric-character-reference-end-state</summary>
    private void NumericCharacterReferenceEnd()
    {
        var code = _characterReferenceCode;
        if (code == 0)
        {
            ReportErrorAtNextCharacter("null-character-reference");
            code = 0xFFFD;
        }
        else if (code > 0x10FFFF)
        {
            ReportErrorAtNextCharacter("character-reference-outside-unicode-range");
            code = 0xFFFD;
        }
        else if (code is >= 0xD800 and <= 0xDFFF)
        {
            ReportErrorAtNextCharacter("surrogate-character-reference");
            code = 0xFFFD;
        }
        else if (IsNoncharacter(code))
        {
            ReportErrorAtNextCharacter("noncharacter-character-reference");
        }
        else if (code == 0x0D || (IsControl(code) && !IsAsciiWhitespace(code)))
        {
            ReportErrorAtNextCharacter("control-character-reference");
            code = code switch
            {
                0x80 => 0x20AC,
                0x82 => 0x201A,
                0x83 => 0x0192,
                0x84 => 0x201E,
                0x85 => 0x2026,
                0x86 => 0x2020,
                0x87 => 0x2021,
                0x88 => 0x02C6,
                0x89 => 0x2030,
                0x8A => 0x0160,
                0x8B => 0x2039,
                0x8C => 0x0152,
                0x8E => 0x017D,
                0x91 => 0x2018,
                0x92 => 0x2019,
                0x93 => 0x201C,
                0x94 => 0x201D,
                0x95 => 0x2022,
                0x96 => 0x2013,
                0x97 => 0x2014,
                0x98 => 0x02DC,
                0x99 => 0x2122,
                0x9A => 0x0161,
                0x9B => 0x203A,
                0x9C => 0x0153,
                0x9E => 0x017E,
                0x9F => 0x0178,
                _ => code,
            };
        }

        _temporaryBuffer.Clear();
        _temporaryBuffer.Append(char.ConvertFromUtf32((int)code));
        FlushCodePointsConsumedAsCharacterReference();
        _state = _returnState;

        static bool IsNoncharacter(long c) => c is (>= 0xFDD0 and <= 0xFDEF) || ((c & 0xFFFE) == 0xFFFE && c <= 0x10FFFF);
        static bool IsControl(long c) => c is (>= 0 and <= 0x1F) or (>= 0x7F and <= 0x9F);
        static bool IsAsciiWhitespace(long c) => c is 0x09 or 0x0A or 0x0C or 0x0D or 0x20;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindStop(ReadOnlySpan<char> remaining, SearchValues<char> stopChars)
    {
        var index = remaining.IndexOfAny(stopChars);
        if (_errorHandler is null)
            return index;

        var errorIndex = (index < 0 ? remaining : remaining[..index]).IndexOfAny(InputStreamErrorChars);
        return errorIndex >= 0 ? errorIndex : index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char ToAsciiLower(char c) => char.IsAsciiLetterUpper(c) ? (char)(c | 0x20) : c;
}
