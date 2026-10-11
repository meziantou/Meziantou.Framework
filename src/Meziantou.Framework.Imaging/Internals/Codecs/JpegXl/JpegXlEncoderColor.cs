using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>How the lossless encoder describes the colors of an image: an ICC profile written after the image header, or an enumerated color encoding.</summary>
/// <param name="HasIccProfile">Whether an ICC profile describes the colors.</param>
/// <param name="WhitePoint">The white point of an enumerated encoding.</param>
/// <param name="Primaries">The primaries of an enumerated encoding.</param>
/// <param name="TransferFunction">The transfer function of an enumerated encoding.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct JpegXlEncoderColor(bool HasIccProfile, int WhitePoint, int Primaries, int TransferFunction)
{
    /// <summary>Gets the sRGB encoding.</summary>
    public static JpegXlEncoderColor Srgb => new(HasIccProfile: false, JpegXlColorEncoding.WhitePointD65, JpegXlColorEncoding.PrimariesSrgb, JpegXlColorEncoding.TransferSrgb);

    /// <summary>Gets the encoding of an image described by an ICC profile.</summary>
    public static JpegXlEncoderColor IccProfile => new(HasIccProfile: true, JpegXlColorEncoding.WhitePointD65, JpegXlColorEncoding.PrimariesSrgb, JpegXlColorEncoding.TransferSrgb);

    /// <summary>Gets the sRGB primaries and white point with a linear transfer function.</summary>
    public static JpegXlEncoderColor LinearSrgb => new(HasIccProfile: false, JpegXlColorEncoding.WhitePointD65, JpegXlColorEncoding.PrimariesSrgb, JpegXlColorEncoding.TransferLinear);

    /// <summary>Gets a value indicating whether the encoding is sRGB.</summary>
    public bool IsSrgb => this == Srgb;

    /// <summary>Gets the enumerated encoding of a CICP description, or <see langword="null"/> when the format has no value for its code points.</summary>
    public static JpegXlEncoderColor? FromCicp(CicpColorDescription cicp)
    {
        ArgumentNullException.ThrowIfNull(cicp);
        (int Primaries, int WhitePoint)? primaries = cicp.ColorPrimaries switch
        {
            1 => (JpegXlColorEncoding.PrimariesSrgb, JpegXlColorEncoding.WhitePointD65),
            9 => (JpegXlColorEncoding.PrimariesRec2100, JpegXlColorEncoding.WhitePointD65),
            11 => (JpegXlColorEncoding.PrimariesP3, JpegXlColorEncoding.WhitePointDci),
            12 => (JpegXlColorEncoding.PrimariesP3, JpegXlColorEncoding.WhitePointD65),
            _ => null,
        };

        if (primaries is not { } value || cicp.TransferCharacteristics is not (1 or 8 or 13 or 16 or 17 or 18))
            return null;

        return new JpegXlEncoderColor(HasIccProfile: false, value.WhitePoint, value.Primaries, cicp.TransferCharacteristics);
    }
}
