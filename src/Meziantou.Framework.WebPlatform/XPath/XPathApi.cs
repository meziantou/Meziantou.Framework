using System.Collections.Concurrent;
using System.Xml;
using System.Xml.XPath;
using Meziantou.Framework.WebPlatform.XPath;
using ClrXPathExpression = System.Xml.XPath.XPathExpression;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#callbackdef-xpathnsresolver</summary>
[IdlInterface("XPathNSResolver", "https://dom.spec.whatwg.org/#callbackdef-xpathnsresolver")]
public interface IXPathNSResolver
{
    /// <summary>Returns the namespace URI associated with the given prefix, or null when there is none.</summary>
    [IdlMember("lookupNamespaceURI", "https://dom.spec.whatwg.org/#dom-xpathnsresolver-lookupnamespaceuri")]
    string? LookupNamespaceUri(string? prefix);
}

/// <summary>https://dom.spec.whatwg.org/#interface-xpathresult (https://www.w3.org/TR/DOM-Level-3-XPath/xpath.html#XPathResult)</summary>
[IdlInterface("XPathResult", "https://dom.spec.whatwg.org/#xpathresult")]
public sealed class XPathResult
{
    /// <summary>The type that lets the evaluation choose the natural type of the result.</summary>
    [IdlMember("ANY_TYPE", "https://dom.spec.whatwg.org/#dom-xpathresult-any_type")]
    public const ushort AnyType = 0;

    /// <summary>A single number.</summary>
    [IdlMember("NUMBER_TYPE", "https://dom.spec.whatwg.org/#dom-xpathresult-number_type")]
    public const ushort NumberType = 1;

    /// <summary>A single string.</summary>
    [IdlMember("STRING_TYPE", "https://dom.spec.whatwg.org/#dom-xpathresult-string_type")]
    public const ushort StringType = 2;

    /// <summary>A single boolean.</summary>
    [IdlMember("BOOLEAN_TYPE", "https://dom.spec.whatwg.org/#dom-xpathresult-boolean_type")]
    public const ushort BooleanType = 3;

    /// <summary>An iterator over the nodes, in any order.</summary>
    [IdlMember("UNORDERED_NODE_ITERATOR_TYPE", "https://dom.spec.whatwg.org/#dom-xpathresult-unordered_node_iterator_type")]
    public const ushort UnorderedNodeIteratorType = 4;

    /// <summary>An iterator over the nodes, in document order.</summary>
    [IdlMember("ORDERED_NODE_ITERATOR_TYPE", "https://dom.spec.whatwg.org/#dom-xpathresult-ordered_node_iterator_type")]
    public const ushort OrderedNodeIteratorType = 5;

    /// <summary>A snapshot of the nodes, in any order.</summary>
    [IdlMember("UNORDERED_NODE_SNAPSHOT_TYPE", "https://dom.spec.whatwg.org/#dom-xpathresult-unordered_node_snapshot_type")]
    public const ushort UnorderedNodeSnapshotType = 6;

    /// <summary>A snapshot of the nodes, in document order.</summary>
    [IdlMember("ORDERED_NODE_SNAPSHOT_TYPE", "https://dom.spec.whatwg.org/#dom-xpathresult-ordered_node_snapshot_type")]
    public const ushort OrderedNodeSnapshotType = 7;

    /// <summary>A single node, any of the matching ones.</summary>
    [IdlMember("ANY_UNORDERED_NODE_TYPE", "https://dom.spec.whatwg.org/#dom-xpathresult-any_unordered_node_type")]
    public const ushort AnyUnorderedNodeType = 8;

    /// <summary>A single node, the first matching one in document order.</summary>
    [IdlMember("FIRST_ORDERED_NODE_TYPE", "https://dom.spec.whatwg.org/#dom-xpathresult-first_ordered_node_type")]
    public const ushort FirstOrderedNodeType = 9;

    private readonly double _number;
    private readonly string _string = "";
    private readonly bool _boolean;
    private readonly List<Node> _nodes = [];
    private readonly Document? _document;
    private readonly int _documentVersion;
    private int _iteratorIndex;

