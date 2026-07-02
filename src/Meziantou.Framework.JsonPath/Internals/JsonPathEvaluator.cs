using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Json.Internals;

/// <summary>Evaluates a parsed JSONPath AST against a JSON value tree.</summary>
internal static class JsonPathEvaluator
{
    public static JsonPathResult Evaluate(JsonPathExpression expression, JsonNode? root)
    {
        return Evaluate(expression, root, JsonPathEvaluationMode.Lax);
    }

    public static JsonPathResult Evaluate(JsonPathExpression expression, JsonNode? root, JsonPathEvaluationMode mode)
    {
        var result = Evaluate(expression, root, JsonNodeNavigator.Instance, mode);
        var matches = new List<JsonPathMatch>(result.Count);
        foreach (var match in result)
        {
            matches.Add(new JsonPathMatch(match.Value, match.Path));
        }

        return new JsonPathResult(matches);
    }

    public static JsonPathResult<TValue> Evaluate<TValue>(
        JsonPathExpression expression,
        TValue? root,
        JsonPathNavigator<TValue> navigator,
        JsonPathEvaluationMode mode)
        where TValue : class
    {
        var currentNodes = new List<(TValue? Node, List<PathComponent> Path)>
        {
            (root, []),
        };

        foreach (var segment in expression.Segments)
        {
            currentNodes = ApplySegment(segment, currentNodes, root, navigator, mode);
        }

        var matches = new List<JsonPathMatch<TValue>>(currentNodes.Count);
        foreach (var (node, path) in currentNodes)
        {
            matches.Add(new JsonPathMatch<TValue>(node, NormalizedPathBuilder.Build(path)));
        }

        return new JsonPathResult<TValue>(matches);
    }

    private static List<(TValue? Node, List<PathComponent> Path)> ApplySegment<TValue>(
        Segment segment,
        List<(TValue? Node, List<PathComponent> Path)> inputNodes,
        TValue? root,
        JsonPathNavigator<TValue> navigator,
        JsonPathEvaluationMode mode)
        where TValue : class
    {
        var result = new List<(TValue? Node, List<PathComponent> Path)>();

        foreach (var (node, path) in inputNodes)
        {
            if (segment.Kind is SegmentKind.Child)
            {
                ApplyChildSegment(segment.Selectors, node, path, root, navigator, mode, result, strictFailure: true);
            }
            else
            {
                ApplyDescendantSegment(segment.Selectors, node, path, root, navigator, mode, result);
            }
        }

        return result;
    }

    private static void ApplyChildSegment<TValue>(
        Selector[] selectors,
        TValue? node,
        List<PathComponent> path,
        TValue? root,
        JsonPathNavigator<TValue> navigator,
        JsonPathEvaluationMode mode,
        List<(TValue? Node, List<PathComponent> Path)> result,
        bool strictFailure)
        where TValue : class
    {
        foreach (var selector in selectors)
        {
            ApplySelector(selector, node, path, root, navigator, mode, result, strictFailure);
        }
    }

    private static void ApplyDescendantSegment<TValue>(
        Selector[] selectors,
        TValue? node,
        List<PathComponent> path,
        TValue? root,
        JsonPathNavigator<TValue> navigator,
        JsonPathEvaluationMode mode,
        List<(TValue? Node, List<PathComponent> Path)> result)
        where TValue : class
    {
        VisitDescendants(node, path, selectors, root, navigator, mode, result);
    }

