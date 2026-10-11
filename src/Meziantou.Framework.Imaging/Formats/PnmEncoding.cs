namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The raster encoding written by <see cref="PnmEncoder"/>.</summary>
public enum PnmEncoding
{
    /// <summary>
    /// Binary samples (<c>P5</c>, <c>P6</c> or <c>P7</c>): the compact, widely readable output. This is the default.
    /// </summary>
    Binary = 0,

    /// <summary>
    /// Plain (ASCII) decimal samples (<c>P2</c> or <c>P3</c>). The family has no plain form with alpha, so a pixel format
    /// with alpha then requires <see cref="PnmEncoder.BackgroundColor"/>.
    /// </summary>
    Plain = 1,
}
