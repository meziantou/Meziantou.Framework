namespace Meziantou.Framework.WebPlatform.WebAudio.Rendering;

/// <summary>
/// Orders the nodes of a context for the rendering of a render quantum (https://webaudio.github.io/web-audio-api/#rendering-loop):
/// a node is processed after the nodes whose outputs it reads (its inputs and the inputs of its parameters). A node that can
/// break a cycle (a <see cref="DelayNode"/>) and is in a cycle is split: its output is produced from its delay line before the
/// other nodes, and its input is written after them. The nodes of a cycle that has no such node are muted.
/// </summary>
internal static class AudioGraph
{
    /// <summary>Computes the processing order of the nodes, and sets their <see cref="AudioNode.IsMuted"/> and <see cref="AudioNode.BreaksCycle"/> flags.</summary>
    public static (AudioNode[] Order, AudioNode[] SplitNodes) ComputeOrder(IReadOnlyList<AudioNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.IsMuted = false;
            node.BreaksCycle = false;
        }

        var index = new Dictionary<AudioNode, int>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            index[nodes[i]] = i;
        }

        // The cycles are broken by the nodes that can break them, until the remaining cycles have no such node
        List<List<int>> components;
        while (true)
        {
            components = ComputeStronglyConnectedComponents(nodes, index);
            var changed = false;
            foreach (var component in components)
            {
                if (!IsCycle(nodes, index, component))
                    continue;

                foreach (var i in component)
                {
                    if (nodes[i].CanBreakCycles && !nodes[i].BreaksCycle)
                    {
                        nodes[i].BreaksCycle = true;
                        changed = true;
                    }
                }
            }

            if (!changed)
                break;
        }

        var order = new List<AudioNode>(nodes.Count);
        var splitNodes = new List<AudioNode>();
        foreach (var component in components)
        {
            var muted = IsCycle(nodes, index, component);
            foreach (var i in component)
            {
                var node = nodes[i];
                node.IsMuted = muted;
                order.Add(node);
                if (node.BreaksCycle)
                {
                    splitNodes.Add(node);
                }
            }
        }

        return ([.. order], [.. splitNodes]);
    }

    private static bool IsCycle(IReadOnlyList<AudioNode> nodes, Dictionary<AudioNode, int> index, List<int> component)
    {
        if (component.Count > 1)
            return true;

        var node = nodes[component[0]];
        foreach (var upstream in GetDependencies(node))
        {
            if (upstream == node)
                return true;
        }

        return false;
    }

    /// <summary>The nodes a node depends on in the current render quantum (the inputs of a split node are read after the quantum).</summary>
    private static IEnumerable<AudioNode> GetDependencies(AudioNode node) => node.BreaksCycle ? node.GetParameterInputNodes() : node.GetUpstreamNodes();

    /// <summary>
    /// The strongly connected components (Tarjan's algorithm, iterative), with edges from a node to the nodes it depends on:
    /// the components are produced in dependency order (a component after the components it depends on).
    /// </summary>
    private static List<List<int>> ComputeStronglyConnectedComponents(IReadOnlyList<AudioNode> nodes, Dictionary<AudioNode, int> index)
    {
        var count = nodes.Count;
        var indices = new int[count];
        var lowLinks = new int[count];
        var onStack = new bool[count];
        Array.Fill(indices, -1);
        var stack = new Stack<int>();
        var components = new List<List<int>>();
        var nextIndex = 0;

        var callStack = new Stack<(int Node, IEnumerator<AudioNode> Edges)>();
        for (var root = 0; root < count; root++)
        {
            if (indices[root] >= 0)
                continue;

            indices[root] = lowLinks[root] = nextIndex++;
            stack.Push(root);
            onStack[root] = true;
            callStack.Push((root, GetDependencies(nodes[root]).GetEnumerator()));
            while (callStack.Count > 0)
            {
                var (v, edges) = callStack.Peek();
                var descended = false;
                while (edges.MoveNext())
                {
                    if (!index.TryGetValue(edges.Current, out var w))
                        continue;

                    if (indices[w] < 0)
                    {
                        indices[w] = lowLinks[w] = nextIndex++;
                        stack.Push(w);
                        onStack[w] = true;
                        callStack.Push((w, GetDependencies(nodes[w]).GetEnumerator()));
                        descended = true;
                        break;
                    }

                    if (onStack[w])
                    {
                        lowLinks[v] = Math.Min(lowLinks[v], indices[w]);
                    }
                }

                if (descended)
                    continue;

                edges.Dispose();
                callStack.Pop();
                if (callStack.Count > 0)
                {
                    var parent = callStack.Peek().Node;
                    lowLinks[parent] = Math.Min(lowLinks[parent], lowLinks[v]);
                }

                if (lowLinks[v] == indices[v])
                {
                    var component = new List<int>();
                    int w;
                    do
                    {
                        w = stack.Pop();
                        onStack[w] = false;
                        component.Add(w);
                    }
                    while (w != v);

                    components.Add(component);
                }
            }
        }

        return components;
    }
}
