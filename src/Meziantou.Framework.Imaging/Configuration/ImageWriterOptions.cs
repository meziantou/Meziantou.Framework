using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options for <see cref="Image.CreateWriter{TPixel}(Stream, ImageWriterOptions)"/>.</summary>
/// <remarks>
/// The writer takes a snapshot of these options when it is created: later changes to the <see cref="Metadata"/> or
/// <see cref="Animation"/> instances passed here do not affect the writer.
/// </remarks>
public sealed class ImageWriterOptions
{
    /// <summary>Initializes a new instance of the <see cref="ImageWriterOptions"/> class.</summary>
    /// <param name="canvasSize">The size of every frame written. Must not be empty.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="canvasSize"/> is empty.</exception>
    public ImageWriterOptions(Size canvasSize)
    {
        if (canvasSize.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(canvasSize), canvasSize, "The canvas size must not be empty.");

        CanvasSize = canvasSize;
    }

    /// <summary>Initializes a new instance of the <see cref="ImageWriterOptions"/> class.</summary>
    /// <param name="width">The width of every frame written. Must be positive.</param>
    /// <param name="height">The height of every frame written. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    public ImageWriterOptions(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        CanvasSize = new Size(width, height);
    }

    /// <summary>Gets the size of every frame written.</summary>
    public Size CanvasSize { get; }

    /// <summary>
    /// Gets the encoder and its settings. Required for stream outputs. For path outputs, <see langword="null"/> selects the
    /// encoder from the file extension, like <see cref="Image.Save(string, ImageEncoder?)"/>.
    /// </summary>
    public ImageEncoder? Encoder { get; init; }

    /// <summary>
    /// Gets the number of displayed frames that will be written (excluding a poster frame), or <see langword="null"/> if unknown.
    /// PNG/APNG output requires a known count; GIF supports an unknown count; JPEG output requires exactly one frame.
    /// <see cref="ImageWriter{TPixel}.Complete"/> fails if the actual count differs.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int? ExpectedFrameCount
    {
        get;
        init
        {
            if (value is not null)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value.Value);
            }

            field = value;
        }
    }

    /// <summary>Gets the image-wide metadata to write, or <see langword="null"/> to write none. The writer copies it when it is created.</summary>
    public ImageMetadata? Metadata { get; init; }

    /// <summary>
    /// Gets the animation-wide settings (such as the number of plays), or <see langword="null"/> for defaults. When
    /// <see langword="null"/> and more than one frame or a poster frame is written, an infinitely looping animation is produced.
    /// The writer copies it when it is created.
    /// </summary>
    public AnimationMetadata? Animation { get; init; }

    /// <summary>Gets the configuration of the writer (resource limits, parallelism). Defaults to <see cref="ImageConfiguration.Default"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public ImageConfiguration Configuration
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = ImageConfiguration.Default;

    /// <summary>
    /// Gets a value indicating whether a caller-provided stream is left open when the writer is disposed. Defaults to
    /// <see langword="true"/>. Streams opened by the path overloads are always owned by the writer.
    /// </summary>
    public bool LeaveOpen { get; init; } = true;

    /// <summary>
    /// Creates the snapshot taken by a writer when it is created: every value is copied, and the mutable
    /// <see cref="Metadata"/> and <see cref="Animation"/> containers are cloned so that later caller edits never affect the
    /// writer. Immutable members (encoder, configuration) are shared.
    /// </summary>
    /// <returns>An independent options instance.</returns>
    internal ImageWriterOptions CreateSnapshot() => new(CanvasSize)
    {
        Encoder = Encoder,
        ExpectedFrameCount = ExpectedFrameCount,
        Metadata = Metadata?.Clone(),
        Animation = Animation?.Clone(),
        Configuration = Configuration,
        LeaveOpen = LeaveOpen,
    };
}
