using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>Encodes frames sequentially, keeping only bounded private state between frames.</summary>
/// <typeparam name="TPixel">The pixel type of the frames written.</typeparam>
/// <remarks>
/// <para>
/// Create writers with <see cref="Image.CreateWriter{TPixel}(Stream, ImageWriterOptions)"/>. Write the optional poster frame
/// first (animated PNG only), then the displayed frames in playback order, then call <see cref="Complete"/>. Each frame must
/// have the canvas size; its duration is taken from <see cref="ImageFrame.Metadata"/>. A frame is only borrowed until the
/// write call (or the returned task) completes; the writer never retains caller frames.
/// </para>
/// <para>
/// <see cref="Complete"/> is mandatory: disposing a writer that was not completed aborts the output instead of finalizing
/// it. For path outputs, an aborted or failed writer never replaces an existing destination file. For stream outputs,
/// bytes already written are not removed, but the output is never finalized as a valid file.
/// </para>
/// <para>
/// A writer supports one operation at a time; overlapping calls throw an <see cref="InvalidOperationException"/>.
/// Argument validation failures leave the writer usable; any failure after an operation starts faults the writer.
/// </para>
/// </remarks>
public sealed class ImageWriter<TPixel> : IDisposable, IAsyncDisposable
    where TPixel : unmanaged
{
    private readonly ImageWriterCore _core;

    internal ImageWriter(ImageWriterCore core) => _core = core;

    /// <summary>Gets the canvas size of every frame.</summary>
    public Size CanvasSize => _core.CanvasSize;

    /// <summary>Gets the output format.</summary>
    public ImageFormat Format => _core.Format;

    /// <summary>Gets the number of displayed frames written so far (excluding the poster frame).</summary>
    public int FramesWritten => _core.FramesWritten;

    /// <summary>Gets the pixel-type-independent implementation (tests inspect its scope and temporary file).</summary>
    internal ImageWriterCore Core => _core;

    /// <summary>Writes the separate poster frame. Only animated PNG output supports it, and it must be written before the first displayed frame.</summary>
    /// <param name="frame">The poster frame. Its duration is ignored.</param>
    /// <exception cref="ArgumentException"><paramref name="frame"/> does not have the canvas size.</exception>
    /// <exception cref="InvalidOperationException">A displayed frame or a poster frame was already written, the output format does not support poster frames, or the writer is faulted or completed.</exception>
    /// <exception cref="ObjectDisposedException">The writer is disposed.</exception>
    public void WritePosterFrame(ImageFrame<TPixel> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _core.WritePosterFrame(frame);
    }

    /// <summary>Asynchronously writes the separate poster frame.</summary>
    /// <param name="frame">The poster frame, borrowed until the returned task completes.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the frame has been written.</returns>
    /// <exception cref="ArgumentException"><paramref name="frame"/> does not have the canvas size.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The writer is faulted.</exception>
    public ValueTask WritePosterFrameAsync(ImageFrame<TPixel> frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return _core.WritePosterFrameAsync(frame, cancellationToken);
    }

    /// <summary>Writes the next displayed frame.</summary>
    /// <param name="frame">The frame. Its pixels and duration are encoded; it is not retained.</param>
    /// <exception cref="ArgumentException"><paramref name="frame"/> does not have the canvas size.</exception>
    /// <exception cref="InvalidOperationException">The expected frame count was already reached, the format supports a single frame, or the writer is faulted or completed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The frame cannot be encoded without a loss that the encoder settings do not allow.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    /// <exception cref="ObjectDisposedException">The writer is disposed.</exception>
    public void WriteFrame(ImageFrame<TPixel> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _core.WriteFrame(frame);
    }

    /// <summary>Asynchronously writes the next displayed frame.</summary>
    /// <param name="frame">The frame, borrowed until the returned task completes.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the frame has been written.</returns>
    /// <exception cref="ArgumentException"><paramref name="frame"/> does not have the canvas size.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The writer is faulted.</exception>
    public ValueTask WriteFrameAsync(ImageFrame<TPixel> frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return _core.WriteFrameAsync(frame, cancellationToken);
    }

    /// <summary>
    /// Verifies that at least one displayed frame (and exactly the expected count, if specified) was written, writes the
    /// trailing data and flushes the output. For path outputs, publishes the file. Calling it again after success has no effect.
    /// </summary>
    /// <exception cref="InvalidOperationException">No frame was written, the frame count differs from <see cref="ImageWriterOptions.ExpectedFrameCount"/>, or the writer is faulted.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    /// <exception cref="ObjectDisposedException">The writer is disposed.</exception>
    public void Complete() => _core.Complete();

    /// <summary>Asynchronously completes the output. See <see cref="Complete"/>.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the output is finalized.</returns>
    /// <exception cref="InvalidOperationException">No frame was written, the frame count differs from the expected count, or the writer is faulted.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The output is not published.</exception>
    public ValueTask CompleteAsync(CancellationToken cancellationToken = default) => _core.CompleteAsync(cancellationToken);

    /// <summary>Releases the writer. If <see cref="Complete"/> did not succeed, the output is aborted: a path output's temporary file is deleted.</summary>
    /// <exception cref="InvalidOperationException">An operation is in progress.</exception>
    public void Dispose() => _core.Dispose();

    /// <summary>Asynchronously releases the writer, aborting the output if it was not completed.</summary>
    /// <returns>A task that completes when the writer is released.</returns>
    /// <exception cref="InvalidOperationException">An operation is in progress.</exception>
    public ValueTask DisposeAsync() => _core.DisposeAsync();
}
