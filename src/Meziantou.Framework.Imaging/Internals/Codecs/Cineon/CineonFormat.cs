using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Constants and shared rules of Kodak Cineon (Image File Format Proposal for Digital Pictures, draft 4.5).</summary>
internal static class CineonFormat
{
    /// <summary>The magic number (field 1.1), read in the byte order of the file.</summary>
    public const uint Magic = 0x802A5FD7;

    /// <summary>The length of the generic section (section 1): the smallest offset of the image data.</summary>
    public const int GenericHeaderLength = 1024;

    /// <summary>The length of the generic and motion-picture sections (sections 1 and 2).</summary>
    public const int FullHeaderLength = 2048;

    /// <summary>The offset of the first channel specifier (field 1.14).</summary>
    public const int FirstChannelOffset = 196;

    /// <summary>The length of a channel specifier.</summary>
    public const int ChannelLength = 28;

    /// <summary>Determines whether the data starts with the magic number in either byte order.</summary>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix)
        => prefix.Length >= 4 && (prefix[..4].SequenceEqual((ReadOnlySpan<byte>)[0x80, 0x2A, 0x5F, 0xD7]) || prefix[..4].SequenceEqual((ReadOnlySpan<byte>)[0xD7, 0x5F, 0x2A, 0x80]));

    public static InvalidImageContentException Invalid(string message) => new("The Cineon file is invalid: " + message, ImageFormat.Cineon);

    public static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.Cineon, feature);

    /// <summary>Maps an orientation code (field 1.12: line scan direction, then page scan direction) to the orientation of the stored pixels.</summary>
    public static ExifOrientation ToExifOrientation(int code) => code switch
    {
        0 => ExifOrientation.TopLeft,
        1 => ExifOrientation.BottomLeft,
        2 => ExifOrientation.TopRight,
        3 => ExifOrientation.BottomRight,
        4 => ExifOrientation.LeftTop,
        5 => ExifOrientation.RightTop,
        6 => ExifOrientation.LeftBottom,
        7 => ExifOrientation.RightBottom,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "The Cineon orientation code is not valid."),
    };

    /// <summary>Maps the orientation of the stored pixels to its orientation code (field 1.12), the inverse of <see cref="ToExifOrientation"/>.</summary>
    public static byte FromExifOrientation(ExifOrientation orientation) => orientation switch
    {
        ExifOrientation.TopLeft => 0,
        ExifOrientation.BottomLeft => 1,
        ExifOrientation.TopRight => 2,
        ExifOrientation.BottomRight => 3,
        ExifOrientation.LeftTop => 4,
        ExifOrientation.RightTop => 5,
        ExifOrientation.LeftBottom => 6,
        ExifOrientation.RightBottom => 7,
        _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "The orientation is not valid."),
    };
}
