using System.Buffers;
using System.IO.Pipelines;
using System.Numerics;

namespace Meziantou.Framework.Bencode;

/// <summary>
/// Reads bencode from a stream or a <see cref="PipeReader"/> one token at a time, without building the whole document in memory.
/// </summary>
/// <remarks>
/// The reader checks the encoding as it reads (one encoding per integer and string, keys that are strings, at most 64
/// nested containers, a single root value), and throws a <see cref="FormatException"/> at the first invalid token. It does
/// not detect duplicate dictionary keys, which would require keeping every key: <see cref="ReadValueAsync"/> does.
/// </remarks>
public sealed class BencodeReader : IAsyncDisposable
{
    // A string length has at most 10 digits (int.MaxValue). An integer has at most MaxIntegerDigits digits and a sign.
    // The reader looks for the terminator of these runs in the buffered data, so the runs must be bounded or a peer could
    // make the reader buffer without limit by never sending it.
    private const int MaxStringLengthDigits = 10;

    // A declared string length is attacker-controlled, so the payload buffer starts small and grows with the data
    // that actually arrives instead of being sized from the declaration.
    private const int InitialStringBufferSize = 64 * 1024;

    private readonly PipeReader _reader;
    private readonly bool _ownsReader;
    private readonly List<Container> _containers = [];
    private ReadOnlySequence<byte> _buffer;
    private bool _hasPendingReadResult;
    private bool _isReaderCompleted;
    private bool _hasReadRoot;
    private bool _isFinished;
    private bool _isDisposed;
    private bool _hasFailed;
    private ReadOnlyMemory<byte> _stringValue;
    private BencodeInteger? _integerValue;
    private ArrayBufferWriter<byte>? _capture;

