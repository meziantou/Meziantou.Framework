using System.Xml;
using System.Xml.XPath;

namespace Meziantou.Framework.Html;
#if HTML_PUBLIC
public
#else
internal
#endif
partial class HtmlNode : IXPathNavigable
{
    public HtmlNode SelectSingleNode(string xpath)
    {
        return SelectSingleNode(xpath, nsmgr: null);
    }

    public HtmlNode SelectSingleNode(string xpath, XmlNamespaceManager nsmgr)
    {
        return SelectSingleNode(xpath, nsmgr, HtmlNodeNavigatorOptions.None);
    }

    public HtmlNode SelectSingleNode(string xpath, HtmlNodeNavigatorOptions options)
    {
        return SelectSingleNode(xpath, nsmgr: null, options);
    }

    public HtmlNode SelectSingleNode(string xpath, XmlNamespaceManager nsmgr, HtmlNodeNavigatorOptions options)
    {
        return SelectNodes(xpath, nsmgr, options).FirstOrDefault();
    }

    public IEnumerable<HtmlNode> SelectNodes(string xpath)
    {
        return SelectNodes(xpath, nsmgr: null);
    }

    public IEnumerable<HtmlNode> SelectNodes(string xpath, XmlNamespaceManager nsmgr)
    {
        return SelectNodes(xpath, nsmgr, HtmlNodeNavigatorOptions.None);
    }

    public IEnumerable<HtmlNode> SelectNodes(string xpath, HtmlNodeNavigatorOptions options)
    {
        return SelectNodes(xpath, namespaceManager: null, options);
    }

    public virtual IXPathNavigable CreateNavigable(HtmlNodeNavigatorOptions options)
    {
        return new Navigable(OwnerDocument, this, options);
    }

    public XPathNavigator CreateNavigator()
    {
        return CreateNavigator(HtmlNodeNavigatorOptions.None);
    }

    public virtual XPathNavigator CreateNavigator(HtmlNodeNavigatorOptions options)
    {
        return new HtmlNodeNavigator(OwnerDocument, this, options);
    }

    public virtual IEnumerable<HtmlNode> SelectNodes(string xpath, XmlNamespaceManager namespaceManager, HtmlNodeNavigatorOptions options)
    {
        if (xpath is null)
            throw new ArgumentNullException(nameof(xpath));

        if ((options & HtmlNodeNavigatorOptions.Dynamic) == HtmlNodeNavigatorOptions.Dynamic)
            return DoSelectNodes(xpath, namespaceManager, options);

        var list = DoSelectNodes(xpath, namespaceManager, options).ToList();

        if ((options & HtmlNodeNavigatorOptions.DepthFirst) == HtmlNodeNavigatorOptions.DepthFirst)
        {
            list.Sort(new HtmlNodeDepthComparer { Direction = ListSortDirection.Descending });
        }
        return list;
    }

    protected virtual IEnumerable<HtmlNode> DoSelectNodes(string xpath, XmlNamespaceManager nsmgr, HtmlNodeNavigatorOptions options)
    {
        var navigator = CreateNavigator(options);
        if (navigator is null)
            yield break;

        var expr = navigator.Compile(xpath);
        if (nsmgr is not null)
        {
            expr.SetContext(nsmgr);
        }

        var eval = navigator.Evaluate(expr);
        if (eval is XPathNodeIterator it)
        {
            while (it.MoveNext())
            {
                var n = it.Current as HtmlNodeNavigator;
                if ((n?.CurrentNode) is not null)
                    yield return n.CurrentNode;
            }
        }
        else
        {
            yield return new HtmlXPathResult(OwnerDocument, eval);
        }
    }

    private sealed class Navigable : IXPathNavigable
    {
        private readonly HtmlDocument _ownerDocument;
        private readonly HtmlNode _node;
        private readonly HtmlNodeNavigatorOptions _options;

        public Navigable(HtmlDocument ownerDocument, HtmlNode node, HtmlNodeNavigatorOptions options)
        {
            _ownerDocument = ownerDocument;
            _node = node;
            _options = options;
        }

        public XPathNavigator CreateNavigator()
        {
            return new HtmlNodeNavigator(_ownerDocument, _node, _options);
        }
    }
}
