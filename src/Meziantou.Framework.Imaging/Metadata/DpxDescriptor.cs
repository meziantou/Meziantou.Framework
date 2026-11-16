namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>The components of a DPX image element and their order (SMPTE ST 268, Table 1, field 21.6).</summary>
/// <remarks>Values the standard reserves keep their number: a file may hold one, and the decoder rejects it.</remarks>
public enum DpxDescriptor
{
    /// <summary>A user-defined or unspecified single component.</summary>
    UserDefined = 0,

    /// <summary>Red.</summary>
    Red = 1,

    /// <summary>Green.</summary>
    Green = 2,

    /// <summary>Blue.</summary>
    Blue = 3,

    /// <summary>Alpha (matte).</summary>
    Alpha = 4,

    /// <summary>Luma (Y).</summary>
    Luma = 6,

    /// <summary>Color difference (Cb, Cr, subsampled by two).</summary>
    ColorDifference = 7,

    /// <summary>Depth (Z).</summary>
    Depth = 8,

    /// <summary>Composite video.</summary>
    CompositeVideo = 9,

    /// <summary>Red, green and blue.</summary>
    Rgb = 50,

    /// <summary>Red, green, blue and alpha.</summary>
    Rgba = 51,

    /// <summary>Alpha, blue, green and red.</summary>
    Abgr = 52,

    /// <summary>Cb, Y, Cr, Y: 4:2:2 (SMPTE ST 125).</summary>
    CbYCrY = 100,

    /// <summary>Cb, Y, A, Cr, Y, A: 4:2:2:4.</summary>
    CbYACrYA = 101,

    /// <summary>Cb, Y, Cr: 4:4:4.</summary>
    CbYCr = 102,

    /// <summary>Cb, Y, Cr, A: 4:4:4:4.</summary>
    CbYCrA = 103,

    /// <summary>A user-defined element of two components.</summary>
    UserDefined2 = 150,

    /// <summary>A user-defined element of three components.</summary>
    UserDefined3 = 151,

    /// <summary>A user-defined element of four components.</summary>
    UserDefined4 = 152,

    /// <summary>A user-defined element of five components.</summary>
    UserDefined5 = 153,

    /// <summary>A user-defined element of six components.</summary>
    UserDefined6 = 154,

    /// <summary>A user-defined element of seven components.</summary>
    UserDefined7 = 155,

    /// <summary>A user-defined element of eight components.</summary>
    UserDefined8 = 156,
}
