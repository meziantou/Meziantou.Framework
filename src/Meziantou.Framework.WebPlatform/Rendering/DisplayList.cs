using System.Diagnostics;
using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Rendering;

/// <summary>
/// A display list: the drawing operations that paint a document (or a canvas), in order, independent of any graphics library.
/// The painting of a document (<see cref="Painter"/>) produces one from the fragment tree, in the painting order of CSS; a
/// <see cref="RenderingBackend"/> rasterizes it (<see cref="RenderingSurface.Draw"/>). The coordinates are CSS px of the
/// viewport (the scroll positions are applied), and the saves and restores are balanced. A display list is immutable.
/// </summary>
internal sealed class DisplayList
{
    private readonly DisplayItem[] _items;

    public DisplayList(DisplayItem[] items, PhysicalSize size)
    {
        _items = items;
        Size = size;
    }

    /// <summary>The empty display list.</summary>
    public static DisplayList Empty { get; } = new([], default);

    /// <summary>The operations, in drawing order.</summary>
    public ReadOnlySpan<DisplayItem> Items => _items;

    /// <summary>The number of operations.</summary>
    public int Count => _items.Length;

    /// <summary>The size of the area the display list paints (the viewport of a document, the bitmap of a canvas), in CSS px.</summary>
    public PhysicalSize Size { get; }

    /// <summary>Returns the display list drawn with a transform (e.g. the scale of a device pixel ratio).</summary>
    public DisplayList WithTransform(DisplayMatrix matrix)
    {
        var items = new DisplayItem[_items.Length + 3];
        items[0] = SaveItem.Instance;
        items[1] = new TransformItem(matrix);
        _items.CopyTo(items, 2);
        items[^1] = RestoreItem.Instance;
        return new DisplayList(items, Size);
    }

    /// <summary>
    /// Returns a text representation of the operations, one per line, indented by the nesting of the saves (for the tests and
    /// the diagnostics): the rectangles are <c>(x, y, width x height)</c>, the colors <c>#rrggbb[aa]</c>, followed by the node
    /// that is painted.
    /// </summary>
    public string Dump()
    {
        var builder = new StringBuilder();
        var depth = 0;
        foreach (var item in _items)
        {
            if (item.Kind == DisplayItemKind.Restore)
            {
                depth = Math.Max(0, depth - 1);
            }

            builder.Append(' ', depth * 2);
            DumpItem(builder, item);
            if (item.Source is { } source)
            {
                builder.Append("  [").Append(DescribeNode(source)).Append(']');
            }

            builder.Append('\n');
            if (item.Kind is DisplayItemKind.Save or DisplayItemKind.SaveLayer)
            {
                depth++;
            }
        }

        return builder.ToString();
    }

