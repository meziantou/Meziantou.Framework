using System.Numerics;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes a lossless Modular sub-bitstream (ISO/IEC 18181-1, section H.3) that holds its own tree: a reversible color
/// transform for three color channels, a tree that gives each channel its own context and its own predictor, and the
/// residuals, written with prefix codes or with the ANS coder, whichever is smaller.
/// </summary>
/// <remarks>
/// The effort selects what is searched, never what is decoded. Below 3, the YCgCo transform and the gradient predictor are
/// used as they are. From 3, the color transform and the predictor of each channel are those with the smallest estimated
/// size among a few candidates; from 7, the weighted predictor is one of them.
/// </remarks>
internal static class JpegXlModularEncoder
{
    /// <summary>The effort from which the color transform and the predictors are searched.</summary>
    public const int SearchEffort = 3;

    /// <summary>The effort from which the weighted predictor is a candidate.</summary>
    public const int WeightedPredictorEffort = 7;

    private const int GradientPredictor = 5;
    private const int WeightedPredictor = 6;
    private const int NoColorTransform = 0;
    private const int YCgCoTransform = 6;

    /// <summary>The green channel first, then the differences of the blue and of the red channels with it.</summary>
    private const int SubtractGreenTransform = 10;

    private const int TokenSplitExponent = 4;
    private const int MaxTokenCount = (1 << TokenSplitExponent) + ((32 - TokenSplitExponent) << 1);

    private static ReadOnlySpan<int> ColorTransformCandidates => [YCgCoTransform, SubtractGreenTransform, NoColorTransform];

    private static ReadOnlySpan<int> PredictorCandidates => [GradientPredictor, 1, 2, 3, 4, 13, WeightedPredictor];

    /// <summary>The predictors of the rows of a channel whose first row is apart: none, the sample on the left, the gradient.</summary>
    private static ReadOnlySpan<int> SeparateRowPredictorCandidates => [0, 1, GradientPredictor];

    private static JpegXlHybridUintConfig SampleConfig => new(TokenSplitExponent, 1, 0);

    // The trees of the small sub-bitstreams: the limits of the lossless encoder at its effort 3. The settings of its higher
    // efforts made the LF image of photographs 1 to 3 % smaller for 5 to 30 times the learning time, and a lower split
    // cost made it larger: the tree and the distributions of a small image cost more than its contexts save
    private const int LearnedTreeMaxLeaves = 32;
    private const int LearnedTreeMaxBuckets = 16;
    private const double LearnedTreeSplitCost = 48;
    private const int LearnedTreeMaxClusters = 32;
    private const int FirstReferenceProperty = 16;
    private const int PropertiesPerReference = 4;

    /// <summary>
    /// The trees of the LF image of a VarDCT frame (the quantized LF value of every block: Y, then X, then B): the
    /// neighbors and the gradients, and the values and the gradient residuals of the previous channels, with the gradient
    /// and the left predictors (the others were almost never chosen), from 4,096 samples per channel at most.
    /// </summary>
    public static LearnedTreeSettings LfImageTree { get; } = new([6, 7, 9, 10, 11, 12, 16, 17, 18, 19, 20, 21, 22, 23], [GradientPredictor, 1], 4096);

    /// <summary>
    /// The trees of the LF image from <see cref="WeightedPredictorEffort"/>: those of <see cref="LfImageTree"/>, with the
    /// weighted predictor and its error as a property, which made the LF image of photographs 0.7 % smaller for 4 % of the
    /// time of an encoding.
    /// </summary>
    public static LearnedTreeSettings LfImageTreeWithWeightedPredictor { get; } = new([.. LfImageTree.Properties, JpegXlMaTree.WeightedProperty], [.. LfImageTree.Predictors, WeightedPredictor], LfImageTree.SamplesPerChannel);

    /// <summary>
    /// The trees of the block metadata of a VarDCT frame (the chroma from luma factors of the tiles, the block list and the
    /// sharpness): the row and the column, which tell the transforms (first row of the block list) from the quantization
    /// factors (second row), and the neighbors; from every sample, since the block list has two rows.
    /// </summary>
    public static LearnedTreeSettings BlockMetadataTree { get; } = new([2, 3, 4, 5, 7, 8, 9, 10, 14], [GradientPredictor, 0, 1], 0);

