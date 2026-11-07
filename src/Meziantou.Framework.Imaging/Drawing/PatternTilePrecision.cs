namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>The samples of the image a <see cref="PatternBrush"/> draws its tile on.</summary>
/// <remarks>See <see cref="PatternBrush.TilePrecision"/>.</remarks>
public enum PatternTilePrecision
{
    /// <summary>The precision of the canvas: 8-bit samples on an 8-bit color canvas, 16-bit samples otherwise, as a layer has. This is the default.</summary>
    Canvas = 0,

    /// <summary>
    /// 8-bit samples (<see cref="Rgba32"/>) on every canvas: the tile is drawn as on an 8-bit color canvas, then painted
    /// as an image of 8-bit samples is, as browsers rasterize the drawing of a canvas (a CSS paint worklet) into an
    /// 8-bit bitmap.
    /// </summary>
    EightBit = 1,
}
