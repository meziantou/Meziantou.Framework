using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Yaml;

/// <summary>
/// A forward-only reader of UTF-8 YAML that records the position of every token and allocates nothing while reading.
/// </summary>
/// <remarks>
/// <para>
/// Every byte of the input that is not whitespace, a line break or a comment belongs to a token: the token itself
/// (<see cref="TokenStart"/>, <see cref="TokenLength"/>), the indicator that precedes it (<see cref="IndicatorOffset"/>:
/// <c>-</c>, <c>?</c>, <c>:</c> or <c>,</c>), or its properties (<see cref="AnchorStart"/>, <see cref="TagStart"/>). The
/// gaps between them are the trivia.
/// </para>
/// <para>
/// Scalars are decoded on demand by <see cref="GetString"/>, <see cref="CopyValue"/> and <see cref="ValueEquals(ReadOnlySpan{byte})"/>.
/// </para>
/// </remarks>
public ref struct Utf8YamlReader
{
    private const int InlineStackSize = 16;

    private static readonly SearchValues<byte> DoubleQuotedStops = SearchValues.Create("\"\\\r\n"u8);
    private static readonly SearchValues<byte> SingleQuotedStops = SearchValues.Create("'\r\n"u8);
    private static readonly SearchValues<byte> LineBreaks = SearchValues.Create("\r\n"u8);
    // The scans also stop at the bytes to validate: the control characters, and the bytes of non-ASCII characters
    private static readonly SearchValues<byte> BlockPlainScanStops = SearchValues.Create([.. ":#\r\n"u8, .. ValidatedBytes(quoted: false)]);
    private static readonly SearchValues<byte> FlowPlainScanStops = SearchValues.Create([.. ":#\r\n,[]{}"u8, .. ValidatedBytes(quoted: false)]);
    private static readonly SearchValues<byte> DoubleQuotedScanStops = SearchValues.Create([.. "\"\\\r\n"u8, .. ValidatedBytes(quoted: true)]);
    private static readonly SearchValues<byte> SingleQuotedScanStops = SearchValues.Create([.. "'\r\n"u8, .. ValidatedBytes(quoted: true)]);
    private static readonly SearchValues<byte> LineScanStops = SearchValues.Create([.. "\r\n"u8, .. ValidatedBytes(quoted: false)]);
    private static readonly SearchValues<byte> NameScanStops = SearchValues.Create([.. " \t\r\n,[]{}"u8, .. ValidatedBytes(quoted: false)]);
    private static readonly SearchValues<byte> WordCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_"u8);
    private static readonly SearchValues<byte> TagCharacters =SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_#;/?:@&=+$.~*'()%"u8);
    private static readonly SearchValues<byte> VerbatimTagCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_#;/?:@&=+$.~*'()%,![]"u8);

    private readonly ReadOnlySpan<byte> _buffer;
    private readonly bool _isFinalBlock;
    private readonly int _maxDepth;
    private readonly Utf8YamlReaderOptions _options;

    // Set by the token source, which reads the tokens of a block again when one is incomplete: a partial read then keeps
    // no copy of the state to go back to, and the reader stops instead
    private readonly bool _stopsAtIncompleteToken;
    private bool _stoppedAtIncompleteToken;
    private int _readCount;

    // Absolute offset of the first byte of the buffer, and number of lines before it
    private readonly long _bufferStart;
    private readonly int _lineNumber;
    private readonly long _charOffset;

    // The byte order marks at the start of the line, before the buffer. A byte order mark token moves the start of the
    // line after it, so the buffer of the next reader can start in the middle of the line, after them.
    private readonly int _lineByteOrderMarks;

    // When the block is not final, a token is complete only if a line break follows it in the buffer
    private readonly int _lastLineBreak;

    private TagDirective[]? _tagDirectives;

    // Position of the next byte to read
    private int _pos;

    // Offset of the start of the line that contains _pos
    private int _lineStart;

    // End of the last byte that belongs to a token, an indicator or a property
    private int _consumedEnd;

    // A line break was crossed since the last consumed byte
    private bool _newLine;

    // The indicators and the properties read for the next token
    private int _indicator;
    private int _explicitKeyIndicator;
    private NodeProperties _properties;
    private bool _hasYamlDirective;

    private bool _hasPending;
    private bool _hasDirectives;
    private DocumentState _documentState;

    private int _depth;
    private StateStack _stack;
    private int[]? _stackOverflow;

    private TokenData _token;
    private TokenData _pending;

    // Result of the last scalar or alias scan
    private int _scanStart;
    private int _scanEnd;
    private int _scanValueStart;
    private int _scanValueEnd;
    private int _scanBlockIndent;
    private int _scanColon;
    private Utf8YamlTokenFlags _scanFlags;
    private Utf8YamlTokenKind _scanKind;
    private bool _scanMultiLine;

    /// <summary>Initializes a reader over a complete UTF-8 YAML input.</summary>
    public Utf8YamlReader(ReadOnlySpan<byte> utf8Yaml)
        : this(utf8Yaml, isFinalBlock: true, default)
    {
    }

    /// <summary>Initializes a reader over a complete UTF-8 YAML input.</summary>
    public Utf8YamlReader(ReadOnlySpan<byte> utf8Yaml, Utf8YamlReaderOptions options)
        : this(utf8Yaml, isFinalBlock: true, new Utf8YamlReaderState(options))
    {
    }

    /// <summary>Initializes a reader over a part of a UTF-8 YAML input.</summary>
    /// <param name="utf8Yaml">The bytes not consumed by the previous reader, followed by the new data.</param>
    /// <param name="isFinalBlock"><see langword="true"/> when <paramref name="utf8Yaml"/> holds the end of the input.</param>
    /// <param name="state">The <see cref="CurrentState"/> of the previous reader, or a new state for the first block.</param>
    public Utf8YamlReader(ReadOnlySpan<byte> utf8Yaml, bool isFinalBlock, Utf8YamlReaderState state)
        : this(utf8Yaml, isFinalBlock, state, stopsAtIncompleteToken: false)
    {
    }

    /// <summary>Initializes a reader over a part of a UTF-8 YAML input.</summary>
    /// <param name="utf8Yaml">The bytes not consumed by the previous reader, followed by the new data.</param>
    /// <param name="isFinalBlock"><see langword="true"/> when <paramref name="utf8Yaml"/> holds the end of the input.</param>
    /// <param name="state">The <see cref="CurrentState"/> of the previous reader, or a new state for the first block.</param>
    /// <param name="stopsAtIncompleteToken">
    /// <see langword="true"/> when the caller reads the block again from <paramref name="state"/> after an incomplete
    /// token, see <see cref="StoppedAtIncompleteToken"/>, instead of the reader going back to its state before the token.
    /// </param>
    internal Utf8YamlReader(ReadOnlySpan<byte> utf8Yaml, bool isFinalBlock, Utf8YamlReaderState state, bool stopsAtIncompleteToken)
    {
        _stopsAtIncompleteToken = stopsAtIncompleteToken;
        _buffer = utf8Yaml;
        _isFinalBlock = isFinalBlock;
        _options = state.Options;
        _maxDepth = state.Options.EffectiveMaxDepth;
        _token.Indicator = -1;
        _token.ExplicitKeyIndicator = -1;
        _token.Properties = NodeProperties.None;
        if (!isFinalBlock)
        {
            // A carriage return at the end may be the first half of CR LF
            var last = utf8Yaml.LastIndexOfAny(LineBreaks);
            if (last >= 0 && last == utf8Yaml.Length - 1 && utf8Yaml[last] == '\r')
            {
                last = utf8Yaml[..last].LastIndexOfAny(LineBreaks);
            }

            _lastLineBreak = last;
        }

        if (state._data.IsInitialized)
        {
            _bufferStart = state._data.BufferStart;
            _lineNumber = state._data.LineNumber;
            _charOffset = state._data.CharOffset;
            _lineByteOrderMarks = state._data.LineByteOrderMarks;
            RestoreState(in state);

            // The state shares the entries of a deep stack. The caller reads the block again from the same state after an
            // incomplete token, so the reader must not change them.
            if (stopsAtIncompleteToken && _stackOverflow is not null)
            {
                _stackOverflow = (int[])_stackOverflow.Clone();
            }
        }
        else
        {
            _newLine = true;
            _indicator = -1;
            _explicitKeyIndicator = -1;
            _properties = NodeProperties.None;
        }
    }

    /// <summary>Gets the number of bytes of the input that the next reader does not need: the caller can drop them.</summary>
    public readonly int BytesConsumed
    {
        get
        {
            var consumed = _lineStart;
            if (_hasPending)
            {
                consumed = Math.Min(consumed, _pending.Start);
                if (_pending.Properties.AnchorStart >= 0)
                {
                    consumed = Math.Min(consumed, _pending.Properties.AnchorStart);
                }

                if (_pending.Properties.TagStart >= 0)
                {
                    consumed = Math.Min(consumed, _pending.Properties.TagStart);
                }
            }

            return consumed;
        }
    }

    /// <summary>Gets the absolute offset, in the whole input, of the first byte of the current buffer.</summary>
    public readonly long BufferOffset => _bufferStart;

    /// <summary>Gets the state to pass to the next reader, relative to the input without the first <see cref="BytesConsumed"/> bytes.</summary>
    public readonly Utf8YamlReaderState CurrentState
    {
        get
        {
            var consumed = BytesConsumed;
            var state = SaveState();
            state._data.BufferStart = _bufferStart + consumed;
            state._data.LineNumber = _lineNumber + CountLines(_buffer[..consumed]);
            state._data.CharOffset = _charOffset + Encoding.UTF8.GetCharCount(_buffer[..consumed]);
            state._data.LineByteOrderMarks = CountLineByteOrderMarks(_buffer[..consumed], _lineByteOrderMarks);
            state._data.Pos -= consumed;
            state._data.LineStart -= consumed;
            state._data.ConsumedEnd -= consumed;
            state._data.Indicator = Rebase(state._data.Indicator, consumed);
            state._data.ExplicitKeyIndicator = Rebase(state._data.ExplicitKeyIndicator, consumed);
            state._data.Properties.AnchorStart = Rebase(state._data.Properties.AnchorStart, consumed);
            state._data.Properties.TagStart = Rebase(state._data.Properties.TagStart, consumed);
            if (state._data.HasPending)
            {
                state._data.Pending.Start -= consumed;
                state._data.Pending.ValueStart -= consumed;
                state._data.Pending.Indicator = Rebase(state._data.Pending.Indicator, consumed);
                state._data.Pending.ExplicitKeyIndicator = Rebase(state._data.Pending.ExplicitKeyIndicator, consumed);
                state._data.Pending.Properties.AnchorStart = Rebase(state._data.Pending.Properties.AnchorStart, consumed);
                state._data.Pending.Properties.TagStart = Rebase(state._data.Pending.Properties.TagStart, consumed);
            }

            return state;

            static int Rebase(int position, int consumed) => position < 0 ? position : position - consumed;
        }
    }

    /// <summary>Gets the position after the last consumed node: the position of the next token without length.</summary>
    internal readonly int ConsumedEnd => _consumedEnd;

    /// <summary>Gets the state to continue reading the same buffer in a new reader, without dropping the consumed bytes.</summary>
    internal readonly Utf8YamlReaderState StateWithoutConsuming => SaveState();

    private readonly Utf8YamlReaderState SaveState()
    {
        var state = new Utf8YamlReaderState(_options)
        {
            _data = new StateData
            {
                IsInitialized = true,
                BufferStart = _bufferStart,
                LineNumber = _lineNumber,
                CharOffset = _charOffset,
                LineByteOrderMarks = _lineByteOrderMarks,
                Pos = _pos,
                LineStart = _lineStart,
                ConsumedEnd = _consumedEnd,
                Indicator = _indicator,
                ExplicitKeyIndicator = _explicitKeyIndicator,
                NewLine = _newLine,
                HasPending = _hasPending,
                HasDirectives = _hasDirectives,
                HasYamlDirective = _hasYamlDirective,
                DocumentState = _documentState,
                Depth = _depth,
                Stack = _stack,
                StackOverflow = _stackOverflow,
                Properties = _properties,
                Pending = _pending,
                TagDirectives = _tagDirectives,
            },
        };

        return state;
    }

    private void RestoreState(in Utf8YamlReaderState state)
    {
        _pos = state._data.Pos;
        _lineStart = state._data.LineStart;
        _consumedEnd = state._data.ConsumedEnd;
        _indicator = state._data.Indicator;
        _explicitKeyIndicator = state._data.ExplicitKeyIndicator;
        _newLine = state._data.NewLine;
        _hasPending = state._data.HasPending;
        _hasDirectives = state._data.HasDirectives;
        _hasYamlDirective = state._data.HasYamlDirective;
        _documentState = state._data.DocumentState;
        _depth = state._data.Depth;
        _stack = state._data.Stack;
        _stackOverflow = state._data.StackOverflow;
        _properties = state._data.Properties;
        _pending = state._data.Pending;
        _tagDirectives = state._data.TagDirectives;
    }

    // The byte order marks at the start of the line that continues after the consumed bytes
    private static int CountLineByteOrderMarks(ReadOnlySpan<byte> consumed, int previous)
    {
        var lineBreak = consumed.LastIndexOfAny((byte)'\r', (byte)'\n');
        var count = lineBreak < 0 ? previous : 0;
        var line = consumed[(lineBreak + 1)..];
        while (line.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            count++;
            line = line[3..];
        }

        return count;
    }

    private static int CountLines(ReadOnlySpan<byte> span)
    {
        var count = span.Count((byte)'\n');
        if (span.Contains((byte)'\r'))
        {
            // A carriage return alone is also a line break
            for (var i = 0; i < span.Length; i++)
            {
                if (span[i] == '\r' && (i + 1 >= span.Length || span[i + 1] != '\n'))
                {
                    count++;
                }
            }
        }

        return count;
    }

    internal enum DocumentState : byte
    {
        StreamStart,
        BeforeDocument,
        DocumentRoot,
        AfterRoot,
        StreamEnd,
    }

    private enum ContainerState : byte
    {
        None,
        BlockSequence,
        BlockMappingKey,
        BlockMappingValue,
        BlockMappingExplicitValue,
        FlowSequenceEntry,
        FlowSequenceAfterEntry,
        FlowMappingKey,
        FlowMappingValue,

        // The value of an entry whose key is a quoted scalar or a flow collection, which ':' can follow without a space
        FlowMappingJsonValue,
        FlowMappingAfterValue,
        FlowPairKey,
        FlowPairValue,

        // The value of a pair whose key is a quoted scalar or a flow collection, or whose ':' the key was read with
        FlowPairJsonValue,
        FlowPairEnd,
    }

    private enum NodeContext : byte
    {
        Root,
        SequenceEntry,
        MappingValue,
        ExplicitKey,
        ExplicitValue,
    }

    private enum PlainStop : byte
    {
        EndOfInput,
        LineBreak,
        Colon,
        Comment,
        FlowIndicator,
    }

    /// <summary>Gets the kind of the current token.</summary>
    public readonly Utf8YamlTokenKind TokenKind => _token.Kind;

    /// <summary>Gets the flags of the current token: scalar style, flow collection, implicit document marker.</summary>
    public readonly Utf8YamlTokenFlags TokenFlags => _token.Flags;

    /// <summary>Gets the offset of the first byte of the current token.</summary>
    public readonly int TokenStart => _token.Start;

    /// <summary>Gets the length of the current token, 0 for a zero-width token.</summary>
    public readonly int TokenLength => _token.Length;

    /// <summary>Gets the bytes of the current token.</summary>
    public readonly ReadOnlySpan<byte> TokenSpan => SliceBuffer(_token.Start, _token.Length);

    /// <summary>Gets the offset of the indicator (<c>-</c>, <c>?</c>, <c>:</c> or <c>,</c>) that precedes the current token, or -1.</summary>
    public readonly int IndicatorOffset => _token.Indicator;

    /// <summary>Gets the offset of the <c>?</c> indicator of an explicit key, or -1.</summary>
    public readonly int ExplicitKeyIndicatorOffset => _token.ExplicitKeyIndicator;

    /// <summary>Gets the offset of the anchor (<c>&amp;name</c>) of the current node, or -1.</summary>
    public readonly int AnchorStart => _token.Properties.AnchorStart;

    /// <summary>Gets the length of the anchor of the current node, including the <c>&amp;</c>.</summary>
    public readonly int AnchorLength => _token.Properties.AnchorLength;

    /// <summary>Gets the offset of the tag of the current node, or -1.</summary>
    public readonly int TagStart => _token.Properties.TagStart;

    /// <summary>Gets the length of the tag of the current node.</summary>
    public readonly int TagLength => _token.Properties.TagLength;

    /// <summary>Gets the name of the anchor of the current node, or of the current alias, without the <c>&amp;</c> or <c>*</c>.</summary>
    public readonly ReadOnlySpan<byte> AnchorName => _token.Kind is Utf8YamlTokenKind.Alias
        ? _buffer.Slice(_token.Start + 1, _token.Length - 1)
        : _token.Properties.AnchorStart < 0 ? default : _buffer.Slice(_token.Properties.AnchorStart + 1, _token.Properties.AnchorLength - 1);

    /// <summary>Gets the tag of the current node as written, such as <c>!!str</c>, or an empty span.</summary>
    public readonly ReadOnlySpan<byte> Tag => _token.Properties.TagStart < 0 ? default : _buffer.Slice(_token.Properties.TagStart, _token.Properties.TagLength);

    /// <summary>Gets the style of the current scalar.</summary>
    public readonly ScalarStyle ScalarStyle => (ScalarStyle)(_token.Flags & Utf8YamlTokenFlags.ScalarStyleMask);

    /// <summary>Gets a value indicating whether the current collection token belongs to a flow collection.</summary>
    public readonly bool IsFlow => (_token.Flags & Utf8YamlTokenFlags.Flow) != Utf8YamlTokenFlags.None;

    /// <summary>Gets a value indicating whether the value of the current scalar is <see cref="RawValueSpan"/> unchanged.</summary>
    public readonly bool ValueIsRaw => (_token.Flags & Utf8YamlTokenFlags.RawValue) != Utf8YamlTokenFlags.None;

    /// <summary>Gets the content of the current scalar as written: without the quotes or the block scalar header, with the escapes and the line breaks.</summary>
    public readonly ReadOnlySpan<byte> RawValueSpan => SliceBuffer(_token.ValueStart, _token.ValueLength);

    /// <summary>Gets the number of collections that contain the current position.</summary>
    public readonly int CurrentDepth => _depth;

    /// <summary>Gets the maximum number of bytes <see cref="CopyValue"/> writes for the current scalar.</summary>
    public readonly int MaxValueLength => _token.ValueLength + (_token.ValueLength / 2) + 1;

    /// <summary>Reads the next token.</summary>
    /// <returns><see langword="true"/> if a token was read; <see langword="false"/> at the end of the input.</returns>
    /// <exception cref="SyntaxErrorException">The input is not valid YAML.</exception>
    public bool Read()
    {
        if (_hasPending)
        {
            _hasPending = false;
            _token = _pending;
            _readCount++;
            return true;
        }

        if (_isFinalBlock)
            return ReadCore();

        return _stopsAtIncompleteToken ? ReadUntilIncompleteToken() : ReadPartial();
    }

    /// <summary>
    /// Gets a value indicating whether the last read found a token that the buffer does not hold completely. The state of
    /// the reader is then lost: a new reader created with the same arguments and read <see cref="ReadCount"/> times has
    /// the state after the last complete token.
    /// </summary>
    internal readonly bool StoppedAtIncompleteToken => _stoppedAtIncompleteToken;

    /// <summary>
    /// Gets the number of calls to <see cref="Read"/> that returned a token, which is exact for a reader created with
    /// <c>stopsAtIncompleteToken</c> over a block that is not the final one.
    /// </summary>
    internal readonly int ReadCount => _readCount;

    // Like ReadPartial, without the copy of the state that it takes before each token: the caller reads the block again
    // when a token is incomplete, which happens once per block
    private bool ReadUntilIncompleteToken()
    {
        if (_stoppedAtIncompleteToken)
            return false;

        try
        {
            var result = ReadCore();
            if (_pos <= _lastLineBreak)
            {
                if (result)
                {
                    _readCount++;
                }

                return result;
            }
        }
        catch (YamlException)
        {
            // The error may come from the missing data. It is reported again when the final block is read.
        }

        _stoppedAtIncompleteToken = true;
        return false;
    }

    // The input continues after the buffer: a token is accepted only when a line break follows the position where the
    // reader stops, as every look-ahead stops at the end of its line. Otherwise the reader goes back to its state before
    // the call and returns false, and the caller provides more data.
    private bool ReadPartial()
    {
        var snapshot = SaveState();
        var overflowTop = _stackOverflow is not null && _depth > 0 ? _stackOverflow[_depth - 1] : 0;
        var token = _token;
        try
        {
            var result = ReadCore();
            if (_pos <= _lastLineBreak)
                return result;
        }
        catch (YamlException)
        {
            // The error may come from the missing data. It is reported again when the final block is read.
        }

        RestoreState(in snapshot);
        if (_stackOverflow is not null && _depth > 0)
        {
            _stackOverflow[_depth - 1] = overflowTop;
        }

        _token = token;
        return false;
    }

    private bool ReadCore()
    {
        if (_depth == 0)
        {
            return _documentState switch
            {
                DocumentState.StreamStart => ReadStreamStart(),
                DocumentState.BeforeDocument => ReadBeforeDocument(),
                DocumentState.DocumentRoot => ReadDocumentRoot(),
                DocumentState.AfterRoot => ReadDocumentEnd(),
                _ => false,
            };
        }

        var entry = PeekEntry();
        var indent = (entry >> 4) - 1;
        return (ContainerState)(entry & 0xF) switch
        {
            ContainerState.BlockSequence => ReadBlockSequenceEntry(indent),
            ContainerState.BlockMappingKey => ReadBlockMappingKey(indent),
            ContainerState.BlockMappingValue => ReadBlockMappingValue(indent),
            ContainerState.BlockMappingExplicitValue => ReadBlockMappingExplicitValue(indent),
            ContainerState.FlowSequenceEntry => ReadFlowSequenceEntry(),
            ContainerState.FlowSequenceAfterEntry => ReadFlowSequenceAfterEntry(),
            ContainerState.FlowMappingKey => ReadFlowMappingKey(),
            ContainerState.FlowMappingValue => ReadFlowMappingValue(afterJsonKey: false),
            ContainerState.FlowMappingJsonValue => ReadFlowMappingValue(afterJsonKey: true),
            ContainerState.FlowMappingAfterValue => ReadFlowMappingAfterValue(),
            ContainerState.FlowPairKey => ReadFlowPairKey(),
            ContainerState.FlowPairValue => ReadFlowPairValue(afterJsonKey: false),
            ContainerState.FlowPairJsonValue => ReadFlowPairValue(afterJsonKey: true),
            _ => ReadFlowPairEnd(),
        };
    }

    /// <summary>Decodes the value of the current scalar.</summary>
    public readonly string GetString()
    {
        EnsureScalar();
        return DecodeString(_buffer, in _token);
    }

    /// <summary>Gets the data of the current token, to decode it after the reader is gone with <see cref="DecodeString"/>.</summary>
    internal readonly TokenData CurrentToken => _token;

    /// <summary>Decodes the value of a scalar token of <paramref name="buffer"/>.</summary>
    internal static string DecodeString(ReadOnlySpan<byte> buffer, in TokenData token)
    {
        if (token.ValueLength == 0)
            return string.Empty;

        var raw = buffer.Slice(token.ValueStart, token.ValueLength);
        if ((token.Flags & Utf8YamlTokenFlags.RawValue) != Utf8YamlTokenFlags.None)
            return Encoding.UTF8.GetString(raw);

        byte[]? rented = null;
        var maxLength = token.ValueLength + (token.ValueLength / 2) + 1;
        Span<byte> destination = maxLength <= 256 ? stackalloc byte[256] : (rented = ArrayPool<byte>.Shared.Rent(maxLength));
        try
        {
            var length = DecodeValue(buffer, in token, destination);
            return Encoding.UTF8.GetString(destination[..length]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>Writes the UTF-8 value of the current scalar to <paramref name="destination"/>, which must hold at least <see cref="MaxValueLength"/> bytes.</summary>
    /// <returns>The number of bytes written.</returns>
    public readonly int CopyValue(Span<byte> destination)
    {
        EnsureScalar();
        if ((_token.Flags & Utf8YamlTokenFlags.RawValue) != Utf8YamlTokenFlags.None)
        {
            var raw = SliceBuffer(_token.ValueStart, _token.ValueLength);
            raw.CopyTo(destination);
            return raw.Length;
        }

        if (destination.Length < MaxValueLength)
            throw new ArgumentException("The destination is too small", nameof(destination));

        return DecodeValue(destination);
    }

    /// <summary>Writes the UTF-8 value of a scalar token of <paramref name="buffer"/> to <paramref name="destination"/>, which must hold at least <see cref="MaxValueLength"/> bytes.</summary>
    internal static int CopyScalarValue(ReadOnlySpan<byte> buffer, in TokenData token, Span<byte> destination)
    {
        if ((token.Flags & Utf8YamlTokenFlags.RawValue) != Utf8YamlTokenFlags.None)
        {
            var raw = token.ValueLength == 0 ? default : buffer.Slice(token.ValueStart, token.ValueLength);
            raw.CopyTo(destination);
            return raw.Length;
        }

        return DecodeValue(buffer, in token, destination);
    }

    /// <summary>Compares the value of the current scalar with a UTF-8 text without allocating.</summary>
    public readonly bool ValueEquals(ReadOnlySpan<byte> utf8Text)
    {
        EnsureScalar();
        return ValueEquals(_buffer, in _token, utf8Text);
    }

    /// <summary>Compares the value of the current scalar with a text without allocating.</summary>
    /// <remarks>A text with an invalid UTF-16 sequence, such as a lone surrogate, is equal to no value.</remarks>
    public readonly bool ValueEquals(ReadOnlySpan<char> text)
    {
        EnsureScalar();
        return ValueEquals(_buffer, in _token, text);
    }

    /// <summary>Compares the value of the current scalar with a text without allocating.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public readonly bool ValueEquals(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ValueEquals(text.AsSpan());
    }

    internal static bool ValueEquals(ReadOnlySpan<byte> buffer, in TokenData token, ReadOnlySpan<char> text)
    {
        // A character is at least one UTF-8 byte, and a value is never more than half longer than its text
        if (text.Length > token.ValueLength + (token.ValueLength / 2) + 1)
            return false;

        // A UTF-16 character is at most three UTF-8 bytes (a surrogate pair is four bytes for two characters)
        var maxLength = text.Length * 3;

        byte[]? rented = null;
        Span<byte> utf8 = maxLength <= 256 ? stackalloc byte[256] : (rented = ArrayPool<byte>.Shared.Rent(maxLength));
        try
        {
            if (System.Text.Unicode.Utf8.FromUtf16(text, utf8, out _, out var written, replaceInvalidSequences: false) is not OperationStatus.Done)
                return false;

            return ValueEquals(buffer, in token, utf8[..written]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>Compares the value of a scalar token of <paramref name="buffer"/> with a UTF-8 text without allocating.</summary>
    internal static bool ValueEquals(ReadOnlySpan<byte> buffer, in TokenData token, ReadOnlySpan<byte> utf8Text)
    {
        var raw = token.ValueLength == 0 ? default : buffer.Slice(token.ValueStart, token.ValueLength);
        if ((token.Flags & Utf8YamlTokenFlags.RawValue) != Utf8YamlTokenFlags.None)
            return raw.SequenceEqual(utf8Text);

        // A value is never more than half longer than its text
        if (utf8Text.Length > raw.Length + (raw.Length / 2) + 1)
            return false;

        byte[]? rented = null;
        var maxLength = token.ValueLength + (token.ValueLength / 2) + 1;
        Span<byte> destination = maxLength <= 256 ? stackalloc byte[256] : (rented = ArrayPool<byte>.Shared.Rent(maxLength));
        try
        {
            var length = DecodeValue(buffer, in token, destination);
            return destination[..length].SequenceEqual(utf8Text);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private readonly void EnsureScalar()
    {
        if (_token.Kind is not Utf8YamlTokenKind.Scalar)
            throw new InvalidOperationException("The current token is not a scalar");
    }

    // ------------------------------------------------------------------------------------------------------------
    // Documents
    // ------------------------------------------------------------------------------------------------------------

    private bool ReadStreamStart()
    {
        _documentState = DocumentState.BeforeDocument;
        if (_buffer.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            SetToken(Utf8YamlTokenKind.ByteOrderMark, Utf8YamlTokenFlags.None, 0, 3);
            _pos = 3;
            _lineStart = 3;
            return true;
        }

        return ReadBeforeDocument();
    }

    private bool ReadBeforeDocument()
    {
        SkipTrivia();
        var buffer = _buffer;
        if (_pos >= buffer.Length)
        {
            if (_hasDirectives)
                throw CreateError(_pos, "Expected '---' after the directives");

            _documentState = DocumentState.StreamEnd;
            return false;
        }

        if (!_newLine)
            throw CreateError(_pos, "Expected a comment or a line break after the document marker");

        var c = buffer[_pos];

        // A byte order mark can start a document prefix, which is before the directives, or precede the '---' that
        // ends them
        if (c == 0xEF && _pos == _lineStart && (!_hasDirectives || DocumentMarkerAt(_pos) == '-') && buffer[_pos..].StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            SetToken(Utf8YamlTokenKind.ByteOrderMark, Utf8YamlTokenFlags.None, _pos, 3);
            _pos += 3;
            _lineStart = _pos;
            return true;
        }

        if (c == '%' && _pos == _lineStart)
        {
            var end = FindDirectiveEnd(_pos);
            ValidateDirective(_pos, end);
            SetToken(Utf8YamlTokenKind.Directive, Utf8YamlTokenFlags.None, _pos, end - _pos);
            _pos = end;
            _newLine = false;
            _hasDirectives = true;
            return true;
        }

        if (IsDocumentMarker(_pos, (byte)'-'))
        {
            SetToken(Utf8YamlTokenKind.DocumentStart, Utf8YamlTokenFlags.None, _pos, 3);
            _pos += 3;
            _newLine = false;
            _hasDirectives = false;
            _hasYamlDirective = false;
            _documentState = DocumentState.DocumentRoot;
            return true;
        }

        if (_hasDirectives)
            throw CreateError(_pos, "Expected '---' after the directives");

        if (IsDocumentMarker(_pos, (byte)'.'))
        {
            SetToken(Utf8YamlTokenKind.DocumentEndMarker, Utf8YamlTokenFlags.None, _pos, 3);
            _pos += 3;
            _newLine = false;
            return true;
        }

        SetToken(Utf8YamlTokenKind.DocumentStart, Utf8YamlTokenFlags.Implicit, _pos, 0);
        _documentState = DocumentState.DocumentRoot;
        return true;
    }

    // The end of a directive, before its comment. The prefix of a %TAG directive can start with '#': it is not a comment.
    private readonly int FindDirectiveEnd(int start)
    {
        var buffer = _buffer;
        if (!buffer[start..].StartsWith("%TAG"u8) || start + 4 >= buffer.Length || buffer[start + 4] is not ((byte)' ' or (byte)'\t'))
            return FindLineContentEnd(start);

        var lineEnd = FindLineEnd(start, isComment: true);
        var p = start + 4;
        for (var parameter = 0; parameter < 2; parameter++)
        {
            while (p < lineEnd && buffer[p] is (byte)' ' or (byte)'\t')
            {
                p++;
            }

            while (p < lineEnd && buffer[p] is not ((byte)' ' or (byte)'\t'))
            {
                p++;
            }
        }

        // What follows the prefix is a comment or a parameter too many, which the validation reports
        return Math.Max(p, FindLineContentEnd(p));
    }

    // %YAML takes a version and appears once per document, %TAG takes a handle and a prefix, other directives are reserved
    private void ValidateDirective(int start, int end)
    {
        var line = _buffer[(start + 1)..end];

        // The name follows the '%': "% TAG" is not a %TAG directive
        if (line.IsEmpty || line[0] is (byte)' ' or (byte)'\t')
            throw CreateError(start + 1, "Expected a directive name");

        var index = 0;
        var name = NextParameter(line, ref index);
        var first = NextParameter(line, ref index);
        var second = NextParameter(line, ref index);
        var third = NextParameter(line, ref index);
        if (name.SequenceEqual("YAML"u8))
        {
            if (_hasYamlDirective)
                throw CreateError(start, "The %YAML directive appears twice");

            _hasYamlDirective = true;
            var dot = first.IndexOf((byte)'.');
            if (!second.IsEmpty || dot <= 0 || dot == first.Length - 1 || first[..dot].ContainsAnyExceptInRange((byte)'0', (byte)'9') || first[(dot + 1)..].ContainsAnyExceptInRange((byte)'0', (byte)'9'))
                throw CreateError(start, "Invalid %YAML directive");

            // YAML 1.1 and later 1.x versions are read with the YAML 1.2 rules
            if (!int.TryParse(first[..dot], System.Globalization.CultureInfo.InvariantCulture, out var major) || !int.TryParse(first[(dot + 1)..], System.Globalization.CultureInfo.InvariantCulture, out var minor) || major != 1 || minor < 1)
            {
                var mark = GetMark(start);
                throw new SemanticErrorException(mark, mark, "Found incompatible YAML document.");
            }
        }
        else if (name.SequenceEqual("TAG"u8))
        {
            // The handle is '!', '!!' or '!' followed by word characters and '!'; the prefix is a tag URI
            if (first.IsEmpty || second.IsEmpty || !third.IsEmpty || first[0] != '!' || first[^1] != '!'
                || (first.Length > 2 && first[1..^1].ContainsAnyExcept(WordCharacters))
                || second.ContainsAnyExcept(VerbatimTagCharacters))
            {
                throw CreateError(start, "Invalid %TAG directive");
            }

            ValidateTagEscapes(second, start);

            if (FindTagDirective(first) is not null)
                throw CreateError(start, "The tag handle is declared twice");

            // Copy on write: a state saved before this directive keeps the previous array
            TagDirective[] directives = [.. _tagDirectives ?? [], new TagDirective(first.ToArray(), second.ToArray())];
            _tagDirectives = directives;
        }

        static ReadOnlySpan<byte> NextParameter(ReadOnlySpan<byte> line, ref int index)
        {
            while (index < line.Length && line[index] is (byte)' ' or (byte)'\t')
            {
                index++;
            }

            var start = index;
            while (index < line.Length && line[index] is not ((byte)' ' or (byte)'\t'))
            {
                index++;
            }

            return line[start..index];
        }
    }

    private bool ReadDocumentRoot()
    {
        _documentState = DocumentState.AfterRoot;
        return ReadBlockNode(-1, NodeContext.Root);
    }

    private bool ReadDocumentEnd()
    {
        SkipTrivia();
        if (_pos >= _buffer.Length)
        {
            SetToken(Utf8YamlTokenKind.DocumentEnd, Utf8YamlTokenFlags.Implicit, _consumedEnd, 0);
            _tagDirectives = null;
            _documentState = DocumentState.BeforeDocument;
            return true;
        }

        if (IsDocumentMarker(_pos, (byte)'.'))
        {
            SetToken(Utf8YamlTokenKind.DocumentEnd, Utf8YamlTokenFlags.None, _pos, 3);
            _tagDirectives = null;
            _pos += 3;
            _newLine = false;
            _documentState = DocumentState.BeforeDocument;
            return true;
        }

        if (IsDocumentMarker(_pos, (byte)'-'))
        {
            SetToken(Utf8YamlTokenKind.DocumentEnd, Utf8YamlTokenFlags.Implicit, _consumedEnd, 0);
            _tagDirectives = null;
            _documentState = DocumentState.BeforeDocument;
            return true;
        }

        throw CreateError(_pos, "Expected the end of the document");
    }

    // ------------------------------------------------------------------------------------------------------------
    // Block collections
    // ------------------------------------------------------------------------------------------------------------

    private bool ReadBlockSequenceEntry(int indent)
    {
        SkipTrivia();
        var buffer = _buffer;
        var pos = _pos;
        if (pos >= buffer.Length)
            return EndBlockCollection(Utf8YamlTokenKind.SequenceEnd);

        var column = pos - _lineStart;
        if (column < indent || IsDocumentMarker(pos))
            return EndBlockCollection(Utf8YamlTokenKind.SequenceEnd);

        if (column == indent)
        {
            if (buffer[pos] != '-' || !IsBlankAt(pos + 1))
                return EndBlockCollection(Utf8YamlTokenKind.SequenceEnd);

            CheckIndentationHasNoTab();
            _indicator = pos;
            _pos = pos + 1;
            _consumedEnd = pos + 1;
            _newLine = false;
            return ReadBlockNode(indent, NodeContext.SequenceEntry);
        }

        throw CreateError(pos, "Expected '-' at the indentation of the block sequence");
    }

    private bool ReadBlockMappingKey(int indent)
    {
        SkipTrivia();
        var buffer = _buffer;
        var pos = _pos;
        if (pos >= buffer.Length)
            return EndBlockCollection(Utf8YamlTokenKind.MappingEnd);

        var column = pos - _lineStart;
        if (column < indent || IsDocumentMarker(pos))
            return EndBlockCollection(Utf8YamlTokenKind.MappingEnd);

        if (column > indent)
            throw CreateError(pos, "Expected a mapping key at the indentation of the block mapping");

        CheckIndentationHasNoTab();
        var c = buffer[pos];
        if (c == '?' && IsBlankAt(pos + 1))
        {
            _explicitKeyIndicator = pos;
            _pos = pos + 1;
            _consumedEnd = pos + 1;
            _newLine = false;
            SetState(ContainerState.BlockMappingExplicitValue);
            return ReadBlockNode(indent, NodeContext.ExplicitKey);
        }

        if (c == ':' && IsBlankAt(pos + 1))
        {
            SetState(ContainerState.BlockMappingValue);
            return EmitEmptyScalar(pos);
        }

        if (c == '-' && IsBlankAt(pos + 1))
            throw CreateError(pos, "A block sequence entry is not allowed in this context");

        _newLine = false;
        while (c is (byte)'&' or (byte)'!')
        {
            ScanProperty();
            SkipBlanks();
            if (_pos >= buffer.Length)
                throw CreateError(_pos, "Expected a mapping key");

            c = buffer[_pos];
        }

        SetState(ContainerState.BlockMappingValue);
        if (c == ':' && IsBlankAt(_pos + 1))
        {
            // An empty key with properties
            return EmitEmptyScalar(_consumedEnd);
        }

        switch (c)
        {
            case (byte)'[' or (byte)'{':
                // An implicit key is on one line, followed by ':'
                if (!IsFlowCollectionImplicitKey(_pos))
                    throw CreateError(_pos, "Expected ':' after the mapping key, on the line where it ends");

                PushFlowCollection(c, indent);
                return true;

            case (byte)'"':
                ScanDoubleQuoted(indent);
                CheckKeyColon(flow: false);
                break;

            case (byte)'\'':
                ScanSingleQuoted(indent);
                CheckKeyColon(flow: false);
                break;

            case (byte)'*':
                if (_properties.HasValue)
                    throw CreateError(_pos, "An alias cannot have properties");

                ScanAlias();
                CheckKeyColon(flow: false);
                break;

            default:
                ValidatePlainStart(_pos, flow: false);
                ScanPlainScalar(indent, flow: false);
                break;
        }

        if (_scanColon < 0)
            throw CreateError(_scanEnd, "Expected ':' after the mapping key");

        CheckImplicitKeyLength();

        EmitScanned();
        _pos = _scanColon;
        return true;
    }

    private bool ReadBlockMappingValue(int indent)
    {
        var buffer = _buffer;
        var pos = _pos;
        while (pos < buffer.Length && buffer[pos] is (byte)' ' or (byte)'\t')
        {
            pos++;
        }

        if (pos >= buffer.Length || buffer[pos] != ':' || !IsBlankAt(pos + 1))
            throw CreateError(pos, "Expected ':' after the mapping key");

        _indicator = pos;
        _pos = pos + 1;
        _consumedEnd = pos + 1;
        _newLine = false;
        SetState(ContainerState.BlockMappingKey);
        return ReadBlockNode(indent, NodeContext.MappingValue);
    }

    private bool ReadBlockMappingExplicitValue(int indent)
    {
        SkipTrivia();
        var buffer = _buffer;
        var pos = _pos;
        SetState(ContainerState.BlockMappingKey);
        if (pos < buffer.Length && pos - _lineStart == indent && buffer[pos] == ':' && IsBlankAt(pos + 1) && !IsDocumentMarker(pos))
        {
            // Like a key, the ':' of the value is indented by spaces
            var indentation = buffer[_lineStart..pos];
            if (indentation.Contains((byte)'\t') && !indentation.ContainsAnyExcept((byte)' ', (byte)'\t'))
                throw CreateError(pos, "A tab cannot be used for indentation");

            _indicator = pos;
            _pos = pos + 1;
            _consumedEnd = pos + 1;
            _newLine = false;
            return ReadBlockNode(indent, NodeContext.ExplicitValue);
        }

        return EmitEmptyScalar(_consumedEnd);
    }

    private bool EndBlockCollection(Utf8YamlTokenKind kind)
    {
        PopEntry();
        SetToken(kind, Utf8YamlTokenFlags.None, _consumedEnd, 0);
        return true;
    }

    // Reads the node that follows an indicator (or the start of a document) in the block context.
    private bool ReadBlockNode(int parentIndent, NodeContext context)
    {
        var indicator = Math.Max(_indicator, _explicitKeyIndicator);
        SkipTrivia();
        var buffer = _buffer;
        var lineCrossed = _newLine;

        // The properties on the lines before the content belong to the node; the properties on the line of the
        // content belong to the content, which may be the first key of a block mapping.
        var outerProperties = NodeProperties.None;
        var linePropertiesStart = -1;
        while (_pos < buffer.Length)
        {
            var p = buffer[_pos];
            if (p is not ((byte)'&' or (byte)'!'))
                break;

            if (_newLine)
            {
                lineCrossed = true;
                if (_pos - _lineStart <= parentIndent)
                    break;

                // Like the content, the properties are indented by spaces
                var propertySpaces = CountSpaces(_lineStart);
                if (propertySpaces < _pos - _lineStart && propertySpaces <= parentIndent)
                    throw CreateError(_pos, "A tab cannot be used for indentation");

                if (_properties.HasValue)
                {
                    MergeProperties(ref outerProperties, _properties);
                    _properties = NodeProperties.None;
                }
            }

            if (!_properties.HasValue)
            {
                linePropertiesStart = _pos;
            }

            ScanProperty();
            _newLine = false;
            SkipTrivia();
        }

        var onNewLine = _newLine;
        if (onNewLine && _properties.HasValue)
        {
            MergeProperties(ref outerProperties, _properties);
            _properties = NodeProperties.None;
        }

        if (_pos >= buffer.Length)
            return EmitEmptyScalar(_consumedEnd, outerProperties);

        var pos = _pos;
        var column = pos - _lineStart;
        var c = buffer[pos];
        if (onNewLine)
        {
            if (column < parentIndent || IsDocumentMarker(pos))
                return EmitEmptyScalar(_consumedEnd, outerProperties);

            if (column == parentIndent && !(context is NodeContext.MappingValue or NodeContext.ExplicitKey or NodeContext.ExplicitValue && c == '-' && IsBlankAt(pos + 1)))
                return EmitEmptyScalar(_consumedEnd, outerProperties);

            // The node is indented by spaces; a tab can only separate it after its indentation
            var spaces = CountSpaces(_lineStart);
            if (spaces < column && spaces <= parentIndent)
                throw CreateError(pos, "A tab cannot be used for indentation");
        }

        // A block collection can start on the line of a '-', '?' or ':' indicator, but not after a tab
        var sameLine = !lineCrossed && !onNewLine;
        var collectionAllowed = !sameLine || context is NodeContext.SequenceEntry or NodeContext.ExplicitKey or NodeContext.ExplicitValue;
        _newLine = false;
        switch (c)
        {
            case (byte)'-' when IsBlankAt(pos + 1):
            case (byte)'?' when IsBlankAt(pos + 1):
                if (!collectionAllowed || _properties.HasValue)
                    throw CreateError(pos, "A block collection is not allowed in this context");

                CheckNoTabBeforeBlockCollection(sameLine, indicator, pos);
                _properties = outerProperties;
                if (c == '-')
                {
                    PushEntry(ContainerState.BlockSequence, column);
                    SetToken(Utf8YamlTokenKind.SequenceStart, Utf8YamlTokenFlags.None, pos, 0);
                }
                else
                {
                    PushEntry(ContainerState.BlockMappingKey, column);
                    SetToken(Utf8YamlTokenKind.MappingStart, Utf8YamlTokenFlags.None, pos, 0);
                }

                return true;

            case (byte)':' when IsBlankAt(pos + 1):
            {
                // A mapping whose first key is empty
                if (!collectionAllowed)
                    throw CreateError(pos, "A block mapping is not allowed in this context");

                var keyStart = _properties.HasValue ? linePropertiesStart : pos;
                CheckNoTabBeforeBlockCollection(sameLine, indicator, keyStart);
                PushEntry(ContainerState.BlockMappingValue, keyStart - _lineStart);
                SetEmptyScalarScan(pos);
                _scanColon = pos;
                return StartBlockMappingWithScannedKey(outerProperties, keyStart);
            }

            case (byte)'|' or (byte)'>':
                MergeProperties(ref _properties, outerProperties);
                ScanBlockScalar(parentIndent);
                EmitScanned();
                return true;

            case (byte)'[' or (byte)'{':
                if (IsFlowCollectionImplicitKey(pos))
                {
                    if (!collectionAllowed)
                        throw CreateError(pos, "A block mapping is not allowed in this context");

                    // The mapping reads the key again, with its properties
                    var keyStart = _properties.HasValue ? linePropertiesStart : pos;
                    CheckNoTabBeforeBlockCollection(sameLine, indicator, keyStart);
                    _properties = outerProperties;
                    PushEntry(ContainerState.BlockMappingKey, keyStart - _lineStart);
                    SetToken(Utf8YamlTokenKind.MappingStart, Utf8YamlTokenFlags.None, keyStart, 0);
                    _pos = keyStart;
                    return true;
                }

                MergeProperties(ref _properties, outerProperties);
                PushFlowCollection(c, parentIndent);
                return true;

            case (byte)'*':
                ScanAlias();
                CheckKeyColon(flow: false);
                if (_scanColon >= 0 ? _properties.HasValue : (_properties.HasValue || outerProperties.HasValue))
                    throw CreateError(pos, "An alias cannot have properties");

                break;

            case (byte)'"':
                ScanDoubleQuoted(parentIndent);
                CheckKeyColon(flow: false);
                break;

            case (byte)'\'':
                ScanSingleQuoted(parentIndent);
                CheckKeyColon(flow: false);
                break;

            default:
                ValidatePlainStart(pos, flow: false);
                ScanPlainScalar(parentIndent, flow: false);
                break;
        }

        if (_scanColon >= 0)
        {
            if (!collectionAllowed)
                throw CreateError(_scanColon, "A mapping value is not allowed in this context");

            CheckImplicitKeyLength();

            var keyStart = _properties.HasValue ? linePropertiesStart : _scanStart;
            CheckNoTabBeforeBlockCollection(sameLine, indicator, keyStart);
            PushEntry(ContainerState.BlockMappingValue, keyStart - _lineStart);
            return StartBlockMappingWithScannedKey(outerProperties, keyStart);
        }

        MergeProperties(ref _properties, outerProperties);
        EmitScanned();
        return true;
    }

    // Emits the start of a mapping and keeps the scanned key, with the current properties, as the pending token
    private bool StartBlockMappingWithScannedKey(NodeProperties mappingProperties, int keyStart)
    {
        var indicator = _indicator;
        var explicitKeyIndicator = _explicitKeyIndicator;
        _indicator = -1;
        _explicitKeyIndicator = -1;
        EmitScanned();
        _pending = _token;
        _hasPending = true;
        _indicator = indicator;
        _explicitKeyIndicator = explicitKeyIndicator;
        _properties = mappingProperties;
        SetToken(Utf8YamlTokenKind.MappingStart, Utf8YamlTokenFlags.None, keyStart, 0);
        _pos = _scanColon;
        return true;
    }

    private readonly void MergeProperties(ref NodeProperties target, NodeProperties source)
    {
        if (source.AnchorStart >= 0)
        {
            if (target.AnchorStart >= 0)
                throw CreateError(source.AnchorStart, "A node cannot have two anchors");

            target.AnchorStart = source.AnchorStart;
            target.AnchorLength = source.AnchorLength;
        }

        if (source.TagStart >= 0)
        {
            if (target.TagStart >= 0)
                throw CreateError(source.TagStart, "A node cannot have two tags");

            target.TagStart = source.TagStart;
            target.TagLength = source.TagLength;
        }
    }

    // An implicit key has at most 1024 Unicode characters, up to its ':'
    private readonly void CheckImplicitKeyLength()
    {
        var length = _scanColon - _scanStart;
        if (length > 1024)
        {
            var characters = 0;
            foreach (var b in _buffer[_scanStart.._scanColon])
            {
                if ((b & 0xC0) != 0x80)
                {
                    characters++;
                }
            }

            if (characters > 1024)
                throw CreateError(_scanStart, "An implicit key cannot be longer than 1024 characters");
        }
    }

    private readonly int CountSpaces(int start)
    {
        var p = start;
        while (p < _buffer.Length && _buffer[p] == ' ')
        {
            p++;
        }

        return p - start;
    }

    // Tabs cannot be used for indentation
    private readonly void CheckIndentationHasNoTab()
    {
        if (_buffer[_lineStart.._pos].Contains((byte)'\t'))
            throw CreateError(_pos, "A tab cannot be used for indentation");
    }

    // A block collection is indented by spaces: the spaces at the start of its line, or after its indicator when it starts
    // on the line of the indicator. A tab can only separate a scalar or a flow collection.
    private readonly void CheckNoTabBeforeBlockCollection(bool sameLine, int indicator, int start)
    {
        var indentation = sameLine && indicator >= 0 ? _buffer[(indicator + 1)..start] : _buffer[_lineStart..start];
        if (indentation.Contains((byte)'\t') && !indentation.ContainsAnyExcept((byte)' ', (byte)'\t'))
            throw CreateError(start, "A tab cannot be used for indentation");
    }

    // ------------------------------------------------------------------------------------------------------------
    // Flow collections
    // ------------------------------------------------------------------------------------------------------------

    private void PushFlowCollection(byte c, int indent)
    {
        if (c == '[')
        {
            PushEntry(ContainerState.FlowSequenceEntry, indent);
            SetToken(Utf8YamlTokenKind.SequenceStart, Utf8YamlTokenFlags.Flow, _pos, 1);
        }
        else
        {
            PushEntry(ContainerState.FlowMappingKey, indent);
            SetToken(Utf8YamlTokenKind.MappingStart, Utf8YamlTokenFlags.Flow, _pos, 1);
        }

        _pos++;
        _newLine = false;
    }

    private bool EndFlowCollection(Utf8YamlTokenKind kind)
    {
        PopEntry();
        switch (_depth > 0 ? (ContainerState)(PeekEntry() & 0xF) : ContainerState.None)
        {
            // The collection is the key of a flow mapping entry or of a pair
            case ContainerState.FlowMappingValue:
                SetState(ContainerState.FlowMappingJsonValue);
                break;

            case ContainerState.FlowPairValue:
                SetState(ContainerState.FlowPairJsonValue);
                break;
        }

        SetToken(kind, Utf8YamlTokenFlags.Flow, _pos, 1);
        _pos++;
        _newLine = false;
        return true;
    }

    private bool ReadFlowSequenceEntry()
    {
        SkipFlowTrivia();
        var buffer = _buffer;
        var pos = _pos;
        if (pos >= buffer.Length)
            throw CreateError(pos, "Unterminated flow sequence");

        var c = buffer[pos];
        if (c == ']')
            return EndFlowCollection(Utf8YamlTokenKind.SequenceEnd);

        if (c == ',')
            throw CreateError(pos, "Expected a node before ','");

        SetState(ContainerState.FlowSequenceAfterEntry);
        if (c == '?' && IsBlankAt(pos + 1))
        {
            PushEntry(ContainerState.FlowPairKey, CurrentIndent);
            SetToken(Utf8YamlTokenKind.MappingStart, Utf8YamlTokenFlags.Flow, pos, 0);
            return true;
        }

        if (c == ':' && IsBlankOrFlowIndicatorAt(pos + 1))
        {
            PushEntry(ContainerState.FlowPairValue, CurrentIndent);
            SetEmptyScalarScan(pos);
            var indicator = _indicator;
            _indicator = -1;
            EmitScanned();
            _pending = _token;
            _hasPending = true;
            _indicator = indicator;
            SetToken(Utf8YamlTokenKind.MappingStart, Utf8YamlTokenFlags.Flow, pos, 0);
            return true;
        }

        return ReadFlowNode(pairAllowed: true);
    }

    private bool ReadFlowSequenceAfterEntry()
    {
        SkipFlowTrivia();
        var buffer = _buffer;
        var pos = _pos;
        if (pos >= buffer.Length)
            throw CreateError(pos, "Unterminated flow sequence");

        var c = buffer[pos];
        if (c == ',')
        {
            ConsumeIndicator(pos);
            SetState(ContainerState.FlowSequenceEntry);
            return ReadFlowSequenceEntry();
        }

        if (c == ']')
            return EndFlowCollection(Utf8YamlTokenKind.SequenceEnd);

        throw CreateError(pos, "Expected ',' or ']'");
    }

    private bool ReadFlowMappingKey()
    {
        SkipFlowTrivia();
        var buffer = _buffer;
        var pos = _pos;
        if (pos >= buffer.Length)
            throw CreateError(pos, "Unterminated flow mapping");

        var c = buffer[pos];
        if (c == '}')
            return EndFlowCollection(Utf8YamlTokenKind.MappingEnd);

        if (c == ',')
            throw CreateError(pos, "Expected a key before ','");

        SetState(ContainerState.FlowMappingValue);
        if (c == '?' && IsBlankAt(pos + 1))
        {
            ConsumeExplicitKeyIndicator(pos);
            return ReadFlowKeyNode(ContainerState.FlowMappingJsonValue);
        }

        if (c == ':' && IsBlankOrFlowIndicatorAt(pos + 1))
            return EmitEmptyScalar(pos);

        return ReadFlowKeyNode(ContainerState.FlowMappingJsonValue);
    }

    // A quoted scalar is a JSON-like key, which ':' can follow without a space. A flow collection is one too, see
    // EndFlowCollection. jsonValueState is the state of the entry after such a key.
    private bool ReadFlowKeyNode(ContainerState jsonValueState)
    {
        var depth = _depth;
        var result = ReadFlowNode(pairAllowed: false);
        if (_depth == depth && _token.Kind == Utf8YamlTokenKind.Scalar && (_token.Flags & Utf8YamlTokenFlags.ScalarStyleMask) is Utf8YamlTokenFlags.SingleQuoted or Utf8YamlTokenFlags.DoubleQuoted)
        {
            SetState(jsonValueState);
        }

        return result;
    }

    // After a key that is not JSON-like, a ':' followed by a character of a plain scalar is not the value indicator
    private bool ReadFlowMappingValue(bool afterJsonKey)
    {
        SkipFlowTrivia();
        var buffer = _buffer;
        var pos = _pos;
        if (pos >= buffer.Length)
            throw CreateError(pos, "Unterminated flow mapping");

        var c = buffer[pos];
        SetState(ContainerState.FlowMappingAfterValue);
        if (c == ':' && (afterJsonKey || IsBlankOrFlowIndicatorAt(pos + 1)))
        {
            ConsumeIndicator(pos);
            return ReadFlowNode(pairAllowed: false);
        }

        if (c is (byte)',' or (byte)'}')
            return EmitEmptyScalar(_consumedEnd);

        throw CreateError(pos, "Expected ':', ',' or '}'");
    }

    private bool ReadFlowMappingAfterValue()
    {
        SkipFlowTrivia();
        var buffer = _buffer;
        var pos = _pos;
        if (pos >= buffer.Length)
            throw CreateError(pos, "Unterminated flow mapping");

        var c = buffer[pos];
        if (c == ',')
        {
            ConsumeIndicator(pos);
            SetState(ContainerState.FlowMappingKey);
            return ReadFlowMappingKey();
        }

        if (c == '}')
            return EndFlowCollection(Utf8YamlTokenKind.MappingEnd);

        throw CreateError(pos, "Expected ',' or '}'");
    }

    private bool ReadFlowPairKey()
    {
        SkipFlowTrivia();
        SetState(ContainerState.FlowPairValue);
        if (_pos < _buffer.Length && _buffer[_pos] == '?' && IsBlankAt(_pos + 1))
        {
            ConsumeExplicitKeyIndicator(_pos);
        }

        return ReadFlowKeyNode(ContainerState.FlowPairJsonValue);
    }

    // After a key that is not JSON-like, a ':' followed by a character of a plain scalar is not the value indicator
    private bool ReadFlowPairValue(bool afterJsonKey)
    {
        SkipFlowTrivia();
        var pos = _pos;
        if (pos >= _buffer.Length)
            throw CreateError(pos, "Unterminated flow sequence");

        SetState(ContainerState.FlowPairEnd);
        if (_buffer[pos] == ':' && (afterJsonKey || IsBlankOrFlowIndicatorAt(pos + 1)))
        {
            ConsumeIndicator(pos);
            return ReadFlowNode(pairAllowed: false);
        }

        return EmitEmptyScalar(_consumedEnd);
    }

    private bool ReadFlowPairEnd()
    {
        PopEntry();
        SetToken(Utf8YamlTokenKind.MappingEnd, Utf8YamlTokenFlags.Flow, _consumedEnd, 0);
        return true;
    }

    // Reads a node in the flow context. In a flow sequence, a node followed by ':' is the key of a single-pair mapping.
    private bool ReadFlowNode(bool pairAllowed)
    {
        SkipFlowTrivia();
        var buffer = _buffer;
        var propertiesStart = -1;
        while (_pos < buffer.Length && buffer[_pos] is (byte)'&' or (byte)'!')
        {
            if (propertiesStart < 0)
            {
                propertiesStart = _pos;
            }

            ScanProperty();
            SkipFlowTrivia();
        }

        _newLine = false;
        var pos = _pos;
        if (pos >= buffer.Length)
            throw CreateError(pos, "Unterminated flow collection");

        // The implicit key of a pair in a flow sequence is on one line, with its properties (YAML 1.2 §7.4.2): a node
        // whose properties are on a previous line is not a key
        if (pairAllowed && propertiesStart >= 0 && buffer[propertiesStart..pos].IndexOfAny(LineBreaks) >= 0)
        {
            pairAllowed = false;
        }

        var c = buffer[pos];
        switch (c)
        {
            case (byte)'[' or (byte)'{':
                if (pairAllowed && IsFlowCollectionImplicitKey(pos))
                {
                    var keyStart = propertiesStart >= 0 ? propertiesStart : pos;
                    _properties = NodeProperties.None;
                    PushEntry(ContainerState.FlowPairKey, CurrentIndent);
                    SetToken(Utf8YamlTokenKind.MappingStart, Utf8YamlTokenFlags.Flow, keyStart, 0);
                    _pos = keyStart;

                    // The key is read again from its first property
                    _lineStart = _buffer[..keyStart].LastIndexOfAny(LineBreaks) + 1;
                    return true;
                }

                PushFlowCollection(c, CurrentIndent);
                return true;

            case (byte)']' or (byte)'}' or (byte)',':
                return EmitEmptyScalar(_consumedEnd);

            case (byte)':' when IsBlankOrFlowIndicatorAt(pos + 1):
                if (pairAllowed && propertiesStart >= 0)
                {
                    // A pair whose key is empty, with the properties: [ !!str : value ]
                    PushEntry(ContainerState.FlowPairValue, CurrentIndent);
                    var indicator = _indicator;
                    _indicator = -1;
                    SetEmptyScalarScan(pos);
                    EmitScanned();
                    _pending = _token;
                    _hasPending = true;
                    _indicator = indicator;
                    SetToken(Utf8YamlTokenKind.MappingStart, Utf8YamlTokenFlags.Flow, propertiesStart, 0);
                    return true;
                }

                return EmitEmptyScalar(_consumedEnd);

            case (byte)'|' or (byte)'>':
                throw CreateError(pos, "A block scalar is not allowed in a flow collection");

            case (byte)'*':
                if (propertiesStart >= 0)
                    throw CreateError(pos, "An alias cannot have properties");

                ScanAlias();
                CheckKeyColon(flow: true, jsonLike: false);
                break;

            case (byte)'"':
                ScanDoubleQuoted(CurrentIndent);
                CheckKeyColon(flow: true, jsonLike: true);
                break;

            case (byte)'\'':
                ScanSingleQuoted(CurrentIndent);
                CheckKeyColon(flow: true, jsonLike: true);
                break;

            default:
                ValidatePlainStart(pos, flow: true);
                ScanPlainScalar(CurrentIndent, flow: true);
                break;
        }

        // A plain scalar on several lines is the key of a flow mapping, but not of a pair of a flow sequence
        if (pairAllowed && _scanColon >= 0 && !_scanMultiLine)
        {
            CheckImplicitKeyLength();
            PushEntry(ContainerState.FlowPairJsonValue, CurrentIndent);
            var indicator = _indicator;
            _indicator = -1;
            EmitScanned();
            _pending = _token;
            _hasPending = true;
            _indicator = indicator;
            SetToken(Utf8YamlTokenKind.MappingStart, Utf8YamlTokenFlags.Flow, propertiesStart >= 0 ? propertiesStart : _scanStart, 0);
            _pos = _scanColon;
            return true;
        }

        EmitScanned();
        return true;
    }

    // Looks ahead over a flow collection on a single line to know whether ':' follows it. A quote starts a quoted
    // scalar only at the start of a node: inside a plain scalar, it is a character of the scalar.
    private readonly bool IsFlowCollectionImplicitKey(int start)
    {
        var buffer = _buffer;
        var depth = 0;
        var p = start;
        var atNodeStart = true;
        var afterJsonNode = false;
        while (p < buffer.Length)
        {
            var c = buffer[p];
            switch (c)
            {
                case (byte)'[' or (byte)'{':
                    depth++;
                    atNodeStart = true;
                    afterJsonNode = false;
                    break;

                case (byte)']' or (byte)'}':
                    depth--;
                    if (depth == 0)
                    {
                        p++;
                        while (p < buffer.Length && buffer[p] is (byte)' ' or (byte)'\t')
                        {
                            p++;
                        }

                        return p < buffer.Length && buffer[p] == ':';
                    }

                    atNodeStart = false;
                    afterJsonNode = true;
                    break;

                case (byte)',':
                    atNodeStart = true;
                    afterJsonNode = false;
                    break;

                // An explicit key: its node starts after the indicator, and a quoted scalar there is skipped as a whole
                case (byte)'?' when atNodeStart && IsBlankAt(p + 1):
                    afterJsonNode = false;
                    break;

                case (byte)':' when afterJsonNode || IsBlankOrFlowIndicatorAt(p + 1):
                    // The value of a pair starts after the ':' of a key
                    atNodeStart = true;
                    afterJsonNode = false;
                    break;

                case (byte)' ' or (byte)'\t':
                    break;

                case (byte)'\r' or (byte)'\n':
                    return false;

                case (byte)'#' when buffer[p - 1] is (byte)' ' or (byte)'\t':
                    return false;

                case (byte)'!' when atNodeStart && p + 1 < buffer.Length && buffer[p + 1] == '<':
                    // A verbatim tag ends at its '>': it can contain flow indicators
                    var verbatimEnd = buffer[(p + 2)..].IndexOfAny((byte)'>', (byte)'\r', (byte)'\n');
                    if (verbatimEnd < 0 || buffer[p + 2 + verbatimEnd] != '>')
                        return false;

                    p += 2 + verbatimEnd;
                    break;

                case (byte)'&' or (byte)'!' when atNodeStart:
                    // A property: the node starts after it
                    while (p + 1 < buffer.Length && !IsBlankOrBreak(buffer[p + 1]) && !IsFlowIndicator(buffer[p + 1]))
                    {
                        p++;
                    }

                    break;

                case (byte)'"' or (byte)'\'' when atNodeStart:
                    p++;
                    while (true)
                    {
                        if (p >= buffer.Length || buffer[p] is (byte)'\r' or (byte)'\n')
                            return false;

                        if (buffer[p] == '\\' && c == '"')
                        {
                            // An escaped line break continues the scalar on the next line: an implicit key is on one line
                            if (p + 1 < buffer.Length && buffer[p + 1] is (byte)'\r' or (byte)'\n')
                                return false;

                            p += 2;
                            continue;
                        }

                        if (buffer[p] == c)
                        {
                            if (c == '\'' && p + 1 < buffer.Length && buffer[p + 1] == '\'')
                            {
                                p += 2;
                                continue;
                            }

                            break;
                        }

                        p++;
                    }

                    atNodeStart = false;
                    afterJsonNode = true;
                    break;

                default:
                    atNodeStart = false;
                    afterJsonNode = false;
                    break;
            }

            p++;
        }

        return false;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Scalars, aliases and properties
    // ------------------------------------------------------------------------------------------------------------

    private void SetEmptyScalarScan(int position)
    {
        _scanKind = Utf8YamlTokenKind.Scalar;
        _scanFlags = Utf8YamlTokenFlags.Plain | Utf8YamlTokenFlags.RawValue;
        _scanStart = position;
        _scanEnd = position;
        _scanValueStart = position;
        _scanValueEnd = position;
        _scanBlockIndent = 0;
        _scanMultiLine = false;
    }

    private bool EmitEmptyScalar(int position)
    {
        SetEmptyScalarScan(position);
        EmitScanned();
        return true;
    }

    private bool EmitEmptyScalar(int position, NodeProperties outerProperties)
    {
        MergeProperties(ref _properties, outerProperties);
        return EmitEmptyScalar(position);
    }

    private void ScanAlias()
    {
        var start = _pos;
        var end = ScanName(start + 1);
        _scanKind = Utf8YamlTokenKind.Alias;
        _scanFlags = Utf8YamlTokenFlags.None;
        _scanStart = start;
        _scanEnd = end;
        _scanValueStart = start + 1;
        _scanValueEnd = end;
        _scanMultiLine = false;
        _scanColon = -1;
        _pos = end;
    }

    private readonly int ScanName(int start)
    {
        var buffer = _buffer;
        var end = start;
        while (true)
        {
            var index = buffer[end..].IndexOfAny(NameScanStops);
            if (index < 0)
            {
                end = buffer.Length;
                break;
            }

            end += index;
            var c = buffer[end];
            if (IsBlankOrBreak(c) || IsFlowIndicator(c))
                break;

            end += ValidateCharacter(end, quoted: false);
        }

        if (end == start)
            throw CreateError(start, "Expected the name of an anchor or an alias");

        return end;
    }

    private void ScanProperty()
    {
        var buffer = _buffer;
        var start = _pos;
        int end;
        if (buffer[start] == '&')
        {
            if (_properties.AnchorStart >= 0)
                throw CreateError(start, "A node cannot have two anchors");

            end = ScanName(start + 1);
            _properties.AnchorStart = start;
            _properties.AnchorLength = end - start;
        }
        else
        {
            if (_properties.TagStart >= 0)
                throw CreateError(start, "A node cannot have two tags");

            end = ScanTag(start);
            _properties.TagStart = start;
            _properties.TagLength = end - start;
        }

        // A property is separated from the node by a space; an empty node can end right after it in a flow collection
        if (end < buffer.Length && !IsBlankOrBreak(buffer[end]) && buffer[end] is not ((byte)',' or (byte)']' or (byte)'}'))
            throw CreateError(end, "Expected a space after the node property");

        _pos = end;
        _consumedEnd = end;
    }

    // Scans a tag: '!<verbatim>', '!', '!suffix', '!!suffix' or '!handle!suffix'. Returns its end.
    private readonly int ScanTag(int start)
    {
        var buffer = _buffer;
        if (start + 1 < buffer.Length && buffer[start + 1] == '<')
        {
            var contentStart = start + 2;
            var length = buffer[contentStart..].IndexOfAnyExcept(VerbatimTagCharacters);
            if (length < 0 || buffer[contentStart + length] != '>')
                throw CreateError(start, "Expected '>' at the end of the verbatim tag");

            var content = buffer.Slice(contentStart, length);
            ValidateTagEscapes(content, start);
            if (content.IsEmpty || content.SequenceEqual("!"u8) || (content[0] != '!' && !HasDecodedUriScheme(content)))
                throw CreateError(start, "A verbatim tag must be a local tag or a URI");

            return contentStart + length + 1;
        }

        // The handle: '!', '!!' or '!word!'
        var p = start + 1;
        while (p < buffer.Length && (char.IsAsciiLetterOrDigit((char)buffer[p]) || buffer[p] is (byte)'-' or (byte)'_'))
        {
            p++;
        }

        var suffixStart = start + 1;
        if (p < buffer.Length && buffer[p] == '!')
        {
            var handle = buffer[start..(p + 1)];
            if (handle.Length > 2 && FindTagDirective(handle) is null)
                throw CreateError(start, "The tag handle " + Encoding.UTF8.GetString(handle) + " is not declared by a %TAG directive");

            suffixStart = p + 1;
        }

        var suffixLength = buffer[suffixStart..].IndexOfAnyExcept(TagCharacters);
        var end = suffixLength < 0 ? buffer.Length : suffixStart + suffixLength;
        if (end == suffixStart && suffixStart != start + 1)
            throw CreateError(start, "Expected the suffix of the tag");

        ValidateTagEscapes(buffer[suffixStart..end], start);
        return end;

        // The scheme is checked on the decoded tag, like the scanner-based parser: "%6Aag:x" is "jag:x"
        static bool HasDecodedUriScheme(ReadOnlySpan<byte> value)
        {
            if (!value.Contains((byte)'%'))
                return HasUriScheme(value);

            var decoded = value.Length <= 256 ? stackalloc byte[256] : new byte[value.Length];
            return TryDecodeTag(value, decoded, out var length) && length > 0 && HasUriScheme(decoded[..length]);
        }

        static bool HasUriScheme(ReadOnlySpan<byte> value)
        {
            if (!char.IsAsciiLetter((char)value[0]))
                return false;

            for (var i = 1; i < value.Length; i++)
            {
                var c = value[i];
                if (c == ':')
                    return true;

                if (!char.IsAsciiLetterOrDigit((char)c) && c is not ((byte)'+' or (byte)'-' or (byte)'.'))
                    return false;
            }

            return false;
        }
    }

    // A '%' starts a URI escape of two hexadecimal digits, and the escaped bytes are valid UTF-8
    private readonly void ValidateTagEscapes(ReadOnlySpan<byte> value, int tagStart)
    {
        if (!value.Contains((byte)'%'))
            return;

        // The decoded tag is never longer than its escaped form
        Span<byte> decoded = value.Length <= 256 ? stackalloc byte[256] : new byte[value.Length];
        if (!TryDecodeTag(value, decoded, out var written) || !System.Text.Unicode.Utf8.IsValid(decoded[..written]))
            throw CreateError(tagStart, "Invalid URI escape in the tag");
    }

    private static bool TryDecodeTag(ReadOnlySpan<byte> value, Span<byte> destination, out int written)
    {
        written = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '%')
            {
                if (i + 2 >= value.Length || !byte.TryParse(value.Slice(i + 1, 2), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out c))
                    return false;

                i += 2;
            }

            if (written >= destination.Length)
                return false;

            destination[written++] = c;
        }

        return true;
    }

    /// <summary>Gets the %TAG directives of the current document, from its start token to its end.</summary>
    internal readonly TagDirective[]? DocumentTagDirectives => _tagDirectives;

    /// <summary>Decodes the URI escapes of the prefix of a %TAG directive, which the reader validated.</summary>
    internal static byte[] DecodeTagPrefix(ReadOnlySpan<byte> prefix)
    {
        var buffer = new byte[prefix.Length];
        _ = TryDecodeTag(prefix, buffer, out var length);
        return buffer[..length];
    }

    private readonly TagDirective? FindTagDirective(ReadOnlySpan<byte> handle)
    {
        if (_tagDirectives is not null)
        {
            foreach (var directive in _tagDirectives)
            {
                if (handle.SequenceEqual(directive.Handle))
                    return directive;
            }
        }

        return null;
    }

    /// <summary>Gets the tag of the current node resolved with the %TAG directives, such as <c>tag:yaml.org,2002:str</c>; <c>!</c> for the non-specific tag; <see langword="null"/> without tag.</summary>
    public readonly string? GetTag()
    {
        if (_token.Properties.TagStart < 0)
            return null;

        var tag = _buffer.Slice(_token.Properties.TagStart, _token.Properties.TagLength);
        if (tag.Length == 1)
            return "!";

        ReadOnlySpan<byte> prefix;
        ReadOnlySpan<byte> suffix;
        if (tag[1] == '<')
        {
            prefix = default;
            suffix = tag[2..^1];
        }
        else
        {
            var secondBang = tag[1..].IndexOf((byte)'!');
            var handle = secondBang < 0 ? tag[..1] : tag[..(secondBang + 2)];
            suffix = tag[handle.Length..];
            prefix = FindTagDirective(handle) is { } directive ? directive.Prefix : handle.Length == 2 ? "tag:yaml.org,2002:"u8 : "!"u8;
        }

        // The URI escapes of the prefix and of the suffix are decoded
        var buffer = new byte[prefix.Length + suffix.Length];
        _ = TryDecodeTag(prefix, buffer, out var prefixLength);
        _ = TryDecodeTag(suffix, buffer.AsSpan(prefixLength), out var suffixLength);
        return Encoding.UTF8.GetString(buffer, 0, prefixLength + suffixLength);
    }

    // Sets _scanColon when ':' follows the scanned scalar or alias on the same line. In a flow collection, it can follow a
    // JSON-like key, a quoted scalar, without a space; it is followed by a space or a flow indicator after another key.
    private void CheckKeyColon(bool flow, bool jsonLike = false)
    {
        _scanColon = -1;
        if (_scanMultiLine)
            return;

        var buffer = _buffer;
        var p = _pos;
        while (p < buffer.Length && buffer[p] is (byte)' ' or (byte)'\t')
        {
            p++;
        }

        if (p < buffer.Length && buffer[p] == ':' && (flow ? jsonLike || IsBlankOrFlowIndicatorAt(p + 1) : IsBlankAt(p + 1)))
        {
            _scanColon = p;
        }
    }

    private readonly void ValidatePlainStart(int pos, bool flow)
    {
        var buffer = _buffer;
        var c = buffer[pos];
        switch (c)
        {
            case (byte)',' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'#' or (byte)'&' or (byte)'*' or (byte)'!' or (byte)'|'
                or (byte)'>' or (byte)'\'' or (byte)'"' or (byte)'%' or (byte)'@' or (byte)'`':
                throw CreateError(pos, "Unexpected character at the start of a plain scalar");

            case (byte)'-' or (byte)'?' or (byte)':':
                if (pos + 1 >= buffer.Length || IsBlankOrBreak(buffer[pos + 1]) || (flow && IsFlowIndicator(buffer[pos + 1])))
                    throw CreateError(pos, "Unexpected indicator at the start of a plain scalar");

                break;

            case < 0x20 and not (byte)'\t':
                throw CreateError(pos, "Unexpected control character");
        }
    }

    // Scans one line of a plain scalar from start, which is not a space. Returns why the line stopped.
    private readonly PlainStop ScanPlainLine(int start, bool flow, out int contentEnd, out int stopPosition)
    {
        var buffer = _buffer;
        var stops = flow ? FlowPlainScanStops : BlockPlainScanStops;
        var p = start;
        PlainStop stop;
        while (true)
        {
            var index = buffer[p..].IndexOfAny(stops);
            if (index < 0)
            {
                p = buffer.Length;
                stop = PlainStop.EndOfInput;
                break;
            }

            p += index;
            var c = buffer[p];
            if (c == ':')
            {
                var next = p + 1;
                if (next >= buffer.Length || IsBlankOrBreak(buffer[next]) || (flow && IsFlowIndicator(buffer[next])))
                {
                    stop = PlainStop.Colon;
                    break;
                }

                p = next;
                continue;
            }

            if (c == '#')
            {
                if (p > start && buffer[p - 1] is (byte)' ' or (byte)'\t')
                {
                    stop = PlainStop.Comment;
                    break;
                }

                p++;
                continue;
            }

            if (c is (byte)'\r' or (byte)'\n')
            {
                stop = PlainStop.LineBreak;
                break;
            }

            if (flow && IsFlowIndicator(c))
            {
                stop = PlainStop.FlowIndicator;
                break;
            }

            p += ValidateCharacter(p, quoted: false);
        }

        stopPosition = p;
        var end = p;
        while (end > start && buffer[end - 1] is (byte)' ' or (byte)'\t')
        {
            end--;
        }

        contentEnd = end;
        return stop;
    }

    private void ScanPlainScalar(int parentIndent, bool flow)
    {
        var buffer = _buffer;
        var start = _pos;
        var stop = ScanPlainLine(start, flow, out var end, out var stopPosition);
        _scanColon = stop == PlainStop.Colon ? stopPosition : -1;
        var multiLine = false;
        var nextPosition = stopPosition;
        if (stop == PlainStop.LineBreak)
        {
            // Continuation lines: more indented than the parent node, not a comment and not a document marker
            while (true)
            {
                var q = stopPosition;
                var tabInEmptyLine = -1;
                int lineStart;
                int indentEnd;
                while (true)
                {
                    q = SkipLineBreak(buffer, q);
                    lineStart = q;
                    while (q < buffer.Length && buffer[q] == ' ')
                    {
                        q++;
                    }

                    indentEnd = q;
                    while (q < buffer.Length && buffer[q] is (byte)' ' or (byte)'\t')
                    {
                        q++;
                    }

                    if (q < buffer.Length && buffer[q] is (byte)'\r' or (byte)'\n')
                    {
                        // An empty line inside the scalar is indented by spaces: a tab is allowed only after the indentation.
                        // In a flow collection, the indentation is the one of the block collection that contains it.
                        if (q > indentEnd && indentEnd - lineStart <= parentIndent && tabInEmptyLine < 0)
                        {
                            tabInEmptyLine = indentEnd;
                        }

                        continue;
                    }

                    break;
                }

                if (q >= buffer.Length
                    || indentEnd - lineStart <= parentIndent
                    || buffer[q] == '#'
                    || (q == lineStart && IsDocumentMarkerAt(q))
                    || (flow && (IsFlowIndicator(buffer[q]) || (buffer[q] == ':' && IsBlankOrFlowIndicatorAt(q + 1))))
                    || (!flow && buffer[q] == ':' && IsBlankAt(q + 1)))
                {
                    // Not a continuation line: the next token starts at q
                    _scanKind = Utf8YamlTokenKind.Scalar;
                    _scanFlags = multiLine ? Utf8YamlTokenFlags.Plain : Utf8YamlTokenFlags.Plain | Utf8YamlTokenFlags.RawValue;
                    _scanStart = start;
                    _scanEnd = end;
                    _scanValueStart = start;
                    _scanValueEnd = end;
                    _scanMultiLine = multiLine;
                    _pos = q;
                    _lineStart = lineStart;
                    _newLine = true;
                    return;
                }

                if (tabInEmptyLine >= 0)
                    throw CreateError(tabInEmptyLine, "A tab cannot be used for indentation");

                stop = ScanPlainLine(q, flow, out var lineEnd, out stopPosition);
                multiLine = true;
                end = lineEnd;
                if (stop == PlainStop.Colon)
                {
                    if (!flow)
                        throw CreateError(stopPosition, "An implicit key cannot span several lines");

                    _scanColon = stopPosition;
                }

                if (stop != PlainStop.LineBreak)
                {
                    nextPosition = stopPosition;
                    break;
                }
            }
        }

        _scanKind = Utf8YamlTokenKind.Scalar;
        _scanFlags = multiLine ? Utf8YamlTokenFlags.Plain : Utf8YamlTokenFlags.Plain | Utf8YamlTokenFlags.RawValue;
        _scanStart = start;
        _scanEnd = end;
        _scanValueStart = start;
        _scanValueEnd = end;
        _scanMultiLine = multiLine;
        _pos = nextPosition;
    }

    private void ScanDoubleQuoted(int parentIndent)
    {
        var buffer = _buffer;
        var start = _pos;
        var p = start + 1;
        var raw = true;
        var multiLine = false;
        while (true)
        {
            var index = buffer[p..].IndexOfAny(DoubleQuotedScanStops);
            if (index < 0)
                throw CreateError(start, "Unterminated double-quoted scalar");

            p += index;
            var c = buffer[p];
            if (c == '"')
                break;

            if (c is not ((byte)'\\' or (byte)'\r' or (byte)'\n'))
            {
                p += ValidateCharacter(p, quoted: true);
                continue;
            }

            raw = false;
            if (c == '\\')
            {
                p++;
                if (p >= buffer.Length)
                    throw CreateError(start, "Unterminated double-quoted scalar");

                if (buffer[p] is (byte)'\r' or (byte)'\n')
                {
                    multiLine = true;
                    p = SkipLineBreak(buffer, p);
                    CheckQuotedScalarLine(p, parentIndent);
                }
                else
                {
                    p = ValidateEscape(p);
                }

                continue;
            }

            multiLine = true;
            p = SkipLineBreak(buffer, p);
            CheckQuotedScalarLine(p, parentIndent);
        }

        SetQuotedScan(start, p, raw ? Utf8YamlTokenFlags.DoubleQuoted | Utf8YamlTokenFlags.RawValue : Utf8YamlTokenFlags.DoubleQuoted, multiLine);
    }

    private void ScanSingleQuoted(int parentIndent)
    {
        var buffer = _buffer;
        var start = _pos;
        var p = start + 1;
        var raw = true;
        var multiLine = false;
        while (true)
        {
            var index = buffer[p..].IndexOfAny(SingleQuotedScanStops);
            if (index < 0)
                throw CreateError(start, "Unterminated single-quoted scalar");

            p += index;
            if (buffer[p] == '\'')
            {
                if (p + 1 < buffer.Length && buffer[p + 1] == '\'')
                {
                    raw = false;
                    p += 2;
                    continue;
                }

                break;
            }

            if (buffer[p] is not ((byte)'\r' or (byte)'\n'))
            {
                p += ValidateCharacter(p, quoted: true);
                continue;
            }

            raw = false;
            multiLine = true;
            p = SkipLineBreak(buffer, p);
            CheckQuotedScalarLine(p, parentIndent);
        }

        SetQuotedScan(start, p, raw ? Utf8YamlTokenFlags.SingleQuoted | Utf8YamlTokenFlags.RawValue : Utf8YamlTokenFlags.SingleQuoted, multiLine);
    }

    // Validates the escape sequence that starts at pos, after the '\', and returns its end
    private readonly int ValidateEscape(int pos)
    {
        var buffer = _buffer;
        var escape = buffer[pos];
        var i = pos + 1;
        Span<byte> destination = stackalloc byte[4];
        try
        {
            _ = DecodeEscape(escape, buffer, ref i, destination);
        }
        catch (SyntaxErrorException)
        {
            throw CreateError(pos - 1, "Invalid escape sequence in a double-quoted scalar");
        }

        return i;
    }

    // A continuation line of a quoted scalar is not a document marker, and is indented more than the parent node
    private readonly void CheckQuotedScalarLine(int lineStart, int parentIndent)
    {
        var buffer = _buffer;
        if (IsDocumentMarkerAt(lineStart))
            throw CreateError(lineStart, "A document marker is not allowed in a quoted scalar");

        var p = lineStart;
        while (p < buffer.Length && buffer[p] == ' ')
        {
            p++;
        }

        if (p - lineStart <= parentIndent && p < buffer.Length && buffer[p] is not ((byte)'\r' or (byte)'\n'))
            throw CreateError(p, "The continuation line of a quoted scalar must be indented");
    }

    private void SetQuotedScan(int start, int closingQuote, Utf8YamlTokenFlags flags, bool multiLine)
    {
        _scanKind = Utf8YamlTokenKind.Scalar;
        _scanFlags = flags;
        _scanStart = start;
        _scanEnd = closingQuote + 1;
        _scanValueStart = start + 1;
        _scanValueEnd = closingQuote;
        _scanMultiLine = multiLine;
        _scanColon = -1;
        _pos = closingQuote + 1;
        if (_pos < _buffer.Length && !IsBlankOrBreak(_buffer[_pos]) && !IsFlowIndicator(_buffer[_pos]) && _buffer[_pos] != ':')
            throw CreateError(_pos, "Unexpected character after a quoted scalar");
    }

    private void ScanBlockScalar(int parentIndent)
    {
        var buffer = _buffer;
        var start = _pos;
        var flags = buffer[start] == '|' ? Utf8YamlTokenFlags.Literal : Utf8YamlTokenFlags.Folded;
        var p = start + 1;
        var increment = 0;
        var chomping = 0;
        for (var i = 0; i < 2 && p < buffer.Length; i++)
        {
            var c = buffer[p];
            if (c is (byte)'+' or (byte)'-' && chomping == 0)
            {
                chomping = c == '+' ? 1 : -1;
                p++;
            }
            else if (c is >= (byte)'1' and <= (byte)'9' && increment == 0)
            {
                increment = c - '0';
                p++;
            }
            else if (c == '0')
            {
                throw CreateError(p, "The indentation indicator of a block scalar must be between 1 and 9");
            }
            else
            {
                break;
            }
        }

        var headerEnd = p;
        while (p < buffer.Length && buffer[p] is (byte)' ' or (byte)'\t')
        {
            p++;
        }

        if (p < buffer.Length && buffer[p] == '#')
        {
            if (p == headerEnd)
                throw CreateError(p, "A comment must be separated from the block scalar header");

            p = FindLineEnd(p, isComment: true);
        }

        if (p < buffer.Length && buffer[p] is not ((byte)'\r' or (byte)'\n'))
            throw CreateError(p, "Expected a comment or a line break after the block scalar header");

        var contentStart = p < buffer.Length ? SkipLineBreak(buffer, p) : buffer.Length;
        int indent;
        if (increment > 0)
        {
            indent = parentIndent >= 0 ? parentIndent + increment : increment;
        }
        else
        {
            // The indentation of the first non-empty line, at least the indentation of the leading empty lines
            var maxIndent = 0;
            var q = contentStart;
            while (true)
            {
                var lineStart = q;
                while (q < buffer.Length && buffer[q] == ' ')
                {
                    q++;
                }

                var spaces = q - lineStart;
                if (q < buffer.Length && buffer[q] is (byte)'\r' or (byte)'\n')
                {
                    maxIndent = Math.Max(maxIndent, spaces);
                    q = SkipLineBreak(buffer, q);
                    continue;
                }

                if (q < buffer.Length && spaces < maxIndent && spaces > parentIndent)
                    throw CreateError(q, "A leading empty line of a block scalar is more indented than its content");

                indent = Math.Max(maxIndent, spaces);
                break;
            }

            if (indent <= parentIndent)
            {
                indent = parentIndent + 1;
            }
        }

        // Consume the lines that are empty or indented at least as much as the content
        var position = contentStart;
        var lastContentEnd = -1;
        var terminatingLineStart = buffer.Length;
        while (position < buffer.Length)
        {
            var lineStart = position;
            if (IsDocumentMarkerAt(lineStart))
            {
                terminatingLineStart = lineStart;
                break;
            }

            var limit = lineStart + indent;
            while (position < buffer.Length && position < limit && buffer[position] == ' ')
            {
                position++;
            }

            if (position >= buffer.Length)
                break;

            var c = buffer[position];
            if (c is (byte)'\r' or (byte)'\n')
            {
                position = SkipLineBreak(buffer, position);
                continue;
            }

            if (position < limit)
            {
                if (buffer[position] == '\t')
                    throw CreateError(position, "A tab cannot be used for indentation");

                terminatingLineStart = lineStart;
                break;
            }

            var lineEnd = FindLineEnd(position, isComment: false);
            lastContentEnd = lineEnd;
            position = lineEnd < buffer.Length ? SkipLineBreak(buffer, lineEnd) : buffer.Length;
        }

        if (chomping == 1)
        {
            flags |= Utf8YamlTokenFlags.KeepChomping;
        }
        else if (chomping == -1)
        {
            flags |= Utf8YamlTokenFlags.StripChomping;
        }

        var end = chomping == 1 ? terminatingLineStart : lastContentEnd >= 0 ? lastContentEnd : headerEnd;
        _scanKind = Utf8YamlTokenKind.Scalar;
        _scanFlags = flags;
        _scanStart = start;
        _scanEnd = end;
        _scanValueStart = contentStart;
        _scanValueEnd = Math.Max(end, contentStart);
        _scanBlockIndent = indent;
        _scanMultiLine = true;
        _scanColon = -1;
        _pos = terminatingLineStart;
        _lineStart = terminatingLineStart;
        _newLine = true;
    }

    private void EmitScanned()
    {
        _token.Kind = _scanKind;
        _token.Flags = _scanFlags;
        _token.Start = _scanStart;
        _token.Length = _scanEnd - _scanStart;
        _token.ValueStart = _scanValueStart;
        _token.ValueLength = _scanValueEnd - _scanValueStart;
        _token.BlockIndent = _scanBlockIndent;
        TakeIndicatorAndProperties();
        if (_scanEnd > _consumedEnd)
        {
            _consumedEnd = _scanEnd;
        }
    }

    private void SetToken(Utf8YamlTokenKind kind, Utf8YamlTokenFlags flags, int start, int length)
    {
        _token.Kind = kind;
        _token.Flags = flags;
        _token.Start = start;
        _token.Length = length;
        _token.ValueStart = start;
        _token.ValueLength = length;
        _token.BlockIndent = 0;
        TakeIndicatorAndProperties();
        if (length > 0)
        {
            _consumedEnd = start + length;
        }
    }

    private void TakeIndicatorAndProperties()
    {
        _token.Indicator = _indicator;
        _token.ExplicitKeyIndicator = _explicitKeyIndicator;
        _token.Properties = _properties;
        _indicator = -1;
        _explicitKeyIndicator = -1;
        _properties = NodeProperties.None;
    }

    private void ConsumeIndicator(int position)
    {
        _indicator = position;
        _pos = position + 1;
        _consumedEnd = position + 1;
        _newLine = false;
    }

    private void ConsumeExplicitKeyIndicator(int position)
    {
        _explicitKeyIndicator = position;
        _pos = position + 1;
        _consumedEnd = position + 1;
        _newLine = false;
    }

    // In a flow collection, a line is indented more than the block collection that contains the flow collection
    private void SkipFlowTrivia()
    {
        SkipTrivia();
        if (_newLine && _pos < _buffer.Length)
        {
            if (IsDocumentMarker(_pos))
                throw CreateError(_pos, "A document marker is not allowed in a flow collection");

            var spaces = 0;
            while (_buffer[_lineStart + spaces] == ' ')
            {
                spaces++;
            }

            if (spaces <= CurrentIndent)
                throw CreateError(_pos, "The content of a flow collection must be indented");
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Trivia
    // ------------------------------------------------------------------------------------------------------------

    // Skips spaces, tabs, line breaks and comments
    private void SkipTrivia()
    {
        var buffer = _buffer;
        var pos = _pos;
        while ((uint)pos < (uint)buffer.Length)
        {
            var c = buffer[pos];
            if (c is (byte)' ' or (byte)'\t')
            {
                pos++;
            }
            else if (c == '\n')
            {
                pos++;
                _lineStart = pos;
                _newLine = true;
            }
            else if (c == '\r')
            {
                pos++;
                if (pos < buffer.Length && buffer[pos] == '\n')
                {
                    pos++;
                }

                _lineStart = pos;
                _newLine = true;
            }
            else if (c == '#' && (pos == 0 || buffer[pos - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' || pos == _lineStart))
            {
                pos = FindLineEnd(pos, isComment: true);
            }
            else
            {
                break;
            }
        }

        _pos = pos;
    }

    private void SkipBlanks()
    {
        var buffer = _buffer;
        var pos = _pos;
        while (pos < buffer.Length && buffer[pos] is (byte)' ' or (byte)'\t')
        {
            pos++;
        }

        _pos = pos;
    }

    // Returns the end of the content of the line: before the comment and the trailing blanks
    private readonly int FindLineContentEnd(int start)
    {
        var buffer = _buffer;
        var end = FindLineEnd(start, isComment: true);
        for (var i = start + 1; i < end; i++)
        {
            if (buffer[i] == '#' && buffer[i - 1] is (byte)' ' or (byte)'\t')
            {
                end = i;
                break;
            }
        }

        while (end > start && buffer[end - 1] is (byte)' ' or (byte)'\t')
        {
            end--;
        }

        return end;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------------------------

    private readonly bool IsDocumentMarker(int pos) => pos == _lineStart && IsDocumentMarkerAt(pos);

    private readonly bool IsDocumentMarker(int pos, byte c) => pos == _lineStart && DocumentMarkerAt(pos) == c;

    // pos must be the start of a line
    private readonly bool IsDocumentMarkerAt(int pos) => DocumentMarkerAt(pos) != 0;

    // Returns '-' for '---', '.' for '...', else 0. A byte order mark can precede the '---' of the next document.
    private readonly byte DocumentMarkerAt(int pos)
    {
        var buffer = _buffer;
        var afterByteOrderMark = false;
        if (pos + 3 <= buffer.Length && buffer[pos] == 0xEF && buffer[pos + 1] == 0xBB && buffer[pos + 2] == 0xBF)
        {
            pos += 3;
            afterByteOrderMark = true;
        }

        if (pos + 3 > buffer.Length)
            return 0;

        var c = buffer[pos];
        if (c is not ((byte)'-' or (byte)'.') || (afterByteOrderMark && c != '-'))
            return 0;

        return buffer[pos + 1] == c && buffer[pos + 2] == c && (pos + 3 == buffer.Length || IsBlankOrBreak(buffer[pos + 3])) ? c : (byte)0;
    }

    private readonly bool IsBlankAt(int pos) => pos >= _buffer.Length || IsBlankOrBreak(_buffer[pos]);

    private readonly bool IsBlankOrFlowIndicatorAt(int pos) => pos >= _buffer.Length || IsBlankOrBreak(_buffer[pos]) || IsFlowIndicator(_buffer[pos]);

    private static bool IsBlankOrBreak(byte c) => c is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    private static bool IsFlowIndicator(byte c) => c is (byte)',' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}';

    private static int SkipLineBreak(ReadOnlySpan<byte> buffer, int pos)
    {
        if (buffer[pos] == '\r' && pos + 1 < buffer.Length && buffer[pos + 1] == '\n')
            return pos + 2;

        return pos + 1;
    }

    // The control characters other than tab and line breaks, DEL outside quoted scalars, and the bytes of the non-ASCII characters
    private static byte[] ValidatedBytes(bool quoted)
    {
        var result = new List<byte>();
        for (var b = 0; b < 256; b++)
        {
            if ((b < 0x20 && b is not ('\t' or '\r' or '\n')) || (b == 0x7F && !quoted) || b >= 0x80)
            {
                result.Add((byte)b);
            }
        }

        return [.. result];
    }

    // Validates the character at pos, a byte of ValidatedBytes, and returns its length. Every character must be valid
    // UTF-8 and printable (YAML 1.2 §5.1); quoted scalars accept every character but the C0 controls.
    private readonly int ValidateCharacter(int pos, bool quoted, bool isComment = false)
    {
        var c = _buffer[pos];
        if (c < 0x80)
        {
            if (c == 0x7F && quoted)
                return 1;

            throw CreateError(pos, "A control character is not allowed");
        }

        var status = Rune.DecodeFromUtf8(_buffer[pos..], out var rune, out var length);
        if (status != OperationStatus.Done)
            throw CreateError(pos, "Invalid UTF-8 sequence");

        if (!quoted && (rune.Value is (>= 0x80 and <= 0x9F and not 0x85) or 0xFFFE or 0xFFFF || (rune.Value == 0xFEFF && !isComment)))
            throw CreateError(pos, "A non-printable character is not allowed outside a quoted scalar");

        return length;
    }

    // Returns the position of the next line break, or the end of the buffer, and validates the characters of the line. A
    // comment or a directive can contain a byte order mark, like the scanner-based parser accepts.
    private readonly int FindLineEnd(int start, bool isComment)
    {
        var buffer = _buffer;
        var p = start;
        while (true)
        {
            var index = buffer[p..].IndexOfAny(LineScanStops);
            if (index < 0)
                return buffer.Length;

            p += index;
            if (buffer[p] is (byte)'\r' or (byte)'\n')
                return p;

            p += ValidateCharacter(p, quoted: false, isComment);
        }
    }

    // A zero-width token can be before the start of the buffer, after the previous reader dropped the consumed bytes
    private readonly ReadOnlySpan<byte> SliceBuffer(int start, int length) => length == 0 ? default : _buffer.Slice(start, length);

    private readonly int PeekEntry() => _stackOverflow is null ? _stack[_depth - 1] : _stackOverflow[_depth - 1];

    // The indentation of the current collection; for a flow collection, the indentation of the block collection that contains it
    private readonly int CurrentIndent => _depth == 0 ? -1 : (PeekEntry() >> 4) - 1;

    private void SetState(ContainerState state)
    {
        if (_stackOverflow is null)
        {
            ref var entry = ref _stack[_depth - 1];
            entry = (entry & ~0xF) | (int)state;
        }
        else
        {
            ref var entry = ref _stackOverflow[_depth - 1];
            entry = (entry & ~0xF) | (int)state;
        }
    }

    private void PushEntry(ContainerState state, int indent)
    {
        if (_depth >= _maxDepth)
            throw YamlDepthHelper.CreateMaxDepthExceededException(_maxDepth, GetMark(_pos), GetMark(_pos), sourceName: null);

        var entry = ((indent + 1) << 4) | (int)state;
        if (_stackOverflow is null)
        {
            if (_depth < InlineStackSize)
            {
                _stack[_depth++] = entry;
                return;
            }

            _stackOverflow = new int[InlineStackSize * 2];
            ((ReadOnlySpan<int>)_stack).CopyTo(_stackOverflow);
        }
        else if (_depth == _stackOverflow.Length)
        {
            Array.Resize(ref _stackOverflow, _depth * 2);
        }

        _stackOverflow[_depth++] = entry;
    }

    private void PopEntry() => _depth--;

    private readonly SyntaxErrorException CreateError(int offset, string message)
    {
        var mark = GetMark(Math.Clamp(offset, 0, _buffer.Length));
        return new SyntaxErrorException(mark, mark, message);
    }

    /// <summary>Computes the location of a position of the buffer, like the marks of <see cref="Parser"/>: UTF-16 index, 0-based line and column.</summary>
    /// <remarks>This method reads the buffer from its start: it is meant for errors, not for every token.</remarks>
    public readonly Mark GetMark(int offset)
    {
        var buffer = _buffer;
        var line = _lineNumber;
        var lineStart = 0;
        for (var i = 0; i < offset; i++)
        {
            var c = buffer[i];
            if (c == '\n' || (c == '\r' && (i + 1 >= buffer.Length || buffer[i + 1] != '\n')))
            {
                line++;
                lineStart = i + 1;
            }
        }

        // A byte order mark at the start of a line, before a document, is not a column. The line can start before the
        // buffer, with byte order marks that a previous reader consumed: the first one is not a column, the others are.
        var column = Encoding.UTF8.GetCharCount(buffer[lineStart..offset]);
        if (lineStart == 0 && _lineByteOrderMarks > 0)
        {
            column += _lineByteOrderMarks - 1;
        }
        else if (offset >= lineStart + 3 && buffer[lineStart..].StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            column--;
        }

        var index = _charOffset + Encoding.UTF8.GetCharCount(buffer[..offset]);
        return new Mark((int)Math.Min(index, int.MaxValue), line, column);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Decoding
    // ------------------------------------------------------------------------------------------------------------

    private readonly int DecodeValue(Span<byte> destination) => DecodeValue(_buffer, in _token, destination);

    private static int DecodeValue(ReadOnlySpan<byte> buffer, in TokenData token, Span<byte> destination)
    {
        var value = token.ValueLength == 0 ? default : buffer.Slice(token.ValueStart, token.ValueLength);
        var flags = token.Flags;
        switch (flags & Utf8YamlTokenFlags.ScalarStyleMask)
        {
            case Utf8YamlTokenFlags.Literal or Utf8YamlTokenFlags.Folded:
                var end = token.ValueStart + token.ValueLength;

                // The end of the input ends the last line like a line break
                var breakAfter = token.ValueLength > 0 && (end == buffer.Length || buffer[end] is (byte)'\r' or (byte)'\n');
                return DecodeBlockScalar(value, destination, token.BlockIndent, flags, breakAfter);

            default:
                return DecodeFlowScalar(value, destination, flags & Utf8YamlTokenFlags.ScalarStyleMask);
        }
    }

    private static int DecodeFlowScalar(ReadOnlySpan<byte> value, Span<byte> destination, Utf8YamlTokenFlags style)
    {
        var stops = style switch
        {
            Utf8YamlTokenFlags.DoubleQuoted => DoubleQuotedStops,
            Utf8YamlTokenFlags.SingleQuoted => SingleQuotedStops,
            _ => LineBreaks,
        };

        var written = 0;
        var protectedEnd = 0;
        var i = 0;
        while (i < value.Length)
        {
            var index = value[i..].IndexOfAny(stops);
            if (index < 0)
            {
                value[i..].CopyTo(destination[written..]);
                written += value.Length - i;
                break;
            }

            if (index > 0)
            {
                value.Slice(i, index).CopyTo(destination[written..]);
                written += index;
                i += index;
            }

            var c = value[i];
            if (c == '\'')
            {
                // '' in a single-quoted scalar
                destination[written++] = (byte)'\'';
                protectedEnd = written;
                i += 2;
                continue;
            }

            if (c == '\\')
            {
                i++;
                var escape = value[i];
                if (escape is (byte)'\r' or (byte)'\n')
                {
                    // Escaped line break: the break and the leading blanks of the next line are removed
                    i = SkipLineBreak(value, i);
                    i = SkipBlanks(value, i);
                    while (i < value.Length && value[i] is (byte)'\r' or (byte)'\n')
                    {
                        destination[written++] = (byte)'\n';
                        i = SkipLineBreak(value, i);
                        i = SkipBlanks(value, i);
                    }

                    protectedEnd = written;
                    continue;
                }

                i++;
                written += DecodeEscape(escape, value, ref i, destination[written..]);
                protectedEnd = written;
                continue;
            }

            // Line folding: the trailing blanks are removed, one line break becomes a space, the others are kept
            while (written > protectedEnd && destination[written - 1] is (byte)' ' or (byte)'\t')
            {
                written--;
            }

            i = SkipLineBreak(value, i);
            i = SkipBlanks(value, i);
            var breaks = 0;
            while (i < value.Length && value[i] is (byte)'\r' or (byte)'\n')
            {
                breaks++;
                i = SkipLineBreak(value, i);
                i = SkipBlanks(value, i);
            }

            if (breaks == 0)
            {
                destination[written++] = (byte)' ';
            }
            else
            {
                destination.Slice(written, breaks).Fill((byte)'\n');
                written += breaks;
            }

            protectedEnd = written;
        }

        return written;

        static int SkipBlanks(ReadOnlySpan<byte> value, int i)
        {
            while (i < value.Length && value[i] is (byte)' ' or (byte)'\t')
            {
                i++;
            }

            return i;
        }
    }

    private static int DecodeEscape(byte escape, ReadOnlySpan<byte> value, ref int i, Span<byte> destination)
    {
        var codePoint = escape switch
        {
            (byte)'0' => 0,
            (byte)'a' => 7,
            (byte)'b' => 8,
            (byte)'t' or (byte)'\t' => 9,
            (byte)'n' => 10,
            (byte)'v' => 11,
            (byte)'f' => 12,
            (byte)'r' => 13,
            (byte)'e' => 0x1B,
            (byte)' ' => ' ',
            (byte)'"' => '"',
            (byte)'/' => '/',
            (byte)'\\' => '\\',
            (byte)'N' => 0x85,
            (byte)'_' => 0xA0,
            (byte)'L' => 0x2028,
            (byte)'P' => 0x2029,
            (byte)'x' => ParseHex(value, ref i, 2),
            (byte)'u' => ParseUtf16(value, ref i),
            (byte)'U' => ParseHex(value, ref i, 8),
            _ => throw new SyntaxErrorException("Invalid escape sequence in a double-quoted scalar"),
        };

        if (!Rune.TryCreate(codePoint, out var rune))
            throw new SyntaxErrorException("Invalid Unicode escape in a double-quoted scalar");

        return rune.EncodeToUtf8(destination);

        // \uXXXX, or a surrogate pair written as two escapes
        static int ParseUtf16(ReadOnlySpan<byte> value, ref int i)
        {
            var codePoint = ParseHex(value, ref i, 4);
            if (codePoint is >= 0xD800 and <= 0xDBFF && i + 6 <= value.Length && value[i] == '\\' && value[i + 1] == 'u')
            {
                var next = i + 2;
                var low = ParseHex(value, ref next, 4);
                if (low is >= 0xDC00 and <= 0xDFFF)
                {
                    i = next;
                    return 0x10000 + ((codePoint - 0xD800) << 10) + (low - 0xDC00);
                }
            }

            return codePoint;
        }

        static int ParseHex(ReadOnlySpan<byte> value, ref int i, int length)
        {
            if (i + length > value.Length)
                throw new SyntaxErrorException("Invalid escape sequence in a double-quoted scalar");

            var result = 0;
            for (var j = 0; j < length; j++)
            {
                var digit = HexConverter(value[i + j]);
                if (digit < 0)
                    throw new SyntaxErrorException("Invalid escape sequence in a double-quoted scalar");

                result = checked((result << 4) | digit);
            }

            i += length;
            return result;
        }

        static int HexConverter(byte c) => c switch
        {
            >= (byte)'0' and <= (byte)'9' => c - '0',
            >= (byte)'a' and <= (byte)'f' => c - 'a' + 10,
            >= (byte)'A' and <= (byte)'F' => c - 'A' + 10,
            _ => -1,
        };
    }

    private static int DecodeBlockScalar(ReadOnlySpan<byte> value, Span<byte> destination, int indent, Utf8YamlTokenFlags flags, bool breakAfter)
    {
        var literal = (flags & Utf8YamlTokenFlags.ScalarStyleMask) == Utf8YamlTokenFlags.Literal;
        var written = 0;
        var leadingBreak = false;
        var leadingBlank = false;
        var trailingBreaks = 0;
        var i = 0;
        EatEmptyLines(value, indent, ref i, ref trailingBreaks);
        while (i < value.Length)
        {
            var trailingBlank = value[i] is (byte)' ' or (byte)'\t';
            if (!literal && leadingBreak && !leadingBlank && !trailingBlank)
            {
                if (trailingBreaks == 0)
                {
                    destination[written++] = (byte)' ';
                }
            }
            else if (leadingBreak)
            {
                destination[written++] = (byte)'\n';
            }

            if (trailingBreaks > 0)
            {
                destination.Slice(written, trailingBreaks).Fill((byte)'\n');
                written += trailingBreaks;
                trailingBreaks = 0;
            }

            leadingBlank = trailingBlank;
            var index = value[i..].IndexOfAny(LineBreaks);
            var lineEnd = index < 0 ? value.Length : i + index;
            value[i..lineEnd].CopyTo(destination[written..]);
            written += lineEnd - i;
            if (lineEnd < value.Length)
            {
                i = SkipLineBreak(value, lineEnd);
                leadingBreak = true;
            }
            else
            {
                i = lineEnd;
                leadingBreak = breakAfter;
            }

            EatEmptyLines(value, indent, ref i, ref trailingBreaks);
        }

        if ((flags & Utf8YamlTokenFlags.StripChomping) == Utf8YamlTokenFlags.None && leadingBreak)
        {
            destination[written++] = (byte)'\n';
        }

        if ((flags & Utf8YamlTokenFlags.KeepChomping) != Utf8YamlTokenFlags.None && trailingBreaks > 0)
        {
            destination.Slice(written, trailingBreaks).Fill((byte)'\n');
            written += trailingBreaks;
        }

        return written;

        static void EatEmptyLines(ReadOnlySpan<byte> value, int indent, ref int i, ref int trailingBreaks)
        {
            while (i < value.Length)
            {
                var lineStart = i;
                while (i < value.Length && i - lineStart < indent && value[i] == ' ')
                {
                    i++;
                }

                if (i < value.Length && value[i] is (byte)'\r' or (byte)'\n')
                {
                    i = SkipLineBreak(value, i);
                    trailingBreaks++;
                    continue;
                }

                if (i >= value.Length)
                {
                    // A last line of spaces without a line break: the end of the input ends the line
                    trailingBreaks++;
                    return;
                }

                if (i - lineStart < indent)
                {
                    // Trailing spaces of a kept block scalar at the end of the input
                    i = value.Length;
                }

                return;
            }
        }
    }

    [StructLayout(LayoutKind.Auto)]
    internal struct TokenData
    {
        public Utf8YamlTokenKind Kind;
        public Utf8YamlTokenFlags Flags;
        public int Start;
        public int Length;
        public int ValueStart;
        public int ValueLength;
        public int BlockIndent;
        public int Indicator;
        public int ExplicitKeyIndicator;
        public NodeProperties Properties;
    }

    [StructLayout(LayoutKind.Auto)]
    internal struct NodeProperties
    {
        public static readonly NodeProperties None = new() { AnchorStart = -1, TagStart = -1 };

        public int AnchorStart;
        public int AnchorLength;
        public int TagStart;
        public int TagLength;

        public readonly bool HasValue => AnchorStart >= 0 || TagStart >= 0;
    }

    /// <summary>The state that a reader passes to the next one, see <see cref="Utf8YamlReaderState"/>.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal struct StateData
    {
        public bool IsInitialized;
        public long BufferStart;
        public int LineNumber;
        public long CharOffset;
        public int LineByteOrderMarks;
        public int Pos;
        public int LineStart;
        public int ConsumedEnd;
        public int Indicator;
        public int ExplicitKeyIndicator;
        public bool NewLine;
        public bool HasPending;
        public bool HasDirectives;
        public bool HasYamlDirective;
        public DocumentState DocumentState;
        public int Depth;
        public StateStack Stack;
        public int[]? StackOverflow;
        public NodeProperties Properties;
        public TokenData Pending;
        public TagDirective[]? TagDirectives;
    }

    /// <summary>A %TAG directive: the handle, such as <c>!e!</c>, and the prefix it stands for.</summary>
    internal sealed class TagDirective(byte[] handle, byte[] prefix)
    {
        public byte[] Handle { get; } = handle;

        public byte[] Prefix { get; } = prefix;
    }

    [InlineArray(InlineStackSize)]
    internal struct StateStack
    {
        private int _element;
    }
}