    private static void DumpItem(StringBuilder builder, DisplayItem item)
    {
        switch (item)
        {
            case SaveItem:
                builder.Append("save");
                break;

            case SaveLayerItem layer:
                builder.Append("save-layer");
                if (layer.Opacity < 1)
                {
                    builder.Append(" opacity=").Append(FormatNumber(layer.Opacity));
                }

                if (layer.BlendMode != BlendMode.Normal)
                {
                    builder.Append(" blend=").Append(FormatEnum(layer.BlendMode));
                }

                if (layer.Filters is { Length: > 0 } filters)
                {
                    builder.Append(" filters=").Append(string.Join(' ', filters.Select(FormatFilter)));
                }

                if (layer.BackdropFilters is { Length: > 0 } backdrop)
                {
                    builder.Append(" backdrop=").Append(string.Join(' ', backdrop.Select(FormatFilter)));
                    if (layer.BackdropClip is { } backdropClip)
                    {
                        builder.Append(" in ").Append(FormatRoundedRect(backdropClip));
                    }
                }

                if (layer.Bounds is { } bounds)
                {
                    builder.Append(" bounds=").Append(FormatRect(bounds));
                }

                break;

            case RestoreItem:
                builder.Append("restore");
                break;

            case TransformItem transform:
                builder.Append("transform ").Append(transform.Matrix.ToString());
                break;

            case ClipRectItem clip:
                builder.Append("clip-rect ").Append(FormatRect(clip.Rect));
                break;

            case ClipRoundedRectItem clip:
                builder.Append("clip-rounded-rect ").Append(FormatRoundedRect(clip.Rect));
                break;

            case ClipPathItem clip:
                builder.Append("clip-path ").Append(clip.Path.ToString());
                if (!clip.AntiAlias)
                {
                    builder.Append(" aliased");
                }
                break;

            case DrawPaintItem paint:
                builder.Append("draw-paint ").Append(FormatPaint(paint.Paint));
                break;

            case DrawRectItem rect:
                builder.Append("draw-rect ").Append(FormatRect(rect.Rect)).Append(' ').Append(FormatPaint(rect.Paint));
                break;

            case DrawRoundedRectItem rect:
                builder.Append("draw-rounded-rect ").Append(FormatRoundedRect(rect.Rect)).Append(' ').Append(FormatPaint(rect.Paint));
                break;

            case DrawRingItem ring:
                builder.Append("draw-ring ").Append(FormatRoundedRect(ring.Outer)).Append(" - ").Append(FormatRoundedRect(ring.Inner)).Append(' ').Append(FormatPaint(ring.Paint));
                break;

            case DrawPathItem path:
                builder.Append("draw-path ").Append(path.Path.ToString()).Append(' ').Append(FormatPaint(path.Paint));
                break;

            case DrawLineItem line:
                builder.Append("draw-line ").Append(FormatPoint(line.Start)).Append(" -> ").Append(FormatPoint(line.End)).Append(' ').Append(FormatPaint(line.Paint));
                break;

            case DrawGlyphsItem glyphs:
            {
                var run = glyphs.Run;
                builder.Append("draw-glyphs \"").Append(run.Text.Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal)).Append("\" ").Append(run.Font.Name).Append(' ').Append(FormatNumber(run.Font.Size)).Append("px");
                if (run.Positions.Length > 0)
                {
                    builder.Append(" at ").Append(FormatPoint(run.Positions[0]));
                }

                builder.Append(" glyphs=").Append(run.Glyphs.Length);
                if (run.ScaleX != 1)
                {
                    builder.Append(" scale-x=").Append(FormatNumber(run.ScaleX));
                }

                builder.Append(' ').Append(FormatPaint(glyphs.Paint));
                break;
            }

            case DrawImageItem image:
                builder.Append("draw-image ").Append(image.Image.ToString()).Append(' ').Append(FormatRect(image.SourceRect)).Append(" -> ").Append(FormatRect(image.DestinationRect));
                if (image.Sampling != ImageSampling.Smooth)
                {
                    builder.Append(' ').Append(FormatEnum(image.Sampling));
                }

                if (image.Paint is { } imagePaint)
                {
                    builder.Append(' ').Append(FormatPaint(imagePaint));
                }

                break;

            default:
                builder.Append(item.Kind.ToString());
                break;
        }
    }

    private static string FormatPaint(DisplayPaint paint)
    {
        var builder = new StringBuilder();
        builder.Append(paint.Style == PaintStyle.Stroke ? "stroke " : "fill ");
        builder.Append(paint.Shader switch
        {
            null => paint.Color.ToString(),
            LinearGradientShader => "linear-gradient",
            RadialGradientShader => "radial-gradient",
            ConicGradientShader => "conic-gradient",
            ImageShader image => "image(" + image.Image + ")",
            DisplayListShader list => "display-list(" + FormatNumber(list.Size.Width) + "x" + FormatNumber(list.Size.Height) + ")",
            TiledShader tiled => "tiles(" + FormatShader(tiled.Content) + " " + FormatNumber(tiled.TileSize.Width) + "x" + FormatNumber(tiled.TileSize.Height) + (tiled.Spacing == default ? "" : " spacing=" + FormatNumber(tiled.Spacing.Width) + "x" + FormatNumber(tiled.Spacing.Height)) + ")",
            _ => "shader",
        });

        if (paint.Style == PaintStyle.Stroke && paint.Stroke is { } stroke)
        {
            builder.Append(" width=").Append(FormatNumber(stroke.Width));
            if (stroke.Dashes is { Length: > 0 } dashes)
            {
                builder.Append(" dashes=").Append(string.Join(',', dashes.Select(FormatNumber)));
            }

            if (stroke.Cap != LineCap.Butt)
            {
                builder.Append(" cap=").Append(FormatEnum(stroke.Cap));
            }
        }

        if (paint.BlendMode != BlendMode.Normal)
        {
            builder.Append(" blend=").Append(FormatEnum(paint.BlendMode));
        }

        if (paint.BlurSigma > 0)
        {
            builder.Append(" blur=").Append(FormatNumber(paint.BlurSigma));
        }

        return builder.ToString();
    }

    private static string FormatShader(DisplayShader shader) => shader switch
    {
        LinearGradientShader => "linear-gradient",
        RadialGradientShader => "radial-gradient",
        ConicGradientShader => "conic-gradient",
        ImageShader image => "image(" + image.Image + ")",
        DisplayListShader list => "display-list(" + FormatNumber(list.Size.Width) + "x" + FormatNumber(list.Size.Height) + ")",
        _ => "shader",
    };

    private static string FormatFilter(DisplayFilter filter) => filter switch
    {
        BlurFilter blur => "blur(" + FormatNumber(blur.DeviationX) + (blur.DeviationY != blur.DeviationX ? "," + FormatNumber(blur.DeviationY) : "") + ")",
        DropShadowFilter shadow => "drop-shadow(" + FormatPoint(shadow.Offset) + " " + FormatNumber(shadow.Deviation) + " " + shadow.Color + ")",
        ColorMatrixFilter matrix => "color-matrix(" + string.Join(',', matrix.Matrix.Select(value => FormatNumber(value))) + ")",
        LuminanceToAlphaFilter => "luminance-to-alpha",
        FilterGraph graph => "graph(" + string.Join(' ', graph.Primitives.Select(FormatPrimitive)) + " in " + FormatRect(graph.Region) + ")",
        _ => "filter",
    };

    private static string FormatPrimitive(FilterPrimitive primitive)
    {
        var name = primitive.GetType().Name;
        name = name.EndsWith("Primitive", StringComparison.Ordinal) ? name[..^"Primitive".Length] : name;
        return FormatEnumName(name) + "[" + string.Join(',', primitive.Inputs) + "]" + (primitive.LinearRgb ? "" : "srgb");
    }

    private static string FormatEnumName(string name) => name.ToLowerInvariant();

    private static string FormatEnum<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    internal static string FormatRect(PhysicalRect rect) => "(" + FormatNumber(rect.X) + ", " + FormatNumber(rect.Y) + ", " + FormatNumber(rect.Width) + "x" + FormatNumber(rect.Height) + ")";

    private static string FormatPoint(PhysicalOffset point) => "(" + FormatNumber(point.X) + ", " + FormatNumber(point.Y) + ")";

    private static string FormatRoundedRect(RoundedRect rect)
    {
        var text = FormatRect(rect.Rect);
        if (rect.IsRect)
            return text;

        var radii = rect.Radii;
        return text + " radii=" + FormatRadius(radii.TopLeft) + "," + FormatRadius(radii.TopRight) + "," + FormatRadius(radii.BottomRight) + "," + FormatRadius(radii.BottomLeft);

        static string FormatRadius((double X, double Y) radius) => radius.X == radius.Y ? FormatNumber(radius.X) : FormatNumber(radius.X) + "/" + FormatNumber(radius.Y);
    }

    /// <summary>Formats a number for the dumps: rounded to 3 decimals, invariant culture.</summary>
    internal static string FormatNumber(double value)
    {
        var rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
        return (rounded == 0 ? 0 : rounded).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string DescribeNode(Node node) => node switch
    {
        Element element => element.GetAttributeValue("id") is { Length: > 0 } id ? element.LocalName + "#" + id : element.LocalName,
        Text => "#text",
        _ => node.NodeName,
    };
}

