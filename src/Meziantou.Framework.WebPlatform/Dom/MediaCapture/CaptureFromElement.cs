using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The video track of a stream captured from a canvas (https://w3c.github.io/mediacapture-fromelement/#the-canvascapturemediastreamtrack):
/// its frames are the content of the canvas, captured when it changes (or when <see cref="RequestFrame"/> is called).
/// </summary>
/// <seealso href="https://w3c.github.io/mediacapture-fromelement/#dom-canvascapturemediastreamtrack"/>
[IdlInterface("CanvasCaptureMediaStreamTrack", "https://w3c.github.io/mediacapture-fromelement/#dom-canvascapturemediastreamtrack")]
public sealed class CanvasCaptureMediaStreamTrack : MediaStreamTrack
{
    internal CanvasCaptureMediaStreamTrack(GlobalScope global, CanvasCaptureTrackSource source, MediaTrackConstraints? constraints = null, Dictionary<string, object>? settings = null)
        : base(global, source, constraints, settings)
    {
    }

    /// <summary>Gets the canvas whose content is captured.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-fromelement/#dom-canvascapturemediastreamtrack-canvas"/>
    [IdlMember("canvas", "https://w3c.github.io/mediacapture-fromelement/#dom-canvascapturemediastreamtrack-canvas")]
    public HtmlCanvasElement Canvas => ((CanvasCaptureTrackSource)Source).Canvas;

    /// <summary>Captures the content of the canvas at the next rendering, even when it did not change (https://w3c.github.io/mediacapture-fromelement/#dom-canvascapturemediastreamtrack-requestframe).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-fromelement/#dom-canvascapturemediastreamtrack-requestframe"/>
    [IdlMember("requestFrame", "https://w3c.github.io/mediacapture-fromelement/#dom-canvascapturemediastreamtrack-requestframe")]
    public void RequestFrame() => ((CanvasCaptureTrackSource)Source).RequestFrame();

    private protected override MediaStreamTrack CreateClone(GlobalScope global) => new CanvasCaptureMediaStreamTrack(global, (CanvasCaptureTrackSource)Source, GetConstraints(), new Dictionary<string, object>(Settings, StringComparer.Ordinal));
}

/// <summary>The source of a <see cref="CanvasCaptureMediaStreamTrack"/>: the content of a canvas, captured when it changes.</summary>
internal sealed class CanvasCaptureTrackSource : MediaStreamTrackSource
{
    private readonly double? _frameRequestRate;
    private bool _frameRequested;
    private MediaVideoFrame? _frame;

    public CanvasCaptureTrackSource(HtmlCanvasElement canvas, double? frameRequestRate)
        : base(MediaTrackKinds.Video, "")
    {
        Canvas = canvas;
        _frameRequestRate = frameRequestRate;
    }

    public HtmlCanvasElement Canvas { get; }

    public override Dictionary<string, object> GetInitialSettings()
    {
        var settings = new Dictionary<string, object>(StringComparer.Ordinal);
        UpdateSettings(settings);
        if (_frameRequestRate is { } rate)
        {
            settings["frameRate"] = rate;
        }

        return settings;
    }

    /// <summary>The size of the frames follows the size of the canvas.</summary>
    public override void UpdateSettings(Dictionary<string, object> settings)
    {
        var (width, height) = Canvas.BitmapSize;
        settings["width"] = (double)width;
        settings["height"] = (double)height;
    }

    /// <summary>The content of the canvas changed: a frame is captured, unless the frames are only captured on request (a frame rate of 0).</summary>
    public void OnCanvasChanged()
    {
        if (_frameRequestRate is not 0)
        {
            _frameRequested = true;
            NotifyTracksChanged();
        }
    }

    public void RequestFrame() => _frameRequested = true;

    public override MediaVideoFrame? GetVideoFrame()
    {
        if (_frameRequested && !IsStopped)
        {
            _frameRequested = false;
            var pixels = Canvas.GetBitmapSnapshot();
            if (pixels.Width > 0 && pixels.Height > 0)
            {
                _frame = new MediaVideoFrame(pixels.Width, pixels.Height, pixels.Rgba);
            }
        }

        return _frame;
    }

    protected override void OnStopped() => Canvas.RemoveCaptureSource(this);
}

/// <summary>The source of a track captured from a media element: its video frames (the audio of the media resources is not decoded).</summary>
internal sealed class MediaElementCaptureTrackSource : MediaStreamTrackSource
{
    private readonly HtmlMediaElement _element;

    public MediaElementCaptureTrackSource(HtmlMediaElement element, MediaTrackKinds kind, bool muted)
        : base(kind, "")
    {
        _element = element;
        if (muted)
        {
            SetMuted(muted: true);
        }
    }

    public override Dictionary<string, object> GetInitialSettings()
    {
        var settings = new Dictionary<string, object>(StringComparer.Ordinal);
        UpdateSettings(settings);
        return settings;
    }

    public override void UpdateSettings(Dictionary<string, object> settings)
    {
        if (Kind == MediaTrackKinds.Video && _element is HtmlVideoElement video && video.VideoWidth > 0 && video.VideoHeight > 0)
        {
            settings["width"] = (double)video.VideoWidth;
            settings["height"] = (double)video.VideoHeight;
        }
    }

    public override MediaVideoFrame? GetVideoFrame() => _element.GetCurrentVideoFrame();

    // The audio of a media stream played by the element is captured (the audio of the other resources is not decoded)
    public override int AudioChannelCount => _element.GetStreamAudioTrack()?.Source.AudioChannelCount ?? 1;

    public override int AudioSampleRate => _element.GetStreamAudioTrack()?.Source.AudioSampleRate ?? 48000;

    public override long AudioPosition => _element.GetStreamAudioTrack()?.Source.AudioPosition ?? 0;

    public override void ReadAudio(long position, Span<float> destination)
    {
        if (_element.GetStreamAudioTrack() is { } track)
        {
            track.ReadAudio(position, destination);
        }
        else
        {
            destination.Clear();
        }
    }

    /// <summary>The captured resource ended or changed: the tracks end (and fire <c>ended</c>).</summary>
    public void End() => EndAllTracks();
}

// Media Capture from DOM Elements (https://w3c.github.io/mediacapture-fromelement/#html-canvas-element-media-capture-extensions)
public partial class HtmlCanvasElement
{
    private List<CanvasCaptureTrackSource>? _captureSources;
    private bool _hasBeenPainted;

    /// <summary>
    /// Captures the content of the canvas as a stream with one video track, a <see cref="CanvasCaptureMediaStreamTrack"/>
    /// (https://w3c.github.io/mediacapture-fromelement/#dom-htmlcanvaselement-capturestream): a frame is captured each time
    /// the canvas changes.
    /// </summary>
    /// <param name="frameRequestRate">The maximum number of frames per second, or 0 to capture the frames only when <see cref="CanvasCaptureMediaStreamTrack.RequestFrame"/> is called.</param>
    /// <exception cref="DomException">A <c>SecurityError</c>: the canvas is not origin-clean; a <c>NotSupportedError</c>: the frame rate is negative.</exception>
    /// <seealso href="https://w3c.github.io/mediacapture-fromelement/#dom-htmlcanvaselement-capturestream"/>
    [IdlMember("captureStream", "https://w3c.github.io/mediacapture-fromelement/#dom-htmlcanvaselement-capturestream")]
    public MediaStream CaptureStream(double? frameRequestRate = null)
    {
        if (!GetBitmapSnapshot().IsOriginClean)
            throw new DomException("The canvas is not origin-clean", "SecurityError");

        if (frameRequestRate < 0)
            throw DomException.NotSupported("The frame rate cannot be negative");

        var global = GlobalScope.Current ?? RelevantGlobal ?? throw DomException.InvalidState("The canvas has no global object");
        var source = new CanvasCaptureTrackSource(this, frameRequestRate);
        (_captureSources ??= []).Add(source);

        // The content that was painted before is the first frame
        if (_hasBeenPainted)
        {
            source.RequestFrame();
        }

        return new MediaStream(global, [new CanvasCaptureMediaStreamTrack(global, source)]);
    }

    internal void RemoveCaptureSource(CanvasCaptureTrackSource source) => _captureSources?.Remove(source);

    /// <summary>The content of the canvas changed: the capture tracks capture a new frame.</summary>
    private void NotifyCaptureSources()
    {
        _hasBeenPainted = true;
        if (_captureSources is { } sources)
        {
            foreach (var source in sources)
            {
                source.OnCanvasChanged();
            }
        }
    }
}

// Media Capture from DOM Elements (https://w3c.github.io/mediacapture-fromelement/#html-media-element-media-capture-extensions)
public partial class HtmlMediaElement
{
    private List<(MediaStream Stream, List<(MediaStreamTrack Track, MediaElementCaptureTrackSource Source)> Tracks)>? _captureStreams;
    private bool _captureUpdateQueued;
    private bool _captureUpdatePending;

    // The player whose media the capture tracks have: the tracks end when the resource changes
    private MediaPlayer? _capturedPlayer;

    /// <summary>
    /// Captures the media of the element as a stream (https://w3c.github.io/mediacapture-fromelement/#dom-htmlmediaelement-capturestream):
    /// a track is added for the video and the audio of the media resource while it plays (<c>addtrack</c>); the tracks end
    /// and are removed (<c>removetrack</c>) when the playback ends or the resource changes. The video of a resource that is
    /// not CORS-same-origin is captured as a muted track.
    /// </summary>
    /// <seealso href="https://w3c.github.io/mediacapture-fromelement/#dom-htmlmediaelement-capturestream"/>
    [IdlMember("captureStream", "https://w3c.github.io/mediacapture-fromelement/#dom-htmlmediaelement-capturestream")]
    public MediaStream CaptureStream()
    {
        var global = GlobalScope.Current ?? RelevantGlobal ?? throw DomException.InvalidState("The element has no global object");
        var stream = new MediaStream(global, []);
        (_captureStreams ??= []).Add((stream, []));
        ScheduleCaptureUpdate();
        return stream;
    }

    /// <summary>The audio track of the media stream played by the element, or null.</summary>
    internal MediaStreamTrack? GetStreamAudioTrack() => (_player as MediaStreamPlayer)?.GetAudioTrack();

    /// <summary>The current frame of the video of the element (for the capture tracks), or null.</summary>
    internal MediaVideoFrame? GetCurrentVideoFrame() => IsMediaDataCorsSameOrigin ? _player?.GetVideoFrame() : null;

    /// <summary>The state of the element changed: the tracks of its capture streams are updated (once the current update is done, when it fires events).</summary>
    private void ScheduleCaptureUpdate()
    {
        if (_captureStreams is null)
            return;

        if (_captureUpdateQueued)
        {
            _captureUpdatePending = true;
            return;
        }

        _captureUpdateQueued = true;
        try
        {
            do
            {
                _captureUpdatePending = false;
                UpdateCaptureTracks();
            }
            while (_captureUpdatePending);
        }
        finally
        {
            _captureUpdateQueued = false;
        }
    }

    private void UpdateCaptureTracks()
    {
        if (_captureStreams is not { } captures || RelevantGlobal is not { } global)
            return;

        var capturing = _player is not null && _readyState >= HaveMetadata && !HasEndedPlayback;
        var hasVideo = capturing && this is HtmlVideoElement && VideoTracks.Length > 0;
        var hasAudio = capturing && AudioTracks.Length > 0;
        var resourceChanged = !ReferenceEquals(_capturedPlayer, _player);
        _capturedPlayer = capturing ? _player : null;
        foreach (var (stream, tracks) in captures)
        {
            if (tracks.Count > 0 && ((!hasVideo && !hasAudio) || resourceChanged))
            {
                // A captured track ends before it is removed from the stream
                foreach (var (track, source) in tracks)
                {
                    source.End();
                    track.Stop();
                }

                stream.RemoveTracksByUserAgent([.. tracks.Select(t => t.Track)]);

                tracks.Clear();
            }

            if (tracks.Count > 0 || (!hasVideo && !hasAudio))
                continue;

            foreach (var kind in (ReadOnlySpan<MediaTrackKinds>)[MediaTrackKinds.Video, MediaTrackKinds.Audio])
            {
                if ((kind == MediaTrackKinds.Video && !hasVideo) || (kind == MediaTrackKinds.Audio && !hasAudio))
                    continue;

                var source = new MediaElementCaptureTrackSource(this, kind, muted: !IsMediaDataCorsSameOrigin);
                tracks.Add((new MediaStreamTrack(global, source), source));
            }

            stream.AddTracksByUserAgent([.. tracks.Select(t => t.Track)]);
        }
    }
}