    private static void VisitDescendants<TValue>(
        TValue? node,
        List<PathComponent> path,
        Selector[] selectors,
        TValue? root,
        JsonPathNavigator<TValue> navigator,
        JsonPathEvaluationMode mode,
        List<(TValue? Node, List<PathComponent> Path)> result)
        where TValue : class
    {
        ApplyChildSegment(selectors, node, path, root, navigator, mode, result, strictFailure: false);

        switch (navigator.GetKind(node))
        {
            case JsonPathNodeKind.Object:
                foreach (var property in navigator.GetProperties(node))
                {
                    var childPath = new List<PathComponent>(path)
                    {
                        PathComponent.FromName(property.Name),
                    };
                    VisitDescendants(property.Value, childPath, selectors, root, navigator, mode, result);
                }

                break;

            case JsonPathNodeKind.Array:
                var length = navigator.GetArrayLength(node);
                for (var i = 0; i < length; i++)
                {
                    if (!navigator.TryGetElement(node, i, out var value))
                    {
                        continue;
                    }

                    var childPath = new List<PathComponent>(path)
                    {
                        PathComponent.FromIndex(i),
                    };
                    VisitDescendants(value, childPath, selectors, root, navigator, mode, result);
                }

                break;
        }
    }

    private static void ApplySelector<TValue>(
        Selector selector,
        TValue? node,
        List<PathComponent> path,
        TValue? root,
        JsonPathNavigator<TValue> navigator,
        JsonPathEvaluationMode mode,
        List<(TValue? Node, List<PathComponent> Path)> result,
        bool strictFailure)
        where TValue : class
    {
        switch (selector)
        {
            case NameSelector nameSelector:
                ApplyNameSelector(nameSelector, node, path, navigator, mode, result, strictFailure);
                break;
            case WildcardSelector:
                ApplyWildcardSelector(node, path, navigator, mode, result, strictFailure);
                break;
            case IndexSelector indexSelector:
                ApplyIndexSelector(indexSelector, node, path, navigator, mode, result, strictFailure);
                break;
            case SliceSelector sliceSelector:
                ApplySliceSelector(sliceSelector, node, path, navigator, mode, result, strictFailure);
                break;
            case FilterSelector filterSelector:
                ApplyFilterSelector(filterSelector, node, path, root, navigator, mode, result, strictFailure);
                break;
        }
    }

    private static void ApplyNameSelector<TValue>(
        NameSelector selector,
        TValue? node,
        List<PathComponent> path,
        JsonPathNavigator<TValue> navigator,
        JsonPathEvaluationMode mode,
        List<(TValue? Node, List<PathComponent> Path)> result,
        bool strictFailure)
        where TValue : class
    {
        if (navigator.GetKind(node) is JsonPathNodeKind.Object)
        {
            if (!navigator.TryGetPropertyValue(node, selector.Name, out var value))
            {
                ThrowPathEvaluationErrorIfStrict(mode, strictFailure, path, $"Object member '{selector.Name}' does not exist");
                return;
            }

            var newPath = new List<PathComponent>(path)
            {
                PathComponent.FromName(selector.Name),
            };
            result.Add((value, newPath));
            return;
        }

        ThrowPathEvaluationErrorIfStrict(mode, strictFailure, path, $"Name selector '{selector.Name}' requires an object");
    }

    private static void ApplyWildcardSelector<TValue>(
        TValue? node,
        List<PathComponent> path,
        JsonPathNavigator<TValue> navigator,
        JsonPathEvaluationMode mode,
        List<(TValue? Node, List<PathComponent> Path)> result,
        bool strictFailure)
        where TValue : class
    {
        switch (navigator.GetKind(node))
        {
            case JsonPathNodeKind.Object:
                foreach (var property in navigator.GetProperties(node))
                {
                    var newPath = new List<PathComponent>(path)
                    {
                        PathComponent.FromName(property.Name),
                    };
                    result.Add((property.Value, newPath));
                }

                break;

            case JsonPathNodeKind.Array:
                var length = navigator.GetArrayLength(node);
                for (var i = 0; i < length; i++)
                {
                    if (!navigator.TryGetElement(node, i, out var value))
                    {
                        continue;
                    }

                    var newPath = new List<PathComponent>(path)
                    {
                        PathComponent.FromIndex(i),
                    };
                    result.Add((value, newPath));
                }

                break;

            default:
                ThrowPathEvaluationErrorIfStrict(mode, strictFailure, path, "Wildcard selector requires an object or an array");
                break;
        }
    }

