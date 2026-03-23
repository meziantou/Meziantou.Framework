using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Json.Internals;

/// <summary>Evaluates a parsed JSONPath AST against a JsonNode tree.</summary>
internal static class JsonPathEvaluator
{
    public static JsonPathResult Evaluate(JsonPathExpression expression, JsonNode? root)
    {
        // Start with the root node
        var currentNodes = new List<(JsonNode? Node, List<PathComponent> Path)>
        {
            (root, []),
        };

        // Apply each segment in sequence
        foreach (var segment in expression.Segments)
        {
            currentNodes = ApplySegment(segment, currentNodes, root);
        }

        // Build results
        var matches = new List<JsonPathMatch>(currentNodes.Count);
        foreach (var (node, path) in currentNodes)
        {
            matches.Add(new JsonPathMatch(node, NormalizedPathBuilder.Build(path)));
        }

        return new JsonPathResult(matches);
    }

    private static List<(JsonNode? Node, List<PathComponent> Path)> ApplySegment(
        Segment segment,
        List<(JsonNode? Node, List<PathComponent> Path)> inputNodes,
        JsonNode? root)
    {
        var result = new List<(JsonNode? Node, List<PathComponent> Path)>();

        foreach (var (node, path) in inputNodes)
        {
            if (segment.Kind is SegmentKind.Child)
            {
                ApplyChildSegment(segment.Selectors, node, path, root, result);
            }
            else
            {
                ApplyDescendantSegment(segment.Selectors, node, path, root, result);
            }
        }

        return result;
    }

    private static void ApplyChildSegment(
        Selector[] selectors,
        JsonNode? node,
        List<PathComponent> path,
        JsonNode? root,
        List<(JsonNode? Node, List<PathComponent> Path)> result)
    {
        foreach (var selector in selectors)
        {
            ApplySelector(selector, node, path, root, result);
        }
    }

    private static void ApplyDescendantSegment(
        Selector[] selectors,
        JsonNode? node,
        List<PathComponent> path,
        JsonNode? root,
        List<(JsonNode? Node, List<PathComponent> Path)> result)
    {
        // Visit nodes in pre-order: node first, then descendants
        // For each visited node, apply the selectors as a child segment
        VisitDescendants(node, path, selectors, root, result);
    }

    private static void VisitDescendants(
        JsonNode? node,
        List<PathComponent> path,
        Selector[] selectors,
        JsonNode? root,
        List<(JsonNode? Node, List<PathComponent> Path)> result)
    {
        // Apply selectors to current node
        ApplyChildSegment(selectors, node, path, root, result);

        // Recurse into children
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj)
                {
                    var childPath = new List<PathComponent>(path)
                    {
                        PathComponent.FromName(property.Key),
                    };
                    VisitDescendants(property.Value, childPath, selectors, root, result);
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var childPath = new List<PathComponent>(path)
                    {
                        PathComponent.FromIndex(i),
                    };
                    VisitDescendants(array[i], childPath, selectors, root, result);
                }

