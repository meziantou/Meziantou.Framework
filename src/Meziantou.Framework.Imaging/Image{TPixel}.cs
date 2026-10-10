using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>An owned, disposable image whose frames store pixels of type <typeparamref name="TPixel"/>.</summary>
/// <typeparam name="TPixel">
/// The pixel type: <see cref="Rgba32"/>, <see cref="Bgra32"/>, <see cref="Rgb24"/>, <see cref="Rgba64"/>,
/// <see cref="Gray8"/> or <see cref="Gray16"/>. Other unmanaged types are rejected with a <see cref="NotSupportedException"/>.
/// </typeparam>
/// <remarks>See <see cref="Image"/> for the ownership, frame and atomicity contract.</remarks>
public sealed class Image<TPixel> : Image
    where TPixel : unmanaged
{
    private readonly ImageFrameCollection<TPixel> _frames;

    /// <summary>Initializes a new single-frame still image whose pixels are all zero (transparent black, or black for formats without alpha).</summary>
    /// <param name="width">The width. Must be positive.</param>
    /// <param name="height">The height. Must be positive.</param>
    /// <param name="configuration">The configuration, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a configured limit.</exception>
    public Image(int width, int height, ImageConfiguration? configuration = null)
        : this(ValidateCanvas(width, height, configuration), new Size(width, height), scope: null, layoutOptions: null)
    {
    }

    /// <summary>Initializes a new single-frame still image filled with the specified pixel value.</summary>
    /// <param name="width">The width. Must be positive.</param>
    /// <param name="height">The height. Must be positive.</param>
    /// <param name="fill">The value of every pixel.</param>
    /// <param name="configuration">The configuration, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a configured limit.</exception>
    public Image(int width, int height, TPixel fill, ImageConfiguration? configuration = null)
        : this(ValidateCanvas(width, height, configuration), new Size(width, height), scope: null, layoutOptions: null)
    {
        // Storage is zeroed on allocation: only a non-zero value needs to be written
        if (unsafe(MemoryMarshal.AsBytes(new ReadOnlySpan<TPixel>(in fill))).ContainsAnyExcept((byte)0))
        {
            using var lease = FrameCore(0).Storage.AcquireLease();
            for (var y = 0; y < height; y++)
            {
                lease.GetRow<TPixel>(y).Fill(fill);
            }
        }
    }

    /// <summary>
    /// Initializes a new single-frame still image with a zeroed frame. This is the internal construction hook used by
    /// imports, clones and decoders: <paramref name="configuration"/> and <paramref name="size"/> must already be validated
    /// (<see cref="ImageResourceLimits.EnsureCanvasWithinLimits(int, int)"/>).
    /// </summary>
    /// <param name="configuration">The configuration captured by the image.</param>
    /// <param name="size">The canvas size.</param>
    /// <param name="scope">
    /// The allocation scope, or <see langword="null"/> for a new one. Sequential readers pass their scope so that the
    /// images they return stay charged to it until those images are disposed, even after the reader is disposed.
    /// </param>
    /// <param name="layoutOptions">The storage layout (tests use it to force segmented or padded rows), or <see langword="null"/> for the default.</param>
    internal Image(ImageConfiguration configuration, Size size, AllocationScope? scope, PixelStorageLayoutOptions? layoutOptions)
        : base(configuration, size, scope, layoutOptions)
    {
        _ = PixelFormats.GetPixelFormat<TPixel>();
        _frames = new ImageFrameCollection<TPixel>(this);
        AppendFrameCore(new FrameMetadata());
    }

    /// <inheritdoc />
    public override PixelFormat PixelFormat => PixelFormats.GetPixelFormat<TPixel>();

    /// <summary>Gets the typed view of the displayed frames. The frame objects are the same as those of the untyped <see cref="Image.Frames"/>.</summary>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public override ImageFrameCollection<TPixel> Frames
    {
        get
        {
            ThrowIfDisposed();
            return _frames;
        }
    }

    /// <summary>Gets the typed poster frame, or <see langword="null"/> if none.</summary>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public override ImageFrame<TPixel>? PosterFrame
    {
        get
        {
            ThrowIfDisposed();
            return (ImageFrame<TPixel>?)PosterCore;
        }
    }

    /// <inheritdoc />
    public override Image<TPixel> Clone()
    {
        ThrowIfDisposed();
        var clone = new Image<TPixel>(Configuration, Size, scope: null, LayoutOptions);
        try
        {
            clone.CopyFrameFrom(FrameCore(0), 0);
            for (var i = 1; i < FrameCountCore; i++)
            {
                CopyPixels(FrameCore(i), clone.AppendFrameUnchecked(FrameCore(i).MetadataCore.Clone()));
            }

            if (PosterCore is { } poster)
            {
                CopyPixels(poster, clone.AttachPosterFrameUnchecked(poster.MetadataCore.Clone()));
            }

            clone.CopyImageStateFrom(Metadata.Clone(), Animation);
            return clone;
        }
        catch
        {
            clone.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public override Image<TPixel> CloneFrame(int index)
    {
        ThrowIfDisposed();
        if ((uint)index >= (uint)FrameCountCore)
            throw new ArgumentOutOfRangeException(nameof(index), index, "The frame index is out of range.");

        return CloneStill(FrameCore(index));
    }

    /// <inheritdoc />
    public override Image<TPixel> ClonePosterFrame()
    {
        ThrowIfDisposed();
        var poster = PosterCore ?? throw new InvalidOperationException("The image has no poster frame.");
        return CloneStill(poster);
    }

    /// <inheritdoc />
    public override ImageFrame<TPixel> AppendFrame() => (ImageFrame<TPixel>)AppendBlankFrame();

    /// <inheritdoc />
    public override ImageFrame<TPixel> AppendFrame(ImageFrame source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return (ImageFrame<TPixel>)InsertFrameCopy(Frames.Count, source, "append a frame");
    }

    /// <inheritdoc />
    public override ImageFrame<TPixel> InsertFrame(int index, ImageFrame source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return (ImageFrame<TPixel>)InsertFrameCopy(index, source, "insert a frame");
    }

    /// <inheritdoc />
    public override ImageFrame<TPixel> SetPosterFrame(ImageFrame source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return (ImageFrame<TPixel>)SetPosterFrameCopy(source);
    }

    /// <summary>Gets a displayed frame without validation.</summary>
    internal ImageFrame<TPixel> FrameAt(int index) => (ImageFrame<TPixel>)FrameCore(index);

    /// <summary>
    /// Appends a zeroed frame without the structural checks of <see cref="AppendFrame()"/> (no lease or frame-count check):
    /// used while building an image that is not yet visible to callers (clones, conversions, decoders that charge frames
    /// through their own <see cref="InputResourceTracker"/>).
    /// </summary>
    internal ImageFrame<TPixel> AppendFrameUnchecked(FrameMetadata? metadata = null) => (ImageFrame<TPixel>)AppendFrameCore(metadata ?? new FrameMetadata());

    /// <summary>Attaches a zeroed poster frame to an image under construction that has none.</summary>
    internal ImageFrame<TPixel> AttachPosterFrameUnchecked(FrameMetadata? metadata = null) => (ImageFrame<TPixel>)AttachPosterFrameCore(metadata ?? new FrameMetadata());

    private protected override ImageFrame CreateFrame(PixelStorage storage, FrameMetadata metadata) => new ImageFrame<TPixel>(this, storage, metadata);

    private static ImageConfiguration ValidateCanvas(int width, int height, ImageConfiguration? configuration)
    {
        _ = PixelFormats.GetPixelFormat<TPixel>();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        configuration ??= ImageConfiguration.Default;
        configuration.Limits.EnsureCanvasWithinLimits(width, height);
        return configuration;
    }

    /// <summary>Creates a new still image from one frame: pixels, a copy of the frame metadata and of the image metadata; no animation settings and no poster.</summary>
    private Image<TPixel> CloneStill(ImageFrame source)
    {
        var result = new Image<TPixel>(Configuration, Size, scope: null, LayoutOptions);
        try
        {
            result.CopyFrameFrom(source, 0);
            result.CopyImageStateFrom(Metadata.Clone(), animation: null);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private void CopyFrameFrom(ImageFrame source, int index)
    {
        var destination = FrameCore(index);
        destination.MetadataCore = source.MetadataCore.Clone();
        CopyPixels(source, destination);
    }
}
