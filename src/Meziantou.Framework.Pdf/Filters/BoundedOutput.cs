using System.Globalization;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>The output of a filter: a growable buffer that refuses to grow beyond the limit of the decoded data.</summary>
internal sealed class BoundedOutput
{
    private byte[] _buffer;
    private int _length;

    public BoundedOutput(long limit, int initialCapacity)
    {
        Limit = limit;
        _buffer = new byte[(int)Math.Clamp(initialCapacity, 16, Math.Min(limit, 1 << 20))];
    }

    public long Limit { get; }

    public int Length => _length;

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _length);

    public void Write(byte value)
    {
        if (_length == _buffer.Length)
        {
            Grow(1);
        }

        _buffer[_length++] = value;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.Length > _buffer.Length - _length)
        {
            Grow(data.Length);
        }

        data.CopyTo(_buffer.AsSpan(_length));
        _length += data.Length;
    }

    /// <summary>Writes a byte several times (run-length).</summary>
    public void WriteRepeated(byte value, int count)
    {
        if (count > _buffer.Length - _length)
        {
            Grow(count);
        }

        _buffer.AsSpan(_length, count).Fill(value);
        _length += count;
    }

    public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();

    private void Grow(int count)
    {
        var required = (long)_length + count;
        if (required > Limit)
            throw new PdfResourceLimitException($"The decoded data of a stream exceeds {Limit.ToString(CultureInfo.InvariantCulture)} bytes ({nameof(PdfReadOptions)}.{nameof(PdfReadOptions.MaxDecodedStreamLength)})");

        if (required > Array.MaxLength)
            throw new PdfResourceLimitException("The decoded data of a stream exceeds the maximum size of an array");

        var newSize = Math.Min(Math.Max(required, (long)_buffer.Length * 2), Math.Min(Limit, Array.MaxLength));
        Array.Resize(ref _buffer, (int)newSize);
    }
}