    private static void ApplyIndexSelector<TValue>(
        IndexSelector selector,
        TValue? node,
        List<PathComponent> path,
        JsonPathNavigator<TValue> navigator,
        JsonPathEvaluationMode mode,
        List<(TValue? Node, List<PathComponent> Path)> result,
        bool strictFailure)
        where TValue : class
    {
        if (navigator.GetKind(node) is not JsonPathNodeKind.Array)
        {
            ThrowPathEvaluationErrorIfStrict(mode, strictFailure, path, $"Index selector [{selector.Index}] requires an array");
            return;
        }

        var length = navigator.GetArrayLength(node);
        var index = NormalizeIndex(selector.Index, length);
        if (index >= 0 && index < length && navigator.TryGetElement(node, (int)index, out var value))
        {
            var newPath = new List<PathComponent>(path)
            {
                PathComponent.FromIndex(index),
            };
            result.Add((value, newPath));
            return;
        }

        ThrowPathEvaluationErrorIfStrict(mode, strictFailure, path, $"Array index [{selector.Index}] is out of range");
    }

    private static void ApplySliceSelector<TValue>(
        SliceSelector selector,
        TValue? node,
        List<PathComponent> path,
        JsonPathNavigator<TValue> navigator,
        JsonPathEvaluationMode mode,
        List<(TValue? Node, List<PathComponent> Path)> result,
        bool strictFailure)
        where TValue : class
    {
        if (navigator.GetKind(node) is not JsonPathNodeKind.Array)
        {
            ThrowPathEvaluationErrorIfStrict(mode, strictFailure, path, "Slice selector requires an array");
            return;
        }

        var len = navigator.GetArrayLength(node);
        var step = selector.Step ?? 1;

        if (step == 0)
        {
            return;
        }

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

        if (step > 0)
        {
            for (var i = lower; i < upper; i += step)
            {
                if (!navigator.TryGetElement(node, (int)i, out var value))
                {
                    continue;
                }

                var newPath = new List<PathComponent>(path)
                {
                    PathComponent.FromIndex(i),
                };
                result.Add((value, newPath));
            }
        }
        else
        {
            for (var i = upper; lower < i; i += step)
            {
                if (!navigator.TryGetElement(node, (int)i, out var value))
                {
                    continue;
                }

                var newPath = new List<PathComponent>(path)
                {
                    PathComponent.FromIndex(i),
                };
                result.Add((value, newPath));
            }
        }
    }

    private static void ApplyFilterSelector<TValue>(
        FilterSelector selector,
        TValue? node,
        List<PathComponent> path,
        TValue? root,
        JsonPathNavigator<TValue> navigator,
        JsonPathEvaluationMode mode,
        List<(TValue? Node, List<PathComponent> Path)> result,
        bool strictFailure)
        where TValue : class
    {
        switch (navigator.GetKind(node))
        {
            case JsonPathNodeKind.Object:
                foreach (var property in navigator.GetProperties(node))
                {
                    if (EvaluateLogicalExpression(selector.Expression, property.Value, root, navigator))
                    {
                        var newPath = new List<PathComponent>(path)
                        {
                            PathComponent.FromName(property.Name),
                        };
                        result.Add((property.Value, newPath));
                    }
                }

                break;

            case JsonPathNodeKind.Array:
                var length = navigator.GetArrayLength(node);
                for (var i = 0; i < length; i++)
                {
                    if (!navigator.TryGetElement(node, i, out var value))
                    {
                        continue;
                    }

                    if (EvaluateLogicalExpression(selector.Expression, value, root, navigator))
                    {
                        var newPath = new List<PathComponent>(path)
                        {
                            PathComponent.FromIndex(i),
                        };
                        result.Add((value, newPath));
                    }
                }

                break;

            default:
                ThrowPathEvaluationErrorIfStrict(mode, strictFailure, path, "Filter selector requires an object or an array");
                break;
        }
    }

