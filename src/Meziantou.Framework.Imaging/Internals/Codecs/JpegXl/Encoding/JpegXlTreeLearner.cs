namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Learns the part of a meta-adaptive tree (ISO/IEC 18181-1, section H.4.2) that codes the samples of one channel: from
/// samples of the channel, the leaf that codes them all is split, best gain first, on the property and the value that
/// divide its samples into the two sets whose residuals have the smallest entropy, each with the predictor that suits it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// The values of each property are reduced to at most <see cref="MaxBuckets"/> ranges whose bounds are quantiles of the
/// samples, plus -1 and 0: a decision tests one of these bounds. A decision only divides samples that the decisions above it
/// let through, so it never tests a range of values that they exclude: the tree is valid.
/// </description></item>
/// <item><description>
/// The size of a leaf is the entropy of the tokens of its residuals, plus their raw bits, with the predictor that makes it
/// the smallest. A decision is kept when it saves more bits, over the whole channel, than <see cref="LearnerSettings.SplitCost"/>.
/// </description></item>
/// <item><description>
/// The decisions only depend on the samples: the order of the evaluations and the ties are fixed.
/// </description></item>
/// </list>
/// </remarks>
internal static class JpegXlTreeLearner
{
    /// <summary>The largest number of ranges of a property.</summary>
    public const int MaxBuckets = 64;

    /// <summary>The number of tokens of a residual (the hybrid integers 4, 1, 0 of 32-bit values).</summary>
    public const int TokenCount = 72;

    private const int LogTableSize = 1 << 16;

    /// <summary>For each token, its number of raw bits.</summary>
    private static readonly byte[] RawBits = CreateRawBits();

    /// <summary>c * log2(c) for the counts below <see cref="LogTableSize"/>.</summary>
    private static readonly float[] CountLogs = CreateCountLogs();

    /// <summary>The token of a residual, as the hybrid integers 4, 1, 0 code it.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static byte GetToken(uint residual)
    {
        if (residual < 16)
            return (byte)residual;

        var log = System.Numerics.BitOperations.Log2(residual);
        return (byte)(16 + ((log - 4) << 1) + (int)((residual >> (log - 1)) & 1));
    }

    /// <summary>Gets the number of raw bits that follow a token.</summary>
    public static int GetRawBitCount(int token) => RawBits[token];

    /// <summary>Learns the tree of the samples of one channel.</summary>
    /// <param name="samples">The samples.</param>
    /// <param name="settings">What the tree is learned with.</param>
    /// <param name="scope">The scope of the working memory.</param>
    /// <param name="cost">Receives the estimated size in bits of the channel with the tree: its leaves, scaled to the whole channel, and their decisions.</param>
    /// <param name="cancellationToken">The token checked between decisions.</param>
    /// <returns>The root of the tree.</returns>
    public static TreeNode Learn(SampleSet samples, LearnerSettings settings, AllocationScope scope, out double cost, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var count = samples.Count;
        var predictorCount = samples.Predictors.Length;
        if (count == 0)
        {
            cost = 0;
            return TreeNode.CreateLeaf(samples.Predictors[0]);
        }

        // One row of bytes per sample, contiguous for the samples of a leaf: the range of each property, then the token
        // of each predictor; a split moves the rows of the left child before those of the right child
        var propertyCount = samples.PropertyIds.Length;
        var stride = propertyCount + predictorCount;
        using var rowStorage = Av1PooledArray<byte>.Rent(scope, (long)count * stride, clear: false);
        var rows = rowStorage.Span[..(count * stride)];
        var thresholds = new int[propertyCount][];
        var valueCounts = new int[1 << 16];
        var lookup = new byte[1 << 16];
        for (var p = 0; p < propertyCount; p++)
        {
            thresholds[p] = Quantize(samples.GetProperty(p), settings.MaxBuckets, valueCounts, lookup, rows[p..], stride);
        }

        for (var k = 0; k < predictorCount; k++)
        {
            var source = samples.GetTokens(k);
            for (var i = 0; i < count; i++)
            {
                rows[(i * stride) + propertyCount + k] = source[i];
            }
        }

        var evaluator = new SplitEvaluator(rowStorage, propertyCount, predictorCount, settings);
        var root = new Candidate(0, count, TreeNode.CreateLeaf(samples.Predictors[0]));
        evaluator.Evaluate(root, samples.Predictors);

        // The leaf whose best decision saves the most bits is split first, until no decision is worth its cost or the
        // tree has its largest number of leaves; the ties go to the leaf found first
        var queue = new PriorityQueue<Candidate, (double, int)>();
        var order = 0;
        var leafCount = 1;
        var leafCosts = root.LeafCost;
        var minimumGain = settings.SplitCost / samples.Scale;
        if (root.Gain > minimumGain)
        {
            queue.Enqueue(root, (-root.Gain, order++));
        }

        var swap = new byte[stride];
        while (queue.Count > 0 && leafCount < settings.MaxLeaves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = queue.Dequeue();
            var property = candidate.Property;
            var bucket = candidate.Bucket;

            // The samples above the split value go first: they are those of the left child
            var low = candidate.Start;
            var high = candidate.End - 1;
            while (true)
            {
                while (low <= high && rows[(low * stride) + property] >= bucket)
                {
                    low++;
                }

                while (low <= high && rows[(high * stride) + property] < bucket)
                {
                    high--;
                }

                if (low >= high)
                    break;

                var a = rows.Slice(low * stride, stride);
                var b = rows.Slice(high * stride, stride);
                a.CopyTo(swap);
                b.CopyTo(a);
                swap.CopyTo(b);
                low++;
                high--;
            }

            var middle = low;
            var node = candidate.Node;
            node.Property = samples.PropertyIds[property];
            node.Value = thresholds[property][bucket - 1];
            node.Left = TreeNode.CreateLeaf(samples.Predictors[0]);
            node.Right = TreeNode.CreateLeaf(samples.Predictors[0]);
            leafCount++;

            var left = new Candidate(candidate.Start, middle, node.Left);
            var right = new Candidate(middle, candidate.End, node.Right);
            evaluator.Evaluate(left, samples.Predictors);
            evaluator.Evaluate(right, samples.Predictors);
            leafCosts += left.LeafCost + right.LeafCost - candidate.LeafCost;
            if (left.Gain > minimumGain)
            {
                queue.Enqueue(left, (-left.Gain, order++));
            }

            if (right.Gain > minimumGain)
            {
                queue.Enqueue(right, (-right.Gain, order++));
            }
        }

        cost = (leafCosts * samples.Scale) + (leafCount * settings.SplitCost);
        return root.Node;
    }

