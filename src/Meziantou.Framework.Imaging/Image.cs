using System.Diagnostics;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>An owned, disposable image made of one or more full-canvas displayed frames sharing one pixel format.</summary>
/// <remarks>
/// <para>
/// <see cref="Image"/> is the untyped view of an image; every instance is an <see cref="Image{TPixel}"/> for one of the
/// built-in pixel structs. The untyped and typed views expose the same frame objects, without copies.
/// </para>
/// <para>Invariants:</para>
/// <list type="bullet">
/// <item><description>The canvas has positive dimensions and there is always at least one displayed frame.</description></item>
/// <item><description>Every attached frame (including the optional <see cref="PosterFrame"/>) has the canvas size and the image pixel format.</description></item>
/// <item><description>
/// Frames are complete displayed images. Encoded delta rectangles, blend and disposal instructions are codec internals
/// that are resolved on decode and recomputed on encode; they are not part of the model.
/// </description></item>
/// <item><description>
/// Attached frames are borrowed views owned by the image. Operations that extract frames (<see cref="CloneFrame(int)"/>,
/// <see cref="ClonePosterFrame"/>) return new independently disposable images.
/// </description></item>
/// </list>
/// <para>
/// Geometry-changing operations (crop, resize, rotate, auto-orient) apply to every frame and to the poster frame
/// atomically: on failure before commit, the image is unchanged. Pixel-only operations (flip, grayscale, convolve, row
/// callbacks) may leave the image partially modified on failure or cancellation, but always structurally valid.
/// </para>
/// <para>
/// An image is not thread-safe for concurrent mutation. Concurrent read-only access is allowed only while no operation
/// mutates the image or holds a pixel lease.
/// </para>
/// </remarks>
public abstract partial class Image : IDisposable
{
    private readonly List<ImageFrame> _frames = [];
    private readonly ImageConfiguration _configuration;
    private Size _size;
    private ImageMetadata _metadata = new();
    private AnimationMetadata? _animation;
    private ImageFrame? _poster;
    private int _version;
    private bool _disposed;

    /// <summary>Initializes the shared state of an image without frames; the derived constructor attaches the first frame.</summary>
    /// <param name="configuration">The validated configuration.</param>
    /// <param name="size">The validated canvas size.</param>
    /// <param name="scope">
    /// The allocation scope to charge, or <see langword="null"/> for a new scope (independently constructed, imported or
    /// cloned images). Readers pass their own scope so that the images they return stay charged to it until disposed
    ///; each image still has its own <see cref="StorageOwner"/> (lifetime and lease gate).
    /// </param>
    /// <param name="layoutOptions">The storage layout of every frame, or <see langword="null"/> for <see cref="PixelStorageLayoutOptions.Default"/>.</param>
    private protected Image(ImageConfiguration configuration, Size size, AllocationScope? scope, PixelStorageLayoutOptions? layoutOptions)
    {
        _configuration = configuration;
        _size = size;
        Owner = new StorageOwner(scope ?? AllocationScope.Create(configuration, "Image"), "Image");
        LayoutOptions = layoutOptions ?? PixelStorageLayoutOptions.Default;
    }

    /// <summary>Gets the canvas width, in pixels.</summary>
    public int Width => Size.Width;

    /// <summary>Gets the canvas height, in pixels.</summary>
    public int Height => Size.Height;

    /// <summary>Gets the canvas size, in pixels. Always positive in both dimensions.</summary>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public Size Size
    {
        get
        {
            ThrowIfDisposed();
            return _size;
        }
    }

    /// <summary>Gets the pixel format shared by every frame.</summary>
    public abstract PixelFormat PixelFormat { get; }

    /// <summary>Gets the configuration captured when the image was created or loaded.</summary>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public ImageConfiguration Configuration
    {
        get
        {
            ThrowIfDisposed();
            return _configuration;
        }
    }

    /// <summary>Gets the mutable image-wide metadata.</summary>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public ImageMetadata Metadata
    {
        get
        {
            ThrowIfDisposed();
            return _metadata;
        }
    }