    private static void ThrowPathEvaluationErrorIfStrict(JsonPathEvaluationMode mode, bool strictFailure, List<PathComponent> path, string error)
    {
        if (mode is not JsonPathEvaluationMode.Strict || !strictFailure)
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
        where TValue : class
    {
        return expr switch
        {
            OrExpression or => EvaluateLogicalExpression(or.Left, currentNode, root, navigator)
                               || EvaluateLogicalExpression(or.Right, currentNode, root, navigator),
            AndExpression and => EvaluateLogicalExpression(and.Left, currentNode, root, navigator)
                                && EvaluateLogicalExpression(and.Right, currentNode, root, navigator),
            NotExpression not => !EvaluateLogicalExpression(not.Operand, currentNode, root, navigator),
            ComparisonExpression comp => EvaluateComparison(comp, currentNode, root, navigator),
            ExistenceTestExpression existence => EvaluateExistenceTest(existence, currentNode, root, navigator),
            FunctionCallExpression func => EvaluateFunctionAsLogical(func, currentNode, root, navigator),
            _ => false,
        };
    }

    private static bool EvaluateExistenceTest<TValue>(
        ExistenceTestExpression test,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
        where TValue : class
    {
        var nodes = EvaluateFilterQuery(test.Query, currentNode, root, navigator);
        return nodes.Count > 0;
    }

    private static bool EvaluateComparison<TValue>(
        ComparisonExpression comp,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
        where TValue : class
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
        where TValue : class
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
            Node = null;
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
        where TValue : class
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
                    var nodes = EvaluateSingularQuery(sq.Query, currentNode, root, navigator);
                    if (nodes.Count is 1)
                    {
                        return ResolvedValue<TValue>.FromNode(nodes[0]);
                    }

                    return ResolvedValue<TValue>.FromNothing();
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
        where TValue : class
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
            return ScalarValuesEqual(left.Scalar, right.Scalar);
        }

        if (left.Kind is ResolvedValueKind.Scalar)
        {
            return ScalarAndNodeEqual(left.Scalar, right.Node, navigator);
        }

        if (right.Kind is ResolvedValueKind.Scalar)
        {
            return ScalarAndNodeEqual(right.Scalar, left.Node, navigator);
        }

        return NodesEqual(left.Node, right.Node, navigator);
    }

    private static bool CompareLessThan<TValue>(
        ResolvedValue<TValue> left,
        ResolvedValue<TValue> right,
        JsonPathNavigator<TValue> navigator)
        where TValue : class
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
            return string.Compare(leftString, rightString, StringComparison.Ordinal) < 0;
        }

