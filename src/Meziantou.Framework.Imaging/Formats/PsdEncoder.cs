namespace Meziantou.Framework.Imaging.Formats;

/// <summary>
/// Immutable settings for Adobe Photoshop encoding (Adobe Photoshop File Formats Specification): a flat document whose
/// composite image is the image.
/// </summary>
/// <remarks>
/// <para>
/// Gray pixel formats are written as a grayscale document, <see cref="Cmyk32"/> and <see cref="Cmyk64"/> pixels as a CMYK
/// document (its samples stored inverted, the largest value being no ink, as the format stores them) and the others as an
/// RGB document. 8-bit pixel formats are written with 8-bit samples, 16-bit ones with 16-bit samples, and floating-point
/// ones (<see cref="RgbaFloat"/>, <see cref="RgbaHalf"/> exactly, through single precision) as a 32-bit document, whose
/// samples are linear light: such an image must be labeled <see cref="Metadata.ColorTransferFunction.Linear"/> (the default of
/// floating-point images) or have an ICC profile.
/// </para>
/// <para>
/// An image that has a pixel that is not fully opaque gets a transparency channel, the "merged alpha" of the composite, and
/// one layer that holds the image (named <c>Layer 0</c>, covering the canvas): a layer count is the only place where the
/// format says that the channel after the colors is the transparency of the composite, and Photoshop shows the
/// transparency of a document through its layers. As Photoshop does, the colors of the composite are stored blended over
/// white (<c>c × α + white × (1 − α)</c>), which readers of the composite undo, and the layer holds the colors as they are:
/// the colors of the composite of partly transparent 8 and 16-bit pixels come back rounded. Opaque images are written
/// without layer and without transparency channel, as the flat documents of Photoshop (its "Background").
/// </para>
/// <para>
/// The file is a PSD file (version 1), or a PSB file (version 2, the large document format) when a side of the image is
/// longer than 30,000 pixels or <see cref="LargeDocument"/> is set; a PSB file stores at most 300,000 pixels per side. The
/// ICC profile, the EXIF profile (which carries an orientation other than <see cref="Metadata.ExifOrientation.TopLeft"/>),
/// the XMP packet and the resolution are written as image resources (1039, 1058, 1060 and 1005), with the version
/// information (1057) that says the composite is the real image. Text entries, another CICP description than one equivalent
/// to sRGB and the other metadata the format cannot store follow <see cref="ImageEncoder.MetadataHandling"/> (rejected by
/// default).
/// </para>
/// <para>
/// A PSD file stores exactly one image: animated images and images with a poster frame are rejected with an
/// <see cref="UnsupportedImageFeatureException"/>; use <see cref="ImageFrame.Save(string, ImageEncoder?)"/> to save one frame
/// explicitly.
/// </para>
/// </remarks>
public sealed class PsdEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Psd;

    /// <summary>Gets the compression of the image data. Defaults to <see cref="PsdCompression.RunLength"/>.</summary>
    /// <remarks>
    /// The PackBits rows of a PSD file record their length in 16 bits: a row that compresses to more than 65,535 bytes (rows
    /// of 32-bit samples of more than about 16,000 pixels) requires <see cref="PsdCompression.None"/> or
    /// <see cref="LargeDocument"/>, and is rejected with an <see cref="UnsupportedImageFeatureException"/> otherwise.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="PsdCompression"/>.</exception>
    public PsdCompression Compression
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The compression is not valid.");

            field = value;
        }
    } = PsdCompression.RunLength;

    /// <summary>
    /// Gets a value indicating whether the file is written in the large document format (a PSB file) even when the image
    /// fits a PSD file. Defaults to <see langword="false"/>: a PSB file is written only for an image with a side longer
    /// than 30,000 pixels. The extension <c>.psb</c> selects it.
    /// </summary>
    public bool LargeDocument { get; init; }

    /// <summary>Gets a value indicating whether floating-point samples are stored without reduction to integer pixels: always, as 32-bit samples.</summary>
    internal override bool StoresFloatingPointSamples => true;

    /// <summary>Gets a value indicating whether CMYK pixels are stored as they are: always, as a CMYK document.</summary>
    internal override bool StoresCmykPixels => true;

    /// <summary>Gray with alpha and 16-bit RGB pixels are written as grayscale and RGB documents, not widened to RGBA.</summary>
    internal override bool StoresPixelFormat(PixelFormat format) => format is PixelFormat.La16 or PixelFormat.La32 or PixelFormat.Rgb48 || base.StoresPixelFormat(format);
}
