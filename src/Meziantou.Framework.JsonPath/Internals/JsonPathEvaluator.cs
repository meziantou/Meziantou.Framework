using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Json.Internals;

/// <summary>Evaluates a parsed JSONPath AST against a JSON value tree.</summary>
internal static class JsonPathEvaluator
{
    /// <summary>
    /// Maximum node depth visited by a descendant segment or a deep equality comparison. Both walks are
    /// recursive, so an unbounded depth would overflow the stack, which cannot be caught and terminates the
    /// process. It also terminates cycles, which a custom <see cref="JsonPathNavigator{TValue}"/> may expose.
    /// The value leaves ample room above the default <see cref="System.Text.Json.JsonDocumentOptions.MaxDepth"/>
    /// of 64 that bounds any document produced by System.Text.Json's own parsers.
    /// </summary>
    private const int MaxRecursionDepth = 256;
    private const int MaxDirectSliceElements = 4;

    /// <summary>
    /// Backstop for a single <c>match()</c>/<c>search()</c> evaluation. <see cref="RegexOptions.NonBacktracking"/>
    /// already guarantees linear time, so this only bounds pathologically long inputs. It is applied per node,
    /// so it must stay small: a filter over a large array multiplies it by the node count.
    /// </summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public static JsonPathResult Evaluate(JsonPathExpression expression, JsonNode? root)
    {
        return Evaluate(expression, root, JsonPathEvaluationMode.Lax);
    }

    public static JsonPathResult Evaluate(JsonPathExpression expression, JsonNode? root, JsonPathEvaluationMode mode, JsonPathEvaluationOptions? options = null)
    {
        var result = Evaluate(expression, root, JsonNodeNavigator.Instance, mode, options);
        var matches = new List<JsonPathMatch>(result.Count);
        foreach (var match in result)
        {
            options?.CancellationToken.ThrowIfCancellationRequested();
            matches.Add(new JsonPathMatch(match.Value, match.RawPath));
        }

        return new JsonPathResult(matches);
    }

    public static JsonPathResult<TValue> Evaluate<TValue>(
        JsonPathExpression expression,
        TValue? root,
        JsonPathNavigator<TValue> navigator,
        JsonPathEvaluationMode mode,
        JsonPathEvaluationOptions? options = null)
    {
        navigator = new EvaluationNavigator<TValue>(navigator, options);
        var context = new SegmentContext<TValue>(root, navigator, mode, trackPaths: true, limit: int.MaxValue);
        NodeSink<TValue> currentNodes = [(root, null)];

        for (var i = 0; i < expression.Segments.Length; i++)
        {
            var result = new NodeSink<TValue>((EvaluationNavigator<TValue>)navigator, final: i == expression.Segments.Length - 1);
            ApplySegment(expression.Segments[i], currentNodes, in context, result);
            currentNodes = result;
        }

        ((EvaluationNavigator<TValue>)navigator).Check();
        if (expression.Segments.Length is 0 && ((EvaluationNavigator<TValue>)navigator).MaximumResults is 0)
            throw new JsonPathEvaluationException("The maximum result count was exceeded.");

        var matches = new List<JsonPathMatch<TValue>>(currentNodes.Count);
        foreach (var (node, path) in currentNodes)
        {
            ((EvaluationNavigator<TValue>)navigator).Check();
            matches.Add(new JsonPathMatch<TValue>(node, new NormalizedPath(path)));
        }

        return new JsonPathResult<TValue>(matches);
    }

    public static (bool Found, TValue? Value) EvaluateFirst<TValue>(
        JsonPathExpression expression,
        TValue? root,
        JsonPathNavigator<TValue> navigator,
        JsonPathEvaluationMode mode,
        JsonPathEvaluationOptions? options = null)
    {
        var evaluation = new EvaluationNavigator<TValue>(navigator, options);
        var context = new SegmentContext<TValue>(root, evaluation, mode, trackPaths: mode is JsonPathEvaluationMode.Strict, limit: int.MaxValue);
        // Bound continuation depth independently of document depth. Long paths use the iterative pipeline.
        if (mode is JsonPathEvaluationMode.Lax && evaluation.MaximumResults is null && evaluation.MaximumIntermediateResults is null && CanStream(expression))
        {
            var first = new NodeSink<TValue>(evaluation, final: true, retention: 1);
            EvaluateNext(0, root);
            evaluation.Check();
            return first.Count > 0 ? (true, first[0].Node) : (false, default);

            void EvaluateNext(int index, TValue? node)
            {
                if (index == expression.Segments.Length)
                {
                    first.Add((node, null));
                    return;
                }

                var sink = new NodeSink<TValue>(evaluation, retention: 0,
                    onNode: match => EvaluateNext(index + 1, match.Node), completed: () => first.Count > 0);
                var segmentContext = index == expression.Segments.Length - 1
                    ? new SegmentContext<TValue>(root, evaluation, mode, trackPaths: false, limit: 1)
                    : context;
                ApplySegment(expression.Segments[index], node, path: null, in segmentContext, sink);
            }
        }

        NodeSink<TValue> nodes = [(root, null)];
        for (var i = 0; i < expression.Segments.Length; i++)
        {
            var final = i == expression.Segments.Length - 1;
            var result = new NodeSink<TValue>(evaluation, final, retention: final ? 1 : int.MaxValue);
            var segmentContext = final && mode is JsonPathEvaluationMode.Lax && evaluation.MaximumResults is null
                ? new SegmentContext<TValue>(root, evaluation, mode, trackPaths: false, limit: 1)
                : context;
            if (final && mode is JsonPathEvaluationMode.Strict && expression.Segments[i].Kind is SegmentKind.Child)
                segmentContext = new SegmentContext<TValue>(root, evaluation, mode, trackPaths: false, limit: int.MaxValue);

            ApplySegment(expression.Segments[i], nodes, in segmentContext, result);
            nodes = result;
        }

        evaluation.Check();
        if (expression.Segments.Length is 0 && evaluation.MaximumResults is 0)
            throw new JsonPathEvaluationException("The maximum result count was exceeded.");

        return nodes.Count > 0 ? (true, nodes[0].Node) : (false, default);
    }

    private static bool CanStream(JsonPathExpression expression)
    {
        if (expression.Segments.Length is 0 or > 64)
            return false;

        for (var i = 0; i < expression.Segments.Length - 1; i++)
        {
            if (expression.Segments[i].Kind is SegmentKind.Descendant)
                return false;
        }

        return true;
    }

    private static NodeSink<TValue> ApplySegment<TValue>(
        Segment segment,
        NodeSink<TValue> inputNodes,
        in SegmentContext<TValue> context)
    {
        var result = new NodeSink<TValue>((EvaluationNavigator<TValue>)context.Navigator);
        ApplySegment(segment, inputNodes, in context, result);
        return result;
    }

    private static void ApplySegment<TValue>(
        Segment segment,
        NodeSink<TValue> inputNodes,
        in SegmentContext<TValue> context,
        NodeSink<TValue> result)
    {
        foreach (var (node, path) in inputNodes)
        {
            if (result.IsComplete(context.Limit))
            {
                break;
            }

            ApplySegment(segment, node, path, in context, result);
        }
    }

