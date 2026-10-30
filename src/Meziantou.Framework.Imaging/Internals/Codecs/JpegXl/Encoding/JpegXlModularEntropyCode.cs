using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The entropy code of the samples of a learned Modular tree (ISO/IEC 18181-1, section D.3), shared by the sub-bitstreams
/// of a frame: the values are counted (<see cref="Counter"/>), the contexts are clustered, each cluster gets the hybrid
/// integer configuration that codes its values in the fewest bits, the code is written once, and each stream is written
/// with it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// A value is counted as a fine token: itself below 256; above, its logarithm, the two bits after its leading one and its two
/// lowest bits. The token of every configuration that is searched (split exponent of 8 at most, two bits at most on each
/// side) is a function of the fine token, so the configurations are compared without the values.
/// </description></item>
/// <item><description>
/// Clustering: the contexts are taken in order; each joins the cluster whose size grows the least, or starts a cluster when
/// that growth exceeds the estimated size of a new distribution; then each context moves to the cluster that codes it in the
/// fewest bits, twice. The order and the ties are fixed, so the result only depends on the counts.
/// </description></item>
/// <item><description>
/// Prefix codes or ANS: both are described, and the smaller code with its values is kept.
/// </description></item>
/// <item><description>
/// LZ77 (section D.3.7), when enabled: a copy is a token of the cluster of the context of its first value, which follows the
/// tokens of the values, then its distance, with the cluster of the extra context that follows the contexts of the stream.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class JpegXlModularEntropyCode : IDisposable
{
    /// <summary>The number of fine tokens.</summary>
    internal const int FineAlphabetSize = (1 << FineSplitExponent) + ((32 - FineSplitExponent) << (2 * FineBitCount));

    /// <summary>The first token of a copy when LZ77 is enabled: every token of a value is below it.</summary>
    internal const int Lz77MinSymbol = 224;

    /// <summary>The shortest copy.</summary>
    internal const int Lz77MinLength = 3;

    /// <summary>The number of tokens of the length of a copy (its configuration has no split).</summary>
    internal const int LengthTokenCount = 32;

    /// <summary>The configuration of the length of a copy: the logarithm in the token, every other bit raw.</summary>
    internal static JpegXlHybridUintConfig LengthConfig => new(0, 0, 0);

    private const int FineSplitExponent = 8;
    private const int FineBitCount = 2;
    private const int BucketCount = 1 << JpegXlEntropyEncoder.AnsLogAlphabetSize;
    private const int MaxPrefixAlphabetSize = 1 << JpegXlPrefixCode.MaxLength;

    /// <summary>The configurations that are compared for each cluster; the first one is the one the contexts are clustered with.</summary>
    private static readonly JpegXlHybridUintConfig[] Configs =
    [
        new(4, 1, 0),
        new(4, 2, 0),
        new(4, 1, 1),
        new(4, 2, 1),
        new(3, 1, 0),
        new(5, 1, 0),
        new(5, 2, 0),
        new(5, 2, 1),
        new(6, 2, 0),
        new(2, 0, 0),
        new(1, 0, 0),
        new(0, 0, 0),
        new(8, 2, 2),
    ];

    /// <summary>For each configuration, the token and the number of raw bits of each fine token.</summary>
    private static readonly (ushort Token, byte BitCount)[][] ConfigTokens = CreateConfigTokens();

    private readonly AllocationScope _scope;
    private readonly int _contextCount;
    private readonly int _maxClusters;
    private readonly bool _searchConfigs;
    private readonly bool _useLz77;
    private readonly Counter _counts;

    // The code
    private Av1PooledArray<byte>? _map;
    private int[] _configIndexes = [];
    private (ushort Token, byte BitCount)[][] _clusterTokens = [];
    private bool _useAns;
    private ushort[][] _codes = [];
    private byte[][] _lengths = [];
    private uint[][] _tokenHistograms = [];
    private Av1PooledArray<int>? _distributions;
    private Av1PooledArray<int>? _starts;
    private Av1PooledArray<ushort>? _positions;

    /// <summary>Starts the code of streams of <paramref name="contextCount"/> contexts.</summary>
    /// <param name="contextCount">The number of contexts, without the one of the LZ77 distances.</param>
    /// <param name="maxClusters">The largest number of clusters, from 1 to 256.</param>
    /// <param name="searchConfigs">Whether each cluster gets the configuration that suits its values; otherwise every cluster has the first one.</param>
    /// <param name="useLz77">Whether copies of previous values may be written.</param>
    /// <param name="scope">The scope of the working memory.</param>
    public JpegXlModularEntropyCode(int contextCount, int maxClusters, bool searchConfigs, bool useLz77, AllocationScope scope)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(contextCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxClusters, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxClusters, JpegXlEntropyCode.MaxClusters);
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
        _contextCount = contextCount;
        _maxClusters = maxClusters;
        _searchConfigs = searchConfigs;
        _useLz77 = useLz77;
        _counts = new Counter(this);
    }

    /// <summary>Gets the number of clusters, once the code is built.</summary>
    public int ClusterCount { get; private set; }

    /// <summary>Gets a value indicating whether the values are written with the ANS coder rather than prefix codes, once the code is built.</summary>
    public bool UsesAns => _useAns;

    /// <summary>Gets the estimated size in bits of the code and of the values it codes, once the code is built.</summary>
    public long EstimatedBits { get; private set; }

    /// <summary>Gets the fine token of a value.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static int GetFineToken(uint value)
    {
        if (value < 1u << FineSplitExponent)
            return (int)value;

        var log = BitOperations.Log2(value);
        var top = (int)((value >> (log - FineBitCount)) & ((1u << FineBitCount) - 1));
        var low = (int)(value & ((1u << FineBitCount) - 1));
        return (1 << FineSplitExponent) + ((log - FineSplitExponent) << (2 * FineBitCount)) + (top << FineBitCount) + low;
    }

    /// <summary>Gets the smallest value of a fine token.</summary>
    internal static uint GetFineValue(int fineToken)
    {
        if (fineToken < 1 << FineSplitExponent)
            return (uint)fineToken;

        var rest = fineToken - (1 << FineSplitExponent);
        var log = FineSplitExponent + (rest >> (2 * FineBitCount));
        var top = (uint)((rest >> FineBitCount) & ((1 << FineBitCount) - 1));
        var low = (uint)(rest & ((1 << FineBitCount) - 1));
        return (1u << log) | (top << (log - FineBitCount)) | low;
    }

    /// <summary>Gets the token of the length of a copy.</summary>
    internal static int GetLengthToken(int length) => JpegXlEntropyEncoder.GetToken(LengthConfig, (uint)(length - Lz77MinLength)).Token;

    /// <summary>Creates the counts of the values of some streams; <see cref="AddCounts"/> adds them to the code.</summary>
    public Counter CreateCounter() => new(this);

    /// <summary>Adds counts to the code.</summary>
    public void AddCounts(Counter counter)
    {
        ArgumentNullException.ThrowIfNull(counter);
        _counts.Add(counter);
    }

    /// <summary>Gets, for each context, the cost in bits of each token of a residual (hybrid integers 4, 1, 0) from the counts, before the code is built.</summary>
    public float[] GetTokenCosts() => JpegXlLz77Parser.GetTokenCosts(_counts, _contextCount);

    /// <summary>Clusters the contexts, chooses the configuration of each cluster and the coding, once every value is counted.</summary>
    public void Build()
    {
        // The contexts, the distances of the copies last, as histograms of the first configuration
        var mapLength = _contextCount + (_useLz77 ? 1 : 0);
        _map = Av1PooledArray<byte>.Rent(_scope, mapLength, clear: true);
        var map = _map.Span;
        var config0 = ConfigTokens[0];
        var coarseSize = Lz77MinSymbol + LengthTokenCount;
        var coarse = new uint[mapLength][];
        for (var context = 0; context < mapLength; context++)
        {
            var histogram = coarse[context] = new uint[coarseSize];
            var fine = _counts.GetFine(context);
            for (var token = 0; token < FineAlphabetSize; token++)
            {
                if (fine[token] != 0)
                {
                    histogram[config0[token].Token] += fine[token];
                }
            }

            var lengths = _counts.GetLengths(context);
            for (var token = 0; token < LengthTokenCount; token++)
            {
                histogram[Lz77MinSymbol + token] += lengths[token];
            }
        }

        var clusters = Cluster(coarse, map, _maxClusters);
        ClusterCount = clusters;

        // The fine counts of each cluster
        var fineClusters = new long[clusters][];
        var lengthClusters = new long[clusters][];
        for (var i = 0; i < clusters; i++)
        {
            fineClusters[i] = new long[FineAlphabetSize];
            lengthClusters[i] = new long[LengthTokenCount];
        }

        for (var context = 0; context < mapLength; context++)
        {
            var target = fineClusters[map[context]];
            var fine = _counts.GetFine(context);
            for (var token = 0; token < FineAlphabetSize; token++)
            {
                target[token] += fine[token];
            }

            var lengthTarget = lengthClusters[map[context]];
            var lengths = _counts.GetLengths(context);
            for (var token = 0; token < LengthTokenCount; token++)
            {
                lengthTarget[token] += lengths[token];
            }
        }

        // Each coding with the configurations that suit it, then the smaller one
        // The tokens of the first configuration are below 72: prefix codes always have a configuration for every cluster
        var prefix = ChooseConfigs(fineClusters, lengthClusters, ans: false)!;
        var ans = ChooseConfigs(fineClusters, lengthClusters, ans: true);
        long prefixBits;
        using (var description = new LsbBitWriter(_scope))
        {
            prefixBits = MeasurePrefix(description, fineClusters, lengthClusters, prefix);
        }

        var ansBits = long.MaxValue;
        if (ans is not null)
        {
            using var description = new LsbBitWriter(_scope);
            ansBits = MeasureAns(description, fineClusters, lengthClusters, ans);
        }

        _useAns = ansBits < prefixBits;
        EstimatedBits = Math.Min(ansBits, prefixBits);
        _configIndexes = _useAns ? ans! : prefix;
        _clusterTokens = new (ushort, byte)[clusters][];
        _tokenHistograms = new uint[clusters][];
        for (var i = 0; i < clusters; i++)
        {
            _clusterTokens[i] = ConfigTokens[_configIndexes[i]];
            _tokenHistograms[i] = GetTokenHistogram(fineClusters[i], lengthClusters[i], _configIndexes[i]);
        }

        if (_useAns)
        {
            _distributions = Av1PooledArray<int>.Rent(_scope, (long)clusters * BucketCount, clear: true);
            _starts = Av1PooledArray<int>.Rent(_scope, (long)clusters * BucketCount, clear: false);
            _positions = Av1PooledArray<ushort>.Rent(_scope, (long)clusters * JpegXlAnsDistribution.TableSize, clear: false);
            for (var i = 0; i < clusters; i++)
            {
                var distribution = _distributions.Span.Slice(i * BucketCount, BucketCount);
                JpegXlEntropyEncoder.Normalize(_tokenHistograms[i], distribution);
                JpegXlEntropyEncoder.BuildAnsEncodingTable(distribution, _starts.Span.Slice(i * BucketCount, BucketCount), _positions.Span.Slice(i * JpegXlAnsDistribution.TableSize, JpegXlAnsDistribution.TableSize));
            }
        }

        _counts.Dispose();
    }

    /// <summary>Writes the description of the code: LZ77, the context map, then the code of each cluster.</summary>
    public void WriteDescription(LsbBitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var mapLength = _contextCount + (_useLz77 ? 1 : 0);
        writer.WriteBool(_useLz77);
        if (_useLz77)
        {
            // The first token of a copy (224), the shortest copy (3), the configuration of the lengths
            writer.WriteBits(0, 2);
            writer.WriteBits(0, 2);
            JpegXlEntropyEncoder.WriteConfig(writer, LengthConfig, 8);
        }

        if (mapLength > 1)
        {
            JpegXlEntropyCodeBuilder.WriteContextMap(writer, _map!.Span[..mapLength], ClusterCount, _scope);
        }

        writer.WriteBool(!_useAns);
        var logAlphabetSize = JpegXlPrefixCode.MaxLength;
        if (_useAns)
        {
            logAlphabetSize = JpegXlEntropyEncoder.AnsLogAlphabetSize;
            writer.WriteBits(JpegXlEntropyEncoder.AnsLogAlphabetSize - 5, 2);
        }

        for (var i = 0; i < ClusterCount; i++)
        {
            JpegXlEntropyEncoder.WriteConfig(writer, Configs[_configIndexes[i]], logAlphabetSize);
        }

        if (_useAns)
        {
            for (var i = 0; i < ClusterCount; i++)
            {
                JpegXlEntropyEncoder.WriteDistribution(writer, _distributions!.Span.Slice(i * BucketCount, BucketCount));
            }

            return;
        }

        _codes = new ushort[ClusterCount][];
        _lengths = new byte[ClusterCount][];
        for (var i = 0; i < ClusterCount; i++)
        {
            var size = GetAlphabetSize(_tokenHistograms[i]);
            _lengths[i] = new byte[size];
            _codes[i] = new ushort[size];
            JpegXlEntropyEncoder.WriteVarLenUint16(writer, size - 1);
        }

        for (var i = 0; i < ClusterCount; i++)
        {
            if (_lengths[i].Length > 1)
            {
                JpegXlEntropyEncoder.WritePrefixCode(writer, _tokenHistograms[i].AsSpan(0, _lengths[i].Length), _lengths[i], _codes[i]);
            }
        }
    }

    /// <summary>Adds a value to a stream, once the code is built.</summary>
    public void Add(JpegXlEntropyCodeBuilder.StreamBuffer stream, int context, uint value)
    {
        var cluster = _map!.Span[context];
        var (token, bitCount) = _clusterTokens[cluster][GetFineToken(value)];
        Append(stream, ((ulong)cluster << 56) | ((ulong)token << 40) | ((ulong)bitCount << 32) | (GetRawBits(value, bitCount, Configs[_configIndexes[cluster]].LsbInToken)));
    }

    /// <summary>Adds a copy to a stream, once the code is built: its length with the cluster of <paramref name="context"/>, then its distance.</summary>
    public void AddCopy(JpegXlEntropyCodeBuilder.StreamBuffer stream, int context, int length, uint distance)
    {
        var cluster = _map!.Span[context];
        var (lengthToken, lengthBitCount, lengthBits) = JpegXlEntropyEncoder.GetToken(LengthConfig, (uint)(length - Lz77MinLength));
        Append(stream, ((ulong)cluster << 56) | ((ulong)(Lz77MinSymbol + lengthToken) << 40) | ((ulong)lengthBitCount << 32) | lengthBits);
        var distanceCluster = _map.Span[_contextCount];
        var (token, bitCount) = _clusterTokens[distanceCluster][GetFineToken(distance)];
        Append(stream, ((ulong)distanceCluster << 56) | ((ulong)token << 40) | ((ulong)bitCount << 32) | GetRawBits(distance, bitCount, Configs[_configIndexes[distanceCluster]].LsbInToken));
    }

    /// <summary>Writes the values of a stream, and empties it: the state of the ANS coder, if any, then the values.</summary>
    public void WriteStream(JpegXlEntropyCodeBuilder.StreamBuffer stream, LsbBitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(writer);
        var values = stream.Values is null ? default : stream.Values.Span[..stream.Count];
        stream.Count = 0;
        if (_useAns)
        {
            if (stream.Refills is null || stream.Refills.Length < values.Length)
            {
                stream.Refills?.Dispose();
                stream.Refills = null;
                stream.Refills = Av1PooledArray<int>.Rent(stream.Scope, Math.Max(4096, stream.Values?.Length ?? 0), clear: false);
            }

            JpegXlEntropyEncoder.WriteAnsStream(writer, values, _distributions!.Span, _starts!.Span, _positions!.Span, stream.Refills.Span);
            return;
        }

        foreach (var value in values)
        {
            var cluster = (int)(value >> 56);
            var token = (int)((value >> 40) & 0xFFFF);
            writer.WriteBits(_codes[cluster][token], _lengths[cluster][token]);
            writer.WriteBits((uint)value, (int)((value >> 32) & 0xFF));
        }
    }

    public void Dispose()
    {
        _counts.Dispose();
        _map?.Dispose();
        _map = null;
        _distributions?.Dispose();
        _distributions = null;
        _starts?.Dispose();
        _starts = null;
        _positions?.Dispose();
        _positions = null;
    }

    /// <summary>The raw bits of a value whose token keeps its <paramref name="lsb"/> lowest bits: the bits between them and the token bits above.</summary>
    private static uint GetRawBits(uint value, int bitCount, int lsb) => bitCount == 0 ? 0 : (uint)((value >> lsb) & ((1UL << bitCount) - 1));

    private static void Append(JpegXlEntropyCodeBuilder.StreamBuffer stream, ulong value)
    {
        if (stream.Values is null || stream.Count == stream.Values.Length)
        {
            var larger = Av1PooledArray<ulong>.Rent(stream.Scope, Math.Max(4096, (long)stream.Count * 2), clear: false);
            stream.Values?.Span[..stream.Count].CopyTo(larger.Span);
            stream.Values?.Dispose();
            stream.Values = larger;
        }

        stream.Values.Span[stream.Count++] = value;
    }

    private static int GetAlphabetSize(uint[] histogram)
    {
        var size = histogram.Length;
        while (size > 1 && histogram[size - 1] == 0)
        {
            size--;
        }

        return size;
    }

    private static (ushort Token, byte BitCount)[][] CreateConfigTokens()
    {
        var result = new (ushort, byte)[Configs.Length][];
        for (var c = 0; c < Configs.Length; c++)
        {
            var config = Configs[c];
            var tokens = result[c] = new (ushort, byte)[FineAlphabetSize];
            for (var fine = 0; fine < FineAlphabetSize; fine++)
            {
                // A value of the fine token: its bits that are not in the fine token are zeros
                var (token, bitCount, _) = JpegXlEntropyEncoder.GetToken(config, GetFineValue(fine));
                tokens[fine] = ((ushort)token, (byte)bitCount);
            }
        }

        return result;
    }

    /// <summary>
    /// Clusters histograms: in order, each joins the cluster whose size grows the least, or starts one when that growth
    /// exceeds the estimated size of a new distribution; then each moves to the cluster that codes it in the fewest bits.
    /// </summary>
    /// <returns>The number of clusters; every cluster below it is used.</returns>
    private static int Cluster(uint[][] histograms, Span<byte> map, int maxClusters)
    {
        var alphabetSize = histograms[0].Length;
        var clusters = new List<long[]>();
        var totals = new List<long>();
        var sums = new List<double>();
        var previous = 0;
        for (var context = 0; context < histograms.Length; context++)
        {
            var histogram = histograms[context];
            long total = 0;
            var sum = 0.0;
            var symbolCount = 0;
            foreach (var count in histogram)
            {
                if (count == 0)
                    continue;

                total += count;
                sum += count * Math.Log2(count);
                symbolCount++;
            }

            if (total == 0)
            {
                map[context] = (byte)previous;
                continue;
            }

            var cost = (total * Math.Log2(total)) - sum;
            var best = -1;
            var bestGrowth = double.MaxValue;
            for (var i = 0; i < clusters.Count; i++)
            {
                var cluster = clusters[i];
                var mergedSum = sums[i];
                for (var token = 0; token < alphabetSize; token++)
                {
                    if (histogram[token] == 0)
                        continue;

                    var merged = (double)cluster[token] + histogram[token];
                    mergedSum += merged * Math.Log2(merged);
                    if (cluster[token] != 0)
                    {
                        mergedSum -= cluster[token] * Math.Log2(cluster[token]);
                    }
                }

                var mergedTotal = totals[i] + total;
                var growth = (mergedTotal * Math.Log2(mergedTotal)) - mergedSum - ((totals[i] * Math.Log2(Math.Max(1, totals[i]))) - sums[i]) - cost;
                if (growth < bestGrowth)
                {
                    bestGrowth = growth;
                    best = i;
                }
            }

            var newClusterCost = 16.0 + (12.0 * symbolCount);
            if (best < 0 || (bestGrowth > newClusterCost && clusters.Count < maxClusters))
            {
                best = clusters.Count;
                clusters.Add(new long[alphabetSize]);
                totals.Add(0);
                sums.Add(0);
            }

            var target = clusters[best];
            var targetSum = 0.0;
            for (var token = 0; token < alphabetSize; token++)
            {
                target[token] += histogram[token];
                if (target[token] != 0)
                {
                    targetSum += target[token] * Math.Log2(target[token]);
                }
            }

            totals[best] += total;
            sums[best] = targetSum;
            map[context] = (byte)best;
            previous = best;
        }

        if (clusters.Count <= 1)
            return 1;

        // Each context moves to the cluster whose distribution codes it in the fewest bits; a token that a cluster does not
        // have costs as much as a token seen once in twice its values
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var logs = new double[clusters.Count][];
            for (var i = 0; i < clusters.Count; i++)
            {
                var cluster = clusters[i];
                long total = 0;
                foreach (var count in cluster)
                {
                    total += count;
                }

                var log = logs[i] = new double[alphabetSize];
                var missing = Math.Log2(2.0 * Math.Max(1, total));
                for (var token = 0; token < alphabetSize; token++)
                {
                    log[token] = cluster[token] == 0 ? missing : Math.Log2((double)total / cluster[token]);
                }
            }

            var changed = false;
            var next = new List<long[]>();
            for (var i = 0; i < clusters.Count; i++)
            {
                next.Add(new long[alphabetSize]);
            }

            for (var context = 0; context < histograms.Length; context++)
            {
                var histogram = histograms[context];
                var best = (int)map[context];
                var bestCost = double.MaxValue;
                var any = false;
                for (var i = 0; i < clusters.Count; i++)
                {
                    var cost = 0.0;
                    var log = logs[i];
                    for (var token = 0; token < alphabetSize; token++)
                    {
                        if (histogram[token] != 0)
                        {
                            cost += histogram[token] * log[token];
                            any = true;
                        }
                    }

                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = i;
                    }
                }

                if (!any)
                    continue;

                changed |= best != map[context];
                map[context] = (byte)best;
                var target = next[best];
                for (var token = 0; token < alphabetSize; token++)
                {
                    target[token] += histogram[token];
                }
            }

            if (!changed)
                break;

            clusters = next;
        }

        // A context without value has the cluster of the context before it, which costs nothing in the context map; the
        // clusters that lost every context are removed, the others keep their order
        for (var context = 0; context < histograms.Length; context++)
        {
            if (IsEmpty(histograms[context]))
            {
                map[context] = context == 0 ? (byte)0 : map[context - 1];
            }
        }

        Span<int> renumber = stackalloc int[clusters.Count];
        renumber.Fill(-1);
        var used = 0;
        for (var context = 0; context < histograms.Length; context++)
        {
            var cluster = map[context];
            if (renumber[cluster] < 0)
            {
                renumber[cluster] = used++;
            }

            map[context] = (byte)renumber[cluster];
        }

        return used;
    }

    private static bool IsEmpty(uint[] histogram)
    {
        foreach (var count in histogram)
        {
            if (count != 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Chooses the configuration of each cluster for a coding: the one whose tokens and raw bits are the fewest. ANS codes
    /// tokens below 256 only, and the tokens of the values stay below the first token of a copy when LZ77 is enabled.
    /// </summary>
    /// <returns>The index of the configuration of each cluster, or <see langword="null"/> when no configuration fits a cluster.</returns>
    private int[]? ChooseConfigs(long[][] fineClusters, long[][] lengthClusters, bool ans)
    {
        var limit = _useLz77 ? Lz77MinSymbol : ans ? BucketCount : MaxPrefixAlphabetSize;
        var result = new int[fineClusters.Length];
        // The largest token of a fine token is below 1024 for every configuration
        Span<long> tokens = stackalloc long[1024];
        for (var cluster = 0; cluster < fineClusters.Length; cluster++)
        {
            var fine = fineClusters[cluster];
            // The copies are counted in the values of the cluster, the same for every configuration
            long lengthCount = 0;
            foreach (var count in lengthClusters[cluster])
            {
                lengthCount += count;
            }

            var bestCost = double.MaxValue;
            var best = -1;
            var candidateCount = _searchConfigs ? Configs.Length : 1;
            for (var c = 0; c < candidateCount; c++)
            {
                var map = ConfigTokens[c];
                tokens.Clear();
                long rawBits = 0;
                long total = lengthCount;
                var maxToken = 0;
                for (var token = 0; token < FineAlphabetSize; token++)
                {
                    var count = fine[token];
                    if (count == 0)
                        continue;

                    var (coarse, bitCount) = map[token];
                    if (coarse >= tokens.Length)
                    {
                        maxToken = int.MaxValue;
                        break;
                    }

                    tokens[coarse] += count;
                    rawBits += count * bitCount;
                    total += count;
                    maxToken = Math.Max(maxToken, coarse);
                }

                if (maxToken >= limit)
                    continue;

                var cost = (double)rawBits;
                var used = 0;
                foreach (var count in tokens)
                {
                    if (count != 0)
                    {
                        cost -= count * Math.Log2((double)count / total);
                        used++;
                    }
                }

                // The description of the tokens
                cost += 5.0 * used;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = c;
                }
            }

            if (best < 0)
                return null;

            result[cluster] = best;
        }

        return result;
    }

    /// <summary>The tokens of a cluster for a configuration: those of the values, then those of the lengths of the copies.</summary>
    private uint[] GetTokenHistogram(long[] fine, long[] lengths, int configIndex)
    {
        var map = ConfigTokens[configIndex];
        var size = _useLz77 ? Lz77MinSymbol + LengthTokenCount : 1;
        for (var token = 0; token < FineAlphabetSize; token++)
        {
            if (fine[token] != 0)
            {
                size = Math.Max(size, map[token].Token + 1);
            }
        }

        var histogram = new uint[size];
        for (var token = 0; token < FineAlphabetSize; token++)
        {
            if (fine[token] != 0)
            {
                histogram[map[token].Token] += (uint)Math.Min(uint.MaxValue, fine[token]);
            }
        }

        for (var token = 0; token < lengths.Length && _useLz77; token++)
        {
            histogram[Lz77MinSymbol + token] += (uint)Math.Min(uint.MaxValue, lengths[token]);
        }

        // Without a value, the token 0 stands for the cluster
        return histogram;
    }

    /// <summary>The size in bits of the code with prefix codes and of the values it codes.</summary>
    private long MeasurePrefix(LsbBitWriter description, long[][] fineClusters, long[][] lengthClusters, int[] configs)
    {
        long bits = 0;
        for (var cluster = 0; cluster < fineClusters.Length; cluster++)
        {
            var histogram = GetTokenHistogram(fineClusters[cluster], lengthClusters[cluster], configs[cluster]);
            var size = GetAlphabetSize(histogram);
            JpegXlEntropyEncoder.WriteConfig(description, Configs[configs[cluster]], JpegXlPrefixCode.MaxLength);
            JpegXlEntropyEncoder.WriteVarLenUint16(description, size - 1);
            if (size > 1)
            {
                var lengths = new byte[size];
                var codes = new ushort[size];
                JpegXlEntropyEncoder.WritePrefixCode(description, histogram.AsSpan(0, size), lengths, codes);
                for (var token = 0; token < size; token++)
                {
                    bits += histogram[token] * (long)lengths[token];
                }
            }

            bits += GetRawBitCount(fineClusters[cluster], lengthClusters[cluster], configs[cluster]);
        }

        return bits + description.BitCount;
    }

    /// <summary>The size in bits of the code with ANS and of the values it codes.</summary>
    private long MeasureAns(LsbBitWriter description, long[][] fineClusters, long[][] lengthClusters, int[] configs)
    {
        var bits = 2.0 + 32;
        Span<int> distribution = stackalloc int[BucketCount];
        for (var cluster = 0; cluster < fineClusters.Length; cluster++)
        {
            var histogram = GetTokenHistogram(fineClusters[cluster], lengthClusters[cluster], configs[cluster]);
            JpegXlEntropyEncoder.WriteConfig(description, Configs[configs[cluster]], JpegXlEntropyEncoder.AnsLogAlphabetSize);
            JpegXlEntropyEncoder.Normalize(histogram, distribution);
            JpegXlEntropyEncoder.WriteDistribution(description, distribution);
            for (var token = 0; token < histogram.Length; token++)
            {
                if (histogram[token] != 0)
                {
                    bits += histogram[token] * (JpegXlAnsDistribution.LogTableSize - Math.Log2(distribution[token]));
                }
            }

            bits += GetRawBitCount(fineClusters[cluster], lengthClusters[cluster], configs[cluster]);
        }

        return (long)Math.Ceiling(bits) + description.BitCount;
    }

    private static long GetRawBitCount(long[] fine, long[] lengths, int configIndex)
    {
        var map = ConfigTokens[configIndex];
        long bits = 0;
        for (var token = 0; token < FineAlphabetSize; token++)
        {
            bits += fine[token] * map[token].BitCount;
        }

        // The length tokens of the configuration of the lengths: the token is one plus the logarithm, the bits below it are raw
        for (var token = 2; token < lengths.Length; token++)
        {
            bits += lengths[token] * (token - 1);
        }

        return bits;
    }

    /// <summary>The counts of the values of some streams, by context: fine tokens, and the tokens of the lengths of the copies.</summary>
    internal sealed class Counter : IDisposable
    {
        private readonly int _mapLength;
        private Av1PooledArray<uint>? _fine;
        private Av1PooledArray<uint>? _lengths;

        internal Counter(JpegXlModularEntropyCode code)
        {
            _mapLength = code._contextCount + (code._useLz77 ? 1 : 0);
            _fine = Av1PooledArray<uint>.Rent(code._scope, (long)_mapLength * FineAlphabetSize, clear: true);
            try
            {
                _lengths = Av1PooledArray<uint>.Rent(code._scope, (long)_mapLength * LengthTokenCount, clear: true);
            }
            catch
            {
                _fine.Dispose();
                throw;
            }
        }

        /// <summary>Counts a value.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Add(int context, uint value) => _fine!.Span[(context * FineAlphabetSize) + GetFineToken(value)]++;

        /// <summary>Counts a copy: its length in the context of its first value, its distance in the last context.</summary>
        public void AddCopy(int context, int length, uint distance)
        {
            _lengths!.Span[(context * LengthTokenCount) + GetLengthToken(length)]++;
            _fine!.Span[((_mapLength - 1) * FineAlphabetSize) + GetFineToken(distance)]++;
        }

        internal ReadOnlySpan<uint> GetFine(int context) => _fine!.Span.Slice(context * FineAlphabetSize, FineAlphabetSize);

        internal ReadOnlySpan<uint> GetLengths(int context) => _lengths!.Span.Slice(context * LengthTokenCount, LengthTokenCount);

        internal void Add(Counter other)
        {
            var target = _fine!.Span;
            var source = other._fine!.Span;
            for (var i = 0; i < target.Length; i++)
            {
                target[i] += source[i];
            }

            var targetLengths = _lengths!.Span;
            var sourceLengths = other._lengths!.Span;
            for (var i = 0; i < targetLengths.Length; i++)
            {
                targetLengths[i] += sourceLengths[i];
            }
        }

        public void Dispose()
        {
            _fine?.Dispose();
            _fine = null;
            _lengths?.Dispose();
            _lengths = null;
        }
    }
}
