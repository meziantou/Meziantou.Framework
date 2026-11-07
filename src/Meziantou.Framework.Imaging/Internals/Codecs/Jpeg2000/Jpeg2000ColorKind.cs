namespace Meziantou.Framework.Imaging.Internals;

/// <summary>How the color channels of a JPEG 2000 image are interpreted.</summary>
internal enum Jpeg2000ColorKind
{
    /// <summary>One channel: a gray level.</summary>
    Gray,

    /// <summary>Three channels: red, green and blue.</summary>
    Rgb,

    /// <summary>Three channels: the luminance and the two chrominances of sYCC (IEC 61966-2-1, Amendment 1), converted to RGB by the decoder.</summary>
    Sycc,

    /// <summary>Four channels: the amounts of cyan, magenta, yellow and black ink.</summary>
    Cmyk,
}
