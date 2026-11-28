using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Serialization;
using TagDirective = Meziantou.Framework.Yaml.Tokens.TagDirective;
using VersionDirective = Meziantou.Framework.Yaml.Tokens.VersionDirective;

namespace Meziantou.Framework.Yaml;

/// <summary>
/// Reads the node tokens of a string, UTF-8 bytes, a <see cref="Stream"/> or a <see cref="TextReader"/> with a
/// <see cref="Utf8YamlReader"/>, from a class. A UTF-8 stream is read by blocks as is; a text reader, or a stream with a
/// UTF-16 or UTF-32 byte order mark, is read by blocks and encoded to UTF-8. The value of a scalar is decoded and the
/// locations are computed only when they are asked for.
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The only reader it creates is a StreamReader that leaves the stream of the caller open: disposing it releases nothing")]
internal sealed class Utf8YamlTokenSource
{
    private const int BlockSize = 16 * 1024;

    // The longest text between two locations that GetMark reads with a loop instead of searches
    private const int ShortSegmentLength = 64;

    // A seekable UTF-8 stream up to this size is read whole: the locations are then computed only for errors, and the
    // buffer is never refilled. Larger streams are read by blocks, so the memory does not grow with them.
    private const int WholeStreamMaxLength = 1024 * 1024;

    // A reader is created from the saved state, and the state saved again, once per batch of tokens instead of once per token
    private const int BatchSize = 64;

    // The stack is probed when a collection starts at the first nesting level, then once every this many levels
    private const int StackProbeInterval = 8;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // The batch of the last source of the thread that was given back: the serializer creates a source per call
    [ThreadStatic]
    private static BatchedToken[]? s_cachedBatch;

    private readonly TextReader? _textReader;
    private readonly Encoder? _encoder;
    private readonly char[]? _chars;
    private readonly Stream? _stream;
    private bool _isPooledBuffer;

    // The pooled buffer was given back: the locations of the input can no longer be computed
    private bool _bufferReturned;

    private byte[] _buffer;
    private int _start;
    private int _length;
    private bool _isFinalBlock;
    private Utf8YamlReaderState _state;

    // The tokens read by the last reader, handed out one at a time. They are relative to the buffer at _batchBufferIndex.
    private BatchedToken[] _batch = RentBatch();
    private int _batchCount;
    private int _batchIndex;
    private int _batchBufferIndex;
    private long _batchBufferOffset;
    private int _batchLineNumber;
    private long _batchCharOffset;

    // The error the reader found after the tokens of the batch: thrown when the next token is asked for
    private ExceptionDispatchInfo? _batchError;

    // The current token, relative to the buffer at _start: the entry of the batch at _current, or _token once the batch is
    // read again or at the end of the input. The entry is not copied, as most tokens are only looked at once.
    private Utf8YamlReader.TokenData _token;
    private int _current = -1;
    private string? _anchor;
    private string? _tag;

    // The number of collections that started and did not end, for the stack probe
    private int _nesting;
    private long _tokenBufferOffset;
    private int _tokenBufferIndex;
    private int _tokenLineNumber;
    private long _tokenCharOffset;
    private string? _value;
    private Mark _startMark;
    private Mark _endMark;

    // The locations of the current token that were computed: they are kept until the next token
    private MarkKinds _marks;

    // Directives of the next document
    private VersionDirective? _pendingVersion;
    private TagDirectiveCollection? _pendingTags;
    private readonly SyntaxErrorException? _pendingError;

    // Location of the last computed position
    private long _markOffset;
    private int _markLine;
    private long _markLineStartOffset;
    private long _markCharIndex;
    private long _markLineStartCharIndex;

    // The line of the location starts with a byte order mark, which is not one of its columns
    private bool _markLineStartsAfterByteOrderMark;

    public Utf8YamlTokenSource(string yaml, int maxDepth, string? sourceName)
    {
        SourceName = sourceName;
        _state = new Utf8YamlReaderState(new Utf8YamlReaderOptions { MaxDepth = maxDepth });
        try
        {
            _buffer = StrictUtf8.GetBytes(yaml);
        }
        catch (EncoderFallbackException)
        {
            // A lone surrogate is not a valid character. The text is read like the TextReader overloads read it, a line at a
            // time, so the error is reported when the reader gets to its line, and the errors of the lines before it first.
            _textReader = new StringReader(yaml);
            _encoder = StrictUtf8.GetEncoder();
            _chars = new char[BlockSize];
            _buffer = new byte[StrictUtf8.GetMaxByteCount(BlockSize) * 2];
            try
            {
                Fill();
            }
            catch (SyntaxErrorException error)
            {
                // Reported by the first read, like other errors
                _pendingError = error;
            }

            SkipByteOrderMarkColumn();
            return;
        }

        _length = _buffer.Length;
        _isFinalBlock = true;
        KeepsWholeInput = true;
        SkipByteOrderMarkColumn();
    }