        return false;
    }

    private static bool ScalarValuesEqual(ScalarValue left, ScalarValue right)
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
            JsonPathNodeKind.String => left.StringValue == right.StringValue,
            _ => false,
        };
    }

    private static bool ScalarAndNodeEqual<TValue>(ScalarValue scalar, TValue? node, JsonPathNavigator<TValue> navigator)
        where TValue : class
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
            JsonPathNodeKind.String => navigator.TryGetString(node, out var value) && scalar.StringValue == value,
            _ => false,
        };
    }

    private static bool NodesEqual<TValue>(TValue? left, TValue? right, JsonPathNavigator<TValue> navigator)
        where TValue : class
    {
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
                       && leftString == rightString;

            case JsonPathNodeKind.Array:
                return ArraysEqual(left, right, navigator);

            case JsonPathNodeKind.Object:
                return ObjectsEqual(left, right, navigator);

            default:
                return false;
        }
    }

    private static bool ArraysEqual<TValue>(TValue? left, TValue? right, JsonPathNavigator<TValue> navigator)
        where TValue : class
    {
        var length = navigator.GetArrayLength(left);
        if (navigator.GetArrayLength(right) != length)
        {
            return false;
        }

        for (var i = 0; i < length; i++)
        {
            if (!navigator.TryGetElement(left, i, out var leftValue) || !navigator.TryGetElement(right, i, out var rightValue))
            {
                return false;
            }

            if (!NodesEqual(leftValue, rightValue, navigator))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ObjectsEqual<TValue>(TValue? left, TValue? right, JsonPathNavigator<TValue> navigator)
        where TValue : class
    {
        var leftCount = 0;
        foreach (var property in navigator.GetProperties(left))
        {
            leftCount++;
            if (!navigator.TryGetPropertyValue(right, property.Name, out var rightValue))
            {
                return false;
            }

            if (!NodesEqual(property.Value, rightValue, navigator))
            {
                return false;
            }
        }

        return leftCount == CountProperties(right, navigator);
    }

    private static bool TryGetNumber<TValue>(ResolvedValue<TValue> value, JsonPathNavigator<TValue> navigator, out double result)
        where TValue : class
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
        where TValue : class
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

    private static List<TValue?> EvaluateFilterQuery<TValue>(
        FilterQuery query,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
        where TValue : class
    {
        var startNode = query.Kind is FilterQueryKind.Relative ? currentNode : root;
        var nodes = new List<(TValue? Node, List<PathComponent> Path)>
        {
            (startNode, []),
        };

        foreach (var segment in query.Segments)
        {
            nodes = ApplySegment(segment, nodes, root, navigator, JsonPathEvaluationMode.Lax);
        }

        var result = new List<TValue?>(nodes.Count);
        foreach (var (node, _) in nodes)
        {
            result.Add(node);
        }

        return result;
    }

    private static List<TValue?> EvaluateSingularQuery<TValue>(
        SingularQuery query,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
        where TValue : class
    {
        var node = query.IsRelative ? currentNode : root;

        foreach (var segment in query.Segments)
        {
            switch (segment.Kind)
            {
                case SingularQuerySegmentKind.Name:
                    if (navigator.GetKind(node) is JsonPathNodeKind.Object
                        && navigator.TryGetPropertyValue(node, segment.Name!, out var propertyValue))
                    {
                        node = propertyValue;
                    }
                    else
                    {
                        return [];
                    }

                    break;

                case SingularQuerySegmentKind.Index:
                    if (navigator.GetKind(node) is JsonPathNodeKind.Array)
                    {
                        var length = navigator.GetArrayLength(node);
                        var index = NormalizeIndex(segment.Index, length);
                        if (index >= 0 && index < length && navigator.TryGetElement(node, (int)index, out var elementValue))
                        {
                            node = elementValue;
                        }
                        else
                        {
                            return [];
                        }
                    }
                    else
                    {
                        return [];
                    }

                    break;
            }
        }

        return [node];
    }

    private static bool EvaluateFunctionAsLogical<TValue>(
        FunctionCallExpression func,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
        where TValue : class
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
            var nodes = EvaluateFunctionAsNodes(func, currentNode, root, navigator);
            return nodes.Count > 0;
        }

        return false;
    }

    private static ResolvedValue<TValue> EvaluateFunction<TValue>(
        FunctionCallExpression func,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
        where TValue : class
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

    private static List<TValue?> EvaluateFunctionAsNodes<TValue>(
        FunctionCallExpression func,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
        where TValue : class
    {
        _ = func;
        _ = currentNode;
        _ = root;
        _ = navigator;
        return [];
    }

    private static ResolvedValue<TValue> EvaluateLengthFunction<TValue>(
        FunctionCallExpression func,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
        where TValue : class
    {
        var argValue = ResolveFunctionArgumentAsValue(func.Arguments[0], currentNode, root, navigator);
        if (argValue.IsNothing)
        {
            return ResolvedValue<TValue>.FromNothing();
        }

        if (argValue.Kind is ResolvedValueKind.Scalar)
        {
            return argValue.Scalar.Kind is JsonPathNodeKind.String
                ? ResolvedValue<TValue>.FromScalar(ScalarValue.FromNumber(CountUnicodeScalarValues(argValue.Scalar.StringValue!)))
                : ResolvedValue<TValue>.FromNothing();
        }

        return navigator.GetKind(argValue.Node) switch
        {
            JsonPathNodeKind.Array => ResolvedValue<TValue>.FromScalar(ScalarValue.FromNumber(navigator.GetArrayLength(argValue.Node))),
            JsonPathNodeKind.Object => ResolvedValue<TValue>.FromScalar(ScalarValue.FromNumber(CountProperties(argValue.Node, navigator))),
            JsonPathNodeKind.String when navigator.TryGetString(argValue.Node, out var value) =>
                ResolvedValue<TValue>.FromScalar(ScalarValue.FromNumber(CountUnicodeScalarValues(value!))),
            _ => ResolvedValue<TValue>.FromNothing(),
        };
    }

    private static ResolvedValue<TValue> EvaluateCountFunction<TValue>(
        FunctionCallExpression func,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
        where TValue : class
    {
        var nodes = ResolveFunctionArgumentAsNodes(func.Arguments[0], currentNode, root, navigator);
        return ResolvedValue<TValue>.FromScalar(ScalarValue.FromNumber(nodes.Count));
    }

    private static ResolvedValue<TValue> EvaluateValueFunction<TValue>(
        FunctionCallExpression func,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
        where TValue : class
    {
        var nodes = ResolveFunctionArgumentAsNodes(func.Arguments[0], currentNode, root, navigator);
        if (nodes.Count is 1)
        {
            return ResolvedValue<TValue>.FromNode(nodes[0]);
        }

        return ResolvedValue<TValue>.FromNothing();
    }

    private static bool EvaluateMatchFunction<TValue>(
        FunctionCallExpression func,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
        where TValue : class
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

        try
        {
            var regex = ConvertIRegexpToRegex(pattern);
            var fullPattern = $"^(?:{regex})$";
            return Regex.IsMatch(str, fullPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool EvaluateSearchFunction<TValue>(
        FunctionCallExpression func,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
        where TValue : class
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

        try
        {
            var regex = ConvertIRegexpToRegex(pattern);
            return Regex.IsMatch(str, regex, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ResolvedValue<TValue> ResolveFunctionArgumentAsValue<TValue>(
        FunctionArgument arg,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
        where TValue : class
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
                    var nodes = EvaluateFilterQuery(query, currentNode, root, navigator);
                    if (nodes.Count is 1)
                    {
                        return ResolvedValue<TValue>.FromNode(nodes[0]);
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

    private static List<TValue?> ResolveFunctionArgumentAsNodes<TValue>(
        FunctionArgument arg,
        TValue? currentNode,
        TValue? root,
        JsonPathNavigator<TValue> navigator)
        where TValue : class
    {
        if (arg.Kind is FunctionArgumentKind.FilterQuery)
        {
            var query = (FilterQuery)arg.Value!;
            return EvaluateFilterQuery(query, currentNode, root, navigator);
        }

        if (arg.Kind is FunctionArgumentKind.FunctionCall)
        {
            var func = (FunctionCallExpression)arg.Value!;
            return EvaluateFunctionAsNodes(func, currentNode, root, navigator);
        }

        return [];
    }

    private static string? GetStringFromValue<TValue>(ResolvedValue<TValue> value, JsonPathNavigator<TValue> navigator)
        where TValue : class
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
        where TValue : class
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

    private static int CountUnicodeScalarValues(string s)
    {
        var count = 0;
        for (var i = 0; i < s.Length; i++)
        {
            count++;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
            }
        }

        return count;
    }

    private static string ConvertIRegexpToRegex(string iregexp)
    {
        var sb = new StringBuilder(iregexp.Length * 2);
        var inCharClass = false;

        for (var i = 0; i < iregexp.Length; i++)
        {
            var ch = iregexp[i];

            if (ch == '\\' && i + 1 < iregexp.Length)
            {
                sb.Append(ch);
                sb.Append(iregexp[i + 1]);
                i++;
                continue;
            }

            if (ch == '[' && !inCharClass)
            {
                inCharClass = true;
                sb.Append(ch);
                continue;
            }

            if (ch == ']' && inCharClass)
            {
                inCharClass = false;
                sb.Append(ch);
                continue;
            }

            if (ch == '.' && !inCharClass)
            {
                sb.Append(@"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\n\r])");
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }
}
