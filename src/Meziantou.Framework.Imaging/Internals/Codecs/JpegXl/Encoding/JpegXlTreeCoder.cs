namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What the encoder computes for each sample of a Modular channel as the decoder does (ISO/IEC 18181-1, section H.4): the
/// properties of the samples a tree is learned from (<see cref="Sample"/>), and the context and the residual of every sample
/// once the tree is known (<see cref="Collect"/>). The neighbors, the properties and the predictions are those of
/// <see cref="JpegXlModularDecoder"/>, whose helpers are reused.
/// </summary>
internal static class JpegXlTreeCoder
{
    private const int FirstReferenceProperty = 16;
    private const int PropertiesPerReference = 4;
    private const int WeightedPredictor = 6;

    /// <summary>
    /// Adds the samples of some rows of a channel to a set: the values of the properties of the set and the tokens of the
    /// residuals of its predictors.
    /// </summary>
    /// <param name="image">The channels of the sub-bitstream.</param>
    /// <param name="channelIndex">The channel.</param>
    /// <param name="streamIndex">The index of the sub-bitstream.</param>
    /// <param name="set">The set.</param>
    /// <param name="rowStep">The distance between two rows that are sampled.</param>
    /// <param name="rowPhase">The first row that is sampled.</param>
    /// <param name="scope">The scope of the working memory.</param>
    public static void Sample(JpegXlModularImage image, int channelIndex, int streamIndex, JpegXlTreeLearner.SampleSet set, int rowStep, int rowPhase, AllocationScope scope)
    {
        var channel = image.Channels[channelIndex];
        var width = channel.Width;
        var height = channel.Height;
        var propertyIds = set.PropertyIds;
        var predictors = set.Predictors;
        var usesWeighted = predictors.Contains(WeightedPredictor) || propertyIds.Contains(JpegXlMaTree.WeightedProperty);
        var maxProperty = FirstReferenceProperty - 1;
        foreach (var property in propertyIds)
        {
            maxProperty = Math.Max(maxProperty, property);
        }

        var references = FindReferences(image, channelIndex, maxProperty, out var referenceStride);
        using var referenceProperties = referenceStride != 0 ? Av1PooledArray<int>.Rent(scope, (long)width * referenceStride, clear: false) : null;
        using var weighted = usesWeighted ? new JpegXlWeightedPredictor(JpegXlWeightedPredictorHeader.Default, width, scope) : null;
        Span<int> properties = stackalloc int[256];
        properties.Clear();
        properties[0] = channelIndex;
        properties[1] = streamIndex;
        Span<int> values = stackalloc int[propertyIds.Length];
        Span<byte> tokens = stackalloc byte[predictors.Length];
        var samples = channel.Samples;
        for (var y = 0; y < height; y++)
        {
            var isSampled = y % rowStep == rowPhase;
            if (!isSampled && weighted is null)
                continue;

            var row = samples.Slice(y * width, width);
            var above = y > 0 ? samples.Slice((y - 1) * width, width) : default;
            var twoAbove = y > 1 ? samples.Slice((y - 2) * width, width) : above;
            properties[2] = y;
            properties[9] = 0;
            if (isSampled && referenceProperties is not null)
            {
                for (var i = 0; i < references.Length; i++)
                {
                    JpegXlModularDecoder.ComputeReferenceProperties(image.Channels[references[i]], y, referenceProperties.Span[(i * PropertiesPerReference)..], referenceStride);
                }
            }

            for (var x = 0; x < width; x++)
            {
                long left = x > 0 ? row[x - 1] : y > 0 ? above[x] : 0;
                long top = y > 0 ? above[x] : left;
                long topLeft = x > 0 && y > 0 ? above[x - 1] : left;
                long topRight = x + 1 < width && y > 0 ? above[x + 1] : top;
                long leftLeft = x > 1 ? row[x - 2] : left;
                long topTop = y > 1 ? twoAbove[x] : top;
                long topRightRight = x + 2 < width && y > 0 ? above[x + 2] : topRight;
                long weightedPrediction = 0;
                var weightedProperty = 0;
                if (weighted is not null)
                {
                    weightedPrediction = weighted.Predict(x, y, top, left, topRight, topLeft, topTop, out weightedProperty);
                }

                if (isSampled)
                {
                    properties[3] = x;
                    properties[4] = (int)Math.Abs(top);
                    properties[5] = (int)Math.Abs(left);
                    properties[6] = (int)top;
                    properties[7] = (int)left;
                    properties[8] = (int)(left - properties[9]);
                    properties[9] = (int)(left + top - topLeft);
                    properties[10] = (int)(left - topLeft);
                    properties[11] = (int)(topLeft - top);
                    properties[12] = (int)(top - topRight);
                    properties[13] = (int)(top - topTop);
                    properties[14] = (int)(left - leftLeft);
                    properties[15] = weightedProperty;
                    if (referenceProperties is not null)
                    {
                        referenceProperties.Span.Slice(x * referenceStride, referenceStride).CopyTo(properties[FirstReferenceProperty..]);
                    }

                    for (var i = 0; i < propertyIds.Length; i++)
                    {
                        values[i] = properties[propertyIds[i]];
                    }

                    for (var i = 0; i < predictors.Length; i++)
                    {
                        var prediction = predictors[i] switch
                        {
                            0 => 0,
                            5 => JpegXlModularTransform.ClampedGradient((int)left, (int)top, (int)topLeft),
                            WeightedPredictor => weightedPrediction,
                            var other => JpegXlModularTransform.Predict(other, left, top, topTop, topLeft, topRight, leftLeft, topRightRight, weightedPrediction),
                        };
                        tokens[i] = JpegXlTreeLearner.GetToken(PackSigned(row[x] - prediction));
                    }

                    set.Add(values, tokens);
                }

                weighted?.Update(row[x], x, y);
            }
        }
    }

