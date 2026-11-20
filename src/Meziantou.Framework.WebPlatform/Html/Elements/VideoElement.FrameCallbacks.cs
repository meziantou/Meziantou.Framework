using Meziantou.Framework.WebPlatform.Media;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The callback of <see cref="HtmlVideoElement.RequestVideoFrameCallback"/> (https://wicg.github.io/video-rvfc/#callbackdef-videoframerequestcallback).</summary>
/// <param name="now">The time of the rendering update, in milliseconds relative to the time origin of the window (the time given to the animation frame callbacks).</param>
/// <param name="metadata">The description of the presented frame.</param>
/// <seealso href="https://wicg.github.io/video-rvfc/#callbackdef-videoframerequestcallback"/>
[IdlInterface("VideoFrameRequestCallback", "https://wicg.github.io/video-rvfc/#callbackdef-videoframerequestcallback")]
public delegate void VideoFrameRequestCallback(double now, VideoFrameCallbackMetadata metadata);

/// <summary>The description of a frame presented by a video element (https://wicg.github.io/video-rvfc/#dictdef-videoframecallbackmetadata).</summary>
/// <seealso href="https://wicg.github.io/video-rvfc/#dictdef-videoframecallbackmetadata"/>
[IdlInterface("VideoFrameCallbackMetadata", "https://wicg.github.io/video-rvfc/#dictdef-videoframecallbackmetadata")]
public sealed class VideoFrameCallbackMetadata
{
    /// <summary>Gets the time the frame was presented, in milliseconds relative to the time origin of the window.</summary>
    /// <seealso href="https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-presentationtime"/>
    [IdlMember("presentationTime", "https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-presentationtime")]
    public required double PresentationTime { get; init; }

    /// <summary>Gets the time the frame is expected to be visible, in milliseconds relative to the time origin of the window.</summary>
    /// <seealso href="https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-expecteddisplaytime"/>
    [IdlMember("expectedDisplayTime", "https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-expecteddisplaytime")]
    public required double ExpectedDisplayTime { get; init; }

    /// <summary>Gets the width of the frame in pixels.</summary>
    /// <seealso href="https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-width"/>
    [IdlMember("width", "https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-width")]
    public required uint Width { get; init; }

    /// <summary>Gets the height of the frame in pixels.</summary>
    /// <seealso href="https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-height"/>
    [IdlMember("height", "https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-height")]
    public required uint Height { get; init; }

    /// <summary>Gets the position of the frame on the media timeline, in seconds.</summary>
    /// <seealso href="https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-mediatime"/>
    [IdlMember("mediaTime", "https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-mediatime")]
    public required double MediaTime { get; init; }

    /// <summary>Gets the number of frames presented by the element, this one included.</summary>
    /// <seealso href="https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-presentedframes"/>
    [IdlMember("presentedFrames", "https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-presentedframes")]
    public required uint PresentedFrames { get; init; }

    /// <summary>Gets the time spent decoding the frame, in seconds, or null.</summary>
    /// <seealso href="https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-processingduration"/>
    [IdlMember("processingDuration", "https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-processingduration")]
    public double? ProcessingDuration { get; init; }

    /// <summary>Gets the time the frame was captured (the frames of a local or remote track), or null.</summary>
    /// <seealso href="https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-capturetime"/>
    [IdlMember("captureTime", "https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-capturetime")]
    public double? CaptureTime { get; init; }

    /// <summary>Gets the time the frame was received (the frames of a remote track), or null.</summary>
    /// <seealso href="https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-receivetime"/>
    [IdlMember("receiveTime", "https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-receivetime")]
    public double? ReceiveTime { get; init; }

    /// <summary>Gets the RTP timestamp of the frame (the frames of a remote track), or null.</summary>
    /// <seealso href="https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-rtptimestamp"/>
    [IdlMember("rtpTimestamp", "https://wicg.github.io/video-rvfc/#dom-videoframecallbackmetadata-rtptimestamp")]
    public uint? RtpTimestamp { get; init; }
}

// The video frame callbacks (https://wicg.github.io/video-rvfc/): they run in the rendering update that follows the
// presentation of a new frame of the video, before the animation frame callbacks
public partial class HtmlVideoElement
{
    private SortedDictionary<uint, VideoFrameRequestCallback>? _frameCallbacks;
    private SortedDictionary<uint, VideoFrameRequestCallback>? _runningFrameCallbacks;
    private uint _frameCallbackIdentifier;
    private MediaVideoFrame? _lastPresentedFrame;
    private double _lastPresentedMediaTime = double.NaN;
    private uint _presentedFrames;

    /// <summary>
    /// Registers a callback that runs when a new frame of the video is presented (https://wicg.github.io/video-rvfc/#dom-htmlvideoelement-requestvideoframecallback):
    /// in the next rendering update of the window after the frame, before the animation frame callbacks and with the same
    /// time. The callback runs once; a callback registered by a callback runs with the next frame.
    /// </summary>
    /// <param name="callback">The callback.</param>
    /// <returns>The handle of the callback, for <see cref="CancelVideoFrameCallback"/>.</returns>
    /// <seealso href="https://wicg.github.io/video-rvfc/#dom-htmlvideoelement-requestvideoframecallback"/>
    [IdlMember("requestVideoFrameCallback", "https://wicg.github.io/video-rvfc/#dom-htmlvideoelement-requestvideoframecallback")]
    public uint RequestVideoFrameCallback(VideoFrameRequestCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _frameCallbackIdentifier++;
        (_frameCallbacks ??= [])[_frameCallbackIdentifier] = callback;
        NodeDocument.EventLoop?.RequestVideoFrameCallbacks(this);
        return _frameCallbackIdentifier;
    }

    /// <summary>Cancels a callback registered by <see cref="RequestVideoFrameCallback"/> (an unknown handle is ignored).</summary>
    /// <param name="handle">The handle of the callback.</param>
    /// <seealso href="https://wicg.github.io/video-rvfc/#dom-htmlvideoelement-cancelvideoframecallback"/>
    [IdlMember("cancelVideoFrameCallback", "https://wicg.github.io/video-rvfc/#dom-htmlvideoelement-cancelvideoframecallback")]
    public void CancelVideoFrameCallback(uint handle)
    {
        _frameCallbacks?.Remove(handle);
        _runningFrameCallbacks?.Remove(handle);
    }

    /// <summary>Whether callbacks wait for a new frame.</summary>
    internal bool HasVideoFrameCallbacks => _frameCallbacks is { Count: > 0 };

    /// <summary>Whether a new frame is expected soon: the playback advances, or the current frame was not presented yet.</summary>
    internal bool ExpectsNewVideoFrame => IsAdvancingPlayback || (GetVideoFrame() is { } frame && !ReferenceEquals(frame, _lastPresentedFrame));

    /// <summary>
    /// https://wicg.github.io/video-rvfc/#video-rvfc-procedures: runs the callbacks when a new frame is presented since the
    /// last rendering update (a frame of another media resource, or the frame at another position of the media timeline).
    /// </summary>
    internal void RunVideoFrameCallbacks(Window window, long frameTimestamp)
    {
        if (_frameCallbacks is not { Count: > 0 } || GetVideoFrame() is not { } frame)
            return;

        var mediaTime = PlaybackPositionForFrameCallbacks;
        if (ReferenceEquals(frame, _lastPresentedFrame) && mediaTime.Equals(_lastPresentedMediaTime))
            return;

        _lastPresentedFrame = frame;
        _lastPresentedMediaTime = mediaTime;
        _presentedFrames++;
        var now = window.ToRelativeHighResolutionTime(frameTimestamp);
        var metadata = new VideoFrameCallbackMetadata
        {
            PresentationTime = now,
            ExpectedDisplayTime = now + DomEventLoop.FrameDuration.TotalMilliseconds,
            Width = (uint)frame.Width,
            Height = (uint)frame.Height,
            MediaTime = mediaTime,
            PresentedFrames = _presentedFrames,
            ProcessingDuration = 0,
        };

        // The callbacks registered by the callbacks run with the next frame; a callback canceled by a callback does not run
        var callbacks = _frameCallbacks;
        _frameCallbacks = null;
        _runningFrameCallbacks = callbacks;
        try
        {
            foreach (var handle in callbacks.Keys.ToArray())
            {
                if (!callbacks.Remove(handle, out var callback))
                    continue;

                try
                {
                    callback(now, metadata);
                }
#pragma warning disable CA1031 // The exceptions of the callbacks are reported
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    ScriptErrors.Report(ex, window);
                }
            }
        }
        finally
        {
            _runningFrameCallbacks = null;
        }

        window.EventLoop.PerformMicrotaskCheckpoint();
    }

    /// <summary>The playback of the element started, or its ready state changed: a new frame may be presented.</summary>
    private protected override void OnVideoFrameMayChange()
    {
        if (HasVideoFrameCallbacks)
        {
            NodeDocument.EventLoop?.RequestVideoFrameCallbacks(this);
        }
    }
}

public partial class HtmlMediaElement
{
    /// <summary>Whether the playback advances: the current playback position moves with the clock.</summary>
    private protected bool IsAdvancingPlayback => _isAdvancing;

    /// <summary>The current playback position, given to the video frame callbacks.</summary>
    private protected double PlaybackPositionForFrameCallbacks => CurrentPlaybackPosition;

    /// <summary>The playback of the element started, or its ready state changed (a video element runs its video frame callbacks).</summary>
    private protected virtual void OnVideoFrameMayChange()
    {
    }
}
