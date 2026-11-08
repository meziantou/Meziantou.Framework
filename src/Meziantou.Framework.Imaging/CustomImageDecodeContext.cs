using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// The context of one call to <see cref="CustomImageCodec.Decode"/> or <see cref="CustomImageCodec.Identify"/>: the
/// configuration and the request of the operation, and the factory of the decoded images.
/// </summary>
/// <remarks>
/// The context is only valid during the call that received it. The images it created and the codec does not return are
/// disposed when the call returns, and all of them when it throws.
/// </remarks>
public sealed class CustomImageDecodeContext
{
    private readonly ImageCodecContext _context;
    private readonly CustomImageCodec _codec;
    private readonly List<Image> _images = [];
    private bool _completed;

    internal CustomImageDecodeContext(CustomImageCodec codec, ImageCodecContext context, ImageDecodeRequest? request, ImageIdentifyMode? identifyMode)
    {
        _codec = codec;
        _context = context;
        RequestedPixelFormat = request?.PixelFormat;
        FrameLimit = request?.FrameLimit;
        IdentifyMode = identifyMode;
    }

    /// <summary>Gets the configuration of the operation: its resource limits and its parallelism.</summary>
    public ImageConfiguration Configuration => _context.Configuration;

    /// <summary>Gets the token of the operation. Check it between rows or frames of a long decode.</summary>
    public CancellationToken CancellationToken => _context.CancellationToken;

    /// <summary>
    /// Gets the pixel format of a typed load (<see cref="Image.Load{TPixel}(Stream, ImageDecodeOptions?)"/>), or
    /// <see langword="null"/>. The library converts the decoded image when its format differs: a codec may produce this
    /// format directly to save the conversion.
    /// </summary>
    public PixelFormat? RequestedPixelFormat { get; }

    /// <summary>
    /// Gets the maximum number of displayed frames the load keeps (<see cref="ImageDecodeOptions.FrameLimit"/>), or
    /// <see langword="null"/> for all of them. The library removes the frames beyond it: a codec may stop decoding there.
    /// </summary>
    public int? FrameLimit { get; }

    /// <summary>Gets the mode of an identification, or <see langword="null"/> when the call decodes the image.</summary>
    public ImageIdentifyMode? IdentifyMode { get; }

    /// <summary>Creates a single-frame still image whose pixels are all zero, charged to the operation.</summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="width">The width. Must be positive.</param>
    /// <param name="height">The height. Must be positive.</param>
    /// <returns>The image. Add frames with <see cref="Image{TPixel}.AppendFrame()"/>.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a limit of <see cref="Configuration"/>.</exception>
    /// <exception cref="InvalidOperationException">The call that received the context has returned.</exception>
    public Image<TPixel> CreateImage<TPixel>(int width, int height)
        where TPixel : unmanaged
        => (Image<TPixel>)CreateImage(PixelFormats.GetPixelFormat<TPixel>(), width, height);

    /// <summary>Creates a single-frame still image of a pixel format whose pixels are all zero, charged to the operation.</summary>
    /// <param name="pixelFormat">The pixel format.</param>
    /// <param name="width">The width. Must be positive.</param>
    /// <param name="height">The height. Must be positive.</param>
    /// <returns>The image, an <see cref="Image{TPixel}"/> of the pixel type of <paramref name="pixelFormat"/>.</returns>
    /// <remarks>
    /// The image has the configuration of the operation, and its pixels count toward the
    /// <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/> budget of the load. An image of a floating-point pixel
    /// format is labeled as linear light (<see cref="ColorTransferFunction.Linear"/>), like a new image.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pixelFormat"/> is not defined, or <paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a limit of <see cref="Configuration"/>.</exception>
    /// <exception cref="InvalidOperationException">The call that received the context has returned.</exception>
    public Image CreateImage(PixelFormat pixelFormat, int width, int height)
    {
        if (!Enum.IsDefined(pixelFormat))
            throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, "The pixel format is not valid.");

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ThrowIfCompleted();
        _context.CancellationToken.ThrowIfCancellationRequested();
        Configuration.Limits.EnsureCanvasWithinLimits(width, height);
        var size = new Size(width, height);
        _context.Tracker.ChargeFrame(size);
        var image = Image.CreateForDecoder(pixelFormat, Configuration, size, _context.Scope);
        if (PixelFormats.IsFloatingPoint(pixelFormat))
        {
            image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        }

        _images.Add(image);
        return image;
    }

    /// <summary>Ends the call: validates the image the codec returned and disposes the other images it created.</summary>
    /// <param name="result">The image returned by the codec, or <see langword="null"/> when the call did not return one (identification, failure).</param>
    /// <param name="expectsImage"><see langword="true"/> when <paramref name="result"/> must be an image of this context.</param>
    /// <exception cref="InvalidOperationException">The codec returned no image, an image of another origin, or a disposed image.</exception>
    internal void Complete(Image? result, bool expectsImage)
    {
        _completed = true;
        Image? kept = null;
        try
        {
            if (expectsImage)
            {
                if (result is null)
                    throw new InvalidOperationException($"The {_codec.FormatInfo.Name} codec returned no image.");

                if (!_images.Exists(image => ReferenceEquals(image, result)))
                    throw new InvalidOperationException($"The {_codec.FormatInfo.Name} codec returned an image that it did not create with CustomImageDecodeContext.CreateImage.");

                result.ThrowIfDisposed();
                kept = result;
            }
        }
        finally
        {
            foreach (var image in _images)
            {
                if (!ReferenceEquals(image, kept))
                {
                    image.Dispose();
                }
            }

            _images.Clear();
        }
    }

    private void ThrowIfCompleted()
    {
        if (_completed)
            throw new InvalidOperationException("The decode context is only valid during the call that received it.");
    }
}