    /// <summary>Writes the channels of a rectangle of the image.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="image">The channels: the color channels (one or three), then the alpha channel when there is one. The samples of three color channels are replaced with their transform.</param>
    /// <param name="colorChannelCount">The number of color channels.</param>
    /// <param name="effort">The compression effort, from 0 to 9.</param>
    /// <param name="scope">The scope of the working memory.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <param name="firstRowChannel">The channel whose first row holds other values than its other rows, or -1: see <see cref="EncodeChannels"/>.</param>
    public static void Encode(LsbBitWriter writer, JpegXlModularImage image, int colorChannelCount, int effort, AllocationScope scope, CancellationToken cancellationToken, int firstRowChannel = -1)
    {
        var colorTransform = NoColorTransform;
        if (colorChannelCount == 3)
        {
            colorTransform = effort >= SearchEffort ? ChooseColorTransform(image, scope, cancellationToken) : YCgCoTransform;
            if (effort < SearchEffort)
            {
                ApplyColorTransform(colorTransform, image.Channels[0].Samples, image.Channels[1].Samples, image.Channels[2].Samples);
            }
        }

        // The sub-bitstream has its own tree; default parameters of the weighted predictor
        writer.WriteBool(false);
        writer.WriteBool(true);
        if (colorTransform != NoColorTransform)
        {
            // One transform: the reversible color transform of the first three channels
            writer.WriteU32(1, 0, 0, 1, 0, 2, 4, 18, 8);
            writer.WriteBits(0, 2);
            writer.WriteU32(0, 0, 3, 8, 6, 72, 10, 1096, 13);
            writer.WriteU32((uint)colorTransform, 6, 0, 0, 2, 2, 4, 10, 6);
        }
        else
        {
            writer.WriteU32(0, 0, 0, 1, 0, 2, 4, 18, 8);
        }

        EncodeChannels(writer, image.Channels, effort, scope, cancellationToken, firstRowChannel);
    }

    /// <summary>
    /// Writes the start of a sub-bitstream that holds its own tree and applies the squeeze transform with its default
    /// steps to the channels of the image, after a reversible color transform of the first three channels when the image
    /// has three color channels; <see cref="EncodeChannels"/> writes what follows.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="hasColorTransform">Whether the YCgCo transform is applied to the first three channels.</param>
    public static void WriteSqueezeHeader(LsbBitWriter writer, bool hasColorTransform)
    {
        writer.WriteBool(false);
        writer.WriteBool(true);
        writer.WriteU32(hasColorTransform ? 2u : 1u, 0, 0, 1, 0, 2, 4, 18, 8);
        if (hasColorTransform)
        {
            writer.WriteBits(0, 2);
            writer.WriteU32(0, 0, 3, 8, 6, 72, 10, 1096, 13);
            writer.WriteU32(YCgCoTransform, 6, 0, 0, 2, 2, 4, 10, 6);
        }

        // The squeeze transform, without step: the default ones
        writer.WriteBits(2, 2);
        writer.WriteU32(0, 0, 0, 1, 4, 9, 6, 41, 8);
    }