    private static void ApplySegment<TValue>(
        Segment segment,
        TValue? node,
        PathNode? path,
        in SegmentContext<TValue> context,
        NodeSink<TValue> result)
    {
        ((EvaluationNavigator<TValue>)context.Navigator).Check();
        if (segment.Kind is SegmentKind.Child)
        {
            ApplyChildSegment(segment.Selectors, node, path, in context, result, strictFailure: true);
        }
        else
        {
            VisitDescendants(node, path, segment.Selectors, in context, result, depth: 0);
        }
    }

    private static void ApplyChildSegment<TValue>(
        Selector[] selectors,
        TValue? node,
        PathNode? path,
        in SegmentContext<TValue> context,
        NodeSink<TValue> result,
        bool strictFailure)
    {
        foreach (var selector in selectors)
        {
            ((EvaluationNavigator<TValue>)context.Navigator).Check();
            if (result.IsComplete(context.Limit))
            {
                return;
            }

            ApplySelector(selector, node, path, in context, result, strictFailure);
        }
    }

    private static void VisitDescendants<TValue>(
        TValue? node,
        PathNode? path,
        Selector[] selectors,
        in SegmentContext<TValue> context,
        NodeSink<TValue> result,
        int depth)
    {
        if (depth > MaxRecursionDepth)
        {
            // Only name a location when paths are being tracked; otherwise 'path' is always the root and would
            // misreport where the limit was hit.
            var location = context.TrackPaths ? $" at {NormalizedPathBuilder.Build(path)}" : "";
            throw new JsonPathEvaluationException($"Maximum recursion depth of {MaxRecursionDepth} exceeded{location}. The value is too deeply nested, or the navigator exposes a cycle.");
        }

        ApplyChildSegment(selectors, node, path, in context, result, strictFailure: false);
        if (result.IsComplete(context.Limit))
            return;

        var navigator = context.Navigator;
        switch (navigator.GetKind(node))
        {
            case JsonPathNodeKind.Object:
                foreach (var property in navigator.GetProperties(node))
                {
                    if (result.IsComplete(context.Limit))
                    {
                        return;
                    }

                    var childPath = ExtendWithName(path, property.Name, context.TrackPaths);
                    VisitDescendants(property.Value, childPath, selectors, in context, result, depth + 1);
                }

                break;

            case JsonPathNodeKind.Array:
                using (var elements = new ArrayElements<TValue>(navigator, node))
                {
                    for (var i = 0; i < elements.Length; i++)
                    {
                        if (result.IsComplete(context.Limit))
                        {
                            return;
                        }

                        if (!elements.TryGet(i, out var value))
                        {
                            continue;
                        }

                        var childPath = ExtendWithIndex(path, i, context.TrackPaths);
                        VisitDescendants(value, childPath, selectors, in context, result, depth + 1);
                    }
                }

                break;
        }
    }

    private static void ApplySelector<TValue>(
        Selector selector,
        TValue? node,
        PathNode? path,
        in SegmentContext<TValue> context,
        NodeSink<TValue> result,
        bool strictFailure)
    {
        switch (selector)
        {
            case NameSelector nameSelector:
                ApplyNameSelector(nameSelector, node, path, in context, result, strictFailure);
                break;
            case WildcardSelector:
                ApplyWildcardSelector(node, path, in context, result, strictFailure);
                break;
            case IndexSelector indexSelector:
                ApplyIndexSelector(indexSelector, node, path, in context, result, strictFailure);
                break;
            case SliceSelector sliceSelector:
                ApplySliceSelector(sliceSelector, node, path, in context, result, strictFailure);
                break;
            case FilterSelector filterSelector:
                ApplyFilterSelector(filterSelector, node, path, in context, result, strictFailure);
                break;
        }
    }

    private static void ApplyNameSelector<TValue>(
        NameSelector selector,
        TValue? node,
        PathNode? path,
        in SegmentContext<TValue> context,
        NodeSink<TValue> result,
        bool strictFailure)
    {
        var navigator = context.Navigator;
        if (navigator.GetKind(node) is JsonPathNodeKind.Object)
        {
            if (!navigator.TryGetPropertyValue(node, selector.Name, out var value))
            {
                ThrowPathEvaluationErrorIfStrict(in context, strictFailure, path, $"Object member '{selector.Name}' does not exist");
                return;
            }

            result.Add((value, ExtendWithName(path, selector.Name, context.TrackPaths)));
            return;
        }

        ThrowPathEvaluationErrorIfStrict(in context, strictFailure, path, $"Name selector '{selector.Name}' requires an object");
    }

    private static void ApplyWildcardSelector<TValue>(
        TValue? node,
        PathNode? path,
        in SegmentContext<TValue> context,
        NodeSink<TValue> result,
        bool strictFailure)
    {
        var navigator = context.Navigator;
        switch (navigator.GetKind(node))
        {
            case JsonPathNodeKind.Object:
                foreach (var property in navigator.GetProperties(node))
                {
                    if (result.IsComplete(context.Limit))
                    {
                        return;
                    }

                    result.Add((property.Value, ExtendWithName(path, property.Name, context.TrackPaths)));
                }

                break;

            case JsonPathNodeKind.Array:
                var start = 0;
                if ((result.HasDemand || context.Limit is 1) && navigator.GetArrayLength(node) > 0)
                {
                    if (navigator.TryGetElement(node, 0, out var first))
                        result.Add((first, ExtendWithIndex(path, 0, context.TrackPaths)));

                    if (result.IsComplete(context.Limit))
                        return;

                    start = 1;
                }

                using (var elements = new ArrayElements<TValue>(navigator, node))
                {
                    for (var i = start; i < elements.Length; i++)
                    {
                        if (result.IsComplete(context.Limit))
                        {
                            return;
                        }

                        if (!elements.TryGet(i, out var value))
                        {
                            continue;
                        }

                        result.Add((value, ExtendWithIndex(path, i, context.TrackPaths)));
                    }
                }

                break;

            default:
                ThrowPathEvaluationErrorIfStrict(in context, strictFailure, path, "Wildcard selector requires an object or an array");
                break;
        }
    }

    private static void ApplyIndexSelector<TValue>(
        IndexSelector selector,
        TValue? node,
        PathNode? path,
        in SegmentContext<TValue> context,
        NodeSink<TValue> result,
        bool strictFailure)
    {
        var navigator = context.Navigator;
        if (navigator.GetKind(node) is not JsonPathNodeKind.Array)
        {
            ThrowPathEvaluationErrorIfStrict(in context, strictFailure, path, $"Index selector [{selector.Index}] requires an array");
            return;
        }

        // A single element is read directly: copying the array to reach it would cost more than any indexer does.
        var length = navigator.GetArrayLength(node);
        var index = NormalizeIndex(selector.Index, length);
        if (index >= 0 && index < length && navigator.TryGetElement(node, (int)index, out var value))
        {
            result.Add((value, ExtendWithIndex(path, index, context.TrackPaths)));
            return;
        }

        ThrowPathEvaluationErrorIfStrict(in context, strictFailure, path, $"Array index [{selector.Index}] is out of range");
    }