    /// <summary>
    /// Reduces the values of a property to ranges: the bounds are values of the samples at regular ranks, plus -1 and 0
    /// when values lie on both sides of them, and a range holds the values above its lower bound up to its upper bound.
    /// </summary>
    /// <returns>The bounds; the range of each value is written to <paramref name="destination"/>, every <paramref name="stride"/> bytes.</returns>
    private static int[] Quantize(ReadOnlySpan<short> values, int maxBuckets, int[] counts, byte[] lookup, Span<byte> destination, int stride)
    {
        // A count of each value, then the bounds at regular ranks
        Array.Clear(counts);
        foreach (var value in values)
        {
            counts[value + 32768]++;
        }

        var bounds = new List<int>();
        var step = (double)values.Length / maxBuckets;
        var next = step;
        long cumulative = 0;
        var smallest = int.MaxValue;
        var largest = int.MinValue;
        for (var i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0)
                continue;

            var value = i - 32768;
            smallest = Math.Min(smallest, value);
            largest = value;
            cumulative += counts[i];
            if (cumulative >= next)
            {
                bounds.Add(value);
                while (next <= cumulative)
                {
                    next += step;
                }
            }
        }

        // The largest value is not a bound: no sample is above it
        bounds.Remove(largest);
        foreach (var bound in (ReadOnlySpan<int>)[-1, 0])
        {
            if (bound >= smallest && bound < largest && !bounds.Contains(bound))
            {
                bounds.Add(bound);
            }
        }

        bounds.Sort();
        while (bounds.Count > maxBuckets - 1)
        {
            // Too many bounds once -1 and 0 are added: the ones with the fewest samples between them go
            var fewest = 0;
            var fewestCount = long.MaxValue;
            for (var i = 0; i + 1 < bounds.Count; i++)
            {
                var between = 0L;
                for (var v = bounds[i] + 1; v <= bounds[i + 1]; v++)
                {
                    between += counts[v + 32768];
                }

                if (between < fewestCount && bounds[i] is not (-1 or 0))
                {
                    fewestCount = between;
                    fewest = i;
                }
            }

            bounds.RemoveAt(fewest);
        }

        var thresholds = bounds.ToArray();

        // The range of each value: the number of bounds below it
        var range = 0;
        for (var i = 0; i < lookup.Length; i++)
        {
            while (range < thresholds.Length && thresholds[range] < i - 32768)
            {
                range++;
            }

            lookup[i] = (byte)range;
        }

        for (var i = 0; i < values.Length; i++)
        {
            destination[i * stride] = lookup[values[i] + 32768];
        }

