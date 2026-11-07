using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The boxes of the CSS Layout API (https://drafts.css-houdini.org/css-layout-api-1/#layout-api-containers)
internal sealed partial class BoxTreeBuilder
{
    /// <summary>
    /// The fixup of a layout API container: a box whose display is <c>layout(name)</c> (a flow-root block container for the box
    /// tree, see <see cref="Display.Parse"/>) and whose layout is registered in the layout worklet of the document. As for the
    /// flex containers, each in-flow child is a child of the layout (a <c>LayoutChild</c>), and each sequence of text runs and
    /// inline-level boxes is wrapped in an anonymous block box (https://drafts.css-houdini.org/css-layout-api-1/#layoutchild:
    /// "A text node which has undergone blockification", or the root inline box of the children that are not blockified
    /// when the <c>childDisplay</c> of the layout is <c>normal</c>). Returns false for the other boxes, and for a box whose
    /// layout is not registered (or invalid): it is a block container.
    /// </summary>
    private bool TryFixupLayoutApiContainer(ContainerBox box)
    {
        if (!box.Display.IsLayoutFunction || box.Kind != ContainerBoxKind.Normal || box.Element?.NodeDocument is not { LayoutWorkletOrNull: { } worklet } document)
            return false;

        if (CssDisplay.GetLayoutFunctionName(box.Style.GetComputedValue("display")) is not { } name || worklet.GetDocumentLayoutDefinition(name) is not { } definition)
            return false;

        if (document.EnvironmentOptions is not { EffectiveFeatures.LayoutWorkletEnabled: true })
            return false;

        box.LayoutApi = new LayoutApiContainer(name, definition);
        box.ChildrenAreInline = false;
        FixupFlexOrGridContainer(box);
        return true;
    }
}