    private static void ApplySliceSelector<TValue>(
        SliceSelector selector,
        TValue? node,
        PathNode? path,
        in SegmentContext<TValue> context,
        NodeSink<TValue> result,
        bool strictFailure)
    {
        var navigator = context.Navigator;
        if (navigator.GetKind(node) is not JsonPathNodeKind.Array)
        {
            ThrowPathEvaluationErrorIfStrict(in context, strictFailure, path, "Slice selector requires an array");
            return;
        }

        var step = selector.Step ?? 1;

        if (step == 0)
        {
            return;
        }

        var len = navigator.GetArrayLength(node);
        var start = selector.Start ?? (step >= 0 ? 0 : len - 1);
        var end = selector.End ?? (step >= 0 ? len : -len - 1);

        var nStart = Normalize(start, len);
        var nEnd = Normalize(end, len);

        long lower, upper;
        if (step >= 0)
        {
            lower = Math.Clamp(nStart, 0, len);
            upper = Math.Clamp(nEnd, 0, len);
        }
        else
        {
            upper = Math.Clamp(nStart, -1, len - 1);
            lower = Math.Clamp(nEnd, -1, len - 1);
        }

        var selectionLength = upper - lower;
        if (selectionLength <= 0)
        {
            return;
        }

        var useDirectAccess = context.Limit is 1 || ShouldUseDirectArrayAccess(lower, upper, step, len);
        using var elements = new ArrayElements<TValue>(navigator, node, copyElements: !useDirectAccess);

        if (step > 0)
        {
            for (var i = lower; i < upper;)
            {
                if (result.IsComplete(context.Limit))
                {
                    return;
                }

                if (elements.TryGet((int)i, out var value))
                {
                    result.Add((value, ExtendWithIndex(path, i, context.TrackPaths)));
                }

                if (step >= upper - i)
                {
                    break;
                }

                i += step;
            }
        }

        else
        {
            for (var i = upper; lower < i;)
            {
                if (result.IsComplete(context.Limit))
                {
                    return;
                }

                if (elements.TryGet((int)i, out var value))
                {
                    result.Add((value, ExtendWithIndex(path, i, context.TrackPaths)));
                }

                if (step <= lower - i)
                {
                    break;
                }

                i += step;
            }
        }
    }

    private static bool ShouldUseDirectArrayAccess(long lower, long upper, long step, int arrayLength)
    {
        var selectedCount = 0;
        long estimatedAccessCount = 0;
        if (step > 0)
        {
            for (var index = lower; index < upper;)
            {
                selectedCount++;
                if (selectedCount > MaxDirectSliceElements)
                {
                    return false;
                }

                estimatedAccessCount += index + 1;
                if (selectedCount > 1 && estimatedAccessCount >= arrayLength)
                {
                    return false;
                }

                if (step >= upper - index)
                {
                    break;
                }

                index += step;
            }
        }
        else
        {
            for (var index = upper; lower < index;)
            {
                selectedCount++;
                if (selectedCount > MaxDirectSliceElements)
                {
                    return false;
                }

                estimatedAccessCount += index + 1;
                if (selectedCount > 1 && estimatedAccessCount >= arrayLength)
                {
                    return false;
                }

                if (step <= lower - index)
                {
                    break;
                }

                index += step;
            }
        }

        return true;
    }

    private static void ApplyFilterSelector<TValue>(
        FilterSelector selector,
        TValue? node,
        PathNode? path,
        in SegmentContext<TValue> context,
        NodeSink<TValue> result,
        bool strictFailure)
    {
        var navigator = context.Navigator;
        var root = context.Root;
        switch (navigator.GetKind(node))
        {
            case JsonPathNodeKind.Object:
                foreach (var property in navigator.GetProperties(node))
                {
                    if (result.IsComplete(context.Limit))
                    {
                        return;
                    }

                    if (EvaluateLogicalExpression(selector.Expression, property.Value, root, navigator))
                    {
                        result.Add((property.Value, ExtendWithName(path, property.Name, context.TrackPaths)));
                    }
                }

                break;

            case JsonPathNodeKind.Array:
                var start = 0;
                if ((result.HasDemand || context.Limit is 1) && navigator.GetArrayLength(node) > 0)
                {
                    if (navigator.TryGetElement(node, 0, out var first) && EvaluateLogicalExpression(selector.Expression, first, root, navigator))
                        result.Add((first, ExtendWithIndex(path, 0, context.TrackPaths)));

                    if (result.IsComplete(context.Limit))
                        return;

                    start = 1;
                }

                using (var elements = new ArrayElements<TValue>(navigator, node))
                {
                    for (var i = start; i < elements.Length; i++)
                    {
                        if (result.IsComplete(context.Limit))
                        {
                            return;
                        }

                        if (!elements.TryGet(i, out var value))
                        {
                            continue;
                        }

                        if (EvaluateLogicalExpression(selector.Expression, value, root, navigator))
                        {
                            result.Add((value, ExtendWithIndex(path, i, context.TrackPaths)));
                        }
                    }
                }

                break;

            default:
                ThrowPathEvaluationErrorIfStrict(in context, strictFailure, path, "Filter selector requires an object or an array");
                break;
        }
    }

    /// <summary>Extends a path with a member name, or returns it untouched when path tracking is off.</summary>
    /// <param name="path">The path so far.</param>
    /// <param name="name">The member name.</param>
    /// <param name="trackPaths">Whether the caller will read the resulting paths.</param>
    /// <returns>The extended path, or <paramref name="path"/> when tracking is off.</returns>
    private static PathNode? ExtendWithName(PathNode? path, string name, bool trackPaths)
    {
        return trackPaths ? PathNode.FromName(path, name) : path;
    }

    /// <summary>Extends a path with an array index, or returns it untouched when path tracking is off.</summary>
    /// <param name="path">The path so far.</param>
    /// <param name="index">The array index.</param>
    /// <param name="trackPaths">Whether the caller will read the resulting paths.</param>
    /// <returns>The extended path, or <paramref name="path"/> when tracking is off.</returns>
    private static PathNode? ExtendWithIndex(PathNode? path, long index, bool trackPaths)
    {
        return trackPaths ? PathNode.FromIndex(path, index) : path;
    }

    private static void ThrowPathEvaluationErrorIfStrict<TValue>(in SegmentContext<TValue> context, bool strictFailure, PathNode? path, string error)
    {
        if (context.Mode is not JsonPathEvaluationMode.Strict || !strictFailure)
        {
            return;
        }

        throw new JsonPathEvaluationException($"Path error at {NormalizedPathBuilder.Build(path)}: {error}");
    }

