namespace Meziantou.Framework.Assertions.Architecture;

// The dependencies between slices, in the order of the slices and of their types
internal sealed class SliceGraph
{
    private SliceGraph(List<string> sliceNames, Dictionary<(string From, string To), List<Dependency>> edges, List<(string Slice, TypeElement Type, string TargetSlice, List<Dependency> Dependencies)> typeDependencies)
    {
        SliceNames = sliceNames;
        Edges = edges;
        TypeDependencies = typeDependencies;
    }

    public IReadOnlyList<string> SliceNames { get; }

    public Dictionary<(string From, string To), List<Dependency>> Edges { get; }

    // The dependencies of each type on each other slice
    public List<(string Slice, TypeElement Type, string TargetSlice, List<Dependency> Dependencies)> TypeDependencies { get; }

    public static SliceGraph Create(IEnumerable<IGrouping<string, TypeElement>> slices, string paramName)
    {
        ArgumentNullException.ThrowIfNull(slices, paramName);

        var sliceList = new List<(string Name, List<TypeElement> Types)>();
        var sliceOfType = new Dictionary<TypeElement, string>();
        foreach (var slice in slices)
        {
            ArgumentNullException.ThrowIfNull(slice, paramName);
            ArgumentNullException.ThrowIfNull(slice.Key, paramName);
            if (sliceList.Exists(item => string.Equals(item.Name, slice.Key, StringComparison.Ordinal)))
                throw new ArgumentException($"Several slices are named '{slice.Key}'.", paramName);

            var types = new List<TypeElement>();
            foreach (var type in slice)
            {
                ArgumentNullException.ThrowIfNull(type, paramName);
                if (!sliceOfType.TryAdd(type, slice.Key))
                {
                    if (string.Equals(sliceOfType[type], slice.Key, StringComparison.Ordinal))
                        continue;

                    throw new ArgumentException($"The type '{type}' is in the slices '{sliceOfType[type]}' and '{slice.Key}'.", paramName);
                }

                types.Add(type);
            }

            sliceList.Add((slice.Key, types));
        }

        var edges = new Dictionary<(string From, string To), List<Dependency>>();
        var typeDependencies = new List<(string Slice, TypeElement Type, string TargetSlice, List<Dependency> Dependencies)>();
        foreach (var (name, types) in sliceList)
        {
            foreach (var type in types)
            {
                var dependenciesBySlice = new Dictionary<string, List<Dependency>>(StringComparer.Ordinal);
                foreach (var dependency in type.Dependencies)
                {
                    if (dependency.Target.Definition is not { } target || !sliceOfType.TryGetValue(target, out var targetSlice) || string.Equals(targetSlice, name, StringComparison.Ordinal))
                        continue;

                    GetOrAdd(edges, (name, targetSlice)).Add(dependency);
                    GetOrAdd(dependenciesBySlice, targetSlice).Add(dependency);
                }

                foreach (var (targetSlice, dependencies) in dependenciesBySlice)
                {
                    typeDependencies.Add((name, type, targetSlice, dependencies));
                }
            }
        }

        return new SliceGraph([.. sliceList.Select(slice => slice.Name)], edges, typeDependencies);
    }

    // Returns one cycle per group of slices that depend on each other (strongly connected component), starting from the first slice of the group by name
    public List<List<string>> FindCycles()
    {
        var successors = SliceNames.ToDictionary(slice => slice, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (from, to) in Edges.Keys)
        {
            successors[from].Add(to);
        }

        foreach (var list in successors.Values)
        {
            list.Sort(StringComparer.Ordinal);
        }

        // Tarjan's algorithm
        var index = 0;
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var components = new List<List<string>>();
        foreach (var slice in SliceNames)
        {
            if (!indexes.ContainsKey(slice))
            {
                StrongConnect(slice);
            }
        }

        var result = new List<List<string>>();
        foreach (var component in components.Where(component => component.Count > 1).OrderBy(component => component.Min(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            var members = component.ToHashSet(StringComparer.Ordinal);
            var start = component.Min(StringComparer.Ordinal)!;
            result.Add(FindShortestCycle(start, members, successors));
        }

        return result;

        void StrongConnect(string slice)
        {
            indexes[slice] = index;
            lowLinks[slice] = index;
            index++;
            stack.Push(slice);
            onStack.Add(slice);

            foreach (var successor in successors[slice])
            {
                if (!indexes.TryGetValue(successor, out var successorIndex))
                {
                    StrongConnect(successor);
                    lowLinks[slice] = Math.Min(lowLinks[slice], lowLinks[successor]);
                }
                else if (onStack.Contains(successor))
                {
                    lowLinks[slice] = Math.Min(lowLinks[slice], successorIndex);
                }
            }

            if (lowLinks[slice] == indexes[slice])
            {
                var component = new List<string>();
                string member;
                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    component.Add(member);
                }
                while (!string.Equals(member, slice, StringComparison.Ordinal));

                components.Add(component);
            }
        }
    }

    private static List<string> FindShortestCycle(string start, HashSet<string> members, Dictionary<string, List<string>> successors)
    {
        var previous = new Dictionary<string, string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var successor in successors[current])
            {
                if (!members.Contains(successor))
                    continue;

                if (string.Equals(successor, start, StringComparison.Ordinal))
                {
                    var path = new List<string> { start };
                    for (var node = current; !string.Equals(node, start, StringComparison.Ordinal); node = previous[node])
                    {
                        path.Add(node);
                    }

                    path.Add(start);
                    path.Reverse(1, path.Count - 2);
                    return path;
                }

                if (previous.TryAdd(successor, current))
                {
                    queue.Enqueue(successor);
                }
            }
        }

        return [start, start];
    }

    private static List<Dependency> GetOrAdd<TKey>(Dictionary<TKey, List<Dependency>> dictionary, TKey key)
        where TKey : notnull
    {
        if (!dictionary.TryGetValue(key, out var list))
        {
            list = [];
            dictionary.Add(key, list);
        }

        return list;
    }
}
