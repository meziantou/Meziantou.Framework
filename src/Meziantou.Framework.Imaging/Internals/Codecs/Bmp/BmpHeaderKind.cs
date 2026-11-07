namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The family of a DIB header, selected by its length.</summary>
internal enum BmpHeaderKind
{
    /// <summary>A Windows header: <c>BITMAPINFOHEADER</c> (40 bytes) or its V2 (52), V3 (56), V4 (108) and V5 (124) extensions.</summary>
    Windows,

    /// <summary>The OS/2 1.x <c>BITMAPCOREHEADER</c> (12 bytes), followed by <c>RGBTRIPLE</c> palette entries.</summary>
    Os2V1,

    /// <summary>
    /// The OS/2 2.x <c>BITMAPCOREHEADER2</c>: 16 to 64 bytes, other than the lengths of the Windows headers it would be
    /// indistinguishable from (40, 52 and 56).
    /// </summary>
    Os2V2,
}
