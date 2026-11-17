namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>
/// The colorimetric specification of a DPX image element (SMPTE ST 268, Table 5B, field 21.8): the color primaries of
/// television signals, or the color responses of printing density.
/// </summary>
/// <remarks>Values the standard reserves, or marks as not applicable (2, 3, 11, 12), keep their number.</remarks>
public enum DpxColorimetricSpecification
{
    /// <summary>User defined.</summary>
    UserDefined = 0,

    /// <summary>Printing density.</summary>
    PrintingDensity = 1,

    /// <summary>Unspecified video.</summary>
    UnspecifiedVideo = 4,

    /// <summary>SMPTE ST 274 (the primaries of BT.709).</summary>
    Smpte274 = 5,

    /// <summary>ITU-R BT.709-4.</summary>
    Bt709 = 6,

    /// <summary>ITU-R BT.601-5 system B or G (625 lines).</summary>
    Bt601System625 = 7,

    /// <summary>ITU-R BT.601-5 system M (525 lines).</summary>
    Bt601System525 = 8,

    /// <summary>Composite video (NTSC, SMPTE ST 170).</summary>
    CompositeNtsc = 9,

    /// <summary>Composite video (PAL, ITU-R 624-4).</summary>
    CompositePal = 10,

    /// <summary>Academy Density Exchange Encoding (ADX, SMPTE ST 2065-3).</summary>
    Adx = 13,

    /// <summary>The field is not filled (the undefined value 255 of the standard).</summary>
    Undefined = 255,
}