/// <summary>
/// Builds a <see cref="DisplayList"/>: the operations are appended in drawing order. The builder tracks the nesting of the
/// saves (<see cref="SaveCount"/>) so that the painting can restore to a known depth.
/// </summary>
internal sealed class DisplayListBuilder
{
    private readonly List<DisplayItem> _items = [];

    /// <summary>The number of saves (and save layers) that are not restored yet.</summary>
    public int SaveCount { get; private set; }

    /// <summary>The number of operations appended so far.</summary>
    public int Count => _items.Count;

    public void Save()
    {
        _items.Add(SaveItem.Instance);
        SaveCount++;
    }

    /// <summary>
    /// Saves the state before drawing the content of another document for a node (e.g. an SVG image of an element): the operations
    /// until the matching restore are the paint of <paramref name="source"/> (for the paint timing).
    /// </summary>
    public void Save(Node source)
    {
        _items.Add(new SaveItem { Source = source });
        SaveCount++;
    }

    public void SaveLayer(SaveLayerItem layer)
    {
        _items.Add(layer);
        SaveCount++;
    }

    public void Restore()
    {
        Debug.Assert(SaveCount > 0, "Restore without save");
        _items.Add(RestoreItem.Instance);
        SaveCount--;
    }

    /// <summary>Restores until <see cref="SaveCount"/> is <paramref name="count"/>.</summary>
    public void RestoreTo(int count)
    {
        while (SaveCount > count)
        {
            Restore();
        }
    }

    /// <summary>Appends the operations of a display list (e.g. the display list of a nested document), with their saves and restores.</summary>
    public void AddList(DisplayList list)
    {
        var saveCount = SaveCount;
        foreach (var item in list.Items)
        {
            switch (item)
            {
                case SaveItem:
                    Save();
                    break;

                case SaveLayerItem layer:
                    SaveLayer(layer);
                    break;

                case RestoreItem:
                    if (SaveCount > saveCount)
                    {
                        Restore();
                    }

                    break;

                default:
                    Add(item);
                    break;
            }
        }

        RestoreTo(saveCount);
    }

