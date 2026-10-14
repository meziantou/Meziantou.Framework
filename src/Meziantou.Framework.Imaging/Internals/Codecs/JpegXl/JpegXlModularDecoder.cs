namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes a Modular sub-bitstream (ISO/IEC 18181-1, section H.3): its header (whether the global tree is used, the
/// parameters of the weighted predictor, the transforms), a local tree when it has one, then the samples of each channel,
/// each predicted from its neighbors and coded with the context its properties select in the tree.
/// </summary>
internal static class JpegXlModularDecoder
{
    private const int StaticPropertyCount = 2;
    private const int FirstReferenceProperty = 16;
    private const int PropertiesPerReference = 4;
    private const int MaxPropertyCount = 256;

    /// <summary>Reads a sub-bitstream into the channels of <paramref name="image"/>.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="image">The image; its channels are those before any transform.</param>
    /// <param name="streamIndex">The index of the sub-bitstream in the frame, which is a property of its samples.</param>
    /// <param name="globalTree">The tree of the frame, or <see langword="null"/> when it has none.</param>
    /// <param name="maxChannelSize">
    /// The side above which a channel of the image is not coded here: the channels that follow, in order, the first one
    /// that is larger are coded in the groups of the frame.
    /// </param>
    /// <param name="undoTransforms">Whether the transforms are undone once the channels are decoded.</param>
    /// <param name="scope">The scope of the samples and of the working memory.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    public static void Decode(JpegXlBitReader reader, JpegXlModularImage image, int streamIndex, JpegXlMaTree? globalTree, int maxChannelSize, bool undoTransforms, AllocationScope scope, CancellationToken cancellationToken)
    {
        if (image.Channels.Count == 0)
            return;

        var useGlobalTree = reader.ReadBool();
        image.WeightedPredictor = JpegXlWeightedPredictorHeader.Read(reader);
        var transformCount = (int)reader.ReadU32(0, 0, 1, 0, 2, 4, 18, 8);
        for (var i = 0; i < transformCount; i++)
        {
            var transform = JpegXlModularTransform.Read(reader);
            reader.EnsureNotOverrun();
            transform.ApplyToChannelList(image);
            image.Transforms.Add(transform);
        }

        // The channels coded here: every meta-channel, then the channels of the image up to the first one that is too large
        var codedChannelCount = 0;
        long pixelCount = 0;
        uint distanceMultiplier = 0;
        var anyChannel = false;
        for (var i = 0; i < image.Channels.Count; i++)
        {
            var channel = image.Channels[i];
            if (i >= image.MetaChannelCount && (channel.Width > maxChannelSize || channel.Height > maxChannelSize))
                break;

            codedChannelCount = i + 1;
            if (channel.IsEmpty)
                continue;

            anyChannel = true;
            pixelCount += (long)channel.Width * channel.Height;
            distanceMultiplier = Math.Max(distanceMultiplier, (uint)channel.Width);
        }

        foreach (var channel in image.Channels)
        {
            channel.Allocate(scope);
        }

        if (anyChannel)
        {
            JpegXlMaTree? localTree = null;
            try
            {
                JpegXlMaTree tree;
                if (useGlobalTree)
                {
                    tree = globalTree ?? throw JpegXlFormat.Invalid("a Modular sub-bitstream uses the global tree of a frame that has none.");
                }
                else
                {
                    tree = localTree = JpegXlMaTree.Read(reader, scope, Math.Min(1 << 20, 1024 + pixelCount));
                }

                using var decoder = new JpegXlEntropyDecoder(tree.Code!, scope);
                decoder.Begin(reader);
                using var work = Av1PooledArray<JpegXlMaTreeNode>.Rent(scope, tree.NodeCount, clear: false);
                using var pending = Av1PooledArray<int>.Rent(scope, tree.NodeCount, clear: false);
                for (var i = 0; i < codedChannelCount; i++)
                {
                    if (image.Channels[i].IsEmpty)
                        continue;

                    DecodeChannel(reader, decoder, tree, work.Span, pending.Span, image, i, streamIndex, distanceMultiplier, scope, cancellationToken);
                    reader.EnsureNotOverrun();
                }

                decoder.EnsureFinalState();
            }
            finally
            {
                localTree?.Dispose();
            }
        }

        if (undoTransforms)
        {
            UndoTransforms(image, scope);
        }
    }

    /// <summary>Undoes the transforms of an image, last to first.</summary>
    public static void UndoTransforms(JpegXlModularImage image, AllocationScope scope)
    {
        for (var i = image.Transforms.Count - 1; i >= 0; i--)
        {
            image.Transforms[i].Undo(image, scope);
        }

        image.Transforms.Clear();
    }

    private static void DecodeChannel(
        JpegXlBitReader reader,
        JpegXlEntropyDecoder decoder,
        JpegXlMaTree tree,
        Span<JpegXlMaTreeNode> nodes,
        Span<int> pending,
        JpegXlModularImage image,
        int channelIndex,
        int streamIndex,
        uint distanceMultiplier,
        AllocationScope scope,
        CancellationToken cancellationToken)
    {
        // The decisions on the channel and on the sub-bitstream are the same for every sample: they are made once
        var nodeCount = Specialize(tree.Nodes, nodes, pending, channelIndex, streamIndex);
        nodes = nodes[..nodeCount];
        var usesWeighted = false;
        var maxProperty = FirstReferenceProperty - 1;
        foreach (ref readonly var node in nodes)
        {
            if (node.IsLeaf)
            {
                usesWeighted |= node.RightOrPredictor == 6;
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
        if (nodes[0].IsLeaf && !usesWeighted)
        {
            DecodeSingleLeaf(reader, decoder, channel, nodes[0], distanceMultiplier, cancellationToken);
            return;
        }

        // The previous channels of the same size are the source of the properties from 16 on, four per channel, nearest first
        var referenceCount = (maxProperty + 1 - FirstReferenceProperty + PropertiesPerReference - 1) / PropertiesPerReference;
        Span<int> references = stackalloc int[referenceCount];
        var found = 0;
        for (var i = channelIndex - 1; i >= 0 && found < referenceCount; i--)
        {
            var other = image.Channels[i];
            if (other.Width == width && other.Height == height && other.HorizontalShift == channel.HorizontalShift && other.VerticalShift == channel.VerticalShift)
            {
                references[found++] = i;
            }
        }

        references = references[..found];

        // Every property index is below 256: with a constant length, the writes of the properties are not checked
        Span<int> properties = stackalloc int[MaxPropertyCount];
        properties.Clear();
        properties[0] = channelIndex;
        properties[1] = streamIndex;
        using var weighted = usesWeighted ? new JpegXlWeightedPredictor(image.WeightedPredictor, width, scope) : null;
        var isSingleLeaf = nodes[0].IsLeaf;

        // The properties of the previous channels are computed a row at a time, all the references of a column together
        var referenceStride = found * PropertiesPerReference;
        using var referenceProperties = found > 0 && !isSingleLeaf ? Av1PooledArray<int>.Rent(scope, (long)width * referenceStride, clear: false) : null;
        var samples = channel.Samples;
        for (var y = 0; y < height; y++)
        {
            if ((y & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reader.EnsureNotOverrun();
            }

            var row = samples.Slice(y * width, width);
            var above = y > 0 ? samples.Slice((y - 1) * width, width) : default;

            // The row two above the first row is the row above: the sample two above is then the sample above, as at the edge
            var twoAbove = y > 1 ? samples.Slice((y - 2) * width, width) : above;
            properties[2] = y;
            properties[9] = 0;
            Span<int> rowReferences = default;
            if (referenceProperties is not null)
            {
                rowReferences = referenceProperties.Span;
                for (var i = 0; i < references.Length; i++)
                {
                    ComputeReferenceProperties(image.Channels[references[i]], y, rowReferences[(i * PropertiesPerReference)..], referenceStride);
                }
            }

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
                    if (referenceStride != 0)
                    {
                        rowReferences.Slice(x * referenceStride, referenceStride).CopyTo(properties[FirstReferenceProperty..]);
                    }

                    while (!nodes[leafIndex].IsLeaf)
                    {
                        ref readonly var decision = ref nodes[leafIndex];
                        leafIndex = properties[decision.Property] > decision.ValueOrOffset ? decision.LeftOrContext : decision.RightOrPredictor;
                    }
                }

                ref readonly var leaf = ref nodes[leafIndex];
                var guess = leaf.ValueOrOffset + JpegXlModularTransform.Predict(leaf.RightOrPredictor, left, top, topTop, topLeft, topRight, leftLeft, topRightRight, weightedPrediction);
                var sample = Sample(decoder.Read(reader, leaf.LeftOrContext, distanceMultiplier), leaf.Multiplier, guess);
                row[x] = sample;
                weighted?.Update(sample, x, y);
            }
        }
    }

    /// <summary>
    /// Computes the four properties that a previous channel gives each sample of a row: its value, and its difference with
    /// the gradient of its own neighbors, each with its magnitude. They are written every <paramref name="stride"/> values.
    /// </summary>
    private static void ComputeReferenceProperties(JpegXlModularChannel other, int y, Span<int> destination, int stride)
    {
        var width = other.Width;
        var samples = other.Samples;
        var row = samples.Slice(y * width, width);
        var above = y > 0 ? samples.Slice((y - 1) * width, width) : default;
        for (var x = 0; x < width; x++)
        {
            long value = row[x];
            long otherLeft = x > 0 ? row[x - 1] : 0;
            long otherTop = y > 0 ? above[x] : otherLeft;
            long otherTopLeft = x > 0 && y > 0 ? above[x - 1] : otherLeft;
            long predicted = JpegXlModularTransform.ClampedGradient((int)otherLeft, (int)otherTop, (int)otherTopLeft);
            var offset = x * stride;
            destination[offset] = (int)Math.Abs(value);
            destination[offset + 1] = (int)value;
            destination[offset + 2] = (int)Math.Abs(value - predicted);
            destination[offset + 3] = (int)(value - predicted);
        }
    }

    /// <summary>
    /// Decodes a channel whose samples all reach the same leaf, which does not use the weighted predictor: no property is
    /// computed, and the rows below the first one have a loop of their own for the predictors that only read the samples on
    /// the left and above. The samples, and the values read from the stream, are those of the general loop.
    /// </summary>
    private static void DecodeSingleLeaf(JpegXlBitReader reader, JpegXlEntropyDecoder decoder, JpegXlModularChannel channel, JpegXlMaTreeNode leaf, uint distanceMultiplier, CancellationToken cancellationToken)
    {
        var width = channel.Width;
        var height = channel.Height;
        var samples = channel.Samples;
        var context = leaf.LeftOrContext;
        var predictor = leaf.RightOrPredictor;
        long offset = leaf.ValueOrOffset;
        long multiplier = leaf.Multiplier;
        for (var y = 0; y < height; y++)
        {
            if ((y & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reader.EnsureNotOverrun();
            }

            var row = samples.Slice(y * width, width);
            if (y == 0 || predictor > 5)
            {
                DecodeRow(reader, decoder, samples, width, y, context, predictor, offset, multiplier, distanceMultiplier);
                continue;
            }

            // The first sample of a row has the sample above as its left, top and top-left neighbors
            var above = samples.Slice((y - 1) * width, width);
            switch (predictor)
            {
                case 0:
                    for (var x = 0; x < width; x++)
                    {
                        row[x] = Sample(decoder.Read(reader, context, distanceMultiplier), multiplier, offset);
                    }

                    break;

                case 1:
                    row[0] = Sample(decoder.Read(reader, context, distanceMultiplier), multiplier, offset + above[0]);
                    for (var x = 1; x < width; x++)
                    {
                        row[x] = Sample(decoder.Read(reader, context, distanceMultiplier), multiplier, offset + row[x - 1]);
                    }

                    break;

                case 2:
                    for (var x = 0; x < width; x++)
                    {
                        row[x] = Sample(decoder.Read(reader, context, distanceMultiplier), multiplier, offset + above[x]);
                    }

                    break;

                case 3:
                    row[0] = Sample(decoder.Read(reader, context, distanceMultiplier), multiplier, offset + (((long)above[0] + above[0]) / 2));
                    for (var x = 1; x < width; x++)
                    {
                        row[x] = Sample(decoder.Read(reader, context, distanceMultiplier), multiplier, offset + (((long)row[x - 1] + above[x]) / 2));
                    }

                    break;

                case 4:
                    row[0] = Sample(decoder.Read(reader, context, distanceMultiplier), multiplier, offset + JpegXlModularTransform.Predict(4, above[0], above[0], 0, above[0], 0, 0, 0, 0));
                    for (var x = 1; x < width; x++)
                    {
                        row[x] = Sample(decoder.Read(reader, context, distanceMultiplier), multiplier, offset + JpegXlModularTransform.Predict(4, row[x - 1], above[x], 0, above[x - 1], 0, 0, 0, 0));
                    }

                    break;

                default:
                    row[0] = Sample(decoder.Read(reader, context, distanceMultiplier), multiplier, offset + JpegXlModularTransform.ClampedGradient(above[0], above[0], above[0]));
                    for (var x = 1; x < width; x++)
                    {
                        row[x] = Sample(decoder.Read(reader, context, distanceMultiplier), multiplier, offset + JpegXlModularTransform.ClampedGradient(row[x - 1], above[x], above[x - 1]));
                    }

                    break;
            }
        }

        static void DecodeRow(JpegXlBitReader reader, JpegXlEntropyDecoder decoder, Span<int> samples, int width, int y, int context, int predictor, long offset, long multiplier, uint distanceMultiplier)
        {
            var row = samples.Slice(y * width, width);
            var above = y > 0 ? samples.Slice((y - 1) * width, width) : default;
            var twoAbove = y > 1 ? samples.Slice((y - 2) * width, width) : default;
            for (var x = 0; x < width; x++)
            {
                long left = x > 0 ? row[x - 1] : y > 0 ? above[x] : 0;
                long top = y > 0 ? above[x] : left;
                long topLeft = x > 0 && y > 0 ? above[x - 1] : left;
                long topRight = x + 1 < width && y > 0 ? above[x + 1] : top;
                long leftLeft = x > 1 ? row[x - 2] : left;
                long topTop = y > 1 ? twoAbove[x] : top;
                long topRightRight = x + 2 < width && y > 0 ? above[x + 2] : topRight;
                var guess = offset + JpegXlModularTransform.Predict(predictor, left, top, topTop, topLeft, topRight, leftLeft, topRightRight, 0);
                row[x] = Sample(decoder.Read(reader, context, distanceMultiplier), multiplier, guess);
            }
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int Sample(uint token, long multiplier, long guess) => (int)(((long)JpegXlBitReader.UnpackSigned(token) * multiplier) + guess);

    /// <summary>
    /// Copies the part of a tree that the samples of one channel of one sub-bitstream can reach: the decisions on the two
    /// properties that are constant for them (the channel and the sub-bitstream) are replaced with the child they select.
    /// </summary>
    /// <returns>The number of nodes of the copy.</returns>
    private static int Specialize(ReadOnlySpan<JpegXlMaTreeNode> source, Span<JpegXlMaTreeNode> destination, Span<int> pending, int channelIndex, int streamIndex)
    {
        var count = 1;
        pending[0] = Resolve(source, 0, channelIndex, streamIndex);
        for (var i = 0; i < count; i++)
        {
            var node = source[pending[i]];
            if (!node.IsLeaf)
            {
                var left = count++;
                var right = count++;
                pending[left] = Resolve(source, node.LeftOrContext, channelIndex, streamIndex);
                pending[right] = Resolve(source, node.RightOrPredictor, channelIndex, streamIndex);
                node.LeftOrContext = left;
                node.RightOrPredictor = right;
            }

            destination[i] = node;
        }

        return count;

        static int Resolve(ReadOnlySpan<JpegXlMaTreeNode> source, int index, int channelIndex, int streamIndex)
        {
            while (true)
            {
                ref readonly var node = ref source[index];
                if (node.IsLeaf || node.Property >= StaticPropertyCount)
                    return index;

                var value = node.Property == 0 ? channelIndex : streamIndex;
                index = value > node.ValueOrOffset ? node.LeftOrContext : node.RightOrPredictor;
            }
        }
    }
}
