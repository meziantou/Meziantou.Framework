namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A meta-adaptive tree (ISO/IEC 18181-1, section H.4.2) and the entropy code of the samples it gives a context to: the
/// tree maps the properties of a sample (its position, its neighbors, the samples of the previous channels) to a leaf.
/// </summary>
internal sealed class JpegXlMaTree : IDisposable
{
    /// <summary>The number of contexts of the stream that codes a tree.</summary>
    public const int TreeContextCount = 6;

    /// <summary>The number of predictors.</summary>
    public const int PredictorCount = 14;

    /// <summary>The index of the property of the weighted predictor; the properties of the previous channels follow.</summary>
    public const int WeightedProperty = 15;

    private const int SplitValueContext = 0;
    private const int PropertyContext = 1;
    private const int PredictorContext = 2;
    private const int OffsetContext = 3;
    private const int MultiplierLogContext = 4;
    private const int MultiplierBitsContext = 5;
    private const int MaxHeight = 2048;

    private readonly Av1PooledArray<JpegXlMaTreeNode> _nodes;

    private JpegXlMaTree(Av1PooledArray<JpegXlMaTreeNode> nodes, int nodeCount, int leafCount)
    {
        _nodes = nodes;
        NodeCount = nodeCount;
        LeafCount = leafCount;
    }

    /// <summary>Gets the number of nodes.</summary>
    public int NodeCount { get; }

    /// <summary>Gets the number of leaves: the number of contexts of the stream of the samples.</summary>
    public int LeafCount { get; }

    /// <summary>Gets the nodes; the root is the first one.</summary>
    public ReadOnlySpan<JpegXlMaTreeNode> Nodes => _nodes.Span[..NodeCount];

    /// <summary>Gets or sets the entropy code of the samples, which follows the tree in the codestream. The tree owns it.</summary>
    public JpegXlEntropyCode? Code { get; set; }

