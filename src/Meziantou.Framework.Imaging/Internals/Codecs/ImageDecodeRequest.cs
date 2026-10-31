namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What an eager load asks a decoder to produce.</summary>
/// <param name="PixelFormat">The requested pixel format (typed loads), or <see langword="null"/> for the default working representation of the file.</param>
/// <param name="FrameLimit">The maximum number of displayed frames (deliberate prefix selection), or <see langword="null"/> for all frames.</param>
/// <param name="Conversion">The conversion policy applied when the decoded samples are converted to <paramref name="PixelFormat"/>.</param>
internal sealed record ImageDecodeRequest(PixelFormat? PixelFormat, int? FrameLimit, PixelConversionOptions Conversion)
{
    /// <summary>
    /// Gets a value indicating whether the decoded image is a payload embedded in the file being loaded (a frame of an
    /// animated cursor), whose displayed frames the outer decoder charges itself: the frames of this decode are then not
    /// charged to the per-input tracker a second time.
    /// </summary>
    public bool IsEmbeddedPayload { get; init; }

    /// <summary>Gets the color conversion that makes CMYK samples loadable, or <see langword="null"/> to reject them.</summary>
    public CmykConversionOptions? CmykConversion { get; init; }

    /// <summary>Gets the factor by which the width and the height are divided (<see cref="ImageDecodeOptions.ScaleDenominator"/>), a power of two.</summary>
    public int ScaleDenominator { get; init; } = 1;

    /// <summary>Gets the smallest size a reduced-resolution decode may produce (<see cref="ImageDecodeOptions.TargetSize"/>).</summary>
    public Size? TargetSize { get; init; }

    /// <summary>Gets a value indicating whether the request asks for a reduced resolution, exact or not.</summary>
    public bool IsScaled => ScaleDenominator > 1 || TargetSize is not null;

    public static ImageDecodeRequest Create(ImageDecodeOptions options, PixelFormat? pixelFormat)
        => new(pixelFormat, options.FrameLimit, options.Conversion)
        {
            CmykConversion = options.CmykConversion,
            ScaleDenominator = options.ScaleDenominator,
            TargetSize = options.TargetSize,
        };

    public static ImageDecodeRequest Create(ImageReaderOptions options, PixelFormat? pixelFormat)
        => new(pixelFormat, options.FrameLimit, options.Conversion)
        {
            CmykConversion = options.CmykConversion,
            ScaleDenominator = options.ScaleDenominator,
            TargetSize = options.TargetSize,
        };

    /// <summary>
    /// Gets the scale denominator of an image that a decoder can divide by the powers of two up to
    /// <paramref name="maxDenominator"/>: <see cref="ScaleDenominator"/>, or the largest one whose size is still at least
    /// <see cref="TargetSize"/>.
    /// </summary>
    /// <param name="size">The full size of the image.</param>
    /// <param name="maxDenominator">The largest denominator the decoder supports for this image, a power of two (1 when it supports none).</param>
    /// <param name="format">The format, for the error.</param>
    /// <param name="reason">Why the image cannot be divided by a larger denominator, for the error.</param>
    /// <returns>The denominator.</returns>
    /// <exception cref="UnsupportedImageFeatureException"><see cref="ScaleDenominator"/> is larger than <paramref name="maxDenominator"/>.</exception>
    public int GetScaleDenominator(Size size, int maxDenominator, ImageFormat format, string reason)
    {
        if (ScaleDenominator > maxDenominator)
            throw CreateUnsupportedScaleException(format, reason);

        if (TargetSize is not { } target)
            return ScaleDenominator;

        var denominator = 1;
        while (denominator < maxDenominator && GetScaledLength(size.Width, denominator * 2) >= target.Width && GetScaledLength(size.Height, denominator * 2) >= target.Height)
        {
            denominator *= 2;
        }

        return denominator;
    }

    /// <summary>Gets the request given to the decoder of a codec, which may not decode at a reduced resolution.</summary>
    /// <param name="codec">The codec of the input.</param>
    /// <returns>This request.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The request asks for an exact scale and the codec only decodes at full resolution.</exception>
    public ImageDecodeRequest For(ImageCodec codec) => codec.SupportsScaledDecoding ? this : ForFullResolution(codec.Format);

    /// <summary>
    /// Gets the request given to a decoder that only decodes at full resolution: a request for an exact scale is
    /// rejected, and a <see cref="TargetSize"/> is satisfied by the full image.
    /// </summary>
    /// <param name="format">The format of the decoder.</param>
    /// <returns>This request.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The request asks for an exact scale.</exception>
    public ImageDecodeRequest ForFullResolution(ImageFormat format)
    {
        if (ScaleDenominator > 1)
            throw CreateUnsupportedScaleException(format, string.Create(CultureInfo.InvariantCulture, $"{ImageFormatNames.Get(format)} images are only decoded at full resolution"));

        return this;
    }

    /// <summary>Gets <c>ceil(length / denominator)</c>.</summary>
    /// <param name="length">A width or a height.</param>
    /// <param name="denominator">The scale denominator.</param>
    /// <returns>The scaled length.</returns>
    public static int GetScaledLength(int length, int denominator) => (int)(((long)length + denominator - 1) / denominator);

    private UnsupportedImageFeatureException CreateUnsupportedScaleException(ImageFormat format, string reason)
        => new(string.Create(CultureInfo.InvariantCulture, $"The image cannot be decoded at 1/{ScaleDenominator} of its size: {reason}. Set TargetSize instead of ScaleDenominator to decode at the smallest size the image allows."), format, "Scaled decoding");
}
