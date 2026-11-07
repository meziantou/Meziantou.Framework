using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The CAP marker segment (T.800, A.5.2): the parts of ISO/IEC 15444 whose capabilities the codestream needs, and one
/// 16-bit field for each. The field of Part 15 (T.814, A.2) describes the use of the high-throughput block coder.
/// </summary>
internal sealed class Jpeg2000Capabilities
{
    private const uint Part15Bit = 1u << (32 - 15);

    private Jpeg2000Capabilities(uint parts, int highThroughput)
    {
        Parts = parts;
        HighThroughput = highThroughput;
    }

    /// <summary>Gets the Pcap field: the most significant bit stands for Part 1, the next one for Part 2, and so on.</summary>
    public uint Parts { get; }

    /// <summary>Gets the Ccap field of Part 15, or -1 when the codestream does not use it.</summary>
    public int HighThroughput { get; }

    /// <summary>Gets a value indicating whether the codestream needs capabilities of a part other than Part 15.</summary>
    public bool HasOtherParts => (Parts & ~Part15Bit) != 0;

    /// <summary>Parses the content of a CAP marker segment.</summary>
    public static Jpeg2000Capabilities Read(ReadOnlySpan<byte> content)
    {
        if (content.Length < 4)
            throw Jpeg2000Format.Invalid("the CAP marker segment is too short.");

        var parts = BinaryPrimitives.ReadUInt32BigEndian(content);
        if (content.Length != 4 + (2 * System.Numerics.BitOperations.PopCount(parts)))
            throw Jpeg2000Format.Invalid("the length of the CAP marker segment is not the one of the parts it names.");

        // The Ccap fields follow in the order of the bits of Pcap, from the most significant one
        var highThroughput = -1;
        var offset = 4;
        for (var bit = 31; bit >= 0; bit--)
        {
            var mask = 1u << bit;
            if ((parts & mask) == 0)
                continue;

            if (mask == Part15Bit)
            {
                highThroughput = BinaryPrimitives.ReadUInt16BigEndian(content[offset..]);
            }

            offset += 2;
        }

        return new Jpeg2000Capabilities(parts, highThroughput);
    }
}