    /// <summary>
    /// Writes a small sub-bitstream that holds its own learned tree and has no transform: the tree of each channel is
    /// learned from its samples (<see cref="JpegXlTreeLearner"/>), and the decisions on the channel lead to these trees;
    /// the contexts are clustered (<see cref="JpegXlModularEntropyCode"/>), and the samples are written with prefix codes
    /// or with the ANS coder, whichever is smaller.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="image">The channels, none of them empty. The residuals of all of them are kept: the image is meant to be small.</param>
    /// <param name="streamIndex">The index of the sub-bitstream, a property of the samples.</param>
    /// <param name="settings">What the trees are learned with.</param>
    /// <param name="scope">The scope of the working memory.</param>
    /// <param name="cancellationToken">The token checked between channels.</param>
    public static void EncodeWithLearnedTree(LsbBitWriter writer, JpegXlModularImage image, int streamIndex, LearnedTreeSettings settings, AllocationScope scope, CancellationToken cancellationToken)
    {
        var channels = image.Channels;
        var learner = new JpegXlTreeLearner.LearnerSettings(LearnedTreeMaxLeaves, LearnedTreeMaxBuckets, LearnedTreeSplitCost, SplitPredictors: 1);
        var trees = new JpegXlTreeLearner.TreeNode[channels.Count];
        long sampleCount = 0;
        var largest = 0;
        for (var c = 0; c < channels.Count; c++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channel = channels[c];
            var length = channel.Samples.Length;
            sampleCount += length;
            largest = Math.Max(largest, length);
            if (channel.Samples.IndexOfAnyExcept(channel.Samples[0]) < 0)
            {
                // A constant channel (the sharpness of the blocks) costs nothing to the gradient predictor but its first sample
                trees[c] = JpegXlTreeLearner.TreeNode.CreateLeaf(GradientPredictor);
                continue;
            }

            // The properties of the previous channels are only offered to a channel that has them
            var references = 0;
            for (var i = c - 1; i >= 0; i--)
            {
                var other = channels[i];
                if (other.Width == channel.Width && other.Height == channel.Height && other.HorizontalShift == channel.HorizontalShift && other.VerticalShift == channel.VerticalShift)
                {
                    references++;
                }
            }

            int[] properties = [.. settings.Properties.Where(property => property < FirstReferenceProperty + (PropertiesPerReference * references))];
            var rowStep = settings.SamplesPerChannel == 0 ? 1 : (int)Math.Max(1, ((long)length + settings.SamplesPerChannel - 1) / settings.SamplesPerChannel);
            using var set = new JpegXlTreeLearner.SampleSet(properties, settings.Predictors, length, scope);
            JpegXlTreeCoder.Sample(image, c, streamIndex, set, rowStep, rowPhase: 0, scope);
            set.Scale = (double)length / set.Count;
            trees[c] = JpegXlTreeLearner.Learn(set, learner, scope, out _, cancellationToken);
        }

        var tree = JpegXlTreeCoder.Flatten(JpegXlTreeCoder.ChainChannels(trees), out var leafCount);

        // The contexts and the residuals of every sample, counted, then written with the code built from the counts
        using var values = Av1PooledArray<ulong>.Rent(scope, sampleCount, clear: false);
        using (var work = Av1PooledArray<JpegXlMaTreeNode>.Rent(scope, tree.Length, clear: false))
        using (var pending = Av1PooledArray<int>.Rent(scope, tree.Length, clear: false))
        using (var residuals = Av1PooledArray<uint>.Rent(scope, largest, clear: false))
        using (var tableBuffer = Av1PooledArray<ulong>.Rent(scope, JpegXlMaTreeTable.BufferLength, clear: false))
        {
            var offset = 0;
            for (var c = 0; c < channels.Count; c++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = channels[c].Samples.Length;
                JpegXlTreeCoder.Collect(image, c, streamIndex, tree, values.Span.Slice(offset, length), work.Span, pending.Span, residuals.Span, tableBuffer.Span, scope);
                offset += length;
            }
        }

        using var code = new JpegXlModularEntropyCode(leafCount, Math.Min(leafCount, LearnedTreeMaxClusters), searchConfigs: false, useLz77: false, scope);
        using (var counter = code.CreateCounter())
        {
            counter.AddRange(values.Span);
            code.AddCounts(counter);
        }

        code.Build();
        WriteHeader(writer);
        JpegXlTreeCoder.WriteTree(writer, tree, scope);
        code.WriteDescription(writer);
        using var stream = new JpegXlEntropyCodeBuilder.StreamBuffer(scope);
        code.AddRange(stream, values.Span);
        code.WriteStream(stream, writer);
    }

    /// <summary>Writes the start of a sub-bitstream that holds its own tree and has no transform.</summary>
    public static void WriteHeader(LsbBitWriter writer)
    {
        writer.WriteBool(false);
        writer.WriteBool(true);
        writer.WriteU32(0, 0, 0, 1, 0, 2, 4, 18, 8);
    }

    /// <summary>Applies the YCgCo transform to three color channels of the same size: the transform <see cref="WriteSqueezeHeader"/> declares.</summary>
    public static void ApplyYCgCo(JpegXlModularImage image) => ApplyColorTransform(YCgCoTransform, image.Channels[0].Samples, image.Channels[1].Samples, image.Channels[2].Samples);

