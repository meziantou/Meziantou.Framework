using System.Xml;
using System.Xml.XPath;
using System.Xml.Xsl;
using Meziantou.Framework.WebPlatform.Dom;
using ClrXPathExpression = System.Xml.XPath.XPathExpression;

namespace Meziantou.Framework.WebPlatform.Xslt;

/// <summary>The namespace declarations in scope of an element of a stylesheet (prefix "" is the default namespace).</summary>
internal sealed class XsltNamespaceScope
{
    public static XsltNamespaceScope Empty { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    private readonly Dictionary<string, string> _namespaces;

    private XsltNamespaceScope(Dictionary<string, string> namespaces)
    {
        _namespaces = namespaces;
    }

    public IReadOnlyDictionary<string, string> Namespaces => _namespaces;

    /// <summary>The namespace scope of an element: the one of its parent, with the declarations of the element.</summary>
    public static XsltNamespaceScope Create(XsltNamespaceScope parent, Element element)
    {
        Dictionary<string, string>? namespaces = null;
        void Set(string prefix, string? ns)
        {
            var current = (namespaces ?? parent._namespaces).GetValueOrDefault(prefix);
            if (current == (ns ?? "") || (current is null && string.IsNullOrEmpty(ns)))
                return;

            namespaces ??= new Dictionary<string, string>(parent._namespaces, StringComparer.Ordinal);
            if (string.IsNullOrEmpty(ns))
            {
                namespaces.Remove(prefix);
            }
            else
            {
                namespaces[prefix] = ns;
            }
        }

        // The prefixes of the names of the element and of its attributes are declared (e.g. an element created by createElementNS)
        Set(element.Prefix ?? "", element.NamespaceUri);
        for (var i = 0; i < element.AttributeCount; i++)
        {
            var ns = element.GetAttributeNamespace(i);
            if (ReferenceEquals(ns, Dom.Namespaces.Xmlns) || ns == Dom.Namespaces.Xmlns)
            {
                Set(element.GetAttributePrefix(i) is null ? "" : element.GetAttributeLocalName(i), element.GetAttributeValue(i));
            }
            else if (ns is not null && element.GetAttributePrefix(i) is { } prefix && prefix != "xml")
            {
                Set(prefix, ns);
            }
        }

        return namespaces is null ? parent : new XsltNamespaceScope(namespaces);
    }

    public string? Lookup(string prefix) => prefix == "xml" ? Dom.Namespaces.Xml : _namespaces.GetValueOrDefault(prefix);
}

/// <summary>The local variables and parameters visible from an element of a stylesheet (an immutable list).</summary>
internal sealed class XsltVariableScope(XsltQName name, XsltVariableScope? parent)
{
    public XsltQName Name { get; } = name;

    public XsltVariableScope? Parent { get; } = parent;

    public bool Contains(XsltQName name)
    {
        for (var scope = this; scope is not null; scope = scope.Parent)
        {
            if (scope.Name == name)
                return true;
        }

        return false;
    }
}

/// <summary>The kinds of expressions, which allow different variables and functions.</summary>
internal enum XsltExpressionKind
{
    /// <summary>An expression of an instruction: the variables in scope and all the functions.</summary>
    Expression,

    /// <summary>A pattern that cannot reference variables (the match patterns of xsl:template and xsl:key).</summary>
    PatternWithoutVariables,

    /// <summary>A pattern that can reference variables (the count and from patterns of xsl:number).</summary>
    Pattern,

    /// <summary>The use expression of xsl:key, which cannot reference variables.</summary>
    KeyUse,
}

/// <summary>
/// The context of the expressions of an element of a stylesheet (https://www.w3.org/TR/xslt-10/#section-Expressions): the
/// namespace declarations in scope, the variables in scope and the functions of XSLT. The values of the variables and the
/// functions that depend on the state of the transformation use <see cref="XsltExecution.GetCurrent"/>.
/// </summary>
internal sealed class XsltExpressionContext : XsltContext
{
    private const string ExsltCommonNamespace = "http://exslt.org/common";

    private readonly XsltCompilation _compilation;
    private readonly XsltVariableScope? _variables;
    private readonly XsltExpressionKind _kind;

    public XsltExpressionContext(XsltCompilation compilation, XsltNamespaceScope namespaces, XsltVariableScope? variables, string staticBaseUri, XsltExpressionKind kind)
        : base(compilation.NameTable)
    {
        _compilation = compilation;
        NamespaceScope = namespaces;
        _variables = variables;
        StaticBaseUri = staticBaseUri;
        _kind = kind;
    }

    public XsltNamespaceScope NamespaceScope { get; }

    /// <summary>The base URI of the element of the stylesheet, which resolves the relative URIs of document().</summary>
    public string StaticBaseUri { get; }

    public override bool Whitespace => false;

    public override string? LookupNamespace(string prefix)
    {
        // The default namespace is not used for the names of an expression (https://www.w3.org/TR/xslt-10/#qname)
        if (prefix.Length == 0)
            return "";

        return NamespaceScope.Lookup(prefix) ?? throw new XsltException($"The namespace prefix '{prefix}' is not declared");
    }

