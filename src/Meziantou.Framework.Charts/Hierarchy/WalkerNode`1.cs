namespace Meziantou.Framework.Charts;

/// <summary>The state of a node during the Buchheim–Walker tree layout, with the names of the paper.</summary>
internal sealed class WalkerNode<T>
{
    private WalkerNode(HierarchyNode<T>? node, int index)
    {
        Node = node;
        Index = index;
        Ancestor = this;
    }

    public HierarchyNode<T>? Node { get; }

    public int Index { get; }

    public WalkerNode<T>? Parent { get; private set; }

    public List<WalkerNode<T>>? Children { get; private set; }

    public WalkerNode<T>? DefaultAncestor { get; set; }

    public WalkerNode<T> Ancestor { get; set; }

    public WalkerNode<T>? Thread { get; set; }

    public double Prelim { get; set; }

    public double Mod { get; set; }

    public double Change { get; set; }

    public double Shift { get; set; }

    public double X { get; set; }

    /// <summary>Wraps a tree. The root gets a parent of its own, without a node, as in d3.</summary>
    public static WalkerNode<T> Create(HierarchyNode<T> root)
    {
        var tree = new WalkerNode<T>(root, 0);
        var stack = new Stack<WalkerNode<T>>();
        stack.Push(tree);
        while (stack.TryPop(out var node))
        {
            var children = node.Node!.Children;
            if (children.Count is 0)
                continue;

            node.Children = new List<WalkerNode<T>>(children.Count);
            for (var i = 0; i < children.Count; i++)
            {
                var child = new WalkerNode<T>(children[i], i) { Parent = node };
                node.Children.Add(child);
                stack.Push(child);
            }
        }

        tree.Parent = new WalkerNode<T>(node: null, 0) { Children = [tree] };
        return tree;
    }

    /// <summary>The nodes in post-order: the children in order, then their parent.</summary>
    public IEnumerable<WalkerNode<T>> PostOrder()
    {
        var stack = new Stack<(WalkerNode<T> Node, bool Visited)>();
        stack.Push((this, false));
        while (stack.TryPop(out var item))
        {
            if (item.Visited || item.Node.Children is null)
            {
                yield return item.Node;
                continue;
            }

            stack.Push((item.Node, true));
            for (var i = item.Node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push((item.Node.Children[i], false));
            }
        }
    }

    /// <summary>The nodes in pre-order: a node before its children, the children in order.</summary>
    public IEnumerable<WalkerNode<T>> PreOrder()
    {
        var stack = new Stack<WalkerNode<T>>();
        stack.Push(this);
        while (stack.TryPop(out var node))
        {
            yield return node;
            if (node.Children is { } children)
            {
                for (var i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }
            }
        }
    }
}
