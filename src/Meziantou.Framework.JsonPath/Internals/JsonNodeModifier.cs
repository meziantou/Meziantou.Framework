using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.Json.Internals;

/// <summary>Changes the values of a <see cref="JsonNode"/> tree that a query selects.</summary>
/// <remarks>
/// The query is evaluated first and every location it selects is found before the tree changes, so a change never
/// affects which values the other changes apply to. Locations are found from the root by their normalized paths rather
/// than from the matched nodes, so that a value read out of a <see cref="JsonValue"/>, which is a detached copy, is
/// reported instead of being changed silently.
/// </remarks>
internal static class JsonNodeModifier
{
    public static int Set(JsonPathExpression expression, JsonNode? root, JsonNode? value)
    {
        var segments = expression.Segments;
        if (segments.Length is 0)
            throw RootCannotChange();

        if (IsSingularPath(segments))
            return SetSingularPath(segments, root, value);

        var locations = new Locations(root);

        // A query ending with a name sets that member on every object the rest of the query selects, whether it exists or not
        if (segments[^1] is { Kind: SegmentKind.Child, Selectors: [NameSelector nameSelector] })
        {
            var parents = new List<JsonObject>();
            var seen = new HashSet<JsonNode>(ReferenceEqualityComparer.Instance);
            foreach (var match in Evaluate(segments[..^1], root))
            {
                if (AsContainer(locations.Find(match.PathNode)) is JsonObject obj && seen.Add(obj))
                {
                    parents.Add(obj);
                }
            }

            foreach (var parent in parents)
            {
                parent[nameSelector.Name] = value?.DeepClone();
            }

            return parents.Count;
        }

        var targets = FindChildren(Evaluate(segments, root), locations);
        foreach (var target in targets)
        {
            switch (target.Parent)
            {
                case JsonObject obj:
                    obj[target.Step.Name!] = value?.DeepClone();
                    break;
                case JsonArray array:
                    array[(int)target.Step.Index] = value?.DeepClone();
                    break;
            }
        }

        return targets.Count;
    }

    public static int Add(JsonPathExpression expression, JsonNode? root, JsonNode? value)
    {
        var locations = new Locations(root);
        var arrays = new List<JsonArray>();
        var seen = new HashSet<JsonNode>(ReferenceEqualityComparer.Instance);
        foreach (var match in Evaluate(expression.Segments, root))
        {
            if (AsContainer(locations.Find(match.PathNode)) is JsonArray array && seen.Add(array))
            {
                arrays.Add(array);
            }
        }

        foreach (var array in arrays)
        {
            array.Add(value?.DeepClone());
        }

        return arrays.Count;
    }

    public static int Remove(JsonPathExpression expression, JsonNode? root)
    {
        if (expression.Segments.Length is 0)
            throw RootCannotChange();

        var targets = FindChildren(Evaluate(expression.Segments, root), new Locations(root));

        // Removing an element moves the next ones, so the elements of an array are removed from the last one
        targets.Sort((left, right) => right.Step.Index.CompareTo(left.Step.Index));
        foreach (var target in targets)
        {
            switch (target.Parent)
            {
                case JsonObject obj:
                    obj.Remove(target.Step.Name!);
                    break;
                case JsonArray array:
                    array.RemoveAt((int)target.Step.Index);
                    break;
            }
        }

        return targets.Count;
    }

    private static JsonPathResult Evaluate(Segment[] segments, JsonNode? root)
    {
        return JsonPathEvaluator.Evaluate(new JsonPathExpression(segments), root, JsonPathEvaluationMode.Lax);
    }

    /// <summary>Finds the container and the step of every match, each one once.</summary>
    private static List<ChildLocation> FindChildren(JsonPathResult matches, Locations locations)
    {
        var targets = new List<ChildLocation>();
        var seen = new HashSet<ChildLocation>();
        foreach (var match in matches)
        {
            var step = match.PathNode ?? throw RootCannotChange();
            var parent = AsContainer(locations.Find(step.Parent))!;
            var target = new ChildLocation(parent, step);
            if (seen.Add(target))
            {
                targets.Add(target);
            }
        }

        return targets;
    }

    private static bool IsSingularPath(Segment[] segments)
    {
        foreach (var segment in segments)
        {
            if (segment is not { Kind: SegmentKind.Child, Selectors: [NameSelector or IndexSelector] })
                return false;
        }

        return true;
    }

