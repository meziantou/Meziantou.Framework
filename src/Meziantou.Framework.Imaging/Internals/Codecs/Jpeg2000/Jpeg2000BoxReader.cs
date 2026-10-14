using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Reads the boxes held in the content of a superbox that is in memory (T.800, I.4).</summary>
internal ref struct Jpeg2000BoxReader
{
    private ReadOnlySpan<byte> _remaining;

    public Jpeg2000BoxReader(ReadOnlySpan<byte> content)
    {
        _remaining = content;
    }

    /// <summary>Reads the next box.</summary>
    /// <param name="type">Receives the type of the box.</param>
    /// <param name="content">Receives the content of the box.</param>
    /// <returns><see langword="false"/> at the end of the superbox.</returns>
    /// <exception cref="InvalidImageContentException">A box does not fit in the superbox.</exception>
    public bool TryRead(out uint type, out ReadOnlySpan<byte> content)
    {
        type = 0;
        content = default;
        if (_remaining.IsEmpty)
            return false;

        if (_remaining.Length < Jpeg2000Format.BoxHeaderLength)
            throw Jpeg2000Format.Invalid("a superbox ends inside the header of a box.");

        var size = BinaryPrimitives.ReadUInt32BigEndian(_remaining);
        type = BinaryPrimitives.ReadUInt32BigEndian(_remaining[4..]);
        var headerLength = Jpeg2000Format.BoxHeaderLength;
        long length;
        if (size == 1)
        {
            headerLength = Jpeg2000Format.LargeBoxHeaderLength;
            if (_remaining.Length < headerLength)
                throw Jpeg2000Format.Invalid("a superbox ends inside the header of a box.");

            var largeSize = BinaryPrimitives.ReadUInt64BigEndian(_remaining[8..]);
            if (largeSize > long.MaxValue)
                throw Jpeg2000Format.Invalid($"the box '{Jpeg2000Format.ToText(type)}' is larger than the superbox that holds it.");

            length = (long)largeSize;
        }
        else if (size == 0)
        {
            // The last box of the superbox extends to its end
            length = _remaining.Length;
        }
        else
        {
            length = size;
        }

        if (length < headerLength)
            throw Jpeg2000Format.Invalid($"the box '{Jpeg2000Format.ToText(type)}' has a size that does not hold its header.");

        if (length > _remaining.Length)
            throw Jpeg2000Format.Invalid($"the box '{Jpeg2000Format.ToText(type)}' is larger than the superbox that holds it.");

        content = _remaining[headerLength..(int)length];
        _remaining = _remaining[(int)length..];
        return true;
    }
}