    public override bool PreserveWhitespace(XPathNavigator node) => true;

    public override int CompareDocument(string baseUri, string nextbaseUri) => string.CompareOrdinal(baseUri, nextbaseUri);

    /// <summary>Expands a QName with the namespace declarations in scope, the default namespace excluded (https://www.w3.org/TR/xslt-10/#qname).</summary>
    public XsltQName ExpandQName(string qname)
    {
        qname = qname.Trim(' ', '\t', '\r', '\n');
        if (!XsltValues.IsQName(qname))
            throw new XsltException($"'{qname}' is not a QName");

        var colon = qname.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
            return new XsltQName("", qname);

        var prefix = qname[..colon];
        var ns = NamespaceScope.Lookup(prefix) ?? throw new XsltException($"The namespace prefix '{prefix}' is not declared");
        return new XsltQName(ns, qname[(colon + 1)..]);
    }

    public override IXsltContextVariable ResolveVariable(string prefix, string name)
    {
        if (_kind is XsltExpressionKind.PatternWithoutVariables or XsltExpressionKind.KeyUse)
            throw new XsltException($"The variable '${(prefix.Length > 0 ? prefix + ":" : "")}{name}' cannot be referenced here");

        var ns = prefix.Length == 0 ? "" : NamespaceScope.Lookup(prefix) ?? throw new XsltException($"The namespace prefix '{prefix}' is not declared");
        var qname = new XsltQName(ns, name);
        if (_variables?.Contains(qname) != true && !_compilation.HasGlobalVariable(qname))
            throw new XsltException($"The variable '${qname}' is not declared");

        return new XsltVariableReference(qname);
    }

    public override IXsltContextFunction ResolveFunction(string prefix, string name, XPathResultType[] argTypes)
    {
        if (prefix.Length == 0)
        {
            var kind = name switch
            {
                "current" => XsltFunctionKind.Current,
                "key" => XsltFunctionKind.Key,
                "document" => XsltFunctionKind.Document,
                "format-number" => XsltFunctionKind.FormatNumber,
                "generate-id" => XsltFunctionKind.GenerateId,
                "system-property" => XsltFunctionKind.SystemProperty,
                "element-available" => XsltFunctionKind.ElementAvailable,
                "function-available" => XsltFunctionKind.FunctionAvailable,
                "unparsed-entity-uri" => XsltFunctionKind.UnparsedEntityUri,
                _ => XsltFunctionKind.Unknown,
            };

            // current() and key() are not allowed in the patterns of xsl:key and xsl:template that would be recursive
            if (kind == XsltFunctionKind.Current && _kind is XsltExpressionKind.PatternWithoutVariables or XsltExpressionKind.Pattern)
                throw new XsltException("The current() function cannot be used in a pattern");

            return new XsltFunction(this, kind, name);
        }

        var ns = NamespaceScope.Lookup(prefix);
        if (ns == ExsltCommonNamespace)
        {
            if (name == "node-set")
                return new XsltFunction(this, XsltFunctionKind.NodeSet, name);

            if (name == "object-type")
                return new XsltFunction(this, XsltFunctionKind.ObjectType, name);
        }

        // An unknown function is an error when it is called (function-available() lets a stylesheet test it)
        return new XsltFunction(this, XsltFunctionKind.Unknown, prefix + ":" + name);
    }

    /// <summary>Whether a function is available (https://www.w3.org/TR/xslt-10/#function-function-available).</summary>
    public bool IsFunctionAvailable(string qname)
    {
        var name = ExpandQName(qname);
        if (name.NamespaceUri.Length == 0)
            return name.LocalName is "last" or "position" or "count" or "id" or "local-name" or "namespace-uri" or "name"
                or "string" or "concat" or "starts-with" or "contains" or "substring-before" or "substring-after" or "substring"
                or "string-length" or "normalize-space" or "translate" or "boolean" or "not" or "true" or "false" or "lang"
                or "number" or "sum" or "floor" or "ceiling" or "round"
                or "current" or "key" or "document" or "format-number" or "generate-id" or "system-property"
                or "element-available" or "function-available" or "unparsed-entity-uri";

        return name.NamespaceUri == ExsltCommonNamespace && name.LocalName is "node-set" or "object-type";
    }
}

/// <summary>A reference to a variable or a parameter, whose value is read from the running transformation.</summary>
internal sealed class XsltVariableReference(XsltQName name) : IXsltContextVariable
{
    public bool IsLocal => false;

    public bool IsParam => false;

    public XPathResultType VariableType => XPathResultType.Any;

