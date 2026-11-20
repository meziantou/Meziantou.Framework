using System.Xml;
using System.Xml.XPath;

namespace Meziantou.Framework.WebPlatform.Xslt;

/// <summary>
/// A pattern (https://www.w3.org/TR/xslt-10/#patterns): alternatives separated by <c>|</c>, each one a location path
/// pattern. A node matches an alternative when System.Xml.XPath matches it (<see cref="XPathNavigator.Matches(System.Xml.XPath.XPathExpression)"/>),
/// or, for an alternative that starts with <c>id()</c> or <c>key()</c>, when it is one of the nodes the alternative
/// selects from the root of the tree of the node.
/// </summary>
internal sealed class XsltPattern
{
    private XsltPattern(string text, XsltPatternAlternative[] alternatives)
    {
        Text = text;
        Alternatives = alternatives;
    }

    public string Text { get; }

    public XsltPatternAlternative[] Alternatives { get; }

    public static XsltPattern Compile(string text, XsltExpressionContext context)
    {
        var alternatives = new List<XsltPatternAlternative>();
        foreach (var alternativeText in SplitAlternatives(text))
        {
            var info = XsltPatternParser.Parse(alternativeText) ?? throw new XsltException($"'{text}' is not a valid pattern");
            var expression = XsltExpression.Compile(alternativeText, context);
            alternatives.Add(new XsltPatternAlternative(expression, info));
        }

        if (alternatives.Count == 0)
            throw new XsltException($"'{text}' is not a valid pattern");

        return new XsltPattern(text, [.. alternatives]);
    }

    public bool Matches(XPathNavigator node)
    {
        foreach (var alternative in Alternatives)
        {
            if (alternative.Matches(node))
                return true;
        }

        return false;
    }

    /// <summary>Splits a pattern at the <c>|</c> that are not in a predicate, in parentheses or in a literal.</summary>
    private static List<string> SplitAlternatives(string text)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '"' or '\'':
                    var end = text.AsSpan(i + 1).IndexOf(c);
                    if (end < 0)
                        throw new XsltException($"'{text}' is not a valid pattern: unterminated literal");

                    i += end + 1;
                    break;
                case '(' or '[':
                    depth++;
                    break;
                case ')' or ']':
                    depth--;
                    break;
                case '|' when depth == 0:
                    result.Add(text[start..i]);
                    start = i + 1;
                    break;
            }
        }

        result.Add(text[start..]);
        return result;
    }
}

/// <summary>The kinds of nodes an alternative of a pattern can match, determined by its last step.</summary>
internal enum XsltPatternNodeKind
{
    Any,
    Root,
    Element,
    Attribute,
    Text,
    Comment,
    ProcessingInstruction,

    /// <summary>The nodes that can be children: elements, text nodes, comments and processing instructions.</summary>
    Child,
}

/// <summary>An alternative of a pattern, with its default priority (https://www.w3.org/TR/xslt-10/#conflict).</summary>
internal sealed class XsltPatternAlternative
{
    private readonly XsltExpression _expression;
    private readonly XsltPatternInfo _info;

    public XsltPatternAlternative(XsltExpression expression, XsltPatternInfo info)
    {
        _expression = expression;
        _info = info;
    }

    public double DefaultPriority => _info.DefaultPriority;

    /// <summary>The local name the matched elements or attributes must have, if any (a quick test before the match).</summary>
    public string? LocalName => _info.LocalName;

    public XsltPatternNodeKind NodeKind => _info.NodeKind;

    public bool Matches(XPathNavigator node)
    {
        if (!MatchesKind(node.NodeType))
            return false;

        if (_info.LocalName is { } localName && node.LocalName != localName)
            return false;

        XsltExecution.GetCurrent().CheckLimits();
        if (_info.StartsWithIdOrKey)
        {
            // id() and key() select the same nodes whatever the context node of the tree: the alternative matches the
            // nodes it selects from the root (https://www.w3.org/TR/xslt-10/#patterns)
            var root = node.Clone();
            root.MoveToRoot();
            var selected = _expression.EvaluateNodeSet(root);
            foreach (var candidate in selected.Nodes)
            {
                if (candidate.IsSamePosition(node))
                    return true;
            }

            return false;
        }

        try
        {
            return node.Matches(_expression.Compiled);
        }
        catch (XPathException ex)
        {
            for (Exception? inner = ex.InnerException; inner is not null; inner = inner.InnerException)
            {
                if (inner is XsltException xsltException)
                    throw new XsltException(xsltException.Message, xsltException);
            }

            throw new XsltException($"Error while matching the pattern '{_expression.Text}': {ex.Message}", ex);
        }
    }

