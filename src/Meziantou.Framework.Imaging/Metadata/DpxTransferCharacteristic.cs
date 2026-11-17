namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>
/// The transfer characteristic of a DPX image element (SMPTE ST 268, Table 5A, field 21.7): the function that was applied to
/// linear light to give the stored code values.
/// </summary>
/// <remarks>
/// This is a description of the file: the decoder never applies its inverse. Values the standard reserves keep their number.
/// </remarks>
public enum DpxTransferCharacteristic
{
    /// <summary>User defined.</summary>
    UserDefined = 0,

    /// <summary>Printing density: logarithmic film scans (the Cineon encoding).</summary>
    PrintingDensity = 1,

    /// <summary>Linear.</summary>
    Linear = 2,

    /// <summary>Logarithmic.</summary>
    Logarithmic = 3,

    /// <summary>Unspecified video.</summary>
    UnspecifiedVideo = 4,

    /// <summary>SMPTE ST 274 (1920 × 1080 video).</summary>
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

    /// <summary>Linear depth (Z).</summary>
    DepthLinear = 11,

    /// <summary>Homogeneous depth (Z).</summary>
    DepthHomogeneous = 12,

    /// <summary>Academy Density Exchange Encoding (ADX, SMPTE ST 2065-3).</summary>
    Adx = 13,

    /// <summary>The field is not filled (the undefined value 255 of the standard).</summary>
    Undefined = 255,
}
