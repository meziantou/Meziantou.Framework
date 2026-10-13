namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The features of the container that a format of the HEIF family reads and writes beyond the ones that every format
/// has: the container layer implements them once, and a format turns one on when its codec and its test corpus cover it.
/// </summary>
[Flags]
internal enum HeifContainerFeatures
{
    None = 0,

    /// <summary>Colors that are multiplied by the alpha plane (a <c>prem</c> reference), which are divided when they are read.</summary>
    PremultipliedAlpha = 1,

    /// <summary>Identity derivations, overlays and grids of derived images (<c>iden</c>, <c>iovl</c>).</summary>
    DerivedImages = 2,

    /// <summary>Edit lists that select parts of the media of an image sequence.</summary>
    EditLists = 4,

    /// <summary>Image sequences whose samples are in movie fragments (<c>mvex</c>, <c>moof</c>).</summary>
    Fragments = 8,

    /// <summary>
    /// The matrix coefficients of ITU-T H.273 beyond the ones that a linear matrix of the coded samples converts: SMPTE ST
    /// 2085, the chromaticity-derived ones, constant luminance and ICtCp.
    /// </summary>
    ExtendedMatrices = 16,

    /// <summary>The orientation of an image sequence, which is the matrix of the header of its track (<c>tkhd</c>).</summary>
    SequenceOrientation = 32,

    /// <summary>
    /// The pixel aspect ratio, the content light level and the mastering display color volume of the metadata
    /// (<c>pasp</c>, <c>clli</c> and <c>mdcv</c> boxes). Without it, a <c>pasp</c> box is only validated.
    /// </summary>
    DisplayMetadata = 64,
}
