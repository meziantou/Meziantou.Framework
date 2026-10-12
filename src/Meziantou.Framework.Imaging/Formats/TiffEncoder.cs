namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for TIFF and BigTIFF encoding.</summary>
/// <remarks>
/// <para>
/// The container is the part of TIFF 6.0 that any reader accepts: little-endian by default, chunky samples, top-down
/// strips, no predictor. <see cref="Compression"/> selects no compression, Deflate (the default), LZW, PackBits, the
/// CCITT facsimile compressions or JPEG. Tiles and planar storage are never written.
/// </para>
/// <para>
/// By default (<see cref="TiffPixelLayout.Auto"/>) the sample layout follows the pixel format of the image, losslessly:
/// <c>Gray8</c>/<c>Gray16</c> become 8- or 16-bit <c>BlackIsZero</c> grayscale, <c>Rgb24</c> becomes 8-bit RGB, and the
/// formats with alpha become RGBA with <c>ExtraSamples = 2</c> (unassociated, straight alpha). Nothing is quantized and
/// alpha is never dropped. <see cref="PixelLayout"/> also selects a bilevel layout (one bit per pixel) and a palette
/// layout, which store the image exactly or reject it: this encoder has no background color, no bit-depth-reduction
/// switch and no color reduction. The only output that is not exact is the one of <see cref="TiffCompression.Jpeg"/>,
/// which is lossy by nature and selected explicitly. CMYK data is never written.
/// </para>
/// <para>
/// <strong>The destination must be seekable.</strong> A TIFF directory stores the offsets of the strips it describes, so
/// it can only be written once their compressed sizes are known; the encoder writes the pixel data first and then patches
/// the header and the directory pointers. A non-seekable stream is rejected before anything is written. File destinations
/// are always seekable.
/// </para>
/// <para>
/// One <see cref="Image.Save(string, ImageEncoder?)"/> writes a one-page document; use
/// <see cref="ImageCollection.Save(string, ImageEncoder?)"/> to write several pages, whose sizes, pixel formats and
/// metadata may all differ. Animated images are rejected: the pages of a document are not animation frames.
/// </para>
/// <para>
/// Metadata (subject to <see cref="ImageEncoder.MetadataHandling"/>): the resolution
/// (<c>XResolution</c>/<c>YResolution</c>/<c>ResolutionUnit</c>), the orientation (<c>Orientation</c>), the ICC profile
/// (<c>InterColorProfile</c>) and the XMP packet (<c>XMP</c>). An EXIF block and text entries are not stored (rejected by
/// default). A profile describes the written samples: a bilevel page is grayscale and a palette page is RGB, whatever
/// the pixel format of the image, so a profile of the other color space is metadata the page cannot store.
/// </para>
/// </remarks>
public sealed class TiffEncoder : ImageEncoder
{
    /// <summary>The default number of uncompressed bytes the encoder aims for in one strip (64 KiB).</summary>
    public const int DefaultStripBytes = 64 * 1024;

    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Tiff;

    /// <summary>Gets the compression of the strips. Defaults to <see cref="TiffCompression.Deflate"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="TiffCompression"/>.</exception>
    public TiffCompression Compression
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The compression is not valid.");

            field = value;
        }
    } = TiffCompression.Deflate;

    /// <summary>Gets the sample layout. Defaults to <see cref="TiffPixelLayout.Auto"/>.</summary>
    /// <remarks>
    /// <see cref="TiffCompression.CcittModifiedHuffman"/>, <see cref="TiffCompression.CcittGroup3"/> and
    /// <see cref="TiffCompression.CcittGroup4"/> require <see cref="TiffPixelLayout.Bilevel"/>, and
    /// <see cref="TiffCompression.Jpeg"/> requires <see cref="TiffPixelLayout.Auto"/>; saving fails with an
    /// <see cref="ArgumentException"/> otherwise.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="TiffPixelLayout"/>.</exception>
    public TiffPixelLayout PixelLayout
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The pixel layout is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the quality of <see cref="TiffCompression.Jpeg"/>, from 1 (smallest output) to 100 (best quality), with the
    /// meaning of <see cref="JpegEncoder.Quality"/>. Defaults to 90. Ignored by the other compressions.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 100.</exception>
    public int JpegQuality
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
            field = value;
        }
    } = 90;

    /// <summary>
    /// Gets the chroma subsampling of <see cref="TiffCompression.Jpeg"/> for the color pixel formats, with the meaning of
    /// <see cref="JpegEncoder.ChromaSubsampling"/>. Defaults to <see cref="JpegChromaSubsampling.Auto"/> (4:2:0). Ignored
    /// by the other compressions and by the gray pixel formats.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="JpegChromaSubsampling"/>.</exception>
    public JpegChromaSubsampling JpegChromaSubsampling
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The chroma subsampling is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether to write a BigTIFF (magic 43, 64-bit offsets) instead of a classic TIFF. Defaults to
    /// <see langword="false"/>: a classic TIFF is accepted by far more readers, and nothing this encoder writes needs
    /// 64-bit offsets below 4 GiB.
    /// </summary>
    public bool BigTiff { get; init; }

    /// <summary>
    /// Gets a value indicating whether to write big-endian (<c>MM</c>) instead of little-endian (<c>II</c>) files.
    /// Defaults to <see langword="false"/>. Both byte orders are equally valid TIFF; this only changes the bytes, never
    /// the pixels.
    /// </summary>
    public bool BigEndian { get; init; }

    /// <summary>
    /// Gets the number of rows of one strip, or 0 (the default) to choose a number of rows whose uncompressed size is
    /// close to <see cref="DefaultStripBytes"/>, so that encoding one page needs a bounded amount of memory whatever the
    /// size of the page. Ignored by <see cref="TiffCompression.Jpeg"/>, which writes each page as one strip.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int RowsPerStrip
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }
}
