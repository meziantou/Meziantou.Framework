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
/// Without a target, a flush keeps the buffer in a list of chunks and continues in a new buffer, twice as large: <see cref="CreateString"/> copies the
/// chunks once, into the string. Growing a single buffer would copy the data again at each growth.
/// </remarks>
internal sealed class CsvWriterCore<T> : IDisposable, IAsyncDisposable
    where T : unmanaged, IBinaryInteger<T>
{
    private const int MaxFormatLength = 1024 * 1024 * 1024;
    private static readonly ConcurrentDictionary<(char Separator, char? Quote, string NewLine), (T[] NewLine, SearchValues<T> SpecialValues)> TablesCache = new();
    private static readonly T SingleQuote = T.CreateTruncating('\'');

    private readonly CsvTarget<T>? _target;
    private readonly T _separator;
    private readonly T _quote;
    private readonly bool _canQuote;
    private readonly bool _alwaysQuote;
    private readonly bool _escapeFormulas;
    private readonly bool _quoteInvariantNumbers;
    private readonly T[] _newLine;
    private readonly SearchValues<T> _specialValues;
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
        (_newLine, _specialValues) = GetTables(options.Separator, options.Quote, options.NewLine);

        // Numbers and dates written with the invariant culture need quotes only when every value is quoted, or when one of their characters is special
        _quoteInvariantNumbers = _canQuote &&
            (_alwaysQuote || InvariantNumbers.Characters.Contains(options.Separator, StringComparison.Ordinal) || InvariantNumbers.Characters.Contains(options.Quote.GetValueOrDefault(), StringComparison.Ordinal) || options.NewLine.AsSpan().ContainsAny(InvariantNumbers.Characters));
        _buffer = ArrayPool<T>.Shared.Rent(Math.Max(options.BufferSize, 64));
        _flushThreshold = GetFlushThreshold(target is null ? _buffer.Length : options.BufferSize);
    }

    private static int GetFlushThreshold(int bufferSize) => Math.Max(1, bufferSize - (bufferSize / 4));

    // The new line and the characters that require quoting, shared by the writers with the same separator, quote and new line: creating SearchValues
    // is costly compared to writing a few rows. The new line array is never modified.
    private static (T[] NewLine, SearchValues<T> SpecialValues) GetTables(char separator, char? quote, string newLine)
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

            result = ((T[])(object)Encoding.UTF8.GetBytes(newLine), (SearchValues<T>)(object)SearchValues.Create(bytes.ToArray()));
        }
        else
        {
            result = ((T[])(object)newLine.ToCharArray(), (SearchValues<T>)(object)SearchValues.Create(specials.ToArray()));
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
        // Most values need neither quotes nor an escaped formula: the value is copied after a single capacity check. The value is searched before
        // it is copied: reading the copy right after writing it is slower.
        var destination = BeginField(value.Length, out var start);
        _lastFieldLength = value.Length;
        if ((isText && _escapeFormulas) || (_canQuote && value.ContainsAny(_specialValues)))
        {
            WriteContent(value, isText);
            return;
        }

        value.CopyTo(destination);
        _position = start + value.Length;
    }

    public void WriteUtf16Field(ReadOnlySpan<char> value)
    {
        if (typeof(T) == typeof(char))
        {
            WriteField(unsafe(MemoryMarshal.Cast<char, T>(value)), isText: true);
            return;
        }

        var destination = unsafe(MemoryMarshal.Cast<T, byte>(BeginField(checked(value.Length * 3), out var start)));

        // Most text is ASCII, which is converted faster than UTF-8 in general
        if (Ascii.FromUtf16(value, destination, out var bytesWritten) is not OperationStatus.Done)
        {
            Utf8.FromUtf16(value, destination, out _, out bytesWritten, replaceInvalidSequences: true);
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

        while (true)
        {
            var index = value.IndexOf(quoteChar);
            if (index < 0)
            {
                value.CopyTo(destination[written..]);
                written += value.Length;
                break;
            }

            value[..(index + 1)].CopyTo(destination[written..]);
            written += index + 1;
            destination[written++] = quoteChar;
            value = value[(index + 1)..];
        }

        destination[written++] = quoteChar;
        _position += written;
    }

    public void EndRow()
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
