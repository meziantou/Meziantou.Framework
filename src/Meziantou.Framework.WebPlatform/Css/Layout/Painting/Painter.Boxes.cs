using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The box decorations: the borders, the outlines, the gap decorations and the graphical items of the MathML boxes (the
// backgrounds are in Painter.Backgrounds.cs, the border images in Painter.BorderImage.cs, the shadows in Painter.Shadows.cs,
// the collapsed borders of the tables in Painter.Tables.cs).
internal sealed partial class Painter
{
    /// <summary>
    /// Whether a box is a table cell without visible content whose borders and background are hidden
    /// (https://drafts.csswg.org/css-tables-3/#empty-cells, <c>empty-cells: hide</c> in the separated borders model).
    /// </summary>
    private static bool IsHiddenEmptyCell(BoxFragment fragment)
        => fragment.Box.Display.Internal == DisplayInternal.TableCell
        && fragment.Box.Style.Layout.Table.EmptyCells == EmptyCells.Hide
        && !HasVisibleContent(fragment)
        && !IsInCollapsedTable(fragment.Box);

    /// <summary>
    /// Whether a cell has visible content (https://drafts.csswg.org/css-tables-3/#empty-cell): an in-flow child other than collapsed
    /// white space (a text fragment with glyphs, a box), or a line box that is not empty.
    /// </summary>
    private static bool HasVisibleContent(Fragment fragment)
    {
        foreach (var child in fragment.ChildrenSpan)
        {
            switch (child)
            {
                case TextFragment { GlyphCount: > 0 }:
                    return true;

                case BoxFragment box when !box.Box.IsOutOfFlow:
                    return true;

                case LineBoxFragment line when HasVisibleContent(line):
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Paints the borders of a box (https://drafts.csswg.org/css-backgrounds/#borders). In the collapsing border model, the
    /// borders of the cells and of the table are painted by the table (see <see cref="PaintCollapsedBorders"/>); a border image
    /// replaces the border styles.
    /// </summary>
    private void PaintBorder(BoxFragment fragment, PhysicalOffset offset)
    {
        if (IsHiddenEmptyCell(fragment) || IsInCollapsedTable(fragment.Box))
            return;

        // https://drafts.csswg.org/css-borders-4/#border-shape: the border is the stroke of the shape, or the area between the
        // two shapes; the border image is not painted (as in Blink)
        if (BorderShapeGeometry.Get(fragment) is { } shape)
        {
            PaintShapedBorder(fragment, shape, fragment.BorderBoxRect.Translate(offset).Offset);
            return;
        }

        var style = fragment.Box.Style;
        if (style.GetComputedValue("border-image-source") is not ("none" or "") && PaintBorderImage(fragment, style, GetSnappedBorderBox(fragment, offset)))
            return;

        var border = fragment.Border;
        if (border == default)
            return;

        // (the colors of the fragment of an inline box on the first line are the ones of its style inheriting from ::first-line,
        // e.g. currentcolor: see GetDecorationStyle)
        var styles = style.Layout.Box.BorderStyle;
        var colorStyle = GetDecorationStyle(fragment);
        var colors = new Sides<DisplayColor>(
            GetColor(colorStyle, "border-top-color"),
            GetColor(colorStyle, "border-right-color"),
            GetColor(colorStyle, "border-bottom-color"),
            GetColor(colorStyle, "border-left-color"));
        var outer = RoundedRect.FromBorderBox(fragment, GetSnappedBorderBox(fragment, offset));
        if (fragment.Box.LayoutState is FieldsetState { HasLegend: true } fieldset && fragment.IsFirstFragment)
        {
            // https://html.spec.whatwg.org/multipage/rendering.html#the-fieldset-and-legend-elements: "the border is expected to
            // not be painted behind the rectangle" of the rendered legend
            var gap = SnapToDevicePixels(fragment.WritingDirection.ToPhysical(fieldset.Gap, fragment.Size).Translate(fragment.BorderBoxRect.Translate(offset).Offset));
            var extent = (2 * (outer.Rect.Width + outer.Rect.Height)) + 128;
            var path = new DisplayPath { FillRule = FillRule.EvenOdd };
            path.AddRect(new PhysicalRect(outer.Rect.X - extent, outer.Rect.Y - extent, outer.Rect.Width + (2 * extent), outer.Rect.Height + (2 * extent)));
            path.AddRect(gap);
            _builder.Save();
            _builder.Add(new ClipPathItem(path));
            BorderPainter.Paint(_builder, outer, border, styles, colors, fragment.Box.Element, _devicePixelRatio);
            _builder.Restore();
            return;
        }

        BorderPainter.Paint(_builder, outer, border, styles, colors, fragment.Box.Element, _devicePixelRatio);
    }

    /// <summary>
    /// Paints the borders between the rows and between the columns of a frameset (see <see cref="FramesetFormattingContext"/>):
    /// "using the element's frame border color".
    /// </summary>
    private void PaintFramesetBorders(BoxFragment fragment, PhysicalOffset offset, FramesetState frameset)
    {
        if (DisplayColor.Parse(FramesetFormattingContext.GetFrameBorderColor(fragment.Box.Element!)) is not { } color)
            return;

        var content = fragment.ContentBoxRect.Translate(offset);
        foreach (var border in frameset.Borders)
        {
            _builder.Add(new DrawRectItem(SnapToDevicePixels(border.Translate(content.Offset)), new DisplayPaint(color)) { Source = fragment.Box.Element });
        }
    }

    private static bool IsInCollapsedTable(ContainerBox box)
    {
        if (box.Kind == ContainerBoxKind.TableGrid)
            return box.Style.Layout.Table.BorderCollapse == BorderCollapse.Collapse;

        if (box.Display.Internal is DisplayInternal.None or DisplayInternal.TableCaption)
            return false;

        for (var ancestor = box.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.Kind == ContainerBoxKind.TableGrid)
                return ancestor.Style.Layout.Table.BorderCollapse == BorderCollapse.Collapse;
        }

        return false;
    }

    /// <summary>
    /// Whether a style has an outline (https://drafts.csswg.org/css-ui-4/#outline-props): a style other than none and a width
    /// (the outline of the <c>auto</c> style does not depend on <c>outline-width</c>, see <see cref="PaintOutline"/>).
    /// </summary>
    private static bool HasOutline(ComputedStyle style)
        => style.GetComputedValue("outline-style") is not ("none" or "") and var outlineStyle && (outlineStyle == "auto" || style.GetComputedValue("outline-width") is not ("0px" or "0"));

    /// <summary>
    /// Returns the color of the focus ring of an element (<c>outline-color: auto</c>): its <c>accent-color</c>
    /// (https://drafts.csswg.org/css-ui-4/#widget-accent), or the <c>AccentColor</c> system color of its used color scheme for <c>auto</c>.
    /// </summary>
    private static DisplayColor GetFocusRingColor(ComputedStyle style)
    {
        var color = style.GetComputedValue("accent-color") is "auto" or "" ? DisplayColor.Parse(CssValueComputer.GetSystemColor(style, "accentcolor")) : DisplayColor.Parse(style.GetResolvedValue("accent-color"));
        return color ?? ControlAccentColor;
    }

    /// <summary>
    /// Paints the outline of a box (https://drafts.csswg.org/css-ui-4/#outline-props) around its border box, at the distance of
    /// <c>outline-offset</c> (a negative offset does not shrink the outline below twice its width; <c>inset</c> is the opposite
    /// of the width of the outline: the outline is inside of the border box), following its border radii and the shapes of its
    /// corners (https://drafts.csswg.org/css-borders-4/#corner-shape-rendering: "An element's outline follows the outer
    /// contour"). <c>auto</c> is a focus ring (see <see cref="PaintFocusRing"/>).
    /// </summary>
    private void PaintOutline(BoxFragment fragment, PhysicalOffset offset)
    {
        var style = fragment.Box.Style;
        var zoom = style.Layout.EffectiveZoom;

        // https://drafts.csswg.org/css-ui-4/#outline-style: "The auto value permits the user agent to render a custom outline
        // style [...] this specification does not define how the outline-color and outline-width are incorporated when
        // rendering custom outline styles": as in Blink, the focus ring is 3px wide whatever outline-width is
        var isAuto = style.GetComputedValue("outline-style") is "auto";
        var width = isAuto ? 3 * zoom : BoxModelStyle.ParseFixedLength(style.GetComputedValue("outline-width"), zoom);
        if (width <= 0)
            return;

        var outlineOffset = GetOutlineOffset(style, width, zoom);

        // https://drafts.csswg.org/css-ui-4/#outline-color: with outline-style: auto, outline-color: auto "represents the accent
        // color"
        var color = style.GetComputedValue("outline-color") == "auto" ? GetFocusRingColor(style) : GetColor(GetDecorationStyle(fragment), "outline-color");
        if (isAuto)
        {
            PaintFocusRing(fragment, offset, width, outlineOffset, color);
            return;
        }

        var lineStyle = LayoutKeywords.ParseBorderStyle(style.GetComputedValue("outline-style"));
        if (BorderShapeGeometry.Get(fragment) is { } shape)
        {
            PaintShapedOutline(fragment, shape, fragment.BorderBoxRect.Translate(offset).Offset, width, outlineOffset, lineStyle, color);
            return;
        }

        var outer = GetOutlineOuterEdge(fragment, offset, width, outlineOffset);
        BorderPainter.Paint(_builder, outer, Uniform(width), new Sides<BorderStyle>(lineStyle, lineStyle, lineStyle, lineStyle), new Sides<DisplayColor>(color, color, color, color), fragment.Box.Element, _devicePixelRatio);
    }

    /// <summary>The used <c>outline-offset</c> of a style: <c>inset</c> is the opposite of the width of the outline (https://drafts.csswg.org/css-ui-4/#outline-offset).</summary>
    private static double GetOutlineOffset(ComputedStyle style, double width, double zoom)
    {
        var value = style.GetComputedValue("outline-offset");
        return value == "inset" ? -width : BoxModelStyle.ParseFixedLength(value, zoom);
    }

    /// <summary>
    /// Returns the outer edge of the outline of a fragment: its border edge outset by <c>outline-offset</c> and the width of the
    /// outline (https://drafts.csswg.org/css-ui-4/#outline-offset: "Negative values must cause the outline to shrink into the
    /// border box. Both the height and the width of the outside of the shape drawn by the outline should not become smaller than
    /// twice the computed value of the outline-width property": in each axis, around the center of the box; each fragment is a
    /// shape).
    /// </summary>
    private RoundedRect GetOutlineOuterEdge(BoxFragment fragment, PhysicalOffset offset, double width, double outlineOffset)
    {
        var border = RoundedRect.FromBorderBox(fragment, GetSnappedBorderBox(fragment, offset));
        var outset = outlineOffset + width;
        var horizontalOutset = Math.Max(outset, width - (border.Rect.Width / 2));
        var verticalOutset = Math.Max(outset, width - (border.Rect.Height / 2));
        return border.Inset(new Sides<double>(-verticalOutset, -horizontalOutset, -verticalOutset, -horizontalOutset));
    }

    /// <summary>
    /// Paints the focus ring of <c>outline-style: auto</c> (https://drafts.csswg.org/css-ui-4/#outline-style: "typically a style
    /// which is either a user interface default for the platform [...] e.g. a rounded edge outline"), as in Blink: two rings of a
    /// 2:1 ratio with rounded corners (a radius of at least the width of the ring), the inner one in the color of the outline,
    /// the outer one white (#101010 in a dark color scheme) so that the ring is visible on any background.
    /// </summary>
    private void PaintFocusRing(BoxFragment fragment, PhysicalOffset offset, double width, double outlineOffset, DisplayColor color)
    {
        var contrast = CssValueComputer.GetUsedColorScheme(fragment.Box.Style) == "dark" ? DisplayColor.FromRgb(0x10, 0x10, 0x10) : DisplayColor.White;
        var innerWidth = width * 2 / 3;
        var source = fragment.Box.Element;
        if (BorderShapeGeometry.Get(fragment) is { } shape)
        {
            var origin = fragment.BorderBoxRect.Translate(offset).Offset;
            PaintShapedOutline(fragment, shape, origin, width, outlineOffset, BorderStyle.Solid, contrast);
            PaintShapedOutline(fragment, shape, origin, innerWidth, outlineOffset, BorderStyle.Solid, color);
            return;
        }

        var outer = GetOutlineOuterEdge(fragment, offset, width, outlineOffset);
        if (outer.Contour is null)
        {
            outer = outer with { Radii = new BorderRadii(Round(outer.Radii.TopLeft), Round(outer.Radii.TopRight), Round(outer.Radii.BottomRight), Round(outer.Radii.BottomLeft)) };
        }

        var solid = new Sides<BorderStyle>(BorderStyle.Solid, BorderStyle.Solid, BorderStyle.Solid, BorderStyle.Solid);
        BorderPainter.Paint(_builder, outer, Uniform(width), solid, new Sides<DisplayColor>(contrast, contrast, contrast, contrast), source, _devicePixelRatio);
        BorderPainter.Paint(_builder, outer.Inset(Uniform(width - innerWidth)), Uniform(innerWidth), solid, new Sides<DisplayColor>(color, color, color, color), source, _devicePixelRatio);

        (double X, double Y) Round((double X, double Y) radius) => (Math.Max(radius.X, width), Math.Max(radius.Y, width));
    }

    /// <summary>
    /// Paints the gap decorations of a grid, flex or multi-column container (https://drafts.csswg.org/css-gaps-1/#gap-decorations):
    /// "Gap decorations are painted just above the border of the container". The rectangles are computed from the gaps of the
    /// layout (<see cref="GapDecorations.GetDecorations"/>).
    /// </summary>
    private void PaintGapDecorations(BoxFragment fragment, PhysicalOffset offset)
    {
        if (GapDecorations.GetDecorations(fragment) is not { } decorations)
            return;

        // "For scrollable containers, note that while the border and background of the container aren't scrolled, the decorations
        // need to scroll along with items in the container": they are clipped as the contents of the box
        if (fragment.ClipsOverflow)
        {
            SyncClips(GetClipChain(fragment, includeSelf: true));
            if (fragment.ScrollState is { IsViewport: false } scrollState)
            {
                offset = new PhysicalOffset(offset.X - scrollState.Offset.X, offset.Y - scrollState.Offset.Y);
            }
        }

        foreach (var decoration in decorations)
        {
            BorderPainter.PaintRule(_builder, SnapToDevicePixels(decoration.Rect.Translate(offset)), decoration.IsVertical, decoration.Style, decoration.Color, fragment.Box.Element, _devicePixelRatio);
        }

        if (fragment.ClipsOverflow)
        {
            SyncClips(GetClipChain(fragment, includeSelf: false));
        }
    }

    /// <summary>
    /// Paints the graphical items of a MathML box (https://w3c.github.io/mathml-core/#layout-algorithms): the rules (fraction
    /// bars, radical overbars) and the glyphs shaped by the math layout (stretched operators, radical symbols), in the color of
    /// the box. The mirroring of the radical symbol in right-to-left math without <c>rtlm</c> glyph is not done.
    /// </summary>
    private void PaintMath(BoxFragment fragment, PhysicalOffset offset)
    {
        if (fragment.MathData is not { PaintItems.Count: > 0 } data)
            return;

        var paint = new DisplayPaint(GetColor(fragment.Box.Style, "color"));
        if (paint.Color.IsTransparent)
            return;

        foreach (var item in data.PaintItems)
        {
            var rect = item.Rect.Translate(offset);
            if (item.Glyph is not { } glyph)
            {
                _builder.Add(new DrawRectItem(rect, paint) { Source = fragment.Box.Element });
                continue;
            }

            var glyphs = new ushort[glyph.Glyphs.Length];
            var positions = new PhysicalOffset[glyph.Glyphs.Length];
            for (var i = 0; i < glyphs.Length; i++)
            {
                var placed = glyph.Glyphs[i];
                glyphs[i] = placed.Glyph;
                positions[i] = new PhysicalOffset(rect.X + placed.X, rect.Y + item.Baseline + placed.Y);
            }

            var run = new GlyphRun(DisplayFont.From(glyph.Font, glyph.EmSize), glyphs, positions, Text: "");
            _builder.Add(new DrawGlyphsItem(run, paint) { Source = fragment.Box.Element });
        }
    }

    private static Sides<double> Uniform(double value) => new(value, value, value, value);

    /// <summary>Returns the last item of a comma-separated list (e.g. the value of the last background layer).</summary>
    private static string GetLastListItem(string value)
    {
        var index = value.LastIndexOf(',', StringComparison.Ordinal);
        return index < 0 ? value : value[(index + 1)..].Trim();
    }
}