        return thresholds;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static double CountLog(long count) => count < LogTableSize ? CountLogs[count] : count * Math.Log2(count);

    private static byte[] CreateRawBits()
    {
        var result = new byte[TokenCount];
        for (var token = 16; token < TokenCount; token++)
        {
            result[token] = (byte)(3 + ((token - 16) >> 1));
        }

        return result;
    }

    private static float[] CreateCountLogs()
    {
        var result = new float[LogTableSize];
        for (var i = 1; i < result.Length; i++)
        {
            result[i] = (float)(i * Math.Log2(i));
        }

        return result;
    }

    /// <summary>The samples of a channel the tree is learned from.</summary>
    internal sealed class SampleSet : IDisposable
    {
        private readonly int _capacity;
        private Av1PooledArray<short>? _properties;
        private Av1PooledArray<byte>? _tokens;

        /// <param name="propertyIds">The properties of the samples (indexes of the format).</param>
        /// <param name="predictors">The predictors the leaves choose from; the first one is the default.</param>
        /// <param name="capacity">The number of samples the set holds.</param>
        /// <param name="scope">The scope the samples are rented from.</param>
        public SampleSet(int[] propertyIds, int[] predictors, int capacity, AllocationScope scope)
        {
            PropertyIds = propertyIds;
            Predictors = predictors;
            _capacity = capacity;
            _properties = Av1PooledArray<short>.Rent(scope, Math.Max(1, (long)capacity * propertyIds.Length), clear: false);
            try
            {
                _tokens = Av1PooledArray<byte>.Rent(scope, Math.Max(1, (long)capacity * predictors.Length), clear: false);
            }
            catch
            {
                _properties.Dispose();
                throw;
            }
        }

        public int[] PropertyIds { get; }

        public int[] Predictors { get; }

        public int Count { get; private set; }

        /// <summary>Gets or sets the number of samples of the channel per sample of the set.</summary>
        public double Scale { get; set; } = 1;

        /// <summary>Adds a sample: the value of each property (clamped to 16 bits), then the token of the residual of each predictor.</summary>
        public void Add(ReadOnlySpan<int> properties, ReadOnlySpan<byte> tokens)
        {
            var propertySpan = _properties!.Span;
            for (var i = 0; i < PropertyIds.Length; i++)
            {
                propertySpan[(i * _capacity) + Count] = (short)Math.Clamp(properties[i], short.MinValue, short.MaxValue);
            }

            var tokenSpan = _tokens!.Span;
            for (var i = 0; i < Predictors.Length; i++)
            {
                tokenSpan[(i * _capacity) + Count] = tokens[i];
            }

            Count++;
        }

        public void Dispose()
        {
            _properties?.Dispose();
            _properties = null;
            _tokens?.Dispose();
            _tokens = null;
        }

        internal ReadOnlySpan<short> GetProperty(int index) => _properties!.Span.Slice(index * _capacity, Count);

        internal ReadOnlySpan<byte> GetTokens(int index) => _tokens!.Span.Slice(index * _capacity, Count);
    }

    /// <summary>What a tree is learned with.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    internal readonly record struct LearnerSettings(int MaxLeaves, int MaxBuckets, double SplitCost, int SplitPredictors);

    /// <summary>A node of a learned tree: a decision on a property, or a leaf with its predictor.</summary>
    internal sealed class TreeNode
    {
        /// <summary>Gets or sets the property of a decision, or -1 for a leaf.</summary>
        public int Property { get; set; } = -1;

        /// <summary>Gets or sets the value a decision compares the property with: the left child takes the larger values.</summary>
        public int Value { get; set; }

        public TreeNode? Left { get; set; }

        public TreeNode? Right { get; set; }

        /// <summary>Gets or sets the predictor of a leaf.</summary>
        public int Predictor { get; set; }

        public bool IsLeaf => Property < 0;

        public static TreeNode CreateLeaf(int predictor) => new() { Predictor = predictor };

        public static TreeNode CreateDecision(int property, int value, TreeNode left, TreeNode right) => new() { Property = property, Value = value, Left = left, Right = right };
    }

    private sealed class Candidate(int start, int end, TreeNode node)
    {
        public int Start { get; } = start;

        public int End { get; } = end;

        public TreeNode Node { get; } = node;

        public int Property { get; set; }

        public int Bucket { get; set; }

        public double Gain { get; set; }

        public double LeafCost { get; set; }
    }

