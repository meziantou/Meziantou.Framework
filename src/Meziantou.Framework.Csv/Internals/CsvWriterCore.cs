using System.Buffers;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Unicode;
using Meziantou.Framework.Csv.Serialization.Converters;

namespace Meziantou.Framework.Csv.Internals;

/// <summary>Writes UTF-16 (<see cref="char"/>) or UTF-8 (<see cref="byte"/>) CSV data into a pooled buffer, and sends the buffer to a target.</summary>
/// <remarks>
/// Without a target, a flush keeps the buffer in a list of chunks and continues in a new buffer, twice as large: <see cref="CreateString"/> and <see cref="CreateArray"/> copy the
/// chunks once, into the result. Growing a single buffer would copy the data again at each growth.
/// </remarks>
internal sealed class CsvWriterCore<T> : IDisposable, IAsyncDisposable
    where T : unmanaged, IBinaryInteger<T>
{
    private const int MaxFormatLength = 1024 * 1024 * 1024;
    private static readonly ConcurrentDictionary<(char Separator, char? Quote, string NewLine), (T[] NewLine, SearchValues<T> SpecialValues, SearchValues<char>? Utf16SpecialValues)> TablesCache = new();
    private static readonly T SingleQuote = T.CreateTruncating('\'');

    private readonly CsvTarget<T>? _target;
    private readonly T _separator;
    private readonly T _quote;
    private readonly bool _canQuote;
    private readonly bool _alwaysQuote;
    private readonly bool _escapeFormulas;
    private readonly bool _quoteInvariantNumbers;
    private readonly bool _canCopyPlain;
    private readonly T[] _newLine;
    private readonly SearchValues<T> _specialValues;
    private readonly SearchValues<char>? _utf16SpecialValues;
    private const int MaxChunkSize = 1024 * 1024;

    private int _flushThreshold;
    private List<(T[] Buffer, int Length)>? _chunks;
    private long _chunksLength;

    private T[]? _buffer;
    private int _position;
    private int _fieldCount;
    private int _lastFieldLength;
    private T[]? _scratch;

    public CsvWriterCore(CsvWriterOptions options, CsvTarget<T>? target)
    {
        var utf8 = typeof(T) == typeof(byte);
        CsvWriterOptions.Validate(options, utf8);

        _target = target;
        _separator = T.CreateTruncating(options.Separator);
        _canQuote = options.Quote.HasValue && options.QuoteMode is not CsvQuoteMode.Never;
        _quote = T.CreateTruncating(options.Quote.GetValueOrDefault());
        _alwaysQuote = options.QuoteMode is CsvQuoteMode.Always;
        _escapeFormulas = options.EscapeFormulas;
        FormatProvider = options.FormatProvider ?? CultureInfo.InvariantCulture;
        (_newLine, _specialValues, _utf16SpecialValues) = GetTables(options.Separator, options.Quote, options.NewLine);

        // The characters that require quotes are the separator, the quote, CR and LF, unless the new line has other characters: the values are then
        // checked while they are copied (see CsvSimd.TryCopyPlain)
        _canCopyPlain = _canQuote && !options.NewLine.AsSpan().ContainsAnyExcept('\r', '\n');

        // Numbers and dates written with the invariant culture need quotes only when every value is quoted, or when one of their characters is special
        _quoteInvariantNumbers = _canQuote &&
            (_alwaysQuote || InvariantNumbers.Characters.Contains(options.Separator, StringComparison.Ordinal) || InvariantNumbers.Characters.Contains(options.Quote.GetValueOrDefault(), StringComparison.Ordinal) || options.NewLine.AsSpan().ContainsAny(InvariantNumbers.Characters));
        _buffer = ArrayPool<T>.Shared.Rent(Math.Max(options.BufferSize, 64));
        _flushThreshold = GetFlushThreshold(target is null ? _buffer.Length : options.BufferSize);
    }

    private static int GetFlushThreshold(int bufferSize) => Math.Max(1, bufferSize - (bufferSize / 4));

    // The new line and the characters that require quoting, shared by the writers with the same separator, quote and new line: creating SearchValues
    // is costly compared to writing a few rows. The new line array is never modified. The UTF-8 writer also gets the characters that require quoting
    // as UTF-16, to search the text before converting it, when they are ASCII.
    private static (T[] NewLine, SearchValues<T> SpecialValues, SearchValues<char>? Utf16SpecialValues) GetTables(char separator, char? quote, string newLine)
    {
        var key = (separator, quote, newLine);
        if (TablesCache.TryGetValue(key, out var result))
            return result;

        var specials = new List<char> { separator, '\r', '\n' };
        if (quote is { } quoteChar)
        {
            specials.Add(quoteChar);
        }

        foreach (var c in newLine)
        {
            if (!specials.Contains(c))
            {
                specials.Add(c);
            }
        }

        if (typeof(T) == typeof(byte))
        {
            // A character of the new line string that is not ASCII cannot be found byte by byte: quote the values that contain any non-ASCII character
            var bytes = new List<byte>();
            var hasNonAscii = false;
            foreach (var c in specials)
            {
                if (c <= 0x7F)
                {
                    bytes.Add((byte)c);
                }
                else
                {
                    hasNonAscii = true;
                }
            }

            if (hasNonAscii)
            {
                for (var b = 0x80; b <= 0xFF; b++)
                {
                    bytes.Add((byte)b);
                }
            }

            result = ((T[])(object)Encoding.UTF8.GetBytes(newLine), (SearchValues<T>)(object)SearchValues.Create(bytes.ToArray()), hasNonAscii ? null : SearchValues.Create(specials.ToArray()));
        }
        else
        {
            result = ((T[])(object)newLine.ToCharArray(), (SearchValues<T>)(object)SearchValues.Create(specials.ToArray()), null);
        }

        if (TablesCache.Count < 64)
        {
            TablesCache.TryAdd(key, result);
        }

        return result;
    }

    public IFormatProvider FormatProvider { get; }

    public int FieldCount => _fieldCount;

    public bool ShouldFlush => _position >= _flushThreshold;

    /// <summary>Creates a string from all the data, for a writer without a target.</summary>
    public string CreateString()
    {
        if (typeof(T) != typeof(char) || _target is not null)
            throw new InvalidOperationException();

        var chunks = _chunks;
        var current = Buffer.AsSpan(0, _position);
        if (chunks is null)
            return new string(unsafe(MemoryMarshal.Cast<T, char>(current)));

        var length = checked((int)(_chunksLength + _position));
        return string.Create(length, this, (destination, writer) =>
        {
            foreach (var (buffer, chunkLength) in writer._chunks!)
            {
                var chunk = unsafe(MemoryMarshal.Cast<T, char>(buffer.AsSpan(0, chunkLength)));
                chunk.CopyTo(destination);
                destination = destination[chunkLength..];
            }

            var last = unsafe(MemoryMarshal.Cast<T, char>(writer.Buffer.AsSpan(0, writer._position)));
            last.CopyTo(destination);
        });
    }

    /// <summary>Creates an array from all the data, for a writer without a target.</summary>
    public T[] CreateArray()
    {
        if (_target is not null)
            throw new InvalidOperationException();

        var result = new T[checked((int)(_chunksLength + _position))];
        var destination = result.AsSpan();
        if (_chunks is not null)
        {
            foreach (var (buffer, chunkLength) in _chunks)
            {
                buffer.AsSpan(0, chunkLength).CopyTo(destination);
                destination = destination[chunkLength..];
            }
        }

        Buffer.AsSpan(0, _position).CopyTo(destination);
        return result;
    }

    private T[] Buffer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_buffer is null, this);
            return _buffer;
        }
    }

    public void WriteField(ReadOnlySpan<T> value, bool isText)
    {
        // When every value is quoted, the value is escaped while it is copied
        if (_alwaysQuote && _canQuote)
        {
            if (!(isText && _escapeFormulas))
            {
                WriteQuoted(value);
                return;
            }

            BeginField();
            WriteContent(value, isText);
            _lastFieldLength = value.Length;
            return;
        }

        WriteFieldAsNeeded(value, isText);
    }

    // Not inlined: inlined into the loops of the callers (WriteFields, WriteRow), writing text was 40% slower on x64 (Zen 5), and not faster on Arm64
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void WriteFieldAsNeeded(ReadOnlySpan<T> value, bool isText)
    {
        // Most values need neither quotes nor an escaped formula: the value is copied after a single capacity check. The value is checked while it
        // is copied, or searched before it is copied: reading the copy right after writing it is slower.
        var destination = BeginField(value.Length, out var start);
        _lastFieldLength = value.Length;
        if (!(isText && _escapeFormulas))
        {
            if (_canCopyPlain)
            {
                if (CsvSimd.TryCopyPlain(value, destination, _separator, _quote))
                {
                    _position = start + value.Length;
                    return;
                }
            }
            else if (!_canQuote || !value.ContainsAny(_specialValues))
            {
                value.CopyTo(destination);
                _position = start + value.Length;
                return;
            }
        }

        WriteContent(value, isText);
    }

    // A value quoted because every value is: the quotes are doubled while the value is copied between them, after a single capacity check
    private void WriteQuoted(ReadOnlySpan<T> value)
    {
        var quote = _quote;
        var quoteCount = value.Count(quote);
        var destination = BeginField(checked(value.Length + quoteCount + 2), out var start);
        destination[0] = quote;
        var written = 1;
        for (; quoteCount > 0; quoteCount--)
        {
            var index = value.IndexOf(quote);
            value[..(index + 1)].CopyTo(destination[written..]);
            written += index + 1;
            destination[written++] = quote;
            value = value[(index + 1)..];
        }

        value.CopyTo(destination[written..]);
        written += value.Length;
        destination[written++] = quote;
        _lastFieldLength = written - 2;
        _position = start + written;
    }

    public void WriteUtf16Field(ReadOnlySpan<char> value)
    {
        if (typeof(T) == typeof(char))
        {
            WriteField(unsafe(MemoryMarshal.Cast<char, T>(value)), isText: true);
            return;
        }

        // When every value is quoted, a value without a quote to double is converted between the quotes. The quote is ASCII: it is the same byte in UTF-8.
        // The space of ASCII text is reserved first: 3 bytes per character only when the text is not ASCII.
        if (_alwaysQuote && _canQuote && !_escapeFormulas && !value.Contains((char)ushort.CreateTruncating(_quote)))
        {
            var quoted = unsafe(MemoryMarshal.Cast<T, byte>(BeginField(checked(value.Length + 2), out var quotedStart)));
            if (Ascii.FromUtf16(value, quoted[1..], out var length) is not OperationStatus.Done)
            {
                quoted = unsafe(MemoryMarshal.Cast<T, byte>(GetSpan(checked((value.Length * 3) + 2))));
                Utf8.FromUtf16(value, quoted[1..], out _, out length, replaceInvalidSequences: true);
            }

            quoted[0] = byte.CreateTruncating(_quote);
            quoted[length + 1] = byte.CreateTruncating(_quote);
            _lastFieldLength = length;
            _position = quotedStart + length + 2;
            return;
        }

        // Most values are ASCII and need neither quotes nor an escaped formula: they are checked while they are converted. The space of ASCII text is
        // reserved first: 3 bytes per character only when the text is not ASCII, so that a long ASCII value does not grow the buffer 3 times its size.
        var destination = unsafe(MemoryMarshal.Cast<T, byte>(BeginField(value.Length, out var start)));
        if (_canCopyPlain && !_escapeFormulas && !_alwaysQuote && CsvSimd.TryCopyPlainAscii(value, destination, byte.CreateTruncating(_separator), byte.CreateTruncating(_quote)))
        {
            _lastFieldLength = value.Length;
            _position = start + value.Length;
            return;
        }

        // When the characters that require quotes are ASCII, they are the same bytes in UTF-8: the text is searched before it is converted, as
        // WriteFieldAsNeeded searches before it copies, and the converted text is not read again.
        var isPlain = _utf16SpecialValues is not null && !_escapeFormulas && !_alwaysQuote && !(_canQuote && value.ContainsAny(_utf16SpecialValues));

        // Most text is ASCII, which is converted faster than UTF-8 in general
        if (Ascii.FromUtf16(value, destination, out var bytesWritten) is not OperationStatus.Done)
        {
            destination = unsafe(MemoryMarshal.Cast<T, byte>(GetSpan(checked(value.Length * 3))));
            Utf8.FromUtf16(value, destination, out _, out bytesWritten, replaceInvalidSequences: true);
        }

        if (isPlain)
        {
            _lastFieldLength = bytesWritten;
            _position = start + bytesWritten;
            return;
        }

        CommitField(start, bytesWritten, isText: true);
    }

    public void WriteSpanFormattable<TValue>(TValue value, ReadOnlySpan<char> format, IFormatProvider? provider)
        where TValue : ISpanFormattable
    {
        if (typeof(T) != typeof(char))
            throw new InvalidOperationException();

        var sizeHint = 64;
        var destination = unsafe(MemoryMarshal.Cast<T, char>(BeginField(sizeHint, out var start)));

        var formatProvider = provider ?? FormatProvider;
        var isInvariantNumber = InvariantNumbers.IsInvariantNumber<TValue>(format, formatProvider);

        // Decimals are written faster than by the BCL in the default format of the invariant culture, as the serializer does
        if (typeof(TValue) == typeof(decimal) && isInvariantNumber &&
            DecimalCsvConverter.TryFormatPlain((decimal)(object)value, destination, out var plainWritten, out var tooSmall) && !tooSmall)
        {
            CommitNumber(start, plainWritten, isInvariantNumber);
            return;
        }

        // Doubles and floats too (see InvariantFloatingPoint.TryFormat)
        if (typeof(TValue) == typeof(double) && format.IsEmpty && InvariantNumbers.IsInvariant(formatProvider) &&
            InvariantFloatingPoint.TryFormat((double)(object)value, destination, out var doubleWritten, out var doubleTooSmall) && !doubleTooSmall)
        {
            CommitNumber(start, doubleWritten, isInvariantNumber);
            return;
        }

        if (typeof(TValue) == typeof(float) && format.IsEmpty && InvariantNumbers.IsInvariant(formatProvider) &&
            InvariantFloatingPoint.TryFormat((float)(object)value, destination, out var singleWritten, out var singleTooSmall) && !singleTooSmall)
        {
            CommitNumber(start, singleWritten, isInvariantNumber);
            return;
        }

        while (true)
        {
            if (value.TryFormat(destination, out var written, format, formatProvider))
            {
                CommitNumber(start, written, isInvariantNumber);
                return;
            }

            sizeHint = GrowFormatSize(Math.Max(sizeHint, destination.Length));
            destination = unsafe(MemoryMarshal.Cast<T, char>(GetSpan(sizeHint)));
        }
    }

    public void WriteUtf8SpanFormattable<TValue>(TValue value, ReadOnlySpan<char> format, IFormatProvider? provider)
        where TValue : IUtf8SpanFormattable
    {
        if (typeof(T) != typeof(byte))
            throw new InvalidOperationException();

        var sizeHint = 64;
        var destination = unsafe(MemoryMarshal.Cast<T, byte>(BeginField(sizeHint, out var start)));

        var formatProvider = provider ?? FormatProvider;
        var isInvariantNumber = InvariantNumbers.IsInvariantNumber<TValue>(format, formatProvider);

        // Decimals are written faster than by the BCL in the default format of the invariant culture, as the serializer does
        if (typeof(TValue) == typeof(decimal) && isInvariantNumber &&
            DecimalCsvConverter.TryFormatPlain((decimal)(object)value, destination, out var plainWritten, out var tooSmall) && !tooSmall)
        {
            CommitNumber(start, plainWritten, isInvariantNumber);
            return;
        }

        // Doubles and floats too (see InvariantFloatingPoint.TryFormat)
        if (typeof(TValue) == typeof(double) && format.IsEmpty && InvariantNumbers.IsInvariant(formatProvider) &&
            InvariantFloatingPoint.TryFormat((double)(object)value, destination, out var doubleWritten, out var doubleTooSmall) && !doubleTooSmall)
        {
            CommitNumber(start, doubleWritten, isInvariantNumber);
            return;
        }

        if (typeof(TValue) == typeof(float) && format.IsEmpty && InvariantNumbers.IsInvariant(formatProvider) &&
            InvariantFloatingPoint.TryFormat((float)(object)value, destination, out var singleWritten, out var singleTooSmall) && !singleTooSmall)
        {
            CommitNumber(start, singleWritten, isInvariantNumber);
            return;
        }

        while (true)
        {
            if (value.TryFormat(destination, out var written, format, formatProvider))
            {
                CommitNumber(start, written, isInvariantNumber);
                return;
            }

            sizeHint = GrowFormatSize(Math.Max(sizeHint, destination.Length));
            destination = unsafe(MemoryMarshal.Cast<T, byte>(GetSpan(sizeHint)));
        }
    }

    internal static int GrowFormatSize(int sizeHint)
    {
        if (sizeHint >= MaxFormatLength)
            throw new CsvException("The formatted value is too long.");

        return sizeHint * 2;
    }

    /// <summary>Writes the separator when the field is not the first of the row, and returns the position where the content of the field starts.</summary>
    public int BeginField()
    {
        if (_fieldCount > 0)
        {
            EnsureCapacity(1);
            Buffer[_position++] = _separator;
        }

        _fieldCount++;
        return _position;
    }

    /// <summary>Writes the separator when the field is not the first of the row, and returns a span of at least <paramref name="sizeHint"/> elements to write its content.</summary>
    /// <remarks>This is <see cref="BeginField()"/> and <see cref="GetSpan"/> with one capacity check, for the values formatted in place.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> BeginField(int sizeHint, out int start)
    {
        var buffer = _buffer;
        ObjectDisposedException.ThrowIf(buffer is null, this);
        if (buffer.Length - _position <= sizeHint)
        {
            EnsureCapacity(sizeHint + 1);
            buffer = _buffer!;
        }

        if (_fieldCount++ > 0)
        {
            buffer[_position++] = _separator;
        }

        start = _position;
        return buffer.AsSpan(_position);
    }

    /// <summary>Gets a span to write the content of the field that starts at the current position.</summary>
    public Span<T> GetSpan(int sizeHint)
    {
        EnsureCapacity(sizeHint);
        return Buffer.AsSpan(_position);
    }

    /// <summary>Commits a formatted value: a number or a date written with the invariant culture is not searched for characters that need quotes, unless one of its characters is special.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CommitNumber(int start, int length, bool isInvariantNumber)
    {
        if (isInvariantNumber && !_quoteInvariantNumbers)
        {
            _lastFieldLength = length;
            _position = start + length;
            return;
        }

        CommitField(start, length, isText: false);
    }

    /// <summary>Quotes or escapes the content written in place by the caller, from <paramref name="start"/>, when needed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CommitField(int start, int length, bool isText)
    {
        _lastFieldLength = length;

        // Most values need neither quotes nor an escaped formula: they stay where the caller wrote them
        var needsQuote = _canQuote && (_alwaysQuote || _buffer.AsSpan(start, length).ContainsAny(_specialValues));
        if (!needsQuote && !(isText && _escapeFormulas))
        {
            _position = start + length;
            return;
        }

        EscapeField(start, length, isText);
    }

    private void EscapeField(int start, int length, bool isText)
    {
        var content = Buffer.AsSpan(start, length);
        if (!NeedsEscaping(content, isText, out _, out _))
        {
            _position = start + length;
            return;
        }

        if (_scratch is null || _scratch.Length < length)
        {
            if (_scratch is not null)
            {
                ArrayPool<T>.Shared.Return(_scratch);
            }

            _scratch = ArrayPool<T>.Shared.Rent(Math.Max(length, 256));
        }

        content.CopyTo(_scratch);
        _position = start;
        WriteContent(_scratch.AsSpan(0, length), isText);
    }

    private bool NeedsEscaping(ReadOnlySpan<T> value, bool isText, out bool formula, out bool quote)
    {
        formula = isText && _escapeFormulas && !value.IsEmpty && IsFormulaStart(value[0]);

        // When the quote character is the apostrophe, the prefix of an escaped formula would open a quoted value: the value is quoted, and the
        // prefix is doubled as any quote
        quote = _canQuote && (_alwaysQuote || (formula && _quote == SingleQuote) || value.ContainsAny(_specialValues));
        return formula || quote;
    }

    private static bool IsFormulaStart(T value)
    {
        return value == T.CreateTruncating('=') || value == T.CreateTruncating('+') || value == T.CreateTruncating('-') || value == T.CreateTruncating('@') || value == T.CreateTruncating('\t') || value == T.CreateTruncating('\r');
    }

    private void WriteContent(ReadOnlySpan<T> value, bool isText)
    {
        NeedsEscaping(value, isText, out var formula, out var quote);
        if (!quote)
        {
            EnsureCapacity(value.Length + (formula ? 1 : 0));
            var buffer = Buffer;
            if (formula)
            {
                buffer[_position++] = SingleQuote;
            }

            value.CopyTo(buffer.AsSpan(_position));
            _position += value.Length;
            return;
        }

        var quoteChar = _quote;
        var quoteCount = value.Count(quoteChar);
        var prefixLength = !formula ? 0 : quoteChar == SingleQuote ? 2 : 1;
        EnsureCapacity(checked(value.Length + quoteCount + 2 + prefixLength));
        var destination = Buffer.AsSpan(_position);
        var written = 0;
        destination[written++] = quoteChar;
        for (var i = 0; i < prefixLength; i++)
        {
            destination[written++] = SingleQuote;
        }

        // Most quoted values have no quote to double: the count says it, without searching the value again
        for (; quoteCount > 0; quoteCount--)
        {
            var index = value.IndexOf(quoteChar);
            value[..(index + 1)].CopyTo(destination[written..]);
            written += index + 1;
            destination[written++] = quoteChar;
            value = value[(index + 1)..];
        }

        value.CopyTo(destination[written..]);
        written += value.Length;
        destination[written++] = quoteChar;
        _position += written;
    }

    public void EndRow()
    {
        // Most rows end with a new line of two characters, which fits in the buffer: it is written without a call to copy it. The rest is in
        // another method, so that the method stays small enough to be inlined in the loops of the callers without taking their inlining budget.
        var buffer = _buffer;
        var newLine = _newLine;
        var position = _position;
        if (buffer is not null && newLine.Length == 2 && buffer.Length - position >= 2 && _fieldCount != 1)
        {
            buffer[position] = newLine[0];
            buffer[position + 1] = newLine[1];
            _position = position + 2;
            _fieldCount = 0;
            _lastFieldLength = 0;
            return;
        }

        EndRowSlow();
    }

    private void EndRowSlow()
    {
        // A row with a single empty value would read back as an empty line, which has no value
        if (_fieldCount == 1 && _lastFieldLength == 0 && _canQuote && !_alwaysQuote)
        {
            EnsureCapacity(2);
            Buffer[_position++] = _quote;
            Buffer[_position++] = _quote;
        }

        EnsureCapacity(_newLine.Length);
        _newLine.CopyTo(Buffer.AsSpan(_position));
        _position += _newLine.Length;
        _fieldCount = 0;
        _lastFieldLength = 0;
    }

    private void EnsureCapacity(int count)
    {
        var buffer = Buffer;
        if (buffer.Length - _position >= count)
            return;

        var newSize = Math.Max((long)buffer.Length * 2, (long)_position + count);
        if (newSize > Array.MaxLength)
        {
            newSize = Math.Max(Array.MaxLength, _position + count);
        }

        var newBuffer = ArrayPool<T>.Shared.Rent((int)newSize);
        buffer.AsSpan(0, _position).CopyTo(newBuffer);
        ArrayPool<T>.Shared.Return(buffer);
        _buffer = newBuffer;
    }

    public void FlushBuffer()
    {
        if (_position == 0)
            return;

        if (_target is null)
        {
            AddChunk();
            return;
        }

        _target.Write(Buffer.AsSpan(0, _position));
        _position = 0;
    }

    public async ValueTask FlushBufferAsync(CancellationToken cancellationToken)
    {
        if (_position == 0)
            return;

        if (_target is null)
        {
            AddChunk();
            return;
        }

        await _target.WriteAsync(Buffer.AsMemory(0, _position), cancellationToken).ConfigureAwait(false);
        _position = 0;
    }

    private void AddChunk()
    {
        var buffer = Buffer;
        (_chunks ??= []).Add((buffer, _position));
        _chunksLength += _position;
        _buffer = ArrayPool<T>.Shared.Rent(Math.Min(buffer.Length * 2, Math.Max(MaxChunkSize, buffer.Length)));
        _flushThreshold = GetFlushThreshold(_buffer.Length);
        _position = 0;
    }

    // Without a target, the data stays in the buffer and the chunks: only a full buffer becomes a chunk
    public void Flush()
    {
        if (_target is null)
            return;

        FlushBuffer();
        _target.Flush();
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_target is null)
            return;

        await FlushBufferAsync(cancellationToken).ConfigureAwait(false);
        await _target.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_buffer is null)
            return;

        try
        {
            Flush();
        }
        finally
        {
            ReturnBuffers();
            _target?.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_buffer is null)
            return;

        try
        {
            await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            ReturnBuffers();
            if (_target is not null)
            {
                await _target.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private void ReturnBuffers()
    {
        if (_buffer is not null)
        {
            ArrayPool<T>.Shared.Return(_buffer);
            _buffer = null;
        }

        if (_scratch is not null)
        {
            ArrayPool<T>.Shared.Return(_scratch);
            _scratch = null;
        }

        if (_chunks is not null)
        {
            foreach (var (buffer, _) in _chunks)
            {
                ArrayPool<T>.Shared.Return(buffer);
            }

            _chunks = null;
            _chunksLength = 0;
        }
    }
}
