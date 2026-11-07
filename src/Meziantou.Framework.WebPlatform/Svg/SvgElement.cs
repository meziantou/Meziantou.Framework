using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// https://svgwg.org/svg2-draft/types.html#InterfaceSVGElement
public partial class SvgElement : Element
{
    internal SvgElement(Document document, string localName, string? prefix)
        : base(document, localName, Namespaces.Svg, prefix)
    {
    }

    private partial int ComputeTabIndex() => Reflection.GetTabIndex(this);
}
