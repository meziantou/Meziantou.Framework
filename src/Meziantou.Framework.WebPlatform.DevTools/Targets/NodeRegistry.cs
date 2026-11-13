using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.DevTools.Targets;

/// <summary>The identifiers of the nodes of a page (the <c>backendNodeId</c> of the protocol): a node keeps its identifier while it is alive.</summary>
internal sealed class NodeRegistry
{
    private readonly ConditionalWeakTable<Node, StrongBox<int>> _ids = [];
    private readonly Dictionary<int, WeakReference<Node>> _nodes = [];
    private int _nextId;

    public int GetId(Node node)
    {
        if (_ids.TryGetValue(node, out var box))
            return box.Value;

        var id = ++_nextId;
        _ids.Add(node, new StrongBox<int>(id));
        _nodes[id] = new WeakReference<Node>(node);

        // The entries of the collected nodes are removed from time to time
        if (_nodes.Count > 1024 && (id % 1024) == 0)
        {
            foreach (var (key, reference) in _nodes.ToArray())
            {
                if (!reference.TryGetTarget(out _))
                {
                    _nodes.Remove(key);
                }
            }
        }

        return id;
    }

    public Node? Find(int id) => _nodes.TryGetValue(id, out var reference) && reference.TryGetTarget(out var node) ? node : null;
}