    /// <summary>
    /// Writes the tree and the samples of the channels of a sub-bitstream, after its header: a tree that gives each channel
    /// its own context and its own predictor, then the residuals.
    /// </summary>
    /// <param name="writer">The writer, after the header of the sub-bitstream.</param>
    /// <param name="channels">The channels, none of them empty.</param>
    /// <param name="effort">The compression effort, from 0 to 9.</param>
    /// <param name="scope">The scope of the working memory.</param>
    /// <param name="cancellationToken">The token checked between channels.</param>
    /// <param name="firstRowChannel">
    /// The channel whose first row holds other values than its other rows, or -1. The tree gives this row a context and a
    /// predictor of its own: values of two kinds that share a context each pay for the presence of the other, and a
    /// predictor that reads the row above mixes them. The block list of a VarDCT frame is such a channel.
    /// </param>
    public static void EncodeChannels(LsbBitWriter writer, IReadOnlyList<JpegXlModularChannel> channels, int effort, AllocationScope scope, CancellationToken cancellationToken, int firstRowChannel = -1)
    {
        // The residuals of one channel
        var sampleCount = 0;
        foreach (var channel in channels)
        {
            sampleCount = Math.Max(sampleCount, channel.Samples.Length);
        }

        using var residuals = Av1PooledArray<uint>.Rent(scope, sampleCount, clear: false);
        // One leaf per channel; the other rows of the channel whose first row is apart have the last leaf
        var leafCount = channels.Count + (firstRowChannel >= 0 ? 1 : 0);
        Span<int> predictors = leafCount <= 64 ? stackalloc int[leafCount] : new int[leafCount];
        Span<int> contexts = leafCount <= 64 ? stackalloc int[leafCount] : new int[leafCount];
        predictors.Fill(GradientPredictor);
        if (effort >= SearchEffort)
        {
            var candidates = effort >= WeightedPredictorEffort ? PredictorCandidates : PredictorCandidates[..^1];
            for (var c = 0; c < channels.Count; c++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (c == firstRowChannel)
                    continue;

                var best = double.MaxValue;
                foreach (var candidate in candidates)
                {
                    var cost = EstimateCost(channels[c], candidate, residuals.Span, scope);
                    if (cost < best)
                    {
                        best = cost;
                        predictors[c] = candidate;
                    }
                }
            }
        }

        if (firstRowChannel >= 0)
        {
            // Each part has the predictor that gives it the fewest bits, at every effort: the channel is small. A value
            // that names something (a transform) is often best left as it is; a value that varies slowly, as its difference
            // with the previous one
            var channel = channels[firstRowChannel];
            var values = residuals.Span[..channel.Samples.Length];
            var bestFirst = double.MaxValue;
            var bestOthers = double.MaxValue;
            foreach (var candidate in SeparateRowPredictorCandidates)
            {
                ComputeResiduals(channel, candidate, values, scope);
                var first = GetTokenCost(values[..channel.Width]);
                if (first < bestFirst)
                {
                    bestFirst = first;
                    predictors[firstRowChannel] = candidate;
                }

                var others = GetTokenCost(values[channel.Width..]);
                if (others < bestOthers)
                {
                    bestOthers = others;
                    predictors[^1] = candidate;
                }
            }
        }

        WriteTree(writer, predictors, contexts, channels.Count, firstRowChannel, scope);

        // Prefix codes or the ANS coder, at every effort: a prefix code spends one bit at least on each sample, which is
        // many times the size of a channel that is constant or nearly so, and the ANS coder needs a longer description
        using var stream = new JpegXlEntropyEncoder(contexts.Length, SampleConfig, scope, JpegXlEntropyCoding.Smallest);
        for (var c = 0; c < channels.Count; c++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channel = channels[c];
            var values = residuals.Span[..channel.Samples.Length];
            ComputeResiduals(channel, predictors[c], values, scope);
            var count = c == firstRowChannel ? channel.Width : values.Length;
            stream.AddRange(contexts[c], values[..count]);

            if (c == firstRowChannel && values.Length > count)
            {
                ComputeResiduals(channel, predictors[^1], values, scope);
                stream.AddRange(contexts[^1], values[count..]);
            }
        }

        stream.Write(writer);
    }

    /// <summary>
    /// Writes the global tree of a frame whose groups each hold their own tree: a single leaf, and the entropy code of a
    /// stream that has no value. A decoder that follows the specification reads a tree for the global Modular data even when
    /// no channel is coded there; the global tree gives it one without a channel-dependent choice.
    /// </summary>
    public static void WriteUnusedGlobalTree(LsbBitWriter writer, AllocationScope scope)
    {
        Span<int> contexts = stackalloc int[1];
        WriteTree(writer, [GradientPredictor], contexts, 1, -1, scope);
        using var stream = new JpegXlEntropyEncoder(1, SampleConfig, scope);
        stream.Write(writer);
    }

