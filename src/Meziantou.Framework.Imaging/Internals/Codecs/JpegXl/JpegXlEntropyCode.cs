using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The description of an entropy-coded stream (ISO/IEC 18181-1, section D.3): the LZ77 parameters, the map from contexts
/// to clusters, and for each cluster a hybrid integer configuration and either a prefix code or an ANS distribution.
/// A <see cref="JpegXlEntropyDecoder"/> reads the symbols of a stream that uses it.
/// </summary>
internal sealed class JpegXlEntropyCode : IDisposable
{
    /// <summary>The largest number of clusters.</summary>
    public const int MaxClusters = 256;

    private readonly PooledBuffer _contextMap;
    private readonly JpegXlHybridUintConfig[] _configs;
    private readonly Av1PooledArray<int>?[]? _prefixTables;
    private readonly byte[]? _prefixLengths;
    private readonly PooledBuffer? _aliasTables;
    private readonly int _aliasTableLength;

    private JpegXlEntropyCode(
        int contextCount,
        PooledBuffer contextMap,
        bool lz77Enabled,
        uint lz77MinSymbol,
        uint lz77MinLength,
        JpegXlHybridUintConfig lz77LengthConfig,
        JpegXlHybridUintConfig[] configs,
        int logAlphabetSize,
        Av1PooledArray<int>?[]? prefixTables,
        byte[]? prefixLengths,
        PooledBuffer? aliasTables,
        int aliasTableLength)
    {
        ContextCount = contextCount;
        _contextMap = contextMap;
        IsLz77Enabled = lz77Enabled;
        Lz77MinSymbol = lz77MinSymbol;
        Lz77MinLength = lz77MinLength;
        Lz77LengthConfig = lz77LengthConfig;
        _configs = configs;
        LogAlphabetSize = logAlphabetSize;
        _prefixTables = prefixTables;
        _prefixLengths = prefixLengths;
        _aliasTables = aliasTables;
        _aliasTableLength = aliasTableLength;
    }

    /// <summary>Gets the number of contexts of the stream, without the context of the LZ77 distances.</summary>
    public int ContextCount { get; }

    /// <summary>Gets a value indicating whether tokens above <see cref="Lz77MinSymbol"/> copy previous values.</summary>
    public bool IsLz77Enabled { get; }

    /// <summary>Gets the first token that is a copy.</summary>
    public uint Lz77MinSymbol { get; }

    /// <summary>Gets the length of the shortest copy.</summary>
    public uint Lz77MinLength { get; }

    /// <summary>Gets the configuration of the length of a copy.</summary>
    public JpegXlHybridUintConfig Lz77LengthConfig { get; }

    /// <summary>Gets a value indicating whether the clusters use prefix codes; otherwise ANS distributions.</summary>
    public bool UsesPrefixCodes => _prefixTables is not null;

    /// <summary>Gets the base-2 logarithm of the alphabet size of the ANS distributions.</summary>
    public int LogAlphabetSize { get; }

    /// <summary>Gets the cluster of each context; the last entry is the cluster of the LZ77 distances when LZ77 is enabled.</summary>
    public ReadOnlySpan<byte> ContextMap => _contextMap.RawBuffer.AsSpan(0, ContextCount + (IsLz77Enabled ? 1 : 0));

    /// <summary>Gets the hybrid integer configuration of each cluster.</summary>
    public ReadOnlySpan<JpegXlHybridUintConfig> Configs => _configs;

    /// <summary>Gets the alias tables of the clusters, one after the other.</summary>
    public ReadOnlySpan<JpegXlAnsDistribution.AliasEntry> AliasTables => _aliasTables is null ? default : GetAliasTables(_aliasTables.RawBuffer, _aliasTableLength);

    /// <summary>Gets the buffer that holds <see cref="ContextMap"/> from its start, for a reader that caches it.</summary>
    internal byte[] ContextMapBuffer => _contextMap.RawBuffer;

    /// <summary>Gets the array behind <see cref="Configs"/>, for a reader that caches it.</summary>
    internal JpegXlHybridUintConfig[] ConfigArray => _configs;