    /// <summary>Creates a reader over a <see cref="PipeReader"/>. Disposing the reader does not complete <paramref name="reader"/>.</summary>
    public BencodeReader(PipeReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <summary>Creates a reader over a stream.</summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave the stream open when the reader is disposed.</param>
    public BencodeReader(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: leaveOpen));
        _ownsReader = true;
    }

    /// <summary>Gets the kind of the current token.</summary>
    public BencodeTokenType TokenType { get; private set; }

    /// <summary>Gets the number of lists and dictionaries that contain the current token.</summary>
    public int CurrentDepth { get; private set; }

    /// <summary>Gets the number of bytes read so far, up to the end of the current token.</summary>
    public long BytesConsumed { get; private set; }

    /// <summary>Reads the next token.</summary>
    /// <returns><see langword="true"/> when a token was read; <see langword="false"/> when the root value is complete and the data ends.</returns>
    /// <exception cref="FormatException">The data is not valid bencode, or it has data after the root value.</exception>
    public async ValueTask<bool> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (FormatException)
        {
            _hasFailed = true;
            throw;
        }
    }

    private async ValueTask<bool> ReadCoreAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isFinished)
            return false;

        _stringValue = default;
        _integerValue = null;
        CurrentDepth = _containers.Count;

        if (_hasReadRoot)
        {
            if (await PeekByteAsync(cancellationToken).ConfigureAwait(false) is not null)
                throw new FormatException("Unexpected trailing data after bencode value.");

            _isFinished = true;
            TokenType = BencodeTokenType.None;
            AdvanceReader();
            return false;
        }

        var token = await PeekByteAsync(cancellationToken).ConfigureAwait(false);
        if (token is not { } current)
        {
            throw new FormatException(_containers.Count is 0 ? "Unexpected end of bencode data."
                : _containers[^1].IsDictionary ? "Unterminated bencode dictionary." : "Unterminated bencode list.");
        }

        if (_containers.Count > 0)
        {
            var container = _containers[^1];
            if (current is (byte)'e' && (!container.IsDictionary || container.ExpectsKey))
            {
                Consume(1);
                _containers.RemoveAt(_containers.Count - 1);
                CurrentDepth = _containers.Count;
                TokenType = container.IsDictionary ? BencodeTokenType.EndDictionary : BencodeTokenType.EndList;
                OnValueCompleted();
                return true;
            }

            if (container.IsDictionary && container.ExpectsKey)
            {
                if (current is < (byte)'0' or > (byte)'9')
                    throw new FormatException($"Bencode dictionary keys must be strings (position {BytesConsumed}).");

                _stringValue = await ReadStringAsync(cancellationToken).ConfigureAwait(false);
                _containers[^1] = container with { ExpectsKey = false };
                TokenType = BencodeTokenType.Key;
                return true;
            }
        }

        switch (current)
        {
            case (byte)'i':
                _integerValue = await ReadIntegerAsync(cancellationToken).ConfigureAwait(false);
                TokenType = BencodeTokenType.Integer;
                OnValueCompleted();
                break;

            case >= (byte)'0' and <= (byte)'9':
                _stringValue = await ReadStringAsync(cancellationToken).ConfigureAwait(false);
                TokenType = BencodeTokenType.String;
                OnValueCompleted();
                break;

            case (byte)'l' or (byte)'d':
                if (_containers.Count >= BencodeDecoder.MaxDepth)
                    throw new FormatException($"Bencode data is nested too deeply. The maximum supported depth is {BencodeDecoder.MaxDepth}.");

                Consume(1);
                var isDictionary = current is (byte)'d';
                _containers.Add(new Container(isDictionary, ExpectsKey: isDictionary));
                TokenType = isDictionary ? BencodeTokenType.StartDictionary : BencodeTokenType.StartList;
                break;

            default:
                throw new FormatException($"Invalid bencode token '{(char)current}' at position {BytesConsumed}.");
        }

        return true;
    }

    /// <summary>Gets the bytes of the current <see cref="BencodeTokenType.String"/> or <see cref="BencodeTokenType.Key"/> token.</summary>
    /// <exception cref="InvalidOperationException">The current token is not a string or a key.</exception>
    public ReadOnlyMemory<byte> GetBytes()
    {
        if (TokenType is not (BencodeTokenType.String or BencodeTokenType.Key))
            throw new InvalidOperationException($"Cannot get the bytes of a {TokenType} token.");

        return _stringValue;
    }

    /// <summary>Decodes the current <see cref="BencodeTokenType.String"/> or <see cref="BencodeTokenType.Key"/> token as UTF-8.</summary>
    /// <exception cref="InvalidOperationException">The current token is not a string or a key.</exception>
    /// <exception cref="DecoderFallbackException">The bytes are not valid UTF-8.</exception>
    public string GetUtf8String() => new BencodeString(GetBytes()).ToUtf8String();

    /// <summary>Gets the current <see cref="BencodeTokenType.Integer"/> token as a <see cref="long"/>.</summary>
    /// <exception cref="InvalidOperationException">The current token is not an integer.</exception>
    /// <exception cref="OverflowException">The integer does not fit in a <see cref="long"/>.</exception>
    public long GetInt64()
    {
        if (!GetInteger().TryGetInt64(out var value))
            throw new OverflowException("The bencode integer does not fit in an Int64.");

        return value;
    }

    /// <summary>Gets the current <see cref="BencodeTokenType.Integer"/> token as a <see cref="long"/> when it fits in one.</summary>
    /// <exception cref="InvalidOperationException">The current token is not an integer.</exception>
    public bool TryGetInt64(out long value) => GetInteger().TryGetInt64(out value);

    /// <summary>Gets the current <see cref="BencodeTokenType.Integer"/> token, whatever its size.</summary>
    /// <exception cref="InvalidOperationException">The current token is not an integer.</exception>
    public BigInteger GetBigInteger() => GetInteger().BigIntegerValue;

    /// <summary>Returns the value that starts at the current token, reading the rest of the list or dictionary it starts.</summary>
    /// <remarks>After the call, the reader is positioned on the last token of the value. A key returns a <see cref="BencodeString"/>.</remarks>
    /// <exception cref="InvalidOperationException">The current token does not start a value.</exception>
    /// <exception cref="FormatException">The data is not valid bencode, or a dictionary has a duplicate key.</exception>
    public async ValueTask<BencodeValue> ReadValueAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        // Iterative, so the depth of the data does not grow the async state machine chain
        var containers = new Stack<(BencodeValue Container, BencodeString? PendingKey)>();
        while (true)
        {
            BencodeValue? completed = null;
            switch (TokenType)
            {
                case BencodeTokenType.Integer:
                    completed = _integerValue!;
                    break;

                case BencodeTokenType.String:
                    completed = new BencodeString(_stringValue);
                    break;

                case BencodeTokenType.Key:
                    var key = new BencodeString(_stringValue);
                    if (containers.Count is 0)
                        return key;

                    var (dictionary, _) = containers.Pop();
                    containers.Push((dictionary, key));
                    break;

                case BencodeTokenType.StartList:
                    containers.Push((new BencodeList(), null));
                    break;

                case BencodeTokenType.StartDictionary:
                    containers.Push((new BencodeDictionary(), null));
                    break;

                case BencodeTokenType.EndList or BencodeTokenType.EndDictionary when containers.Count > 0:
                    completed = containers.Pop().Container;
                    break;

                default:
                    throw new InvalidOperationException($"Cannot read a value from a {TokenType} token.");
            }

            if (completed is not null)
            {
                if (containers.Count is 0)
                    return completed;

                var (parent, pendingKey) = containers.Pop();
                if (parent is BencodeList list)
                {
                    list.Add(completed);
                }
                else
                {
                    try
                    {
                        ((BencodeDictionary)parent).Add(pendingKey!, completed);
                    }
                    catch (ArgumentException ex)
                    {
                        _hasFailed = true;
                        throw new FormatException("Duplicate bencode dictionary key.", ex);
                    }
                }

                containers.Push((parent, null));
            }

            // ReadAsync returns false only after the root value, which cannot happen inside a container
            await ReadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Skips the children of the current <see cref="BencodeTokenType.StartList"/> or <see cref="BencodeTokenType.StartDictionary"/> token, and positions the reader on its end. Other tokens are left as they are.</summary>
    public async ValueTask SkipAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (TokenType is not (BencodeTokenType.StartList or BencodeTokenType.StartDictionary))
            return;

        var depth = CurrentDepth;
        do
        {
            await ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        while (!(CurrentDepth == depth && TokenType is BencodeTokenType.EndList or BencodeTokenType.EndDictionary));
    }

    /// <summary>Releases the buffers of the reader, and completes the stream reader when the reader was created from a stream.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        // After valid data, the bytes after the last token are not examined, so a caller that keeps reading the PipeReader
        // gets them at once. After invalid data, they are: a Pipe counts examined bytes to resume a writer that waits for
        // the reader to make room, and the caller may never read them.
        AdvanceReader(examineAll: _hasFailed);
        if (_ownsReader)
        {
            await _reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Records that the caller found the data invalid, so disposing releases the bytes that are left.</summary>
    internal void MarkFailed() => _hasFailed = true;

    /// <summary>Starts copying every byte the reader consumes, so the caller can get the exact bytes of a value.</summary>
    internal void StartCapture() => _capture = new ArrayBufferWriter<byte>();

    internal byte[] StopCapture()
    {
        var result = _capture?.WrittenSpan.ToArray() ?? [];
        _capture = null;
        return result;
    }

    private BencodeInteger GetInteger()
    {
        if (TokenType is not BencodeTokenType.Integer || _integerValue is null)
            throw new InvalidOperationException($"Cannot get an integer from a {TokenType} token.");

        return _integerValue;
    }

    private void OnValueCompleted()
    {
        if (_containers.Count is 0)
        {
            _hasReadRoot = true;
            return;
        }

        var container = _containers[^1];
        if (container.IsDictionary)
        {
            _containers[^1] = container with { ExpectsKey = true };
        }
    }

    private async ValueTask<BencodeInteger> ReadIntegerAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (TryReadInteger(_buffer, out var encodedLength) is { } value)
            {
                Consume(encodedLength);
                return value;
            }

            if (!await ReadMoreAsync(cancellationToken).ConfigureAwait(false))
                throw new FormatException("Unterminated bencode integer.");
        }
    }

    private async ValueTask<ReadOnlyMemory<byte>> ReadStringAsync(CancellationToken cancellationToken)
    {
        int length;
        int headerLength;
        while (!TryReadStringLength(_buffer, out length, out headerLength))
        {
            if (!await ReadMoreAsync(cancellationToken).ConfigureAwait(false))
                throw new FormatException("Invalid bencode string length format.");
        }

        Consume(headerLength);
        if (length is 0)
            return ReadOnlyMemory<byte>.Empty;

        if (_buffer.Length >= length)
        {
            var bytes = _buffer.Slice(0, length).ToArray();
            Consume(length);
            return bytes;
        }

        var destination = new ArrayBufferWriter<byte>(Math.Min(length, InitialStringBufferSize));
        while (destination.WrittenCount < length)
        {
            if (_buffer.IsEmpty && !await ReadMoreAsync(cancellationToken).ConfigureAwait(false))
                throw new FormatException("Unexpected end of bencode string data.");

            var bytesToCopy = (int)Math.Min(_buffer.Length, length - destination.WrittenCount);
            _buffer.Slice(0, bytesToCopy).CopyTo(destination.GetSpan(bytesToCopy));
            destination.Advance(bytesToCopy);
            Consume(bytesToCopy);
        }

        return destination.WrittenMemory;
    }

    private async ValueTask<byte?> PeekByteAsync(CancellationToken cancellationToken)
    {
        if (_buffer.IsEmpty && !await ReadMoreAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return PeekFirstByte(_buffer);
    }

    private static byte PeekFirstByte(ReadOnlySequence<byte> buffer)
    {
        // The first segment of a sliced sequence can be empty
        var reader = new SequenceReader<byte>(buffer);
        reader.TryPeek(out var value);
        return value;
    }

    /// <summary>Parses the <c>&lt;length&gt;:</c> header of a string at the start of the buffer.</summary>
    /// <returns><see langword="false"/> when the buffer does not hold the whole header yet.</returns>
    private static bool TryReadStringLength(ReadOnlySequence<byte> buffer, out int length, out int headerLength)
    {
        var candidate = buffer.Slice(0, Math.Min(buffer.Length, MaxStringLengthDigits + 1));
        var colon = candidate.PositionOf((byte)':');
        var digits = colon is { } position ? candidate.Slice(0, position) : candidate;
        Span<byte> digitBytes = stackalloc byte[MaxStringLengthDigits + 1];
        digits.CopyTo(digitBytes);
        var digitSpan = digitBytes[..(int)digits.Length];
        if (digitSpan.ContainsAnyExceptInRange((byte)'0', (byte)'9'))
            throw new FormatException("Invalid bencode string length format.");

        if (colon is null)
        {
            if (candidate.Length > MaxStringLengthDigits)
                throw new FormatException("Invalid bencode string length.");

            length = 0;
            headerLength = 0;
            return false;
        }

        if (digitSpan.IsEmpty)
            throw new FormatException("Invalid bencode string length format.");

        if (digitSpan.Length > 1 && digitSpan[0] is (byte)'0')
            throw new FormatException("Bencode string length cannot have leading zeroes.");

        if (!int.TryParse(digitSpan, NumberStyles.None, CultureInfo.InvariantCulture, out length))
            throw new FormatException("Invalid bencode string length.");

        headerLength = digitSpan.Length + 1;
        return true;
    }

    /// <summary>Parses the integer at the start of the buffer.</summary>
    /// <returns><see langword="null"/> when the buffer does not hold the whole integer yet.</returns>
    private static BencodeInteger? TryReadInteger(ReadOnlySequence<byte> buffer, out int encodedLength)
    {
        // 'i', an optional sign, the digits, and 'e'
        const int MaxLength = BencodeDecoder.MaxIntegerDigits + 3;
        var candidate = buffer.Slice(0, Math.Min(buffer.Length, MaxLength));
        if (candidate.PositionOf((byte)'e') is { } end)
        {
            var text = candidate.Slice(1, end);
            encodedLength = (int)text.Length + 2;
            return text.IsSingleSegment ? BencodeDecoder.ParseIntegerText(text.FirstSpan) : BencodeDecoder.ParseIntegerText(text.ToArray());
        }

        if (candidate.Length == MaxLength)
            throw new FormatException("Invalid bencode integer format.");

        encodedLength = 0;
        return null;
    }

    /// <summary>Reads until the buffer holds more data than it does now.</summary>
    /// <returns><see langword="false"/> when the data ended.</returns>
    private async ValueTask<bool> ReadMoreAsync(CancellationToken cancellationToken)
    {
        var previousLength = _buffer.Length;
        while (!_isReaderCompleted)
        {
            AdvanceReader();

            var readResult = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            _buffer = readResult.Buffer;
            _hasPendingReadResult = true;
            _isReaderCompleted = readResult.IsCompleted;

            if (readResult.IsCanceled)
                throw new OperationCanceledException("The bencode read was canceled.");

            if (_buffer.Length > previousLength)
                return true;
        }

        return false;
    }

    private void Consume(long length)
    {
        if (_capture is not null)
        {
            var slice = _buffer.Slice(0, length);
            slice.CopyTo(_capture.GetSpan((int)length));
            _capture.Advance((int)length);
        }

        _buffer = _buffer.Slice(length);
        BytesConsumed += length;
    }

    private void AdvanceReader(bool examineAll = true)
    {
        if (!_hasPendingReadResult)
            return;

        // When everything left in the buffer is examined, the next read waits for more data
        _reader.AdvanceTo(_buffer.Start, examineAll ? _buffer.End : _buffer.Start);
        _hasPendingReadResult = false;
    }

    private readonly record struct Container(bool IsDictionary, bool ExpectsKey);
}
