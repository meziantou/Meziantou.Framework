using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Layout;

namespace Meziantou.Framework.WebPlatform.Dom;

// The resizer of the elements whose resize property is not none (https://drafts.csswg.org/css-ui-4/#resize): as in the browsers,
// the press of the primary button of a pointer that hovers (the mouse, a pen) on the resizer, a square at the bottom corner of the
// scroll container on the side of its vertical scrollbar (bottom-left in a right-to-left horizontal box), inside its borders, is
// handled by the resizer, as the press of a scrollbar (see UserInput.Scrollbars.cs: the press has no click, and the moves fire no
// event until the release). A move resizes the box by the move of the pointer in the coordinates of the box (its transforms are
// taken into account): "the user agent sets the width and height properties to px unit length values" in the style attribute,
// for the sizes that change. The resizer is not painted.
public sealed partial class UserInput
{
    /// <summary>The size of the resizer when the box has no classic scrollbar, in CSS pixels (as in the browsers).</summary>
    private const double DefaultResizerSize = 15;

    /// <summary>The press of the primary button of a pointer on the resizer of an element.</summary>
    /// <param name="Element">The element that is resized.</param>
    /// <param name="Horizontal">Whether the width can be adjusted.</param>
    /// <param name="Vertical">Whether the height can be adjusted.</param>
    /// <param name="Width">The used value of <c>width</c> when the button was pressed, in CSS pixels.</param>
    /// <param name="Height">The used value of <c>height</c> when the button was pressed, in CSS pixels.</param>
    /// <param name="X">The position of the pointer when the button was pressed (in the viewport of the top-level browsing context).</param>
    /// <param name="Y">The position of the pointer when the button was pressed.</param>
    /// <param name="IsLeft">Whether the resizer is on the left side of the box: a move to the left makes it wider.</param>
    internal sealed record ResizerPress(Element Element, bool Horizontal, bool Vertical, double Width, double Height, double X, double Y, bool IsLeft);

    /// <summary>Returns the press of a pointer on the resizer of the element at its position (or of one of its ancestors), or null.</summary>
    private ResizerPress? CreateResizerPress(PointerInput pointer)
    {
        if (HitTest(pointer.X, pointer.Y, out _) is not { } hit)
            return null;

        var document = hit.NodeDocument;
        if (document.BrowsingContext is null || FocusModel.IsInert(hit))
            return null;

        var engine = document.GetLayoutEngine();
        var (x, y) = MapToDocument(document, pointer.X, pointer.Y);
        var point = engine.FromViewport(new PhysicalOffset(x, y));
        for (var element = hit; element is not null; element = element.ParentElement)
        {
            var style = document.GetStyleEngine().GetStyle(element);
            var resize = style.GetComputedValue("resize");
            if (resize is "none" or "" || !style.Layout.IsScrollContainer || engine.GetFragments(element) is not [var fragment]
                || !LayoutEngine.TryMapFromRoot(fragment, point, out var local))
            {
                continue;
            }

            var isLeft = style.Layout.IsHorizontalWritingMode && style.Layout.Direction == Direction.Rtl;
            if (!GetResizerRect(document, fragment, isLeft).Contains(local.X, local.Y))
                continue;

            // https://drafts.csswg.org/css-ui-4/#resize: block and inline adjust the size of an axis of the writing mode
            var horizontalWritingMode = style.Layout.IsHorizontalWritingMode;
            var horizontal = resize is "both" or "horizontal" || (resize == "inline" && horizontalWritingMode) || (resize == "block" && !horizontalWritingMode);
            var vertical = resize is "both" or "vertical" || (resize == "block" && horizontalWritingMode) || (resize == "inline" && !horizontalWritingMode);
            var declarations = Css.Cascade.ComputedStyleDeclarations.Create(element, pseudoElement: null);
            return new ResizerPress(element, horizontal, vertical, ParsePixels(declarations.GetPropertyValue("width")), ParsePixels(declarations.GetPropertyValue("height")), pointer.X, pointer.Y, isLeft);
        }

        return null;

        static double ParsePixels(string value)
            => value.EndsWith("px", StringComparison.Ordinal) && double.TryParse(value.AsSpan(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    /// <summary>
    /// Returns the area of the resizer of a scroll container in the coordinates of its fragment: a square as large as its classic
    /// scrollbars (<see cref="DefaultResizerSize"/> without them) in its bottom corner, inside its borders.
    /// </summary>
    private static PhysicalRect GetResizerRect(Document document, BoxFragment fragment, bool isLeft)
    {
        var scrollbars = fragment.Scrollbars;
        var size = Math.Max(Math.Max(scrollbars.Left, scrollbars.Right), scrollbars.Bottom);
        if (size <= 0)
        {
            size = ScrollContainers.GetScrollbarThickness(document, fragment.Box.Style.Layout);
            if (size <= 0)
            {
                size = DefaultResizerSize;
            }
        }

        var inner = fragment.BorderBoxRect.Contract(fragment.Border);
        size = Math.Min(size, Math.Min(inner.Width, inner.Height));
        return new PhysicalRect(isLeft ? inner.Left : inner.Right - size, inner.Bottom - size, size, size);
    }

    /// <summary>The resizer handles the press: the press is not the start of a selection nor of a drag-and-drop operation.</summary>
    private static void BeginResizerPress(PointerInput pointer)
    {
        pointer.DragNode = null;
        pointer.DragsSelection = false;
    }

    /// <summary>
    /// The pointer moves while it presses a resizer: the size of the element is its size when the button was pressed plus the move
    /// of the pointer since then, in the coordinates of the box (the move is mapped by the inverse of its transforms).
    /// </summary>
    private void MoveResizerPress(PointerInput pointer, ResizerPress press)
    {
        var element = press.Element;
        var document = element.NodeDocument;
        if (!element.IsConnected || document.BrowsingContext is null)
            return;

        var engine = document.GetLayoutEngine();
        if (engine.EnsureLayout() is null || engine.GetFragments(element) is not [var fragment])
            return;

        var (startX, startY) = MapToDocument(document, press.X, press.Y);
        var (x, y) = MapToDocument(document, pointer.X, pointer.Y);
        if (!LayoutEngine.TryMapFromRoot(fragment, engine.FromViewport(new PhysicalOffset(startX, startY)), out var start)
            || !LayoutEngine.TryMapFromRoot(fragment, engine.FromViewport(new PhysicalOffset(x, y)), out var current))
        {
            return;
        }

        // As in the browsers, the box is not made smaller than the resizer (the min and max sizes still apply,
        // https://drafts.csswg.org/css-ui-4/#resize); a size that does not change is not set (it keeps its specified value, e.g. a
        // percentage: WPT css/css-overflow/resizer-no-size-change.tentative.html)
        var style = element.GetInlineStyle();
        if (press.Horizontal)
        {
            var deltaX = current.X - start.X;
            SetSize("width", press.Width, press.IsLeft ? -deltaX : deltaX);
        }

        if (press.Vertical)
        {
            SetSize("height", press.Height, current.Y - start.Y);
        }

        void SetSize(string property, double size, double delta)
        {
            // (the size is rounded to 1/64 px, the layout unit of the browsers: the moves mapped through the transforms are not exact)
            var value = Math.Max(Math.Min(size, DefaultResizerSize), Math.Round((size + delta) * 64, MidpointRounding.AwayFromZero) / 64);
            if (Math.Abs(value - size) > 0.01)
            {
                style.SetProperty(property, Css.Values.CssSerializer.SerializeNumber(value) + "px");
            }
        }
    }
}
