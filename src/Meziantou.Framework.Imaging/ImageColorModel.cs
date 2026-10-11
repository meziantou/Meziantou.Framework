namespace Meziantou.Framework.Imaging;

/// <summary>Describes the color model of the <em>encoded</em> source samples, independently of the decoded <see cref="PixelFormat"/>.</summary>
public enum ImageColorModel
{
    /// <summary>The color model is unknown.</summary>
    Unknown = 0,

    /// <summary>Grayscale samples without alpha.</summary>
    Grayscale = 1,

    /// <summary>Grayscale samples with an alpha channel.</summary>
    GrayscaleAlpha = 2,

    /// <summary>Red, green and blue samples without alpha.</summary>
    Rgb = 3,

    /// <summary>Red, green, blue and alpha samples.</summary>
    Rgba = 4,

    /// <summary>Palette indexes (PNG color type 3, GIF). Palette entries may carry transparency.</summary>
    Indexed = 5,

    /// <summary>Luma/chroma samples (JPEG YCbCr).</summary>
    YCbCr = 6,
}
