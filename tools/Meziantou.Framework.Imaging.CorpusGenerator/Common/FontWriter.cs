namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>Writes big-endian values, the byte order of the OpenType tables.</summary>
internal sealed class FontWriter
{
    private readonly List<byte> _bytes = [];

    public int Length => _bytes.Count;

    public byte[] ToArray() => [.. _bytes];

    public void Byte(byte value) => _bytes.Add(value);

    public void Bytes(ReadOnlySpan<byte> value) => _bytes.AddRange(value);

    public void Zeros(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _bytes.Add(0);
        }
    }

    public void Pad(int alignment)
    {
        while (_bytes.Count % alignment != 0)
        {
            _bytes.Add(0);
        }
    }

    public void UInt16(int value)
    {
        if (value is < 0 or > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value does not fit in 16 bits");

        _bytes.Add((byte)(value >> 8));
        _bytes.Add((byte)value);
    }

    public void Int16(int value)
    {
        if (value is < short.MinValue or > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value does not fit in 16 bits");

        _bytes.Add((byte)(value >> 8));
        _bytes.Add((byte)value);
    }

    public void UInt24(int value)
    {
        if (value is < 0 or > 0xFFFFFF)
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value does not fit in 24 bits");

        _bytes.Add((byte)(value >> 16));
        _bytes.Add((byte)(value >> 8));
        _bytes.Add((byte)value);
    }

    public void UInt32(uint value)
    {
        _bytes.Add((byte)(value >> 24));
        _bytes.Add((byte)(value >> 16));
        _bytes.Add((byte)(value >> 8));
        _bytes.Add((byte)value);
    }

    public void Int32(int value) => UInt32((uint)value);

    /// <summary>Writes a signed fixed-point number with 14 fractional bits.</summary>
    public void F2Dot14(double value) => Int16((int)Math.Round(value * 16384, MidpointRounding.AwayFromZero));

    public void Tag(string tag)
    {
        if (tag.Length != 4)
            throw new ArgumentException("A tag has four characters", nameof(tag));

        foreach (var c in tag)
        {
            _bytes.Add((byte)c);
        }
    }

    public void PatchUInt32(int offset, uint value)
    {
        _bytes[offset] = (byte)(value >> 24);
        _bytes[offset + 1] = (byte)(value >> 16);
        _bytes[offset + 2] = (byte)(value >> 8);
        _bytes[offset + 3] = (byte)value;
    }
}