    internal XPathResult(ushort resultType, object value, Node contextNode)
    {
        ResultType = resultType;
        switch (resultType)
        {
            case NumberType:
                _number = (double)value;
                break;
            case StringType:
                _string = (string)value;
                break;
            case BooleanType:
                _boolean = (bool)value;
                break;
            default:
                _nodes = (List<Node>)value;
                _document = contextNode as Document ?? contextNode.OwnerDocument;
                _documentVersion = _document?.DomVersion ?? 0;
                break;
        }
    }

    /// <summary>Gets the type of the result, one of the <c>*_TYPE</c> constants.</summary>
    [IdlMember("resultType", "https://dom.spec.whatwg.org/#dom-xpathresult-resulttype")]
    public ushort ResultType { get; }

    /// <summary>Gets the number of the result; throws a <c>TypeError</c> when the result is not a number.</summary>
    [IdlMember("numberValue", "https://dom.spec.whatwg.org/#dom-xpathresult-numbervalue")]
    public double NumberValue => ResultType == NumberType ? _number : throw new TypeErrorException("The result is not a number");

    /// <summary>Gets the string of the result; throws a <c>TypeError</c> when the result is not a string.</summary>
    [IdlMember("stringValue", "https://dom.spec.whatwg.org/#dom-xpathresult-stringvalue")]
    public string StringValue => ResultType == StringType ? _string : throw new TypeErrorException("The result is not a string");

    /// <summary>Gets the boolean of the result; throws a <c>TypeError</c> when the result is not a boolean.</summary>
    [IdlMember("booleanValue", "https://dom.spec.whatwg.org/#dom-xpathresult-booleanvalue")]
    public bool BooleanValue => ResultType == BooleanType ? _boolean : throw new TypeErrorException("The result is not a boolean");

    /// <summary>
    /// Gets the node of the result, or null when none matched; throws a <c>TypeError</c> when the result is not a
    /// single node.
    /// </summary>
    [IdlMember("singleNodeValue", "https://dom.spec.whatwg.org/#dom-xpathresult-singlenodevalue")]
    public Node? SingleNodeValue => ResultType is AnyUnorderedNodeType or FirstOrderedNodeType
        ? (_nodes.Count > 0 ? _nodes[0] : null)
        : throw new TypeErrorException("The result is not a single node");

    /// <summary>Whether the document was modified since the result was created, which invalidates iterators.</summary>
    [IdlMember("invalidIteratorState", "https://dom.spec.whatwg.org/#dom-xpathresult-invaliditeratorstate")]
    public bool InvalidIteratorState => ResultType is UnorderedNodeIteratorType or OrderedNodeIteratorType && _document is not null && _document.DomVersion != _documentVersion;

    /// <summary>
    /// Gets the number of nodes of the snapshot; throws a <c>TypeError</c> when the result is not a snapshot.
    /// </summary>
    [IdlMember("snapshotLength", "https://dom.spec.whatwg.org/#dom-xpathresult-snapshotlength")]
    public uint SnapshotLength => ResultType is UnorderedNodeSnapshotType or OrderedNodeSnapshotType
        ? (uint)_nodes.Count
        : throw new TypeErrorException("The result is not a snapshot");

    /// <summary>
    /// Returns the next node of the iterator, or null at the end; throws an <c>InvalidStateError</c> when the document
    /// was modified.
    /// </summary>
    [IdlMember("iterateNext", "https://dom.spec.whatwg.org/#dom-xpathresult-iteratenext")]
    public Node? IterateNext()
    {
        if (ResultType is not (UnorderedNodeIteratorType or OrderedNodeIteratorType))
            throw new TypeErrorException("The result is not an iterator");

        if (InvalidIteratorState)
            throw DomException.InvalidState("The document has been mutated since the result was returned");

        return _iteratorIndex < _nodes.Count ? _nodes[_iteratorIndex++] : null;
    }

    /// <summary>Returns the node of the snapshot at the given index, or null when the index is out of range.</summary>
    [IdlMember("snapshotItem", "https://dom.spec.whatwg.org/#dom-xpathresult-snapshotitem")]
    public Node? SnapshotItem(uint index)
    {
        if (ResultType is not (UnorderedNodeSnapshotType or OrderedNodeSnapshotType))
            throw new TypeErrorException("The result is not a snapshot");

        return index < (uint)_nodes.Count ? _nodes[(int)index] : null;
    }
}

