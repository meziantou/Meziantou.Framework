using System.Xml;
using System.Xml.XPath;
using System.Xml.Xsl;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.XPath;

/// <summary>Thrown by the XSLT context to report a DOM error; System.Xml.XPath may wrap it.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Internal control-flow exception")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Internal control-flow exception")]
internal sealed class XPathDomException(DomException exception) : Exception(exception.Message, exception)
{
    public DomException DomException { get; } = exception;
}

/// <summary>
/// The context of the evaluation: namespace prefixes are resolved by the XPathNSResolver
/// (https://dom.spec.whatwg.org/#callbackdef-xpathnsresolver), and the functions used by the HTML rewriting are provided.
/// </summary>
internal sealed class DomXsltContext : XsltContext
{
    private const string FunctionNamespace = "urn:meziantou-html:xpath-functions";

    private readonly IXPathNSResolver? _resolver;

    public DomXsltContext(IXPathNSResolver? resolver)
        : base(new NameTable())
    {
        _resolver = resolver;
    }

    public override bool Whitespace => true;

    public override string? LookupNamespace(string prefix)
    {
        if (prefix.Length == 0)
            return "";

        if (prefix == XPathExpressionRewriter.HtmlPrefix)
            return Namespaces.Html;

        if (prefix == XPathExpressionRewriter.AttributeFunctionPrefix)
            return FunctionNamespace;

        if (prefix == "xml")
            return Namespaces.Xml;

        string? ns;
        try
        {
            ns = _resolver?.LookupNamespaceUri(prefix);
        }
#pragma warning disable CA1031 // The exceptions of the resolver are reported, and the prefix is not resolved
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Scripting.ScriptErrors.Report(ex);
            ns = null;
        }

        if (ns is null)
            throw new XPathDomException(DomException.Namespace($"The namespace prefix '{prefix}' is not declared"));

        return ns;
    }

    public override bool PreserveWhitespace(XPathNavigator node) => true;

    public override int CompareDocument(string baseUri, string nextbaseUri) => 0;

    public override IXsltContextFunction ResolveFunction(string prefix, string name, XPathResultType[] argTypes)
    {
        if (prefix == XPathExpressionRewriter.AttributeFunctionPrefix && name == XPathExpressionRewriter.AttributeFunctionName)
            return AttributeNameMatchesFunction.Instance;

        throw new XPathDomException(DomException.Syntax($"The function '{(prefix.Length > 0 ? prefix + ":" : "")}{name}' is not supported"));
    }

    public override IXsltContextVariable ResolveVariable(string prefix, string name) =>
        throw new XPathDomException(DomException.Namespace($"The variable '${name}' is not declared"));

    /// <summary>
    /// Matches an unprefixed attribute name test against the context attribute: ASCII case-insensitively for the
    /// attributes of HTML elements in HTML documents, exactly otherwise; only attributes in no namespace match.
    /// </summary>
    private sealed class AttributeNameMatchesFunction : IXsltContextFunction
    {
        public static AttributeNameMatchesFunction Instance { get; } = new();

        public int Minargs => 1;

        public int Maxargs => 1;

        public XPathResultType ReturnType => XPathResultType.Boolean;

        public XPathResultType[] ArgTypes => [XPathResultType.String];

        public object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext)
        {
            if (docContext.NodeType != XPathNodeType.Attribute || docContext.NamespaceURI.Length > 0)
                return false;

            var name = args[0] is string value ? value : args[0]?.ToString() ?? "";
            var attribute = (docContext as DomXPathNavigator)?.CurrentNode as Attr;
            if (attribute?.OwnerElement is { } owner && owner.IsHtmlNamespace && owner.NodeDocument.IsHtmlDocument)
                return InfraAscii.ToAsciiLowercase(name) == docContext.LocalName;

            return name == docContext.LocalName;
        }
    }
}
