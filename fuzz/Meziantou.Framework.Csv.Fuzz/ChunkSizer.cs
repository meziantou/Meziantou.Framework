namespace Meziantou.Framework.Csv.Fuzz;

/// <summary>
/// Gives the number of bytes each read returns, from an option byte: 0 returns everything at once, any other value returns
/// between 1 and <c>(value &amp; 0x0F) + 1</c> bytes, in a sequence that the other bits of the value vary.
/// </summary>
internal sealed class ChunkSizer(byte seed)
{
    private uint _state = (seed * 2654435761u) | 1;

    public int Next()
    {
        if (seed is 0)
            return int.MaxValue;

        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return 1 + (int)(_state % (uint)((seed & 0x0F) + 1));
    }
}