    public Utf8YamlTokenSource(TextReader reader, int maxDepth, string? sourceName)
    {
        SourceName = sourceName;
        _state = new Utf8YamlReaderState(new Utf8YamlReaderOptions { MaxDepth = maxDepth });
        _textReader = reader;
        _encoder = StrictUtf8.GetEncoder();
        _chars = new char[BlockSize];
        _buffer = new byte[StrictUtf8.GetMaxByteCount(BlockSize) * 2];
        Fill();
        SkipByteOrderMarkColumn();
    }

    public Utf8YamlTokenSource(ReadOnlySpan<byte> utf8Yaml, int maxDepth, string? sourceName)
    {
        SourceName = sourceName;
        _state = new Utf8YamlReaderState(new Utf8YamlReaderOptions { MaxDepth = maxDepth });

        // The source is a class, which cannot keep a span: the bytes are copied to a pooled buffer, given back by ReturnPooledBuffers
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(utf8Yaml.Length, 1));
        _isPooledBuffer = true;
        utf8Yaml.CopyTo(_buffer);
        _length = utf8Yaml.Length;
        _isFinalBlock = true;
        KeepsWholeInput = true;
        SkipByteOrderMarkColumn();
    }

    public Utf8YamlTokenSource(Stream stream, int maxDepth, string? sourceName)
    {
        SourceName = sourceName;
        _state = new Utf8YamlReaderState(new Utf8YamlReaderOptions { MaxDepth = maxDepth });
        _buffer = new byte[BlockSize * 2];

        // The encoding is detected from the byte order mark, like StreamReader does, and is UTF-8 without one
        while (_length < 4)
        {
            var read = stream.Read(_buffer, _length, 4 - _length);
            if (read == 0)
                break;

            _length += read;
        }

        var prefix = _buffer.AsSpan(0, _length);
        if (prefix.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) || prefix.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || prefix.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]))
        {
            // UTF-16 or UTF-32: the stream reader decodes it, from the bytes already read
            _textReader = new StreamReader(new PrefixedStream(prefix.ToArray(), stream), StrictUtf8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            _encoder = StrictUtf8.GetEncoder();
            _chars = new char[BlockSize];
            _buffer = new byte[StrictUtf8.GetMaxByteCount(BlockSize) * 2];
            _length = 0;
            Fill();
            SkipByteOrderMarkColumn();
            return;
        }

        _stream = stream;
        var byteOrderMarkLength = prefix.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        if (stream.CanSeek && _length == 4 && stream.Length - stream.Position <= WholeStreamMaxLength - 4)
        {
            // The rest of the stream fits in a pooled buffer, which the source gives back like the copy of a span
            var whole = ArrayPool<byte>.Shared.Rent(_length + (int)(stream.Length - stream.Position) + 1);
            prefix.CopyTo(whole);
            var length = _length;
            while (true)
            {
                var read = stream.Read(whole, length, whole.Length - length);
                if (read == 0)
                    break;

                length += read;
                if (length == whole.Length)
                {
                    // The stream grew since its length was read: read the rest by blocks, in a buffer that can grow
                    _buffer = new byte[Math.Max(length * 2, BlockSize * 2)];
                    whole.AsSpan(0, length).CopyTo(_buffer);
                    ArrayPool<byte>.Shared.Return(whole);
                    _start = byteOrderMarkLength;
                    _length = length - byteOrderMarkLength;
                    Fill();
                    return;
                }
            }

            _buffer = whole;
            _isPooledBuffer = true;
            _start = byteOrderMarkLength;
            _length = length - byteOrderMarkLength;
            _isFinalBlock = true;
            KeepsWholeInput = true;
            return;
        }

        // Like the stream reader, the byte order mark is not a character of the text: the locations start after it
        _start = byteOrderMarkLength;
        _length -= byteOrderMarkLength;
        Fill();
    }

    public string? SourceName { get; }

    /// <summary>Gets a value indicating whether the source keeps all the bytes of the input: a location can then be computed for any offset.</summary>
    public bool KeepsWholeInput { get; }

    /// <summary>Gets the offset of the current token in the input, at its anchor or tag if any.</summary>
    public long TokenStartOffset
    {
        get
        {
            ref var token = ref Token;
            var start = token.Start;
            if (token.Properties.AnchorStart >= 0)
            {
                start = Math.Min(start, token.Properties.AnchorStart);
            }

            if (token.Properties.TagStart >= 0)
            {
                start = Math.Min(start, token.Properties.TagStart);
            }

            return _tokenBufferOffset + start;
        }
    }

    /// <summary>Gets the location of the current token, computed from its offsets when it is asked for: the source must keep the whole input.</summary>
    public YamlLocation TokenLocation
    {
        get
        {
            ref var token = ref Token;
            var start = token.Start;
            if (token.Properties.AnchorStart >= 0)
            {
                start = Math.Min(start, token.Properties.AnchorStart);
            }

            if (token.Properties.TagStart >= 0)
            {
                start = Math.Min(start, token.Properties.TagStart);
            }

            return new YamlLocation(this, _tokenBufferOffset + start, _tokenBufferOffset + token.Start + token.Length);
        }
    }

    /// <summary>Gets the offset of the end of the current token in the input.</summary>
    public long TokenEndOffset => _tokenBufferOffset + Token.Start + Token.Length;

    /// <summary>Computes the location of an offset of the input. When the source does not keep the whole input, only the offsets of the current buffer are known.</summary>
    /// <exception cref="InvalidOperationException">The bytes of the input were given back by <see cref="ReturnPooledBuffers"/>.</exception>
    public Mark GetMarkAt(long offset)
    {
        if (_bufferReturned)
            throw new InvalidOperationException("The location of a node can only be computed while its YAML is read.");

        return GetMark(offset);
    }

    /// <summary>
    /// Gives back the batch of tokens, for the next source of the thread, and the pooled copy of the bytes of a span. The
    /// source must not be read afterward.
    /// </summary>
    public void ReturnPooledBuffers()
    {
        var batch = _batch;
        if (batch.Length > 0)
        {
            // The current token stays available, like when the batch is read again
            if (_current >= 0)
            {
                ref var current = ref batch[_current];
                _token = current.Token;
                _anchor = current.HasStrings ? current.Anchor : null;
                _tag = current.HasStrings ? current.Tag : null;
                _current = -1;
            }

            _batch = [];
            _batchCount = 0;
            _batchIndex = 0;

            // The strings of the tokens are not kept alive by the cache
            Array.Clear(batch);
            s_cachedBatch = batch;
        }

        if (_isPooledBuffer)
        {
            _isPooledBuffer = false;
            _bufferReturned = true;
            var buffer = _buffer;
            _buffer = [];
            _start = 0;
            _length = 0;
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static BatchedToken[] RentBatch()
    {
        // A source created while another one is read, such as by a converter that deserializes, gets a batch of its own
        var batch = s_cachedBatch;
        if (batch is null)
            return new BatchedToken[BatchSize];

        s_cachedBatch = null;
        return batch;
    }

    // The byte order mark of the stream is a character, but not a column of the first line
    private void SkipByteOrderMarkColumn()
    {
        if (_buffer.AsSpan(0, _length).StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            _markOffset = 3;
            _markLineStartOffset = 3;
            _markCharIndex = 1;
            _markLineStartCharIndex = 1;
            _markLineStartsAfterByteOrderMark = true;
        }
    }

    /// <summary>Gets the kind of the current token: a document start or end, a collection start or end, a scalar or an alias; <see cref="Utf8YamlTokenKind.None"/> at the end.</summary>
    public Utf8YamlTokenKind Kind { get; private set; }

    public bool IsFlow => (Token.Flags & Utf8YamlTokenFlags.Flow) != Utf8YamlTokenFlags.None;

    /// <summary>Gets a value indicating whether the current document start or end has no marker.</summary>
    public bool IsImplicit => Token.Length == 0;

    public ScalarStyle ScalarStyle => (ScalarStyle)(Token.Flags & Utf8YamlTokenFlags.ScalarStyleMask);

    /// <summary>Gets the anchor of the current node, or the name of the current alias.</summary>
    public string? Anchor => _current >= 0 ? (_batch[_current].HasStrings ? _batch[_current].Anchor : null) : _anchor;

    /// <summary>Gets the resolved tag of the current node.</summary>
    public string? Tag => _current >= 0 ? (_batch[_current].HasStrings ? _batch[_current].Tag : null) : _tag;

    /// <summary>Gets the %YAML directive of the current document start.</summary>
    public VersionDirective? Version { get; private set; }

    /// <summary>Gets the tag directives of the current document start, with the default ones.</summary>
    public TagDirectiveCollection? TagDirectives { get; private set; }

    /// <summary>Gets the bytes of the current scalar when they are its value: a plain scalar on one line, or a quoted scalar without escapes or line breaks.</summary>
    public bool TryGetRawScalar(out ReadOnlySpan<byte> value)
    {
        if (Kind is not Utf8YamlTokenKind.Scalar || (Token.Flags & Utf8YamlTokenFlags.RawValue) == Utf8YamlTokenFlags.None)
        {
            value = default;
            return false;
        }

        value = Token.ValueLength == 0 ? default : TokenBuffer.Slice(Token.ValueStart, Token.ValueLength);
        return true;
    }

    /// <summary>Gets the value of the current scalar.</summary>
    public string ScalarValue => _value ??= Utf8YamlReader.DecodeString(TokenBuffer, in Token);

    private ref Utf8YamlReader.TokenData Token => ref _current >= 0 ? ref _batch[_current].Token : ref _token;

    // The buffer of the current token: the bytes stay in place until the next read
    private ReadOnlySpan<byte> TokenBuffer => _buffer.AsSpan(_tokenBufferIndex, _start + _length - _tokenBufferIndex);

    /// <summary>Gets the start of the current token, at its anchor or tag if any.</summary>
    public Mark Start
    {
        get
        {
            if ((_marks & MarkKinds.Start) == MarkKinds.None)
            {
                var start = Token.Start;
                if (Token.Properties.AnchorStart >= 0)
                {
                    start = Math.Min(start, Token.Properties.AnchorStart);
                }

                if (Token.Properties.TagStart >= 0)
                {
                    start = Math.Min(start, Token.Properties.TagStart);
                }

                _startMark = GetMark(_tokenBufferOffset + start);
                _marks |= MarkKinds.Start;
            }

            return _startMark;
        }
    }

    /// <summary>Gets the end of the current token.</summary>
    public Mark End
    {
        get
        {
            if ((_marks & MarkKinds.End) == MarkKinds.None)
            {
                _endMark = GetMark(_tokenBufferOffset + Token.Start + Token.Length);
                _marks |= MarkKinds.End;
            }

            return _endMark;
        }
    }

    /// <summary>Reads the next node token, or document start or end.</summary>
    /// <returns><see langword="false"/> at the end of the input.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Read()
    {
        // Most tokens are already in the batch: the loop that reads the next batch is only called when it is consumed
        if (_batchIndex < _batchCount)
        {
            ReadFromBatch();
            return true;
        }

        return ReadBatch();
    }

    // The token stays valid until the next read: the buffer moves only when the batch is consumed
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReadFromBatch()
    {
        _current = _batchIndex++;
        ref var entry = ref _batch[_current];
        Kind = entry.Kind;
        if (entry.Kind is Utf8YamlTokenKind.DocumentStart)
        {
            Version = entry.Version;
            TagDirectives = entry.TagDirectives;
        }

        // The tokens of a batch share their buffer
        if (_current == 0)
        {
            _tokenBufferOffset = _batchBufferOffset;
            _tokenBufferIndex = _batchBufferIndex;
            _tokenLineNumber = _batchLineNumber;
            _tokenCharOffset = _batchCharOffset;
        }

        _value = null;
        _marks = MarkKinds.None;
        if (Kind is Utf8YamlTokenKind.MappingEnd or Utf8YamlTokenKind.SequenceEnd)
        {
            _nesting--;
        }
        else if (Kind is Utf8YamlTokenKind.MappingStart or Utf8YamlTokenKind.SequenceStart && (++_nesting & (StackProbeInterval - 1)) == 1)
        {
            EnsureSufficientExecutionStack();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool ReadBatch()
    {
        if (_pendingError is not null)
            throw _pendingError;

        while (true)
        {
            if (_batchIndex < _batchCount)
            {
                ReadFromBatch();
                return true;
            }

            // The error comes after the tokens that the batch handed out, like when the tokens are read one at a time
            _batchError?.Throw();

            // The entries of the batch are read again: the current token is kept apart
            if (_current >= 0)
            {
                ref var current = ref _batch[_current];
                _token = current.Token;
                _anchor = current.HasStrings ? current.Anchor : null;
                _tag = current.HasStrings ? current.Tag : null;
                _current = -1;
            }

            var bufferIndex = _start;
            var lineNumber = _state._data.LineNumber;
            var charOffset = _state._data.CharOffset;
            var reader = new Utf8YamlReader(_buffer.AsSpan(_start, _length), _isFinalBlock, _state, stopsAtIncompleteToken: true);
            _batchCount = 0;
            _batchIndex = 0;
            _batchBufferIndex = bufferIndex;
            _batchBufferOffset = reader.BufferOffset;
            _batchLineNumber = lineNumber;
            _batchCharOffset = charOffset;
            var found = true;
            try
            {
                while (_batchCount < BatchSize)
                {
                    if (!ReadToken(ref reader, ref _batch[_batchCount]))
                    {
                        found = false;
                        break;
                    }

                    _batchCount++;
                }
            }
            catch (YamlException ex)
            {
                var error = SourceName is not null && ex.SourceName is null ? new YamlException(SourceName, ex.Start, ex.End, ex.Message, ex) : ex;
                if (_batchCount == 0)
                {
                    if (ReferenceEquals(error, ex))
                        throw;

                    throw error;
                }

                _batchError = ExceptionDispatchInfo.Capture(error);
                continue;
            }

            // The last token is not complete in the buffer, and the reader did not keep its state before it: the tokens of
            // the batch are read again from the start of the batch
            if (reader.StoppedAtIncompleteToken)
            {
                var readCount = reader.ReadCount;
                reader = new Utf8YamlReader(_buffer.AsSpan(_start, _length), _isFinalBlock, _state, stopsAtIncompleteToken: true);
                for (var i = 0; i < readCount; i++)
                {
                    _ = reader.Read();
                }
            }

            if (_batchCount > 0)
            {
                // The buffer keeps its bytes until more data is needed: the state stays relative to it
                _state = reader.StateWithoutConsuming;
                continue;
            }

            if (!found && _isFinalBlock)
            {
                Kind = Utf8YamlTokenKind.None;
                _token = default;
                _token.Start = _length;
                _token.Properties = Utf8YamlReader.NodeProperties.None;
                _tokenBufferOffset = reader.BufferOffset;
                _tokenBufferIndex = bufferIndex;
                _tokenLineNumber = lineNumber;
                _tokenCharOffset = charOffset;
                _marks = MarkKinds.None;
                return false;
            }

            // The previous token is gone: the location is now computed in the current buffer, which the refill moves
            _tokenBufferOffset = reader.BufferOffset;
            _tokenBufferIndex = bufferIndex;
            _tokenLineNumber = lineNumber;
            _tokenCharOffset = charOffset;
            Save(ref reader);
            Fill();
        }
    }

    // Consumers such as converters and the document object model recurse once per nesting level, so a large MaxDepth can
    // exhaust the stack before the depth limit is reached. Probing the stack turns an uncatchable crash into a YamlException.
    // The probe leaves room for more than the levels until the next one, so it runs at the first level and then every few
    // levels, instead of at every collection start.
    private void EnsureSufficientExecutionStack()
    {
        try
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
        }
        catch (InsufficientExecutionStackException exception)
        {
            throw YamlDepthHelper.CreateInsufficientStackException(_state._data.Depth, Start, End, SourceName, exception);
        }
    }

    // Reads the next node token, with the strings that it needs while the state of the reader is the one of the token
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ReadToken(ref Utf8YamlReader reader, ref BatchedToken entry)
    {
        while (reader.Read())
        {
            var kind = reader.TokenKind;
            switch (kind)
            {
                case Utf8YamlTokenKind.Directive:
                    AddDirective(reader.TokenSpan);
                    continue;

                case Utf8YamlTokenKind.ByteOrderMark or Utf8YamlTokenKind.DocumentEndMarker:
                    continue;

                case Utf8YamlTokenKind.DocumentStart:
                    var tags = _pendingTags ?? [];
                    foreach (var directive in Constants.DefaultTagDirectives)
                    {
                        if (!tags.Contains(directive))
                        {
                            tags.Add(directive);
                        }
                    }

                    entry.Version = _pendingVersion;
                    entry.TagDirectives = tags;
                    _pendingVersion = null;
                    _pendingTags = null;
                    entry.Anchor = null;
                    entry.Tag = null;
                    entry.HasStrings = true;
                    break;

                case Utf8YamlTokenKind.Alias:
                    entry.Anchor = Encoding.UTF8.GetString(reader.AnchorName);
                    entry.Tag = null;
                    entry.HasStrings = true;
                    break;

                default:
                    // Most nodes have neither anchor nor tag: the strings of the entry, which may be the ones of an earlier
                    // token, are then left as they are, without the cost of storing references
                    var anchor = reader.AnchorStart < 0 ? null : Encoding.UTF8.GetString(reader.AnchorName);
                    var tag = reader.TagStart < 0 ? null : reader.GetTag();
                    entry.HasStrings = anchor is not null || tag is not null;
                    if (entry.HasStrings)
                    {
                        entry.Anchor = anchor;
                        entry.Tag = tag;
                    }

                    break;
            }

            entry.Kind = kind;
            entry.Token = reader.CurrentToken;
            return true;
        }

        return false;
    }

    /// <summary>Creates the event of the current token for <see cref="IParser"/>.</summary>
    public ParsingEvent CreateEvent()
    {
        var tag = Tag;
        return Kind switch
        {
            Utf8YamlTokenKind.DocumentStart => new DocumentStart(Version, TagDirectives, IsImplicit, Start, End),
            Utf8YamlTokenKind.DocumentEnd => new DocumentEnd(IsImplicit, Start, End),
            Utf8YamlTokenKind.MappingStart => new MappingStart(Anchor, tag, string.IsNullOrEmpty(tag), IsFlow ? YamlStyle.Flow : YamlStyle.Block, Start, End) { IsTagResolved = true },
            Utf8YamlTokenKind.SequenceStart => new SequenceStart(Anchor, tag, string.IsNullOrEmpty(tag), IsFlow ? YamlStyle.Flow : YamlStyle.Block, Start, End) { IsTagResolved = true },
            Utf8YamlTokenKind.MappingEnd => new MappingEnd(Start, End),
            Utf8YamlTokenKind.SequenceEnd => new SequenceEnd(Start, End),
            Utf8YamlTokenKind.Alias => new AnchorAlias(Anchor!, Start, End),
            Utf8YamlTokenKind.Scalar => CreateScalar(),
            _ => throw new InvalidOperationException("There is no current token."),
        };

        Scalar CreateScalar()
        {
            var style = ScalarStyle;
            var isPlainImplicit = (style is ScalarStyle.Plain && tag is null) || tag == Constants.DefaultHandle;
            var isQuotedImplicit = !isPlainImplicit && tag is null;
            return new Scalar(Anchor, tag, ScalarValue, style, isPlainImplicit, isQuotedImplicit, Start, End) { IsTagResolved = true };
        }
    }

    private void AddDirective(ReadOnlySpan<byte> directive)
    {
        var parts = Encoding.UTF8.GetString(directive).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts[0] == "%YAML")
        {
            var dot = parts[1].IndexOf('.', StringComparison.Ordinal);
            _pendingVersion = new VersionDirective(new Version(int.Parse(parts[1].AsSpan(0, dot), System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1].AsSpan(dot + 1), System.Globalization.CultureInfo.InvariantCulture)));
        }
        else if (parts[0] == "%TAG")
        {
            _pendingTags ??= [];
            _pendingTags.Add(new TagDirective(parts[1], Uri.UnescapeDataString(parts[2])));
        }
    }

    // Moves the location to an absolute offset. The tokens come in the order of the input, so the location usually moves
    // forward; a position before the current line is computed from the start of the buffer.
    private Mark GetMark(long offset)
    {
        var buffer = TokenBuffer;
        var bufferOffset = _tokenBufferOffset;
        if (offset < _markLineStartOffset || _markOffset < bufferOffset)
        {
            if (offset < bufferOffset)
                return CurrentMark();

            ComputeFromBufferStart(buffer, (int)(offset - bufferOffset));
            return CurrentMark();
        }

        var from = (int)(_markOffset - bufferOffset);
        var to = (int)(offset - bufferOffset);
        if (offset >= _markOffset)
        {
            var segment = buffer[from..to];
            if (segment.Length <= ShortSegmentLength && TryAdvanceOverShortAsciiSegment(segment))
            {
                _markOffset = offset;
                return CurrentMark();
            }

            var lastBreak = segment.LastIndexOfAny((byte)'\r', (byte)'\n');
            if (lastBreak >= 0)
            {
                var lines = segment[..(lastBreak + 1)];
                _markLine += lines.Count((byte)'\n') + CountLoneCarriageReturns(buffer, from, lines.Length);
                _markLineStartCharIndex = _markCharIndex + GetCharCount(lines);
                _markLineStartOffset = _markOffset + lastBreak + 1;
                _markCharIndex = _markLineStartCharIndex + GetCharCount(segment[(lastBreak + 1)..]);
                _markLineStartsAfterByteOrderMark = false;
                SkipLineStartByteOrderMark(segment[(lastBreak + 1)..]);
            }
            else
            {
                // The location can stop at the start of a line, before the bytes of its byte order mark are read
                if (_markOffset == _markLineStartOffset)
                {
                    SkipLineStartByteOrderMark(segment);
                }

                _markCharIndex += GetCharCount(segment);
            }
        }
        else
        {
            // Backward on the same line
            var lineStart = (int)(_markLineStartOffset - bufferOffset);
            _markCharIndex = _markLineStartCharIndex + GetCharCount(buffer[lineStart..to]);
        }

        _markOffset = offset;
        return CurrentMark();
    }

    // The input is valid UTF-8. Most of the text between two locations is a few ASCII characters, whose count is their length.
    private static int GetCharCount(ReadOnlySpan<byte> utf8) => Ascii.IsValid(utf8) ? utf8.Length : Encoding.UTF8.GetCharCount(utf8);

    // Most of the text between two locations is a few ASCII characters on one or two lines: a loop over its bytes replaces
    // the searches of GetMark, whose cost is in their calls. A carriage return and the other bytes take the general path.
    private bool TryAdvanceOverShortAsciiSegment(ReadOnlySpan<byte> segment)
    {
        var lineBreaks = 0;
        var lastBreak = -1;
        var bits = 0;
        for (var i = 0; i < segment.Length; i++)
        {
            var b = segment[i];
            bits |= b;
            if (b == (byte)'\n')
            {
                lineBreaks++;
                lastBreak = i;
            }
            else if (b == (byte)'\r')
            {
                return false;
            }
        }

        if (bits >= 0x80)
            return false;

        if (lastBreak < 0)
        {
            _markCharIndex += segment.Length;
            return true;
        }

        // An ASCII line does not start with a byte order mark
        _markLine += lineBreaks;
        _markLineStartCharIndex = _markCharIndex + lastBreak + 1;
        _markLineStartOffset = _markOffset + lastBreak + 1;
        _markCharIndex = _markLineStartCharIndex + segment.Length - (lastBreak + 1);
        _markLineStartsAfterByteOrderMark = false;
        return true;
    }

    private Mark CurrentMark() => new((int)Math.Min(_markCharIndex, int.MaxValue), _markLine, (int)Math.Min(_markCharIndex - _markLineStartCharIndex, int.MaxValue));

    // The start of the buffer is the start of a line, whose number and character index are in the state of the reader
    private void ComputeFromBufferStart(ReadOnlySpan<byte> buffer, int position)
    {
        var lines = buffer[..position];
        var lastBreak = lines.LastIndexOfAny((byte)'\r', (byte)'\n');
        _markLine = _tokenLineNumber + lines[..(lastBreak + 1)].Count((byte)'\n') + CountLoneCarriageReturns(buffer, 0, lastBreak + 1);
        _markLineStartCharIndex = _tokenCharOffset + GetCharCount(lines[..(lastBreak + 1)]);
        _markLineStartOffset = _tokenBufferOffset + lastBreak + 1;
        _markCharIndex = _markLineStartCharIndex + GetCharCount(lines[(lastBreak + 1)..]);
        _markOffset = _tokenBufferOffset + position;
        _markLineStartsAfterByteOrderMark = false;
        SkipLineStartByteOrderMark(lines[(lastBreak + 1)..]);
    }

    // A byte order mark at the start of a line, before a document, is not a column. The location has moved to the start
    // of the line; lineContent is the part of the line it passed.
    private void SkipLineStartByteOrderMark(ReadOnlySpan<byte> lineContent)
    {
        if (!_markLineStartsAfterByteOrderMark && lineContent.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            _markLineStartOffset += 3;
            _markLineStartCharIndex++;
            _markLineStartsAfterByteOrderMark = true;
        }
    }

    private static int CountLoneCarriageReturns(ReadOnlySpan<byte> buffer, int from, int length)
    {
        var span = buffer.Slice(from, length);
        if (!span.Contains((byte)'\r'))
            return 0;

        var count = 0;
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == '\r' && (from + i + 1 >= buffer.Length || buffer[from + i + 1] != '\n'))
            {
                count++;
            }
        }

        return count;
    }

    private void Save(ref Utf8YamlReader reader)
    {
        var consumed = reader.BytesConsumed;
        if (consumed > 0)
        {
            // The location must not depend on the bytes that are dropped. A token without length, such as the end of an
            // implicit document after comments, can be at the end of the last node, before the dropped bytes: the
            // location stops there, and the positions after it are computed from the start of the new buffer.
            var keep = Math.Min(consumed, reader.ConsumedEnd);
            var trackedPoint = reader.BufferOffset + keep;
            if (keep >= 0 && _markOffset < trackedPoint)
            {
                _ = GetMark(trackedPoint);
            }
        }

        _state = reader.CurrentState;
        _start += consumed;
        _length -= consumed;
    }

    // Moves the unread bytes to the start of the buffer and appends the next block of the stream or the text reader
    private void Fill()
    {
        if (_isFinalBlock)
            return;

        if (_start > 0)
        {
            _buffer.AsSpan(_start, _length).CopyTo(_buffer);
            _start = 0;
        }

        if (_stream is not null)
        {
            // The bytes are read as is: the reader validates them, and a sequence cut by the end of a block is read again
            // with the next one
            if (_buffer.Length - _length < BlockSize)
            {
                Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + BlockSize));
            }

            var read = _stream.Read(_buffer, _length, _buffer.Length - _length);
            _length += read;
            _isFinalBlock = read == 0;
            return;
        }

        var maxBytes = StrictUtf8.GetMaxByteCount(BlockSize);

        if (_buffer.Length - _length < maxBytes)
        {
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + maxBytes));
        }

        int charCount;
        if (_textReader is StreamReader)
        {
            // A StreamReader returns what one read of the stream gives, without waiting for the whole block
            charCount = _textReader.Read(_chars!, 0, _chars!.Length);
        }
        else
        {
            // Other readers can wait for input that is not needed yet when asked for a block: read up to the end of a line
            charCount = 0;
            while (charCount < _chars!.Length)
            {
                var c = _textReader!.Read();
                if (c < 0)
                    break;

                _chars[charCount++] = (char)c;
                if (c == '\n')
                    break;
            }
        }

        _isFinalBlock = charCount == 0;
        try
        {
            _length += _encoder!.GetBytes(_chars.AsSpan(0, charCount), _buffer.AsSpan(_length), flush: _isFinalBlock);
        }
        catch (EncoderFallbackException ex)
        {
            throw new SyntaxErrorException("The input contains an invalid UTF-16 sequence.", ex);
        }
    }

    [Flags]
    private enum MarkKinds : byte
    {
        None = 0,
        Start = 1,
        End = 2,
    }

    [StructLayout(LayoutKind.Auto)]
    private struct BatchedToken
    {
        public Utf8YamlReader.TokenData Token;
        public Utf8YamlTokenKind Kind;

        // Anchor and Tag are the ones of the token; otherwise they are null, whatever the fields hold
        public bool HasStrings;
        public string? Anchor;
        public string? Tag;
        public VersionDirective? Version;
        public TagDirectiveCollection? TagDirectives;
    }

    /// <summary>A read-only stream that returns bytes already read from a stream, then the rest of that stream.</summary>
    private sealed class PrefixedStream(byte[] prefix, Stream stream) : Stream
    {
        private int _prefixPosition;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_prefixPosition < prefix.Length)
            {
                var count = Math.Min(buffer.Length, prefix.Length - _prefixPosition);
                prefix.AsSpan(_prefixPosition, count).CopyTo(buffer);
                _prefixPosition += count;
                return count;
            }

            return stream.Read(buffer);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
