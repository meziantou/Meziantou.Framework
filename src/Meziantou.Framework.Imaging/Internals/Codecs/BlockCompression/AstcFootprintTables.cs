namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The tables of the ASTC encoder that depend on the footprint: the weight grids that fit in a block for each number of
/// endpoint integers (section 23.10 and the rules of section 23.23), and the texels of each partition of the 1024
/// partition patterns (section 23.20). Each footprint computes them once, on first use: they never change afterward, and
/// <see cref="Lazy{T}"/> makes the first use thread-safe.
/// </summary>
internal sealed class AstcFootprintTables
{
    /// <summary>The two-dimensional footprints of ASTC (section 23.2).</summary>
    public static readonly (int Width, int Height)[] Footprints =
    [
        (4, 4), (5, 4), (5, 5), (6, 5), (6, 6), (8, 5), (8, 6), (8, 8), (10, 5), (10, 6), (10, 8), (10, 10), (12, 10), (12, 12),
    ];

    private static readonly Lazy<AstcFootprintTables>[] Tables = CreateTables();
    private static readonly Lazy<AstcPartitionPatterns>[] TwoPartitionPatterns = CreatePartitionPatterns(2);
    private static readonly Lazy<AstcPartitionPatterns>[] ThreePartitionPatterns = CreatePartitionPatterns(3);

    // The configurations of each (one or several partitions, one or two planes, number of endpoint integers / 2)
    private readonly AstcGridConfig[][] _configs = new AstcGridConfig[2 * 2 * 10][];

    // The infill of each weight grid of the configurations, by (width - 2) * 11 + height - 2
    private readonly AstcWeightGrid?[] _grids = new AstcWeightGrid?[11 * 11];

    private AstcFootprintTables(int blockWidth, int blockHeight)
    {
        for (var multiple = 0; multiple < 2; multiple++)
        {
            for (var dual = 0; dual < 2; dual++)
            {
                for (var values = 2; values <= 18; values += 2)
                {
                    var configs = Enumerate(blockWidth, blockHeight, multiple != 0, dual != 0, values);
                    _configs[ConfigIndex(multiple != 0, dual != 0, values)] = configs;
                    foreach (var config in configs)
                    {
                        _grids[GridIndex(config.GridWidth, config.GridHeight)] ??= new AstcWeightGrid(blockWidth, blockHeight, config.GridWidth, config.GridHeight);
                    }
                }
            }
        }
    }

    /// <summary>Gets the index of a footprint in <see cref="Footprints"/>, or -1.</summary>
    public static int GetFootprintIndex(int blockWidth, int blockHeight) => Array.IndexOf(Footprints, (blockWidth, blockHeight));

    /// <summary>Gets the tables of a footprint.</summary>
    public static AstcFootprintTables Get(int footprintIndex) => Tables[footprintIndex].Value;

    /// <summary>Gets the distinct partition patterns of a footprint for two or three partitions.</summary>
    public static AstcPartitionPatterns GetPartitionPatterns(int footprintIndex, int partitionCount)
        => partitionCount == 2 ? TwoPartitionPatterns[footprintIndex].Value : ThreePartitionPatterns[footprintIndex].Value;

    /// <summary>
    /// Gets the weight grids that leave enough bits for the color endpoints, each with the largest color range that fits
    /// (section 23.13: the decoder selects it the same way). A configuration is left out when another one is at least as
    /// large in both dimensions of the grid, the weight range and the color range: it cannot be worse.
    /// </summary>
    public ReadOnlySpan<AstcGridConfig> GetConfigs(bool multiplePartitions, bool dualPlane, int colorValueCount) => _configs[ConfigIndex(multiplePartitions, dualPlane, colorValueCount)];

    /// <summary>Gets the infill of the weight grid of a configuration of <see cref="GetConfigs"/>.</summary>
    public AstcWeightGrid GetGrid(AstcGridConfig config) => _grids[GridIndex(config.GridWidth, config.GridHeight)]!;

    private static int GridIndex(int width, int height) => ((width - 2) * 11) + height - 2;

    private static int ConfigIndex(bool multiple, bool dual, int values) => (((multiple ? 2 : 0) + (dual ? 1 : 0)) * 10) + (values / 2);

    private static AstcGridConfig[] Enumerate(int blockWidth, int blockHeight, bool multiple, bool dual, int values)
    {
        var all = new List<AstcGridConfig>();
        var configBits = (multiple ? 29 : 17) + (dual ? 2 : 0);
        for (var gridWidth = 2; gridWidth <= blockWidth; gridWidth++)
        {
            for (var gridHeight = 2; gridHeight <= blockHeight; gridHeight++)
            {
                var weightCount = gridWidth * gridHeight * (dual ? 2 : 1);
                if (weightCount > AstcBlockDecoder.MaxWeights)
                    continue;

                // A grid whose aspect ratio is more than twice as far from the footprint's resolves one direction much
                // more finely than the other: such grids rarely win, and leaving them out keeps the search short
                if (gridWidth * blockHeight > 2 * gridHeight * blockWidth || gridHeight * blockWidth > 2 * gridWidth * blockHeight)
                    continue;

                for (var range = 0; range < AstcEncodingTables.WeightRangeCount; range++)
                {
                    var mode = AstcEncodingTables.GetBlockMode(gridWidth, gridHeight, dual, range);
                    if (mode < 0)
                        continue;

                    var (trits, quints, bits) = AstcEncodingTables.WeightRanges[range];
                    var weightBits = AstcBlockDecoder.GetSequenceBits(weightCount, trits, quints, bits);
                    if (weightBits is < 24 or > 96)
                        continue;

                    var remaining = 128 - configBits - weightBits;
                    var colorRange = -1;
                    for (var i = 0; i < AstcBlockDecoder.ColorRanges.Length; i++)
                    {
                        var color = AstcBlockDecoder.ColorRanges[i];
                        if (AstcBlockDecoder.GetSequenceBits(values, color.Trits, color.Quints, color.Bits) <= remaining)
                        {
                            colorRange = i;
                            break;
                        }
                    }

                    // The decoder also requires ceil(13 * values / 5) bits, which the smallest range (6 levels) needs
                    if (colorRange < 0 || remaining < ((13 * values) + 4) / 5)
                        continue;

                    all.Add(new AstcGridConfig(gridWidth, gridHeight, range, colorRange, mode));
                }
            }
        }

        var result = new List<AstcGridConfig>();
        foreach (var config in all)
        {
            var dominated = false;
            foreach (var other in all)
            {
                if (other != config && other.WeightRange >= config.WeightRange && other.GridWidth >= config.GridWidth && other.GridHeight >= config.GridHeight && other.ColorRange <= config.ColorRange)
                {
                    dominated = true;
                    break;
                }
            }

            if (!dominated)
            {
                result.Add(config);
            }
        }

        return [.. result];
    }

    private static Lazy<AstcFootprintTables>[] CreateTables()
    {
        var result = new Lazy<AstcFootprintTables>[Footprints.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var (width, height) = Footprints[i];
            result[i] = new Lazy<AstcFootprintTables>(() => new AstcFootprintTables(width, height), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        return result;
    }

    private static Lazy<AstcPartitionPatterns>[] CreatePartitionPatterns(int partitionCount)
    {
        var result = new Lazy<AstcPartitionPatterns>[Footprints.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var (width, height) = Footprints[i];
            result[i] = new Lazy<AstcPartitionPatterns>(() => new AstcPartitionPatterns(width, height, partitionCount), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        return result;
    }
}