    /// <summary>Reads a tree and the entropy code of its samples.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="scope">The scope the nodes and the tables are rented from.</param>
    /// <param name="maxNodeCount">
    /// The node limit of the tree. A node is read only while the nodes already read are not more than the limit, so the
    /// tree may have one node more than the limit, as in libjxl and jxl-oxide.
    /// </param>
    /// <returns>The tree; it must be disposed.</returns>
    public static JpegXlMaTree Read(JpegXlBitReader reader, AllocationScope scope, long maxNodeCount)
    {
        var tree = ReadNodes(reader, scope, maxNodeCount);
        try
        {
            tree.Code = JpegXlEntropyCode.Read(reader, tree.LeafCount, scope);
            return tree;
        }
        catch
        {
            tree.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _nodes.Dispose();
        Code?.Dispose();
        Code = null;
    }

    private static JpegXlMaTree ReadNodes(JpegXlBitReader reader, AllocationScope scope, long maxNodeCount)
    {
        using var code = JpegXlEntropyCode.Read(reader, TreeContextCount, scope);
        using var decoder = new JpegXlEntropyDecoder(code, scope);
        decoder.Begin(reader);

        // The nodes are stored in breadth-first order: the children of a decision node come after every node already announced
        Av1PooledArray<JpegXlMaTreeNode>? owned = null;
        try
        {
            var nodes = owned = Av1PooledArray<JpegXlMaTreeNode>.Rent(scope, 64, clear: false);
            var count = 0;
            var leafCount = 0;
            var toDecode = 1;
            while (toDecode > 0)
            {
                if (reader.IsOverrun)
                    throw JpegXlFormat.Invalid("a Modular tree is truncated.");

                // count is the number of nodes already read; libjxl and jxl-oxide compare it the same way
                if (count > maxNodeCount)
                    throw JpegXlFormat.Invalid("a Modular tree has more nodes than its image allows.");

                if (count == nodes.Length)
                {
                    var larger = Av1PooledArray<JpegXlMaTreeNode>.Rent(scope, (long)count * 2, clear: false);
                    nodes.Span.CopyTo(larger.Span);
                    nodes.Dispose();
                    nodes = owned = larger;
                }

                toDecode--;
                var property = (int)Math.Min(decoder.Read(reader, PropertyContext), 257) - 1;
                if (property > 255)
                    throw JpegXlFormat.Invalid("a Modular tree tests a property above 255.");

                ref var node = ref nodes.Span[count];
                if (property < 0)
                {
                    var predictor = decoder.Read(reader, PredictorContext);
                    if (predictor >= PredictorCount)
                        throw JpegXlFormat.Invalid("a Modular tree uses a predictor that does not exist.");

                    var offset = JpegXlBitReader.UnpackSigned(decoder.Read(reader, OffsetContext));
                    var multiplierLog = decoder.Read(reader, MultiplierLogContext);
                    if (multiplierLog >= 31)
                        throw JpegXlFormat.Invalid("a Modular tree has a multiplier that is too large.");

                    var multiplierBits = decoder.Read(reader, MultiplierBitsContext);
                    if (multiplierBits >= (1u << (31 - (int)multiplierLog)) - 1)
                        throw JpegXlFormat.Invalid("a Modular tree has a multiplier that is too large.");

                    node = new JpegXlMaTreeNode
                    {
                        Property = -1,
                        ValueOrOffset = offset,
                        LeftOrContext = leafCount++,
                        RightOrPredictor = (int)predictor,
                        Multiplier = (int)((multiplierBits + 1) << (int)multiplierLog),
                    };
                }
                else
                {
                    node = new JpegXlMaTreeNode
                    {
                        Property = property,
                        ValueOrOffset = JpegXlBitReader.UnpackSigned(decoder.Read(reader, SplitValueContext)),
                        LeftOrContext = count + toDecode + 1,
                        RightOrPredictor = count + toDecode + 2,
                        Multiplier = 1,
                    };
                    toDecode += 2;
                }

                count++;
            }

            decoder.EnsureFinalState();
            Validate(nodes.Span[..count]);
            owned = null;
            return new JpegXlMaTree(nodes, count, leafCount);
        }
        finally
        {
            owned?.Dispose();
        }
    }

    /// <summary>
    /// Checks that no decision is made on a range of values that the decisions above it already exclude, and that the tree
    /// is not taller than 2048 nodes: both make a tree invalid.
    /// </summary>
    private static void Validate(ReadOnlySpan<JpegXlMaTreeNode> nodes)
    {
        Span<int> lower = stackalloc int[256];
        Span<int> upper = stackalloc int[256];
        lower.Fill(int.MinValue);
        upper.Fill(int.MaxValue);

        // Depth-first walk with an explicit stack: the node, the bounds to restore and the next step (0: go left, 1: go right, 2: leave)
        Span<int> stackNode = stackalloc int[MaxHeight];
        Span<int> stackLower = stackalloc int[MaxHeight];
        Span<int> stackUpper = stackalloc int[MaxHeight];
        Span<byte> stackStep = stackalloc byte[MaxHeight];
        var depth = 1;
        stackNode[0] = 0;
        stackStep[0] = 0;
        while (depth > 0)
        {
            if (depth >= MaxHeight)
                throw JpegXlFormat.Invalid("a Modular tree is too tall.");

            var top = depth - 1;
            ref readonly var node = ref nodes[stackNode[top]];
            if (node.IsLeaf)
            {
                depth--;
                continue;
            }

            var property = node.Property;
            switch (stackStep[top])
            {
                case 0:
                {
                    var low = lower[property];
                    var high = upper[property];
                    if (low > node.ValueOrOffset || high <= node.ValueOrOffset)
                        throw JpegXlFormat.Invalid("a Modular tree makes a decision that is already made.");

                    stackLower[top] = low;
                    stackUpper[top] = high;
                    stackStep[top] = 1;
                    lower[property] = node.ValueOrOffset + 1;
                    stackNode[depth] = node.LeftOrContext;
                    stackStep[depth] = 0;
                    depth++;
                    break;
                }

                case 1:
                    stackStep[top] = 2;
                    lower[property] = stackLower[top];
                    upper[property] = node.ValueOrOffset;
                    stackNode[depth] = node.RightOrPredictor;
                    stackStep[depth] = 0;
                    depth++;
                    break;

                default:
                    upper[property] = stackUpper[top];
                    depth--;
                    break;
            }
        }
    }
}
