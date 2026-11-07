namespace Meziantou.Framework.Charts;

/// <summary>Creates trees of <see cref="HierarchyNode{T}"/> from nested objects, from rows with a parent identifier, or from paths.</summary>
public static class Hierarchy
{
    /// <summary>Creates a tree from nested objects.</summary>
    /// <typeparam name="T">The type of the objects.</typeparam>
    /// <param name="root">The root object.</param>
    /// <param name="children">The children of an object, or <see langword="null"/> for none.</param>
    /// <param name="name">The name of an object, or <see langword="null"/> to use <see cref="object.ToString"/>.</param>
    /// <param name="value">The own value of an object, to compute the values of the nodes with <see cref="HierarchyNode{T}.Sum"/>; or <see langword="null"/> to leave them at zero.</param>
    /// <returns>The root node.</returns>
    public static HierarchyNode<T> FromTree<T>(T root, Func<T, IEnumerable<T>?> children, Func<T, string?>? name = null, Func<T, double>? value = null)
    {
        ArgumentNullException.ThrowIfNull(children);
        var rootNode = new HierarchyNode<T>(root, GetName(root, name), parent: null);
        var stack = new Stack<HierarchyNode<T>>();
        stack.Push(rootNode);
        while (stack.TryPop(out var node))
        {
            if (children(node.Data) is not { } items)
                continue;

            foreach (var item in items)
            {
                if (node.Depth >= 1000)
                    throw new ArgumentException("The tree is deeper than 1000 levels, or has a cycle.", nameof(children));

                stack.Push(new HierarchyNode<T>(item, GetName(item, name), node));
            }
        }

        if (value is not null)
        {
            rootNode.Sum(value);
        }

        return rootNode;
    }

    /// <summary>Creates a tree from rows that give their identifier and the identifier of their parent.</summary>
    /// <typeparam name="T">The type of the rows.</typeparam>
    /// <param name="rows">The rows. Exactly one of them, the root, has no parent.</param>
    /// <param name="id">The identifier of a row, which is also the name of its node.</param>
    /// <param name="parentId">The identifier of the parent of a row, or <see langword="null"/> or empty for the root.</param>
    /// <param name="value">The own value of a row, to compute the values of the nodes; or <see langword="null"/> to leave them at zero.</param>
    /// <returns>The root node.</returns>
    /// <exception cref="ArgumentException">There is no root or several roots, an identifier is duplicated, a parent does not exist, or there is a cycle.</exception>
    public static HierarchyNode<T> Stratify<T>(IEnumerable<T> rows, Func<T, string> id, Func<T, string?> parentId, Func<T, double>? value = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(parentId);
        var items = rows.ToList();
        var byId = new Dictionary<string, T>(StringComparer.Ordinal);
        var childrenOf = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        T? root = default;
        var rootCount = 0;
        foreach (var item in items)
        {
            var key = id(item);
            if (!byId.TryAdd(key, item))
                throw new ArgumentException($"The identifier '{key}' is duplicated.", nameof(rows));

            var parent = parentId(item);
            if (string.IsNullOrEmpty(parent))
            {
                root = item;
                rootCount++;
                continue;
            }

            if (!childrenOf.TryGetValue(parent, out var list))
            {
                list = [];
                childrenOf.Add(parent, list);
            }

            list.Add(item);
        }

        if (rootCount is not 1)
            throw new ArgumentException(rootCount is 0 ? "There is no root: every row has a parent." : "There are several roots: several rows have no parent.", nameof(rows));

        foreach (var parent in childrenOf.Keys)
        {
            if (!byId.ContainsKey(parent))
                throw new ArgumentException($"The parent '{parent}' does not exist.", nameof(rows));
        }

        var node = FromTree(root!, item => childrenOf.GetValueOrDefault(id(item)), id, value);
        if (node.Descendants().Count() != items.Count)
            throw new ArgumentException("The rows have a cycle.", nameof(rows));

        return node;
    }

    /// <summary>Creates a tree from rows that give their path, such as <c>src/app/main.cs</c>. The intermediate nodes the paths imply are created, with the default value of <typeparamref name="T"/> as their data.</summary>
    /// <typeparam name="T">The type of the rows.</typeparam>
    /// <param name="rows">The rows.</param>
    /// <param name="path">The path of a row.</param>
    /// <param name="value">The own value of a row, to compute the values of the nodes; or <see langword="null"/> to leave them at zero.</param>
    /// <param name="separator">The separator of the segments of the paths. The default is <c>/</c>.</param>
    /// <returns>The root node: a node with an empty name, whose children are the first segments of the paths.</returns>
    public static HierarchyNode<T?> FromPaths<T>(IEnumerable<T> rows, Func<T, string> path, Func<T, double>? value = null, char separator = '/')
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(path);
        var root = new HierarchyNode<T?>(default, string.Empty, parent: null);
        var nodes = new Dictionary<string, HierarchyNode<T?>>(StringComparer.Ordinal) { [string.Empty] = root };
        var values = new Dictionary<HierarchyNode<T?>, double>();
        foreach (var row in rows)
        {
            var segments = path(row).Split(separator, StringSplitOptions.RemoveEmptyEntries);
            var parent = root;
            var key = string.Empty;
            for (var i = 0; i < segments.Length; i++)
            {
                key += separator + segments[i];
                if (!nodes.TryGetValue(key, out var node))
                {
                    node = new HierarchyNode<T?>(i == segments.Length - 1 ? row : default, segments[i], parent);
                    nodes.Add(key, node);
                }

                parent = node;
            }

            if (value is not null)
            {
                // Rows that share a path add their values, which may overflow
                values[parent] = Math.Min(values.GetValueOrDefault(parent) + HierarchyNode<T?>.GetOwnValue(value(row)), double.MaxValue);
            }
        }

        if (value is not null)
        {
            // Several nodes may share the default data: their values are read by node
            root.SumNodes(node => values.GetValueOrDefault(node));
        }

        return root;
    }

    private static string GetName<T>(T item, Func<T, string?>? name) => (name is null ? item?.ToString() : name(item)) ?? string.Empty;
}
