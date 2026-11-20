using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// Finds the content painted over an element in the display list of its document, for the visibility of Intersection Observer
/// v2 (https://w3c.github.io/IntersectionObserver/v2/#calculate-visibility-algo): the display list is in the painting order of
/// CSS (the stacking contexts, the planes of a 3D rendering context sorted and split by the painter, the content of the SVG
/// elements and of the nested documents), so a drawing operation that is after the first visible operation of the element
/// paints over it. The area of an operation is its ink: the outlines of the glyphs, the blur of a shadow, the width of a
/// stroke, then the filters of its layers (blurs and drop shadows extend it, an SVG filter fills its filter region), with the
/// transforms and the clips. The operations of a group whose opacity is 0, of a mask, or that paint nothing (a transparent
/// color) do not occlude.
/// </summary>
internal static class OcclusionScanner
{
    /// <summary>
    /// Returns whether an operation painted after the first visible operation of <paramref name="subject"/> (an element of
    /// <paramref name="document"/>, with its shadow-including descendants and the content of its nested documents) covers a part
    /// of <paramref name="rect"/> (in the coordinates of the display list), or null when the element paints nothing visible.
    /// </summary>
    public static bool? IsOccluded(DisplayList list, Document document, Element subject, PhysicalRect rect)
    {
        // The layers (index 0 is the display list) collect the areas of the occluding operations in the coordinates of the display
        // list; a layer applies its filters to them when it is restored
        var layers = new List<Layer> { new(Filters: null, DisplayMatrix.Identity, Discarded: false) };
        var state = new ScanState(DisplayMatrix.Identity, Clip: null, LayerClip: null, Hidden: false, LayerIndex: 0, Owner: null);
        var stack = new Stack<ScanState>();
        var started = false;
        foreach (var item in list.Items)
        {
            switch (item)
            {
                case SaveItem save:
                    stack.Push(state);

                    // The content of another document drawn for an element (e.g. an SVG image) or the drawings of a canvas
                    if (save.Source is { } source && GetNodeInDocument(source, document) is { } owner)
                    {
                        state = state with { Owner = owner };
                    }

                    continue;

                case SaveLayerItem layer:
                {
                    stack.Push(state);
                    var hidden = state.Hidden || layer.Opacity <= 0 || layer.BlendMode is BlendMode.DestinationIn or BlendMode.DestinationOut or BlendMode.Clear;
                    layers.Add(new Layer(layer.Filters, state.Transform, hidden));
                    state = state with { LayerClip = null, Hidden = hidden, LayerIndex = layers.Count - 1 };
                    continue;
                }

                case RestoreItem:
                {
                    if (stack.Count == 0)
                        continue;

                    var parent = stack.Pop();
                    if (parent.LayerIndex != state.LayerIndex && CloseLayer(layers[state.LayerIndex], layers[parent.LayerIndex], parent, rect))
                        return true;

                    state = parent;
                    continue;
                }

                case TransformItem transform:
                    state = state with { Transform = state.Transform.PreConcat(transform.Matrix) };
                    continue;

                case ClipRectItem clip:
                    state = state.WithClip(ContentfulPaintScanner.TransformBounds(state.Transform, clip.Rect));
                    continue;

                case ClipRoundedRectItem clip:
                    state = state.WithClip(ContentfulPaintScanner.TransformBounds(state.Transform, clip.Rect.Rect));
                    continue;

                case ClipPathItem clip:
                    state = state.WithClip(ContentfulPaintScanner.TransformBounds(state.Transform, clip.Path.GetBounds()));
                    continue;
            }

            var itemOwner = item.Source is { } itemSource ? GetNodeInDocument(itemSource, document) ?? state.Owner : state.Owner;
            if (itemOwner is null || GetInkBounds(item) is not { } local)
                continue;

            var bounds = ContentfulPaintScanner.TransformBounds(state.Transform, local);
            if (subject.IsShadowIncludingInclusiveAncestorOf(itemOwner))
            {
                // The element is painted where its first operation that is not clipped out is (a plane split by a 3D rendering
                // context is painted in several parts)
                started |= !state.Hidden && !Intersect(state.Clip, bounds).IsEmpty;
                continue;
            }

            if (!started || state.Hidden || !Paints(item))
                continue;

            var area = Intersect(state.LayerClip, bounds);
            if (area.IsEmpty)
                continue;

            if (state.LayerIndex == 0)
            {
                if (Overlaps(area, rect))
                    return true;
            }
            else
            {
                layers[state.LayerIndex].Areas.Add(area);
            }
        }

        return started ? false : null;
    }

