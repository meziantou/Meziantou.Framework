namespace Meziantou.Framework.Imaging.Formats;

/// <summary>What a JPEG 2000 output is made of.</summary>
public enum Jpeg2000Container
{
    /// <summary>
    /// A JP2 file (<c>.jp2</c>), or a JPH file (<c>.jph</c>) with the high-throughput block coder: the codestream with the
    /// boxes that say what its components are (a color space or an ICC profile, the opacity channel) and that hold the
    /// resolution, the EXIF data and the XMP packet.
    /// </summary>
    Jp2 = 0,

    /// <summary>
    /// The codestream alone (<c>.j2k</c>, <c>.j2c</c>, <c>.jpc</c>). It stores no metadata: a reader takes one component for a
    /// gray level, two for a gray level and its opacity, three for red, green and blue, and four for these with an opacity.
    /// </summary>
    Codestream = 1,
}
