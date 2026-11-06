namespace Meziantou.Framework.Charts;

/// <summary>
/// Lays a tree out as a tidy node-link diagram, as d3's <c>tree</c>: the Reingold–Tilford algorithm in the linear-time
/// version of Buchheim, Jünger and Leipert (2002). The nodes of a depth are on a line, a parent is centered over its
/// children, and subtrees are as compact as possible without overlapping. Siblings are 1 unit apart, cousins 2.
/// </summary>
public sealed class TreeLayout
{
    /// <summary>Gets or sets the size of the layout across the depths. The default is 1.</summary>
    public double Width { get; set; } = 1;

    /// <summary>Gets or sets the size of the layout along the depths. The default is 1.</summary>
    public double Height { get; set; } = 1;

    /// <summary>Places the nodes of a tree.</summary>
    /// <typeparam name="T">The type of the data of the nodes.</typeparam>
    /// <param name="root">The root of the tree.</param>
    /// <returns>The point of every node, in pre-order.</returns>
    public IReadOnlyList<HierarchyPoint<T>> Layout<T>(HierarchyNode<T> root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var tree = WalkerNode<T>.Create(root);
        // The first walk is in post-order, from left to right: a node is placed after its left siblings and its children
        foreach (var node in tree.PostOrder())
        {
            FirstWalk(node);
        }

        var nodes = tree.PreOrder().ToList();
        tree.Parent!.Mod = -tree.Prelim;
        foreach (var node in nodes)
        {
            node.X = node.Prelim + node.Parent!.Mod;
            node.Mod += node.Parent.Mod;
        }

        // Scale to the size, leaving half a separation on each side, as d3
        var left = nodes[0];
        var right = nodes[0];
        var bottom = nodes[0];
        foreach (var node in nodes)
        {
            if (node.X < left.X)
            {
                left = node;
            }

            if (node.X > right.X)
            {
                right = node;
            }

            if (node.Node!.Depth > bottom.Node!.Depth)
            {
                bottom = node;
            }
        }

        var s = left == right ? 1 : Separation(left.Node!, right.Node!) / 2;
        var tx = s - left.X;
        var kx = Width / (right.X + s + tx);
        var ky = Height / Math.Max(1, bottom.Node!.Depth);
        return [.. nodes.Select(node => new HierarchyPoint<T>(node.Node!, (node.X + tx) * kx, node.Node!.Depth * ky))];
    }

    internal static double Separation<T>(HierarchyNode<T> a, HierarchyNode<T> b) => a.Parent == b.Parent ? 1 : 2;

    private static void FirstWalk<T>(WalkerNode<T> v)
    {
        var siblings = v.Parent!.Children!;
        var w = v.Index > 0 ? siblings[v.Index - 1] : null;
        if (v.Children is { } children)
        {
            ExecuteShifts(v);
            var midpoint = (children[0].Prelim + children[^1].Prelim) / 2;
            if (w is not null)
            {
                v.Prelim = w.Prelim + Separation(v.Node!, w.Node!);
                v.Mod = v.Prelim - midpoint;
            }
            else
            {
                v.Prelim = midpoint;
            }
        }
        else if (w is not null)
        {
            v.Prelim = w.Prelim + Separation(v.Node!, w.Node!);
        }

        v.Parent.DefaultAncestor = Apportion(v, w, v.Parent.DefaultAncestor ?? siblings[0]);
    }

    private static WalkerNode<T> Apportion<T>(WalkerNode<T> v, WalkerNode<T>? w, WalkerNode<T> ancestor)
    {
        if (w is null)
            return ancestor;

        var vip = v;
        var vop = v;
        var vim = w;
        var vom = vip.Parent!.Children![0];
        var sip = vip.Mod;
        var sop = vop.Mod;
        var sim = vim.Mod;
        var som = vom.Mod;
        WalkerNode<T>? nextRight;
        WalkerNode<T>? nextLeft;
        while ((nextRight = NextRight(vim)) is not null & (nextLeft = NextLeft(vip)) is not null)
        {
            vim = nextRight!;
            vip = nextLeft!;
            vom = NextLeft(vom)!;
            vop = NextRight(vop)!;
            vop.Ancestor = v;
            var shift = vim.Prelim + sim - vip.Prelim - sip + Separation(vim.Node!, vip.Node!);
            if (shift > 0)
            {
                MoveSubtree(NextAncestor(vim, v, ancestor), v, shift);
                sip += shift;
                sop += shift;
            }

            sim += vim.Mod;
            sip += vip.Mod;
            som += vom.Mod;
            sop += vop.Mod;
        }

        if (nextRight is not null && NextRight(vop) is null)
        {
            vop.Thread = nextRight;
            vop.Mod += sim - sop;
        }

        if (nextLeft is not null && NextLeft(vom) is null)
        {
            vom.Thread = nextLeft;
            vom.Mod += sip - som;
            ancestor = v;
        }

        return ancestor;
    }

    private static WalkerNode<T>? NextLeft<T>(WalkerNode<T> v) => v.Children is { } children ? children[0] : v.Thread;

    private static WalkerNode<T>? NextRight<T>(WalkerNode<T> v) => v.Children is { } children ? children[^1] : v.Thread;

    private static void MoveSubtree<T>(WalkerNode<T> wm, WalkerNode<T> wp, double shift)
    {
        var change = shift / (wp.Index - wm.Index);
        wp.Change -= change;
        wp.Shift += shift;
        wm.Change += change;
        wp.Prelim += shift;
        wp.Mod += shift;
    }

    private static void ExecuteShifts<T>(WalkerNode<T> v)
    {
        var shift = 0.0;
        var change = 0.0;
        var children = v.Children!;
        for (var i = children.Count - 1; i >= 0; i--)
        {
            var w = children[i];
            w.Prelim += shift;
            w.Mod += shift;
            change += w.Change;
            shift += w.Shift + change;
        }
    }

    private static WalkerNode<T> NextAncestor<T>(WalkerNode<T> vim, WalkerNode<T> v, WalkerNode<T> ancestor) => vim.Ancestor.Parent == v.Parent ? vim.Ancestor : ancestor;
}
