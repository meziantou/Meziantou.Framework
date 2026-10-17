using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Rendering;

/// <summary>The kinds of the <see cref="DisplayItem"/>s.</summary>
internal enum DisplayItemKind : byte
{
    Save,
    SaveLayer,
    Restore,
    Transform,
    ClipRect,
    ClipRoundedRect,
    ClipPath,
    DrawPaint,
    DrawRect,
    DrawRoundedRect,
    DrawRing,
    DrawPath,
    DrawLine,
    DrawGlyphs,
    DrawImage,
}

/// <summary>
/// An operation of a <see cref="DisplayList"/>. The state operations (save, restore, transform, clip) apply to the following
/// operations until the matching restore; the drawing operations paint over the pixels drawn before them.
/// </summary>
internal abstract record DisplayItem
{
    /// <summary>The kind of the operation.</summary>
    public abstract DisplayItemKind Kind { get; }

    /// <summary>
    /// The node the operation paints (an element for its box, a text node for its glyphs), or null: for the dumps, and for the
    /// contentful paints of the paint timing (#380).
    /// </summary>
    public Node? Source { get; init; }
}

/// <summary>Saves the current transform and clip: the matching <see cref="RestoreItem"/> restores them.</summary>
internal sealed record SaveItem : DisplayItem
{
    public static SaveItem Instance { get; } = new();

    /// <inheritdoc/>
    public override DisplayItemKind Kind => DisplayItemKind.Save;
}

/// <summary>
/// Saves the state and starts an offscreen layer (a group, https://drafts.fxtf.org/compositing-1/#csscompositingrules_CSS):
/// the following operations are drawn into a transparent layer, which the matching <see cref="RestoreItem"/> filters, then
/// composites with <see cref="Opacity"/> and <see cref="BlendMode"/> (an isolated group, https://drafts.fxtf.org/compositing-1/#isolation).
/// </summary>
/// <param name="Opacity">The opacity of the layer, from 0 to 1.</param>
/// <param name="BlendMode">The operator the layer is composited with.</param>
/// <param name="Filters">The filters applied to the layer in order before it is composited, or null.</param>
internal sealed record SaveLayerItem(double Opacity, BlendMode BlendMode, DisplayFilter[]? Filters) : DisplayItem
{
    /// <inheritdoc/>
    public override DisplayItemKind Kind => DisplayItemKind.SaveLayer;

    /// <summary>A bound of the area drawn in the layer (in the current coordinates), or null when it is not known: a hint for the size of the layer.</summary>
    public PhysicalRect? Bounds { get; init; }

    /// <summary>
    /// The filters of the backdrop (https://drafts.fxtf.org/filter-effects-2/#BackdropFilterProperty), or null: the layer starts
    /// with the pixels drawn before it in the parent layer (the backdrop), filtered in order and clipped to <see cref="BackdropClip"/>.
    /// </summary>
    public DisplayFilter[]? BackdropFilters { get; init; }

    /// <summary>The area of the filtered backdrop (the border box of the element, rounded by its border radii), in the current coordinates.</summary>
    public RoundedRect? BackdropClip { get; init; }
}

/// <summary>Restores the state saved by the matching <see cref="SaveItem"/> or <see cref="SaveLayerItem"/> (and composites the layer).</summary>
internal sealed record RestoreItem : DisplayItem
{
    public static RestoreItem Instance { get; } = new();

    /// <inheritdoc/>
    public override DisplayItemKind Kind => DisplayItemKind.Restore;
}

/// <summary>Multiplies the current transform by <paramref name="Matrix"/>: the coordinates of the following operations are transformed by it, then by the current transform.</summary>
internal sealed record TransformItem(DisplayMatrix Matrix) : DisplayItem
{
    /// <inheritdoc/>
    public override DisplayItemKind Kind => DisplayItemKind.Transform;
}

