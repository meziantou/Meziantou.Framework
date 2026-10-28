namespace Meziantou.Framework.Hashing;

/// <summary>
/// Computes the CRC-32 of the pages of Ogg streams (RFC 3533): the polynomial 0x04C11DB7, not reflected, with an initial
/// value of 0 and no final XOR.
/// </summary>
/// <remarks>This is not the CRC-32 of zlib, gzip, ZIP or PNG: use <see cref="Crc32"/> for those.</remarks>
public static class OggCrc32
{
    private static readonly uint[] Table = GenerateTable();

    /// <summary>Computes the Ogg CRC-32 of data.</summary>
    /// <param name="data">The data.</param>
    /// <returns>The Ogg CRC-32 of <paramref name="data"/>; 0 when it is empty.</returns>
    public static uint Compute(ReadOnlySpan<byte> data) => Append(0, data);

    /// <summary>Computes the Ogg CRC-32 of data that follows other data, from the CRC-32 of the other data.</summary>
    /// <param name="crc">The Ogg CRC-32 of the preceding data: 0 when there is none.</param>
    /// <param name="data">The data that follows.</param>
    /// <returns>The Ogg CRC-32 of the preceding data followed by <paramref name="data"/>.</returns>
    /// <remarks><c>Append(Compute(a), b)</c> is <c>Compute(a + b)</c>: use it to compute the CRC-32 of data received in several parts.</remarks>
    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        var table = Table;
        foreach (var b in data)
        {
            crc = (crc << 8) ^ table[(crc >> 24) ^ b];
        }

        return crc;
    }

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
}
