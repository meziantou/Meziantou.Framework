using System.Buffers;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Pdf.Writing;

/// <summary>A growable buffer of bytes that knows its position, with the formatting rules of the PDF syntax.</summary>
internal sealed class PdfByteWriter : IBufferWriter<byte>
{
    private byte[] _buffer;
    private int _length;

    public PdfByteWriter(int capacity = 4096)
    {
        _buffer = new byte[Math.Max(16, capacity)];
    }

    public int Length => _length;

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _length);

    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _length);

    public void Clear() => _length = 0;

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

        if (required > Array.MaxLength)
            throw new PdfResourceLimitException("The output exceeds the maximum size of an array");

        var newSize = Math.Max(required, Math.Min((long)_buffer.Length * 2, Array.MaxLength));
        Array.Resize(ref _buffer, (int)newSize);
    }
}