    private bool MatchesKind(XPathNodeType type) => _info.NodeKind switch
    {
        XsltPatternNodeKind.Root => type == XPathNodeType.Root,
        XsltPatternNodeKind.Element => type == XPathNodeType.Element,
        XsltPatternNodeKind.Attribute => type == XPathNodeType.Attribute,
        XsltPatternNodeKind.Text => type is XPathNodeType.Text or XPathNodeType.Whitespace or XPathNodeType.SignificantWhitespace,
        XsltPatternNodeKind.Comment => type == XPathNodeType.Comment,
        XsltPatternNodeKind.ProcessingInstruction => type == XPathNodeType.ProcessingInstruction,
        XsltPatternNodeKind.Child => type is XPathNodeType.Element or XPathNodeType.Text or XPathNodeType.Whitespace or XPathNodeType.SignificantWhitespace or XPathNodeType.Comment or XPathNodeType.ProcessingInstruction,
        _ => true,
    };
}

/// <summary>The syntactic properties of an alternative of a pattern.</summary>
internal sealed record XsltPatternInfo(double DefaultPriority, XsltPatternNodeKind NodeKind, string? LocalName, bool StartsWithIdOrKey);

/// <summary>
/// Checks that an alternative of a pattern is a LocationPathPattern (https://www.w3.org/TR/xslt-10/#NT-LocationPathPattern):
/// only the child and attribute axes, <c>/</c> and <c>//</c>, and an <c>id()</c> or <c>key()</c> call at the start (the
/// predicates are any expression, checked by the XPath compiler). Computes the default priority of the alternative.
/// </summary>
internal ref struct XsltPatternParser
{
    private readonly ReadOnlySpan<char> _text;
    private int _position;

    private XsltPatternParser(ReadOnlySpan<char> text)
    {
        _text = text;
    }

    public static XsltPatternInfo? Parse(string text)
    {
        var parser = new XsltPatternParser(text);
        return parser.ParseAlternative();
    }

    private XsltPatternInfo? ParseAlternative()
    {
        SkipWhitespace();
        if (AtEnd)
            return null;

        var startsWithIdOrKey = false;
        var hasPrefix = false;
        var save = _position;
        var name = ReadNCName();
        if (name is "id" or "key")
        {
            SkipWhitespace();
            if (Peek == '(')
            {
                if (!SkipBalanced('(', ')'))
                    return null;

                startsWithIdOrKey = true;
                hasPrefix = true;
                SkipWhitespace();
                if (AtEnd)
                    return new XsltPatternInfo(0.5, XsltPatternNodeKind.Any, null, StartsWithIdOrKey: true);

                if (Peek != '/')
                    return null;
            }
            else
            {
                _position = save;
            }
        }
        else
        {
            _position = save;
        }

        if (Peek == '/')
        {
            _position++;
            hasPrefix = true;
            if (Peek == '/')
            {
                _position++;
            }
            else
            {
                SkipWhitespace();
                if (AtEnd)
                    return startsWithIdOrKey ? null : new XsltPatternInfo(0.5, XsltPatternNodeKind.Root, null, StartsWithIdOrKey: false);
            }
        }

        // RelativePathPattern ::= StepPattern | RelativePathPattern '/' StepPattern | RelativePathPattern '//' StepPattern
        var steps = 0;
        (double Priority, XsltPatternNodeKind Kind, string? LocalName, bool HasPredicates) lastStep;
        while (true)
        {
            if (ParseStep() is not { } step)
                return null;

            lastStep = step;
            steps++;
            SkipWhitespace();
            if (AtEnd)
                break;

            if (Peek != '/')
                return null;

            _position++;
            if (Peek == '/')
            {
                _position++;
            }
        }

        var priority = steps == 1 && !hasPrefix && !lastStep.HasPredicates ? lastStep.Priority : 0.5;
        return new XsltPatternInfo(priority, lastStep.Kind, lastStep.LocalName, startsWithIdOrKey);
    }

    /// <summary>StepPattern ::= ChildOrAttributeAxisSpecifier NodeTest Predicate*</summary>
    private (double Priority, XsltPatternNodeKind Kind, string? LocalName, bool HasPredicates)? ParseStep()
    {
        SkipWhitespace();
        var attribute = false;
        if (Peek == '@')
        {
            _position++;
            attribute = true;
            SkipWhitespace();
        }
        else
        {
            var save = _position;
            var axis = ReadNCName();
            SkipWhitespace();
            if (axis is not null && Peek == ':' && PeekAt(1) == ':')
            {
                if (axis is not ("child" or "attribute"))
                    return null;

                attribute = axis == "attribute";
                _position += 2;
                SkipWhitespace();
            }
            else
            {
                _position = save;
            }
        }

        (double Priority, XsltPatternNodeKind Kind, string? LocalName) test;
        if (Peek == '*')
        {
            _position++;
            test = (-0.5, attribute ? XsltPatternNodeKind.Attribute : XsltPatternNodeKind.Element, null);
        }
        else
        {
            var name = ReadNCName();
            if (name is null)
                return null;

            if (Peek == ':' && PeekAt(1) != ':')
            {
                _position++;
                if (Peek == '*')
                {
                    _position++;
                    test = (-0.25, attribute ? XsltPatternNodeKind.Attribute : XsltPatternNodeKind.Element, null);
                }
                else
                {
                    var localName = ReadNCName();
                    if (localName is null)
                        return null;

                    test = (0, attribute ? XsltPatternNodeKind.Attribute : XsltPatternNodeKind.Element, localName);
                }
            }
            else
            {
                var save = _position;
                SkipWhitespace();
                if (Peek == '(' && name is "node" or "text" or "comment" or "processing-instruction")
                {
                    _position++;
                    SkipWhitespace();
                    var hasLiteral = false;
                    if (name == "processing-instruction" && Peek is '"' or '\'')
                    {
                        var quote = Peek;
                        var end = _text[(_position + 1)..].IndexOf(quote);
                        if (end < 0)
                            return null;

                        _position += end + 2;
                        hasLiteral = true;
                        SkipWhitespace();
                    }

                    if (Peek != ')')
                        return null;

                    _position++;
                    var kind = name switch
                    {
                        "text" => XsltPatternNodeKind.Text,
                        "comment" => XsltPatternNodeKind.Comment,
                        "processing-instruction" => XsltPatternNodeKind.ProcessingInstruction,
                        _ => attribute ? XsltPatternNodeKind.Attribute : XsltPatternNodeKind.Child,
                    };

                    // The node types other than node() never match on the attribute axis
                    if (attribute && kind != XsltPatternNodeKind.Attribute)
                    {
                        kind = XsltPatternNodeKind.Root;
                    }

                    test = (hasLiteral ? 0 : -0.5, kind, null);
                }
                else
                {
                    _position = save;
                    test = (0, attribute ? XsltPatternNodeKind.Attribute : XsltPatternNodeKind.Element, name);
                }
            }
        }

        var hasPredicates = false;
        SkipWhitespace();
        while (Peek == '[')
        {
            if (!SkipBalanced('[', ']'))
                return null;

            hasPredicates = true;
            SkipWhitespace();
        }

        return (test.Priority, test.Kind, test.LocalName, hasPredicates);
    }

    private readonly bool AtEnd => _position >= _text.Length;

    private readonly char Peek => _position < _text.Length ? _text[_position] : '\0';

    private readonly char PeekAt(int offset) => _position + offset < _text.Length ? _text[_position + offset] : '\0';

    private void SkipWhitespace()
    {
        while (_position < _text.Length && _text[_position] is ' ' or '\t' or '\r' or '\n')
        {
            _position++;
        }
    }

    private string? ReadNCName()
    {
        if (AtEnd || !XmlConvert.IsStartNCNameChar(_text[_position]))
            return null;

        var start = _position;
        _position++;
        while (_position < _text.Length && XmlConvert.IsNCNameChar(_text[_position]))
        {
            _position++;
        }

        return _text[start.._position].ToString();
    }

    /// <summary>Skips a bracketed part (a predicate, or the arguments of a function), with the literals it contains.</summary>
    private bool SkipBalanced(char open, char close)
    {
        var depth = 0;
        while (_position < _text.Length)
        {
            var c = _text[_position];
            if (c is '"' or '\'')
            {
                var end = _text[(_position + 1)..].IndexOf(c);
                if (end < 0)
                    return false;

                _position += end + 2;
                continue;
            }

            _position++;
            if (c == open)
            {
                depth++;
            }
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                    return true;
            }
        }

        return false;
    }
}