    /// <summary>Gets or sets the animation-wide settings, or <see langword="null"/> for a still image.</summary>
    /// <remarks>
    /// <para>
    /// Adding a second frame or a poster frame creates default settings (infinite loop) when none exist. Removing frames
    /// down to one does not remove the settings: a single-frame image with animation settings is still animated and is
    /// saved as a one-frame animation.
    /// </para>
    /// <para>The setter stores a copy of the value. Setting <see langword="null"/> is only allowed when the image has a single frame and no poster frame.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The value is <see langword="null"/> while the image has several frames or a poster frame.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public AnimationMetadata? Animation
    {
        get
        {
            ThrowIfDisposed();
            return _animation;
        }

        set
        {
            ThrowIfDisposed();
            if (value is null && (_frames.Count > 1 || _poster is not null))
                throw new InvalidOperationException("The animation settings can only be removed from an image with a single frame and no poster frame.");

            _animation = value?.Clone();
        }
    }

    /// <summary>Gets the displayed frames, in playback order. Never empty.</summary>
    /// <remarks>Structural edits (insert, remove, move) invalidate enumerators; surviving frame objects keep their identity when frames are moved.</remarks>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public abstract ImageFrameCollection Frames { get; }

    /// <summary>
    /// Gets the separate poster frame, or <see langword="null"/> if none. A poster frame is a static image shown by
    /// decoders that do not support animation (APNG default image that is not part of the animation). It is not included
    /// in <see cref="Frames"/>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public abstract ImageFrame? PosterFrame { get; }

    /// <summary>
    /// Gets a value indicating whether the image is an animation: it has more than one frame, a poster frame, or
    /// animation settings (<see cref="Animation"/> is not <see langword="null"/>).
    /// </summary>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public bool IsAnimated
    {
        get
        {
            ThrowIfDisposed();
            return _frames.Count > 1 || _poster is not null || _animation is not null;
        }
    }

    /// <summary>Gets the lifetime and lease gate of the storages of the frames and of the poster frame.</summary>
    internal StorageOwner Owner { get; }

    /// <summary>Gets the storage layout used for every frame of the image (segmentation and row padding never change visible rows).</summary>
    internal PixelStorageLayoutOptions LayoutOptions { get; }

    /// <summary>Gets the structural version of <see cref="Frames"/>, incremented by every insert, removal and move.</summary>
    internal int StructureVersion => _version;

    /// <summary>Gets the poster frame without validation.</summary>
    private protected ImageFrame? PosterCore => _poster;

    /// <summary>Creates an independent deep copy of the image: frames, poster frame, metadata and animation settings.</summary>
    /// <returns>A new image owned by the caller, with its own allocation scope.</returns>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="ImageResourceLimitException">The copy would exceed the configured allocation limit.</exception>
    public abstract Image Clone();

