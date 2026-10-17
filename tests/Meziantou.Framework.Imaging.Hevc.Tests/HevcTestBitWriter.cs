namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Writes the syntax elements of a parameter set bit by bit, for the tests that need a NAL unit no encoder writes (Rec.
/// ITU-T H.265, clauses 7.2 and 9.2). It shares no code with the library.
/// </summary>
internal sealed class HevcTestBitWriter
{
    private readonly List<bool> _bits = [];

    public HevcTestBitWriter U(int count, ulong value)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            _bits.Add(((value >> i) & 1) is not 0);
        }

        return this;
    }

    public HevcTestBitWriter Flag(bool value) => U(1, value ? 1UL : 0UL);

    // Clause 9.2: codeNum + 1 written with as many leading zero bits as it has bits after its first one
    public HevcTestBitWriter UE(uint codeNum)
    {
        var value = (ulong)codeNum + 1;
        var length = 64 - (int)ulong.LeadingZeroCount(value);
        U(length - 1, 0);
        return U(length, value);
    }

    public HevcTestBitWriter SE(int value) => UE(value > 0 ? (uint)((2 * value) - 1) : (uint)(-2 * (long)value));

    /// <summary>Gets the NAL unit: the header, then the payload with its trailing bits and its emulation prevention bytes.</summary>
    public byte[] ToNalUnit(int nalUnitType, int layerId = 0, bool trailingBits = true)
    {
        var bits = new List<bool>(_bits);
        if (trailingBits)
        {
            bits.Add(true);
        }

        while (bits.Count % 8 is not 0)
        {
            bits.Add(false);
        }

        var nal = new List<byte> { (byte)((nalUnitType << 1) | (layerId >> 5)), (byte)(((layerId & 31) << 3) | 1) };
        var zeros = 0;
        for (var i = 0; i < bits.Count; i += 8)
        {
            var value = 0;
            for (var j = 0; j < 8; j++)
            {
                value = (value << 1) | (bits[i + j] ? 1 : 0);
            }

            if (zeros >= 2 && value <= 3)
            {
                nal.Add(3);
                zeros = 0;
            }

            zeros = value is 0 ? zeros + 1 : 0;
            nal.Add((byte)value);
        }

        return [.. nal];
    }
}