    /// <summary>
    /// Finds the best decision of a leaf, and its predictor. The decisions are compared with the predictors that suit the
    /// whole leaf best (<see cref="LearnerSettings.SplitPredictors"/> of them); each child then has the best of all.
    /// </summary>
    private sealed class SplitEvaluator
    {
        private readonly Av1PooledArray<byte> _rows;
        private readonly int _propertyCount;
        private readonly int _predictorCount;
        private readonly int _stride;
        private readonly int _splitPredictors;
        private readonly int _rowLength;
        private readonly int[] _total;
        private readonly int[] _counts;
        private readonly int[] _bucketCounts;
        private readonly int[] _left;
        private readonly double[] _leftSums;
        private readonly double[] _rightSums;
        private readonly long[] _leftRaw;
        private readonly long[] _rightRaw;
        private readonly int[] _low;
        private readonly int[] _high;

        public SplitEvaluator(Av1PooledArray<byte> rows, int propertyCount, int predictorCount, LearnerSettings settings)
        {
            _rows = rows;
            _propertyCount = propertyCount;
            _predictorCount = predictorCount;
            _stride = propertyCount + predictorCount;
            _splitPredictors = Math.Min(settings.SplitPredictors, predictorCount);
            _rowLength = _splitPredictors * TokenCount;
            _total = new int[predictorCount * TokenCount];
            _counts = new int[propertyCount * settings.MaxBuckets * _rowLength];
            _bucketCounts = new int[propertyCount * settings.MaxBuckets];
            _left = new int[_rowLength];
            _leftSums = new double[_splitPredictors];
            _rightSums = new double[_splitPredictors];
            _leftRaw = new long[_splitPredictors];
            _rightRaw = new long[_splitPredictors];
            _low = new int[propertyCount];
            _high = new int[propertyCount];
            MaxBuckets = settings.MaxBuckets;
        }

        private int MaxBuckets { get; }

        public void Evaluate(Candidate candidate, int[] predictors)
        {
            var start = candidate.Start;
            var end = candidate.End;
            var n = end - start;
            var rows = _rows.Span;
            var stride = _stride;
            var propertyCount = _propertyCount;
            var total = _total.AsSpan();
            total.Clear();
            for (var i = start; i < end; i++)
            {
                var row = rows.Slice(i * stride, stride);
                for (var k = 0; k < _predictorCount; k++)
                {
                    total[(k * TokenCount) + row[propertyCount + k]]++;
                }
            }

            // The leaf with each predictor; the predictors the decisions are compared with are the best ones
            Span<double> leafCosts = stackalloc double[_predictorCount];
            for (var k = 0; k < _predictorCount; k++)
            {
                var sum = 0.0;
                long raw = 0;
                for (var t = 0; t < TokenCount; t++)
                {
                    var c = total[(k * TokenCount) + t];
                    if (c != 0)
                    {
                        sum += CountLog(c);
                        raw += (long)c * RawBits[t];
                    }
                }

                leafCosts[k] = CountLog(n) - sum + raw;
            }

            Span<int> selected = stackalloc int[_splitPredictors];
            Span<bool> taken = stackalloc bool[_predictorCount];
            taken.Clear();
            for (var j = 0; j < selected.Length; j++)
            {
                var best = -1;
                for (var k = 0; k < _predictorCount; k++)
                {
                    if (!taken[k] && (best < 0 || leafCosts[k] < leafCosts[best]))
                    {
                        best = k;
                    }
                }

                selected[j] = best;
                taken[best] = true;
            }

            var leafCost = leafCosts[selected[0]];
            candidate.LeafCost = leafCost;
            candidate.Node.Predictor = predictors[selected[0]];
            candidate.Gain = 0;
            if (n < 2 || leafCost <= 0)
                return;

            // The tokens that occur, for each selected predictor
            var rowLength = _rowLength;
            Span<int> present = stackalloc int[rowLength];
            Span<int> presentCounts = stackalloc int[selected.Length];
            for (var j = 0; j < selected.Length; j++)
            {
                var used = 0;
                for (var t = 0; t < TokenCount; t++)
                {
                    if (total[(selected[j] * TokenCount) + t] != 0)
                    {
                        present[(j * TokenCount) + used++] = t;
                    }
                }

                presentCounts[j] = used;
            }

            // One pass over the samples counts the tokens of every range of every property; the counts are zero outside the
            // ranges of the leaf, which are cleared once read
            var counts = _counts;
            var bucketCounts = _bucketCounts;
            var low = _low.AsSpan();
            var high = _high.AsSpan();
            low.Fill(int.MaxValue);
            high.Fill(-1);
            var propertyLength = MaxBuckets * rowLength;
            Span<int> offsets = stackalloc int[selected.Length];
            for (var i = start; i < end; i++)
            {
                var row = rows.Slice(i * stride, stride);
                for (var j = 0; j < selected.Length; j++)
                {
                    offsets[j] = (j * TokenCount) + row[propertyCount + selected[j]];
                }

                for (var p = 0; p < propertyCount; p++)
                {
                    var b = (int)row[p];
                    low[p] = Math.Min(low[p], b);
                    high[p] = Math.Max(high[p], b);
                    bucketCounts[(p * MaxBuckets) + b]++;
                    var bucketRow = (p * propertyLength) + (b * rowLength);
                    for (var j = 0; j < offsets.Length; j++)
                    {
                        counts[bucketRow + offsets[j]]++;
                    }
                }
            }

            var bestGain = 0.0;
            for (var p = 0; p < propertyCount; p++)
            {
                if (low[p] < high[p])
                {
                    var (gain, bucket) = Sweep(counts.AsSpan(p * propertyLength, propertyLength), bucketCounts.AsSpan(p * MaxBuckets, MaxBuckets), low[p], high[p], n, leafCost, selected, present, presentCounts);
                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        candidate.Property = p;
                        candidate.Bucket = bucket;
                    }
                }

                counts.AsSpan((p * propertyLength) + (low[p] * rowLength), (high[p] - low[p] + 1) * rowLength).Clear();
                bucketCounts.AsSpan((p * MaxBuckets) + low[p], high[p] - low[p] + 1).Clear();
            }

