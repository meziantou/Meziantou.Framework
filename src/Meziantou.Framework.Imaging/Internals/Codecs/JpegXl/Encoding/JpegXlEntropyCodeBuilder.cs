namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Builds the entropy code of the streams that share one description (ISO/IEC 18181-1, section D.3), such as the
/// coefficients of the groups of a pass: the values are counted, their contexts are clustered, the description is
/// written once, then each stream is written with it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Order of the calls: <see cref="Count"/> for every value of every stream; <see cref="Build"/>;
/// <see cref="WriteDescription"/>; then, for each stream, <see cref="BeginStream"/>, <see cref="Add(int, uint)"/> for each of its
/// values, and <see cref="WriteStream(LsbBitWriter)"/>. The values are given twice so that only the ones of one stream are kept.
/// Once the code is built, several streams can be written at the same time, each in a <see cref="StreamBuffer"/> of its own;
/// the counts of several builders can be added up (<see cref="AddCounts"/>).
/// </description></item>
/// <item><description>
/// Clustering: the contexts are taken in order; each joins the cluster whose size grows the least, or starts a cluster
/// when that growth exceeds the estimated size of a new distribution. The order of the contexts and the ties are fixed, so
/// the result only depends on the values. A context without value takes the cluster of the context before it.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class JpegXlEntropyCodeBuilder : IDisposable
{
    private const int BucketCount = 1 << JpegXlEntropyEncoder.AnsLogAlphabetSize;

    private readonly AllocationScope _scope;
    private readonly int _contextCount;
    private readonly JpegXlHybridUintConfig _config;
    private readonly int _alphabetSize;
    private readonly int _maxClusters;
    private readonly bool _useAns;

    // The histogram of each context that has a value
    private Av1PooledArray<int>? _rows;
    private Av1PooledArray<uint>? _histograms;
    private int _rowCount;

    // The code
    private Av1PooledArray<byte>? _map;
    private uint[][] _clusterHistograms = [];
    private ushort[][] _codes = [];
    private byte[][] _lengths = [];
    private Av1PooledArray<int>? _distributions;
    private Av1PooledArray<int>? _starts;
    private Av1PooledArray<ushort>? _positions;

    // The stream that is being written
    private readonly StreamBuffer _stream;

    /// <summary>Starts the code of streams of <paramref name="contextCount"/> contexts.</summary>
    /// <param name="contextCount">The number of contexts.</param>
    /// <param name="config">The hybrid integer configuration of every cluster.</param>
    /// <param name="maxClusters">The largest number of clusters, from 1 to 256.</param>
    /// <param name="useAns"><see langword="true"/> to write the values with the ANS coder; <see langword="false"/> to write them with prefix codes.</param>
    /// <param name="scope">The scope of the working memory.</param>
    public JpegXlEntropyCodeBuilder(int contextCount, JpegXlHybridUintConfig config, int maxClusters, bool useAns, AllocationScope scope)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(contextCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxClusters, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxClusters, JpegXlEntropyCode.MaxClusters);
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
        _contextCount = contextCount;
        _config = config;
        _maxClusters = maxClusters;
        _useAns = useAns;

        // The largest token of a 32-bit value: the split, then one group of tokens per number of raw bits
        _alphabetSize = (1 << config.SplitExponent) + ((32 - config.SplitExponent) << (config.MsbInToken + config.LsbInToken));
        if (useAns && (_alphabetSize > BucketCount || config.SplitExponent > JpegXlEntropyEncoder.AnsLogAlphabetSize))
            throw new ArgumentException("The tokens of the configuration do not fit the alphabet of the ANS coder.", nameof(config));

        _rows = Av1PooledArray<int>.Rent(scope, contextCount, clear: false);
        _rows.Span.Fill(-1);
        _stream = new StreamBuffer(scope);
    }

    /// <summary>Gets the number of clusters, once the code is built.</summary>
    public int ClusterCount { get; private set; }

    /// <summary>Counts a value of a stream.</summary>
    public void Count(int context, uint value)
    {
        var rows = _rows!.Span;
        var row = rows[context];
        if (row < 0)
        {
            row = rows[context] = AddRow();
        }

        var (token, _, _) = JpegXlEntropyEncoder.GetToken(_config, value);
        _histograms!.Span[(row * _alphabetSize) + token]++;
    }

    /// <summary>Adds the values counted by another builder of the same contexts: the counts are those of one builder that counted both.</summary>
    public void AddCounts(JpegXlEntropyCodeBuilder other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var otherRows = other._rows!.Span;
        for (var context = 0; context < _contextCount; context++)
        {
            if (otherRows[context] < 0)
                continue;

            var rows = _rows!.Span;
            var row = rows[context];
            if (row < 0)
            {
                row = rows[context] = AddRow();
            }

            var source = other._histograms!.Span.Slice(otherRows[context] * _alphabetSize, _alphabetSize);
            var target = _histograms!.Span.Slice(row * _alphabetSize, _alphabetSize);
            for (var token = 0; token < _alphabetSize; token++)
            {
                target[token] += source[token];
            }
        }
    }

    /// <summary>Clusters the contexts and computes the code of each cluster, once every value is counted.</summary>
    public void Build()
    {
        var rows = _rows!.Span;
        _map = Av1PooledArray<byte>.Rent(_scope, _contextCount, clear: true);
        var map = _map.Span;
        var clusters = new List<uint[]>();
        var totals = new List<long>();
        var sums = new List<double>();
        var previous = 0;
        for (var context = 0; context < _contextCount; context++)
        {
            if (rows[context] < 0)
            {
                map[context] = (byte)previous;
                continue;
            }

            var histogram = _histograms!.Span.Slice(rows[context] * _alphabetSize, _alphabetSize);
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

            // The growth of each cluster when the context joins it: the size of the merged values minus the two sizes
            var cost = (total * Math.Log2(total)) - sum;
            var best = -1;
            var bestGrowth = double.MaxValue;
            for (var i = 0; i < clusters.Count; i++)
            {
                var cluster = clusters[i];
                var mergedSum = sums[i];
                for (var token = 0; token < histogram.Length; token++)
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
                var growth = (mergedTotal * Math.Log2(mergedTotal)) - mergedSum - ((totals[i] * Math.Log2(totals[i])) - sums[i]) - cost;
                if (growth < bestGrowth)
                {
                    bestGrowth = growth;
                    best = i;
                }
            }

            // A distribution of its own costs about a dozen bits per token
            var newClusterCost = 16.0 + (12.0 * symbolCount);
            if (best < 0 || (bestGrowth > newClusterCost && clusters.Count < _maxClusters))
            {
                best = clusters.Count;
                clusters.Add(new uint[_alphabetSize]);
                totals.Add(0);
                sums.Add(0);
            }

            var target = clusters[best];
            var targetSum = 0.0;
            for (var token = 0; token < histogram.Length; token++)
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

        if (clusters.Count == 0)
        {
            clusters.Add(new uint[_alphabetSize]);
        }

        _clusterHistograms = [.. clusters];
        ClusterCount = clusters.Count;
        _histograms?.Dispose();
        _histograms = null;
        _rows?.Dispose();
        _rows = null;
        if (_useAns)
        {
            _distributions = Av1PooledArray<int>.Rent(_scope, (long)ClusterCount * BucketCount, clear: true);
            _starts = Av1PooledArray<int>.Rent(_scope, (long)ClusterCount * BucketCount, clear: false);
            _positions = Av1PooledArray<ushort>.Rent(_scope, (long)ClusterCount * JpegXlAnsDistribution.TableSize, clear: false);
            for (var i = 0; i < ClusterCount; i++)
            {
                var distribution = _distributions.Span.Slice(i * BucketCount, BucketCount);
                JpegXlEntropyEncoder.Normalize(_clusterHistograms[i], distribution);
                JpegXlEntropyEncoder.BuildAnsEncodingTable(distribution, _starts.Span.Slice(i * BucketCount, BucketCount), _positions.Span.Slice(i * JpegXlAnsDistribution.TableSize, JpegXlAnsDistribution.TableSize));
            }
        }
    }

    /// <summary>Writes the description of the code: no LZ77, the context map, then the code of each cluster.</summary>
    public void WriteDescription(LsbBitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBool(false);
        if (_contextCount > 1)
        {
            WriteContextMap(writer, _map!.Span[.._contextCount], ClusterCount, _scope);
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
            JpegXlEntropyEncoder.WriteConfig(writer, _config, logAlphabetSize);
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
            var size = _alphabetSize;
            while (size > 1 && _clusterHistograms[i][size - 1] == 0)
            {
                size--;
            }

            _lengths[i] = new byte[size];
            _codes[i] = new ushort[size];
            JpegXlEntropyEncoder.WriteVarLenUint16(writer, size - 1);
        }

        for (var i = 0; i < ClusterCount; i++)
        {
            if (_lengths[i].Length > 1)
            {
                JpegXlEntropyEncoder.WritePrefixCode(writer, _clusterHistograms[i].AsSpan(0, _lengths[i].Length), _lengths[i], _codes[i]);
            }
        }
    }

    /// <summary>Starts a stream.</summary>
    public void BeginStream() => _stream.Count = 0;

    /// <summary>Adds a value to the stream that was begun: one of the values that were counted.</summary>
    public void Add(int context, uint value) => Add(_stream, context, value);

    /// <summary>Writes the stream that was begun: the state of the ANS coder, if any, then its values.</summary>
    public void WriteStream(LsbBitWriter writer) => WriteStream(_stream, writer);

    /// <summary>
    /// Adds a value to a stream of its own, once the code is built: several streams can be filled and written at the same
    /// time, the code being only read.
    /// </summary>
    public void Add(StreamBuffer stream, int context, uint value)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (stream.Values is null || stream.Count == stream.Values.Length)
        {
            var larger = Av1PooledArray<ulong>.Rent(stream.Scope, Math.Max(4096, (long)stream.Count * 2), clear: false);
            stream.Values?.Span.CopyTo(larger.Span);
            stream.Values?.Dispose();
            stream.Values = larger;
        }

        // A value is kept as its cluster, its token, the number of raw bits and the raw bits
        var cluster = _map!.Span[context];
        var (token, bitCount, bits) = JpegXlEntropyEncoder.GetToken(_config, value);
        stream.Values.Span[stream.Count++] = ((ulong)cluster << 56) | ((ulong)token << 40) | ((ulong)bitCount << 32) | bits;
    }

    /// <summary>Writes the values of a stream, and empties it.</summary>
    public void WriteStream(StreamBuffer stream, LsbBitWriter writer)
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
        _rows?.Dispose();
        _rows = null;
        _histograms?.Dispose();
        _histograms = null;
        _map?.Dispose();
        _map = null;
        _distributions?.Dispose();
        _distributions = null;
        _starts?.Dispose();
        _starts = null;
        _positions?.Dispose();
        _positions = null;
        _stream.Dispose();
    }

    /// <summary>
    /// Writes the cluster of each context (section D.3.5), in the shorter of its two forms: a fixed number of bits per
    /// context, for at most 8 clusters, or an entropy-coded stream of the clusters once moved to the front of the list of
    /// the clusters, where a run of contexts of one cluster is a run of zeros.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="map">The cluster of each context: every cluster below <paramref name="clusterCount"/> is used.</param>
    /// <param name="clusterCount">The number of clusters.</param>
    /// <param name="scope">The scope of the working memory.</param>
    public static void WriteContextMap(LsbBitWriter writer, ReadOnlySpan<byte> map, int clusterCount, AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(scope);
        var bitsPerEntry = JpegXlHybridUintConfig.CeilingLog2(clusterCount);
        var simpleBits = bitsPerEntry <= 3 ? 3 + ((long)map.Length * bitsPerEntry) : long.MaxValue;

        using var coded = new LsbBitWriter(scope);
        coded.WriteBool(false);
        coded.WriteBool(true);
        using (var stream = new JpegXlEntropyEncoder(1, new JpegXlHybridUintConfig(4, 2, 0), scope, JpegXlEntropyCoding.Ans))
        {
            Span<byte> order = stackalloc byte[JpegXlEntropyCode.MaxClusters];
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = (byte)i;
            }

            foreach (var cluster in map)
            {
                var index = order.IndexOf(cluster);
                stream.Add(0, (uint)index);
                if (index != 0)
                {
                    order[..index].CopyTo(order[1..]);
                    order[0] = cluster;
                }
            }

            stream.Write(coded);
        }

        var codedBits = coded.BitCount;
        if (simpleBits <= codedBits)
        {
            writer.WriteBool(true);
            writer.WriteBits((uint)bitsPerEntry, 2);
            foreach (var cluster in map)
            {
                writer.WriteBits(cluster, bitsPerEntry);
            }

            return;
        }

        coded.Finish();
        writer.WriteBits(coded.WrittenSpan, codedBits);
    }

    private int AddRow()
    {
        if (_histograms is null || (long)(_rowCount + 1) * _alphabetSize > _histograms.Length)
        {
            var capacity = Math.Max(64, _rowCount * 2);
            var larger = Av1PooledArray<uint>.Rent(_scope, (long)capacity * _alphabetSize, clear: true);
            _histograms?.Span.CopyTo(larger.Span);
            _histograms?.Dispose();
            _histograms = larger;
        }

        return _rowCount++;
    }

    /// <summary>The values of a stream that is being written (<see cref="Add(StreamBuffer, int, uint)"/>), with its working memory.</summary>
    public sealed class StreamBuffer : IDisposable
    {
        public StreamBuffer(AllocationScope scope)
        {
            ArgumentNullException.ThrowIfNull(scope);
            Scope = scope;
        }

        internal AllocationScope Scope { get; }

        internal Av1PooledArray<ulong>? Values { get; set; }

        internal Av1PooledArray<int>? Refills { get; set; }

        internal int Count { get; set; }

        public void Dispose()
        {
            Values?.Dispose();
            Values = null;
            Refills?.Dispose();
            Refills = null;
        }
    }
}