    /// <summary>Gets the buffer that holds <see cref="AliasTables"/> from its start, or <see langword="null"/> for prefix codes.</summary>
    internal byte[]? AliasTableBuffer => _aliasTables?.RawBuffer;

    /// <summary>Gets the number of entries of <see cref="AliasTables"/>.</summary>
    internal int AliasTableLength => _aliasTableLength;

    /// <summary>Gets the alias tables held by a buffer that <see cref="AliasTableBuffer"/> returned.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static ReadOnlySpan<JpegXlAnsDistribution.AliasEntry> GetAliasTables(byte[] buffer, int length)
        => unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, JpegXlAnsDistribution.AliasEntry>(buffer.AsSpan()))[..length];

    /// <summary>Gets the lookup table of the prefix code of a cluster, or an empty span when its alphabet has one symbol.</summary>
    public ReadOnlySpan<int> GetPrefixTable(int cluster, out int maxLength)
    {
        maxLength = _prefixLengths![cluster];
        return _prefixTables![cluster] is { } table ? table.Span : default;
    }

    /// <summary>Reads the description of a stream of <paramref name="contextCount"/> contexts.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="contextCount">The number of contexts.</param>
    /// <param name="scope">The scope the tables are rented from.</param>
    /// <param name="allowLz77"><see langword="false"/> where the format forbids LZ77 (the stream that codes a small context map).</param>
    /// <returns>The description; it must be disposed.</returns>
    public static JpegXlEntropyCode Read(JpegXlBitReader reader, int contextCount, AllocationScope scope, bool allowLz77 = true)
    {
        var lz77Enabled = reader.ReadBool();
        uint lz77MinSymbol = 0;
        uint lz77MinLength = 0;
        JpegXlHybridUintConfig lz77LengthConfig = default;
        var mapLength = contextCount;
        if (lz77Enabled)
        {
            if (!allowLz77)
                throw JpegXlFormat.Invalid("LZ77 is used where the format forbids it.");

            lz77MinSymbol = reader.ReadU32(224, 0, 512, 0, 4096, 0, 8, 15);
            lz77MinLength = reader.ReadU32(3, 0, 4, 0, 5, 2, 9, 8);
            lz77LengthConfig = JpegXlHybridUintConfig.Read(reader, 8);
            mapLength++;
        }

        var contextMap = scope.Rent(mapLength, AllocationKind.DecoderState, clear: true);
        Av1PooledArray<int>?[]? prefixTables = null;
        PooledBuffer? aliasTables = null;
        var aliasTableLength = 0;
        try
        {
            var clusterCount = mapLength > 1 ? ReadContextMap(reader, contextMap.RawBuffer.AsSpan(0, mapLength), scope) : 1;
            var usePrefixCodes = reader.ReadBool();
            var logAlphabetSize = usePrefixCodes ? JpegXlPrefixCode.MaxLength : 5 + (int)reader.ReadBits(2);
            var configs = new JpegXlHybridUintConfig[clusterCount];
            for (var i = 0; i < configs.Length; i++)
            {
                configs[i] = JpegXlHybridUintConfig.Read(reader, logAlphabetSize);
            }

            byte[]? prefixLengths = null;
            if (usePrefixCodes)
            {
                Span<int> alphabetSizes = stackalloc int[clusterCount];
                for (var i = 0; i < clusterCount; i++)
                {
                    alphabetSizes[i] = ReadVarLenUint16(reader) + 1;
                    if (alphabetSizes[i] > 1 << JpegXlPrefixCode.MaxLength)
                        throw JpegXlFormat.Invalid("the alphabet of a prefix code is too large.");
                }

                prefixTables = new Av1PooledArray<int>?[clusterCount];
                prefixLengths = new byte[clusterCount];
                for (var i = 0; i < clusterCount; i++)
                {
                    if (alphabetSizes[i] > 1)
                    {
                        prefixTables[i] = JpegXlPrefixCode.Read(reader, alphabetSizes[i], scope, out var maxLength);
                        prefixLengths[i] = (byte)maxLength;
                    }

                    // The reads of a description are bounded by its declared sizes, not by the data
                    if (reader.IsOverrun)
                        throw JpegXlFormat.Invalid("the description of an entropy-coded stream is truncated.");
                }
            }
            else
            {
                var tableSize = 1 << logAlphabetSize;
                // At most 256 clusters of 256 entries of 10 bytes: the size cannot overflow
                aliasTableLength = clusterCount * tableSize;
                aliasTables = scope.Rent(aliasTableLength * Unsafe.SizeOf<JpegXlAnsDistribution.AliasEntry>(), AllocationKind.DecoderState, clear: false);
                var entries = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, JpegXlAnsDistribution.AliasEntry>(aliasTables.RawBuffer.AsSpan()))[..aliasTableLength];
                Span<int> distribution = stackalloc int[tableSize];
                for (var i = 0; i < clusterCount; i++)
                {
                    JpegXlAnsDistribution.Read(reader, distribution);
                    JpegXlAnsDistribution.BuildAliasTable(distribution, entries.Slice(i * tableSize, tableSize));
                    if (reader.IsOverrun)
                        throw JpegXlFormat.Invalid("the description of an entropy-coded stream is truncated.");
                }
            }

            return new JpegXlEntropyCode(contextCount, contextMap, lz77Enabled, lz77MinSymbol, lz77MinLength, lz77LengthConfig, configs, logAlphabetSize, prefixTables, prefixLengths, aliasTables, aliasTableLength);
        }
        catch
        {
            contextMap.Dispose();
            aliasTables?.Dispose();
            DisposeTables(prefixTables);
            throw;
        }
    }

    public void Dispose()
    {
        _contextMap.Dispose();
        _aliasTables?.Dispose();
        DisposeTables(_prefixTables);
    }

    private static void DisposeTables(Av1PooledArray<int>?[]? tables)
    {
        if (tables is null)
            return;

        foreach (var table in tables)
        {
            table?.Dispose();
        }
    }

    /// <summary>Reads the cluster of each context (section D.3.5) and returns the number of clusters.</summary>
    public static int ReadContextMap(JpegXlBitReader reader, Span<byte> map, AllocationScope scope)
    {
        if (reader.ReadBool())
        {
            var bitsPerEntry = (int)reader.ReadBits(2);
            for (var i = 0; i < map.Length; i++)
            {
                map[i] = (byte)reader.ReadBits(bitsPerEntry);
            }
        }
        else
        {
            var useMoveToFront = reader.ReadBool();
            using var code = Read(reader, contextCount: 1, scope, allowLz77: map.Length > 2);
            using var decoder = new JpegXlEntropyDecoder(code, scope);
            decoder.Begin(reader);
            for (var i = 0; i < map.Length; i++)
            {
                var value = decoder.Read(reader, 0);
                if (value >= MaxClusters)
                    throw JpegXlFormat.Invalid("a context map names a cluster above 255.");

                map[i] = (byte)value;
                if ((i & 0xFF) == 0xFF && reader.IsOverrun)
                    throw JpegXlFormat.Invalid("a context map is truncated.");
            }

            decoder.EnsureFinalState();
            if (useMoveToFront)
            {
                InverseMoveToFront(map);
            }
        }

        // Every cluster up to the largest one must be used
        Span<bool> used = stackalloc bool[MaxClusters];
        used.Clear();
        var max = 0;
        foreach (var cluster in map)
        {
            used[cluster] = true;
            max = Math.Max(max, cluster);
        }

        for (var i = 0; i <= max; i++)
        {
            if (!used[i])
                throw JpegXlFormat.Invalid("a context map skips a cluster.");
        }

        return max + 1;
    }

    private static void InverseMoveToFront(Span<byte> values)
    {
        Span<byte> order = stackalloc byte[256];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = (byte)i;
        }

        for (var i = 0; i < values.Length; i++)
        {
            var index = values[i];
            var value = order[index];
            values[i] = value;
            if (index != 0)
            {
                order[..index].CopyTo(order[1..]);
                order[0] = value;
            }
        }
    }

    private static int ReadVarLenUint16(JpegXlBitReader reader)
    {
        if (!reader.ReadBool())
            return 0;

        var count = (int)reader.ReadBits(4);
        return (int)reader.ReadBits(count) + (1 << count);
    }
}
