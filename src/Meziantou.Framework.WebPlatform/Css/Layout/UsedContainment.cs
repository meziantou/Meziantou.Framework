using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The containment that applies to a box (https://drafts.csswg.org/css-contain-2/#containment-types): the containment types of
/// <c>contain</c>, of the size container types (size and style containment, https://drafts.csswg.org/css-conditional-5/#container-type)
/// and of <c>content-visibility</c> (see <see cref="LayoutStyle.HasLayoutContainment"/>), except for the boxes they have no effect on.
/// </summary>
/// <remarks>
/// <para>
/// Size containment, and so <c>content-visibility</c> ("applies to: elements for which size containment can apply"), has no
/// effect when the element does not generate a principal box, when its inner display type is <c>table</c>, or when its
/// principal box is an internal table box, an internal ruby box or a non-atomic inline-level box (a table caption is not an
/// internal table box: the containments apply to it). Layout and paint
/// containment have no effect on the same boxes, except the tables and the table cells
/// (https://drafts.csswg.org/css-contain-2/#containment-layout). Style containment only needs a principal box.
/// </para>
/// <para>
/// The effects: size containment is applied by the intrinsic sizing (<see cref="IntrinsicSizing.HasSizeContainment"/>) and
/// <see cref="FormattingContext.Layout"/>; layout containment establishes an independent formatting context
/// (<see cref="BlockFormattingContext.EstablishesNewFormattingContext"/>), a containing block for the absolutely and fixed
/// positioned descendants and a stacking context (<see cref="LayoutStyle.EstablishesFixedPositionContainingBlock"/>), and
/// suppresses the baselines of the box (<see cref="LayoutContext.LayoutBox"/>); paint containment clips the contents of the box
/// to its overflow clip edge (<see cref="BoxFragment"/>); style containment scopes the counters (<see cref="CssCounters"/>)
/// and the quotes (<see cref="BoxTreeBuilder"/>).
/// </para>
/// </remarks>
internal static class UsedContainment
{
    /// <summary>
    /// Returns whether size containment (and so <c>content-visibility</c>) can apply to a box
    /// (https://drafts.csswg.org/css-contain-2/#containment-size): not to the inline boxes, the internal table and ruby
    /// boxes, the ruby containers, the tables (inner display type <c>table</c>) and the viewport.
    /// </summary>
    public static bool CanApplySizeContainment(ContainerBox box)
        => !box.IsInlineBox && box.Kind is not (ContainerBoxKind.Viewport or ContainerBoxKind.TableWrapper or ContainerBoxKind.TableGrid)
            && box.Display.Internal is DisplayInternal.None or DisplayInternal.TableCaption && box.Display.Inside != DisplayInside.Ruby;

    /// <summary>
    /// Returns whether size containment can apply to the principal box of an element from its computed style (see
    /// <see cref="CanApplySizeContainment(ContainerBox)"/>), without building the box tree: the element generates a box that
    /// is not a non-atomic inline box (a replaced element is atomic), an internal table or ruby box, a ruby container or a table.
    /// </summary>
    public static bool CanApplySizeContainment(Element element, ComputedStyle style)
    {
        var display = style.Layout.Display;
        if (display.IsNone || display.IsContents || display.Internal is not (DisplayInternal.None or DisplayInternal.TableCaption) || display.Inside is DisplayInside.Table or DisplayInside.Ruby)
            return false;

        return !display.IsInlineBox || BoxGeneration.IsReplaced(element, style);
    }

    /// <summary>
    /// Returns whether a box has layout containment (https://drafts.csswg.org/css-contain-2/#containment-layout): it has no
    /// effect on the inline boxes, the internal table boxes other than the table cells and the internal ruby boxes.
    /// </summary>
    public static bool HasLayoutContainment(ContainerBox box) => box.Style.Layout.HasLayoutContainment && CanApplyLayoutOrPaintContainment(box);

    /// <summary>Returns whether a box has paint containment (https://drafts.csswg.org/css-contain-2/#containment-paint), with the same exceptions as layout containment.</summary>
    public static bool HasPaintContainment(ContainerBox box) => box.Style.Layout.HasPaintContainment && CanApplyLayoutOrPaintContainment(box);

    /// <summary>
    /// Returns whether a box establishes a containing block for its fixed positioned descendants (and a stacking context) from
    /// its style (see <see cref="LayoutStyle.EstablishesFixedPositionContainingBlock"/>): the layout and paint containment only
    /// count for the boxes they apply to (https://drafts.csswg.org/css-contain-2/#containment-layout: not the rows and the row
    /// groups of a table, for example).
    /// </summary>
    public static bool EstablishesFixedPositionContainingBlock(ContainerBox box)
    {
        var layout = box.Style.Layout;
        return layout.EstablishesFixedPositionContainingBlock && (!layout.EstablishesFixedPositionContainingBlockByContainmentOnly || CanApplyLayoutOrPaintContainment(box));
    }

    private static bool CanApplyLayoutOrPaintContainment(ContainerBox box)
        => !box.IsInlineBox && (!box.IsAnonymous || box.IsFieldsetContent) && box.Kind != ContainerBoxKind.Viewport
            && box.Display.Internal is DisplayInternal.None or DisplayInternal.TableCell or DisplayInternal.TableCaption && box.Display.Inside != DisplayInside.Ruby;
}
