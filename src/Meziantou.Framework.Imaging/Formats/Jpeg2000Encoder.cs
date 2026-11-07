namespace Meziantou.Framework.Imaging.Formats;

/// <summary>
/// Immutable JPEG 2000 encoder settings (ISO/IEC 15444-1). By default the output is a JP2 file compressed without loss:
/// every pixel format is stored as it is, 16-bit samples and the color of fully transparent pixels included.
/// </summary>
/// <remarks>
/// <para>
/// Gray pixel formats are written as one component, the other formats as red, green and blue components with the
/// multiple component transformation, and an opacity component when the pixel format has an alpha channel. The layout
/// follows the pixel format, never the pixels. By default the image is one tile with one quality layer in the
/// <see cref="Jpeg2000ProgressionOrder.Lrcp"/> order and up to five decomposition levels; <see cref="TileWidth"/>,
/// <see cref="TileHeight"/>, <see cref="QualityLayers"/>, <see cref="ProgressionOrder"/> and
/// <see cref="DecompositionLevels"/> change them. The code-blocks have 64 by 64 coefficients and are coded with the block
/// coder of ISO/IEC 15444-1, or with the high-throughput block coder of ISO/IEC 15444-15 (HTJ2K) with
/// <see cref="BlockCoder"/>.
/// </para>
/// <para>
/// A JP2 file stores the ICC profile, the resolution, the EXIF data (which carries the orientation) and the XMP packet.
/// The ICC profile must be a grayscale profile or a matrix-based RGB profile, the only ones a JP2 file allows; any other
/// profile follows <see cref="ImageEncoder.MetadataHandling"/>. A codestream stores no metadata at all.
/// </para>
/// <para>
/// With <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> above 1 (the configuration of the saved image), an image
/// of at least 65,536 pixels is transformed and its code-blocks are coded by up to that many workers; the bytes are those
/// of one worker.
/// </para>
/// </remarks>
public sealed class Jpeg2000Encoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Jpeg2000;

    /// <summary>Gets whether the image is compressed without loss. Defaults to <see cref="Jpeg2000Compression.Lossless"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Jpeg2000Compression"/>.</exception>
    public Jpeg2000Compression Compression
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The compression is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the quality of a <see cref="Jpeg2000Compression.Lossy"/> output, from 1 (smallest output) to 100 (best quality).
    /// Defaults to 90. It is ignored by a lossless output.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The quality sets the quantization step sizes of the sub-bands; there is no rate control, so the size of the output
    /// follows from the image. With the scale of <see cref="JpegEncoder.Quality"/>, <c>s = 5000 / Quality</c> below 50 and
    /// <c>s = 200 - 2 * Quality</c> otherwise (at least 1), the error that the quantization of a sub-band of the luminance
    /// (or of the gray levels) adds to the image has a step of <c>0.47 * s^(3/4)</c> levels of an 8-bit sample,
    /// proportionally more for deeper samples; the two chrominances are quantized twice as coarsely.
    /// </para>
    /// <para>
    /// The values are calibrated on <see cref="JpegEncoder"/>: on photographs, a quality gives the structural similarity
    /// (SSIM) of a JPEG file of the same quality, with chrominances that are not worse than its sub-sampled ones, in a file
    /// of about two thirds of the size (half of it at the quality 20). 100 is the finest quantization, still lossy.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 100.</exception>
    public int Quality
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
    /// Gets the quality layers of the output, in order. Defaults to an empty list: one layer with every coding pass, which
    /// is <c>[Jpeg2000QualityLayer.Complete]</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each layer adds coding passes to the previous ones, so that a reader that stops after a layer gets the best image
    /// of that size: with the <see cref="Jpeg2000ProgressionOrder.Lrcp"/> order, the bytes of the first layers are the
    /// start of the output. A target of a size or of a number of bits per pixel is reached by truncating the coding
    /// passes of the code-blocks at the points that reduce the squared error of the image most for their bytes (the
    /// post-compression rate-distortion optimization of T.800, J.14); a layer whose target is not above the size of the
    /// previous ones adds nothing. A target only removes coding passes: <see cref="Quality"/> still sets the finest
    /// quantization of a lossy output, and a lossless output whose last layer has a target is not lossless when the
    /// target cuts coding passes.
    /// </para>
    /// <para>
    /// The coded bytes of every code-block are kept until the layers are chosen, as with one layer; choosing the layers
    /// writes the packet headers a few dozen times. <see cref="Jpeg2000QualityLayer.Complete"/> is only allowed last.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>, or holds <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The value has more than 65,535 layers, or <see cref="Jpeg2000QualityLayer.Complete"/> before its last layer.</exception>
    public IReadOnlyList<Jpeg2000QualityLayer> QualityLayers
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            Jpeg2000QualityLayer[] layers = [.. value];
            if (layers.Length > ushort.MaxValue)
                throw new ArgumentException("A JPEG 2000 output has at most 65535 quality layers.", nameof(value));

            for (var i = 0; i < layers.Length; i++)
            {
                if (layers[i] is null)
                    throw new ArgumentNullException(nameof(value), "A quality layer is null.");

                if (layers[i].IsComplete && i != layers.Length - 1)
                    throw new ArgumentException("Only the last quality layer can be Jpeg2000QualityLayer.Complete.", nameof(value));
            }

            field = layers;
        }
    } = [];

    /// <summary>Gets the order of the packets of each tile. Defaults to <see cref="Jpeg2000ProgressionOrder.Lrcp"/>.</summary>
    /// <remarks>
    /// Without precincts, a resolution level of a tile-component is one position: <see cref="Jpeg2000ProgressionOrder.Pcrl"/>
    /// and <see cref="Jpeg2000ProgressionOrder.Cprl"/> then give the same packets, component by component, and
    /// <see cref="Jpeg2000ProgressionOrder.Rpcl"/> the packets of <see cref="Jpeg2000ProgressionOrder.Rlcp"/> with the layers of
    /// a component together.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Jpeg2000ProgressionOrder"/>.</exception>
    public Jpeg2000ProgressionOrder ProgressionOrder
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The progression order is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the width of a tile, in pixels, or 0 (the default) for tiles as wide as the image. The tiles of the last column
    /// are narrower when the width of the image is not a multiple of it.
    /// </summary>
    /// <remarks>
    /// Each tile is transformed and coded on its own, so that the encoder holds the coefficients of one tile at a time
    /// instead of those of the whole image: a large image is coded with tiles of 512 to 2048 pixels without holding its
    /// transformation. A reader can also decode one tile alone. Small tiles cost bytes and quality at their edges. An
    /// image has at most 65,535 tiles.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int TileWidth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets the height of a tile, in pixels, or 0 (the default) for tiles as high as the image. The tiles of the last row
    /// are lower when the height of the image is not a multiple of it. See <see cref="TileWidth"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int TileHeight
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets the largest number of decomposition levels of the wavelet transformation, from 0 to 32. Defaults to 5. A tile
    /// gets fewer levels when its smaller side is shorter than <c>2^levels</c> pixels, so that its lowest resolution level
    /// is not empty; the tiles of the last row or column do not reduce it.
    /// </summary>
    /// <remarks>
    /// Each level halves the size of the lowest resolution: more levels compress large smooth images slightly better and
    /// let a reader decode smaller thumbnails (a decode at <c>1/2^n</c> of the size discards <c>n</c> levels).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 32.</exception>
    public int DecompositionLevels
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 32);
            field = value;
        }
    } = 5;

    /// <summary>
    /// Gets the coder of the code-blocks. Defaults to <see cref="Jpeg2000BlockCoder.Standard"/>, the block coder of
    /// ISO/IEC 15444-1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Jpeg2000BlockCoder.HighThroughput"/> codes each code-block with one HT cleanup pass of ISO/IEC 15444-15
    /// (no HT refinement pass), and declares it in the codestream (the CAP marker segment, the code-block style); a
    /// <see cref="Jpeg2000Container.Jp2"/> output is then a JPH file, of the brand <c>jph </c>. The pass codes the same
    /// quantized coefficients as the standard coder: a lossless output is the image, and a lossy output decodes to the
    /// samples of the standard coder at the same <see cref="Quality"/>.
    /// </para>
    /// <para>
    /// The pass has no fractional bit-planes and cannot be split between layers: the output has one quality layer, and
    /// <see cref="QualityLayers"/> may only hold one layer. Its target is reached by choosing, for each code-block, the
    /// lowest bit-plane its pass codes (each candidate is coded, at the same rate-distortion slopes as the coding passes
    /// of the standard coder), so that a target is reached in steps of whole bit-planes of the code-blocks.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Jpeg2000BlockCoder"/>.</exception>
    public Jpeg2000BlockCoder BlockCoder
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The block coder is not valid.");

            field = value;
        }
    }

    /// <summary>Gets whether the output is a JP2 file or a codestream alone. Defaults to <see cref="Jpeg2000Container.Jp2"/>.</summary>
    /// <remarks>With the <see cref="Jpeg2000BlockCoder.HighThroughput"/> block coder, the file is a JPH file.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Jpeg2000Container"/>.</exception>
    public Jpeg2000Container Container
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The container is not valid.");

            field = value;
        }
    }
}
