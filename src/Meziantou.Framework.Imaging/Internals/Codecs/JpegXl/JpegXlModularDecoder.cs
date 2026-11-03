using System.Runtime.Intrinsics;

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
    private const int MaxLeafTableLength = 2048;

    // The most properties a table of the general loop combines: the loop and the table keep fewer values in the 16 general
    // registers of x64, where a table of more properties is slower than the walk (Windows, Zen 5: +6 to +12% with 12
    // properties, -19% with 6 to 11)
    private static readonly int MaxGeneralTablePropertyCount = System.Runtime.Intrinsics.X86.X86Base.IsSupported ? 10 : 16;

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
        ReadTransforms(reader, image, cancellationToken);

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

    /// <summary>
    /// Reads the transforms of a sub-bitstream and changes the channels of <paramref name="image"/> to those that are coded.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">The transforms add too many channels (see <see cref="JpegXlModularImage"/>).</exception>
    public static void ReadTransforms(JpegXlBitReader reader, JpegXlModularImage image, CancellationToken cancellationToken)
    {
        var transformCount = (int)reader.ReadU32(0, 0, 1, 0, 2, 4, 18, 8);
        for (var i = 0; i < transformCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transform = JpegXlModularTransform.Read(reader);
            reader.EnsureNotOverrun();
            transform.ApplyToChannelList(image);
            image.Transforms.Add(transform);
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
        var usedProperties = 0;
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
                usedProperties |= node.Property < 16 ? 1 << node.Property : 0;
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

        // A tree whose decisions all test one property of the sample and its neighbors is a function of that property: a
        // table gives the leaf of each value between the smallest split value and the largest one plus one, which also
        // stand for the values below and above them
        Span<int> leafTable = isSingleLeaf ? default : stackalloc int[MaxLeafTableLength];
        var leaves = leafTable[..FillLeafTable(nodes, leafTable, out var tableProperty, out var tableLow)];
        var samples = channel.Samples;
        if (weighted is not null && !isSingleLeaf && TryDecodeWeighted(reader, decoder, weighted, nodes, leaves, tableProperty, tableLow, channel, distanceMultiplier, scope, cancellationToken))
            return;

        if (weighted is null && !leaves.IsEmpty && tableProperty != 8)
        {
            DecodeLeafTable(reader, decoder, nodes, leaves, tableProperty, tableLow, channel, distanceMultiplier, cancellationToken);
            return;
        }

        // A tree of several properties is evaluated without a branch per decision when it can be a table: the decisions on
        // the samples are not predictable
        using var tableBuffer = leaves.IsEmpty && !isSingleLeaf && JpegXlMaTreeTable.IsCandidate(nodes, anyProperty: true, MaxGeneralTablePropertyCount) ? Av1PooledArray<ulong>.Rent(scope, JpegXlMaTreeTable.BufferLength, clear: false) : null;
        Span<JpegXlMaTreeTable.Slot> slots = stackalloc JpegXlMaTreeTable.Slot[JpegXlMaTreeTable.SlotBufferLength];
        scoped JpegXlMaTreeTable table = default;
        var hasTable = tableBuffer is not null && JpegXlMaTreeTable.TryCreate(nodes, decoder: null, tableBuffer.Span, slots, anyProperty: true, out table);

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
                if (!leaves.IsEmpty)
                {
                    var value = tableProperty switch
                    {
                        2 => y,
                        3 => x,
                        4 => (int)Math.Abs(top),
                        5 => (int)Math.Abs(left),
                        6 => (int)top,
                        7 => (int)left,
                        8 => (int)(left - properties[9]),
                        9 => (int)(left + top - topLeft),
                        10 => (int)(left - topLeft),
                        11 => (int)(topLeft - top),
                        12 => (int)(top - topRight),
                        13 => (int)(top - topTop),
                        14 => (int)(left - leftLeft),
                        _ => weightedProperty,
                    };

                    // The property 8 is the difference with the property 9 of the previous sample
                    properties[9] = (int)(left + top - topLeft);
                    leafIndex = leaves[(int)(Math.Clamp(value, tableLow, (long)tableLow + leaves.Length - 1) - tableLow)];
                }
                else if (!isSingleLeaf)
                {
                    // The groups of properties that no decision tests are not computed
                    if ((usedProperties & 0b1111_1000) != 0)
                    {
                        properties[3] = x;
                        properties[4] = (int)Math.Abs(top);
                        properties[5] = (int)Math.Abs(left);
                        properties[6] = (int)top;
                        properties[7] = (int)left;
                    }

                    properties[8] = (int)(left - properties[9]);
                    properties[9] = (int)(left + top - topLeft);
                    properties[10] = (int)(left - topLeft);
                    properties[11] = (int)(topLeft - top);
                    properties[12] = (int)(top - topRight);
                    if ((usedProperties & 0b0110_0000_0000_0000) != 0)
                    {
                        properties[13] = (int)(top - topTop);
                        properties[14] = (int)(left - leftLeft);
                    }

                    properties[15] = weightedProperty;
                    if (referenceStride != 0)
                    {
                        rowReferences.Slice(x * referenceStride, referenceStride).CopyTo(properties[FirstReferenceProperty..]);
                    }

                    if (hasTable)
                    {
                        leafIndex = table.GetCell(properties) & 0xFFFFFF;
                    }
                    else
                    {
                        while (!nodes[leafIndex].IsLeaf)
                        {
                            ref readonly var decision = ref nodes[leafIndex];
                            leafIndex = properties[decision.Property] > decision.ValueOrOffset ? decision.LeftOrContext : decision.RightOrPredictor;
                        }
                    }
                }

                ref readonly var leaf = ref nodes[leafIndex];

                // The predictors that most trees use (zero, the gradient and the weighted predictor) are not dispatched
                var predictor = leaf.RightOrPredictor;
                var prediction = predictor switch
                {
                    0 => 0,
                    5 => JpegXlModularTransform.ClampedGradient((int)left, (int)top, (int)topLeft),
                    6 => weightedPrediction,
                    _ => JpegXlModularTransform.Predict(predictor, left, top, topTop, topLeft, topRight, leftLeft, topRightRight, weightedPrediction),
                };
                var guess = leaf.ValueOrOffset + prediction;
                var sample = Sample(decoder.Read(reader, leaf.LeftOrContext, distanceMultiplier), leaf.Multiplier, guess);
                row[x] = sample;
                weighted?.Update(sample, x, y);
            }
        }
    }

    /// <summary>
    /// Decodes a channel that uses the weighted predictor with <see cref="DecodeWeighted"/> when its tree is a table of the
    /// property of the weighted predictor whose leaves all use that predictor, or can be evaluated by a
    /// <see cref="JpegXlMaTreeTable"/>.
    /// </summary>
    /// <returns><see langword="false"/> when the channel is left to the general loop.</returns>
    /// <remarks>A method of its own, so that the general loop keeps its registers.</remarks>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool TryDecodeWeighted(
        JpegXlBitReader reader,
        JpegXlEntropyDecoder decoder,
        JpegXlWeightedPredictor weighted,
        ReadOnlySpan<JpegXlMaTreeNode> nodes,
        Span<int> leaves,
        int tableProperty,
        int tableLow,
        JpegXlModularChannel channel,
        uint distanceMultiplier,
        AllocationScope scope,
        CancellationToken cancellationToken)
    {
        if (tableProperty == JpegXlMaTree.WeightedProperty && !leaves.IsEmpty && AllUseWeightedPredictor(nodes, leaves))
        {
            // The leaf of each value, with the cluster of its context in the high byte (a tree has fewer than 2^24 nodes, and a
            // stream fewer than 256 clusters)
            for (var i = 0; i < leaves.Length; i++)
            {
                leaves[i] |= decoder.GetCluster(nodes[leaves[i]].LeftOrContext) << 24;
            }

            var selector = new WeightedTableSelector(leaves, tableLow);
            DecodeWeighted(reader, decoder, weighted, nodes, in selector, channel, distanceMultiplier, cancellationToken);
            return true;
        }

        // A learned tree, which tests a few properties of the neighbors with a few split values each, is a table
        if (!JpegXlMaTreeTable.IsCandidate(nodes, anyProperty: false))
            return false;

        using var tableBuffer = Av1PooledArray<ulong>.Rent(scope, JpegXlMaTreeTable.BufferLength, clear: false);
        Span<JpegXlMaTreeTable.Slot> slots = stackalloc JpegXlMaTreeTable.Slot[JpegXlMaTreeTable.SlotBufferLength];
        if (!JpegXlMaTreeTable.TryCreate(nodes, decoder, tableBuffer.Span, slots, anyProperty: false, out var table))
            return false;

        var treeSelector = new TreeTableSelector(table);
        DecodeWeighted(reader, decoder, weighted, nodes, in treeSelector, channel, distanceMultiplier, cancellationToken);
        return true;
    }

    /// <summary>
    /// Decodes a channel whose leaf is selected through a table by one property of the sample and its neighbors (2 to 14, but
    /// the property 8, which depends on the previous sample), without the weighted predictor: the property is the only one
    /// computed, the gradient predictor takes no branch, and the values of an ANS stream without LZ77 are read with the state
    /// of the entropy decoder and of the bit reader in locals. The samples, and the values read from the stream, are those of
    /// the general loop.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void DecodeLeafTable(
        JpegXlBitReader reader,
        JpegXlEntropyDecoder decoder,
        ReadOnlySpan<JpegXlMaTreeNode> nodes,
        Span<int> leaves,
        int property,
        int low,
        JpegXlModularChannel channel,
        uint distanceMultiplier,
        CancellationToken cancellationToken)
    {
        var width = channel.Width;
        var height = channel.Height;
        var samples = channel.Samples;
        var inline = decoder.CanReadInline;

        // The leaf of each value, with the cluster of its context in the high byte (a tree has fewer than 2^24 nodes, and a
        // stream fewer than 256 clusters)
        for (var i = 0; i < leaves.Length; i++)
        {
            leaves[i] |= decoder.GetCluster(nodes[leaves[i]].LeftOrContext) << 24;
        }

        var high = (long)low + leaves.Length - 1;
        var tables = inline ? new JpegXlEntropyDecoder.InlineTables(decoder, reader) : default;
        for (var y = 0; y < height; y++)
        {
            if ((y & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reader.EnsureNotOverrun();
            }

            var row = samples.Slice(y * width, width);
            var above = y > 0 ? samples.Slice((y - 1) * width, width) : default;
            var twoAbove = y > 1 ? samples.Slice((y - 2) * width, width) : above;
            var ansState = decoder.AnsState;
            reader.GetState(out var bits, out var bitCount, out var position);
            for (var x = 0; x < width; x++)
            {
                long left;
                long top;
                long topLeft;
                long topRight;
                long leftLeft;
                long topTop;
                if (y > 0 && x > 1 && x + 1 < width)
                {
                    left = row[x - 1];
                    top = above[x];
                    topLeft = above[x - 1];
                    topRight = above[x + 1];
                    leftLeft = row[x - 2];
                    topTop = twoAbove[x];
                }
                else
                {
                    left = x > 0 ? row[x - 1] : y > 0 ? above[x] : 0;
                    top = y > 0 ? above[x] : left;
                    topLeft = x > 0 && y > 0 ? above[x - 1] : left;
                    topRight = x + 1 < width && y > 0 ? above[x + 1] : top;
                    leftLeft = x > 1 ? row[x - 2] : left;
                    topTop = y > 1 ? twoAbove[x] : top;
                }

                // The property does not change: the branch of the switch is predictable
                int value = property switch
                {
                    2 => y,
                    3 => x,
                    4 => (int)Math.Abs(top),
                    5 => (int)Math.Abs(left),
                    6 => (int)top,
                    7 => (int)left,
                    9 => (int)(left + top - topLeft),
                    10 => (int)(left - topLeft),
                    11 => (int)(topLeft - top),
                    12 => (int)(top - topRight),
                    13 => (int)(top - topTop),
                    _ => (int)(left - leftLeft),
                };
                var entry = leaves[(int)(Math.Min(Math.Max(value, low), high) - low)];
                ref readonly var leaf = ref nodes[entry & 0xFFFFFF];
                var token = inline ? tables.Read((int)((uint)entry >> 24), ref ansState, ref bits, ref bitCount, ref position) : decoder.Read(reader, leaf.LeftOrContext, distanceMultiplier);
                var predictor = leaf.RightOrPredictor;
                long guess;
                if (predictor == 5)
                {
                    guess = JpegXlModularTransform.ClampedGradientWithoutBranch(left, top, topLeft);
                }
                else
                {
                    var topRightRight = x + 2 < width && y > 0 ? above[x + 2] : topRight;
                    guess = JpegXlModularTransform.Predict(predictor, left, top, topTop, topLeft, topRight, leftLeft, topRightRight, 0);
                }

                row[x] = Sample(token, leaf.Multiplier, leaf.ValueOrOffset + guess);
            }

            if (inline)
            {
                decoder.AnsState = ansState;
                reader.SetState(bits, bitCount, position);
            }
        }
    }

    private static bool AllUseWeightedPredictor(ReadOnlySpan<JpegXlMaTreeNode> nodes, ReadOnlySpan<int> leaves)
    {
        foreach (var leaf in leaves)
        {
            if (nodes[leaf].RightOrPredictor != 6)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Decodes a channel that uses the weighted predictor and whose leaves a selector gives (a table of the leaves of the
    /// property of the weighted predictor, or a table of the whole tree): the predictor runs a row at a time. The samples,
    /// and the values read from the stream, are those of the general loop.
    /// </summary>
    private static void DecodeWeighted<TSelector>(
        JpegXlBitReader reader,
        JpegXlEntropyDecoder decoder,
        JpegXlWeightedPredictor weighted,
        ReadOnlySpan<JpegXlMaTreeNode> nodes,
        in TSelector selector,
        JpegXlModularChannel channel,
        uint distanceMultiplier,
        CancellationToken cancellationToken)
        where TSelector : struct, ILeafSelector, allows ref struct
    {
        var width = channel.Width;
        var height = channel.Height;
        var samples = channel.Samples;
        var inline = decoder.CanReadInline && width >= 3;
        for (var y = 0; y < height; y++)
        {
            if ((y & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reader.EnsureNotOverrun();
            }

            var row = samples.Slice(y * width, width);
            var above = y > 0 ? samples.Slice((y - 1) * width, width) : default;
            var twoAbove = y > 1 ? samples.Slice((y - 2) * width, width) : above;
            var sink = new WeightedSink<TSelector>(reader, decoder, nodes, in selector, y, row, above, twoAbove, distanceMultiplier);
            if (inline && y > 0)
            {
                DecodeWeightedRowInline(reader, decoder, weighted, nodes, in selector, y, row, above, twoAbove, ref sink);
            }
            else
            {
                weighted.PredictRow(y, above, twoAbove, ref sink);
            }
        }
    }

    /// <summary>
    /// Decodes a row below the first one for <see cref="DecodeWeighted"/> when the stream uses ANS without LZ77: the
    /// operations of <see cref="JpegXlWeightedPredictor.PredictRow"/>, with the state of the entropy decoder and of the bit
    /// reader in locals for the samples between the first and the last one.
    /// </summary>
    private static void DecodeWeightedRowInline<TSelector>(
        JpegXlBitReader reader,
        JpegXlEntropyDecoder decoder,
        JpegXlWeightedPredictor weighted,
        ReadOnlySpan<JpegXlMaTreeNode> nodes,
        in TSelector selector,
        int y,
        Span<int> row,
        ReadOnlySpan<int> above,
        ReadOnlySpan<int> twoAbove,
        ref WeightedSink<TSelector> sink)
        where TSelector : struct, ILeafSelector, allows ref struct
    {
        var width = row.Length;
        var errors = weighted.GetRowErrors(y);
        var parameters = weighted.GetParameters();
        var state = default(JpegXlWeightedPredictor.RowState);
        JpegXlWeightedPredictor.PredictEdge(ref sink, in parameters, ref state, 0, y, above, twoAbove, errors);

        var last = width - 1;
        var aboveRow = above[..width];
        var topTopRow = twoAbove[..width];
        var previousErrors = errors.Previous;
        var currentErrors = errors.Current;
        var currentSubErrors = errors.CurrentSub;
        ref var previousSubErrors = ref unsafe(System.Runtime.InteropServices.MemoryMarshal.GetReference(errors.PreviousSub));
        Vector128<uint> subN;
        Vector128<uint> subNE;
        unsafe
        {
            // The rows of sub-errors hold four values per column: the reads stay below the last column
            subN = Vector128.LoadUnsafe(ref previousSubErrors, 0);
            subNE = Vector128.LoadUnsafe(ref previousSubErrors, 4);
        }

        var errorW = state.ErrorW;
        var west = state.West;
        var westWest = west;
        var w0 = state.W0;
        var w1 = state.W1;
        var w2 = state.W2;
        var w3 = state.W3;
        var ww0 = state.WW0;
        var ww1 = state.WW1;
        var ww2 = state.WW2;
        var ww3 = state.WW3;
        var tables = new JpegXlEntropyDecoder.InlineTables(decoder, reader);
        var ansState = decoder.AnsState;
        reader.GetState(out var bits, out var bitCount, out var position);
        for (var x = 1; x < last; x++)
        {
            var subNW = subN;
            subN = subNE;
            unsafe
            {
                subNE = Vector128.LoadUnsafe(ref previousSubErrors, (nuint)((x + 1) * 4));
            }

            // The errors above and those of the sample two on the left are known before the sample on the left
            var above3 = subNW + subN + subNE;
            long north = aboveRow[x];
            long northEast = aboveRow[x + 1];
            long northWest = aboveRow[x - 1];
            long northNorth = topTopRow[x];
            long errorN = previousErrors[x];
            long errorNW = previousErrors[x - 1];
            long errorNE = previousErrors[x + 1];
            var prediction = JpegXlWeightedPredictor.PredictSample(
                in parameters,
                above3.GetElement(0) + ww0 + w0,
                above3.GetElement(1) + ww1 + w1,
                above3.GetElement(2) + ww2 + w2,
                above3.GetElement(3) + ww3 + w3,
                north,
                west,
                northEast,
                northWest,
                northNorth,
                errorW,
                errorN,
                errorNW,
                errorNE,
                out var prediction0,
                out var prediction1,
                out var prediction2,
                out var prediction3);
            var maxError = JpegXlWeightedPredictor.GetMaxError(errorW, errorN, errorNW, errorNE);
            var cell = selector.GetCell(x, y, maxError, west, north, northWest, northEast, westWest, northNorth);
            var token = tables.Read((int)((uint)cell >> 24), ref ansState, ref bits, ref bitCount, ref position);
            ref readonly var leaf = ref nodes[cell & 0xFFFFFF];
            var weightedPrediction = JpegXlWeightedPredictor.Round(prediction);
            long guess;
            if (selector.IsWeightedOnly)
            {
                guess = weightedPrediction;
            }
            else
            {
                // The gradient and the weighted predictor, which most learned trees mix, are selected without a branch
                var predictor = leaf.RightOrPredictor;
                if ((uint)(predictor - 5) <= 1)
                {
                    var isWeighted = -(long)(predictor - 5);
                    guess = (weightedPrediction & isWeighted) | (JpegXlModularTransform.ClampedGradientWithoutBranch(west, north, northWest) & ~isWeighted);
                }
                else
                {
                    var northEastEast = x + 2 < width ? aboveRow[x + 2] : northEast;
                    guess = JpegXlModularTransform.Predict(predictor, west, north, northNorth, northWest, northEast, westWest, northEastEast, weightedPrediction);
                }
            }

            var sample = Sample(token, leaf.Multiplier, leaf.ValueOrOffset + guess);
            row[x] = sample;
            ww0 = w0;
            ww1 = w1;
            ww2 = w2;
            ww3 = w3;
            errorW = JpegXlWeightedPredictor.Record(prediction, prediction0, prediction1, prediction2, prediction3, sample, x, currentErrors, currentSubErrors, out w0, out w1, out w2, out w3);
            westWest = west;
            west = sample;
        }

        decoder.AnsState = ansState;
        reader.SetState(bits, bitCount, position);
        state = new JpegXlWeightedPredictor.RowState(w0, w1, w2, w3, ww0, ww1, ww2, ww3, errorW, west);
        JpegXlWeightedPredictor.PredictEdge(ref sink, in parameters, ref state, last, y, above, twoAbove, errors);
    }

    /// <summary>Gives the leaf of a sample, for <see cref="DecodeWeighted"/>.</summary>
    private interface ILeafSelector
    {
        /// <summary>Gets a value indicating whether every leaf uses the weighted predictor.</summary>
        bool IsWeightedOnly { get; }

        /// <summary>Returns the leaf of a sample, with the cluster of the context of the leaf in the high byte.</summary>
        int GetCell(int x, int y, int maxError, long left, long top, long topLeft, long topRight, long leftLeft, long topTop);
    }

    /// <summary>The leaves of the values of the property of the weighted predictor, when they all use that predictor.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly ref struct WeightedTableSelector(ReadOnlySpan<int> cells, int low) : ILeafSelector
    {
        private readonly ReadOnlySpan<int> _cells = cells;
        private readonly int _low = low;
        private readonly long _high = (long)low + cells.Length - 1;

        public bool IsWeightedOnly => true;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public int GetCell(int x, int y, int maxError, long left, long top, long topLeft, long topRight, long leftLeft, long topTop)
            => _cells[(int)(Math.Min(Math.Max(maxError, _low), _high) - _low)];
    }

    /// <summary>The leaves of a tree given by its table.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly ref struct TreeTableSelector(JpegXlMaTreeTable table) : ILeafSelector
    {
        private readonly JpegXlMaTreeTable _table = table;

        public bool IsWeightedOnly => false;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public int GetCell(int x, int y, int maxError, long left, long top, long topLeft, long topRight, long leftLeft, long topTop)
            => _table.GetCell(x, y, maxError, left, top, topLeft, topRight, leftLeft, topTop);
    }

    /// <summary>
    /// Decodes the samples that <see cref="DecodeWeighted"/> does not decode inline (the first row, the first and the last
    /// column, and every sample of a stream with prefix codes or LZ77), with the neighbors of the general loop.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly ref struct WeightedSink<TSelector> : JpegXlWeightedPredictor.IRowSink
        where TSelector : struct, ILeafSelector, allows ref struct
    {
        private readonly JpegXlBitReader _reader;
        private readonly JpegXlEntropyDecoder _decoder;
        private readonly ReadOnlySpan<JpegXlMaTreeNode> _nodes;
        private readonly TSelector _selector;
        private readonly int _y;
        private readonly Span<int> _row;
        private readonly ReadOnlySpan<int> _above;
        private readonly ReadOnlySpan<int> _twoAbove;
        private readonly uint _distanceMultiplier;

        public WeightedSink(JpegXlBitReader reader, JpegXlEntropyDecoder decoder, ReadOnlySpan<JpegXlMaTreeNode> nodes, in TSelector selector, int y, Span<int> row, ReadOnlySpan<int> above, ReadOnlySpan<int> twoAbove, uint distanceMultiplier)
        {
            _reader = reader;
            _decoder = decoder;
            _nodes = nodes;
            _selector = selector;
            _y = y;
            _row = row;
            _above = above;
            _twoAbove = twoAbove;
            _distanceMultiplier = distanceMultiplier;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public int Next(int x, long prediction, int maxError)
        {
            long guess;
            int cell;
            if (_selector.IsWeightedOnly)
            {
                cell = _selector.GetCell(x, _y, maxError, 0, 0, 0, 0, 0, 0);
                guess = prediction;
            }
            else
            {
                var row = _row;
                var above = _above;
                var width = row.Length;
                var y = _y;
                long left = x > 0 ? row[x - 1] : y > 0 ? above[x] : 0;
                long top = y > 0 ? above[x] : left;
                long topLeft = x > 0 && y > 0 ? above[x - 1] : left;
                long topRight = x + 1 < width && y > 0 ? above[x + 1] : top;
                long leftLeft = x > 1 ? row[x - 2] : left;
                long topTop = y > 1 ? _twoAbove[x] : top;
                long topRightRight = x + 2 < width && y > 0 ? above[x + 2] : topRight;
                cell = _selector.GetCell(x, y, maxError, left, top, topLeft, topRight, leftLeft, topTop);
                guess = JpegXlModularTransform.Predict(_nodes[cell & 0xFFFFFF].RightOrPredictor, left, top, topTop, topLeft, topRight, leftLeft, topRightRight, prediction);
            }

            ref readonly var leaf = ref _nodes[cell & 0xFFFFFF];
            var sample = Sample(_decoder.Read(_reader, leaf.LeftOrContext, _distanceMultiplier), leaf.Multiplier, leaf.ValueOrOffset + guess);
            _row[x] = sample;
            return sample;
        }
    }

    /// <summary>
    /// Fills the table of the leaves of a tree whose decisions all test the same property, one of those computed from the
    /// position and the neighbors of a sample (2 to 15), when the split values span fewer values than the table holds: the
    /// entry i is the leaf of the value <paramref name="low"/> + i, the first entry is also the leaf of the smaller values
    /// and the last one of the larger values.
    /// </summary>
    /// <returns>The number of entries, or 0 when the tree is not of that kind.</returns>
    private static int FillLeafTable(ReadOnlySpan<JpegXlMaTreeNode> nodes, Span<int> leaves, out int property, out int low)
    {
        property = -1;
        low = 0;
        long smallest = long.MaxValue;
        long largest = long.MinValue;
        foreach (ref readonly var node in nodes)
        {
            if (node.IsLeaf)
                continue;

            if (property >= 0 && node.Property != property)
                return 0;

            property = node.Property;
            smallest = Math.Min(smallest, node.ValueOrOffset);
            largest = Math.Max(largest, node.ValueOrOffset);
        }

        // A value above the largest split value is greater than all of them: it has the leaf of the largest one plus one
        var length = largest + 2 - smallest;
        if (property is < 2 or >= FirstReferenceProperty || length > leaves.Length)
            return 0;

        low = (int)smallest;
        leaves = leaves[..(int)length];
        for (var i = 0; i < leaves.Length; i++)
        {
            var value = smallest + i;
            var index = 0;
            while (!nodes[index].IsLeaf)
            {
                ref readonly var decision = ref nodes[index];
                index = value > decision.ValueOrOffset ? decision.LeftOrContext : decision.RightOrPredictor;
            }

            leaves[i] = index;
        }

        return leaves.Length;
    }

    /// <summary>
    /// Computes the four properties that a previous channel gives each sample of a row: its value, and its difference with
    /// the gradient of its own neighbors, each with its magnitude. They are written every <paramref name="stride"/> values.
    /// </summary>
    internal static void ComputeReferenceProperties(JpegXlModularChannel other, int y, Span<int> destination, int stride)
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
    internal static int Specialize(ReadOnlySpan<JpegXlMaTreeNode> source, Span<JpegXlMaTreeNode> destination, Span<int> pending, int channelIndex, int streamIndex)
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
