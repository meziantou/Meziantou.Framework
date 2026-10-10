namespace Meziantou.Framework.Imaging.Formats;

/// <summary>How <see cref="IcoEncoder"/> stores the pixels of one representation.</summary>
public enum IconPayloadFormat
{
    /// <summary>
    /// A 32-bit DIB for representations of at most 64 pixels per side, and a PNG above that. This is what current icon
    /// tools write: small sizes stay readable by every version of Windows, and large ones avoid a megabyte of
    /// uncompressed samples.
    /// </summary>
    Auto = 0,

    /// <summary>Always a PNG payload. Readers older than Windows Vista do not understand it.</summary>
    Png = 1,

    /// <summary>Always a 32-bit <c>BITMAPINFOHEADER</c> DIB with an all-zero AND mask.</summary>
    Dib = 2,
}
