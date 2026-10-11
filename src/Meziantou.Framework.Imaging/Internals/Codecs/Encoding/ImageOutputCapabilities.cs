using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The container constraints of one output, resolved once per writer or save from the
/// encoder settings and the writer options. Every rule that does not depend on pixel data is enforced here, in shared
/// code, before any byte is written, so that every encoder reports the same errors in the same order:
/// </summary>
/// <remarks>
/// <list type="table">
/// <listheader><term>Output</term><description>Constraints</description></listheader>
/// <item><term>Static PNG</term><description>Known frame count of exactly 1; no animation settings; no poster.</description></item>
/// <item><term>APNG</term><description>Known frame count (acTL precedes the image data; seeking is never required); optional poster written before frame zero.</description></item>
/// <item><term>GIF</term><description>Unknown frame count allowed (local palettes, no global prepass); no poster; at most 65,535 pixels per side; play count up to 65,536.</description></item>
/// <item><term>JPEG</term><description>Exactly one frame; no animation settings; no poster; at most 65,535 pixels per side.</description></item>
/// <item><term>WebP</term><description>Unknown frame count allowed; no poster; at most 16,384 (lossless) or 16,383 (lossy) pixels per side; play count up to 65,535; animations (several or unknown frames, or animation settings) need a seekable destination (the RIFF size is patched at completion).</description></item>
/// <item><term>QOI</term><description>Exactly one frame; no animation settings; no poster; at most 2^31 - 1 pixels per side (32-bit header fields).</description></item>
/// <item><term>BMP</term><description>Exactly one frame; no animation settings; no poster; at most 2^31 - 1 pixels per side, and a file size the 32-bit <c>bfSize</c> field can declare.</description></item>
/// <item><term>TGA</term><description>Exactly one frame; no animation settings; no poster; at most 65,535 pixels per side.</description></item>
/// <item><term>PNM</term><description>Exactly one frame; no animation settings; no poster; at most 2^31 - 1 pixels per side.</description></item>
/// <item><term>TIFF</term><description>Exactly one page (<c>ImageCollection.Save</c> writes several); no animation settings; no poster; a seekable destination (a directory stores the offsets of its strips, so the pointer to it is patched once the page is written).</description></item>
/// <item><term>ICO/CUR</term><description>Exactly one representation (<c>ImageCollection.Save</c> writes several); no animation settings; no poster; at most 256 pixels per side; no seeking (a representation payload is encoded in memory).</description></item>
/// <item><term>ANI</term><description>Always an animation container; unknown frame count allowed; no poster; at most 256 pixels per side; infinite play count only; a seekable destination (the header counts and the RIFF and frame-list sizes are patched at completion).</description></item>
/// </list>
/// <para>
/// <see cref="PngAnimationMode.Auto"/> writes an APNG when more than one frame is expected or animation settings are given
/// (for <c>Save</c>: when the image is animated); otherwise a static PNG.
/// </para>
/// </remarks>
internal sealed class ImageOutputCapabilities
{
    private ImageOutputCapabilities(ImageFormat format, bool isAnimated, bool requiresFrameCount, int? maxFrameCount, bool supportsPosterFrame, int maxDimension, bool requiresSeekableOutput = false)
    {
        RequiresSeekableOutput = requiresSeekableOutput;
        Format = format;
        IsAnimated = isAnimated;
        RequiresFrameCount = requiresFrameCount;
        MaxFrameCount = maxFrameCount;
        SupportsPosterFrame = supportsPosterFrame;
        MaxDimension = maxDimension;
    }

    /// <summary>Gets the output format.</summary>
    public ImageFormat Format { get; }

    /// <summary>
    /// Gets a value indicating whether the output is an animation container (APNG, or a GIF that may receive several frames
    /// or has animation settings). Encoders write the animation-wide structures (acTL, loop extension) accordingly.
    /// </summary>
    public bool IsAnimated { get; }

    /// <summary>Gets a value indicating whether <see cref="ImageWriterOptions.ExpectedFrameCount"/> is mandatory.</summary>
    public bool RequiresFrameCount { get; }

    /// <summary>Gets the maximum number of displayed frames, or <see langword="null"/> when unbounded.</summary>
    public int? MaxFrameCount { get; }

    /// <summary>Gets a value indicating whether a separate poster frame can be written (animated PNG only).</summary>
    public bool SupportsPosterFrame { get; }

    /// <summary>Gets the largest canvas width and height the format can store.</summary>
    public int MaxDimension { get; }

