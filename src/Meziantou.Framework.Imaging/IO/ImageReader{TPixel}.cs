using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>Decodes the displayed frames of an image sequentially, with memory bounded by one frame of compositor state.</summary>
/// <typeparam name="TPixel">The pixel type of the frames returned by the reader.</typeparam>
/// <remarks>
/// <para>
/// Create readers with <see cref="Image.OpenReader{TPixel}(Stream, ImageReaderOptions?)"/>. Frames are produced in playback
/// order as complete full-canvas displayed images; the reader resolves encoded delta rectangles, blending and disposal
/// internally, and its compositor state is independent of anything the caller does with returned images.
/// </para>
/// <para>
/// Images returned by <see cref="ReadFrame"/> and <see cref="ReadPosterFrame"/> are single-frame still images owned by the
/// caller: they keep the frame duration and image-wide metadata, but not the animation-wide settings, which are exposed by
/// <see cref="Info"/>. They remain valid after the reader is disposed and stay charged to the reader's allocation scope
/// until they are disposed.
/// </para>
/// <para>
/// A reader supports one operation at a time; overlapping calls throw an <see cref="InvalidOperationException"/>.
/// Argument validation failures leave the reader usable. Any other failure (malformed data, limit, I/O error, cancellation)
/// after an operation starts faults the reader: subsequent calls throw an <see cref="InvalidOperationException"/>. A
/// <see langword="null"/> or <see langword="false"/> result always means a clean end of input, never an error.
/// </para>
/// </remarks>
public sealed class ImageReader<TPixel> : IDisposable, IAsyncDisposable
    where TPixel : unmanaged
{
    private readonly ImageReaderCore _core;

    internal ImageReader(ImageReaderCore core) => _core = core;

    /// <summary>
    /// Gets the information read from the header: format, canvas size, frame count when known, animation settings and
    /// image-wide metadata found before the first frame. The snapshot does not change as frames are read.
    /// </summary>
    public ImageInfo Info => _core.Info;

    /// <summary>Gets the number of displayed frames read so far (excluding the poster frame).</summary>
    public int FramesRead => _core.FramesRead;

    /// <summary>Gets the pixel-type-independent implementation (tests inspect its scope and limits).</summary>
    internal ImageReaderCore Core => _core;

    /// <summary>
    /// Reads the separate poster frame, if the input has one. Must be called before the first displayed frame is read;
    /// it can be called at most once. Skipping it is allowed: the poster frame is then discarded.
    /// </summary>
    /// <returns>A new single-frame image owned by the caller, or <see langword="null"/> if the input has no poster frame.</returns>
    /// <exception cref="InvalidOperationException">A displayed frame was already read, the poster frame was already read, or the reader is faulted.</exception>
    /// <exception cref="InvalidImageContentException">The data is malformed or truncated.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    public Image<TPixel>? ReadPosterFrame() => (Image<TPixel>?)_core.ReadPosterFrame();

    /// <summary>Asynchronously reads the separate poster frame, if the input has one.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A new single-frame image owned by the caller, or <see langword="null"/> if the input has no poster frame.</returns>
    /// <exception cref="InvalidOperationException">A displayed frame was already read, the poster frame was already read, or the reader is faulted.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The reader is faulted.</exception>
    public async ValueTask<Image<TPixel>?> ReadPosterFrameAsync(CancellationToken cancellationToken = default)
        => (Image<TPixel>?)await _core.ReadPosterFrameAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>Reads the next displayed frame as a new image.</summary>
    /// <returns>A new single-frame image owned by the caller, or <see langword="null"/> at the clean end of input (or once <see cref="ImageReaderOptions.FrameLimit"/> frames were read).</returns>
    /// <exception cref="InvalidOperationException">Another operation is in progress or the reader is faulted.</exception>
    /// <exception cref="InvalidImageContentException">The data is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The frame uses an unsupported feature, or the conversion to <typeparamref name="TPixel"/> would drop information without an explicit policy.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    public Image<TPixel>? ReadFrame() => (Image<TPixel>?)_core.ReadFrame();

    /// <summary>Asynchronously reads the next displayed frame as a new image.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A new single-frame image owned by the caller, or <see langword="null"/> at the clean end of input.</returns>
    /// <exception cref="InvalidOperationException">Another operation is in progress or the reader is faulted.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The reader is faulted.</exception>
    public async ValueTask<Image<TPixel>?> ReadFrameAsync(CancellationToken cancellationToken = default)
        => (Image<TPixel>?)await _core.ReadFrameAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Reads the next displayed frame into an existing image, reusing its pixel storage. The destination's frame duration and
    /// image-wide metadata are replaced by those of the decoded frame.
    /// </summary>
    /// <param name="destination">
    /// A single-frame still image with the canvas size: exactly one frame, no poster frame and no animation settings. It stays
    /// charged to its own allocation scope.
    /// </param>
    /// <returns><see langword="true"/> if a frame was read; <see langword="false"/> at the clean end of input, in which case the destination is unchanged.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> does not have the canvas size, has several frames, a poster frame, or animation settings.</exception>
    /// <exception cref="InvalidOperationException">Another operation is in progress, the reader is faulted, or a lease is active on the destination.</exception>
    /// <exception cref="InvalidImageContentException">The data is malformed or truncated. The destination may be partially updated but remains valid.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <exception cref="ObjectDisposedException">The reader or the destination is disposed.</exception>
    public bool ReadFrameInto(Image<TPixel> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return _core.ReadFrameInto(destination);
    }

    /// <summary>Asynchronously reads the next displayed frame into an existing image, reusing its pixel storage.</summary>
    /// <param name="destination">A single-frame still image with the canvas size.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> if a frame was read; <see langword="false"/> at the clean end of input.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not a compatible single-frame still image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The reader is faulted; the destination may be partially updated but remains valid.</exception>
    public ValueTask<bool> ReadFrameIntoAsync(Image<TPixel> destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return _core.ReadFrameIntoAsync(destination, cancellationToken);
    }

    /// <summary>Releases the reader and its compositor state, and closes the input when owned. Images already returned remain valid.</summary>
    /// <exception cref="InvalidOperationException">An operation is in progress.</exception>
    public void Dispose() => _core.Dispose();

    /// <summary>Asynchronously releases the reader and closes the input when owned. Images already returned remain valid.</summary>
    /// <returns>A task that completes when the reader is released.</returns>
    /// <exception cref="InvalidOperationException">An operation is in progress.</exception>
    public ValueTask DisposeAsync() => _core.DisposeAsync();
}