    /// <summary>
    /// Writes a tree whose leaves are the channels (a chain of decisions on the index of the channel), each with its
    /// predictor, no offset and a multiplier of 1; gives the context of each leaf, which is its rank. The channel whose
    /// first row is apart has a decision on the row instead of a leaf: its first row has the leaf of the channel, its other
    /// rows the last leaf.
    /// </summary>
    private static void WriteTree(LsbBitWriter writer, ReadOnlySpan<int> predictors, Span<int> contexts, int channelCount, int firstRowChannel, AllocationScope scope)
    {
        const int SplitValueContext = 0;
        const int PropertyContext = 1;
        const int PredictorContext = 2;
        const int OffsetContext = 3;
        const int MultiplierLogContext = 4;
        const int MultiplierBitsContext = 5;
        const int RowProperty = 2;

        using var stream = new JpegXlEntropyEncoder(JpegXlMaTree.TreeContextCount, new JpegXlHybridUintConfig(4, 0, 0), scope);

        // Breadth-first: a decision "channel > k" has the rest of the chain on the left and the channel k on the right; a
        // decision "row > 0" has the other rows on the left and the first row on the right. A queued value is the first
        // channel of a chain, or, as its complement, a channel or the leaf of the other rows
        var queue = new Queue<int>();
        queue.Enqueue(0);
        var leafCount = 0;
        var isRowDecisionWritten = false;
        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            if (item >= 0 && item < channelCount - 1)
            {
                // The property 0 is the index of the channel; a property is written plus one
                stream.Add(PropertyContext, 1);
                stream.Add(SplitValueContext, (uint)(item << 1));
                queue.Enqueue(item + 1);
                queue.Enqueue(~item);
                continue;
            }

            var leaf = item < 0 ? ~item : item;
            if (leaf == firstRowChannel && !isRowDecisionWritten)
            {
                stream.Add(PropertyContext, RowProperty + 1);
                stream.Add(SplitValueContext, 0);
                isRowDecisionWritten = true;
                queue.Enqueue(~channelCount);
                queue.Enqueue(~leaf);
                continue;
            }

            contexts[leaf] = leafCount++;
            stream.Add(PropertyContext, 0);
            stream.Add(PredictorContext, (uint)predictors[leaf]);
            stream.Add(OffsetContext, 0);
            stream.Add(MultiplierLogContext, 0);
            stream.Add(MultiplierBitsContext, 0);
        }

