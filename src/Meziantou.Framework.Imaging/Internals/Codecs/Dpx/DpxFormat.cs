using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Constants and shared rules of DPX (SMPTE ST 268:2014 and 268M-2003, "File Format for Digital Moving-Picture Exchange").</summary>
internal static class DpxFormat
{
    /// <summary>The magic number of a big-endian file: <c>SDPX</c>.</summary>
    public const uint Magic = 0x53445058;

    /// <summary>The length of the generic headers (file, image and image source information, clause 5): the smallest offset of the image data.</summary>
    public const int GenericHeaderLength = 1664;

    /// <summary>The length of the generic and industry-specific headers (clauses 5 and 6): the user-defined data start there.</summary>
    public const int FullHeaderLength = 2048;

    /// <summary>The offset of the image data the encoder writes: the 8 KiB block the standard recommends (field 77).</summary>
    public const int EncodedDataOffset = 8192;

    /// <summary>The offset of the first image element structure (field 21).</summary>
    public const int FirstElementOffset = 780;

    /// <summary>The length of an image element structure (fields 21 to 28).</summary>
    public const int ElementLength = 72;

    /// <summary>The undefined value of the 32-bit fields (clause 4.2).</summary>
    public const uint UndefinedU32 = uint.MaxValue;

    /// <summary>Determines whether the data starts with the magic number of either byte order.</summary>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix)
        => prefix.Length >= 4 && (prefix[..4].SequenceEqual("SDPX"u8) || prefix[..4].SequenceEqual("XPDS"u8));

    public static InvalidImageContentException Invalid(string message) => new("The DPX file is invalid: " + message, ImageFormat.Dpx);

    public static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.Dpx, feature);

    /// <summary>Gets the number of components of a pixel of a descriptor, or 0 for a descriptor that is not decoded.</summary>
    /// <remarks>A pixel of the 4:2:2 descriptors is one picture element: Cb, Y and Cr, Y alternate, so two picture elements hold four (or six) components.</remarks>
    public static int GetComponentsPerPixel(DpxDescriptor descriptor) => descriptor switch
    {
        DpxDescriptor.Red or DpxDescriptor.Green or DpxDescriptor.Blue or DpxDescriptor.Alpha or DpxDescriptor.Luma => 1,
        DpxDescriptor.CbYCrY => 2,
        DpxDescriptor.Rgb or DpxDescriptor.CbYCr or DpxDescriptor.CbYACrYA => 3,
        DpxDescriptor.Rgba or DpxDescriptor.Abgr or DpxDescriptor.CbYCrA => 4,
        _ => 0,
    };

    /// <summary>Gets whether a descriptor stores Cb, Y and Cr samples.</summary>
    public static bool IsYCbCr(DpxDescriptor descriptor) => descriptor is DpxDescriptor.CbYCrY or DpxDescriptor.CbYACrYA or DpxDescriptor.CbYCr or DpxDescriptor.CbYCrA;

    /// <summary>Gets whether the chroma samples of a descriptor are subsampled by two horizontally.</summary>
    public static bool IsSubsampled(DpxDescriptor descriptor) => descriptor is DpxDescriptor.CbYCrY or DpxDescriptor.CbYACrYA;

    /// <summary>Gets whether a descriptor has an alpha component.</summary>
    public static bool HasAlpha(DpxDescriptor descriptor) => descriptor is DpxDescriptor.Alpha or DpxDescriptor.Rgba or DpxDescriptor.Abgr or DpxDescriptor.CbYACrYA or DpxDescriptor.CbYCrA;

    /// <summary>Gets whether a descriptor has a single component.</summary>
    public static bool IsSingleComponent(DpxDescriptor descriptor) => GetComponentsPerPixel(descriptor) == 1;

    /// <summary>Maps an orientation code (Table 2: line direction, then frame direction) to the orientation of the stored pixels.</summary>
    public static ExifOrientation ToExifOrientation(int code) => code switch
    {
        0 => ExifOrientation.TopLeft,
        1 => ExifOrientation.TopRight,
        2 => ExifOrientation.BottomLeft,
        3 => ExifOrientation.BottomRight,
        4 => ExifOrientation.LeftTop,
        5 => ExifOrientation.RightTop,
        6 => ExifOrientation.LeftBottom,
        7 => ExifOrientation.RightBottom,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "The DPX orientation code is not valid."),
    };

    /// <summary>Maps the orientation of stored pixels to the orientation code of Table 2.</summary>
    public static ushort FromExifOrientation(ExifOrientation orientation) => orientation switch
    {
        ExifOrientation.TopLeft => 0,
        ExifOrientation.TopRight => 1,
        ExifOrientation.BottomLeft => 2,
        ExifOrientation.BottomRight => 3,
        ExifOrientation.LeftTop => 4,
        ExifOrientation.RightTop => 5,
        ExifOrientation.LeftBottom => 6,
        ExifOrientation.RightBottom => 7,
        _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "The orientation is not valid."),
    };

    /// <summary>
    /// Gets the CICP color description of a pair of transfer characteristic and colorimetric specification that describe a
    /// television signal of ITU-T H.273, or <see langword="null"/>: SMPTE ST 274 and BT.709 (primaries 1, transfer 1),
    /// BT.601 625 lines (5, 6) and 525 lines (6, 6). The samples are R'G'B' in full range (matrix 0) once decoded.
    /// </summary>
    public static CicpColorDescription? ToCicp(DpxTransferCharacteristic transfer, DpxColorimetricSpecification colorimetric) => (transfer, colorimetric) switch
    {
        (DpxTransferCharacteristic.Smpte274 or DpxTransferCharacteristic.Bt709, DpxColorimetricSpecification.Smpte274 or DpxColorimetricSpecification.Bt709) => new CicpColorDescription(1, 1, 0, fullRange: true),
        (DpxTransferCharacteristic.Bt601System625, DpxColorimetricSpecification.Bt601System625) => new CicpColorDescription(5, 6, 0, fullRange: true),
        (DpxTransferCharacteristic.Bt601System525, DpxColorimetricSpecification.Bt601System525) => new CicpColorDescription(6, 6, 0, fullRange: true),
        _ => null,
    };

    /// <summary>
    /// Gets the transfer characteristic and the colorimetric specification the encoder writes for a CICP description of
    /// RGB samples, or <see langword="null"/> when DPX has no codes for it (the inverse of <see cref="ToCicp"/>, BT.709
    /// written as <see cref="DpxTransferCharacteristic.Bt709"/>).
    /// </summary>
    public static (DpxTransferCharacteristic Transfer, DpxColorimetricSpecification Colorimetric)? FromCicp(CicpColorDescription cicp)
    {
        if (cicp.MatrixCoefficients != 0 || !cicp.FullRange)
            return null;

        return (cicp.ColorPrimaries, cicp.TransferCharacteristics) switch
        {
            (1, 1) => (DpxTransferCharacteristic.Bt709, DpxColorimetricSpecification.Bt709),
            (5, 6) => (DpxTransferCharacteristic.Bt601System625, DpxColorimetricSpecification.Bt601System625),
            (6, 6) => (DpxTransferCharacteristic.Bt601System525, DpxColorimetricSpecification.Bt601System525),
            _ => null,
        };
    }
}
