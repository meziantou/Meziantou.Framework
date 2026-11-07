using System.Xml.XPath;
using Meziantou.Framework.WebPlatform.XPath;

namespace Meziantou.Framework.WebPlatform.Dom;

public abstract partial class Node : IXPathNavigable, IXPathNSResolver
{
    /// <summary>
    /// Creates a read-only <see cref="XPathNavigator"/> positioned on this node, to use the .NET XPath and XSLT APIs over
    /// the DOM (the XPath 1.0 data model: adjacent text nodes are merged, doctypes are ignored).
    /// </summary>
    [NotInIdl(".NET integration")]
    public XPathNavigator CreateNavigator() => new DomXPathNavigator(this);
}
