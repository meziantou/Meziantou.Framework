using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Assertions.Fuzz;

/// <summary>
/// <c>assertions-object-graph</c>: <c>Assert.Equivalent</c> compares a graph of objects, with cycles and shared nodes,
/// with a copy in which the name of one node may differ. In order, the comparison succeeds exactly when that node cannot
/// be reached from the root or when its names compare equal. Ignoring the order of the children, it succeeds at least
/// then. <c>Equivalent</c> searches the pairs of objects it visited in a list up to a few pairs, then in a set: graphs of
/// up to 40 nodes go past that limit.
/// </summary>
/// <remarks>
/// The input starts with three option bytes: the number of nodes, the flags (bit 0: rename a node of the copy, bit 1:
/// ignore the case of the names, bit 2: append to the name instead of changing its case) and the renamed node. Then each
/// node reads its name, its next node (or none), its number of children (0 to 2), and its children.
/// </remarks>
public static partial class AssertionsFuzzTargets
{
    private const int MaxGraphNodes = 40;
    private const byte RenameFlag = 0x01;
    private const byte GraphIgnoreStringCaseFlag = 0x02;
    private const byte AppendToNameFlag = 0x04;

    // Changing the case of a name is the other name of its pair (index ^ 1); "" and "x" differ ignoring case too
    private static readonly string[] GraphNames = ["a", "A", "b", "B", "node", "NODE", "", "x"];

    private static void ObjectGraph(ReadOnlySpan<byte> data)
    {
        var count = 1 + (FuzzInput.ReadByte(ref data) % MaxGraphNodes);
        var flags = FuzzInput.ReadByte(ref data);
        var renamed = FuzzInput.ReadByte(ref data) % count;
        var ignoreStringCase = (flags & GraphIgnoreStringCaseFlag) is not 0;

        var expected = CreateNodes(count);
        var actual = CreateNodes(count);
        var nameIndexes = new int[count];
        for (var i = 0; i < count; i++)
        {
            nameIndexes[i] = FuzzInput.ReadByte(ref data) % GraphNames.Length;
            expected[i].Name = GraphNames[nameIndexes[i]];
            actual[i].Name = GraphNames[nameIndexes[i]];

            // The value `count` is no next node
            var next = FuzzInput.ReadByte(ref data) % (count + 1);
            if (next < count)
            {
                expected[i].Next = expected[next];
                actual[i].Next = actual[next];
            }

            var childCount = FuzzInput.ReadByte(ref data) % 3;
            for (var j = 0; j < childCount; j++)
            {
                var child = FuzzInput.ReadByte(ref data) % count;
                expected[i].Children.Add(expected[child]);
                actual[i].Children.Add(actual[child]);
            }
        }

        var namesDiffer = false;
        if ((flags & RenameFlag) is not 0)
        {
            var originalName = actual[renamed].Name;
            actual[renamed].Name = (flags & AppendToNameFlag) is not 0 ? originalName + "!" : GraphNames[nameIndexes[renamed] ^ 1];
            namesDiffer = !string.Equals(originalName, actual[renamed].Name, ignoreStringCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        var expectedResult = !namesDiffer || !IsReachable(expected[0], expected[renamed]);
        var description = $"graph of {count} nodes, node {renamed} renamed: {namesDiffer}";

        var orderedOptions = new EquivalentOptions { IgnoreStringCase = ignoreStringCase };
        var ordered = Passes(() => Assert.Equivalent(expected[0], actual[0], orderedOptions));
        FuzzAssert.Equal(expectedResult, ordered, $"Equivalent of a {description}");
        FuzzAssert.Equal(!ordered, Passes(() => Assert.NotEquivalent(expected[0], actual[0], orderedOptions)), $"NotEquivalent of a {description} is not the opposite of Equivalent");

        var unorderedOptions = new EquivalentOptions { IgnoreStringCase = ignoreStringCase, IgnoreCollectionOrder = true };
        FuzzAssert.True(!ordered || Passes(() => Assert.Equivalent(expected[0], actual[0], unorderedOptions)), $"Equivalent ignoring the order of a {description} fails, although it succeeds in order");

        FuzzAssert.True(Passes(() => Assert.Equivalent(expected[0], expected[0], orderedOptions)), $"A {description} is not equivalent to itself");
    }

    private static GraphNode[] CreateNodes(int count)
    {
        var nodes = new GraphNode[count];
        for (var i = 0; i < count; i++)
        {
            nodes[i] = new GraphNode();
        }

        return nodes;
    }

    private static bool IsReachable(GraphNode root, GraphNode target)
    {
        var visited = new HashSet<GraphNode>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<GraphNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (!visited.Add(node))
                continue;

            if (ReferenceEquals(node, target))
                return true;

            if (node.Next is not null)
            {
                pending.Push(node.Next);
            }

            foreach (var child in node.Children)
            {
                pending.Push(child);
            }
        }

        return false;
    }

    /// <summary>
    /// The graphs of the unit tests (AssertEqualByStructureTests.LongCycles): rings of 20 nodes, with a renamed node, and
    /// rings of 12 nodes whose children are listed in a different order. Also a node that cannot be reached, and a graph
    /// of shared nodes.
    /// </summary>
    private static IEnumerable<FuzzSeed> GetObjectGraphSeeds()
    {
        yield return GraphSeed("ring-20", 20, flags: 0, renamed: 0, i => (i % GraphNames.Length, (i + 1) % 20, []));
        yield return GraphSeed("ring-20-renamed", 20, RenameFlag | AppendToNameFlag, renamed: 10, i => (i % GraphNames.Length, (i + 1) % 20, []));
        yield return GraphSeed("ring-20-case", 20, RenameFlag, renamed: 10, i => (i % GraphNames.Length, (i + 1) % 20, []));
        yield return GraphSeed("ring-20-ignore-case", 20, RenameFlag | GraphIgnoreStringCaseFlag, renamed: 10, i => (i % GraphNames.Length, (i + 1) % 20, []));
        yield return GraphSeed("ring-12-children", 12, flags: 0, renamed: 0, i => (4, (i + 1) % 12, [(i + 1) % 12, (i + 11) % 12]));
        yield return GraphSeed("ring-12-children-renamed", 12, RenameFlag | AppendToNameFlag, renamed: 5, i => (4, (i + 1) % 12, [(i + 1) % 12, (i + 11) % 12]));
        yield return GraphSeed("unreachable", 10, RenameFlag | AppendToNameFlag, renamed: 9, i => (i % 4, i < 8 ? (i + 1) % 8 : 10, []));
        yield return GraphSeed("shared", 40, RenameFlag | AppendToNameFlag, renamed: 39, i => (i % 6, 40, i < 39 ? [i + 1, 39] : []));
    }

    private static FuzzSeed GraphSeed(string name, int count, int flags, byte renamed, Func<int, (int Name, int Next, int[] Children)> node)
    {
        var bytes = new List<byte> { (byte)(count - 1), (byte)flags, renamed };
        for (var i = 0; i < count; i++)
        {
            var (nameIndex, next, children) = node(i);
            bytes.Add((byte)nameIndex);
            bytes.Add((byte)next);
            bytes.Add((byte)children.Length);
            bytes.AddRange(children.Select(child => (byte)child));
        }

        return new FuzzSeed($"graph-{name}", bytes.ToArray());
    }

    private sealed class GraphNode
    {
        public string Name { get; set; } = "";

        public GraphNode? Next { get; set; }

        public List<GraphNode> Children { get; } = [];
    }
}
