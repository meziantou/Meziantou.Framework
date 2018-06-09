using System;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Xsl;

namespace Meziantou.Framework.Html.Tests
{
    public class HtmlXsltContext : XsltContext
    {
        public HtmlXsltContext(IXmlNamespaceResolver resolver)
            : base(new NameTable())
        {
            Resolver = resolver;
        }

        public IXmlNamespaceResolver Resolver { get; private set; }

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
            IXsltContextFunction fn = HtmlXsltFunction.GetBuiltIn(this, prefix, name, ArgTypes);
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
            if (ns == null)
            {
                if (ns == null)
                {
                    if (Resolver != null)
                    {
                        ns = Resolver.LookupNamespace(prefix);
                    }
                }
            }
            return ns ?? string.Empty;
        }

        public override string LookupPrefix(string uri)
        {
            var prefix = base.LookupPrefix(uri);
            if (prefix == null)
            {
                if (prefix == null)
                {
                    if (Resolver != null)
                    {
                        prefix = Resolver.LookupPrefix(prefix);
                    }
                }
            }
            return prefix ?? string.Empty;
        }

        public override bool Whitespace
        {
            get { return true; }
        }
    }

    public abstract class HtmlXsltFunction : IXsltContextFunction
    {
        protected HtmlXsltFunction(HtmlXsltContext context, string prefix, string name, XPathResultType[] argTypes)
        {
            Context = context;
            Prefix = prefix;
            Name = name;
            ArgTypes = argTypes;
        }

        public HtmlXsltContext Context { get; private set; }
        public string Prefix { get; private set; }
        public string Name { get; private set; }
        public XPathResultType[] ArgTypes { get; private set; }

        public static object CreateXsltArgument(HtmlXsltContext context)
        {
            return new XsltArgument(context);
        }

        public abstract object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext);

        public virtual int Maxargs
        {
            get { return Minargs; }
        }

        public virtual int Minargs
        {
            get { return 1; }
        }

        public virtual XPathResultType ReturnType
        {
            get { return XPathResultType.String; }
        }

        public static T ConvertTo<T>(object argument, CultureInfo ci, T defaultValue)
        {
            if (argument == null)
                return defaultValue;

            if (argument is T)
                return (T)argument;

            var it = argument as XPathNodeIterator;
            if (it != null)
            {
                if (!it.MoveNext())
                    return defaultValue;

                var n = it.Current as HtmlNodeNavigator;
                if (n != null && n.CurrentNode != null)
                {
                    if (n.CurrentNode is T)
                        return (T)(object)n.CurrentNode;
                }
                return defaultValue;
            }

            var enumerable = argument as IEnumerable;
            if (enumerable != null && (!(argument is string)))
            {
                foreach (object arg in enumerable)
                {
                    if (arg is T)
                        return (T)arg;

                    break;
                }
            }

            return defaultValue;
        }

        public static string ConvertToString(object argument, bool outer, string separator)
        {
            if (argument == null)
                return null;

            string s = argument as string;
            if (s != null)
                return s;

            var it = argument as XPathNodeIterator;
            if (it != null)
            {
                if (!it.MoveNext())
                    return null;

                var sb = new StringBuilder();
                do
                {
                    var n = it.Current as HtmlNodeNavigator;
                    if (n != null && n.CurrentNode != null)
                    {
                        if (sb.Length > 0 && separator != null)
                        {
                            sb.Append(separator);
                        }

                        var element = n.CurrentNode as HtmlElement;
                        if (element != null)
                        {
                            var clone = (HtmlElement)element.Clone();
                            //HtmlExtensions.RemoveNamespaces(clone); // REVIEW
                            sb.Append(outer ? clone.OuterHtml : clone.InnerHtml);
                        }
                        else
                        {
                            sb.Append(n.CurrentNode.Value);
                        }
                    }
                }
                while (it.MoveNext());
                return sb.ToString();
            }

            var enumerable = argument as IEnumerable;
            if (enumerable != null)
            {
                StringBuilder sb = null;
                foreach (object arg in enumerable)
                {
                    if (sb == null)
                    {
                        sb = new StringBuilder();
                    }

                    if (sb.Length > 0 && separator != null)
                    {
                        sb.Append(separator);
                    }

                    string s2 = ConvertToString(arg, outer, separator);
                    if (s2 != null)
                    {
                        sb.Append(s2);
                    }
                }
                return sb != null ? sb.ToString() : null;
            }

            return string.Format(CultureInfo.InvariantCulture, "{0}", argument);
        }

        internal static bool IsNull(object arg)
        {
            if (arg == null || Convert.IsDBNull(arg))
                return true;

            if (arg is string)
                return false;

            var it = arg as XPathNodeIterator;
            if (it != null)
            {
                it = it.Clone();
                if (!it.MoveNext())
                    return true;

                object current = it.Current;
                return IsNull(current);
            }

            IEnumerable e = arg as IEnumerable;
            if (e != null)
            {
                foreach (object o in e)
                {
                    return false;
                }
                return true;
            }
            return false;
        }

        private class XsltArgument
        {
            public XsltArgument(HtmlXsltContext context)
            {
                Context = context;
            }

            public HtmlXsltContext Context { get; private set; }

            public string Lowercase(object obj)
            {
                return (string)new Lowercase(Context, "Lowercase").Invoke(null, new[] { obj }, null);
            }

            // add methods as needed
        }

        public static IXsltContextFunction GetBuiltIn(HtmlXsltContext context, string prefix, string name, XPathResultType[] argTypes)
        {
            if (name == "lowercase")
                return new Lowercase(context, name);

            return null;
        }

        public class NamespacePrefix : HtmlXsltFunction
        {
            public NamespacePrefix(HtmlXsltContext context, string name)
                : base(context, null, name, null)
            {
            }

            public override object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext)
            {
                var nav = docContext as HtmlNodeNavigator;
                if (nav == null || nav.CurrentNode == null)
                    return string.Empty;

                return nav.CurrentNode.Prefix;
            }
        }

        public class Lowercase : HtmlXsltFunction
        {
            public Lowercase(HtmlXsltContext context, string name)
                : base(context, null, name, null)
            {
            }

            public override object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext)
            {
                string s = ConvertToString(args, false, null);
                return s != null ? s.ToLower() : null;
            }
        }
    }
}