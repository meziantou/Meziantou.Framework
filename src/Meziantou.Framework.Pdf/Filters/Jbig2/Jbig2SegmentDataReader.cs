namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>Reads the big-endian fields of the data of a JBIG2 segment, within its bounds.</summary>
internal sealed class Jbig2SegmentDataReader(byte[] data, int offset, int length)
{
    private readonly int _end = offset + length;

    public int Position { get; private set; } = offset;

    public int Remaining => _end - Position;

    public int ReadByte()
    {
        Ensure(1);
        return data[Position++];
    }

    public int ReadSByte() => (sbyte)ReadByte();

    public int ReadUInt16()
    {
        Ensure(2);
        var value = (data[Position] << 8) | data[Position + 1];
        Position += 2;
        return value;
    }

    public uint ReadUInt32()
    {
        Ensure(4);
        var value = ((uint)data[Position] << 24) | ((uint)data[Position + 1] << 16) | ((uint)data[Position + 2] << 8) | data[Position + 3];
        Position += 4;
        return value;
    }

    public int ReadInt32() => (int)ReadUInt32();

    /// <summary>Reads a dimension: an unsigned 32-bit number that must fit an <see cref="int"/>.</summary>
    public int ReadDimension()
    {
        var value = ReadUInt32();

        // 0xFFFFFFFF is the height of a region whose height is given at its end (7.4.6.4)
        if (value == uint.MaxValue)
            return int.MaxValue;

        if (value > int.MaxValue)
            throw new InvalidPdfContentException("A JBIG2 dimension is out of range");

        return (int)value;
    }

    /// <summary>Reads the region segment information field (7.4.1): the size, the position and the combination operator.</summary>
    public Jbig2RegionInfo ReadRegionInfo()
    {
        var width = ReadDimension();
        var height = ReadDimension();
        var x = ReadInt32();
        var y = ReadInt32();
        var flags = ReadByte();
        return new Jbig2RegionInfo(width, height, x, y, flags & 7);
    }

    private void Ensure(int count)
    {
        if (Position + count > _end)
            throw new InvalidPdfContentException("A JBIG2 segment is truncated");
    }
}