    // System.Xml.XPath evaluates the variables of the arguments of the functions when the context is set, to get their type:
    // there is no value when the expression is compiled
    public object Evaluate(XsltContext xsltContext) => XsltExecution.TryGetCurrent() is { } execution ? XsltValues.ToXPath(execution.GetVariableValue(name)) : "";
}

/// <summary>The functions added by XSLT to the XPath core functions (https://www.w3.org/TR/xslt-10/#add-func).</summary>
internal enum XsltFunctionKind
{
    Unknown,
    Current,
    Key,
    Document,
    FormatNumber,
    GenerateId,
    SystemProperty,
    ElementAvailable,
    FunctionAvailable,
    UnparsedEntityUri,
    NodeSet,
    ObjectType,
}

/// <summary>A function of XSLT, evaluated by the running transformation.</summary>
internal sealed class XsltFunction(XsltExpressionContext context, XsltFunctionKind kind, string name) : IXsltContextFunction
{
    public XsltExpressionContext Context { get; } = context;

    public XsltFunctionKind Kind { get; } = kind;

    public string Name { get; } = name;

    public int Minargs => Kind switch
    {
        XsltFunctionKind.Current => 0,
        XsltFunctionKind.Key => 2,
        XsltFunctionKind.Document => 1,
        XsltFunctionKind.FormatNumber => 2,
        XsltFunctionKind.GenerateId => 0,
        XsltFunctionKind.SystemProperty or XsltFunctionKind.ElementAvailable or XsltFunctionKind.FunctionAvailable or XsltFunctionKind.UnparsedEntityUri or XsltFunctionKind.NodeSet or XsltFunctionKind.ObjectType => 1,
        _ => 0,
    };

    public int Maxargs => Kind switch
    {
        XsltFunctionKind.Current => 0,
        XsltFunctionKind.Key => 2,
        XsltFunctionKind.Document => 2,
        XsltFunctionKind.FormatNumber => 3,
        XsltFunctionKind.GenerateId => 1,
        XsltFunctionKind.SystemProperty or XsltFunctionKind.ElementAvailable or XsltFunctionKind.FunctionAvailable or XsltFunctionKind.UnparsedEntityUri or XsltFunctionKind.NodeSet or XsltFunctionKind.ObjectType => 1,
        _ => int.MaxValue,
    };

    public XPathResultType ReturnType => Kind switch
    {
        XsltFunctionKind.Current or XsltFunctionKind.Key or XsltFunctionKind.Document or XsltFunctionKind.NodeSet => XPathResultType.NodeSet,
        XsltFunctionKind.ElementAvailable or XsltFunctionKind.FunctionAvailable => XPathResultType.Boolean,
        XsltFunctionKind.FormatNumber or XsltFunctionKind.GenerateId or XsltFunctionKind.UnparsedEntityUri or XsltFunctionKind.ObjectType => XPathResultType.String,
        _ => XPathResultType.Any,
    };

    public XPathResultType[] ArgTypes => [];

    public object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext)
    {
        var values = new object[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            values[i] = XsltValues.FromXPath(args[i]);
        }

        return XsltValues.ToXPath(XsltExecution.GetCurrent().CallFunction(this, values, docContext));
    }
}

/// <summary>A compiled XPath expression of a stylesheet.</summary>
internal sealed class XsltExpression
{
    private readonly ClrXPathExpression _expression;

    private XsltExpression(string text, ClrXPathExpression expression, XsltExpressionContext context)
    {
        Text = text;
        _expression = expression;
        Context = context;
    }

    public string Text { get; }

    public XsltExpressionContext Context { get; }

    /// <summary>The compiled expression, used by the patterns.</summary>
    public ClrXPathExpression Compiled => _expression;

    public static XsltExpression Compile(string text, XsltExpressionContext context)
    {
        try
        {
            var expression = ClrXPathExpression.Compile(text);
            expression.SetContext(context);
            return new XsltExpression(text, expression, context);
        }
        catch (XsltException ex)
        {
            throw new XsltException($"Invalid expression '{text}': {ex.Message}", ex);
        }
        catch (XPathException ex)
        {
            throw new XsltException($"Invalid expression '{text}': {ex.Message}", ex);
        }
        catch (ArgumentException ex)
        {
            throw new XsltException($"Invalid expression '{text}': {ex.Message}", ex);
        }
    }

    /// <summary>Evaluates the expression with a context node, a context position and a context size.</summary>
    public object Evaluate(XPathNavigator node, int position = 1, int size = 1)
    {
        XsltExecution.GetCurrent().CheckLimits();
        try
        {
            return XsltValues.FromXPath(node.Evaluate(_expression, new XsltContextIterator(node, position, size)));
        }
        catch (XPathException ex)
        {
            // System.Xml.XPath wraps the errors of the functions
            for (Exception? inner = ex.InnerException; inner is not null; inner = inner.InnerException)
            {
                if (inner is XsltException xsltException)
                    throw new XsltException(xsltException.Message, xsltException);
            }

            throw new XsltException($"Error while evaluating '{Text}': {ex.Message}", ex);
        }
    }

    /// <summary>Evaluates an expression whose result must be a node-set (https://www.w3.org/TR/xslt-10/#section-Applying-Template-Rules).</summary>
    public XsltNodeSet EvaluateNodeSet(XPathNavigator node, int position = 1, int size = 1)
    {
        return Evaluate(node, position, size) as XsltNodeSet ?? throw new XsltException($"The expression '{Text}' does not evaluate to a node-set");
    }
}