        stream.Write(writer);
    }

    /// <summary>
    /// Applies to the color channels the candidate transform whose samples have the smallest estimated size with the gradient
    /// predictor, and returns it.
    /// </summary>
    private static int ChooseColorTransform(JpegXlModularImage image, AllocationScope scope, CancellationToken cancellationToken)
    {
        var length = image.Channels[0].Samples.Length;
        using var original = Av1PooledArray<int>.Rent(scope, (long)length * 3, clear: false);
        for (var c = 0; c < 3; c++)
        {
            image.Channels[c].Samples.CopyTo(original.Span.Slice(length * c, length));
        }

        var best = ColorTransformCandidates[0];
        var bestCost = double.MaxValue;
        var applied = NoColorTransform;
        using var residuals = Av1PooledArray<uint>.Rent(scope, length, clear: false);
        foreach (var candidate in ColorTransformCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Apply(candidate);
            var cost = 0.0;
            for (var c = 0; c < 3; c++)
            {
                cost += EstimateCost(image.Channels[c], GradientPredictor, residuals.Span, scope);
            }

            if (cost < bestCost)
            {
                bestCost = cost;
                best = candidate;
            }
        }

        if (applied != best)
        {
            Apply(best);
        }

        return best;

        void Apply(int transform)
        {
            var first = image.Channels[0].Samples;
            var second = image.Channels[1].Samples;
            var third = image.Channels[2].Samples;
            var source = original.Span;
            source[..first.Length].CopyTo(first);
            source.Slice(first.Length, first.Length).CopyTo(second);
            source.Slice(first.Length * 2, first.Length).CopyTo(third);
            ApplyColorTransform(transform, first, second, third);
            applied = transform;
        }
    }

    /// <summary>The forward transforms of the reversible color transforms the encoder uses, which the decoder undoes.</summary>
    internal static void ApplyColorTransform(int transform, Span<int> red, Span<int> green, Span<int> blue)
    {
        switch (transform)
        {
            case YCgCoTransform:
                for (var i = 0; i < red.Length; i++)
                {
                    var orange = red[i] - blue[i];
                    var temp = blue[i] + (orange >> 1);
                    var chromaGreen = green[i] - temp;
                    red[i] = temp + (chromaGreen >> 1);
                    green[i] = orange;
                    blue[i] = chromaGreen;
                }

                break;

            case SubtractGreenTransform:
                // The permutation 1 stores green, blue, red; the type 3 subtracts the first channel from the two others
                for (var i = 0; i < red.Length; i++)
                {
                    var first = green[i];
                    var second = blue[i] - first;
                    var third = red[i] - first;
                    red[i] = first;
                    green[i] = second;
                    blue[i] = third;
                }

                break;
        }
    }

    /// <summary>
    /// Estimates the number of bits of a channel coded with a predictor: the entropy of the tokens of its residuals, plus
    /// the bits that follow the tokens.
    /// </summary>
    internal static double EstimateCost(JpegXlModularChannel channel, int predictor, Span<uint> residuals, AllocationScope scope)
    {
        residuals = residuals[..channel.Samples.Length];
        ComputeResiduals(channel, predictor, residuals, scope);
        return GetTokenCost(residuals);
    }

    /// <summary>Estimates the number of bits of residuals that have a context of their own.</summary>
    private static double GetTokenCost(ReadOnlySpan<uint> residuals)
    {
        if (residuals.IsEmpty)
            return 0;

        // Four histograms, one per position modulo 4: consecutive equal residuals (most of them are small) would otherwise
        // wait on the increment of the same counter
        Span<int> histograms = stackalloc int[4 * MaxTokenCount];
        histograms.Clear();
        var histogram0 = histograms[..MaxTokenCount];
        var histogram1 = histograms.Slice(MaxTokenCount, MaxTokenCount);
        var histogram2 = histograms.Slice(2 * MaxTokenCount, MaxTokenCount);
        var histogram3 = histograms.Slice(3 * MaxTokenCount, MaxTokenCount);
        long rawBits = 0;
        var i = 0;
        for (; i + 4 <= residuals.Length; i += 4)
        {
            histogram0[GetToken(residuals[i], ref rawBits)]++;
            histogram1[GetToken(residuals[i + 1], ref rawBits)]++;
            histogram2[GetToken(residuals[i + 2], ref rawBits)]++;
            histogram3[GetToken(residuals[i + 3], ref rawBits)]++;
        }

        for (; i < residuals.Length; i++)
        {
            histogram0[GetToken(residuals[i], ref rawBits)]++;
        }

        for (var token = 0; token < MaxTokenCount; token++)
        {
            histogram0[token] += histogram1[token] + histogram2[token] + histogram3[token];
        }

        var histogram = histogram0;
        var total = (double)residuals.Length;
        var cost = (double)rawBits;
        foreach (var count in histogram)
        {
            if (count != 0)
            {
                cost -= count * Math.Log2(count / total);
            }
        }

        return cost;

        // Without a branch: residuals of photographs are small and large in no predictable order
        static int GetToken(uint residual, ref long rawBits)
        {
            var log = BitOperations.Log2(residual | 1);
            var isLarge = residual >= (1u << TokenSplitExponent);
            rawBits += isLarge ? log - 1 : 0;
            var large = (1 << TokenSplitExponent) + ((log - TokenSplitExponent) << 1) + (int)((residual >> ((log - 1) & 31)) & 1);
            return isLarge ? large : (int)residual;
        }
    }

    /// <summary>
    /// Computes the residual of every sample of a channel, as the unsigned value that is coded, in the order of the samples.
    /// Away from the edges, the predictors that only read the original samples are computed four samples at a time: the
    /// predictions are those of <see cref="ComputeResidualsScalar"/>, in 32-bit arithmetic where the scalar code adds
    /// 64-bit values only when every sample is small enough for the sums to be exact.
    /// </summary>
    internal static void ComputeResiduals(JpegXlModularChannel channel, int predictor, Span<uint> residuals, AllocationScope scope)
    {
        var samples = channel.Samples;
        if (!Vector128.IsHardwareAccelerated || predictor is not (0 or 1 or 2 or 3 or 4 or GradientPredictor or 13) || (predictor is 3 or 4 or 13 && !HasSmallSamples(samples)))
        {
            ComputeResidualsScalar(channel, predictor, residuals, scope);
            return;
        }

        var width = channel.Width;
        var height = channel.Height;
        for (var y = 0; y < height; y++)
        {
            var row = samples.Slice(y * width, width);
            var above = y > 0 ? samples.Slice((y - 1) * width, width) : default;
            var twoAbove = y > 1 ? samples.Slice((y - 2) * width, width) : default;
            var output = residuals.Slice(y * width, width);

            // The samples whose neighbors are all inside the channel
            var start = predictor switch
            {
                0 => 0,
                13 => 2,
                _ => 1,
            };
            var end = predictor == 13 ? width - 2 : width;
            if (y == 0 || (predictor == 13 && y == 1) || end - start < Vector128<int>.Count)
            {
                ComputeRowResidualsScalar(row, above, twoAbove, y, predictor, output, 0, width);
                continue;
            }

            ComputeRowResidualsScalar(row, above, twoAbove, y, predictor, output, 0, start);
            var x = start;
            for (; x <= end - Vector128<int>.Count; x += Vector128<int>.Count)
            {
                var prediction = predictor switch
                {
                    0 => Vector128<int>.Zero,
                    1 => Vector128.Create(row.Slice(x - 1, 4)),
                    2 => Vector128.Create(above.Slice(x, 4)),
                    3 => Average(Vector128.Create(row.Slice(x - 1, 4)), Vector128.Create(above.Slice(x, 4))),
                    4 => Select(Vector128.Create(row.Slice(x - 1, 4)), Vector128.Create(above.Slice(x, 4)), Vector128.Create(above.Slice(x - 1, 4))),
                    GradientPredictor => ClampedGradient(Vector128.Create(row.Slice(x - 1, 4)), Vector128.Create(above.Slice(x, 4)), Vector128.Create(above.Slice(x - 1, 4))),
                    _ => Weighted(row, above, twoAbove, x),
                };
                var residual = Vector128.Create(row.Slice(x, 4)) - prediction;
                ((residual << 1) ^ Vector128.ShiftRightArithmetic(residual, 31)).AsUInt32().CopyTo(output.Slice(x, 4));
            }

            ComputeRowResidualsScalar(row, above, twoAbove, y, predictor, output, x, width);
        }

        // (left + top) / 2, rounded toward zero
        static Vector128<int> Average(Vector128<int> left, Vector128<int> top)
        {
            var sum = left + top;
            return Vector128.ShiftRightArithmetic(sum + Vector128.ShiftRightLogical(sum, 31), 1);
        }

        // The neighbor whose difference with the top-left one is the larger, the top one on a tie
        static Vector128<int> Select(Vector128<int> left, Vector128<int> top, Vector128<int> topLeft)
        {
            var useLeft = Vector128.LessThan(Vector128.Abs(top - topLeft), Vector128.Abs(left - topLeft));
            return Vector128.ConditionalSelect(useLeft, left, top);
        }

        static Vector128<int> ClampedGradient(Vector128<int> left, Vector128<int> top, Vector128<int> topLeft)
        {
            var min = Vector128.Min(left, top);
            var max = Vector128.Max(left, top);
            var gradient = left + top - topLeft;
            var result = Vector128.ConditionalSelect(Vector128.LessThan(topLeft, min), max, gradient);
            return Vector128.ConditionalSelect(Vector128.GreaterThan(topLeft, max), min, result);
        }

        // (6 top - 2 topTop + 7 left + leftLeft + topRightRight + 3 topRight + 8) / 16, rounded toward zero
        static Vector128<int> Weighted(ReadOnlySpan<int> row, ReadOnlySpan<int> above, ReadOnlySpan<int> twoAbove, int x)
        {
            var top = Vector128.Create(above.Slice(x, 4));
            var left = Vector128.Create(row.Slice(x - 1, 4));
            var sum = (top * 6) - (Vector128.Create(twoAbove.Slice(x, 4)) * 2) + (left * 7) + Vector128.Create(row.Slice(x - 2, 4)) + Vector128.Create(above.Slice(x + 2, 4)) + (Vector128.Create(above.Slice(x + 1, 4)) * 3) + Vector128.Create(8);
            return Vector128.ShiftRightArithmetic(sum + (Vector128.ShiftRightArithmetic(sum, 31) & Vector128.Create(15)), 4);
        }
    }

    /// <summary>
    /// Gets a value indicating whether every sample is below 2^26 in magnitude: the sums of the predictors then fit in 32 bits.
    /// </summary>
    private static bool HasSmallSamples(ReadOnlySpan<int> samples)
    {
        const int Limit = 1 << 26;
        foreach (var sample in samples)
        {
            if (sample is >= Limit or <= -Limit)
                return false;
        }

        return true;
    }

    /// <summary>The residuals of the samples <paramref name="start"/> to <paramref name="end"/> of a row, without the weighted predictor.</summary>
    private static void ComputeRowResidualsScalar(ReadOnlySpan<int> row, ReadOnlySpan<int> above, ReadOnlySpan<int> twoAbove, int y, int predictor, Span<uint> output, int start, int end)
    {
        var width = row.Length;
        for (var x = start; x < end; x++)
        {
            long left = x > 0 ? row[x - 1] : y > 0 ? above[x] : 0;
            long top = y > 0 ? above[x] : left;
            long topLeft = x > 0 && y > 0 ? above[x - 1] : left;
            long prediction;
            if (predictor == GradientPredictor)
            {
                prediction = JpegXlModularTransform.ClampedGradient((int)left, (int)top, (int)topLeft);
            }
            else
            {
                long topRight = x + 1 < width && y > 0 ? above[x + 1] : top;
                long leftLeft = x > 1 ? row[x - 2] : left;
                long topTop = y > 1 ? twoAbove[x] : top;
                long topRightRight = x + 2 < width && y > 0 ? above[x + 2] : topRight;
                prediction = JpegXlModularTransform.Predict(predictor, left, top, topTop, topLeft, topRight, leftLeft, topRightRight, 0);
            }

            var residual = unchecked(row[x] - (int)prediction);
            output[x] = (uint)((residual << 1) ^ (residual >> 31));
        }
    }

    /// <summary>The scalar reference of <see cref="ComputeResiduals"/>, and the computation of the weighted predictor.</summary>
    internal static void ComputeResidualsScalar(JpegXlModularChannel channel, int predictor, Span<uint> residuals, AllocationScope scope)
    {
        var width = channel.Width;
        var height = channel.Height;
        using var weighted = predictor == WeightedPredictor ? new JpegXlWeightedPredictor(JpegXlWeightedPredictorHeader.Default, width, scope) : null;
        for (var y = 0; y < height; y++)
        {
            var row = channel.GetRow(y);
            var above = y > 0 ? channel.GetRow(y - 1) : default;
            var twoAbove = y > 1 ? channel.GetRow(y - 2) : default;
            var output = residuals.Slice(y * width, width);
            for (var x = 0; x < width; x++)
            {
                // The neighbors that do not exist are replaced as the decoder replaces them
                long left = x > 0 ? row[x - 1] : y > 0 ? above[x] : 0;
                long top = y > 0 ? above[x] : left;
                long topLeft = x > 0 && y > 0 ? above[x - 1] : left;
                long prediction;
                if (predictor == GradientPredictor)
                {
                    prediction = JpegXlModularTransform.ClampedGradient((int)left, (int)top, (int)topLeft);
                }
                else
                {
                    long topRight = x + 1 < width && y > 0 ? above[x + 1] : top;
                    long leftLeft = x > 1 ? row[x - 2] : left;
                    long topTop = y > 1 ? twoAbove[x] : top;
                    long topRightRight = x + 2 < width && y > 0 ? above[x + 2] : topRight;
                    long weightedPrediction = 0;
                    if (weighted is not null)
                    {
                        weightedPrediction = weighted.Predict(x, y, top, left, topRight, topLeft, topTop, out _);
                    }

                    prediction = JpegXlModularTransform.Predict(predictor, left, top, topTop, topLeft, topRight, leftLeft, topRightRight, weightedPrediction);
                }

                var residual = unchecked(row[x] - (int)prediction);
                output[x] = (uint)((residual << 1) ^ (residual >> 31));
                weighted?.Update(row[x], x, y);
            }
        }
    }

    /// <summary>What the trees of <see cref="EncodeWithLearnedTree"/> are learned with.</summary>
    /// <param name="Properties">The properties the decisions test; those of the previous channels (from 16) are only offered to a channel that has them.</param>
    /// <param name="Predictors">The predictors of the leaves.</param>
    /// <param name="SamplesPerChannel">The largest number of samples a tree is learned from (rows are skipped evenly beyond it), or 0 for every sample.</param>
    internal sealed record LearnedTreeSettings(int[] Properties, int[] Predictors, int SamplesPerChannel);
}