    private static bool EvaluateLogicalExpression<TValue>(
        LogicalExpression expr,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
    {
        ((EvaluationNavigator<TValue>)navigator).Check();
        return expr switch
        {
            // The operands of a chain are held in a list rather than nested pairwise, so a chain of any length
            // costs a single evaluation frame. Both operators still short-circuit.
            OrExpression or => EvaluateAnyOperand(or.Operands, currentNode, root, navigator),
            AndExpression and => EvaluateEveryOperand(and.Operands, currentNode, root, navigator),
            NotExpression not => !EvaluateLogicalExpression(not.Operand, currentNode, root, navigator),
            ComparisonExpression comp => EvaluateComparison(comp, currentNode, root, navigator),
            ExistenceTestExpression existence => EvaluateExistenceTest(existence, currentNode, root, navigator),
            FunctionCallExpression func => EvaluateFunctionAsLogical(func, currentNode, root, navigator),
            _ => false,
        };
    }

    private static bool EvaluateAnyOperand<TValue>(
        LogicalExpression[] operands,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
    {
        foreach (var operand in operands)
        {
            if (EvaluateLogicalExpression(operand, currentNode, root, navigator))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EvaluateEveryOperand<TValue>(
        LogicalExpression[] operands,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
    {
        foreach (var operand in operands)
        {
            if (!EvaluateLogicalExpression(operand, currentNode, root, navigator))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EvaluateExistenceTest<TValue>(
        ExistenceTestExpression test,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
    {
        // Only whether a node exists matters, so stop at the first one.
        var nodes = EvaluateFilterQuery(test.Query, currentNode, root, navigator, limit: 1);
        return nodes.Count > 0;
    }

    private static bool EvaluateComparison<TValue>(
        ComparisonExpression comp,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
    {
        var left = ResolveComparable(comp.Left, currentNode, root, navigator);
        var right = ResolveComparable(comp.Right, currentNode, root, navigator);

        return comp.Operator switch
        {
            ComparisonOperator.Equal => CompareEqual(left, right, navigator),
            ComparisonOperator.NotEqual => !CompareEqual(left, right, navigator),
            ComparisonOperator.LessThan => CompareLessThan(left, right, navigator),
            ComparisonOperator.LessThanOrEqual => CompareLessThan(left, right, navigator) || CompareEqual(left, right, navigator),
            ComparisonOperator.GreaterThan => CompareLessThan(right, left, navigator),
            ComparisonOperator.GreaterThanOrEqual => CompareLessThan(right, left, navigator) || CompareEqual(left, right, navigator),
            _ => false,
        };
    }

    private enum ResolvedValueKind
    {
        Nothing,
        Node,
        Scalar,
    }

    private readonly struct ResolvedValue<TValue>
    {
        private ResolvedValue(TValue? node)
        {
            Kind = ResolvedValueKind.Node;
            Node = node;
            Scalar = default;
        }

        private ResolvedValue(ScalarValue scalar)
        {
            Kind = ResolvedValueKind.Scalar;
            Node = default;
            Scalar = scalar;
        }

        public ResolvedValueKind Kind { get; }

        public TValue? Node { get; }

        public ScalarValue Scalar { get; }

        public bool IsNothing => Kind is ResolvedValueKind.Nothing;

        public static ResolvedValue<TValue> FromNode(TValue? node) => new(node);

        public static ResolvedValue<TValue> FromScalar(ScalarValue scalar) => new(scalar);

        public static ResolvedValue<TValue> FromNothing() => default;
    }

    private readonly struct ScalarValue
    {
        private ScalarValue(JsonPathNodeKind kind, string? stringValue, double numberValue, bool booleanValue)
        {
            Kind = kind;
            StringValue = stringValue;
            NumberValue = numberValue;
            BooleanValue = booleanValue;
        }

        public JsonPathNodeKind Kind { get; }

        public string? StringValue { get; }

        public double NumberValue { get; }

        public bool BooleanValue { get; }

        public static ScalarValue Null() => new(JsonPathNodeKind.Null, stringValue: null, numberValue: 0, booleanValue: false);

        public static ScalarValue FromBoolean(bool value) => new(JsonPathNodeKind.Boolean, stringValue: null, numberValue: 0, booleanValue: value);

        public static ScalarValue FromNumber(double value) => new(JsonPathNodeKind.Number, stringValue: null, numberValue: value, booleanValue: false);

        public static ScalarValue FromString(string value) => new(JsonPathNodeKind.String, value, numberValue: 0, booleanValue: false);
    }

    private static ResolvedValue<TValue> ResolveComparable<TValue>(
        Comparable comparable,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
    {
        switch (comparable)
        {
            case LiteralComparable literal:
                return literal.Value switch
                {
                    null => ResolvedValue<TValue>.FromScalar(ScalarValue.Null()),
                    true => ResolvedValue<TValue>.FromScalar(ScalarValue.FromBoolean(value: true)),
                    false => ResolvedValue<TValue>.FromScalar(ScalarValue.FromBoolean(value: false)),
                    string s => ResolvedValue<TValue>.FromScalar(ScalarValue.FromString(s)),
                    double d => ResolvedValue<TValue>.FromScalar(ScalarValue.FromNumber(d)),
                    _ => ResolvedValue<TValue>.FromNothing(),
                };

            case SingularQueryComparable sq:
            {
                return TryEvaluateSingularQuery(sq.Query, currentNode, root, navigator, out var node)
                    ? ResolvedValue<TValue>.FromNode(node)
                    : ResolvedValue<TValue>.FromNothing();
            }

            case FunctionCallComparable fc:
                return EvaluateFunction(fc.FunctionCall, currentNode, root, navigator);

            default:
                return ResolvedValue<TValue>.FromNothing();
        }
    }

    private static bool CompareEqual<TValue>(
        ResolvedValue<TValue> left,
        ResolvedValue<TValue> right,
        JsonPathNavigator<TValue> navigator)
    {
        if (left.IsNothing && right.IsNothing)
        {
            return true;
        }

        if (left.IsNothing || right.IsNothing)
        {
            return false;
        }

        if (left.Kind is ResolvedValueKind.Scalar && right.Kind is ResolvedValueKind.Scalar)
        {
            return ScalarValuesEqual(left.Scalar, right.Scalar, navigator);
        }

        if (left.Kind is ResolvedValueKind.Scalar)
        {
            return ScalarAndNodeEqual(left.Scalar, right.Node, navigator);
        }

        if (right.Kind is ResolvedValueKind.Scalar)
        {
            return ScalarAndNodeEqual(right.Scalar, left.Node, navigator);
        }

        return NodesEqual(left.Node, right.Node, navigator, depth: 0);
    }

    private static bool CompareLessThan<TValue>(
        ResolvedValue<TValue> left,
        ResolvedValue<TValue> right,
        JsonPathNavigator<TValue> navigator)
    {
        if (left.IsNothing || right.IsNothing)
        {
            return false;
        }

        if (TryGetNumber(left, navigator, out var leftNumber) && TryGetNumber(right, navigator, out var rightNumber))
        {
            return leftNumber < rightNumber;
        }

        if (TryGetString(left, navigator, out var leftString) && TryGetString(right, navigator, out var rightString))
        {
            return CompareByScalarValue(leftString, rightString, navigator) < 0;
        }

        return false;
    }

    /// <summary>
    /// Orders two strings the way RFC 9535 §2.3.5.2.2 does, by their Unicode scalar values. An ordinal comparison
    /// would order them by UTF-16 code unit instead, which puts every supplementary character before U+E000 to
    /// U+FFFF because it is written as a surrogate pair.
    /// </summary>
    /// <param name="left">The first string.</param>
    /// <param name="right">The second string.</param>
    /// <returns>A negative number, zero or a positive number, as <see cref="string.CompareTo(string)"/> does.</returns>
    private static int CompareByScalarValue<TValue>(string? left, string? right, JsonPathNavigator<TValue> navigator)
    {
        if (left is null || right is null)
        {
            return (left is null ? 0 : 1) - (right is null ? 0 : 1);
        }

        var shared = Math.Min(left.Length, right.Length);
        for (var i = 0; i < shared; i++)
        {
            ((EvaluationNavigator<TValue>)navigator).Check();
            if (left[i] != right[i])
            {
                return ScalarOrderOf(left[i]) - ScalarOrderOf(right[i]);
            }
        }

        return left.Length - right.Length;
    }

    /// <summary>
    /// Ranks a code unit so that comparing units one by one yields the scalar value order: a surrogate stands for
    /// a scalar value above the BMP, so it has to rank above every character that is not one. Sorting the halves
    /// of a pair among themselves keeps their order, since both halves grow with the scalar value they encode.
    /// </summary>
    private static int ScalarOrderOf(char value) => char.IsSurrogate(value) ? value + 0x10000 : value;

    private static bool ScalarValuesEqual<TValue>(ScalarValue left, ScalarValue right, JsonPathNavigator<TValue> navigator)
    {
        if (left.Kind != right.Kind)
        {
            return false;
        }

        return left.Kind switch
        {
            JsonPathNodeKind.Null => true,
            JsonPathNodeKind.Boolean => left.BooleanValue == right.BooleanValue,
            JsonPathNodeKind.Number => left.NumberValue.CompareTo(right.NumberValue) is 0,
            JsonPathNodeKind.String => StringsEqual(left.StringValue, right.StringValue, navigator),
            _ => false,
        };
    }

    private static bool ScalarAndNodeEqual<TValue>(ScalarValue scalar, TValue? node, JsonPathNavigator<TValue> navigator)
    {
        var kind = navigator.GetKind(node);
        if (scalar.Kind != kind)
        {
            return false;
        }

        return scalar.Kind switch
        {
            JsonPathNodeKind.Null => true,
            JsonPathNodeKind.Boolean => navigator.TryGetBoolean(node, out var value) && scalar.BooleanValue == value,
            JsonPathNodeKind.Number => navigator.TryGetNumber(node, out var value) && scalar.NumberValue.CompareTo(value) is 0,
            JsonPathNodeKind.String => navigator.TryGetString(node, out var value) && StringsEqual(scalar.StringValue, value, navigator),
            _ => false,
        };
    }

    private static bool NodesEqual<TValue>(TValue? left, TValue? right, JsonPathNavigator<TValue> navigator, int depth)
    {
        if (depth > MaxRecursionDepth)
        {
            throw new JsonPathEvaluationException($"Maximum recursion depth of {MaxRecursionDepth} exceeded while comparing two values. A value is too deeply nested, or the navigator exposes a cycle.");
        }

        var leftKind = navigator.GetKind(left);
        var rightKind = navigator.GetKind(right);
        if (leftKind != rightKind)
        {
            return false;
        }

        switch (leftKind)
        {
            case JsonPathNodeKind.Null:
                return true;

            case JsonPathNodeKind.Boolean:
                return navigator.TryGetBoolean(left, out var leftBoolean)
                       && navigator.TryGetBoolean(right, out var rightBoolean)
                       && leftBoolean == rightBoolean;

            case JsonPathNodeKind.Number:
                return navigator.TryGetNumber(left, out var leftNumber)
                       && navigator.TryGetNumber(right, out var rightNumber)
                       && leftNumber.CompareTo(rightNumber) is 0;

            case JsonPathNodeKind.String:
                return navigator.TryGetString(left, out var leftString)
                       && navigator.TryGetString(right, out var rightString)
                       && StringsEqual(leftString, rightString, navigator);

            case JsonPathNodeKind.Array:
                return ArraysEqual(left, right, navigator, depth);

            case JsonPathNodeKind.Object:
                return ObjectsEqual(left, right, navigator, depth);

            default:
                return false;
        }
    }

    private static bool StringsEqual<TValue>(string? left, string? right, JsonPathNavigator<TValue> navigator)
    {
        if (!ReferenceEquals(left, right) && left is not null && right is not null && left.Length == right.Length)
            ((EvaluationNavigator<TValue>)navigator).Check(left.Length);

        return string.Equals(left, right, StringComparison.Ordinal);
    }

    private static bool ArraysEqual<TValue>(TValue? left, TValue? right, JsonPathNavigator<TValue> navigator, int depth)
    {
        if (navigator.GetArrayLength(left) != navigator.GetArrayLength(right))
        {
            return false;
        }

        using var leftElements = new ArrayElements<TValue>(navigator, left);
        using var rightElements = new ArrayElements<TValue>(navigator, right);
        for (var i = 0; i < leftElements.Length; i++)
        {
            if (!leftElements.TryGet(i, out var leftValue) || !rightElements.TryGet(i, out var rightValue))
            {
                return false;
            }

            if (!NodesEqual(leftValue, rightValue, navigator, depth + 1))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ObjectsEqual<TValue>(TValue? left, TValue? right, JsonPathNavigator<TValue> navigator, int depth)
    {
        if (navigator.HasConstantTimePropertyLookup)
        {
            var leftCount = 0;
            foreach (var property in navigator.GetProperties(left))
            {
                leftCount++;
                if (!navigator.TryGetPropertyValue(right, property.Name, out var rightValue))
                {
                    return false;
                }

                if (!NodesEqual(property.Value, rightValue, navigator, depth + 1))
                {
                    return false;
                }
            }

            return leftCount == CountProperties(right, navigator);
        }

        var leftProperties = new Dictionary<string, TValue?>(StringComparer.Ordinal);
        foreach (var property in navigator.GetProperties(left))
        {
            leftProperties[property.Name] = property.Value;
        }

        var rightProperties = new Dictionary<string, TValue?>(StringComparer.Ordinal);
        foreach (var property in navigator.GetProperties(right))
        {
            rightProperties[property.Name] = property.Value;
        }

        if (leftProperties.Count != rightProperties.Count)
        {
            return false;
        }

        foreach (var (name, leftValue) in leftProperties)
        {
            if (!rightProperties.TryGetValue(name, out var rightValue))
            {
                return false;
            }

            if (!NodesEqual(leftValue, rightValue, navigator, depth + 1))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetNumber<TValue>(ResolvedValue<TValue> value, JsonPathNavigator<TValue> navigator, out double result)
    {
        if (value.Kind is ResolvedValueKind.Scalar)
        {
            if (value.Scalar.Kind is JsonPathNodeKind.Number)
            {
                result = value.Scalar.NumberValue;
                return true;
            }

            result = 0;
            return false;
        }

        if (navigator.GetKind(value.Node) is JsonPathNodeKind.Number)
        {
            return navigator.TryGetNumber(value.Node, out result);
        }

        result = 0;
        return false;
    }

    private static bool TryGetString<TValue>(ResolvedValue<TValue> value, JsonPathNavigator<TValue> navigator, out string? result)
    {
        if (value.Kind is ResolvedValueKind.Scalar)
        {
            if (value.Scalar.Kind is JsonPathNodeKind.String)
            {
                result = value.Scalar.StringValue;
                return true;
            }

            result = null;
            return false;
        }

        if (navigator.GetKind(value.Node) is JsonPathNodeKind.String)
        {
            return navigator.TryGetString(value.Node, out result);
        }

        result = null;
        return false;
    }

    /// <summary>Evaluates a filter query, stopping once it has produced <paramref name="limit"/> nodes.</summary>
    /// <param name="query">The query.</param>
    /// <param name="currentNode">The node <c>@</c> stands for.</param>
    /// <param name="root">The node <c>$</c> stands for.</param>
    /// <param name="navigator">The navigator.</param>
    /// <param name="limit">
    /// How many nodes the caller needs: 1 to test for existence, 2 to tell a single node from several. Nodes past it
    /// cannot change the caller's answer, so they are not searched for.
    /// </param>
    /// <returns>The nodes, in document order, up to <paramref name="limit"/>. Their paths are not tracked.</returns>
    private static NodeSink<TValue> EvaluateFilterQuery<TValue>(
        FilterQuery query,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator,
        int limit,
        bool countOnly = false)
    {
        var startNode = query.Kind is FilterQueryKind.Relative ? currentNode : root;
        var segments = query.Segments;
        if (segments.Length is 0)
        {
            var single = new NodeSink<TValue>((EvaluationNavigator<TValue>)navigator, retention: countOnly ? 0 : int.MaxValue);
            single.Add((startNode, null));
            return single;
        }

        // Only the last segment produces the query's nodes, so it alone may stop early; an intermediate segment
        // that stopped would drop nodes the next segments could still select from.
        var intermediate = new SegmentContext<TValue>(root, navigator, JsonPathEvaluationMode.Lax, trackPaths: false, limit: int.MaxValue);
        var last = new SegmentContext<TValue>(root, navigator, JsonPathEvaluationMode.Lax, trackPaths: false, limit);

        // The first segment starts from a single node, so apply it directly rather than through a one-item list.
        var evaluation = (EvaluationNavigator<TValue>)navigator;
        var nodes = new NodeSink<TValue>(evaluation, retention: segments.Length is 1 && countOnly ? 0 : int.MaxValue);
        if (segments.Length is 1)
        {
            ApplySegment(segments[0], startNode, path: null, in last, nodes);
            return nodes;
        }

        ApplySegment(segments[0], startNode, path: null, in intermediate, nodes);
        for (var i = 1; i < segments.Length - 1; i++)
        {
            nodes = ApplySegment(segments[i], nodes, in intermediate);
        }

        var result = new NodeSink<TValue>(evaluation, retention: countOnly ? 0 : int.MaxValue);
        ApplySegment(segments[^1], nodes, in last, result);
        nodes = result;

        return nodes;
    }

    private static bool TryEvaluateSingularQuery<TValue>(
        SingularQuery query,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator,
        out TValue? node)
    {
        node = query.IsRelative ? currentNode : root;

        foreach (var segment in query.Segments)
        {
            ((EvaluationNavigator<TValue>)navigator).Check();
            switch (segment.Kind)
            {
                case SingularQuerySegmentKind.Name:
                    if (navigator.GetKind(node) is not JsonPathNodeKind.Object
                        || !navigator.TryGetPropertyValue(node, segment.Name!, out node))
                    {
                        return false;
                    }

                    break;

                case SingularQuerySegmentKind.Index:
                    if (navigator.GetKind(node) is not JsonPathNodeKind.Array)
                    {
                        return false;
                    }

                    var length = navigator.GetArrayLength(node);
                    var index = NormalizeIndex(segment.Index, length);
                    if (index < 0 || index >= length || !navigator.TryGetElement(node, (int)index, out node))
                    {
                        return false;
                    }

                    break;
            }
        }

        return true;
    }

    private static bool EvaluateFunctionAsLogical<TValue>(
        FunctionCallExpression func,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
    {
        if (func.ResultType is FunctionExpressionType.LogicalType)
        {
            return func.Name switch
            {
                "match" => EvaluateMatchFunction(func, currentNode, root, navigator),
                "search" => EvaluateSearchFunction(func, currentNode, root, navigator),
                _ => false,
            };
        }

        if (func.ResultType is FunctionExpressionType.NodesType)
        {
            var nodes = EvaluateFunctionAsNodes(func, currentNode, root, navigator, limit: 1);
            return nodes.Count > 0;
        }

        return false;
    }

    private static ResolvedValue<TValue> EvaluateFunction<TValue>(
        FunctionCallExpression func,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
    {
        return func.Name switch
        {
            "length" => EvaluateLengthFunction(func, currentNode, root, navigator),
            "count" => EvaluateCountFunction(func, currentNode, root, navigator),
            "value" => EvaluateValueFunction(func, currentNode, root, navigator),
            "match" => ResolvedValue<TValue>.FromScalar(ScalarValue.FromBoolean(EvaluateMatchFunction(func, currentNode, root, navigator))),
            "search" => ResolvedValue<TValue>.FromScalar(ScalarValue.FromBoolean(EvaluateSearchFunction(func, currentNode, root, navigator))),
            _ => ResolvedValue<TValue>.FromNothing(),
        };
    }

    private static NodeSink<TValue> EvaluateFunctionAsNodes<TValue>(
        FunctionCallExpression func,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator,
        int limit)
    {
        _ = func;
        _ = currentNode;
        _ = root;
        _ = navigator;
        _ = limit;
        return [];
    }

    private static ResolvedValue<TValue> EvaluateLengthFunction<TValue>(
        FunctionCallExpression func,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
    {
        var argValue = ResolveFunctionArgumentAsValue(func.Arguments[0], currentNode, root, navigator);
        if (argValue.IsNothing)
        {
            return ResolvedValue<TValue>.FromNothing();
        }

        if (argValue.Kind is ResolvedValueKind.Scalar)
        {
            return argValue.Scalar.Kind is JsonPathNodeKind.String
                ? ResolvedValue<TValue>.FromScalar(ScalarValue.FromNumber(CountUnicodeScalarValues(argValue.Scalar.StringValue!, navigator)))
                : ResolvedValue<TValue>.FromNothing();
        }

        return navigator.GetKind(argValue.Node) switch
        {
            JsonPathNodeKind.Array => ResolvedValue<TValue>.FromScalar(ScalarValue.FromNumber(navigator.GetArrayLength(argValue.Node))),
            JsonPathNodeKind.Object => ResolvedValue<TValue>.FromScalar(ScalarValue.FromNumber(CountProperties(argValue.Node, navigator))),
            JsonPathNodeKind.String when navigator.TryGetString(argValue.Node, out var value) =>
                ResolvedValue<TValue>.FromScalar(ScalarValue.FromNumber(CountUnicodeScalarValues(value!, navigator))),
            _ => ResolvedValue<TValue>.FromNothing(),
        };
    }

    private static ResolvedValue<TValue> EvaluateCountFunction<TValue>(
        FunctionCallExpression func,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
    {
        var evaluation = (EvaluationNavigator<TValue>)navigator;
        evaluation.Check();
        var argument = func.Arguments[0];
        if (navigator.IsPure && evaluation.Counts.TryGetValue(func, out var cached))
            return ResolvedValue<TValue>.FromScalar(ScalarValue.FromNumber(cached));

        var invariant = navigator.IsPure && argument.Value is FilterQuery { Kind: FilterQueryKind.Absolute } absolute && ContainsOnlyBuiltInFunctions(absolute);
        if (argument.Value is FilterQuery query)
        {
            var count = EvaluateFilterQuery(query, currentNode, root, navigator, limit: int.MaxValue, countOnly: true).Count;
            if (invariant)
                evaluation.Counts.Add(func, count);

            return ResolvedValue<TValue>.FromScalar(ScalarValue.FromNumber(count));
        }

        var nodes = ResolveFunctionArgumentAsNodes(func.Arguments[0], currentNode, root, navigator, limit: int.MaxValue);
        return ResolvedValue<TValue>.FromScalar(ScalarValue.FromNumber(nodes.Count));
    }

    private static bool ContainsOnlyBuiltInFunctions(object? expression)
    {
        return expression switch
        {
            FilterQuery query => query.Segments.All(segment => segment.Selectors.All(selector =>
                selector is not FilterSelector filter || ContainsOnlyBuiltInFunctions(filter.Expression))),
            FunctionCallExpression function => function.Name is "count" or "length" or "value" or "match" or "search"
                && function.Arguments.All(argument => ContainsOnlyBuiltInFunctions(argument.Value)),
            OrExpression or => or.Operands.All(ContainsOnlyBuiltInFunctions),
            AndExpression and => and.Operands.All(ContainsOnlyBuiltInFunctions),
            NotExpression not => ContainsOnlyBuiltInFunctions(not.Operand),
            ComparisonExpression comparison => ContainsOnlyBuiltInFunctions(comparison.Left) && ContainsOnlyBuiltInFunctions(comparison.Right),
            ExistenceTestExpression existence => ContainsOnlyBuiltInFunctions(existence.Query),
            FunctionCallComparable comparable => ContainsOnlyBuiltInFunctions(comparable.FunctionCall),
            _ => true,
        };
    }

    private static ResolvedValue<TValue> EvaluateValueFunction<TValue>(
        FunctionCallExpression func,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
    {
        var nodes = ResolveFunctionArgumentAsNodes(func.Arguments[0], currentNode, root, navigator, limit: 2);
        if (nodes.Count is 1)
        {
            return ResolvedValue<TValue>.FromNode(nodes[0].Node);
        }

        return ResolvedValue<TValue>.FromNothing();
    }

    private static bool EvaluateMatchFunction<TValue>(
        FunctionCallExpression func,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
    {
        var strValue = ResolveFunctionArgumentAsValue(func.Arguments[0], currentNode, root, navigator);
        var patternValue = ResolveFunctionArgumentAsValue(func.Arguments[1], currentNode, root, navigator);

        if (strValue.IsNothing || patternValue.IsNothing)
        {
            return false;
        }

        var str = GetStringFromValue(strValue, navigator);
        var pattern = GetStringFromValue(patternValue, navigator);

        if (str is null || pattern is null)
        {
            return false;
        }

        return IsRegexMatch(func, str, pattern, anchored: true, (EvaluationNavigator<TValue>)navigator);
    }

    private static bool EvaluateSearchFunction<TValue>(
        FunctionCallExpression func,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
    {
        var strValue = ResolveFunctionArgumentAsValue(func.Arguments[0], currentNode, root, navigator);
        var patternValue = ResolveFunctionArgumentAsValue(func.Arguments[1], currentNode, root, navigator);

        if (strValue.IsNothing || patternValue.IsNothing)
        {
            return false;
        }

        var str = GetStringFromValue(strValue, navigator);
        var pattern = GetStringFromValue(patternValue, navigator);

        if (str is null || pattern is null)
        {
            return false;
        }

        return IsRegexMatch(func, str, pattern, anchored: false, (EvaluationNavigator<TValue>)navigator);
    }

    /// <summary>
    /// Runs an I-Regexp pattern (RFC 9485) against a string. Per RFC 9535 §2.4.6/§2.4.7, a pattern that
    /// cannot be evaluated yields LogicalFalse rather than an error, so every failure mode returns
    /// <see langword="false"/> instead of propagating out of <c>Evaluate</c>.
    /// </summary>
    /// <param name="func">The call being evaluated, used to cache the compiled pattern.</param>
    /// <param name="input">The string to test.</param>
    /// <param name="iRegexp">The I-Regexp pattern.</param>
    /// <param name="anchored">Whether the pattern must match the whole string (<c>match()</c>) or any substring (<c>search()</c>).</param>
    /// <returns><see langword="true"/> when the pattern matches; otherwise, <see langword="false"/>.</returns>
    private static bool IsRegexMatch<TValue>(FunctionCallExpression func, string input, string iRegexp, bool anchored, EvaluationNavigator<TValue> evaluation)
    {
        evaluation.Check((long)input.Length + iRegexp.Length + 1);
        // Translating and compiling a NonBacktracking regex costs far more than matching one, so reuse the
        // patterns this call compiled recently rather than rebuilding one for every node the filter visits.
        var regex = func.GetOrCreateRegex(iRegexp, anchored, CreateRegex).Regex;

        if (regex is null)
        {
            return false;
        }

        try
        {
            var result = regex.IsMatch(input);
            evaluation.Check();
            return result;
        }
        catch (RegexMatchTimeoutException)
        {
            evaluation.Check();
            return false;
        }
    }

    private static FunctionCallExpression.RegexCacheEntry CreateRegex(string iRegexp, bool anchored)
    {
        try
        {
            var pattern = IRegexpTranslator.Translate(iRegexp, anchored);
            return new FunctionCallExpression.RegexCacheEntry(
                new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, RegexTimeout));
        }
        catch (FormatException)
        {
            // Not a valid I-Regexp.
            return FunctionCallExpression.RegexCacheEntry.Unusable;
        }
        catch (ArgumentException)
        {
            // A backstop: the translation is meant to only ever emit patterns .NET accepts, and a bug in it must
            // still leave match()/search() with a Boolean result rather than throw out of Evaluate.
            return FunctionCallExpression.RegexCacheEntry.Unusable;
        }
        catch (NotSupportedException)
        {
            // NonBacktracking gave up. It rejects no construct the translation emits, but it does cap how large
            // an automaton it will build, and a quantifier such as 'a{1000000}' goes past that cap.
            return FunctionCallExpression.RegexCacheEntry.Unusable;
        }
    }

    private static ResolvedValue<TValue> ResolveFunctionArgumentAsValue<TValue>(
        FunctionArgument arg,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
    {
        switch (arg.Kind)
        {
            case FunctionArgumentKind.Literal:
                return arg.Value switch
                {
                    null => ResolvedValue<TValue>.FromScalar(ScalarValue.Null()),
                    true => ResolvedValue<TValue>.FromScalar(ScalarValue.FromBoolean(value: true)),
                    false => ResolvedValue<TValue>.FromScalar(ScalarValue.FromBoolean(value: false)),
                    string s => ResolvedValue<TValue>.FromScalar(ScalarValue.FromString(s)),
                    double d => ResolvedValue<TValue>.FromScalar(ScalarValue.FromNumber(d)),
                    _ => ResolvedValue<TValue>.FromNothing(),
                };

            case FunctionArgumentKind.FilterQuery:
            {
                var query = (FilterQuery)arg.Value!;
                var nodes = EvaluateFilterQuery(query, currentNode, root, navigator, limit: 2);
                if (nodes.Count is 1)
                {
                    return ResolvedValue<TValue>.FromNode(nodes[0].Node);
                }

                return ResolvedValue<TValue>.FromNothing();
            }

            case FunctionArgumentKind.FunctionCall:
            {
                var func = (FunctionCallExpression)arg.Value!;
                return EvaluateFunction(func, currentNode, root, navigator);
            }

            default:
                return ResolvedValue<TValue>.FromNothing();
        }
    }

    private static NodeSink<TValue> ResolveFunctionArgumentAsNodes<TValue>(
        FunctionArgument arg,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator,
        int limit)
    {
        if (arg.Kind is FunctionArgumentKind.FilterQuery)
        {
            var query = (FilterQuery)arg.Value!;
            return EvaluateFilterQuery(query, currentNode, root, navigator, limit);
        }

        if (arg.Kind is FunctionArgumentKind.FunctionCall)
        {
            var func = (FunctionCallExpression)arg.Value!;
            return EvaluateFunctionAsNodes(func, currentNode, root, navigator, limit);
        }

        return [];
    }

    private static string? GetStringFromValue<TValue>(ResolvedValue<TValue> value, JsonPathNavigator<TValue> navigator)
    {
        if (value.Kind is ResolvedValueKind.Scalar)
        {
            return value.Scalar.Kind is JsonPathNodeKind.String ? value.Scalar.StringValue : null;
        }

        return navigator.GetKind(value.Node) is JsonPathNodeKind.String && navigator.TryGetString(value.Node, out var result)
            ? result
            : null;
    }

    private static int CountProperties<TValue>(TValue? value, JsonPathNavigator<TValue> navigator)
    {
        var count = 0;
        foreach (var property in navigator.GetProperties(value))
        {
            _ = property;
            count++;
        }

        return count;
    }

    private static long NormalizeIndex(long index, int length)
    {
        if (index >= 0)
        {
            return index;
        }

        return length + index;
    }

    private static long Normalize(long value, long length)
    {
        if (value >= 0)
        {
            return value;
        }

        return length + value;
    }

    private static int CountUnicodeScalarValues<TValue>(string s, JsonPathNavigator<TValue> navigator)
    {
        var count = 0;
        for (var i = 0; i < s.Length; i++)
        {
            ((EvaluationNavigator<TValue>)navigator).Check();
            count++;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
            }
        }

        return count;
    }

    /// <summary>The parts of a segment evaluation that do not change from one node to the next.</summary>
    private readonly struct SegmentContext<TValue>
    {
        public SegmentContext(TValue? root, JsonPathNavigator<TValue> navigator, JsonPathEvaluationMode mode, bool trackPaths, int limit)
        {
            Root = root;
            Navigator = navigator;
            Mode = mode;
            TrackPaths = trackPaths;
            Limit = limit;
        }

        public TValue? Root { get; }

        public JsonPathNavigator<TValue> Navigator { get; }

        public JsonPathEvaluationMode Mode { get; }

        /// <summary>Gets whether the caller reads the paths of the resulting nodes.</summary>
        public bool TrackPaths { get; }

        /// <summary>Gets the number of result nodes after which the evaluation stops.</summary>
        public int Limit { get; }
    }

    /// <summary>
    /// Reads the elements of an array in constant time each. When the navigator's indexer is slower than that, the
    /// elements are first copied to a pooled buffer, which the caller must return by disposing this instance.
    /// </summary>
    private readonly struct ArrayElements<TValue> : IDisposable
    {
        private readonly JsonPathNavigator<TValue> _navigator;
        private readonly TValue? _array;
        private readonly TValue?[]? _buffer;

        public ArrayElements(JsonPathNavigator<TValue> navigator, TValue? array, bool copyElements = true)
        {
            _navigator = navigator;
            _array = array;
            Length = navigator.GetArrayLength(array);
            if (copyElements && !navigator.HasConstantTimeElementAccess && Length > 1)
            {
                ((EvaluationNavigator<TValue>)navigator).Check(Length);
                _buffer = ArrayPool<TValue?>.Shared.Rent(Length);
                try
                {
                    navigator.CopyElements(array, _buffer);
                }
                catch
                {
                    ArrayPool<TValue?>.Shared.Return(_buffer, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<TValue>());
                    throw;
                }
            }
        }

        public int Length { get; }

        public bool TryGet(int index, out TValue? value)
        {
            ((EvaluationNavigator<TValue>)_navigator).Check();
            if (_buffer is not null)
            {
                value = _buffer[index];
                return true;
            }

            return _navigator.TryGetElement(_array, index, out value);
        }

        public void Dispose()
        {
            if (_buffer is not null)
            {
                // Clear the buffer so the pool does not keep the values, and whatever they reference, alive.
                ArrayPool<TValue?>.Shared.Return(_buffer, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<TValue>());
            }
        }

    }

    private sealed class NodeSink<TValue> : IEnumerable<(TValue? Node, PathNode? Path)>
    {
        private readonly List<(TValue? Node, PathNode? Path)> _nodes = [];
        private readonly EvaluationNavigator<TValue>? _evaluation;
        private readonly int? _maximum;
        private readonly bool _final;
        private readonly int _retention;
        private readonly Action<(TValue? Node, PathNode? Path)>? _onNode;
        private readonly Func<bool>? _completed;

        public NodeSink()
        {
            _retention = int.MaxValue;
        }

        public NodeSink(EvaluationNavigator<TValue> evaluation, bool final = false, int retention = int.MaxValue,
            Action<(TValue? Node, PathNode? Path)>? onNode = null, Func<bool>? completed = null)
        {
            _evaluation = evaluation;
            _maximum = final ? evaluation.MaximumResults : evaluation.MaximumIntermediateResults;
            _final = final;
            _retention = retention;
            _onNode = onNode;
            _completed = completed;
        }

        public int Count { get; private set; }

        public (TValue? Node, PathNode? Path) this[int index] => _nodes[index];

        public bool HasDemand => _completed is not null;

        public bool IsComplete(int limit) => Count >= limit || (_completed?.Invoke() ?? false);

        public void Add((TValue? Node, PathNode? Path) node)
        {
            _evaluation?.Check();
            if (_maximum is { } maximum && Count >= maximum)
                throw new JsonPathEvaluationException(_final ? "The maximum result count was exceeded." : "The maximum intermediate result count was exceeded.");

            if (Count == int.MaxValue)
                throw new JsonPathEvaluationException("The result count exceeds the supported range.");

            Count++;
            if (_nodes.Count < _retention)
                _nodes.Add(node);

            _onNode?.Invoke(node);
        }

        public IEnumerator<(TValue? Node, PathNode? Path)> GetEnumerator() => _nodes.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