    /// <summary>Creates an independent copy of the image converted to another pixel format.</summary>
    /// <typeparam name="TPixel">The target pixel type.</typeparam>
    /// <param name="options">The conversion options, or <see langword="null"/> for <see cref="PixelConversionOptions.Default"/>.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct.</exception>
    /// <exception cref="UnsupportedImageFeatureException">
    /// The conversion would drop non-opaque alpha without <see cref="PixelConversionOptions.BackgroundColor"/>, or would retain an incompatible ICC profile.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public Image<TPixel> CloneAs<TPixel>(PixelConversionOptions? options = null)
        where TPixel : unmanaged
    {
        var destinationFormat = PixelFormats.GetPixelFormat<TPixel>();
        ThrowIfDisposed();
        var plan = PixelConversionPlan.Create(PixelFormat, destinationFormat, options, _metadata.IccProfile);
        if (plan.RequiresOpaqueSource)
        {
            // Fail before allocating anything: the conversion must not be partially applied
            foreach (var frame in _frames)
            {
                EnsureOpaque(frame, plan);
            }

            if (_poster is not null)
            {
                EnsureOpaque(_poster, plan);
            }
        }

        var result = new Image<TPixel>(_configuration, _size, scope: null, LayoutOptions);
        try
        {
            ConvertFrame(_frames[0], result.FrameAt(0), plan);
            for (var i = 1; i < _frames.Count; i++)
            {
                ConvertFrame(_frames[i], result.AppendFrameUnchecked(), plan);
            }

            if (_poster is not null)
            {
                ConvertFrame(_poster, result.AttachPosterFrameUnchecked(), plan);
            }

            var metadata = _metadata.Clone();
            metadata.IccProfile = plan.ColorProfile;
            result.CopyImageStateFrom(metadata, _animation);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Extracts a copy of one displayed frame as a new single-frame still image. The copy keeps the frame duration and the
    /// image-wide metadata, but not the animation settings or the poster frame.
    /// </summary>
    /// <param name="index">The index of the frame in <see cref="Frames"/>.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public abstract Image CloneFrame(int index);

    /// <summary>Extracts a copy of the poster frame as a new single-frame still image with the image-wide metadata.</summary>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="InvalidOperationException">The image has no poster frame.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public abstract Image ClonePosterFrame();

    /// <summary>Appends a new frame whose pixels are all zero (transparent black, or black for formats without alpha) and whose duration is zero.</summary>
    /// <returns>The attached frame.</returns>
    /// <exception cref="ImageResourceLimitException">The frame would exceed the configured frame count (displayed frames plus poster) or allocation limit; the image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public abstract ImageFrame AppendFrame();

    /// <summary>Appends a copy of a frame. Pixels and frame metadata are copied; the source is not attached.</summary>
    /// <param name="source">The frame to copy. It must have the canvas size and the pixel format of this image; it may belong to this image.</param>
    /// <returns>The attached copy.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> has a different size or pixel format. Frames are never resized or converted implicitly.</exception>
    /// <exception cref="ImageResourceLimitException">The frame would exceed the configured frame count (displayed frames plus poster) or allocation limit; the image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease of the image is active, or the source frame is leased.</exception>
    /// <exception cref="ObjectDisposedException">The image or the source is disposed.</exception>
    public abstract ImageFrame AppendFrame(ImageFrame source);

    /// <summary>Inserts a copy of a frame at the specified index. Pixels and frame metadata are copied; the source is not attached.</summary>
    /// <param name="index">The index at which the copy is inserted, from 0 to <c>Frames.Count</c>.</param>
    /// <param name="source">The frame to copy. It must have the canvas size and the pixel format of this image; it may belong to this image.</param>
    /// <returns>The attached copy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    /// <exception cref="ArgumentException"><paramref name="source"/> has a different size or pixel format.</exception>
    /// <exception cref="ImageResourceLimitException">The frame would exceed the configured frame count (displayed frames plus poster) or allocation limit; the image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease of the image is active, or the source frame is leased.</exception>
    /// <exception cref="ObjectDisposedException">The image or the source is disposed.</exception>
    public abstract ImageFrame InsertFrame(int index, ImageFrame source);

    /// <summary>Removes a frame. References to the removed frame become invalid and throw <see cref="ObjectDisposedException"/> on use.</summary>
    /// <param name="index">The index of the frame to remove.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    /// <exception cref="InvalidOperationException">The frame is the last remaining frame, or a pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public void RemoveFrame(int index)
    {
        Owner.EnsureCanModify("remove a frame");
        if ((uint)index >= (uint)_frames.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, "The frame index is out of range.");

        if (_frames.Count == 1)
            throw new InvalidOperationException("Cannot remove the last frame: an image always has at least one displayed frame.");

        var frame = _frames[index];
        _frames.RemoveAt(index);
        _version++;
        frame.Storage.Dispose();
    }

    /// <summary>Moves a frame to a new position. Frame objects keep their identity.</summary>
    /// <param name="sourceIndex">The current index of the frame.</param>
    /// <param name="destinationIndex">The final index of the frame after the move.</param>
    /// <exception cref="ArgumentOutOfRangeException">An index is out of range.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public void MoveFrame(int sourceIndex, int destinationIndex)
    {
        Owner.EnsureCanModify("move a frame");
        if ((uint)sourceIndex >= (uint)_frames.Count)
            throw new ArgumentOutOfRangeException(nameof(sourceIndex), sourceIndex, "The frame index is out of range.");

        if ((uint)destinationIndex >= (uint)_frames.Count)
            throw new ArgumentOutOfRangeException(nameof(destinationIndex), destinationIndex, "The destination index is out of range.");

        if (sourceIndex == destinationIndex)
            return;

        var frame = _frames[sourceIndex];
        _frames.RemoveAt(sourceIndex);
        _frames.Insert(destinationIndex, frame);
        _version++;
    }

    /// <summary>
    /// Sets the poster frame to a copy of a frame, replacing (and invalidating) any existing poster frame. Creates default
    /// animation settings when none exist.
    /// </summary>
    /// <param name="source">The frame to copy. It must have the canvas size and the pixel format of this image.</param>
    /// <returns>The attached poster frame.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> has a different size or pixel format.</exception>
    /// <exception cref="ImageResourceLimitException">The frame would exceed the configured frame count (displayed frames plus poster) or allocation limit; the image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease of the image is active, or the source frame is leased.</exception>
    /// <exception cref="ObjectDisposedException">The image or the source is disposed.</exception>
    public abstract ImageFrame SetPosterFrame(ImageFrame source);

    /// <summary>Removes the poster frame, if any. References to the removed poster frame become invalid.</summary>
    /// <returns><see langword="true"/> if a poster frame was removed; <see langword="false"/> if there was none.</returns>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public bool RemovePosterFrame()
    {
        Owner.EnsureCanModify("remove the poster frame");
        var poster = _poster;
        if (poster is null)
            return false;

        _poster = null;
        poster.Storage.Dispose();
        return true;
    }

    /// <summary>
    /// Encodes the image to a file. The file is written to a temporary file in the same directory and published only after
    /// encoding succeeds; on failure an existing file at <paramref name="path"/> is left unchanged.
    /// </summary>
    /// <param name="path">The destination path.</param>
    /// <param name="encoder">
    /// The encoder, or <see langword="null"/> to select it from the file extension: <c>.png</c> (PNG, automatic animation mode),
    /// <c>.apng</c> (animated PNG), <c>.gif</c> (GIF), <c>.jpg</c>/<c>.jpeg</c> (JPEG), each with default settings.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty, or <paramref name="encoder"/> is <see langword="null"/> and the extension is not recognized.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The image cannot be encoded without a loss that the encoder settings do not allow (animation, alpha, precision, metadata).</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public void Save(string path, ImageEncoder? encoder = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ThrowIfDisposed();
        encoder ??= ImageEncoder.FromPath(path);
        using var writer = ImageWriterCore.Create(path, CreateSaveOptions(encoder), PixelFormat, asynchronous: false);
        WriteTo(writer);
    }

    /// <summary>Encodes the image to a stream, starting at its current position. The stream is left open.</summary>
    /// <param name="stream">The writable destination stream. Seeking is never required.</param>
    /// <param name="encoder">The encoder. Required, as a stream has no file name to infer the format from.</param>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not writable.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The image cannot be encoded without a loss that the encoder settings do not allow.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public void Save(Stream stream, ImageEncoder encoder)
    {
        ValidateWritableStream(stream);
        ArgumentNullException.ThrowIfNull(encoder);
        ThrowIfDisposed();
        using var writer = ImageWriterCore.Create(stream, CreateSaveOptions(encoder), PixelFormat);
        WriteTo(writer);
    }

    /// <summary>Asynchronously encodes the image to a file, with the same atomic-publication guarantees as <see cref="Save(string, ImageEncoder?)"/>.</summary>
    /// <param name="path">The destination path.</param>
    /// <param name="encoder">The encoder, or <see langword="null"/> to select it from the file extension.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. On cancellation, no file is published.</param>
    /// <returns>A task that completes when the file has been published.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty, or the extension is not recognized.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The image cannot be encoded without a loss that the encoder settings do not allow.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public Task SaveAsync(string path, ImageEncoder? encoder = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ThrowIfDisposed();
        encoder ??= ImageEncoder.FromPath(path);
        var options = CreateSaveOptions(encoder);
        return SaveCoreAsync(path, options, cancellationToken);
    }

