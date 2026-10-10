using System.Diagnostics;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The state shared by a sequential reader, the container walker and the decoder sink of one input: the header snapshot,
/// the target requested by the current reader call, the completed image and the yield signal.
/// </summary>
/// <remarks>
/// <para>
/// Sequential decoding reuses the eager pipeline unchanged — the same <see cref="ImageCodec.CreateDecodeParser"/>, container
/// walker and decode observer — driven by <see cref="SequentialDecodeParser"/> one step at a time:
/// </para>
/// <list type="number">
/// <item><description>
/// Container walkers (or codec parsers) call <see cref="OnHeader"/> with the header-mode <see cref="ImageInfo"/> right before
/// their header callback, then stop at the next yield point (<see cref="ImageCodecContext.TryConsumeYield"/>): the reader is
/// open, its <c>Info</c> is known and no pixel was produced.
/// </description></item>
/// <item><description>
/// Each reader call sets a request (<see cref="BeginRequest"/>) and resumes the walk. The decoder's
/// <see cref="SequentialFrameSink"/> binds the next image to the request and, at <see cref="DecodedFrameSink.EndImage"/>,
/// hands it over (<see cref="OnImageCompleted"/>) and requests a yield; the walker returns right after the image end,
/// without beginning the next image, so memory stays bounded by one frame of decoder/compositor state.
/// </description></item>
/// <item><description>A walk that completes without a pending image is the clean end of input.</description></item>
/// </list>
/// </remarks>
internal sealed class SequentialDecodeSession
{
    private SequentialDecodeEvent? _pendingEvent;
    private Image? _completedImage;
    private bool _completedIsPoster;
    private bool _completedIsDestination;
    private bool _yieldRequested;

    /// <summary>Gets the header snapshot reported by the container walker, or <see langword="null"/> before the header is parsed.</summary>
    public ImageInfo? HeaderInfo { get; private set; }

    /// <summary>Gets a private copy of the header metadata, given to the images produced by the reader (never the caller-visible <see cref="ImageInfo.Metadata"/>).</summary>
    public ImageMetadata HeaderMetadata { get; private set; } = new();

    /// <summary>Gets the request of the reader call in progress.</summary>
    public SequentialRequest Request { get; private set; }

    /// <summary>Gets the caller image of a <c>ReadFrameInto</c> call, or <see langword="null"/> when a new image must be created.</summary>
    public Image? Destination { get; private set; }

    /// <summary>Gets a value indicating whether a step result is waiting to be returned to the reader.</summary>
    public bool HasPendingEvent => _pendingEvent is not null;

    /// <summary>Reports the header snapshot. Called once, before the decoder's header callback.</summary>
    /// <param name="info">The header-mode information (frame count when declared, poster presence, animation settings, metadata before the first image).</param>
    /// <exception cref="InvalidOperationException">The header was already reported.</exception>
    public void OnHeader(ImageInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (HeaderInfo is not null)
            throw new InvalidOperationException("The header was already reported.");

        HeaderInfo = info;
        HeaderMetadata = info.Metadata.Clone();
        _pendingEvent = SequentialDecodeEvent.Header;
        _yieldRequested = true;
    }

    /// <summary>Consumes the yield request, if any.</summary>
    /// <returns><see langword="true"/> if the walker must return to the reader.</returns>
    public bool TryConsumeYield()
    {
        if (!_yieldRequested)
            return false;

        _yieldRequested = false;
        return true;
    }

    /// <summary>Hands a completed image (poster or displayed frame) to the reader and requests a yield.</summary>
    /// <exception cref="InvalidOperationException">A previous result was not returned to the reader: the walker did not yield.</exception>
    internal void OnImageCompleted(Image image, bool isPoster, bool isDestination)
    {
        EnsureNoPendingEvent();
        _completedImage = image;
        _completedIsPoster = isPoster;
        _completedIsDestination = isDestination;
        _pendingEvent = SequentialDecodeEvent.Image;
        _yieldRequested = true;
    }

    /// <exception cref="InvalidOperationException">A completed image is pending: the walker began another image without yielding.</exception>
    internal void EnsureNoPendingEvent()
    {
        // A pending header is fine: a decoder may begin its first image in the header callback (bound later)
        if (_pendingEvent == SequentialDecodeEvent.Image)
            throw new InvalidOperationException("The decoder began a new image before the previous result was returned to the reader: the container walker must yield after each image (ImageCodecContext.TryConsumeYield).");
    }

    internal void BeginRequest(SequentialRequest request, Image? destination)
    {
        Debug.Assert(request != SequentialRequest.None);
        Debug.Assert(destination is null || request == SequentialRequest.Frame);
        Request = request;
        Destination = destination;
    }

    internal void EndRequest()
    {
        Request = SequentialRequest.None;
        Destination = null;
    }

    /// <summary>Takes the pending step result, clearing the yield request.</summary>
    internal bool TryTakeEvent(out SequentialDecodeEvent result)
    {
        _yieldRequested = false;
        if (_pendingEvent is { } pending)
        {
            _pendingEvent = null;
            result = pending;
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>Takes the completed image of the last <see cref="SequentialDecodeEvent.Image"/> step.</summary>
    internal Image TakeImage(out bool isPoster)
    {
        var image = _completedImage ?? throw new InvalidOperationException("No image was completed.");
        _completedImage = null;
        isPoster = _completedIsPoster;
        return image;
    }

    /// <summary>Releases a completed image that was never returned (the reader faulted or was disposed).</summary>
    internal void ReleasePendingImage()
    {
        var image = _completedImage;
        _completedImage = null;
        if (image is not null && !_completedIsDestination)
        {
            image.Dispose();
        }
    }
}
