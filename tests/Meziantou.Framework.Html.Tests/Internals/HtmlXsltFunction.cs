using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Xml.XPath;
using System.Xml.Xsl;

namespace Meziantou.Framework.Html.Tests
{
    internal abstract class HtmlXsltFunction : IXsltContextFunction
    {
        protected HtmlXsltFunction(HtmlXsltContext context, string prefix, string name, XPathResultType[] argTypes)
        {
            Context = context;
            Prefix = prefix;
            Name = name;
            ArgTypes = argTypes;
        }

        public HtmlXsltContext Context { get; }
        public string Prefix { get; }
        public string Name { get; }
        public XPathResultType[] ArgTypes { get; }

        public static object CreateXsltArgument(HtmlXsltContext context) => new XsltArgument(context);

        public abstract object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext);

        public virtual int Maxargs => Minargs;

        public virtual int Minargs => 1;

        public virtual XPathResultType ReturnType => XPathResultType.String;

        public static T ConvertTo<T>(object argument, T defaultValue)
        {
            if (argument == null)
                return defaultValue;

            if (argument is T convertedValue)
                return convertedValue;

            if (argument is XPathNodeIterator it)
            {
                if (!it.MoveNext())
                    return defaultValue;

                if (it.Current is HtmlNodeNavigator n && n.CurrentNode is T result)
                    return result;

                return defaultValue;
            }

            if (argument is IEnumerable enumerable && (argument is not string))
            {
                foreach (var arg in enumerable)
                {
                    if (arg is T result)
                        return result;

                    break;
                }
            }

            return defaultValue;
        }

        public static string ConvertToString(object argument, bool outer, string separator)
        {
            if (argument == null)
                return null;

            if (argument is string s)
                return s;

            if (argument is XPathNodeIterator it)
            {
                if (!it.MoveNext())
                    return null;

                var sb = new StringBuilder();
                do
                {
                    if (it.Current is HtmlNodeNavigator n && n.CurrentNode != null)
                    {
                        if (sb.Length > 0 && separator != null)
                        {
                            sb.Append(separator);
                        }

                        if (n.CurrentNode is HtmlElement element)
                        {
                            var clone = (HtmlElement)element.Clone();
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

            if (argument is IEnumerable enumerable)
            {
                StringBuilder sb = null;
                foreach (var arg in enumerable)
                {
                    if (sb == null)
                    {
                        sb = new StringBuilder();
                    }

                    if (sb.Length > 0 && separator != null)
                    {
                        sb.Append(separator);
                    }

                    var s2 = ConvertToString(arg, outer, separator);
                    if (s2 != null)
                    {
                        sb.Append(s2);
                    }
                }

                return sb?.ToString();
            }

            return string.Format(CultureInfo.InvariantCulture, "{0}", argument);
        }

        internal static bool IsNull(object arg)
        {
            if (arg == null || Convert.IsDBNull(arg))
                return true;

            if (arg is string)
                return false;

            if (arg is XPathNodeIterator it)
            {
                it = it.Clone();
                if (!it.MoveNext())
                    return true;

                object current = it.Current;
                return IsNull(current);
            }

            if (arg is IEnumerable e)
            {
                foreach (var _ in e)
                    return false;

                return true;
            }
            return false;
        }

        private sealed class XsltArgument
        {
            public XsltArgument(HtmlXsltContext context)
            {
                Context = context;
            }

            public HtmlXsltContext Context { get; }

            public string Lowercase(object obj)
            {
                return (string)new Lowercase(Context, "Lowercase").Invoke(xsltContext: null, new[] { obj }, docContext: null);
            }

            // add methods as needed
        }

        [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "They may be needed later")]
        [SuppressMessage("Usage", "CA1801:Review unused parameters", Justification = "They may be needed later")]
        public static IXsltContextFunction GetBuiltIn(HtmlXsltContext context, string prefix, string name, XPathResultType[] argTypes)
        {
            if (string.Equals(name, "lowercase", StringComparison.Ordinal))
                return new Lowercase(context, name);

            return null;
        }

        public sealed class Lowercase : HtmlXsltFunction
        {
            public Lowercase(HtmlXsltContext context, string name)
                : base(context, prefix: null, name, argTypes: null)
            {
            }

            [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "By design")]
            public override object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext)
            {
                return ConvertToString(args, outer: false, separator: null)?.ToLowerInvariant();
            }
        }
    }
}
