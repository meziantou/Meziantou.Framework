namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// The permutation of the primary leads (high 16 bits of a primary) of a <c>[reorder]</c>. A reordering moves whole
/// script groups, so most blocks of 256 leads move by the same offset: they store the offset, and only the blocks
/// where a moved group starts or ends store their 256 leads (a few KB instead of a 128 KB table per reordering).
/// </summary>
internal sealed class CollationReorderTable
{
    private const int BlockShift = 8;
    private const int BlockSize = 1 << BlockShift;
    private const int BlockCount = 0x10000 >> BlockShift;

    private readonly int[] _offsets = new int[BlockCount];
    private readonly ushort[]?[] _blocks = new ushort[]?[BlockCount];

    /// <summary>Creates the table of a permutation of the 65536 leads.</summary>
    public CollationReorderTable(ReadOnlySpan<ushort> leads)
    {
        if (leads.Length != 0x10000)
            throw new ArgumentException("The table must have an entry per lead", nameof(leads));

        for (var block = 0; block < BlockCount; block++)
        {
            var start = block << BlockShift;
            var values = leads.Slice(start, BlockSize);
            var offset = values[0] - start;
            var isUniform = true;
            for (var i = 1; i < values.Length; i++)
            {
                if (values[i] - (start + i) != offset)
                {
                    isUniform = false;
                    break;
                }
            }

            if (isUniform)
            {
                _offsets[block] = offset;
            }
            else
            {
                _blocks[block] = values.ToArray();
            }
        }
    }

    /// <summary>Gets the new lead of a lead.</summary>
    public ushort this[uint lead]
    {
        get
        {
            var block = (int)(lead >> BlockShift);
            var values = _blocks[block];
            return values is not null ? values[lead & (BlockSize - 1)] : (ushort)(lead + (uint)_offsets[block]);
        }
    }
}
