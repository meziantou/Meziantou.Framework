using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A brush painting a drawing that repeats: a tile drawn by a callback, laid side by side in both directions.</summary>
/// <remarks>
/// <para>
/// <b>Tile.</b> <see cref="Tile"/> is the rectangle that repeats, in the coordinates of the pattern, and
/// <see cref="Content"/> draws it, in those coordinates: with a tile at <c>(0, 0)</c> of 16 by 16, the callback draws
/// between 0 and 16, and the same drawing is at <c>(16, 0)</c>, <c>(0, 16)</c> and so on. What the callback draws
/// outside the tile is not painted. <see cref="Transform"/> places the pattern on the canvas, as it places a gradient
/// or an image; without it, the coordinates of the pattern are those of the canvas, whatever the shape that is painted.
/// </para>
/// <para>
/// <b>Rendering.</b> When a shape is painted, the tile is drawn once, on an image of the size the transformation
/// (the one of the brush, then the one of the canvas) gives it, with the spacing (<see cref="SpacingX"/>,
/// <see cref="SpacingY"/>) on its right and below it, transparent, then that image is laid over the shape like the
/// image of an <see cref="ImageBrush"/> with the wrap modes of the pattern (<see cref="ImageWrapMode.Repeat"/> by
/// default): a pattern that is enlarged stays as
/// sharp as its drawing. The image has the width <c>ceil(w - 1/1024)</c>, where <c>w</c> is the width of the tile and
/// of its spacing on the canvas (and the same for its height), so that a tile whose size is a whole number of pixels is drawn on
/// exactly that many: its pixels are then those of the drawing, exactly, when it is placed on whole pixels. Otherwise
/// the image is interpolated (<see cref="ImageSampling.Bilinear"/>), also across the edges of the tiles. The image
/// has colors and alpha, with 8-bit samples on an 8-bit color canvas and 16-bit samples otherwise, as a layer has.
/// The canvas the callback receives is that image: its <see cref="DrawingCanvas.Width"/> and
/// <see cref="DrawingCanvas.Height"/> are its size in pixels, not the size of the tile, and filling it fills the tile.
/// </para>
/// <para>
/// A tile whose image would have more than four times the pixels the clip can show (and 65,536), or would exceed the
/// limits of the image, is drawn where the canvas shows it: along each axis where the canvas shows less than a tile,
/// the image holds that part of the pattern, at the same scale, and the callback runs once for each tile it shows (two
/// at most, mirrored for a tile that <see cref="ImageWrapMode.Reflect"/> turns), clipped to its tile. Under a projective
/// transformation, the image of the tile is drawn at the resolution those limits allow instead.
/// </para>
/// <para>
/// <b>Callback.</b> The callback runs while a shape is drawn, before any pixel of that shape changes, and may run
/// once for each shape: it must draw the same thing every time, must not be asynchronous and must not keep the canvas
/// it receives. It can use any brush except, directly or not, the pattern itself. A brush that is kept and reused
/// keeps its callback, and everything the callback refers to.
/// </para>
/// </remarks>
public sealed class PatternBrush : Brush
{
    /// <summary>Initializes a new instance of the <see cref="PatternBrush"/> class.</summary>
    /// <param name="tile">The rectangle that repeats, in the coordinates of the pattern. A <see cref="Rectangle"/> converts implicitly.</param>
    /// <param name="content">The callback that draws the tile, in the coordinates of the pattern.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="tile"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate of <paramref name="tile"/> exceeds 16777216, or its width or its height is not positive.</exception>
    public PatternBrush(RectangleD tile, DrawingAction content)
    {
        DrawingGuard.Rectangle(tile, nameof(tile));
        if (!(tile.Width > 0 && tile.Height > 0))
            throw new ArgumentOutOfRangeException(nameof(tile), tile, "The width and the height of the tile must be positive.");

        ArgumentNullException.ThrowIfNull(content);
        Tile = tile;
        Content = content;
    }

    /// <summary>Gets the rectangle that repeats, in the coordinates of the pattern.</summary>
    public RectangleD Tile { get; }

    /// <summary>Gets the callback that draws the tile.</summary>
    public DrawingAction Content { get; }

    /// <summary>Gets the transformation that places the pattern on the canvas. Defaults to the identity.</summary>
    /// <remarks>With a transformation that flattens the plane to a line or a point, the brush paints nothing.</remarks>
    public AffineTransform Transform { get; init; }

    /// <summary>
    /// Gets how the tile is laid along x: <see cref="ImageWrapMode.Repeat"/> (the default) lays it side by side,
    /// <see cref="ImageWrapMode.Transparent"/> paints the tile once along x (<c>background-repeat: repeat-y</c> of CSS
    /// with <see cref="WrapModeY"/> left to repeat), <see cref="ImageWrapMode.Reflect"/> mirrors every other tile, and
    /// <see cref="ImageWrapMode.Pad"/> extends the pixels of the edges of the image of the tile.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ImageWrapMode"/>.</exception>
    public ImageWrapMode WrapModeX
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The wrap mode is not valid.");

            field = value;
        }
    } = ImageWrapMode.Repeat;

    /// <summary>Gets how the tile is laid along y. Defaults to <see cref="ImageWrapMode.Repeat"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ImageWrapMode"/>.</exception>
    public ImageWrapMode WrapModeY
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The wrap mode is not valid.");

            field = value;
        }
    } = ImageWrapMode.Repeat;

    /// <summary>
    /// Gets the gap between two tiles along x, in the coordinates of the pattern: nothing is painted there
    /// (<c>background-repeat: space</c> of CSS, once the gap is computed). Defaults to 0.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or exceeds 16777216.</exception>
    public double SpacingX
    {
        get;
        init
        {
            DrawingGuard.Length(value, nameof(value));
            field = value;
        }
    }

    /// <summary>Gets the gap between two tiles along y, in the coordinates of the pattern. Defaults to 0.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or exceeds 16777216.</exception>
    public double SpacingY
    {
        get;
        init
        {
            DrawingGuard.Length(value, nameof(value));
            field = value;
        }
    }
}