            candidate.Gain = bestGain;
        }

        /// <summary>
        /// The best decision on a property: the left child takes the ranges from the highest one down, and the sums of both
        /// children are updated token by token.
        /// </summary>
        private (double Gain, int Bucket) Sweep(ReadOnlySpan<int> counts, ReadOnlySpan<int> bucketCounts, int low, int high, int n, double leafCost, ReadOnlySpan<int> selected, ReadOnlySpan<int> present, ReadOnlySpan<int> presentCounts)
        {
            var total = _total;
            var rowLength = _rowLength;
            var left = _left.AsSpan();
            left.Clear();
            for (var j = 0; j < selected.Length; j++)
            {
                var sum = 0.0;
                long raw = 0;
                for (var i = 0; i < presentCounts[j]; i++)
                {
                    var t = present[(j * TokenCount) + i];
                    var c = total[(selected[j] * TokenCount) + t];
                    sum += CountLog(c);
                    raw += (long)c * RawBits[t];
                }

                _leftSums[j] = 0;
                _rightSums[j] = sum;
                _leftRaw[j] = 0;
                _rightRaw[j] = raw;
            }

            var bestGain = 0.0;
            var bestBucket = 0;
            var leftCount = 0;
            for (var b = high; b > low; b--)
            {
                var bucketCount = bucketCounts[b];
                if (bucketCount == 0)
                    continue;

                leftCount += bucketCount;
                var row = b * rowLength;
                for (var j = 0; j < selected.Length; j++)
                {
                    var leftSum = _leftSums[j];
                    var rightSum = _rightSums[j];
                    var leftRaw = _leftRaw[j];
                    var rightRaw = _rightRaw[j];
                    var totalRow = selected[j] * TokenCount;
                    for (var i = 0; i < presentCounts[j]; i++)
                    {
                        var t = present[(j * TokenCount) + i];
                        var index = (j * TokenCount) + t;
                        var c = counts[row + index];
                        if (c == 0)
                            continue;

                        var before = left[index];
                        var after = before + c;
                        leftSum += CountLog(after) - CountLog(before);
                        var rightBefore = total[totalRow + t] - before;
                        rightSum += CountLog(rightBefore - c) - CountLog(rightBefore);
                        left[index] = after;
                        leftRaw += (long)c * RawBits[t];
                        rightRaw -= (long)c * RawBits[t];
                    }

                    _leftSums[j] = leftSum;
                    _rightSums[j] = rightSum;
                    _leftRaw[j] = leftRaw;
                    _rightRaw[j] = rightRaw;
                }

                var rightCount = n - leftCount;
                if (rightCount == 0)
                    break;

                var leftCost = double.MaxValue;
                var rightCost = double.MaxValue;
                var leftLog = CountLog(leftCount);
                var rightLog = CountLog(rightCount);
                for (var j = 0; j < selected.Length; j++)
                {
                    leftCost = Math.Min(leftCost, leftLog - _leftSums[j] + _leftRaw[j]);
                    rightCost = Math.Min(rightCost, rightLog - _rightSums[j] + _rightRaw[j]);
                }

                var gain = leafCost - leftCost - rightCost;
                if (gain > bestGain)
                {
                    bestGain = gain;
                    bestBucket = b;
                }
            }

            return (bestGain, bestBucket);
        }
    }
}
