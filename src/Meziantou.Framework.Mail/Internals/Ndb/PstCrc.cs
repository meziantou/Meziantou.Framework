namespace Meziantou.Framework.Mail.Internals.Ndb;

/// <summary>The CRC of [MS-PST] 5.3: the reflected CRC-32 table (polynomial 0xEDB88320), an initial value of 0 and no final XOR.</summary>
internal static class PstCrc
{
    private static readonly uint[] Table = CreateTable();

    public static uint Compute(ReadOnlySpan<byte> data, uint crc = 0)
    {
        var table = Table;
        foreach (var b in data)
        {
            crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ 0xEDB88320u : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
