using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGElement</summary>
public partial class SvgElement : Element
{
    internal SvgElement(Document document, string localName, string? prefix)
        : base(document, localName, Namespaces.Svg, prefix)
    {
    }

    private partial int ComputeTabIndex() => Reflection.GetTabIndex(this);
}
