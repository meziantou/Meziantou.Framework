namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The distinct partition patterns of a footprint and a partition count (section 23.20): the seeds whose partitions are
/// all used, one per pattern (two seeds can give the same texels, with the partitions numbered differently), and the
/// texels of each partition.
/// </summary>
internal sealed class AstcPartitionPatterns
{
    public AstcPartitionPatterns(int blockWidth, int blockHeight, int partitionCount)
    {
        var seeds = new List<int>();
        var masks = new List<ulong>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        var smallBlock = blockWidth * blockHeight < 31;
        Span<ulong> pattern = stackalloc ulong[4 * 3];
        Span<ulong> canonical = stackalloc ulong[4 * 3];
        Span<int> order = stackalloc int[4];
        for (var seed = 0; seed < 1024; seed++)
        {
            pattern.Clear();
            canonical.Clear();
            order.Fill(-1);
            var used = 0;
            for (var y = 0; y < blockHeight; y++)
            {
                for (var x = 0; x < blockWidth; x++)
                {
                    var texel = x + (blockWidth * y);
                    var partition = AstcBlockDecoder.SelectPartition(seed, x, y, 0, partitionCount, smallBlock);
                    pattern[(partition * 3) + (texel >> 6)] |= 1UL << (texel & 63);

                    // The partitions numbered by their first texel identify the pattern
                    if (order[partition] < 0)
                    {
                        order[partition] = used++;
                    }

                    canonical[(order[partition] * 3) + (texel >> 6)] |= 1UL << (texel & 63);
                }
            }

            if (used < partitionCount || !known.Add(string.Join(',', canonical[..(partitionCount * 3)].ToArray())))
                continue;

            seeds.Add(seed);
            masks.AddRange(pattern[..(partitionCount * 3)]);
        }

        Seeds = [.. seeds];
        Masks = [.. masks];
    }

    /// <summary>Gets the seed (partition index) of each pattern.</summary>
    public int[] Seeds { get; }

    /// <summary>Gets the texels of each partition of each pattern: 3 ulongs per partition (texel t at bit t % 64 of ulong t / 64).</summary>
    public ulong[] Masks { get; }
}
