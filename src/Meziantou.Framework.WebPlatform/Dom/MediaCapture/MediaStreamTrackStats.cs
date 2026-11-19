namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The statistics of the frames of a video track (https://w3c.github.io/mediacapture-extensions/#the-mediastreamtrackvideostats-interface):
/// the frames produced by its source while the track is enabled, at the native frame rate of the source, and the ones
/// delivered at the frame rate of the settings of the track (the other ones are discarded by the frame rate decimation).
/// </summary>
/// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackvideostats"/>
[IdlInterface("MediaStreamTrackVideoStats", "https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackvideostats")]
public sealed class MediaStreamTrackVideoStats
{
    private readonly MediaStreamTrack _track;
    private readonly TimeProvider _clock;
    private long _lastTimestamp;
    private double _totalFrames;
    private double _deliveredFrames;
    private bool _isStable;

    internal MediaStreamTrackVideoStats(MediaStreamTrack track, TimeProvider clock)
    {
        _track = track;
        _clock = clock;
        _lastTimestamp = clock.GetTimestamp();
    }

    /// <summary>Gets the number of frames delivered to the sinks of the track.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackvideostats-deliveredframes"/>
    [IdlMember("deliveredFrames", "https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackvideostats-deliveredframes")]
    public ulong DeliveredFrames
    {
        get
        {
            Update();
            return (ulong)_deliveredFrames;
        }
    }

    /// <summary>Gets the number of frames that were not delivered (the frame rate of the track is lower than the one of its source).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackvideostats-discardedframes"/>
    [IdlMember("discardedFrames", "https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackvideostats-discardedframes")]
    public ulong DiscardedFrames
    {
        get
        {
            Update();
            return (ulong)_totalFrames - (ulong)_deliveredFrames;
        }
    }

    /// <summary>Gets the number of frames produced by the source while the track was enabled.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackvideostats-totalframes"/>
    [IdlMember("totalFrames", "https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackvideostats-totalframes")]
    public ulong TotalFrames
    {
        get
        {
            Update();
            return (ulong)_totalFrames;
        }
    }

    /// <summary>
    /// Counts the frames produced since the last update: none while the track is disabled, muted or ended. The values do not
    /// change until the next microtask checkpoint, so they are stable while a script runs.
    /// </summary>
    private void Update()
    {
        if (_isStable)
            return;

        if (_track.RelevantGlobal?.EventLoop is { } eventLoop)
        {
            _isStable = true;
            eventLoop.QueueMicrotask(() => _isStable = false);
        }

        var now = _clock.GetTimestamp();
        var elapsed = _clock.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
        _lastTimestamp = now;
        if (_track.ReadyState != MediaStreamTrackState.Live || !_track.Enabled || _track.Muted || elapsed <= 0)
            return;

        var frameRate = _track.Settings.GetValueOrDefault("frameRate") is double rate && rate > 0 ? rate : 30;
        var sourceFrameRate = Math.Max(frameRate, _track.Source.NativeFrameRate ?? frameRate);
        _totalFrames += elapsed * sourceFrameRate;
        _deliveredFrames += elapsed * frameRate;
    }
}

/// <summary>
/// The statistics of the latency of an audio track (https://w3c.github.io/mediacapture-extensions/#the-mediastreamtrackaudiostats-interface):
/// the latency of its source (its <c>latency</c> setting).
/// </summary>
/// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackaudiostats"/>
[IdlInterface("MediaStreamTrackAudioStats", "https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackaudiostats")]
public sealed class MediaStreamTrackAudioStats
{
    private readonly MediaStreamTrack _track;

    internal MediaStreamTrackAudioStats(MediaStreamTrack track) => _track = track;

    /// <summary>Gets the current latency, in milliseconds.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackaudiostats-latency"/>
    [IdlMember("latency", "https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackaudiostats-latency")]
    public double Latency => _track.Settings.GetValueOrDefault("latency") is double latency ? latency * 1000 : 0;

    /// <summary>Gets the average latency since the last reset, in milliseconds.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackaudiostats-averagelatency"/>
    [IdlMember("averageLatency", "https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackaudiostats-averagelatency")]
    public double AverageLatency => Latency;

    /// <summary>Gets the minimum latency since the last reset, in milliseconds.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackaudiostats-minimumlatency"/>
    [IdlMember("minimumLatency", "https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackaudiostats-minimumlatency")]
    public double MinimumLatency => Latency;

    /// <summary>Gets the maximum latency since the last reset, in milliseconds.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackaudiostats-maximumlatency"/>
    [IdlMember("maximumLatency", "https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackaudiostats-maximumlatency")]
    public double MaximumLatency => Latency;

    /// <summary>Resets the average, minimum and maximum latencies (the latency of the sources is constant).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackaudiostats-resetlatency"/>
    [IdlMember("resetLatency", "https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrackaudiostats-resetlatency")]
    public void ResetLatency()
    {
    }
}

// Media Capture Extensions (https://w3c.github.io/mediacapture-extensions/#mediastreamtrack-statistics)
public partial class MediaStreamTrack
{
    private object? _stats;

    /// <summary>
    /// Gets the statistics of the track (https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrack-stats): a
    /// <see cref="MediaStreamTrackVideoStats"/> for a video track, a <see cref="MediaStreamTrackAudioStats"/> for an audio track.
    /// </summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrack-stats"/>
    [IdlMember("stats", "https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrack-stats")]
    public object? Stats => !Source.IsCaptureDevice ? null : _stats ??= Source.Kind == MediaTrackKinds.Video
        ? new MediaStreamTrackVideoStats(this, RelevantGlobal?.EventLoop.TimeProvider ?? TimeProvider.System)
        : new MediaStreamTrackAudioStats(this);
}
