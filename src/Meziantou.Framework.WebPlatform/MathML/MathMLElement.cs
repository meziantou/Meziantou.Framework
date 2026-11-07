using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// https://w3c.github.io/mathml-core/#dom-mathmlelement
public partial class MathMLElement : Element
{
    internal MathMLElement(Document document, string localName, string? prefix)
        : base(document, localName, Namespaces.MathML, prefix)
    {
    }

    /// <summary>For annotation-xml elements: whether the start tag had an encoding attribute making it an HTML integration point.</summary>
    internal bool IsHtmlIntegrationPoint { get; set; }

    private partial int ComputeTabIndex() => Reflection.GetTabIndex(this);
}
