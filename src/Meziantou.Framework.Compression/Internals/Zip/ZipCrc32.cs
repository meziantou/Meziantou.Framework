namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>The CRC-32 of zip, one byte at a time: the keys of ZipCrypto, and the CRC of the bytes before each position a data descriptor may start at.</summary>
internal static class ZipCrc32
{
    private static readonly uint[] Table = CreateTable();

    /// <summary>Updates a CRC register (not inverted) with a byte.</summary>
    public static uint Update(uint crc, byte value) => Table[(crc ^ value) & 0xFF] ^ (crc >> 8);

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var value = i;
            for (var j = 0; j < 8; j++)
            {
                value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