    /// <summary>Appends an operation that is not a save or a restore.</summary>
    public void Add(DisplayItem item)
    {
        Debug.Assert(item.Kind is not (DisplayItemKind.Save or DisplayItemKind.SaveLayer or DisplayItemKind.Restore), "Use Save/SaveLayer/Restore");
        if (!AddShapedCorners(item))
        {
            _items.Add(item);
        }
    }

    /// <summary>
    /// Appends the operations of a rounded rectangle whose corners are shaped by <c>corner-shape</c>
    /// (<see cref="RoundedRect.Contour"/>), and returns whether the operation was one: the backends only draw the rounded
    /// rectangles with elliptical corners, so the contour of the box is a path (the polygon of the flattened curves). When the
    /// corner areas overlap, the shape is the rectangle of the edge clipped by the outside of each corner area.
    /// </summary>
    private bool AddShapedCorners(DisplayItem item)
    {
        switch (item)
        {
            case ClipRoundedRectItem { Rect.Contour: not null } clip:
                AddContourClip(clip.Rect, clip.Source);
                return true;

            case DrawRoundedRectItem { Rect.Contour: { } contour } draw:
            {
                var shape = contour.GetShape(draw.Rect.Rect, draw.Rect.HasAdjustedOutsets);
                if (shape.Cutouts is null)
                {
                    _items.Add(new DrawPathItem(ToPath(shape.Polygon), draw.Paint) { Source = draw.Source });
                }
                else
                {
                    Save();
                    AddCutoutClips(draw.Rect.Rect, shape.Cutouts, draw.Source);
                    _items.Add(new DrawRectItem(draw.Rect.Rect, draw.Paint) { Source = draw.Source });
                    Restore();
                }

                return true;
            }

            case DrawRingItem ring when ring.Outer.Contour is not null || ring.Inner.Contour is not null:
            {
                // The area inside of the outer contour and outside of the inner one
                Save();
                Add(ring.Outer.IsRect ? new ClipRectItem(ring.Outer.Rect, AntiAlias: true) { Source = ring.Source } : new ClipRoundedRectItem(ring.Outer) { Source = ring.Source });
                var bounds = ring.Outer.Rect;
                _items.Add(new DrawPathItem(DisplayPath.CreateComplement(new PhysicalRect(bounds.X - 1, bounds.Y - 1, bounds.Width + 2, bounds.Height + 2), ring.Inner), ring.Paint) { Source = ring.Source });
                Restore();
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>Clips to the contour of a rounded rectangle whose corners are shaped by <c>corner-shape</c>.</summary>
    private void AddContourClip(RoundedRect rect, Node? source)
    {
        var shape = rect.Contour!.GetShape(rect.Rect, rect.HasAdjustedOutsets);
        if (shape.Cutouts is null)
        {
            _items.Add(new ClipPathItem(ToPath(shape.Polygon)) { Source = source });
            return;
        }

        _items.Add(new ClipRectItem(rect.Rect, AntiAlias: true) { Source = source });
        AddCutoutClips(rect.Rect, shape.Cutouts, source);
    }

    /// <summary>Clips to the outside of each corner area of a contour.</summary>
    private void AddCutoutClips(PhysicalRect rect, List<List<PhysicalOffset>> cutouts, Node? source)
    {
        // The bounds contain the corner areas (they extend diagonally by the width and the height of the rectangle, plus 64)
        var extent = (2 * (rect.Width + rect.Height)) + 128;
        var bounds = new PhysicalRect(rect.X - extent, rect.Y - extent, rect.Width + (2 * extent), rect.Height + (2 * extent));
        foreach (var cutout in cutouts)
        {
            var path = new DisplayPath { FillRule = FillRule.EvenOdd };
            path.AddRect(bounds);
            CornerContour.AddPolygon(path, cutout, reverse: false);
            _items.Add(new ClipPathItem(path) { Source = source });
        }
    }

    private static DisplayPath ToPath(List<PhysicalOffset> polygon)
    {
        var path = new DisplayPath();
        CornerContour.AddPolygon(path, polygon, reverse: false);
        return path;
    }

    /// <summary>Appends a transform, unless it is the identity.</summary>
    public void Transform(DisplayMatrix matrix)
    {
        if (matrix.IsTranslation && matrix.E == 0 && matrix.F == 0)
            return;

        _items.Add(new TransformItem(matrix));
    }

    /// <summary>Returns the display list (the saves that are not restored are restored).</summary>
    public DisplayList ToDisplayList(PhysicalSize size)
    {
        RestoreTo(0);
        return new DisplayList([.. _items], size);
    }
}
