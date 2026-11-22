namespace Meziantou.Framework.FileConversionTool.Mobi;

/// <summary>The decompressed text of a book, which cannot grow past a maximum length.</summary>
internal sealed class MobiBuffer(int maxLength)
{
    private byte[] _buffer = new byte[Math.Min(maxLength, 4096)];

    public int Length { get; private set; }

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, Length);

    /// <exception cref="InvalidDataException">The text is longer than the maximum length.</exception>
    public void Append(byte value)
    {
        EnsureCapacity(1);
        _buffer[Length++] = value;
    }

    /// <exception cref="InvalidDataException">The text is longer than the maximum length.</exception>
    public void Append(ReadOnlySpan<byte> value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(_buffer.AsSpan(Length));
        Length += value.Length;
    }

    /// <summary>Appends a copy of the bytes that start <paramref name="distance"/> bytes before the end, which can overlap the bytes it appends.</summary>
    /// <exception cref="InvalidDataException">The text is longer than the maximum length.</exception>
    public void AppendBackReference(int distance, int length)
    {
        EnsureCapacity(length);
        var source = Length - distance;
        for (var i = 0; i < length; i++)
        {
            _buffer[Length++] = _buffer[source + i];
        }
    }

    public byte[] ToArray() => _buffer.AsSpan(0, Length).ToArray();

    private void EnsureCapacity(int count)
    {
        if (count > maxLength - Length)
            throw new InvalidDataException($"The text of the book is longer than {maxLength.ToString(CultureInfo.InvariantCulture)} bytes");

        if (Length + count <= _buffer.Length)
            return;

        var capacity = (int)Math.Min(maxLength, Math.Max(Length + (long)count, _buffer.Length * 2L));
        Array.Resize(ref _buffer, capacity);
    }
}