                break;
        }
    }

    private static void ApplySelector(
        Selector selector,
        JsonNode? node,
        List<PathComponent> path,
        JsonNode? root,
        List<(JsonNode? Node, List<PathComponent> Path)> result)
    {
        switch (selector)
        {
            case NameSelector nameSelector:
                ApplyNameSelector(nameSelector, node, path, result);
                break;
            case WildcardSelector:
                ApplyWildcardSelector(node, path, result);
                break;
            case IndexSelector indexSelector:
                ApplyIndexSelector(indexSelector, node, path, result);
                break;
            case SliceSelector sliceSelector:
                ApplySliceSelector(sliceSelector, node, path, result);
                break;
            case FilterSelector filterSelector:
                ApplyFilterSelector(filterSelector, node, path, root, result);
                break;
        }
    }

    private static void ApplyNameSelector(
        NameSelector selector,
        JsonNode? node,
        List<PathComponent> path,
        List<(JsonNode? Node, List<PathComponent> Path)> result)
    {
        if (node is JsonObject obj && obj.TryGetPropertyValue(selector.Name, out var value))
        {
            var newPath = new List<PathComponent>(path)
            {
                PathComponent.FromName(selector.Name),
            };
            result.Add((value, newPath));
        }
    }

    private static void ApplyWildcardSelector(
        JsonNode? node,
        List<PathComponent> path,
        List<(JsonNode? Node, List<PathComponent> Path)> result)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj)
                {
                    var newPath = new List<PathComponent>(path)
                    {
                        PathComponent.FromName(property.Key),
                    };
                    result.Add((property.Value, newPath));
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var newPath = new List<PathComponent>(path)
                    {
                        PathComponent.FromIndex(i),
                    };
                    result.Add((array[i], newPath));
                }

                break;
        }
    }

    private static void ApplyIndexSelector(
        IndexSelector selector,
        JsonNode? node,
        List<PathComponent> path,
        List<(JsonNode? Node, List<PathComponent> Path)> result)
    {
        if (node is not JsonArray array)
        {
            return;
        }

        var index = NormalizeIndex(selector.Index, array.Count);
        if (index >= 0 && index < array.Count)
        {
            var newPath = new List<PathComponent>(path)
            {
                PathComponent.FromIndex(index),
            };
            result.Add((array[(int)index], newPath));
        }
    }

    private static void ApplySliceSelector(
        SliceSelector selector,
        JsonNode? node,
        List<PathComponent> path,
        List<(JsonNode? Node, List<PathComponent> Path)> result)
    {
        if (node is not JsonArray array)
        {
            return;
        }

        var len = array.Count;
        var step = selector.Step ?? 1;

        if (step == 0)
        {
            return; // step=0 selects nothing
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
                var newPath = new List<PathComponent>(path)
                {
                    PathComponent.FromIndex(i),
                };
                result.Add((array[(int)i], newPath));
            }
        }
        else
        {
            for (var i = upper; lower < i; i += step)
            {
                var newPath = new List<PathComponent>(path)
                {
                    PathComponent.FromIndex(i),
                };
                result.Add((array[(int)i], newPath));
            }
        }
    }

    private static void ApplyFilterSelector(
        FilterSelector selector,
        JsonNode? node,
        List<PathComponent> path,
        JsonNode? root,
        List<(JsonNode? Node, List<PathComponent> Path)> result)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj)
                {
                    if (EvaluateLogicalExpression(selector.Expression, property.Value, root))
                    {
                        var newPath = new List<PathComponent>(path)
                        {
                            PathComponent.FromName(property.Key),
                        };
                        result.Add((property.Value, newPath));
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (EvaluateLogicalExpression(selector.Expression, array[i], root))
                    {
                        var newPath = new List<PathComponent>(path)
                        {
                            PathComponent.FromIndex(i),
                        };
                        result.Add((array[i], newPath));
                    }
                }

                break;
        }
    }

    // ---- Filter Expression Evaluation ----

    private static bool EvaluateLogicalExpression(LogicalExpression expr, JsonNode? currentNode, JsonNode? root)
    {
        return expr switch
        {
            OrExpression or => EvaluateLogicalExpression(or.Left, currentNode, root)
                               || EvaluateLogicalExpression(or.Right, currentNode, root),
            AndExpression and => EvaluateLogicalExpression(and.Left, currentNode, root)
                                && EvaluateLogicalExpression(and.Right, currentNode, root),
            NotExpression not => !EvaluateLogicalExpression(not.Operand, currentNode, root),
            ComparisonExpression comp => EvaluateComparison(comp, currentNode, root),
            ExistenceTestExpression existence => EvaluateExistenceTest(existence, currentNode, root),
            FunctionCallExpression func => EvaluateFunctionAsLogical(func, currentNode, root),
            _ => false,
        };
    }

    private static bool EvaluateExistenceTest(ExistenceTestExpression test, JsonNode? currentNode, JsonNode? root)
    {
        var nodes = EvaluateFilterQuery(test.Query, currentNode, root);
        return nodes.Count > 0;
    }

    private static bool EvaluateComparison(ComparisonExpression comp, JsonNode? currentNode, JsonNode? root)
    {
        var left = ResolveComparable(comp.Left, currentNode, root);
        var right = ResolveComparable(comp.Right, currentNode, root);

        return comp.Operator switch
        {
            ComparisonOperator.Equal => CompareEqual(left, right),
            ComparisonOperator.NotEqual => !CompareEqual(left, right),
            ComparisonOperator.LessThan => CompareLessThan(left, right),
            ComparisonOperator.LessThanOrEqual => CompareLessThan(left, right) || CompareEqual(left, right),
            ComparisonOperator.GreaterThan => CompareLessThan(right, left),
            ComparisonOperator.GreaterThanOrEqual => CompareLessThan(right, left) || CompareEqual(left, right),
            _ => false,
        };
    }

    /// <summary>
    /// Represents a resolved comparable value for filter expression evaluation.
    /// Nothing represents the absence of a value (empty nodelist or Nothing from functions).
    /// </summary>
    private sealed class ResolvedValue
    {
        public static readonly ResolvedValue Nothing = new() { IsNothing = true };

        public JsonNode? Node { get; private init; }

        public bool IsNothing { get; private init; }

        public static ResolvedValue FromNode(JsonNode? node) => new() { Node = node, IsNothing = false };

        public static ResolvedValue FromNothing() => Nothing;
    }

    private static ResolvedValue ResolveComparable(Comparable comparable, JsonNode? currentNode, JsonNode? root)
    {
        switch (comparable)
        {
            case LiteralComparable literal:
                return literal.Value switch
                {
                    null => ResolvedValue.FromNode(node: null),
                    true => ResolvedValue.FromNode(JsonValue.Create(value: true)),
                    false => ResolvedValue.FromNode(JsonValue.Create(value: false)),
                    string s => ResolvedValue.FromNode(JsonValue.Create(s)),
                    double d => ResolvedValue.FromNode(JsonValue.Create(d)),
                    _ => ResolvedValue.FromNothing(),
                };

            case SingularQueryComparable sq:
                {
                    var nodes = EvaluateSingularQuery(sq.Query, currentNode, root);
                    if (nodes.Count is 1)
                    {
                        return ResolvedValue.FromNode(nodes[0]);
                    }

                    return ResolvedValue.FromNothing();
                }

            case FunctionCallComparable fc:
                {
                    var result = EvaluateFunction(fc.FunctionCall, currentNode, root);
                    return result;
                }

            default:
                return ResolvedValue.FromNothing();
        }
    }

    // RFC 9535 §2.3.5.2.2: Comparison semantics
    private static bool CompareEqual(ResolvedValue left, ResolvedValue right)
    {
        // Both Nothing/empty → true
        if (left.IsNothing && right.IsNothing)
        {
            return true;
        }

        // One Nothing/empty → false
        if (left.IsNothing || right.IsNothing)
        {
            return false;
        }

        return JsonNodesEqual(left.Node, right.Node);
    }

    private static bool CompareLessThan(ResolvedValue left, ResolvedValue right)
    {
        // Any Nothing → false
        if (left.IsNothing || right.IsNothing)
        {
            return false;
        }

        return JsonNodeLessThan(left.Node, right.Node);
    }

    private static bool JsonNodesEqual(JsonNode? left, JsonNode? right)
    {
        // Both null → equal
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        // Compare by kind
        if (left is JsonValue leftVal && right is JsonValue rightVal)
        {
            return JsonValuesEqual(leftVal, rightVal);
        }

        if (left is JsonArray leftArr && right is JsonArray rightArr)
        {
            return JsonArraysEqual(leftArr, rightArr);
        }

        if (left is JsonObject leftObj && right is JsonObject rightObj)
        {
            return JsonObjectsEqual(leftObj, rightObj);
        }

        return false;
    }

    private static bool JsonValuesEqual(JsonValue left, JsonValue right)
    {
        var leftKind = left.GetValueKind();
        var rightKind = right.GetValueKind();

        if (leftKind != rightKind)
        {
            return false;
        }

        return leftKind switch
        {
            JsonValueKind.True or JsonValueKind.False => true,
            JsonValueKind.Null => true,
            JsonValueKind.String => GetStringValue(left) == GetStringValue(right),
            JsonValueKind.Number => CompareNumberValues(left, right) is 0,
            _ => false,
        };
    }

    private static bool JsonArraysEqual(JsonArray left, JsonArray right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!JsonNodesEqual(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool JsonObjectsEqual(JsonObject left, JsonObject right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var property in left)
        {
            if (!right.TryGetPropertyValue(property.Key, out var rightValue))
            {
                return false;
            }

            if (!JsonNodesEqual(property.Value, rightValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool JsonNodeLessThan(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        if (left is not JsonValue leftVal || right is not JsonValue rightVal)
        {
            return false;
        }

        var leftKind = leftVal.GetValueKind();
        var rightKind = rightVal.GetValueKind();

        if (leftKind is JsonValueKind.Number && rightKind is JsonValueKind.Number)
        {
            return CompareNumberValues(leftVal, rightVal) < 0;
        }

        if (leftKind is JsonValueKind.String && rightKind is JsonValueKind.String)
        {
            return string.Compare(GetStringValue(leftVal), GetStringValue(rightVal), StringComparison.Ordinal) < 0;
        }

        return false;
    }

    private static int CompareNumberValues(JsonValue left, JsonValue right)
    {
        var leftDouble = GetDoubleValue(left);
        var rightDouble = GetDoubleValue(right);
        return leftDouble.CompareTo(rightDouble);
    }

    private static double GetDoubleValue(JsonValue value)
    {
        if (value.TryGetValue<JsonElement>(out var element))
        {
            return element.GetDouble();
        }

        if (value.TryGetValue<double>(out var d))
        {
            return d;
        }

        if (value.TryGetValue<float>(out var f))
        {
            return f;
        }

        if (value.TryGetValue<decimal>(out var dec))
        {
            return (double)dec;
        }

        if (value.TryGetValue<long>(out var l))
        {
            return l;
        }

        if (value.TryGetValue<ulong>(out var ul))
        {
            return ul;
        }

        if (value.TryGetValue<int>(out var i))
        {
            return i;
        }

        if (value.TryGetValue<uint>(out var ui))
        {
            return ui;
        }

        if (value.TryGetValue<short>(out var s))
        {
            return s;
        }

        if (value.TryGetValue<ushort>(out var us))
        {
            return us;
        }

        if (value.TryGetValue<byte>(out var b))
        {
            return b;
        }

        if (value.TryGetValue<sbyte>(out var sb))
        {
            return sb;
        }

        return 0;
    }

    private static string? GetStringValue(JsonValue value)
    {
        if (value.TryGetValue<JsonElement>(out var element))
        {
            return element.GetString();
        }

        if (value.TryGetValue<string>(out var s))
        {
            return s;
        }

        return value.ToString();
    }

    // ---- Query Evaluation ----

    private static List<JsonNode?> EvaluateFilterQuery(FilterQuery query, JsonNode? currentNode, JsonNode? root)
    {
        var startNode = query.Kind is FilterQueryKind.Relative ? currentNode : root;
        var nodes = new List<(JsonNode? Node, List<PathComponent> Path)>
        {
            (startNode, []),
        };

        foreach (var segment in query.Segments)
        {
            nodes = ApplySegment(segment, nodes, root);
        }

        var result = new List<JsonNode?>(nodes.Count);
        foreach (var (node, _) in nodes)
        {
            result.Add(node);
        }

        return result;
    }

    private static List<JsonNode?> EvaluateSingularQuery(SingularQuery query, JsonNode? currentNode, JsonNode? root)
    {
        var node = query.IsRelative ? currentNode : root;

        foreach (var segment in query.Segments)
        {
            if (node is null && segment.Kind is SingularQuerySegmentKind.Name)
            {
                return [];
            }

            switch (segment.Kind)
            {
                case SingularQuerySegmentKind.Name:
                    if (node is JsonObject obj && obj.TryGetPropertyValue(segment.Name!, out var value))
                    {
                        node = value;
                    }
                    else
                    {
                        return [];
                    }

                    break;
                case SingularQuerySegmentKind.Index:
                    if (node is JsonArray array)
                    {
                        var idx = NormalizeIndex(segment.Index, array.Count);
                        if (idx >= 0 && idx < array.Count)
                        {
                            node = array[(int)idx];
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

    // ---- Built-in Functions ----

    private static bool EvaluateFunctionAsLogical(FunctionCallExpression func, JsonNode? currentNode, JsonNode? root)
    {
        if (func.ResultType is FunctionExpressionType.LogicalType)
        {
            return func.Name switch
            {
                "match" => EvaluateMatchFunction(func, currentNode, root),
                "search" => EvaluateSearchFunction(func, currentNode, root),
                _ => false,
            };
        }

        // NodesType → convert to logical (non-empty = true)
        if (func.ResultType is FunctionExpressionType.NodesType)
        {
            var nodes = EvaluateFunctionAsNodes(func, currentNode, root);
            return nodes.Count > 0;
        }

        return false;
    }

    private static ResolvedValue EvaluateFunction(FunctionCallExpression func, JsonNode? currentNode, JsonNode? root)
    {
        return func.Name switch
        {
            "length" => EvaluateLengthFunction(func, currentNode, root),
            "count" => EvaluateCountFunction(func, currentNode, root),
            "value" => EvaluateValueFunction(func, currentNode, root),
            "match" => EvaluateMatchFunction(func, currentNode, root)
                ? ResolvedValue.FromNode(JsonValue.Create(value: true))
                : ResolvedValue.FromNode(JsonValue.Create(value: false)),
            "search" => EvaluateSearchFunction(func, currentNode, root)
                ? ResolvedValue.FromNode(JsonValue.Create(value: true))
                : ResolvedValue.FromNode(JsonValue.Create(value: false)),
            _ => ResolvedValue.FromNothing(),
        };
    }

    private static List<JsonNode?> EvaluateFunctionAsNodes(FunctionCallExpression func, JsonNode? currentNode, JsonNode? root)
    {
        // Currently no built-in functions return NodesType
        _ = func;
        _ = currentNode;
        _ = root;
        return [];
    }

    private static ResolvedValue EvaluateLengthFunction(FunctionCallExpression func, JsonNode? currentNode, JsonNode? root)
    {
        var argValue = ResolveFunctionArgumentAsValue(func.Arguments[0], currentNode, root);
        if (argValue.IsNothing)
        {
            return ResolvedValue.FromNothing();
        }

        var node = argValue.Node;
        return node switch
        {
            JsonArray array => ResolvedValue.FromNode(JsonValue.Create(array.Count)),
            JsonObject obj => ResolvedValue.FromNode(JsonValue.Create(obj.Count)),
            JsonValue val when val.GetValueKind() is JsonValueKind.String =>
                ResolvedValue.FromNode(JsonValue.Create(CountUnicodeScalarValues(GetStringValue(val)!))),
            _ => ResolvedValue.FromNothing(),
        };
    }

    private static ResolvedValue EvaluateCountFunction(FunctionCallExpression func, JsonNode? currentNode, JsonNode? root)
    {
        var nodes = ResolveFunctionArgumentAsNodes(func.Arguments[0], currentNode, root);
        return ResolvedValue.FromNode(JsonValue.Create(nodes.Count));
    }

    private static ResolvedValue EvaluateValueFunction(FunctionCallExpression func, JsonNode? currentNode, JsonNode? root)
    {
        var nodes = ResolveFunctionArgumentAsNodes(func.Arguments[0], currentNode, root);
        if (nodes.Count is 1)
        {
            return ResolvedValue.FromNode(nodes[0]);
        }

        return ResolvedValue.FromNothing();
    }

    private static bool EvaluateMatchFunction(FunctionCallExpression func, JsonNode? currentNode, JsonNode? root)
    {
        var strValue = ResolveFunctionArgumentAsValue(func.Arguments[0], currentNode, root);
        var patternValue = ResolveFunctionArgumentAsValue(func.Arguments[1], currentNode, root);

        if (strValue.IsNothing || patternValue.IsNothing)
        {
            return false;
        }

        var str = GetStringFromNode(strValue.Node);
        var pattern = GetStringFromNode(patternValue.Node);

        if (str is null || pattern is null)
        {
            return false;
        }

        try
        {
            // match() requires the ENTIRE string to match (anchored)
            var regex = ConvertIRegexpToRegex(pattern);
            var fullPattern = $"^(?:{regex})$";
            return Regex.IsMatch(str, fullPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
        }
        catch (RegexParseException)
        {
            return false;
        }
    }

    private static bool EvaluateSearchFunction(FunctionCallExpression func, JsonNode? currentNode, JsonNode? root)
    {
        var strValue = ResolveFunctionArgumentAsValue(func.Arguments[0], currentNode, root);
        var patternValue = ResolveFunctionArgumentAsValue(func.Arguments[1], currentNode, root);

        if (strValue.IsNothing || patternValue.IsNothing)
        {
            return false;
        }

        var str = GetStringFromNode(strValue.Node);
        var pattern = GetStringFromNode(patternValue.Node);

        if (str is null || pattern is null)
        {
            return false;
        }

        try
        {
            // search() checks if any substring matches (unanchored)
            var regex = ConvertIRegexpToRegex(pattern);
            return Regex.IsMatch(str, regex, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
        }
        catch (RegexParseException)
        {
            return false;
        }
    }

    private static ResolvedValue ResolveFunctionArgumentAsValue(FunctionArgument arg, JsonNode? currentNode, JsonNode? root)
    {
        switch (arg.Kind)
        {
            case FunctionArgumentKind.Literal:
                return arg.Value switch
                {
                    null => ResolvedValue.FromNode(node: null),
                    true => ResolvedValue.FromNode(JsonValue.Create(value: true)),
                    false => ResolvedValue.FromNode(JsonValue.Create(value: false)),
                    string s => ResolvedValue.FromNode(JsonValue.Create(s)),
                    double d => ResolvedValue.FromNode(JsonValue.Create(d)),
                    _ => ResolvedValue.FromNothing(),
                };

            case FunctionArgumentKind.FilterQuery:
                {
                    var query = (FilterQuery)arg.Value!;
                    var nodes = EvaluateFilterQuery(query, currentNode, root);
                    if (nodes.Count is 1)
                    {
                        return ResolvedValue.FromNode(nodes[0]);
                    }

                    return ResolvedValue.FromNothing();
                }

            case FunctionArgumentKind.FunctionCall:
                {
                    var func = (FunctionCallExpression)arg.Value!;
                    return EvaluateFunction(func, currentNode, root);
                }

            default:
                return ResolvedValue.FromNothing();
        }
    }

    private static List<JsonNode?> ResolveFunctionArgumentAsNodes(FunctionArgument arg, JsonNode? currentNode, JsonNode? root)
    {
        if (arg.Kind is FunctionArgumentKind.FilterQuery)
        {
            var query = (FilterQuery)arg.Value!;
            return EvaluateFilterQuery(query, currentNode, root);
        }

        if (arg.Kind is FunctionArgumentKind.FunctionCall)
        {
            var func = (FunctionCallExpression)arg.Value!;
            return EvaluateFunctionAsNodes(func, currentNode, root);
        }

        return [];
    }

    // ---- Helpers ----

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

    private static string? GetStringFromNode(JsonNode? node)
    {
        if (node is JsonValue val && val.GetValueKind() is JsonValueKind.String)
        {
            return GetStringValue(val);
        }

        return null;
    }

    private static int CountUnicodeScalarValues(string s)
    {
        var count = 0;
        for (var i = 0; i < s.Length; i++)
        {
            count++;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++; // Skip the low surrogate
            }
        }

        return count;
    }

    /// <summary>
    /// Converts I-Regexp (RFC 9485) to .NET Regex pattern.
    /// I-Regexp '.' matches any single Unicode code point except U+000A and U+000D.
    /// .NET Regex '.' matches any char except '\n', so we need to:
    /// 1. Exclude '\r' (which .NET '.' matches)
    /// 2. Handle surrogate pairs as single code points
    /// </summary>
    private static string ConvertIRegexpToRegex(string iregexp)
    {
        var sb = new StringBuilder(iregexp.Length * 2);
        var inCharClass = false;

        for (var i = 0; i < iregexp.Length; i++)
        {
            var ch = iregexp[i];

            if (ch == '\\' && i + 1 < iregexp.Length)
            {
                // Escaped character — pass through as-is
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
                // I-Regexp dot: any Unicode code point except \n and \r
                // Must handle surrogate pairs as single code points
                sb.Append(@"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\n\r])");
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }
}
