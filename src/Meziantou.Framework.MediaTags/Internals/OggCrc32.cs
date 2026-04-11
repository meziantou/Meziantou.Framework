namespace Meziantou.Framework.MediaTags.Internals;

/// <summary>
/// CRC-32 implementation for OGG page checksums.
/// Uses the polynomial 0x04C11DB7 with direct (non-reflected) calculation.
/// </summary>
internal static class OggCrc32
{
    private static readonly uint[] Table = GenerateTable();

    private static uint[] GenerateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var crc = i << 24;
            for (var j = 0; j < 8; j++)
            {
                crc = (crc & 0x80000000) != 0
                    ? (crc << 1) ^ 0x04C11DB7
                    : crc << 1;
            }
            table[i] = crc;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0u;
        foreach (var b in data)
        {
            crc = (crc << 8) ^ Table[(crc >> 24) ^ b];
        }
        return crc;
    }

    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc = (crc << 8) ^ Table[(crc >> 24) ^ b];
        }
        return crc;
    }
}
