using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The source of tracks (https://w3c.github.io/mediacapture-main/#source): a capture device, a canvas, a media element, a
/// Web Audio graph, a <c>VideoTrackGenerator</c>... A source is shared by the tracks created from it (the clones of a
/// track), and stops when its last track stops. The subclasses produce the media (<see cref="GetVideoFrame"/>,
/// <see cref="ReadAudio"/>) and, when they are constrainable, describe their properties (<see cref="Model"/>).
/// </summary>
/// <remarks>
/// The sources of the remote tracks of WebRTC and of the <c>VideoFrame</c> objects of WebCodecs are other subclasses: the
/// tracks and their sinks (the media elements, <c>ImageCapture</c>, the Web Audio nodes, <c>MediaStreamTrackProcessor</c>)
/// only use this class.
/// </remarks>
internal abstract class MediaStreamTrackSource
{
    private readonly List<MediaStreamTrack> _tracks = [];

    protected MediaStreamTrackSource(MediaTrackKinds kind, string label)
    {
        Kind = kind;
        Label = label;
    }

    /// <summary>The kind of the tracks of the source.</summary>
    public MediaTrackKinds Kind { get; }

    /// <summary>The label of the tracks of the source.</summary>
    public string Label { get; }

    /// <summary>Whether the source stopped: its last track stopped, or the source ended.</summary>
    public bool IsStopped { get; private set; }

    /// <summary>Whether the source is muted (https://w3c.github.io/mediacapture-main/#source-muted).</summary>
    public bool IsMuted { get; private set; }

    /// <summary>The tracks of the source that are not ended.</summary>
    public IReadOnlyList<MediaStreamTrack> Tracks => _tracks;

    /// <summary>The constrainable properties of the source, or null when the constraints cannot change its settings.</summary>
    public virtual MediaTrackSourceModel? Model => null;

    /// <summary>
    /// The model in which the constraints of a track select its settings (<c>applyConstraints()</c>): the settings that the
    /// source can give to the track without changing the media of its other tracks in a way they cannot use.
    /// </summary>
    public virtual MediaTrackSourceModel? GetModelForTrack(MediaStreamTrack track) => Model;

    /// <summary>The values of the properties that the constraints cannot change (e.g. the <c>deviceId</c> of a device), or null.</summary>
    public virtual IReadOnlyDictionary<string, object>? FixedValues => null;

    /// <summary>The capture device of the source, or null when it is not a capture device.</summary>
    public virtual MediaCaptureDevice? Device => null;

    /// <summary>Whether the tracks of the source are capture tracks of a camera, a microphone or a screen (their settings expose the identifiers of the device).</summary>
    public bool IsCaptureDevice => Device is not null;

    /// <summary>The settings of a new track of the source (https://w3c.github.io/mediacapture-main/#dfn-settings).</summary>
    public abstract Dictionary<string, object> GetInitialSettings();

    /// <summary>Updates the settings of a track with the values that follow the state of the source (e.g. the size of a canvas).</summary>
    public virtual void UpdateSettings(Dictionary<string, object> settings)
    {
    }

    /// <summary>The capabilities of the source (https://w3c.github.io/mediacapture-main/#dfn-capabilities).</summary>
    public virtual MediaTrackCapabilities GetCapabilities() => Model?.CreateCapabilities(FixedValues?.GetValueOrDefault("deviceId") as string, FixedValues?.GetValueOrDefault("groupId") as string) ?? new MediaTrackCapabilities();

    /// <summary>The constraints of a track changed: the source applies the settings selected for it.</summary>
    public virtual void ApplySettings(MediaStreamTrack track, IReadOnlyDictionary<string, object> settings)
    {
    }

    /// <summary>The current video frame of the source, or null when it has none.</summary>
    public virtual MediaVideoFrame? GetVideoFrame() => null;

    /// <summary>The frame rate at which the source produces its video frames, or null when it is the one of the settings of its tracks.</summary>
    public virtual double? NativeFrameRate => Model is { VideoModes.Count: > 0 } model ? model.VideoModes.Max(mode => mode.FrameRate) : null;

    /// <summary>The number of audio channels of the source.</summary>
    public virtual int AudioChannelCount => 1;

    /// <summary>The sample rate of the audio of the source, in Hz.</summary>
    public virtual int AudioSampleRate => 48000;

    /// <summary>Reads the interleaved audio samples of the source from a position (in sample frames since the start of the source). The default is silence.</summary>
    public virtual void ReadAudio(long position, Span<float> destination) => destination.Clear();

    /// <summary>The position of the audio of the source now, in sample frames since its start.</summary>
    public virtual long AudioPosition => 0;

    internal void AddTrack(MediaStreamTrack track) => _tracks.Add(track);

    /// <summary>A track of the source ended: the source stops when it was the last one (https://w3c.github.io/mediacapture-main/#source-stopped).</summary>
    internal void RemoveTrack(MediaStreamTrack track)
    {
        _tracks.Remove(track);
        if (_tracks.Count == 0 && !IsStopped)
        {
            IsStopped = true;
            OnStopped();
        }
    }

    /// <summary>The source stopped: the device is released.</summary>
    protected virtual void OnStopped()
    {
    }

    /// <summary>Changes whether the source is muted: its tracks fire <c>mute</c> or <c>unmute</c> (https://w3c.github.io/mediacapture-main/#set-track-muted).</summary>
    protected void SetMuted(bool muted)
    {
        if (IsMuted == muted)
            return;

        IsMuted = muted;
        foreach (var track in _tracks.ToArray())
        {
            track.OnSourceMutedChanged(muted);
        }
    }

    /// <summary>The media of the source changed (e.g. the size of a canvas): the sinks of its tracks update their state.</summary>
    protected void NotifyTracksChanged()
    {
        foreach (var track in _tracks.ToArray())
        {
            track.OnSourceChanged();
        }
    }

    /// <summary>The source ended (e.g. the device was removed): its tracks end (https://w3c.github.io/mediacapture-main/#track-ended).</summary>
    protected void EndAllTracks()
    {
        foreach (var track in _tracks.ToArray())
        {
            track.OnSourceEnded();
        }
    }

    /// <summary>Stops the source and ends its tracks without firing <c>ended</c> (e.g. the document is unloaded).</summary>
    internal void StopAllTracks()
    {
        foreach (var track in _tracks.ToArray())
        {
            track.Stop();
        }
    }
}

/// <summary>A source whose media is a capture device of the <see cref="MediaCaptureBackend"/>: a camera, a microphone or a display surface.</summary>
internal sealed class CaptureDeviceTrackSource : MediaStreamTrackSource
{
    private readonly MediaDevices _mediaDevices;
    private readonly MediaCaptureSource _capture;
    private readonly MediaTrackSourceModel _model;
    private readonly Dictionary<string, object> _fixedValues;
    private readonly TimeProvider _clock;
    private readonly long _startTimestamp;
    private Dictionary<string, object> _settings;

    public CaptureDeviceTrackSource(MediaDevices mediaDevices, MediaCaptureDevice device, MediaCaptureSource capture, MediaTrackSourceModel model, Dictionary<string, object> settings, Dictionary<string, object> fixedValues, TimeProvider clock)
        : base(device.Kind == MediaDeviceKind.AudioInput ? MediaTrackKinds.Audio : MediaTrackKinds.Video, device.Label)
    {
        _mediaDevices = mediaDevices;
        Device = device;
        _capture = capture;
        _model = model;
        _settings = settings;
        _fixedValues = fixedValues;
        _clock = clock;
        _startTimestamp = clock.GetTimestamp();
        capture.SetCallbacks(
            muted => mediaDevices.QueueTask(() => SetMuted(capture.IsMuted)),
            () => mediaDevices.QueueTask(EndAllTracks));

        // The state the backend set before the callbacks were registered (e.g. during StartCapture()): a change that races
        // with the registration is reported by a callback too, whose task reads the state again
        if (capture.IsMuted)
        {
            SetMuted(muted: true);
        }

        if (capture.IsEnded)
        {
            // The tracks of the source are created after it: they end by a task
            mediaDevices.QueueTask(EndAllTracks);
        }
    }

    public override MediaCaptureDevice Device { get; }

    public override MediaTrackSourceModel Model => _model;

    public override IReadOnlyDictionary<string, object> FixedValues => _fixedValues;

    /// <summary>
    /// The settings of a track that shares the device with other live tracks come from the native modes that can also give
    /// their media to these tracks (by cropping, scaling down or dropping frames): constraints that only another mode can
    /// satisfy are rejected with an <c>OverconstrainedError</c>, without changing the device nor the other tracks
    /// (https://w3c.github.io/mediacapture-main/#dfn-applyconstraints-algorithm).
    /// </summary>
    public override MediaTrackSourceModel? GetModelForTrack(MediaStreamTrack track)
    {
        if (_model.VideoModes.Count == 0)
            return _model;

        var others = Tracks.Where(other => other != track).ToList();
        if (others.Count == 0)
            return _model;

        var modes = _model.VideoModes.Where(mode => others.TrueForAll(other => Covers(mode, other.Settings))).ToList();
        if (modes.Count == _model.VideoModes.Count)
            return _model;

        if (modes.Count == 0)
        {
            // Only the tracks created when the device had other modes have no common mode: the largest one
            modes.Add(GetLargestMode());
        }

        return new MediaTrackSourceModel
        {
            Kind = _model.Kind,
            Capabilities = _model.Capabilities,
            Defaults = _model.Defaults,
            VideoModes = modes,
        };
    }

    public override Dictionary<string, object> GetInitialSettings() => new(_settings, StringComparer.Ordinal);

    /// <summary>
    /// The settings of a track changed: the device is configured with settings that all its live tracks can use (the other
    /// values come from the track whose constraints changed). When the tracks have different sizes or frame rates, the
    /// device runs in the smallest native mode that can give their media to all of them (by cropping, scaling down or
    /// dropping frames), and the audio has the largest number of channels of the tracks. Each track adapts the media to its
    /// own settings: the frames are scaled to its size, and the audio is mixed to its number of channels (see
    /// <see cref="MediaStreamTrack.GetVideoFrame"/> and <see cref="MediaStreamTrack.ReadAudio"/>).
    /// </summary>
    public override void ApplySettings(MediaStreamTrack track, IReadOnlyDictionary<string, object> settings)
    {
        var combined = new Dictionary<string, object>(settings, StringComparer.Ordinal);
        var tracksSettings = Tracks.Select(other => other == track ? settings : other.Settings).ToList();
        foreach (var other in tracksSettings)
        {
            if (other.GetValueOrDefault("channelCount") is double channels && (combined.GetValueOrDefault("channelCount") is not double current || channels > current))
            {
                combined["channelCount"] = channels;
            }
        }

        if (_model.VideoModes.Count > 0 && tracksSettings.Exists(other => !SameVideoMode(other, settings)))
        {
            var mode = _model.VideoModes
                .Where(mode => tracksSettings.TrueForAll(other => Covers(mode, other)))
                .OrderBy(mode => (long)mode.Width * mode.Height)
                .ThenBy(mode => mode.FrameRate)
                .FirstOrDefault();

            // The settings of the tracks come from modes that can give their media to all of them (see GetModelForTrack): the
            // largest mode is for the tracks created when the device had other modes
            mode ??= GetLargestMode();
            combined["width"] = (double)mode.Width;
            combined["height"] = (double)mode.Height;
            combined["frameRate"] = mode.FrameRate;
            combined["resizeMode"] = "none";
        }
        else if (_model.VideoModes.Count == 0)
        {
            foreach (var other in tracksSettings)
            {
                foreach (var name in (ReadOnlySpan<string>)["width", "height", "frameRate"])
                {
                    if (other.GetValueOrDefault(name) is double value && (combined.GetValueOrDefault(name) is not double current || value > current))
                    {
                        combined[name] = value;
                    }
                }
            }
        }

        if (combined.GetValueOrDefault("width") is double width && combined.GetValueOrDefault("height") is double height && height > 0)
        {
            combined["aspectRatio"] = width / height;
        }

        _settings = combined;
        _capture.ApplySettings(MediaTrackConstrainableProperty.CreateSettings(combined));

        static bool SameVideoMode(IReadOnlyDictionary<string, object> a, IReadOnlyDictionary<string, object> b)
        {
            foreach (var name in (ReadOnlySpan<string>)["width", "height", "frameRate"])
            {
                if (!Equals(a.GetValueOrDefault(name), b.GetValueOrDefault(name)))
                    return false;
            }

            return true;
        }
    }

    private MediaCaptureVideoMode GetLargestMode() => _model.VideoModes.OrderByDescending(mode => (long)mode.Width * mode.Height).ThenByDescending(mode => mode.FrameRate).First();

    /// <summary>Whether a native mode can give its media to a track: its frames can be cropped and scaled down to the size of the track, and its frame rate decimated to the one of the track.</summary>
    private static bool Covers(MediaCaptureVideoMode mode, IReadOnlyDictionary<string, object> settings)
    {
        return (settings.GetValueOrDefault("width") is not double width || width <= mode.Width)
            && (settings.GetValueOrDefault("height") is not double height || height <= mode.Height)
            && (settings.GetValueOrDefault("frameRate") is not double frameRate || frameRate <= mode.FrameRate + 1e-9);
    }

    public override MediaVideoFrame? GetVideoFrame() => IsStopped ? null : _capture.GetVideoFrame(_clock.GetElapsedTime(_startTimestamp));

    public override int AudioChannelCount => _settings.GetValueOrDefault("channelCount") is double channels ? Math.Max(1, (int)channels) : 1;

    public override int AudioSampleRate => _settings.GetValueOrDefault("sampleRate") is double sampleRate ? Math.Max(1, (int)sampleRate) : 48000;

    public override long AudioPosition => (long)(_clock.GetElapsedTime(_startTimestamp).TotalSeconds * AudioSampleRate);

    public override void ReadAudio(long position, Span<float> destination)
    {
        if (IsStopped)
        {
            destination.Clear();
            return;
        }

        _capture.ReadAudio(position, destination);
    }

    protected override void OnStopped()
    {
        _capture.SetCallbacks(null, null);
        _capture.Dispose();
        _mediaDevices.OnSourceStopped(this);
    }
}