/// <summary>https://dom.spec.whatwg.org/#interface-xpathexpression</summary>
[IdlInterface("XPathExpression", "https://dom.spec.whatwg.org/#xpathexpression")]
public sealed class XPathExpression
{
    private static readonly ConcurrentDictionary<(string Expression, bool Html), ClrXPathExpression> Cache = new();
    private static readonly System.Buffers.SearchValues<char> NumberChars = System.Buffers.SearchValues.Create("0123456789.");

    private readonly string _expression;
    private readonly IXPathNSResolver? _resolver;

    internal XPathExpression(string expression, IXPathNSResolver? resolver)
    {
        _expression = expression;
        _resolver = resolver;

        // Syntax errors are reported by createExpression(); namespace prefixes are resolved on evaluation
        _ = GetCompiled(isHtml: false);
    }

    private static ClrXPathExpression GetCompiled(bool isHtml, string expression) => Cache.GetOrAdd((expression, isHtml), static key =>
    {
        try
        {
            return ClrXPathExpression.Compile(key.Html ? XPathExpressionRewriter.Rewrite(key.Expression) : key.Expression);
        }
        catch (XPathException ex)
        {
            throw DomException.Syntax($"'{key.Expression}' is not a valid XPath expression: {ex.Message}");
        }
    });

    private ClrXPathExpression GetCompiled(bool isHtml)
    {
        var compiled = GetCompiled(isHtml, _expression);
        if (Cache.Count > 1024)
        {
            Cache.Clear();
        }

        return compiled;
    }

    /// <summary>Compiles the expression (rewritten for HTML documents) and binds the namespace resolver.</summary>
    private ClrXPathExpression Compile(bool isHtml)
    {
        var clone = GetCompiled(isHtml).Clone();
        try
        {
            clone.SetContext(new DomXsltContext(_resolver));
        }
        catch (Exception ex)
        {
            RethrowResolverOrDomException(ex);
            throw;
        }

        return clone;
    }

    /// <summary>Rethrows the DOM exception reported by the XSLT context (System.Xml.XPath may have wrapped it).</summary>
    private static void RethrowResolverOrDomException(Exception exception)
    {
        if (GetDomException(exception) is { } domException)
            throw domException;
    }

    private static DomException? GetDomException(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is XPathDomException xpathDomException)
                return xpathDomException.DomException;