    /// <summary>
    /// Computes the context and the residual of every sample of a channel with a tree, in the order of the samples, as
    /// <c>context &lt;&lt; 32 | residual</c>.
    /// </summary>
    /// <param name="image">The channels of the sub-bitstream.</param>
    /// <param name="channelIndex">The channel.</param>
    /// <param name="streamIndex">The index of the sub-bitstream.</param>
    /// <param name="tree">The nodes of the tree, as the decoder reads them.</param>
    /// <param name="output">Where the values are written: one per sample.</param>
    /// <param name="work">Scratch memory of the length of the tree.</param>
    /// <param name="pending">Scratch memory of the length of the tree.</param>
    /// <param name="residuals">Scratch memory of the length of the channel.</param>
    /// <param name="scope">The scope of the working memory.</param>
    public static void Collect(JpegXlModularImage image, int channelIndex, int streamIndex, ReadOnlySpan<JpegXlMaTreeNode> tree, Span<ulong> output, Span<JpegXlMaTreeNode> work, Span<int> pending, Span<uint> residuals, AllocationScope scope)
    {
        var nodeCount = JpegXlModularDecoder.Specialize(tree, work, pending, channelIndex, streamIndex);
        var nodes = work[..nodeCount];
        var usesWeighted = false;
        var maxProperty = FirstReferenceProperty - 1;
        foreach (ref readonly var node in nodes)
        {
            if (node.IsLeaf)
            {
                usesWeighted |= node.RightOrPredictor == WeightedPredictor;
            }
            else
            {
                usesWeighted |= node.Property == JpegXlMaTree.WeightedProperty;
                maxProperty = Math.Max(maxProperty, node.Property);
            }
        }

        var channel = image.Channels[channelIndex];
        var width = channel.Width;
        var height = channel.Height;
        var samples = channel.Samples;
        if (nodes[0].IsLeaf && !usesWeighted && nodes[0].ValueOrOffset == 0)
        {
            // One leaf without the weighted predictor: the residuals of a predictor, computed four samples at a time
            var values = residuals[..samples.Length];
            JpegXlModularEncoder.ComputeResiduals(channel, nodes[0].RightOrPredictor, values, scope);
            var context = (ulong)nodes[0].LeftOrContext << 32;
            for (var i = 0; i < values.Length; i++)
            {
                output[i] = context | values[i];
            }

            return;
        }

        var references = FindReferences(image, channelIndex, maxProperty, out var referenceStride);
        using var referenceProperties = referenceStride != 0 ? Av1PooledArray<int>.Rent(scope, (long)width * referenceStride, clear: false) : null;
        using var weighted = usesWeighted ? new JpegXlWeightedPredictor(image.WeightedPredictor, width, scope) : null;
        Span<int> properties = stackalloc int[256];
        properties.Clear();
        properties[0] = channelIndex;
        properties[1] = streamIndex;
        var isSingleLeaf = nodes[0].IsLeaf;
        for (var y = 0; y < height; y++)
        {
            var row = samples.Slice(y * width, width);
            var above = y > 0 ? samples.Slice((y - 1) * width, width) : default;
            var twoAbove = y > 1 ? samples.Slice((y - 2) * width, width) : above;
            properties[2] = y;
            properties[9] = 0;
            if (referenceProperties is not null && !isSingleLeaf)
            {
                for (var i = 0; i < references.Length; i++)
                {
                    JpegXlModularDecoder.ComputeReferenceProperties(image.Channels[references[i]], y, referenceProperties.Span[(i * PropertiesPerReference)..], referenceStride);
                }
            }

            var outputRow = output.Slice(y * width, width);
            for (var x = 0; x < width; x++)
            {
                long left;
                long top;
                long topLeft;
                long topRight;
                long leftLeft;
                long topTop;
                long topRightRight;
                if (y > 0 && x > 1 && x + 2 < width)
                {
                    left = row[x - 1];
                    top = above[x];
                    topLeft = above[x - 1];
                    topRight = above[x + 1];
                    leftLeft = row[x - 2];
                    topTop = twoAbove[x];
                    topRightRight = above[x + 2];
                }
                else
                {
                    left = x > 0 ? row[x - 1] : y > 0 ? above[x] : 0;
                    top = y > 0 ? above[x] : left;
                    topLeft = x > 0 && y > 0 ? above[x - 1] : left;
                    topRight = x + 1 < width && y > 0 ? above[x + 1] : top;
                    leftLeft = x > 1 ? row[x - 2] : left;
                    topTop = y > 1 ? twoAbove[x] : top;
                    topRightRight = x + 2 < width && y > 0 ? above[x + 2] : topRight;
                }

                long weightedPrediction = 0;
                var weightedProperty = 0;
                if (weighted is not null)
                {
                    weightedPrediction = weighted.Predict(x, y, top, left, topRight, topLeft, topTop, out weightedProperty);
                }

                var leafIndex = 0;
                if (!isSingleLeaf)
                {
                    properties[3] = x;
                    properties[4] = (int)Math.Abs(top);
                    properties[5] = (int)Math.Abs(left);
                    properties[6] = (int)top;
                    properties[7] = (int)left;
                    properties[8] = (int)(left - properties[9]);
                    properties[9] = (int)(left + top - topLeft);
                    properties[10] = (int)(left - topLeft);
                    properties[11] = (int)(topLeft - top);
                    properties[12] = (int)(top - topRight);
                    properties[13] = (int)(top - topTop);
                    properties[14] = (int)(left - leftLeft);
                    properties[15] = weightedProperty;
                    if (referenceProperties is not null)
                    {
                        referenceProperties.Span.Slice(x * referenceStride, referenceStride).CopyTo(properties[FirstReferenceProperty..]);
                    }

                    while (!nodes[leafIndex].IsLeaf)
                    {
                        ref readonly var decision = ref nodes[leafIndex];
                        leafIndex = properties[decision.Property] > decision.ValueOrOffset ? decision.LeftOrContext : decision.RightOrPredictor;
                    }
                }

                ref readonly var leaf = ref nodes[leafIndex];
                var predictor = leaf.RightOrPredictor;
                var prediction = predictor switch
                {
                    0 => 0,
                    5 => JpegXlModularTransform.ClampedGradient((int)left, (int)top, (int)topLeft),
                    WeightedPredictor => weightedPrediction,
                    _ => JpegXlModularTransform.Predict(predictor, left, top, topTop, topLeft, topRight, leftLeft, topRightRight, weightedPrediction),
                };
                var residual = row[x] - (leaf.ValueOrOffset + prediction);
                outputRow[x] = ((ulong)leaf.LeftOrContext << 32) | PackSigned(residual);
                weighted?.Update(row[x], x, y);
            }
        }
    }

