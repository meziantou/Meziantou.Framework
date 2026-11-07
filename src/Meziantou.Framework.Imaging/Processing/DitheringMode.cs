namespace Meziantou.Framework.Imaging;

/// <summary>
/// Selects how colors are dithered when they are reduced to a palette, by
/// <see cref="ImageProcessingExtensions.Quantize(Image, QuantizeOptions?, CancellationToken)"/> and by the encoders that reduce
/// colors (<see cref="QuantizeOptions.Dithering"/>).
/// </summary>
/// <remarks>
/// <para>
/// Error diffusion carries the difference between the color of a pixel and the palette color it gets to the pixels to its right
/// and below, with the weights of the named kernel, left to right on every row from the top row down: the output is the
/// same whatever the row order of the file. Ordered dithering (the Bayer matrices) adds a fixed pattern to the colors before
/// they are mapped, so every pixel is mapped independently of the others.
/// </para>
/// <para>
/// Only opaque pixels are dithered by error diffusion: a translucent pixel maps to its nearest palette entry, and transparent
/// pixels neither receive nor spread error. Ordered dithering applies to every pixel that is not fully transparent. A frame
/// whose colors are all in the palette is never dithered.
/// </para>
/// </remarks>
public enum DitheringMode
{
    /// <summary>No dithering: each pixel maps to its nearest palette entry. This is the default.</summary>
    None = 0,

    /// <summary>Floyd–Steinberg error diffusion (7, 3, 5, 1 sixteenths).</summary>
    FloydSteinberg = 1,

    /// <summary>Atkinson error diffusion: six neighbors get an eighth each, so a quarter of the error is dropped (less noise, more contrast).</summary>
    Atkinson = 2,

    /// <summary>Burkes error diffusion over two rows (thirty-seconds).</summary>
    Burkes = 3,

    /// <summary>Jarvis, Judice and Ninke error diffusion over three rows (forty-eighths).</summary>
    JarvisJudiceNinke = 4,

    /// <summary>Sierra error diffusion over three rows (thirty-seconds).</summary>
    Sierra = 5,

    /// <summary>Two-row Sierra error diffusion (sixteenths).</summary>
    SierraTwoRow = 6,

    /// <summary>Sierra Lite error diffusion (2, 1, 1 quarters): the fastest kernel after Floyd–Steinberg.</summary>
    SierraLite = 7,

    /// <summary>Stucki error diffusion over three rows (forty-seconds).</summary>
    Stucki = 8,

    /// <summary>Ordered dithering with the 2×2 Bayer matrix.</summary>
    Bayer2x2 = 9,

    /// <summary>Ordered dithering with the 4×4 Bayer matrix.</summary>
    Bayer4x4 = 10,

    /// <summary>Ordered dithering with the 8×8 Bayer matrix.</summary>
    Bayer8x8 = 11,
}
