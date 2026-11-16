namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>Reads the bits of the code of a RAR 2.9 filter, most significant bit first. Reading past the end returns zeros and sets <see cref="IsOverrun"/>.</summary>
internal ref struct RarMemoryBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _bitPosition;

    public RarMemoryBitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
    }

    public readonly bool IsOverrun => _bitPosition > _data.Length * 8;

    public uint ReadBits(int count)
    {
        uint value = 0;
        for (var i = 0; i < count; i++)
        {
            var index = _bitPosition >> 3;
            var bit = index < _data.Length ? (_data[index] >> (7 - (_bitPosition & 7))) & 1 : 0;
            value = (value << 1) | (uint)bit;
            _bitPosition++;
        }

        return value;
    }

    /// <summary>Reads a number of the RarVM encoding: 2 bits that select 4, 8, 16 or 32 bits.</summary>
    public uint ReadNumber()
    {
        switch (ReadBits(2))
        {
            case 0:
                return ReadBits(4);

            case 1:
                var value = ReadBits(8);
                if (value >= 16)
                    return value;

                return 0xFFFFFF00 | (value << 4) | ReadBits(4);

            case 2:
                return ReadBits(16);

            default:
                return ReadBits(32);
        }
    }
}