    /// <summary>
    /// Flattens a learned tree into the nodes the decoder reads, breadth first: the children of a decision come after every
    /// node already listed, and the leaves are numbered in that order (their context).
    /// </summary>
    public static JpegXlMaTreeNode[] Flatten(JpegXlTreeLearner.TreeNode root, out int leafCount)
    {
        var nodes = new List<JpegXlMaTreeNode>();
        var queue = new Queue<JpegXlTreeLearner.TreeNode>();
        queue.Enqueue(root);
        leafCount = 0;
        var announced = 1;
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node.IsLeaf)
            {
                nodes.Add(new JpegXlMaTreeNode { Property = -1, ValueOrOffset = 0, LeftOrContext = leafCount++, RightOrPredictor = node.Predictor, Multiplier = 1 });
                continue;
            }

            nodes.Add(new JpegXlMaTreeNode { Property = node.Property, ValueOrOffset = node.Value, LeftOrContext = announced, RightOrPredictor = announced + 1, Multiplier = 1 });
            announced += 2;
            queue.Enqueue(node.Left!);
            queue.Enqueue(node.Right!);
        }

        return [.. nodes];
    }

    /// <summary>Writes the nodes of a tree, breadth first, with the entropy code of the format for trees.</summary>
    public static void WriteTree(LsbBitWriter writer, ReadOnlySpan<JpegXlMaTreeNode> nodes, AllocationScope scope)
    {
        const int SplitValueContext = 0;
        const int PropertyContext = 1;
        const int PredictorContext = 2;
        const int OffsetContext = 3;
        const int MultiplierLogContext = 4;
        const int MultiplierBitsContext = 5;

        using var stream = new JpegXlEntropyEncoder(JpegXlMaTree.TreeContextCount, new JpegXlHybridUintConfig(4, 0, 0), scope, JpegXlEntropyCoding.Smallest);
        foreach (ref readonly var node in nodes)
        {
            if (node.IsLeaf)
            {
                stream.Add(PropertyContext, 0);
                stream.Add(PredictorContext, (uint)node.RightOrPredictor);
                stream.Add(OffsetContext, PackSigned(node.ValueOrOffset));
                stream.Add(MultiplierLogContext, 0);
                stream.Add(MultiplierBitsContext, 0);
            }
            else
            {
                stream.Add(PropertyContext, (uint)(node.Property + 1));
                stream.Add(SplitValueContext, PackSigned(node.ValueOrOffset));
            }
        }

        stream.Write(writer);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static uint PackSigned(long value)
    {
        var residual = unchecked((int)value);
        return (uint)((residual << 1) ^ (residual >> 31));
    }

    /// <summary>The previous channels of the same size, nearest first, that give a sample the properties from 16 on.</summary>
    private static int[] FindReferences(JpegXlModularImage image, int channelIndex, int maxProperty, out int referenceStride)
    {
        var channel = image.Channels[channelIndex];
        var referenceCount = (maxProperty + 1 - FirstReferenceProperty + PropertiesPerReference - 1) / PropertiesPerReference;
        var references = new List<int>();
        for (var i = channelIndex - 1; i >= 0 && references.Count < referenceCount; i--)
        {
            var other = image.Channels[i];
            if (other.Width == channel.Width && other.Height == channel.Height && other.HorizontalShift == channel.HorizontalShift && other.VerticalShift == channel.VerticalShift)
            {
                references.Add(i);
            }
        }

        referenceStride = references.Count * PropertiesPerReference;
        return [.. references];
    }
}
