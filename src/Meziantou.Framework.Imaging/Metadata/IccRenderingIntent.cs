namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>The rendering intents of ICC color conversion. The values are those of the ICC profile header.</summary>
public enum IccRenderingIntent
{
    /// <summary>Perceptual: a pleasing reproduction, with a gamut mapping chosen by the profile creator.</summary>
    Perceptual = 0,

    /// <summary>Media-relative colorimetric: in-gamut colors are reproduced relative to the media white point.</summary>
    RelativeColorimetric = 1,

    /// <summary>Saturation: preserves vividness at the expense of hue and lightness accuracy.</summary>
    Saturation = 2,

    /// <summary>ICC-absolute colorimetric: in-gamut colors are reproduced exactly, including the media white point.</summary>
    AbsoluteColorimetric = 3,
}
