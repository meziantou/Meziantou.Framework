namespace Meziantou.Framework.Imaging.Formats;

/// <summary>How <see cref="IcoEncoder"/> stores the pixels of one representation, and <see cref="AniEncoder"/> those of one frame.</summary>
public enum IconPayloadFormat
{
    /// <summary>
    /// A 32-bit DIB for representations of at most 64 pixels per side, and a PNG above that. This is what current icon
    /// tools write: small sizes stay readable by every version of Windows, and large ones avoid a megabyte of
    /// uncompressed samples. Pixels with 16-bit samples are always written as a PNG, which stores them losslessly.
    /// </summary>
    Auto = 0,

    /// <summary>Always a PNG payload. Readers older than Windows Vista do not understand it.</summary>
    Png = 1,

    /// <summary>
    /// Always a 32-bit <c>BITMAPINFOHEADER</c> DIB with an all-zero AND mask. It stores 8-bit samples: pixels with 16-bit
    /// samples are rejected with an <see cref="UnsupportedImageFeatureException"/> instead of being narrowed.
    /// </summary>
    Dib = 2,
}
