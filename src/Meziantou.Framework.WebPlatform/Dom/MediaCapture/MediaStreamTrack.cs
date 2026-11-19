using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The state of a track (https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackstate).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackstate"/>
[IdlInterface("MediaStreamTrackState", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackstate")]
public enum MediaStreamTrackState
{
    /// <summary>The track is active: its source produces media.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackstate-live"/>
    [IdlMember("live", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackstate-live")]
    Live,

    /// <summary>The track ended: it was stopped, or its source ended.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackstate-ended"/>
    [IdlMember("ended", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackstate-ended")]
    Ended,
}

/// <summary>
/// A track of media (https://w3c.github.io/mediacapture-main/#mediastreamtrack): the audio or the video of a source (a
/// camera, a microphone, a screen, a canvas, a media element...), with its constraints and its settings.
/// </summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack"/>
[IdlInterface("MediaStreamTrack", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack")]
public partial class MediaStreamTrack : EventTarget
{
    private readonly GlobalScope _global;
    private MediaTrackConstraints _constraints;
    private Dictionary<string, object> _settings;
    private string _contentHint = "";
    private Action? _stateChanged;

    internal MediaStreamTrack(GlobalScope global, MediaStreamTrackSource source, MediaTrackConstraints? constraints = null, Dictionary<string, object>? settings = null)
    {
        _global = global;
        Source = source;
        Id = Guid.NewGuid().ToString();
        _constraints = constraints ?? new MediaTrackConstraints();
        _settings = settings ?? source.GetInitialSettings();
        Muted = source.IsMuted;
        source.AddTrack(this);
    }

    internal override GlobalScope? RelevantGlobal => _global;

    /// <summary>The source of the track ([[Source]]).</summary>
    internal MediaStreamTrackSource Source { get; }

    /// <summary>The current settings of the track, by name.</summary>
    internal IReadOnlyDictionary<string, object> Settings => _settings;

    /// <summary>The current settings of the track, with the values that follow the state of the source (e.g. the size of a canvas).</summary>
    internal IReadOnlyDictionary<string, object> LiveSettings
    {
        get
        {
            if (ReadyState == MediaStreamTrackState.Live)
            {
                Source.UpdateSettings(_settings);
            }

            return _settings;
        }
    }

    /// <summary>Gets the kind of the track: <c>audio</c> or <c>video</c>.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-kind"/>
    [IdlMember("kind", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-kind")]
    public string Kind => Source.Kind == MediaTrackKinds.Audio ? "audio" : "video";

    /// <summary>Gets the identifier of the track (a UUID).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-id"/>
    [IdlMember("id", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-id")]
    public string Id { get; }

    /// <summary>Gets the label of the source of the track (e.g. the name of the camera), or an empty string.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-label"/>
    [IdlMember("label", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-label")]
    public string Label => Source.Label;

    /// <summary>
    /// Gets or sets whether the track is enabled. A disabled track produces silence or black frames
    /// (https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-enabled).
    /// </summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-enabled"/>
    [IdlMember("enabled", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Gets whether the source of the track is muted (it produces no media for now).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-muted"/>
    [IdlMember("muted", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-muted")]
    public bool Muted { get; private set; }

    /// <summary>Gets or sets the event handler of the <c>mute</c> event.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-onmute"/>
    [IdlMember("onmute", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-onmute")]
    public Action<Event>? OnMute
    {
        get => GetEventHandler("mute");
        set => SetEventHandler("mute", value);
    }

    /// <summary>Gets or sets the event handler of the <c>unmute</c> event.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-onunmute"/>
    [IdlMember("onunmute", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-onunmute")]
    public Action<Event>? OnUnmute
    {
        get => GetEventHandler("unmute");
        set => SetEventHandler("unmute", value);
    }

    /// <summary>Gets the state of the track.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-readystate"/>
    [IdlMember("readyState", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-readystate")]
    public MediaStreamTrackState ReadyState { get; private set; }

    /// <summary>Gets or sets the event handler of the <c>ended</c> event.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-onended"/>
    [IdlMember("onended", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-onended")]
    public Action<Event>? OnEnded
    {
        get => GetEventHandler("ended");
        set => SetEventHandler("ended", value);
    }

    /// <summary>Gets or sets the event handler of the <c>configurationchange</c> event.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrack-onconfigurationchange"/>
    [IdlMember("onconfigurationchange", "https://w3c.github.io/mediacapture-extensions/#dom-mediastreamtrack-onconfigurationchange")]
    public Action<Event>? OnConfigurationChange
    {
        get => GetEventHandler("configurationchange");
        set => SetEventHandler("configurationchange", value);
    }

    /// <summary>
    /// Gets or sets the hint of the content of the track (https://w3c.github.io/mst-content-hint/): for an audio track,
    /// <c>speech</c>, <c>speech-recognition</c> or <c>music</c>; for a video track, <c>motion</c>, <c>detail</c> or
    /// <c>text</c>; or an empty string. Another value is ignored.
    /// </summary>
    /// <seealso href="https://w3c.github.io/mst-content-hint/#dom-mediastreamtrack-contenthint"/>
    [IdlMember("contentHint", "https://w3c.github.io/mst-content-hint/#dom-mediastreamtrack-contenthint")]
    public string ContentHint
    {
        get => _contentHint;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var isValid = Source.Kind == MediaTrackKinds.Audio
                ? value is "" or "speech" or "speech-recognition" or "music"
                : value is "" or "motion" or "detail" or "text";
            if (isValid)
            {
                _contentHint = value;
            }
        }
    }

    /// <summary>
    /// Creates a track of the same source, with the same constraints, settings, state and content hint
    /// (https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-clone).
    /// </summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-clone"/>
    [IdlMember("clone", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-clone")]
    public MediaStreamTrack Clone()
    {
        var clone = CreateClone(GlobalScope.Current ?? _global);
        clone.Enabled = Enabled;
        clone._contentHint = _contentHint;
        if (ReadyState == MediaStreamTrackState.Ended)
        {
            clone.ReadyState = MediaStreamTrackState.Ended;
            clone.Source.RemoveTrack(clone);
        }

        return clone;
    }

    /// <summary>Creates the clone of the track, of the same type (e.g. a <c>CanvasCaptureMediaStreamTrack</c>).</summary>
    private protected virtual MediaStreamTrack CreateClone(GlobalScope global) => new(global, Source, _constraints, new Dictionary<string, object>(_settings, StringComparer.Ordinal));

    /// <summary>
    /// Stops the track (https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-stop): it ends, and its source stops
    /// when it was its last track. No <c>ended</c> event is fired.
    /// </summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-stop"/>
    [IdlMember("stop", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-stop")]
    public void Stop()
    {
        if (ReadyState == MediaStreamTrackState.Ended)
            return;

        ReadyState = MediaStreamTrackState.Ended;
        Source.RemoveTrack(this);
        OnTrackEnded();
    }

    /// <summary>The track ended (stopped, or its source ended): the sinks are notified.</summary>
    private protected virtual void OnTrackEnded() => _stateChanged?.Invoke();

    /// <summary>Registers a callback invoked when the track ends or its muted state changes (for the sinks: the media elements, the Web Audio nodes...).</summary>
    internal void AddStateChangedCallback(Action callback) => _stateChanged += callback;

    internal void RemoveStateChangedCallback(Action callback) => _stateChanged -= callback;

    /// <summary>
    /// Returns the capabilities of the source of the track: the values or the ranges of values of its constrainable
    /// properties (https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-getcapabilities).
    /// </summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-getcapabilities"/>
    [IdlMember("getCapabilities", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-getcapabilities")]
    public MediaTrackCapabilities GetCapabilities() => Source.GetCapabilities();

    /// <summary>Returns the constraints applied by the most recent successful <see cref="ApplyConstraints"/> (https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-getconstraints).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-getconstraints"/>
    [IdlMember("getConstraints", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-getconstraints")]
    public MediaTrackConstraints GetConstraints() => _constraints;

    /// <summary>Returns the current settings of the constrainable properties of the track (https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-getsettings).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-getsettings"/>
    [IdlMember("getSettings", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-getsettings")]
    public MediaTrackSettings GetSettings()
    {
        if (ReadyState == MediaStreamTrackState.Live)
        {
            Source.UpdateSettings(_settings);
        }

        return MediaTrackConstrainableProperty.CreateSettings(_settings);
    }

    /// <summary>
    /// Applies constraints to the track (https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-applyconstraints): the
    /// settings that satisfy them best are selected. The task fails with an <see cref="OverconstrainedError"/> when a
    /// required constraint cannot be satisfied (the current constraints remain). The task of an ended track succeeds.
    /// </summary>
    /// <param name="constraints">The new constraints, which replace the current ones.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-applyconstraints"/>
    [IdlMember("applyConstraints", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-applyconstraints")]
    public Task ApplyConstraints(MediaTrackConstraints? constraints = null)
    {
        constraints ??= new MediaTrackConstraints();
        if (ReadyState == MediaStreamTrackState.Ended)
            return Task.CompletedTask;

        // The algorithm runs in parallel, then a task settles the promise: the tasks keep the order of the calls
        var result = new TaskCompletionSource();
        _global.QueueGlobalTask(() => ApplyConstraintsCore(constraints, result));
        return result.Task;
    }

    private void ApplyConstraintsCore(MediaTrackConstraints constraints, TaskCompletionSource result)
    {
        // https://w3c.github.io/mediacapture-main/#dfn-applyconstraints-algorithm
        Dictionary<string, object>? settings;
        string failedConstraint;
        if (Source.Model is { } model)
        {
            var selection = MediaTrackConstraintProcessing.SelectSettings(model, constraints, Source.FixedValues);
            settings = selection.Settings;
            failedConstraint = selection.FailedConstraint;
        }
        else
        {
            (settings, failedConstraint) = SelectFixedSettings(constraints);
        }

        if (settings is null)
        {
            result.TrySetException(new OverconstrainedError(failedConstraint, "The constraints cannot be satisfied"));
            return;
        }

        _constraints = constraints;
        if (ReadyState == MediaStreamTrackState.Live)
        {
            var changed = !SettingsEqual(_settings, settings);
            _settings = settings;
            if (changed)
            {
                Source.ApplySettings(this, settings);
            }
        }

        result.TrySetResult();
    }

    /// <summary>The constraints of a track whose settings cannot change: the current settings, when they satisfy the required constraints.</summary>
    private (Dictionary<string, object>? Settings, string FailedConstraint) SelectFixedSettings(MediaTrackConstraints constraints)
    {
        foreach (var (name, constraint) in MediaTrackConstraintProcessing.Normalize(constraints, bareValuesAreExact: false))
        {
            if (!MediaTrackConstraintProcessing.Satisfies(_settings, name, constraint))
                return (null, name);
        }

        return (_settings, "");
    }

    private static bool SettingsEqual(Dictionary<string, object> a, Dictionary<string, object> b)
    {
        if (a.Count != b.Count)
            return false;

        foreach (var (name, value) in a)
        {
            if (!b.TryGetValue(name, out var other) || !Equals(value, other))
                return false;
        }

        return true;
    }

    /// <summary>The source of the track ended: the track ends and fires <c>ended</c> (https://w3c.github.io/mediacapture-main/#track-ended).</summary>
    internal void OnSourceEnded()
    {
        if (ReadyState == MediaStreamTrackState.Ended)
            return;

        ReadyState = MediaStreamTrackState.Ended;
        Source.RemoveTrack(this);
        OnTrackEnded();
        FireEvent(new Event("ended"));
    }

    /// <summary>The media of the source changed: the sinks update their state.</summary>
    internal void OnSourceChanged() => _stateChanged?.Invoke();

    /// <summary>https://w3c.github.io/mediacapture-main/#set-track-muted</summary>
    internal void OnSourceMutedChanged(bool muted)
    {
        if (ReadyState == MediaStreamTrackState.Ended || Muted == muted)
            return;

        Muted = muted;
        _stateChanged?.Invoke();
        FireEvent(new Event(muted ? "mute" : "unmute"));
    }

    /// <summary>
    /// The current video frame of the track, of the size of its settings: a black frame when the track is disabled, null
    /// when it is ended or muted, or when its source has no frame.
    /// </summary>
    internal MediaVideoFrame? GetVideoFrame()
    {
        if (ReadyState == MediaStreamTrackState.Ended || Muted || Source.Kind != MediaTrackKinds.Video)
            return null;

        Source.UpdateSettings(_settings);
        var frame = Source.GetVideoFrame();
        var width = _settings.GetValueOrDefault("width") is double w && w >= 1 ? (int)w : frame?.Width ?? 0;
        var height = _settings.GetValueOrDefault("height") is double h && h >= 1 ? (int)h : frame?.Height ?? 0;
        if (width <= 0 || height <= 0)
            return null;

        if (!Enabled)
            return MediaVideoFrames.CreateBlack(width, height);

        return frame is null ? null : MediaVideoFrames.Scale(frame, width, height);
    }

    /// <summary>Reads the audio samples of the track (silence when it is disabled, muted or ended).</summary>
    /// <summary>The number of channels of the audio of the track: the one of its settings, else the one of its source.</summary>
    internal int AudioChannelCount => _settings.GetValueOrDefault("channelCount") is double channels && channels >= 1 ? Math.Min((int)channels, 32) : Math.Clamp(Source.AudioChannelCount, 1, 32);

    /// <summary>
    /// Reads the interleaved audio samples of the track (<see cref="AudioChannelCount"/> per frame; silence when it is
    /// disabled, muted or ended). The audio of a source with another number of channels is mixed: the channels are averaged
    /// for a mono track, and a mono source is copied to each channel.
    /// </summary>
    internal void ReadAudio(long position, Span<float> destination)
    {
        if (ReadyState == MediaStreamTrackState.Ended || Muted || !Enabled || Source.Kind != MediaTrackKinds.Audio)
        {
            destination.Clear();
            return;
        }

        var trackChannels = AudioChannelCount;
        var sourceChannels = Math.Clamp(Source.AudioChannelCount, 1, 32);
        if (trackChannels == sourceChannels)
        {
            Source.ReadAudio(position, destination);
            return;
        }

        var frames = destination.Length / trackChannels;
        var buffer = new float[frames * sourceChannels];
        Source.ReadAudio(position, buffer);
        for (var frame = 0; frame < frames; frame++)
        {
            var input = buffer.AsSpan(frame * sourceChannels, sourceChannels);
            var output = destination.Slice(frame * trackChannels, trackChannels);
            if (trackChannels == 1)
            {
                var sum = 0f;
                foreach (var sample in input)
                {
                    sum += sample;
                }

                output[0] = sum / sourceChannels;
            }
            else
            {
                for (var channel = 0; channel < trackChannels; channel++)
                {
                    output[channel] = sourceChannels == 1 ? input[0] : channel < sourceChannels ? input[channel] : 0;
                }
            }
        }
    }
}

/// <summary>Helpers for the video frames of the tracks.</summary>
internal static class MediaVideoFrames
{
    /// <summary>A black frame (https://w3c.github.io/mediacapture-main/#dom-mediastreamtrack-enabled: a disabled video track renders black frames).</summary>
    public static MediaVideoFrame CreateBlack(int width, int height)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        return new MediaVideoFrame(width, height, pixels);
    }

    /// <summary>Scales a frame to a size (nearest neighbor; the frame is returned as is when it has the size).</summary>
    public static MediaVideoFrame Scale(MediaVideoFrame frame, int width, int height)
    {
        if (frame.Width == width && frame.Height == height)
            return frame;

        var source = frame.Pixels.Span;
        var pixels = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            var sourceY = (int)((y + 0.5) * frame.Height / height);
            for (var x = 0; x < width; x++)
            {
                var sourceX = (int)((x + 0.5) * frame.Width / width);
                source.Slice(((sourceY * frame.Width) + sourceX) * 4, 4).CopyTo(pixels.AsSpan(((y * width) + x) * 4, 4));
            }
        }

        return new MediaVideoFrame(width, height, pixels);
    }
}
