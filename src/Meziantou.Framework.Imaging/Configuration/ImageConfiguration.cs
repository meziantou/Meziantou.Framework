namespace Meziantou.Framework.Imaging;

/// <summary>Immutable configuration shared by an image and the operations performed on it.</summary>
/// <remarks>
/// An image captures the configuration it was created or loaded with; operations on the image use that snapshot.
/// The configuration is immutable, so a single instance can be shared freely across threads.
/// </remarks>
public sealed class ImageConfiguration
{
    /// <summary>Gets the default configuration: <see cref="ImageResourceLimits.Default"/> limits and one worker.</summary>
    public static ImageConfiguration Default { get; } = new();

    /// <summary>Gets the resource limits. Defaults to <see cref="ImageResourceLimits.Default"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public ImageResourceLimits Limits
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = ImageResourceLimits.Default;

    /// <summary>
    /// Gets how the decoders treat files that deviate from their specification. Defaults to
    /// <see cref="DecodingStrictness.Strict"/>, which rejects them with an <see cref="InvalidImageContentException"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="DecodingStrictness.Lenient"/> accepts the deviations that the reference decoders and the browsers accept,
    /// in identification, eager loads, sequential readers and collections alike. The resource limits are the same in both
    /// modes. Encoders and the other operations ignore the value.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Imaging.DecodingStrictness"/>.</exception>
    public DecodingStrictness DecodingStrictness
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The decoding strictness is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the options of the OpenEXR decoder: the layer it decodes and the window the image covers. Defaults to
    /// <see cref="OpenExrDecodingOptions.Default"/>, which rejects the files with channels or pixels outside the image.
    /// </summary>
    /// <remarks>Identification, eager loads, sequential readers and collections read them alike. The other formats ignore them.</remarks>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public OpenExrDecodingOptions OpenExrDecoding
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = OpenExrDecodingOptions.Default;

    /// <summary>
    /// Gets the options of the JPEG 2000 decoder: the quality layers it decodes. Defaults to
    /// <see cref="Jpeg2000DecodingOptions.Default"/>, which decodes every layer.
    /// </summary>
    /// <remarks>Eager loads, sequential readers and collections read them alike. The other formats ignore them.</remarks>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public Jpeg2000DecodingOptions Jpeg2000Decoding
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = Jpeg2000DecodingOptions.Default;

    /// <summary>
    /// Gets the codecs of the formats the library does not read or write itself, consulted before the built-in ones. Defaults
    /// to an empty list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The codecs are used by the entry points that receive this configuration: <see cref="Image.Identify(Stream, ImageIdentifyOptions?)"/>,
    /// <see cref="Image.Load(Stream, ImageDecodeOptions?)"/>, their overloads and asynchronous variants, and
    /// <see cref="Image.DetectFormatInfo(ReadOnlySpan{byte}, ImageConfiguration?)"/>; <see cref="Image.Save(string, Formats.ImageEncoder?)"/> selects
    /// their encoders from the file extension (<see cref="CustomImageCodec.CreateEncoder"/>) when the image has this
    /// configuration. A file is read by the first codec, in this order, whose <see cref="CustomImageCodec.IsMatch"/> accepts
    /// its first bytes; the built-in codecs are consulted after them, so that a registered codec also takes the files of a
    /// built-in format it recognizes. Sequential readers (<see cref="ImageReader{TPixel}"/>) reject the files of a custom
    /// codec with an <see cref="UnsupportedImageFeatureException"/>, and <see cref="ImageCollection"/> and
    /// <see cref="AuxiliaryImageCollection"/> only read the built-in formats.
    /// </para>
    /// <para>The setter stores a copy of the list.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">The value or one of its items is <see langword="null"/>.</exception>
    public IReadOnlyList<CustomImageCodec> Codecs
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            CustomImageCodec[] codecs = [.. value];
            foreach (var codec in codecs)
            {
                ArgumentNullException.ThrowIfNull(codec, nameof(value));
            }