    /// <summary>
    /// Gets a value indicating whether the destination must be seekable: sizes written before the data are patched once every
    /// frame is written (animated WebP). Non-seekable destinations are rejected when the writer is created, before any output.
    /// </summary>
    public bool RequiresSeekableOutput { get; }

    /// <summary>
    /// Gets a value indicating whether every frame of the output stores a cursor hotspot (<c>CUR</c>, <c>ANI</c>). For any
    /// other output, a frame hotspot is metadata the format cannot store (see <see cref="MetadataWritePlan.ValidateFrameMetadata"/>).
    /// </summary>
    public bool SupportsFrameHotspot => StoresFrameHotspot(Format);

    /// <summary>Gets the output description used in messages (<c>static PNG</c>, <c>APNG</c>, <c>GIF</c>, <c>JPEG</c>).</summary>
    public string Name => Format switch
    {
        ImageFormat.Png => IsAnimated ? "APNG" : "static PNG",
        _ => ImageFormatNames.Get(Format),
    };

    /// <summary>Determines whether a format stores a cursor hotspot with every image it holds.</summary>
    /// <param name="format">The output format.</param>
    /// <returns><see langword="true"/> for cursor files and animated cursors.</returns>
    public static bool StoresFrameHotspot(ImageFormat format) => format is ImageFormat.Cur or ImageFormat.Ani;

