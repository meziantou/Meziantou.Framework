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
/// (the one of the brush, then the one of the canvas) gives it, then that image is repeated over the shape like the
/// image of an <see cref="ImageBrush"/> with <see cref="ImageWrapMode.Repeat"/>: a pattern that is enlarged stays as
/// sharp as its drawing. The image has the width <c>ceil(w - 1/1024)</c>, where <c>w</c> is the width of the tile on
/// the canvas (and the same for its height), so that a tile whose size is a whole number of pixels is drawn on
/// exactly that many: its pixels are then those of the drawing, exactly, when it is placed on whole pixels. Otherwise
/// the image is interpolated (<see cref="ImageSampling.Bilinear"/>), also across the edges of the tiles. The image
/// has colors and alpha, with 8-bit samples on an 8-bit color canvas and 16-bit samples otherwise, as a layer has.
/// The canvas the callback receives is that image: its <see cref="DrawingCanvas.Width"/> and
/// <see cref="DrawingCanvas.Height"/> are its size in pixels, not the size of the tile, and filling it fills the tile.
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
}
