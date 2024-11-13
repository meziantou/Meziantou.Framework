using System.Xml;
using System.Xml.XPath;
using System.Xml.Xsl;

namespace Meziantou.Framework.Html.Tests;

internal sealed class HtmlXsltContext : XsltContext
{
    public HtmlXsltContext(IXmlNamespaceResolver resolver)
        : base(new NameTable())
    {
        Resolver = resolver;
    }

    public IXmlNamespaceResolver Resolver { get; }

    public override int CompareDocument(string baseUri, string nextbaseUri)
    {
        throw new NotSupportedException();
    }

    public override bool PreserveWhitespace(XPathNavigator node)
    {
        throw new NotSupportedException();
    }

    private IXsltContextFunction CreateHtmlXsltFunction(string prefix, string name, XPathResultType[] argTypes)
    {
        var fn = HtmlXsltFunction.GetBuiltIn(this, prefix, name, argTypes);
        if (fn is null)
            throw new ArgumentException("XPATH function '" + name + "' is unknown.", nameof(name));

        return fn;
    }

    public override IXsltContextFunction ResolveFunction(string prefix, string name, XPathResultType[] argTypes)
    {
        return CreateHtmlXsltFunction(prefix, name, argTypes);
    }

    public override IXsltContextVariable ResolveVariable(string prefix, string name)
    {
        throw new NotSupportedException();
    }

    public override string LookupNamespace(string prefix)
    {
        var ns = base.LookupNamespace(prefix);
        if (ns is null && Resolver is not null)
        {
            ns = Resolver.LookupNamespace(prefix);
        }

        return ns ?? string.Empty;
    }

    public override string LookupPrefix(string uri)
    {
        var prefix = base.LookupPrefix(uri);
        if (prefix is null && Resolver is not null)
        {
            prefix = Resolver.LookupPrefix(prefix);
        }

        return prefix ?? string.Empty;
    }

    public override bool Whitespace => true;
}
