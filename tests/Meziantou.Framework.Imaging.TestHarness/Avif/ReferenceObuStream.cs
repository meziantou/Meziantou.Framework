namespace Meziantou.Framework.Imaging.TestHarness.Avif;

/// <summary>
/// The units of an AV1 low overhead bitstream (AV1 specification, section 5.3 "OBU syntax") and the operating points of its
/// sequence header (section 5.5.1), read from the bytes independently of the library under test: what the tests need to
/// tell which spatial layers a stream holds, to remove units and to check what a fixture claims. No frame is decoded.
/// </summary>
public static class ReferenceObuStream
{
    /// <summary>The type of a sequence header unit.</summary>
    public const int SequenceHeader = 1;

    /// <summary>The type of a temporal delimiter unit.</summary>
    public const int TemporalDelimiter = 2;

    /// <summary>The type of a frame header unit.</summary>
    public const int FrameHeader = 3;

    /// <summary>The type of a frame unit (a frame header and its tile group).</summary>
    public const int Frame = 6;

    /// <summary>Reads the units of a stream. Every unit must have a size field.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The units, in order.</returns>
    /// <exception cref="InvalidDataException">A unit has no size field or exceeds the stream.</exception>
    public static IReadOnlyList<Unit> ReadUnits(ReadOnlySpan<byte> stream)
    {
        var units = new List<Unit>();
        var offset = 0;
        while (offset < stream.Length)
        {
            var header = stream[offset];
            if ((header & 0x02) == 0)
                throw new InvalidDataException("A unit of the stream has no size field.");

            var hasExtension = (header & 0x04) != 0;
            var position = offset + 1;
            var (temporalId, spatialId) = (0, 0);
            if (hasExtension)
            {
                if (position >= stream.Length)
                    throw new InvalidDataException("A unit of the stream is truncated.");

                (temporalId, spatialId) = (stream[position] >> 5, (stream[position] >> 3) & 3);
                position++;
            }

            var size = 0L;
            for (var shift = 0; ; shift += 7)
            {
                if (position >= stream.Length || shift > 28)
                    throw new InvalidDataException("The size of a unit of the stream is truncated.");

                var value = stream[position++];
                size |= (long)(value & 0x7F) << shift;
                if ((value & 0x80) == 0)
                    break;
            }

            if (position + size > stream.Length)
                throw new InvalidDataException("A unit of the stream is truncated.");

            units.Add(new Unit((header >> 3) & 15, offset, position - offset, (int)size, hasExtension, temporalId, spatialId));
            offset = position + (int)size;
        }

        return units;
    }

    /// <summary>Gets the spatial layers of the frames of a stream, in increasing order.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The distinct spatial layers of its frame and frame header units.</returns>
    public static IReadOnlyList<int> GetSpatialLayers(ReadOnlySpan<byte> stream) =>
        [.. ReadUnits(stream).Where(unit => unit.Type is Frame or FrameHeader).Select(unit => unit.SpatialId).Distinct().Order()];

    /// <summary>Copies a stream without some of its units.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="remove">Whether a unit is left out.</param>
    /// <returns>The remaining units, in order.</returns>
    public static byte[] Remove(ReadOnlySpan<byte> stream, Func<Unit, bool> remove)
    {
        ArgumentNullException.ThrowIfNull(remove);
        var result = new List<byte>(stream.Length);
        foreach (var unit in ReadUnits(stream))
        {
            if (!remove(unit))
                result.AddRange(stream.Slice(unit.Offset, unit.HeaderLength + unit.PayloadLength));
        }

        return [.. result];
    }

    /// <summary>
    /// Reads the <c>operating_point_idc</c> of each operating point of the first sequence header of a stream: the temporal
    /// layers of the operating point in bits 0 to 7 and its spatial layers in bits 8 to 11 (section 6.4.1). A reduced still
    /// picture header has one operating point, whose value is 0.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The values, in the order of the sequence header.</returns>
    /// <exception cref="InvalidDataException">The stream has no sequence header.</exception>
    /// <exception cref="NotSupportedException">The sequence header has timing information, which this reader does not skip.</exception>
    public static IReadOnlyList<int> ReadOperatingPoints(ReadOnlySpan<byte> stream)
    {
        foreach (var unit in ReadUnits(stream))
        {
            if (unit.Type != SequenceHeader)
                continue;

            var bits = new BitReader(stream.Slice(unit.Offset + unit.HeaderLength, unit.PayloadLength));
            bits.Read(3); // seq_profile
            bits.Read(1); // still_picture
            if (bits.Read(1) == 1)
                return [0]; // reduced_still_picture_header

            if (bits.Read(1) == 1)
                throw new NotSupportedException("The sequence header has timing information.");

            var hasInitialDisplayDelay = bits.Read(1) == 1;
            var count = bits.Read(5) + 1;
            var result = new int[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = bits.Read(12);
                if (bits.Read(5) > 7)
                    bits.Read(1); // seq_tier

                if (hasInitialDisplayDelay && bits.Read(1) == 1)
                    bits.Read(4);
            }

            return result;
        }

        throw new InvalidDataException("The stream has no sequence header.");
    }

    /// <summary>A unit of a stream.</summary>
    /// <param name="Type">The type of the unit (<c>obu_type</c>).</param>
    /// <param name="Offset">The offset of the header of the unit in the stream.</param>
    /// <param name="HeaderLength">The length of the header: the header byte, the extension byte and the size field.</param>
    /// <param name="PayloadLength">The length of the payload.</param>
    /// <param name="HasExtension">Whether the unit has an extension, which names its layers.</param>
    /// <param name="TemporalId">The temporal layer of the unit; 0 without extension.</param>
    /// <param name="SpatialId">The spatial layer of the unit; 0 without extension.</param>
    public sealed record Unit(int Type, int Offset, int HeaderLength, int PayloadLength, bool HasExtension, int TemporalId, int SpatialId);

    private ref struct BitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public int Read(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                if (_position >> 3 >= _data.Length)
                    throw new InvalidDataException("The sequence header is truncated.");

                value = (value << 1) | ((_data[_position >> 3] >> (7 - (_position & 7))) & 1);
                _position++;
            }

            return value;
        }
    }
}
