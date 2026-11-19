using System.Globalization;
using System.Xml;
using System.Xml.XPath;

namespace Meziantou.Framework.WebPlatform.Xslt;

/// <summary>An error of an XSLT stylesheet (a static error) or of a transformation (a dynamic error).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Internal exception")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Internal exception, converted to a DOMException by the public API")]
internal sealed class XsltException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>An expanded name (https://www.w3.org/TR/xslt-10/#qname): a namespace URI (empty for no namespace) and a local name.</summary>
internal readonly record struct XsltQName(string NamespaceUri, string LocalName)
{
    public override string ToString() => NamespaceUri.Length == 0 ? LocalName : "{" + NamespaceUri + "}" + LocalName;
}

/// <summary>
/// A node-set (https://www.w3.org/TR/xpath-10/#node-sets): the nodes in document order, without duplicates. A result tree
/// fragment is a node-set of its root node.
/// </summary>
internal sealed class XsltNodeSet
{
    public static XsltNodeSet Empty { get; } = new([]);

    private readonly List<XPathNavigator> _nodes;

    private XsltNodeSet(List<XPathNavigator> nodes)
    {
        _nodes = nodes;
    }

    public int Count => _nodes.Count;

    public XPathNavigator this[int index] => _nodes[index];

    public IReadOnlyList<XPathNavigator> Nodes => _nodes;

    /// <summary>Whether the node-set is the root of a result tree fragment.</summary>
    public bool IsResultTreeFragment { get; private init; }

    public static XsltNodeSet FromSingle(XPathNavigator node) => new([node.Clone()]);

    public static XsltNodeSet FromResultTreeFragment(XPathNavigator root) => new([root]) { IsResultTreeFragment = true };

    /// <summary>Creates a sequence of nodes in the given order (the nodes sorted by xsl:sort), which is only iterated.</summary>
    public static XsltNodeSet FromSequence(List<XPathNavigator> nodes) => new(nodes);

    /// <summary>Creates a node-set from nodes in any order, which may contain duplicates.</summary>
    public static XsltNodeSet FromUnordered(List<XPathNavigator> nodes)
    {
        if (nodes.Count <= 1)
            return new XsltNodeSet(nodes);

        var sorted = true;
        for (var i = 1; i < nodes.Count; i++)
        {
            if (nodes[i - 1].ComparePosition(nodes[i]) != XmlNodeOrder.Before)
            {
                sorted = false;
                break;
            }
        }

        if (sorted)
            return new XsltNodeSet(nodes);

        // A stable sort: the nodes of different trees keep a consistent order (see DomXPathNavigator.ComparePosition)
        var ordered = nodes.Select((node, index) => (node, index)).OrderBy(item => item, NodeOrderComparer.Instance).Select(item => item.node).ToList();
        var result = new List<XPathNavigator>(ordered.Count);
        foreach (var node in ordered)
        {
            if (result.Count == 0 || !result[^1].IsSamePosition(node))
            {
                result.Add(node);
            }
        }

        return new XsltNodeSet(result);
    }

    /// <summary>Creates a node-set from the nodes of an iterator of System.Xml.XPath.</summary>
    public static XsltNodeSet FromIterator(XPathNodeIterator iterator)
    {
        var nodes = new List<XPathNavigator>();
        while (iterator.MoveNext())
        {
            nodes.Add(iterator.Current!.Clone());
        }

        return FromUnordered(nodes);
    }

    public XPathNodeIterator CreateIterator() => new XsltNodeSetIterator(_nodes);

    private sealed class NodeOrderComparer : IComparer<(XPathNavigator Node, int Index)>
    {
        public static NodeOrderComparer Instance { get; } = new();

        public int Compare((XPathNavigator Node, int Index) x, (XPathNavigator Node, int Index) y)
        {
            return x.Node.ComparePosition(y.Node) switch
            {
                XmlNodeOrder.Before => -1,
                XmlNodeOrder.After => 1,
                XmlNodeOrder.Same => 0,
                _ => x.Index.CompareTo(y.Index),
            };
        }
    }
}

/// <summary>An iterator over a list of nodes, which gives the nodes of a node-set to System.Xml.XPath.</summary>
internal sealed class XsltNodeSetIterator : XPathNodeIterator
{
    private readonly IReadOnlyList<XPathNavigator> _nodes;
    private int _position;

    public XsltNodeSetIterator(IReadOnlyList<XPathNavigator> nodes)
    {
        _nodes = nodes;
    }

    public override XPathNavigator? Current => _position > 0 && _position <= _nodes.Count ? _nodes[_position - 1] : null;

    public override int CurrentPosition => _position;

    public override int Count => _nodes.Count;

    public override XPathNodeIterator Clone() => new XsltNodeSetIterator(_nodes) { _position = _position };

    public override bool MoveNext()
    {
        if (_position >= _nodes.Count)
            return false;

        _position++;
        return true;
    }
}

/// <summary>
/// The context of the evaluation of an expression: the context node, the context position and the context size
/// (https://www.w3.org/TR/xpath-10/#section-Introduction), given to System.Xml.XPath as an iterator positioned on the node.
/// </summary>
internal sealed class XsltContextIterator(XPathNavigator current, int position, int size) : XPathNodeIterator
{
    public override XPathNavigator Current => current;

    public override int CurrentPosition => position;

    public override int Count => size;

    public override XPathNodeIterator Clone() => new XsltContextIterator(current, position, size);

    public override bool MoveNext() => false;
}

/// <summary>The conversions of the XPath values (https://www.w3.org/TR/xpath-10/#section-Function-Calls): strings, numbers, booleans and node-sets.</summary>
internal static class XsltValues
{
    /// <summary>Converts a value returned by System.Xml.XPath (or by an XSLT function) to an XSLT value.</summary>
    public static object FromXPath(object? value) => value switch
    {
        string or double or bool => value,
        XPathNodeIterator iterator => XsltNodeSet.FromIterator(iterator),
        XPathNavigator navigator => XsltNodeSet.FromSingle(navigator),
        XsltNodeSet => value,
        int number => (double)number,
        null => "",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>Converts an XSLT value to a value given to System.Xml.XPath.</summary>
    public static object ToXPath(object value) => value is XsltNodeSet nodeSet ? nodeSet.CreateIterator() : value;

    /// <summary>https://www.w3.org/TR/xpath-10/#function-string</summary>
    public static string ToStringValue(object value) => value switch
    {
        string text => text,
        double number => NumberToString(number),
        bool boolean => boolean ? "true" : "false",
        XsltNodeSet nodeSet => nodeSet.Count == 0 ? "" : nodeSet[0].Value,
        _ => ToStringValue(FromXPath(value)),
    };

    /// <summary>https://www.w3.org/TR/xpath-10/#function-number</summary>
    public static double ToNumber(object value) => value switch
    {
        double number => number,
        bool boolean => boolean ? 1 : 0,
        string text => StringToNumber(text),
        XsltNodeSet nodeSet => StringToNumber(ToStringValue(nodeSet)),
        _ => ToNumber(FromXPath(value)),
    };

    /// <summary>https://www.w3.org/TR/xpath-10/#function-boolean</summary>
    public static bool ToBoolean(object value) => value switch
    {
        bool boolean => boolean,
        double number => number != 0 && !double.IsNaN(number),
        string text => text.Length > 0,
        XsltNodeSet nodeSet => nodeSet.Count > 0,
        _ => ToBoolean(FromXPath(value)),
    };

    /// <summary>The conversion of a string to a number (https://www.w3.org/TR/xpath-10/#function-number): NaN when the string is not a Number.</summary>
    public static double StringToNumber(string text)
    {
        var value = text.AsSpan().Trim(" \t\r\n");
        if (value.IsEmpty)
            return double.NaN;

        var index = value[0] == '-' ? 1 : 0;
        var digits = 0;
        var dot = false;
        for (var i = index; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsAsciiDigit(c))
            {
                digits++;
            }
            else if (c == '.' && !dot)
            {
                dot = true;
            }
            else
            {
                return double.NaN;
            }
        }

        if (digits == 0)
            return double.NaN;

        return double.Parse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    /// <summary>The conversion of a number to a string (https://www.w3.org/TR/xpath-10/#function-string): no exponent, and no fractional part for an integer.</summary>
    public static string NumberToString(double number)
    {
        if (double.IsNaN(number))
            return "NaN";

        if (double.IsPositiveInfinity(number))
            return "Infinity";

        if (double.IsNegativeInfinity(number))
            return "-Infinity";

        if (number == 0)
            return "0";

        if (Math.Abs(number) < 1e15 && number == Math.Floor(number))
            return ((long)number).ToString(CultureInfo.InvariantCulture);

        var text = number.ToString("R", CultureInfo.InvariantCulture);
        var exponentIndex = text.IndexOf('E', StringComparison.Ordinal);
        if (exponentIndex < 0)
            return text;

        // Expand the exponent: the digits of the mantissa, and the position of the decimal point
        var negative = text[0] == '-';
        var mantissa = text.AsSpan(negative ? 1 : 0, exponentIndex - (negative ? 1 : 0));
        var exponent = int.Parse(text.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var digits = new System.Text.StringBuilder();
        var pointPosition = 0;
        var seenPoint = false;
        foreach (var c in mantissa)
        {
            if (c == '.')
            {
                seenPoint = true;
                continue;
            }

            digits.Append(c);
            if (!seenPoint)
            {
                pointPosition++;
            }
        }

        pointPosition += exponent;
        var allDigits = digits.ToString();
        string result;
        if (pointPosition <= 0)
        {
            result = "0." + new string('0', -pointPosition) + allDigits;
        }
        else if (pointPosition >= allDigits.Length)
        {
            result = allDigits + new string('0', pointPosition - allDigits.Length);
        }
        else
        {
            result = allDigits[..pointPosition] + "." + allDigits[pointPosition..];
        }

        result = result.Contains('.', StringComparison.Ordinal) ? result.TrimEnd('0').TrimEnd('.') : result;
        return negative ? "-" + result : result;
    }

    /// <summary>The type of a value, as returned by the <c>exsl:object-type()</c> function.</summary>
    public static string GetObjectType(object value) => value switch
    {
        string => "string",
        double => "number",
        bool => "boolean",
        XsltNodeSet { IsResultTreeFragment: true } => "RTF",
        XsltNodeSet { Count: 1 } nodeSet when nodeSet[0].NodeType == XPathNodeType.Root && nodeSet[0].UnderlyingObject is Dom.DocumentFragment => "RTF",
        _ => "node-set",
    };

    /// <summary>Whether a string is an NCName (https://www.w3.org/TR/xml-names/#NT-NCName).</summary>
    public static bool IsNCName(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty || !XmlConvert.IsStartNCNameChar(name[0]))
            return false;

        foreach (var c in name[1..])
        {
            if (!XmlConvert.IsNCNameChar(c))
                return false;
        }

        return true;
    }

    /// <summary>Whether a string is a QName (https://www.w3.org/TR/xml-names/#NT-QName).</summary>
    public static bool IsQName(ReadOnlySpan<char> name)
    {
        var colon = name.IndexOf(':');
        return colon < 0 ? IsNCName(name) : IsNCName(name[..colon]) && IsNCName(name[(colon + 1)..]);
    }
}
