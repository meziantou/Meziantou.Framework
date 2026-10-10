namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>The data color space declared in the header of an ICC profile.</summary>
public enum IccProfileColorSpace
{
    /// <summary>The header is missing or too short to declare a color space.</summary>
    Unknown = 0,

    /// <summary>Grayscale (<c>'GRAY'</c>).</summary>
    Gray = 1,

    /// <summary>RGB (<c>'RGB '</c>).</summary>
    Rgb = 2,

    /// <summary>CMYK (<c>'CMYK'</c>).</summary>
    Cmyk = 3,

    /// <summary>Any other declared color space.</summary>
    Other = 4,
}