    /// <summary>
    /// Returns the node of a document that paints an operation whose source is <paramref name="node"/>: the node itself, or the
    /// container (an iframe element) of the nested document that contains it, or null.
    /// </summary>
    private static Node? GetNodeInDocument(Node node, Document document)
    {
        var current = node;
        for (var depth = 0; depth < 64; depth++)
        {
            if (ReferenceEquals(current.NodeDocument, document))
                return current;

            if (current.NodeDocument.BrowsingContext?.Container is not { } container)
                return null;

            current = container;
        }

        return null;
    }

    /// <summary>Applies the filters of a restored layer to its areas, clips them by the clip of the parent layer, and adds them to the parent layer. Returns whether one of them covers a part of <paramref name="rect"/> in the display list.</summary>
    private static bool CloseLayer(Layer layer, Layer parentLayer, ScanState parent, PhysicalRect rect)
    {
        if (layer.Discarded || layer.Areas.Count == 0)
            return false;

        var areas = layer.Filters is { } filters ? ApplyFilters(filters, layer.Transform, layer.Areas) : layer.Areas;
        foreach (var filtered in areas)
        {
            var area = Intersect(parent.LayerClip, filtered);
            if (area.IsEmpty)
                continue;

            if (parent.LayerIndex == 0)
            {
                if (Overlaps(area, rect))
                    return true;
            }
            else
            {
                parentLayer.Areas.Add(area);
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the areas a filter paints from the areas of its input (in the coordinates of the display list), the filter
    /// applying in the coordinates of <paramref name="transform"/>: a blur extends them by three standard deviations, a drop shadow
    /// adds them moved by its offset, an SVG filter paints in its filter region, a color matrix whose alpha is 0 paints nothing.
    /// </summary>
    private static List<PhysicalRect> ApplyFilters(DisplayFilter[] filters, DisplayMatrix transform, List<PhysicalRect> input)
    {
        var scaleX = Math.Sqrt((transform.A * transform.A) + (transform.B * transform.B));
        var scaleY = Math.Sqrt((transform.C * transform.C) + (transform.D * transform.D));
        var areas = input;
        foreach (var filter in filters)
        {
            if (areas.Count == 0)
                break;

            var result = new List<PhysicalRect>(areas.Count);
            switch (filter)
            {
                case BlurFilter blur:
                    foreach (var area in areas)
                    {
                        result.Add(Expand(area, 3 * Math.Abs(blur.DeviationX) * scaleX, 3 * Math.Abs(blur.DeviationY) * scaleY));
                    }

                    break;

                case DropShadowFilter shadow:
                {
                    var offset = new PhysicalOffset((transform.A * shadow.Offset.X) + (transform.C * shadow.Offset.Y), (transform.B * shadow.Offset.X) + (transform.D * shadow.Offset.Y));
                    var reach = 3 * Math.Abs(shadow.Deviation);
                    foreach (var area in areas)
                    {
                        if (!shadow.Color.IsTransparent)
                        {
                            result.Add(Expand(area.Translate(offset), reach * scaleX, reach * scaleY));
                        }

                        if (!shadow.ShadowOnly)
                        {
                            result.Add(area);
                        }
                    }

                    break;
                }

                case FilterGraph graph:
                    result.Add(ContentfulPaintScanner.TransformBounds(transform, graph.Region));
                    break;

                case ColorMatrixFilter matrix when matrix.Matrix is [_, _, _, _, _, _, _, _, _, _, _, _, _, _, _, 0, 0, 0, 0, 0, ..]:
                    break;

                default:
                    result.AddRange(areas);
                    break;
            }

            areas = result;
        }

        return areas;
    }

    /// <summary>Returns the bounds of what a drawing operation paints in its coordinates (the outlines of its glyphs, extended by its blur and its stroke), or null when it paints nothing.</summary>
    private static PhysicalRect? GetInkBounds(DisplayItem item)
    {
        if (item is DrawGlyphsItem glyphs)
        {
            if (GetGlyphBounds(glyphs.Run) is not { } glyphBounds)
                return null;

            var glyphExtent = (glyphs.Paint.BlurSigma * 3) + ((glyphs.Paint.Stroke?.Width ?? 0) / 2);
            return glyphExtent > 0 ? Expand(glyphBounds, glyphExtent, glyphExtent) : glyphBounds;
        }

        if (ContentfulPaintScanner.GetBounds(item) is not { } bounds)
            return null;

        var paint = ContentfulPaintScanner.GetPaint(item);
        var extent = ((paint?.BlurSigma ?? 0) * 3) + ((paint?.Stroke?.Width ?? 0) / 2);
        return extent > 0 ? Expand(bounds, extent, extent) : bounds;
    }

    /// <summary>
    /// Returns the bounds of the outlines of the glyphs of a run (in the coordinates of the run), extended for the synthesized
    /// bold and oblique styles, or the em boxes of the glyphs when the font has no outline bounds (e.g. a bitmap font).
    /// </summary>
    private static PhysicalRect? GetGlyphBounds(GlyphRun run)
    {
        var font = run.Font;
        if (font.File.Faces is not { } faces || font.FaceIndex >= faces.Count || faces[font.FaceIndex] is not { UnitsPerEm: > 0 } face)
            return ContentfulPaintScanner.GetBounds(new DrawGlyphsItem(run, new DisplayPaint(DisplayColor.Black)));

        var scale = font.Size / face.UnitsPerEm;
        var slant = Math.Abs(Math.Tan(font.SyntheticObliqueAngle * Math.PI / 180));
        var bold = font.SyntheticBold ? font.Size / 16 : 0;
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        var hasOutline = false;
        for (var i = 0; i < run.Glyphs.Length && i < run.Positions.Length; i++)
        {
            if (!face.TryGetGlyphBounds(run.Glyphs[i], out var glyph) || glyph.XMax <= glyph.XMin || glyph.YMax <= glyph.YMin)
                continue;

            hasOutline = true;
            var position = run.Positions[i];
            var skew = slant * Math.Max(Math.Abs((double)glyph.YMax), Math.Abs((double)glyph.YMin)) * scale;
            left = Math.Min(left, position.X + (glyph.XMin * scale * run.ScaleX) - skew - bold);
            right = Math.Max(right, position.X + (glyph.XMax * scale * run.ScaleX) + skew + bold);
            top = Math.Min(top, position.Y - (glyph.YMax * scale) - bold);
            bottom = Math.Max(bottom, position.Y - (glyph.YMin * scale) + bold);
        }

        if (!hasOutline)
            return ContentfulPaintScanner.HasNonWhiteSpace(run.Text) ? ContentfulPaintScanner.GetBounds(new DrawGlyphsItem(run, new DisplayPaint(DisplayColor.Black))) : null;

        return new PhysicalRect(left, top, right - left, bottom - top);
    }

    /// <summary>Whether a drawing operation paints something: not a transparent color, nor an operator that only erases.</summary>
    private static bool Paints(DisplayItem item)
    {
        var paint = item switch
        {
            DrawGlyphsItem glyphs => glyphs.Paint,
            DrawImageItem image => image.Paint,
            _ => ContentfulPaintScanner.GetPaint(item),
        };

        if (paint is null)
            return item is DrawImageItem;

        return (paint.Shader is not null || !paint.Color.IsTransparent) && paint.BlendMode is not (BlendMode.DestinationIn or BlendMode.DestinationOut or BlendMode.Clear);
    }

    private static PhysicalRect Expand(PhysicalRect rect, double x, double y) => new(rect.X - x, rect.Y - y, rect.Width + (2 * x), rect.Height + (2 * y));

    /// <summary>Returns the part of a rectangle in a clip (an empty rectangle when they do not intersect).</summary>
    private static PhysicalRect Intersect(PhysicalRect? clip, PhysicalRect rect)
        => clip is not { } clipRect ? rect : IntersectionGeometry.TryIntersect(clipRect, rect, out var result) ? result : new PhysicalRect(rect.X, rect.Y, 0, 0);

    /// <summary>Whether two rectangles share an area (edge-adjacent rectangles do not).</summary>
    private static bool Overlaps(PhysicalRect a, PhysicalRect b) => a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    /// <summary>
    /// The state of the drawing operations: the transform, the bounds of the clip in the coordinates of the display list (all the
    /// clips, and the clips set in the current layer, which apply before its filters), whether the operations are in a group
    /// that paints nothing, the current layer, and the node whose content is drawn (the owner of the operations of another document).
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct ScanState(DisplayMatrix Transform, PhysicalRect? Clip, PhysicalRect? LayerClip, bool Hidden, int LayerIndex, Node? Owner)
    {
        public ScanState WithClip(PhysicalRect clip) => this with { Clip = Intersect(Clip, clip), LayerClip = Intersect(LayerClip, clip) };
    }

    /// <summary>A layer: its filters, the transform they apply in, whether it paints nothing (opacity 0, a mask), and the areas of the occluding operations drawn in it.</summary>
    private sealed record Layer(DisplayFilter[]? Filters, DisplayMatrix Transform, bool Discarded)
    {
        public List<PhysicalRect> Areas { get; } = [];
    }
}
