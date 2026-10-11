namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>How the four components of a <see cref="JpegTestImage"/> code inks.</summary>
internal enum JpegTestInkCoding
{
    /// <summary>The image has one or three components: no ink.</summary>
    None,

    /// <summary>CMYK samples, declared by an Adobe APP14 segment with the transform 0.</summary>
    Cmyk,

    /// <summary>YCCK samples, declared by an Adobe APP14 segment with the transform 2.</summary>
    Ycck,

    /// <summary>CMYK samples without any APP0 or APP14 segment.</summary>
    CmykWithoutAdobe,
}