            field = Array.AsReadOnly(codecs);
            CodecRegistry = codecs.Length == 0 ? null : new Internals.ImageCodecRegistry([.. codecs.Select(codec => new Internals.CustomImageCodecAdapter(codec)), .. Internals.ImageCodecRegistry.Default.Codecs]);
        }
    } = [];

    /// <summary>Gets the registry of the custom codecs followed by the built-in ones, or <see langword="null"/> without custom codecs.</summary>
    internal Internals.ImageCodecRegistry? CodecRegistry { get; private set; }

    /// <summary>Gets the environment of the fonts loaded with this configuration.</summary>
    internal Internals.ImageFontEnvironment FontEnvironment => field ??= new Internals.ImageFontEnvironment(this);

    /// <summary>
    /// Gets the maximum number of workers an internal operation may use concurrently. Defaults to 1, meaning operations run on
    /// the calling thread only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Currently, <see cref="ImageProcessingExtensions.Resize"/>, the transformations,
    /// <see cref="ImageProcessingExtensions.Convolve(Image, ConvolutionOptions, CancellationToken)"/>, the blurs
    /// (<see cref="ImageProcessingExtensions.GaussianBlur(Image, double, ConvolutionEdgeMode, ConvolutionWorkingSpace, CancellationToken)"/>,
    /// <see cref="ImageProcessingExtensions.BoxBlur(Image, int, ConvolutionEdgeMode, ConvolutionWorkingSpace, CancellationToken)"/>,
    /// <see cref="ImageProcessingExtensions.MedianBlur(Image, int, ConvolutionEdgeMode, CancellationToken)"/>),
    /// <see cref="ImageProcessingExtensions.Sharpen(Image, double, double, double, ConvolutionEdgeMode, ConvolutionWorkingSpace, CancellationToken)"/> and
    /// <see cref="ImageProcessingExtensions.DetectEdges(Image, EdgeDetector, bool, ConvolutionEdgeMode, CancellationToken)"/> use it: the output rows
    /// of a large frame (at least 65,536 output pixels and 32 rows per worker; nearest-neighbor resizing excepted) are split
    /// into at most this many bands processed concurrently, the calling thread included. The result is identical whatever
    /// the value. Each worker needs its own row scratch, charged to the image's
    /// <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/> budget. The
    /// <see cref="ImageProcessingExtensions.Pixelate(Image, int, CancellationToken)"/>,
    /// <see cref="ImageProcessingExtensions.OilPaint(Image, int, int, CancellationToken)"/> and
    /// <see cref="ImageProcessingExtensions.Flatten(Image, Rgba64, CancellationToken)"/> effects split the rows the same way
    /// (the bands of the pixelation start on a row of blocks, and those of the oil painting are at least as tall as its
    /// window). The comparisons of <see cref="ImageSimilarity"/> (SSIM, PSNR, SSIMULACRA 2 and Butteraugli) use the value of
    /// the image of the expected frame: their floating-point sums are made by bands of 64 rows, added in order, so their
    /// results do not depend on it either. The HEIC and AVIF decoders use it too: the tiles of a
    /// grid of at least 65,536 samples are decoded by up to this many workers, each holding the decoder and the picture of its
    /// tile, with the pixels and the errors of a decoding by one worker. The HEIC and AVIF encoders code the tiles of such a
    /// grid (<see cref="Formats.HeicEncoder.GridTileSize"/>, <see cref="Formats.AvifEncoder.GridTileSize"/>) by up to this
    /// many workers, each with encoders of its own, and write the same bytes as one worker. The AVIF decoder also runs the
    /// post-processing filters of an AV1 frame of at least 65,536 pixels (the loop filter, CDEF, the super-resolution
    /// upscaling and the loop restoration) by bands of rows, with the pixels of a decoding by one worker, unless the frame is
    /// a tile decoded alongside other tiles. They take about 25% to 40% of the decoding time of a 3840x2160 image that uses
    /// CDEF, which then decodes about 1.3 to 1.45 times faster with 8 workers. Each worker needs its own rows, about 140
    /// bytes per pixel of the width of the frame, charged to <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/>. The
    /// JPEG XL encoder, the JPEG XL decoder and <see cref="JpegXlTranscoder"/> use it as well: the groups of a frame of at least
    /// 65,536 pixels (squares of 256x256 pixels, which the format codes independently) are encoded or decoded by up to this
    /// many workers, each with its own working memory, and the restoration filters and the conversion of a decoded frame to
    /// its color space are split into row bands; the bytes, the pixels and the errors are those of one worker. The codecs of
    /// the other formats split the independent parts of a frame of at least 65,536 pixels the same way, with the bytes, the
    /// pixels and the errors of one worker: the JPEG decoder and encoder transform batches of MCU rows (the entropy coding
    /// stays on the calling thread), the TIFF decoder decompresses the LZW, Deflate and CCITT strips and tiles and the TIFF
    /// encoder compresses the LZW and Deflate strips, the OpenEXR decoder and encoder process the chunks, the JPEG 2000
    /// decoder and encoder code the code-blocks and split the wavelet transforms, the AVIF decoder and encoder code the tiles
    /// of an AV1 frame that has several (unless the frame is a grid tile coded alongside other tiles), and the HEIC decoder
    /// decodes the tiles and the rows of the wavefront of a picture that start at entry points. Drawing uses it too: the
    /// rows of a shape, an image or a layer that may cover at least 131,072 pixels are composited by up to this many
    /// workers, by batches of rows, while the calling thread computes the coverages of the next rows; the pixels and the
    /// errors are those of one worker. Each worker needs about 128 KB and 40 bytes per pixel of the width of the canvas,
    /// charged to <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/>. The other decoders (the HEVC in-loop filters
    /// included), the encoders and the other operations run on the calling thread.
    /// </para>
    /// <para>
    /// Keep the default in services that already process several images concurrently: the bound applies per operation, so
    /// <c>n</c> concurrent operations may use up to <c>n</c> times this many threads.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxDegreeOfParallelism
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 1;
}