            if (current is DomException domException)
                return domException;
        }

        return null;
    }

    /// <summary>https://www.w3.org/TR/DOM-Level-3-XPath/xpath.html#XPathExpression-evaluate</summary>
    [IdlMember("evaluate", "https://dom.spec.whatwg.org/#dom-xpathexpression-evaluate")]
    public XPathResult Evaluate(Node contextNode, ushort type = XPathResult.AnyType, XPathResult? result = null)
    {
        ArgumentNullException.ThrowIfNull(contextNode);
        _ = result; // A new result is always created

        if (contextNode is DocumentType)
            throw DomException.NotSupported("The context node cannot be a document type");

        if (type > XPathResult.FirstOrderedNodeType)
            throw DomException.NotSupported($"The result type {type} is not supported");

        var document = contextNode as Document ?? contextNode.NodeDocument;
        var expression = Compile(document.IsHtmlDocument);
        try
        {
            // The node-sets are evaluated lazily: the errors of the steps (e.g. "0/p", a path from a number) are reported
            // while the nodes of the result are collected
            var value = new DomXPathNavigator(contextNode).Evaluate(expression);
            return CreateResult(value, type, contextNode);
        }
        catch (Exception ex)
        {
            RethrowResolverOrDomException(ex);
            if (ex is XPathException)
                throw new TypeErrorException(ex.Message, ex);

            throw;
        }
    }

    private static XPathResult CreateResult(object value, ushort type, Node contextNode)
    {
        if (type == XPathResult.AnyType)
        {
            type = value switch
            {
                double => XPathResult.NumberType,
                string => XPathResult.StringType,
                bool => XPathResult.BooleanType,
                _ => XPathResult.UnorderedNodeIteratorType,
            };
        }

        switch (type)
        {
            case XPathResult.NumberType:
                return new XPathResult(type, value switch
                {
                    double number => number,
                    string s => XmlConvertToNumber(s),
                    bool b => b ? 1d : 0d,
                    XPathNodeIterator iterator => XmlConvertToNumber(GetFirstNodeStringValue(iterator)),
                    _ => double.NaN,
                }, contextNode);

            case XPathResult.StringType:
                return new XPathResult(type, value switch
                {
                    double number => NumberToString(number),
                    string s => s,
                    bool b => b ? "true" : "false",
                    XPathNodeIterator iterator => GetFirstNodeStringValue(iterator),
                    _ => "",
                }, contextNode);

            case XPathResult.BooleanType:
                return new XPathResult(type, value switch
                {
                    double number => number != 0 && !double.IsNaN(number),
                    string s => s.Length > 0,
                    bool b => b,
                    XPathNodeIterator iterator => iterator.MoveNext(),
                    _ => false,
                }, contextNode);

            default:
                if (value is not XPathNodeIterator nodeIterator)
                    throw new TypeErrorException("The result of the expression is not a node-set");

                var nodes = CollectNodes(nodeIterator);
                if (type is XPathResult.AnyUnorderedNodeType or XPathResult.FirstOrderedNodeType && nodes.Count > 1)
                {
                    nodes.RemoveRange(1, nodes.Count - 1);
                }

                return new XPathResult(type, nodes, contextNode);
        }
    }

    /// <summary>Returns the nodes of a node-set in document order (namespace nodes are not DOM nodes and are skipped).</summary>
    private static List<Node> CollectNodes(XPathNodeIterator iterator)
    {
        var navigators = new List<DomXPathNavigator>();
        while (iterator.MoveNext())
        {
            if (iterator.Current is DomXPathNavigator navigator && navigator.CurrentNode is not null)
            {
                navigators.Add((DomXPathNavigator)navigator.Clone());
            }
        }

        navigators.Sort(static (a, b) => a.ComparePosition(b) switch
        {
            XmlNodeOrder.Before => -1,
            XmlNodeOrder.After => 1,
            _ => 0,
        });

        var nodes = new List<Node>(navigators.Count);
        foreach (var navigator in navigators)
        {
            nodes.Add(navigator.CurrentNode!);
        }

        return nodes;
    }

    private static string GetFirstNodeStringValue(XPathNodeIterator iterator)
    {
        // The string-value of the node-set is the string-value of its first node in document order
        var first = default(XPathNavigator);
        while (iterator.MoveNext())
        {
            if (first is null || iterator.Current!.ComparePosition(first) == XmlNodeOrder.Before)
            {
                first = iterator.Current!.Clone();
            }
        }

        return first?.Value ?? "";
    }

    /// <summary>https://www.w3.org/TR/xpath-10/#function-number</summary>
    private static double XmlConvertToNumber(string value)
    {
        var trimmed = value.AsSpan().Trim(" \t\r\n");
        if (trimmed.Length == 0)
            return double.NaN;

        // Number ::= Digits ('.' Digits?)? | '.' Digits, with an optional leading minus sign
        var digits = trimmed[0] == '-' ? trimmed[1..] : trimmed;
        if (digits.Length == 0 || digits.IndexOfAnyExcept(NumberChars) >= 0 || digits.Count('.') > 1 || digits is ".")
            return double.NaN;

        return double.Parse(trimmed, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>https://www.w3.org/TR/xpath-10/#function-string (numbers)</summary>
    private static string NumberToString(double number)
    {
        if (double.IsNaN(number))
            return "NaN";

        if (double.IsPositiveInfinity(number))
            return "Infinity";

        if (double.IsNegativeInfinity(number))
            return "-Infinity";

        if (number == 0)
            return "0";

        // The shortest round-trippable representation, without the exponent notation
        var text = number.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var exponentIndex = text.IndexOf('E', StringComparison.Ordinal);
        if (exponentIndex < 0)
            return text;

        var negative = text[0] == '-';
        var mantissa = text.AsSpan(negative ? 1 : 0, exponentIndex - (negative ? 1 : 0));
        var exponent = int.Parse(text.AsSpan(exponentIndex + 1), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture);
        var pointIndex = mantissa.IndexOf('.');
        var digits = pointIndex < 0 ? mantissa.ToString() : string.Concat(mantissa[..pointIndex], mantissa[(pointIndex + 1)..]);
        var integerDigitCount = (pointIndex < 0 ? mantissa.Length : pointIndex) + exponent;

        var builder = new System.Text.StringBuilder(digits.Length + Math.Abs(exponent) + 3);
        if (negative)
        {
            builder.Append('-');
        }

        if (integerDigitCount <= 0)
        {
            builder.Append("0.").Append('0', -integerDigitCount).Append(digits);
        }
        else if (integerDigitCount >= digits.Length)
        {
            builder.Append(digits).Append('0', integerDigitCount - digits.Length);
        }
        else
        {
            builder.Append(digits, 0, integerDigitCount).Append('.').Append(digits, integerDigitCount, digits.Length - integerDigitCount);
        }

        return builder.ToString();
    }
}

/// <summary>https://dom.spec.whatwg.org/#interface-xpathevaluator</summary>
[IdlInterface("XPathEvaluator", "https://dom.spec.whatwg.org/#xpathevaluator")]
public sealed class XPathEvaluator
{
    /// <summary>Creates an evaluator.</summary>
    [IdlMember("constructor", "https://dom.spec.whatwg.org/#dom-xpathevaluator-xpathevaluator")]
    public XPathEvaluator()
    {
    }

    /// <summary>Compiles the XPath expression, using the optional resolver for its namespace prefixes.</summary>
    [IdlMember("createExpression", "https://dom.spec.whatwg.org/#dom-xpathevaluatorbase-createexpression")]
    public XPathExpression CreateExpression(string expression, IXPathNSResolver? resolver = null) => XPathEvaluatorBase.CreateExpression(expression, resolver);

    /// <summary>Returns a namespace resolver for the given node, which is the node itself.</summary>
    [IdlMember("createNSResolver", "https://dom.spec.whatwg.org/#dom-xpathevaluatorbase-creatensresolver")]
    public Node CreateNSResolver(Node nodeResolver) => XPathEvaluatorBase.CreateNSResolver(nodeResolver);

    /// <summary>
    /// Evaluates the XPath expression against the context node and returns the result, of the requested type.
    /// </summary>
    [IdlMember("evaluate", "https://dom.spec.whatwg.org/#dom-xpathevaluatorbase-evaluate")]
    public XPathResult Evaluate(string expression, Node contextNode, IXPathNSResolver? resolver = null, ushort type = XPathResult.AnyType, XPathResult? result = null) =>
        XPathEvaluatorBase.Evaluate(expression, contextNode, resolver, type, result);
}

/// <summary>The steps of the XPathEvaluatorBase mixin (https://dom.spec.whatwg.org/#mixin-xpathevaluatorbase).</summary>
internal static class XPathEvaluatorBase
{
    public static XPathExpression CreateExpression(string expression, IXPathNSResolver? resolver)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return new XPathExpression(expression, resolver);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-xpathevaluatorbase-creatensresolver: returns the node (nodes are namespace resolvers).</summary>
    public static Node CreateNSResolver(Node nodeResolver)
    {
        ArgumentNullException.ThrowIfNull(nodeResolver);
        return nodeResolver;
    }

    public static XPathResult Evaluate(string expression, Node contextNode, IXPathNSResolver? resolver, ushort type, XPathResult? result)
    {
        var compiled = CreateExpression(expression, resolver);
        return compiled.Evaluate(contextNode, type, result);
    }
}

public partial class Document
{
    /// <summary>Compiles the XPath expression, using the optional resolver for its namespace prefixes.</summary>
    [IdlMember("createExpression", "https://dom.spec.whatwg.org/#dom-xpathevaluatorbase-createexpression")]
    public XPathExpression CreateExpression(string expression, IXPathNSResolver? resolver = null) => XPathEvaluatorBase.CreateExpression(expression, resolver);

    /// <summary>Returns a namespace resolver for the given node, which is the node itself.</summary>
    [IdlMember("createNSResolver", "https://dom.spec.whatwg.org/#dom-xpathevaluatorbase-creatensresolver")]
    public Node CreateNSResolver(Node nodeResolver) => XPathEvaluatorBase.CreateNSResolver(nodeResolver);

    /// <summary>
    /// Evaluates the XPath expression against the context node and returns the result, of the requested type.
    /// </summary>
    [IdlMember("evaluate", "https://dom.spec.whatwg.org/#dom-xpathevaluatorbase-evaluate")]
    public XPathResult Evaluate(string expression, Node contextNode, IXPathNSResolver? resolver = null, ushort type = XPathResult.AnyType, XPathResult? result = null) =>
        XPathEvaluatorBase.Evaluate(expression, contextNode, resolver, type, result);
}