    /// <summary>
    /// Sets the value at a path made of names and indexes, such as <c>$.a.b[0].c</c>. When a member is missing and the rest
    /// of the path is made of names only, the missing objects are created. Nothing changes when an index is out of range
    /// or a value on the path is not a container.
    /// </summary>
    private static int SetSingularPath(Segment[] segments, JsonNode? root, JsonNode? value)
    {
        var current = root;
        for (var i = 0; i < segments.Length; i++)
        {
            var isLast = i == segments.Length - 1;
            switch (segments[i].Selectors[0])
            {
                case NameSelector { Name: var name }:
                    if (AsContainer(current) is not JsonObject obj)
                        return 0;

                    if (isLast)
                    {
                        obj[name] = value?.DeepClone();
                        return 1;
                    }

                    if (TryGetMember(obj, name, out var member))
                    {
                        current = member;
                        break;
                    }

                    // A new object has no elements, so an index after the missing member cannot select anything
                    var rest = segments.AsSpan(i + 1);
                    foreach (var segment in rest)
                    {
                        if (segment.Selectors[0] is not NameSelector)
                            return 0;
                    }

                    foreach (var segment in rest)
                    {
                        var created = new JsonObject();
                        obj[name] = created;
                        obj = created;
                        name = ((NameSelector)segment.Selectors[0]).Name;
                    }

                    obj[name] = value?.DeepClone();
                    return 1;

                case IndexSelector { Index: var index }:
                    if (AsContainer(current) is not JsonArray array)
                        return 0;

                    if (index < 0)
                    {
                        index += array.Count;
                    }

                    if (index < 0 || index >= array.Count)
                        return 0;

                    if (isLast)
                    {
                        array[(int)index] = value?.DeepClone();
                        return 1;
                    }

                    current = array[(int)index];
                    break;
            }
        }

        throw new UnreachableException();
    }

    // Reads a member the way the navigator does: an object configured to ignore case is still matched by its exact name
    private static bool TryGetMember(JsonObject obj, string name, out JsonNode? member)
    {
        if (obj.Options?.PropertyNameCaseInsensitive is true)
        {
            foreach (var property in obj)
            {
                if (string.Equals(property.Key, name, StringComparison.Ordinal))
                {
                    member = property.Value;
                    return true;
                }
            }

            member = null;
            return false;
        }

        return obj.TryGetPropertyValue(name, out member);
    }

    /// <summary>Returns the node when it is a container that can change.</summary>
    /// <exception cref="InvalidOperationException">The node is a <see cref="JsonValue"/> that serializes as an object or an array.</exception>
    private static JsonNode? AsContainer(JsonNode? node)
    {
        if (node is JsonValue value && value.GetValueKind() is JsonValueKind.Object or JsonValueKind.Array)
            throw new InvalidOperationException("The value cannot change because it is part of a JsonValue that wraps an object or an array. Create the document with JsonNode.Parse or with JsonObject and JsonArray instead.");

        return node;
    }

    private static InvalidOperationException RootCannotChange()
    {
        return new InvalidOperationException("The query selects the root value, which has no parent: replace the root value itself instead.");
    }

    private readonly record struct ChildLocation(JsonNode Parent, PathNode Step)
    {
        public bool Equals(ChildLocation other)
        {
            return ReferenceEquals(Parent, other.Parent)
                && string.Equals(Step.Name, other.Step.Name, StringComparison.Ordinal)
                && Step.Index == other.Step.Index;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(RuntimeHelpers.GetHashCode(Parent), Step.Name, Step.Index);
        }
    }

    /// <summary>Finds the nodes at the paths of the matches, walking each step from the root once.</summary>
    private sealed class Locations
    {
        private readonly JsonNode? _root;
        private readonly Dictionary<PathNode, JsonNode?> _nodes = new(ReferenceEqualityComparer.Instance);

        public Locations(JsonNode? root)
        {
            _root = root;
        }

        public JsonNode? Find(PathNode? path)
        {
            if (path is null)
                return _root;

            if (_nodes.TryGetValue(path, out var node))
                return node;

            node = AsContainer(Find(path.Parent)) switch
            {
                JsonObject obj when path.Name is not null && TryGetMember(obj, path.Name, out var member) => member,
                JsonArray array when path.Name is null => array[(int)path.Index],
                _ => throw new InvalidOperationException("The document changed during the evaluation of the query."),
            };

            _nodes.Add(path, node);
            return node;
        }
    }
}
