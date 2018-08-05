using System;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Xsl;

namespace Meziantou.Framework.Html.Tests
{
    internal class HtmlXsltContext : XsltContext
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

        protected virtual IXsltContextFunction CreateHtmlXsltFunction(string prefix, string name, XPathResultType[] ArgTypes)
        {
            var fn = HtmlXsltFunction.GetBuiltIn(this, prefix, name, ArgTypes);
            if (fn == null)
                throw new Exception("XPATH function '" + name + "' is unknown.");

            return fn;
        }

        public override IXsltContextFunction ResolveFunction(string prefix, string name, XPathResultType[] ArgTypes)
        {
            return CreateHtmlXsltFunction(prefix, name, ArgTypes);
        }

        public override IXsltContextVariable ResolveVariable(string prefix, string name)
        {
            throw new NotSupportedException();
        }

        public override string LookupNamespace(string prefix)
        {
            var ns = base.LookupNamespace(prefix);
            if (ns == null && Resolver != null)
            {
                ns = Resolver.LookupNamespace(prefix);
            }
            return ns ?? string.Empty;
        }

        public override string LookupPrefix(string uri)
        {
            var prefix = base.LookupPrefix(uri);
            if (prefix == null && Resolver != null)
            {
                prefix = Resolver.LookupPrefix(prefix);
            }

            return prefix ?? string.Empty;
        }

        public override bool Whitespace => true;
    }
}