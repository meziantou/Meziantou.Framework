using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Meziantou.Framework.Pdf.Writing;

/// <summary>
/// A growable buffer of bytes that knows its position, with the formatting rules of the PDF syntax. With a
/// <see cref="PdfOutputSink"/>, the buffer has a bounded size: it is passed to the sink when it is full, so that a save
/// does not hold the whole file in one array.
/// </summary>
internal sealed class PdfByteWriter : IBufferWriter<byte>
{
    private readonly PdfOutputSink? _sink;
    private byte[] _buffer;
    private int _length;
    private long _flushed;

    // The hash of the bytes written since BeginHash: the bytes of the buffer from _hashStart are not hashed yet
    private IncrementalHash? _hash;
    private int _hashStart;

    public PdfByteWriter(int capacity = 4096)
    {
        _buffer = new byte[Math.Max(16, capacity)];
    }

    /// <summary>Initializes a writer that passes its buffer to <paramref name="sink"/> when it is full.</summary>
    public PdfByteWriter(PdfOutputSink sink, int bufferSize = 64 * 1024)
        : this(bufferSize)
    {
        _sink = sink;
    }

    /// <summary>Gets the number of bytes in the buffer: for a writer without sink, the number of bytes written.</summary>
    public int Length => _length;

    /// <summary>Gets the number of bytes written, including the bytes passed to the sink.</summary>
    public long Position => _flushed + _length;

    /// <summary>Gets the bytes in the buffer: for a writer without sink, the bytes written.</summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _length);

    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _length);

    public void Clear()
    {
        _length = 0;
        _hashStart = 0;
    }

    /// <summary>Starts the MD5 hash of the bytes written from now on (the file identifier, 14.4).</summary>
    public void BeginHash()
    {
        _hash?.Dispose();
        _hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        _hashStart = _length;
    }

    /// <summary>Gets the hash of the bytes written since <see cref="BeginHash"/>, and stops hashing.</summary>
    public byte[] EndHash()
    {
        var hash = _hash ?? throw new InvalidOperationException("BeginHash was not called");
        hash.AppendData(_buffer, _hashStart, _length - _hashStart);
        _hash = null;
        _hashStart = 0;
        using (hash)
        {
            return hash.GetHashAndReset();
        }
    }

    /// <summary>Passes the bytes of the buffer to the sink.</summary>
    public void Flush()
    {
        if (_sink is null || _length == 0)
            return;

        _hash?.AppendData(_buffer, _hashStart, _length - _hashStart);
        _hashStart = 0;
        _flushed += _length;
        var count = _length;
        _length = 0;
        _buffer = _sink.Flush(_buffer, count);
    }

    /// <summary>
    /// Writes bytes that are not modified until the save completes (the original file of an incremental update, the
    /// data of a stream): a sink keeps a large block as it is, without copying it into the buffer.
    /// </summary>
    public void Write(ReadOnlyMemory<byte> data)
    {
        if (_sink is null || data.Length < _buffer.Length / 4)
        {
            Write(data.Span);
            return;
        }

        Flush();
        _hash?.AppendData(data.Span);
        _flushed += data.Length;
        _sink.Write(data, isImmutable: true);
    }

    public byte[] ToArray() => WrittenSpan.ToArray();

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_length + count > _buffer.Length)
            throw new InvalidOperationException("Cannot advance past the end of the buffer");

        _length += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(Math.Max(sizeHint, 1));
        return _buffer.AsMemory(_length);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(Math.Max(sizeHint, 1));
        return _buffer.AsSpan(_length);
    }

    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_length++] = value;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (_sink is not null && data.Length >= _buffer.Length / 4)
        {
            Flush();
            _hash?.AppendData(data);
            _flushed += data.Length;
            _sink.Write(data);
            return;
        }

        EnsureCapacity(data.Length);
        data.CopyTo(_buffer.AsSpan(_length));
        _length += data.Length;
    }

    /// <summary>Writes a string whose characters are all ASCII.</summary>
    public void WriteAscii(string value) => WriteAscii(value.AsSpan());

    public void WriteAscii(ReadOnlySpan<char> value)
    {
        EnsureCapacity(value.Length);
        var count = Encoding.ASCII.GetBytes(value, _buffer.AsSpan(_length));
        _length += count;
    }

    public void WriteInteger(long value)
    {
        EnsureCapacity(20);
        value.TryFormat(_buffer.AsSpan(_length), out var written, provider: CultureInfo.InvariantCulture);
        _length += written;
    }

    /// <summary>Writes an integer padded with zeros on the left (cross-reference tables).</summary>
    public void WritePaddedInteger(long value, int width)
    {
        EnsureCapacity(Math.Max(width, 20));
        Span<char> format = stackalloc char[3];
        format[0] = 'D';
        width.TryFormat(format[1..], out var formatLength, provider: CultureInfo.InvariantCulture);
        value.TryFormat(_buffer.AsSpan(_length), out var written, format[..(1 + formatLength)], CultureInfo.InvariantCulture);
        _length += written;
    }

    public void WriteNumber(double value)
    {
        EnsureCapacity(PdfNumberFormatter.MaxLength);
        _length += PdfNumberFormatter.Format(value, _buffer.AsSpan(_length));
    }

    public void WriteNewLine() => WriteByte((byte)'\n');

    public void WriteSpace() => WriteByte((byte)' ');

    private void EnsureCapacity(int count)
    {
        var required = (long)_length + count;
        if (required <= _buffer.Length)
            return;

        if (_sink is not null)
        {
            Flush();
            required = count;
            if (required <= _buffer.Length)
                return;
        }

        if (required > Array.MaxLength)
            throw new PdfResourceLimitException("The output exceeds the maximum size of an array");

        var newSize = Math.Max(required, Math.Min((long)_buffer.Length * 2, Array.MaxLength));
        Array.Resize(ref _buffer, (int)newSize);
    }
}