/// <summary>Intersects the current clip with a rectangle.</summary>
internal sealed record ClipRectItem(PhysicalRect Rect, bool AntiAlias = false) : DisplayItem
{
    /// <inheritdoc/>
    public override DisplayItemKind Kind => DisplayItemKind.ClipRect;
}

/// <summary>Intersects the current clip with a rounded rectangle (antialiased).</summary>
internal sealed record ClipRoundedRectItem(RoundedRect Rect) : DisplayItem
{
    /// <inheritdoc/>
    public override DisplayItemKind Kind => DisplayItemKind.ClipRoundedRect;
}

/// <summary>
/// Intersects the current clip with the interior of a path (with its fill rule), antialiased unless <paramref name="AntiAlias"/>
/// is false: the pixels whose center is in the path are kept, so two clips that share an edge cover each pixel once (the joins of
/// the sides of a border of one color).
/// </summary>
internal sealed record ClipPathItem(DisplayPath Path, bool AntiAlias = true) : DisplayItem
{
    /// <inheritdoc/>
    public override DisplayItemKind Kind => DisplayItemKind.ClipPath;
}

/// <summary>Paints the whole clip area (e.g. the background of the canvas, https://drafts.csswg.org/css-backgrounds/#special-backgrounds).</summary>
internal sealed record DrawPaintItem(DisplayPaint Paint) : DisplayItem
{
    /// <inheritdoc/>
    public override DisplayItemKind Kind => DisplayItemKind.DrawPaint;
}

/// <summary>Paints a rectangle.</summary>
internal sealed record DrawRectItem(PhysicalRect Rect, DisplayPaint Paint) : DisplayItem
{
    /// <inheritdoc/>
    public override DisplayItemKind Kind => DisplayItemKind.DrawRect;
}

/// <summary>Paints a rounded rectangle.</summary>
internal sealed record DrawRoundedRectItem(RoundedRect Rect, DisplayPaint Paint) : DisplayItem
{
    /// <inheritdoc/>
    public override DisplayItemKind Kind => DisplayItemKind.DrawRoundedRect;
}

/// <summary>Fills the area between two rounded rectangles, <paramref name="Inner"/> being inside <paramref name="Outer"/> (a uniform border).</summary>
internal sealed record DrawRingItem(RoundedRect Outer, RoundedRect Inner, DisplayPaint Paint) : DisplayItem
{
    /// <inheritdoc/>
    public override DisplayItemKind Kind => DisplayItemKind.DrawRing;
}

/// <summary>Fills (with its fill rule) or strokes a path.</summary>
internal sealed record DrawPathItem(DisplayPath Path, DisplayPaint Paint) : DisplayItem
{
    /// <inheritdoc/>
    public override DisplayItemKind Kind => DisplayItemKind.DrawPath;
}

/// <summary>Strokes a straight line (e.g. a dashed or dotted border side, a text decoration).</summary>
internal sealed record DrawLineItem(PhysicalOffset Start, PhysicalOffset End, DisplayPaint Paint) : DisplayItem
{
    /// <inheritdoc/>
    public override DisplayItemKind Kind => DisplayItemKind.DrawLine;
}

/// <summary>Fills (or strokes, e.g. <c>-webkit-text-stroke</c>) the outlines of glyphs.</summary>
internal sealed record DrawGlyphsItem(GlyphRun Run, DisplayPaint Paint) : DisplayItem
{
    /// <inheritdoc/>
    public override DisplayItemKind Kind => DisplayItemKind.DrawGlyphs;
}

/// <summary>
/// Draws the area <paramref name="SourceRect"/> of an image (in image pixels) scaled into <paramref name="DestinationRect"/>.
/// The opacity and the blend mode of <paramref name="Paint"/> apply.
/// </summary>
internal sealed record DrawImageItem(PaintImage Image, PhysicalRect SourceRect, PhysicalRect DestinationRect, ImageSampling Sampling, DisplayPaint? Paint = null) : DisplayItem
{
    /// <inheritdoc/>
    public override DisplayItemKind Kind => DisplayItemKind.DrawImage;
}