    /// <summary>Resolves and validates the constraints of a writer.</summary>
    /// <param name="options">The writer options snapshot; <see cref="ImageWriterOptions.Encoder"/> is resolved.</param>
    /// <returns>The capabilities.</returns>
    /// <exception cref="ArgumentException">The options are invalid for the output (missing or wrong frame count, animation settings for a static output).</exception>
    /// <exception cref="UnsupportedImageFeatureException">The canvas or the play count cannot be represented by the format.</exception>
    public static ImageOutputCapabilities ForWriter(ImageWriterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var encoder = options.Encoder ?? throw new ArgumentException("The encoder is not resolved.", nameof(options));
        var expectedFrameCount = options.ExpectedFrameCount;
        var animation = options.Animation;
        var capabilities = Resolve(encoder, expectedFrameCount, animation is not null);
        if (capabilities.RequiresFrameCount && expectedFrameCount is null)
            throw new ArgumentException($"{capabilities.Name} output requires ImageWriterOptions.ExpectedFrameCount: the frame count is written before the image data and seeking is never required.", nameof(options));

        if (expectedFrameCount > capabilities.MaxFrameCount)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"{capabilities.Name} output stores at most {capabilities.MaxFrameCount} frame, but ImageWriterOptions.ExpectedFrameCount is {expectedFrameCount}."), nameof(options));

        if (animation is not null && !capabilities.IsAnimated)
            throw new ArgumentException($"{capabilities.Name} output cannot store animation settings: remove ImageWriterOptions.Animation or select an animated output.", nameof(options));

        capabilities.EnsureRepresentable(options.CanvasSize, animation);
        return capabilities;
    }

    /// <summary>
    /// Resolves and validates the constraints of an eager save: static outputs reject animated
    /// images instead of silently saving frame zero, and only animated PNG output stores a poster frame.
    /// </summary>
    /// <param name="encoder">The encoder.</param>
    /// <param name="image">The image (not disposed).</param>
    /// <returns>The capabilities.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The image is animated (or has a poster frame) and the output cannot store it, or a value cannot be represented.</exception>
    public static ImageOutputCapabilities ForImage(ImageEncoder encoder, Image image)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(image);
        var capabilities = Resolve(encoder, image.Frames.Count, image.IsAnimated);
        if (image.IsAnimated && !capabilities.IsAnimated)
            throw new UnsupportedImageFeatureException($"The image is animated ({image.Frames.Count} frame(s){(image.PosterFrame is null ? "" : ", a poster frame")}, animation settings) but {capabilities.Name} output stores a single still image. Extract a frame with CloneFrame, remove the animation settings of a single-frame image, or select an animated output.", capabilities.Format, "Animation");

        if (image.PosterFrame is not null && !capabilities.SupportsPosterFrame)
            throw new UnsupportedImageFeatureException($"The image has a separate poster frame, which only animated PNG output can store. Remove it with RemovePosterFrame or extract it with ClonePosterFrame.", capabilities.Format, "Poster frame");

        capabilities.EnsureRepresentable(image.Size, image.Animation);
        return capabilities;
    }

    private static ImageOutputCapabilities Resolve(ImageEncoder encoder, int? frameCount, bool hasAnimation) => encoder switch
    {
        PngEncoder png => png.AnimationMode switch
        {
            PngAnimationMode.Animated => Apng(),
            PngAnimationMode.Static => StaticPng(),
            _ => frameCount > 1 || hasAnimation ? Apng() : StaticPng(),
        },
        GifEncoder => new ImageOutputCapabilities(ImageFormat.Gif, isAnimated: frameCount != 1 || hasAnimation, requiresFrameCount: false, maxFrameCount: null, supportsPosterFrame: false, maxDimension: ushort.MaxValue),
        JpegEncoder => new ImageOutputCapabilities(ImageFormat.Jpeg, isAnimated: false, requiresFrameCount: false, maxFrameCount: 1, supportsPosterFrame: false, maxDimension: ushort.MaxValue),
        WebPEncoder webp => WebP(webp, isAnimated: frameCount != 1 || hasAnimation),
        QoiEncoder => new ImageOutputCapabilities(ImageFormat.Qoi, isAnimated: false, requiresFrameCount: false, maxFrameCount: 1, supportsPosterFrame: false, maxDimension: int.MaxValue),
        BmpEncoder => new ImageOutputCapabilities(ImageFormat.Bmp, isAnimated: false, requiresFrameCount: false, maxFrameCount: 1, supportsPosterFrame: false, maxDimension: int.MaxValue),
        TgaEncoder => new ImageOutputCapabilities(ImageFormat.Tga, isAnimated: false, requiresFrameCount: false, maxFrameCount: 1, supportsPosterFrame: false, maxDimension: ushort.MaxValue),
        PnmEncoder => new ImageOutputCapabilities(ImageFormat.Pnm, isAnimated: false, requiresFrameCount: false, maxFrameCount: 1, supportsPosterFrame: false, maxDimension: int.MaxValue),
        TiffEncoder => new ImageOutputCapabilities(ImageFormat.Tiff, isAnimated: false, requiresFrameCount: false, maxFrameCount: 1, supportsPosterFrame: false, maxDimension: int.MaxValue, requiresSeekableOutput: true),
        IcoEncoder ico => new ImageOutputCapabilities(ico.Format, isAnimated: false, requiresFrameCount: false, maxFrameCount: 1, supportsPosterFrame: false, maxDimension: IcoEncoder.MaxDimension),
        AniEncoder => new ImageOutputCapabilities(ImageFormat.Ani, isAnimated: true, requiresFrameCount: false, maxFrameCount: null, supportsPosterFrame: false, maxDimension: AniEncoder.MaxDimension, requiresSeekableOutput: true),
        _ => throw new ArgumentException($"The encoder {encoder.GetType().Name} is not supported.", nameof(encoder)),
    };

    private static ImageOutputCapabilities WebP(WebPEncoder encoder, bool isAnimated)
    {
        // VP8L stores 14-bit sizes minus one (16,384); VP8 stores 14-bit sizes (16,383); animation frames cover the canvas
        var maxDimension = encoder.Compression == WebPCompression.Lossless ? 16384 : 16383;
        return new ImageOutputCapabilities(ImageFormat.WebP, isAnimated, requiresFrameCount: false, maxFrameCount: null, supportsPosterFrame: false, maxDimension, requiresSeekableOutput: isAnimated);
    }

    private static ImageOutputCapabilities Apng() => new(ImageFormat.Png, isAnimated: true, requiresFrameCount: true, maxFrameCount: null, supportsPosterFrame: true, maxDimension: int.MaxValue);

    private static ImageOutputCapabilities StaticPng() => new(ImageFormat.Png, isAnimated: false, requiresFrameCount: true, maxFrameCount: 1, supportsPosterFrame: false, maxDimension: int.MaxValue);

    private void EnsureRepresentable(Size canvasSize, AnimationMetadata? animation)
    {
        if (canvasSize.Width > MaxDimension || canvasSize.Height > MaxDimension)
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"{Name} output stores at most {MaxDimension} pixels per side; the canvas is {canvasSize.Width}x{canvasSize.Height}."), Format, "Canvas size");

        if (Format == ImageFormat.Gif && IsAnimated)
        {
            // Throws UnsupportedImageFeatureException above 65,536 plays (never clamped)
            _ = AnimationTiming.ToGifLoopCount(animation?.TotalPlays);
        }

        if (Format == ImageFormat.WebP && IsAnimated)
        {
            // Throws UnsupportedImageFeatureException above 65,535 plays (never clamped)
            _ = AnimationTiming.ToWebPLoopCount(animation?.TotalPlays);
        }

        if (Format == ImageFormat.Ani)
        {
            // Throws UnsupportedImageFeatureException for any finite play count: an animated cursor always loops
            AnimationTiming.EnsureAniPlayCount(animation?.TotalPlays);
        }
    }
}