    /// <summary>Asynchronously encodes the image to a stream, starting at its current position. The stream is left open.</summary>
    /// <param name="stream">The writable destination stream.</param>
    /// <param name="encoder">The encoder.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the image has been written.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not writable.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The image cannot be encoded without a loss that the encoder settings do not allow.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public Task SaveAsync(Stream stream, ImageEncoder encoder, CancellationToken cancellationToken = default)
    {
        ValidateWritableStream(stream);
        ArgumentNullException.ThrowIfNull(encoder);
        ThrowIfDisposed();
        var options = CreateSaveOptions(encoder);
        return SaveCoreAsync(stream, options, cancellationToken);
    }

    /// <summary>
    /// Releases the pixel storage of every frame and of the poster frame. Borrowed frame references become invalid.
    /// Disposing more than once has no effect.
    /// </summary>
    /// <exception cref="InvalidOperationException">A pixel lease is active (for example, <c>Dispose</c> is called from a row callback).</exception>
    public void Dispose()
    {
        if (Owner.Dispose())
        {
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>Gets the displayed frames of a live image.</summary>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    internal List<ImageFrame> GetFrameList()
    {
        ThrowIfDisposed();
        return _frames;
    }

    /// <summary>Creates a typed frame over a storage of this image.</summary>
    private protected abstract ImageFrame CreateFrame(PixelStorage storage, FrameMetadata metadata);

    /// <summary>Allocates a zeroed frame storage of the canvas size, charged to the scope of the image.</summary>
    private protected PixelStorage AllocateFrameStorage()
        => Owner.Allocate(_size.Width, _size.Height, PixelFormats.GetBytesPerPixel(PixelFormat), LayoutOptions);

    /// <summary>Attaches a zeroed frame at the end without structural checks (construction, clones, decoders).</summary>
    private protected ImageFrame AppendFrameCore(FrameMetadata metadata)
    {
        var frame = CreateFrame(AllocateFrameStorage(), metadata);
        _frames.Add(frame);
        OnFramesAdded();
        return frame;
    }

    /// <summary>Attaches a zeroed poster frame without structural checks (clones, decoders). The image must not have one.</summary>
    private protected ImageFrame AttachPosterFrameCore(FrameMetadata metadata)
    {
        Debug.Assert(_poster is null);
        _poster = CreateFrame(AllocateFrameStorage(), metadata);
        _animation ??= new AnimationMetadata();
        return _poster;
    }

    /// <summary>Gets an attached frame without validation.</summary>
    private protected ImageFrame FrameCore(int index) => _frames[index];

    /// <summary>Gets the number of displayed frames without validation.</summary>
    private protected int FrameCountCore => _frames.Count;

    /// <summary>Replaces the image-wide state of an image being built (clones). The values are attached as is.</summary>
    internal void CopyImageStateFrom(ImageMetadata metadata, AnimationMetadata? animation)
    {
        _metadata = metadata;
        _animation = animation?.Clone();
        if (_animation is null && (_frames.Count > 1 || _poster is not null))
        {
            _animation = new AnimationMetadata();
        }
    }

    /// <summary>Inserts a copy of <paramref name="source"/> (pixels and frame metadata) at <paramref name="index"/>.</summary>
    private protected ImageFrame InsertFrameCopy(int index, ImageFrame source, string operation)
    {
        Owner.EnsureCanModify(operation);
        if ((uint)index > (uint)_frames.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, "The insertion index must be between 0 and the number of frames.");

        ValidateSourceFrame(source);
        EnsureFrameCountWithinLimit();
        var frame = CreateFrameCopy(source);
        _frames.Insert(index, frame);
        OnFramesAdded();
        return frame;
    }

    /// <summary>Appends a zeroed frame with a zero duration.</summary>
    private protected ImageFrame AppendBlankFrame()
    {
        Owner.EnsureCanModify("append a frame");
        EnsureFrameCountWithinLimit();
        return AppendFrameCore(new FrameMetadata());
    }

    /// <summary>Sets the poster frame to a copy of <paramref name="source"/>, invalidating the previous poster frame.</summary>
    private protected ImageFrame SetPosterFrameCopy(ImageFrame source)
    {
        Owner.EnsureCanModify("set the poster frame");
        ValidateSourceFrame(source);
        if (_poster is null)
        {
            EnsureFrameCountWithinLimit();
        }

        // Copy first: the source may be the current poster frame
        var frame = CreateFrameCopy(source);
        var previous = _poster;
        _poster = frame;
        _animation ??= new AnimationMetadata();
        previous?.Storage.Dispose();
        return frame;
    }

    /// <summary>Copies the visible rows of a frame into another frame of the same geometry, under a stable-order lease pair.</summary>
    private protected static void CopyPixels(ImageFrame source, ImageFrame destination)
    {
        using var leases = PixelLeasePair.Acquire(source.GetStorage(), destination.GetStorage());
        leases.First.CopyTo(leases.Second);
    }

    private ImageFrame CreateFrameCopy(ImageFrame source)
    {
        var storage = AllocateFrameStorage();
        var frame = CreateFrame(storage, source.MetadataCore.Clone());
        try
        {
            CopyPixels(source, frame);
        }
        catch
        {
            storage.Dispose();
            throw;
        }

        return frame;
    }

    private void ValidateSourceFrame(ImageFrame source)
    {
        var storage = source.GetStorage();
        if (source.PixelFormat != PixelFormat)
            throw new ArgumentException($"The source frame has the pixel format {source.PixelFormat} but the image has {PixelFormat}. Frames are never converted implicitly; use CloneAs to convert the source image first.", nameof(source));

        if (storage.Width != _size.Width || storage.Height != _size.Height)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The source frame is {storage.Width}x{storage.Height} but the canvas is {_size.Width}x{_size.Height}. Frames are never resized implicitly."), nameof(source));
    }

    /// <summary>Validates that one more frame (displayed or poster) fits in <see cref="ImageResourceLimits.MaxFrames"/>.</summary>
    private void EnsureFrameCountWithinLimit()
    {
        var limit = _configuration.Limits.MaxFrames;
        var requested = _frames.Count + (_poster is null ? 0L : 1L) + 1L;
        if (requested > limit)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Frames, limit, requested);
    }

    private void OnFramesAdded()
    {
        _version++;
        if (_frames.Count > 1)
        {
            _animation ??= new AnimationMetadata();
        }
    }

    private static void EnsureOpaque(ImageFrame frame, PixelConversionPlan plan)
    {
        using var lease = frame.GetStorage().AcquireLease();
        for (var y = 0; y < lease.Height; y++)
        {
            var index = PixelConverter.IndexOfNonOpaque(plan.SourceFormat, lease.GetRowBytes(y));
            if (index >= 0)
                throw PixelConverter.CreateNonOpaqueException(plan.SourceFormat, plan.DestinationFormat, index, plan.Format);
        }
    }

    private static void ConvertFrame(ImageFrame source, ImageFrame destination, PixelConversionPlan plan)
    {
        destination.MetadataCore = source.MetadataCore.Clone();
        using var leases = PixelLeasePair.Acquire(source.GetStorage(), destination.GetStorage());
        var input = leases.First;
        var output = leases.Second;
        for (var y = 0; y < input.Height; y++)
        {
            plan.ConvertRow(input.GetRowBytes(y), output.GetRowBytes(y));
        }
    }

    /// <summary>
    /// Validates that the image can be saved with <paramref name="encoder"/> (static outputs reject
    /// animated images, only animated PNG stores a poster) and creates the writer options of an eager save.
    /// </summary>
    private ImageWriterOptions CreateSaveOptions(ImageEncoder encoder)
    {
        _ = ImageOutputCapabilities.ForImage(encoder, this);
        return new ImageWriterOptions(_size)
        {
            Encoder = encoder,
            ExpectedFrameCount = _frames.Count,
            Metadata = _metadata,
            Animation = _animation,
            Configuration = _configuration,
            LeaveOpen = true,
        };
    }

    /// <summary>Writes the poster frame and every displayed frame, then completes the output (disposing the writer aborts it on failure).</summary>
    private void WriteTo(ImageWriterCore writer)
    {
        writer.PreflightFrames(_poster, _frames);
        if (_poster is not null)
        {
            writer.WritePosterFrame(_poster);
        }

        foreach (var frame in _frames)
        {
            writer.WriteFrame(frame);
        }

        writer.Complete();
    }

    private async Task WriteToAsync(ImageWriterCore writer, CancellationToken cancellationToken)
    {
        writer.PreflightFrames(_poster, _frames);
        if (_poster is not null)
        {
            await writer.WritePosterFrameAsync(_poster, cancellationToken).ConfigureAwait(false);
        }

        foreach (var frame in _frames)
        {
            await writer.WriteFrameAsync(frame, cancellationToken).ConfigureAwait(false);
        }

        await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveCoreAsync(string path, ImageWriterOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var writer = ImageWriterCore.Create(path, options, PixelFormat, asynchronous: true);
        await using (writer.ConfigureAwait(false))
        {
            await WriteToAsync(writer, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SaveCoreAsync(Stream stream, ImageWriterOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var writer = ImageWriterCore.Create(stream, options, PixelFormat);
        await using (writer.ConfigureAwait(false))
        {
            await WriteToAsync(writer, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateReadableStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The stream must be readable.", nameof(stream));
    }

    private static void ValidateWritableStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("The stream must be writable.", nameof(stream));
    }
}
