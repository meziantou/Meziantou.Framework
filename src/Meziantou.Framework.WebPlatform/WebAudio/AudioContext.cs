using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The options of an <see cref="AudioContext"/> (https://webaudio.github.io/web-audio-api/#AudioContextOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioContextOptions"/>
[IdlInterface("AudioContextOptions", "https://webaudio.github.io/web-audio-api/#AudioContextOptions")]
public sealed class AudioContextOptions
{
    /// <summary>Gets the latency hint: an <see cref="AudioContextLatencyCategory"/>, or a latency in seconds.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-latencyhint"/>
    [IdlMember("latencyHint", "https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-latencyhint")]
    public AudioContextLatencyHint LatencyHint { get; init; } = AudioContextLatencyCategory.Interactive;

    /// <summary>Gets the sample rate in Hz, or null for the default sample rate (48000 Hz).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-samplerate"/>
    [IdlMember("sampleRate", "https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-samplerate")]
    public float? SampleRate { get; init; }

    /// <summary>Gets the audio output device: the identifier of a device (the empty string for the default device), or the options of a sink without device.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-sinkid"/>
    [IdlMember("sinkId", "https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-sinkid")]
    public StringOrAudioSinkOptions? SinkId { get; init; }

    /// <summary>Gets the render quantum size: an <see cref="AudioContextRenderSizeCategory"/>, or a number of sample frames (a <see cref="uint"/>).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-rendersizehint"/>
    [IdlMember("renderSizeHint", "https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-rendersizehint")]
    public AudioContextRenderSizeHint RenderSizeHint { get; init; } = AudioContextRenderSizeCategory.Default;
}

/// <summary>The options of an audio output without device (https://webaudio.github.io/web-audio-api/#AudioSinkOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioSinkOptions"/>
[IdlInterface("AudioSinkOptions", "https://webaudio.github.io/web-audio-api/#AudioSinkOptions")]
public sealed class AudioSinkOptions
{
    /// <summary>Gets the type of the sink.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiosinkoptions-type"/>
    [IdlMember("type", "https://webaudio.github.io/web-audio-api/#dom-audiosinkoptions-type")]
    public required AudioSinkType Type { get; init; }
}

/// <summary>The audio output without device of an <see cref="AudioContext"/> (https://webaudio.github.io/web-audio-api/#AudioSinkInfo).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioSinkInfo"/>
[IdlInterface("AudioSinkInfo", "https://webaudio.github.io/web-audio-api/#AudioSinkInfo")]
public sealed class AudioSinkInfo
{
    internal AudioSinkInfo(AudioSinkType type) => Type = type;

    /// <summary>Gets the type of the sink.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiosinkinfo-type"/>
    [IdlMember("type", "https://webaudio.github.io/web-audio-api/#dom-audiosinkinfo-type")]
    public AudioSinkType Type { get; }
}

/// <summary>A time of an <see cref="AudioContext"/> and the corresponding time of the performance timeline (https://webaudio.github.io/web-audio-api/#AudioTimestamp).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioTimestamp"/>
[IdlInterface("AudioTimestamp", "https://webaudio.github.io/web-audio-api/#AudioTimestamp")]
public sealed class AudioTimestamp
{
    /// <summary>Gets the time of the context in seconds.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiotimestamp-contexttime"/>
    [IdlMember("contextTime", "https://webaudio.github.io/web-audio-api/#dom-audiotimestamp-contexttime")]
    public double ContextTime { get; init; }

    /// <summary>Gets the time of the performance timeline (in milliseconds, relative to the time origin of the window) at which the frame of <see cref="ContextTime"/> is output.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiotimestamp-performancetime"/>
    [IdlMember("performanceTime", "https://webaudio.github.io/web-audio-api/#dom-audiotimestamp-performancetime")]
    public double PerformanceTime { get; init; }
}

/// <summary>
/// An audio context that renders its graph in real time (https://webaudio.github.io/web-audio-api/#AudioContext). There is no
/// audio device: the audio is rendered and discarded, at the pace of the clock of the event loop
/// (<see cref="DomEventLoop.TimeProvider"/>), so that the time of the context progresses as in a browser.
/// </summary>
/// <remarks>
/// The rendering runs in timer tasks of the event loop of the window of the context: a task renders the render quanta whose
/// time has elapsed. A context is allowed to start: it starts running after its construction. A context created by .NET code
/// without window does not render (its time does not progress), but its state changes.
/// </remarks>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioContext"/>
[IdlInterface("AudioContext", "https://webaudio.github.io/web-audio-api/#AudioContext")]
public sealed class AudioContext : BaseAudioContext
{
    internal const float DefaultSampleRate = 48000;

    // The latencies of the categories, in render quanta
    private const int InteractiveQuanta = 2;
    private const int BalancedQuanta = 8;
    private const int PlaybackQuanta = 32;

    // The maximum time rendered by a task: the rendering skips the time beyond it (an underrun)
    private const double MaxRenderSeconds = 0.5;

    private readonly List<TaskCompletionSource> _pendingResumes = [];
    private readonly int _latencyFrames;
    private ControlState _controlState = ControlState.Suspended;
    private bool _renderingRunning;
    private bool _timerScheduled;
    private bool _hasRendered;
    private long _renderStartTimestamp;
    private long _renderStartFrame;
    private object _sinkId = "";

    // The playback statistics: the time played before the current run, and the underruns
    private double _playedSeconds;
    private long _runningSinceTimestamp;
    private bool _playbackStarted;
    private double _underrunSeconds;
    private uint _underrunEvents;
    private bool _hasLatency;
    private double _latencySum;
    private long _latencyCount;
    private double _minimumLatency;
    private double _maximumLatency;

    /// <summary>Creates a realtime context, which starts running.</summary>
    /// <exception cref="DomException">The sample rate or the render quantum size is not supported (a <c>NotSupportedError</c>), or the document is not fully active (an <c>InvalidStateError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontext-audiocontext"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-audiocontext-audiocontext")]
    public AudioContext(AudioContextOptions? contextOptions = null)
        : this(Validate(contextOptions ?? new AudioContextOptions()))
    {
    }

    private AudioContext((AudioContextOptions Options, float SampleRate, int RenderQuantumSize) arguments)
        : base(arguments.SampleRate, arguments.RenderQuantumSize, destinationChannelCount: 2, maxChannelCount: 2, isOffline: false)
    {
        var options = arguments.Options;
        _latencyFrames = GetLatencyFrames(options.LatencyHint, arguments.RenderQuantumSize, arguments.SampleRate);
        PlaybackStats = new AudioPlaybackStats(this);

        // https://webaudio.github.io/web-audio-api/#dom-audiocontext-audiocontext: an invalid sink is an error event, and the
        // context uses the default device
        if (options.SinkId is { } sinkId)
        {
            if (ValidateSinkId(sinkId) is null)
            {
                _sinkId = sinkId.Options is { } sinkOptions ? new AudioSinkInfo(sinkOptions.Type) : sinkId.DeviceId ?? "";
            }
            else
            {
                QueueMediaElementTask(() => FireEvent(new Event("error")));
            }
        }

        Window?.Document.RegisterAudioContext(this);

        // The context is allowed to start: a control message starts the processing
        _controlState = ControlState.Running;
        StartRendering(resumes: null);
    }

    private static (AudioContextOptions Options, float SampleRate, int RenderQuantumSize) Validate(AudioContextOptions options)
    {
        if (GlobalScope.Current is Window { Document.IsFullyActive: false })
            throw new DomException("The document is not fully active", "InvalidStateError");

        var sampleRate = options.SampleRate ?? DefaultSampleRate;
        if (!(sampleRate >= AudioBuffer.MinSampleRate && sampleRate <= AudioBuffer.MaxSampleRate))
            throw new DomException($"The sample rate {sampleRate} is not between {AudioBuffer.MinSampleRate} and {AudioBuffer.MaxSampleRate}", "NotSupportedError");

        return (options, sampleRate, OfflineAudioContext.GetRenderQuantumSize(options.RenderSizeHint, sampleRate));
    }

    /// <summary>The latency of a hint, in frames: a multiple of the render quantum size, from the interactive latency to the playback latency.</summary>
    private static int GetLatencyFrames(AudioContextLatencyHint hint, int renderQuantumSize, float sampleRate)
    {
        var quanta = hint.Category switch
        {
            AudioContextLatencyCategory.Balanced => BalancedQuanta,
            AudioContextLatencyCategory.Playback => PlaybackQuanta,
            AudioContextLatencyCategory.Interactive => InteractiveQuanta,
            _ => hint.Seconds is { } seconds && double.IsFinite(seconds) ? (int)Math.Clamp(Math.Ceiling(seconds * sampleRate / renderQuantumSize), InteractiveQuanta, PlaybackQuanta) : InteractiveQuanta,
        };

        return quanta * renderQuantumSize;
    }

    /// <summary>Gets the latency in seconds of the processing of the audio by the context (the latency of the hint, rounded to render quanta).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontext-baselatency"/>
    [IdlMember("baseLatency", "https://webaudio.github.io/web-audio-api/#dom-audiocontext-baselatency")]
    public double BaseLatency => _latencyFrames / (double)SampleRate;

    /// <summary>Gets the latency in seconds of the audio output: 0, as there is no audio device.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontext-outputlatency"/>
    [IdlMember("outputLatency", "https://webaudio.github.io/web-audio-api/#dom-audiocontext-outputlatency")]
    public double OutputLatency => 0;

    /// <summary>Gets the audio output device: the identifier of a device (the empty string for the default device), or an <see cref="AudioSinkInfo"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontext-sinkid"/>
    [IdlMember("sinkId", "https://webaudio.github.io/web-audio-api/#dom-audiocontext-sinkid")]
    public StringOrAudioSinkInfo SinkId => _sinkId is AudioSinkInfo info ? info : (string)_sinkId;

    /// <summary>Gets the statistics of the playback.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontext-playbackstats"/>
    [IdlMember("playbackStats", "https://webaudio.github.io/web-audio-api/#dom-audiocontext-playbackstats")]
    public AudioPlaybackStats PlaybackStats { get; }

    /// <summary>Gets or sets the event handler of the <c>sinkchange</c> event.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontext-onsinkchange"/>
    [IdlMember("onsinkchange", "https://webaudio.github.io/web-audio-api/#dom-audiocontext-onsinkchange")]
    public Action<Event>? OnSinkChange
    {
        get => GetEventHandler("sinkchange");
        set => SetEventHandler("sinkchange", value);
    }

    /// <summary>Gets or sets the event handler of the <c>error</c> event.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontext-onerror"/>
    [IdlMember("onerror", "https://webaudio.github.io/web-audio-api/#dom-audiocontext-onerror")]
    public Action<Event>? OnError
    {
        get => GetEventHandler("error");
        set => SetEventHandler("error", value);
    }

    /// <summary>
    /// Returns the time of the last rendered frame, and the time of the performance timeline at which it is output (both 0
    /// before the first render quantum).
    /// </summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontext-getoutputtimestamp"/>
    [IdlMember("getOutputTimestamp", "https://webaudio.github.io/web-audio-api/#dom-audiocontext-getoutputtimestamp")]
    public AudioTimestamp GetOutputTimestamp()
    {
        if (!_hasRendered || Window is not { } window)
            return new AudioTimestamp();

        var frequency = window.EventLoop.TimeProvider.TimestampFrequency;
        var timestamp = _renderStartTimestamp + (long)((CurrentFrame - _renderStartFrame) / (double)SampleRate * frequency);
        return new AudioTimestamp
        {
            ContextTime = CurrentTime,
            PerformanceTime = Math.Max(0, window.ToRelativeHighResolutionTime(timestamp)),
        };
    }

    /// <summary>Resumes the rendering suspended by <see cref="Suspend"/>.</summary>
    /// <returns>A task completed once the context is running.</returns>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontext-resume"/>
    [IdlMember("resume", "https://webaudio.github.io/web-audio-api/#dom-audiocontext-resume")]
    public Task Resume()
    {
        if (!IsDocumentFullyActive)
            return Task.FromException(new DomException("The document is not fully active", "InvalidStateError"));

        if (_controlState == ControlState.Closed)
            return Task.FromException(new DomException("The context is closed", "InvalidStateError"));

        var completion = new TaskCompletionSource();
        _controlState = ControlState.Running;
        if (State == AudioContextState.Running && _renderingRunning)
        {
            completion.SetResult();
            return completion.Task;
        }

        _pendingResumes.Add(completion);
        StartRendering(resumes: _pendingResumes);
        return completion.Task;
    }

    /// <summary>Suspends the rendering: the time of the context stops progressing.</summary>
    /// <returns>A task completed once the context is suspended.</returns>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontext-suspend"/>
    [IdlMember("suspend", "https://webaudio.github.io/web-audio-api/#dom-audiocontext-suspend")]
    public Task Suspend()
    {
        if (!IsDocumentFullyActive)
            return Task.FromException(new DomException("The document is not fully active", "InvalidStateError"));

        if (_controlState == ControlState.Closed)
            return Task.FromException(new DomException("The context is closed", "InvalidStateError"));

        var completion = new TaskCompletionSource();
        _controlState = ControlState.Suspended;
        StopRendering();
        QueueMediaElementTask(() =>
        {
            completion.TrySetResult();
            if (State is not (AudioContextState.Suspended or AudioContextState.Closed))
            {
                SetState(AudioContextState.Suspended);
            }
        });
        return completion.Task;
    }

    /// <summary>Closes the context: the rendering stops, and the context cannot be resumed.</summary>
    /// <returns>A task completed once the context is closed.</returns>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontext-close"/>
    [IdlMember("close", "https://webaudio.github.io/web-audio-api/#dom-audiocontext-close")]
    public Task Close()
    {
        if (!IsDocumentFullyActive)
            return Task.FromException(new DomException("The document is not fully active", "InvalidStateError"));

        if (_controlState == ControlState.Closed)
            return Task.FromException(new DomException("The context is closed", "InvalidStateError"));

        var completion = new TaskCompletionSource();
        _controlState = ControlState.Closed;
        StopRendering();
        QueueMediaElementTask(() =>
        {
            foreach (var resume in _pendingResumes)
            {
                resume.TrySetException(new DomException("The context was closed", "InvalidStateError"));
            }

            _pendingResumes.Clear();
            completion.TrySetResult();
            if (State != AudioContextState.Closed)
            {
                SetState(AudioContextState.Closed);
            }
        });
        return completion.Task;
    }

    /// <summary>Changes the audio output device: the empty string for the default device, or the options of a sink without device.</summary>
    /// <returns>A task completed once the device is changed: failed with a <c>NotAllowedError</c> when the <c>speaker-selection</c> feature is not allowed, or a <c>NotFoundError</c> for an unknown device.</returns>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontext-setsinkid"/>
    [IdlMember("setSinkId", "https://webaudio.github.io/web-audio-api/#dom-audiocontext-setsinkid")]
    public Task SetSinkId(StringOrAudioSinkOptions sinkId)
    {
        if (sinkId.Value is null)
            throw new ArgumentException("The sink identifier is not initialized", nameof(sinkId));

        // https://webaudio.github.io/web-audio-api/#dom-audiocontext-setsinkid: the same sink resolves at once
        if (IsSameSink(sinkId))
            return Task.CompletedTask;

        if (ValidateSinkId(sinkId) is { } error)
            return Task.FromException(error);

        if (_controlState == ControlState.Closed)
            return Task.FromException(new DomException("The context is closed", "InvalidStateError"));

        var completion = new TaskCompletionSource();
        QueueMediaElementTask(() =>
        {
            _sinkId = sinkId.Options is { } options ? new AudioSinkInfo(options.Type) : sinkId.DeviceId ?? "";
            FireEvent(new Event("sinkchange"));
            completion.TrySetResult();
        });
        return completion.Task;
    }

    private bool IsSameSink(StringOrAudioSinkOptions sinkId) => sinkId.Options is { } options
        ? _sinkId is AudioSinkInfo info && info.Type == options.Type
        : _sinkId is string current && string.Equals(current, sinkId.DeviceId, StringComparison.Ordinal);

    /// <summary>Closes the context when its document is destroyed: the state is closed at once, without <c>statechange</c> event.</summary>
    internal void OnDocumentDestroyed()
    {
        _controlState = ControlState.Closed;
        StopRendering();
        foreach (var resume in _pendingResumes)
        {
            resume.TrySetException(new DomException("The document of the context was destroyed", "InvalidStateError"));
        }

        _pendingResumes.Clear();
        SetStateSilently(AudioContextState.Closed);
    }

    /// <summary>
    /// Validates a sink identifier (https://webaudio.github.io/web-audio-api/#sink-identifier-validation): the empty string (the
    /// default device) and the sinks without device are valid; there is no other device. Returns the error, or null.
    /// </summary>
    private DomException? ValidateSinkId(StringOrAudioSinkOptions sinkId)
    {
        if (Window is { } window && !PermissionsPolicyAlgorithms.IsAllowedToUse(window.Document, PolicyControlledFeature.SpeakerSelection))
            return new DomException("The 'speaker-selection' feature is not allowed in the document", "NotAllowedError");

        if (sinkId.DeviceId is { Length: > 0 } deviceId)
            return new DomException($"The audio output device '{deviceId}' does not exist", "NotFoundError");

        return null;
    }

    /// <summary>
    /// Starts the processing (https://webaudio.github.io/web-audio-api/#dom-audiocontext-resume): the rendering starts at the
    /// clock, and a task resolves the pending resumes and sets the state to running.
    /// </summary>
    private void StartRendering(List<TaskCompletionSource>? resumes)
    {
        if (!_renderingRunning && IsDocumentFullyActive)
        {
            _renderingRunning = true;
            if (Window is { } window)
            {
                var now = window.EventLoop.TimeProvider.GetTimestamp();
                _renderStartTimestamp = now;
                _renderStartFrame = CurrentFrame;
                _playbackStarted = false;
                ScheduleTick(window);
            }
        }

        QueueMediaElementTask(() =>
        {
            if (resumes is not null)
            {
                var pending = resumes.ToArray();
                resumes.Clear();
                foreach (var resume in pending)
                {
                    resume.TrySetResult();
                }
            }

            if (_controlState == ControlState.Running && State != AudioContextState.Running)
            {
                SetState(AudioContextState.Running);
            }
        });
    }

    private void StopRendering()
    {
        if (!_renderingRunning)
            return;

        _renderingRunning = false;
        if (_playbackStarted && Window is { } window)
        {
            var now = window.EventLoop.TimeProvider.GetTimestamp();
            _playedSeconds += (now - _runningSinceTimestamp) / (double)window.EventLoop.TimeProvider.TimestampFrequency;
        }
    }

    /// <summary>The time between two rendering tasks: half the latency, from 5 ms to 50 ms.</summary>
    private TimeSpan TickInterval => TimeSpan.FromSeconds(Math.Clamp(BaseLatency / 2, 0.005, 0.05));

    private void ScheduleTick(Window window)
    {
        if (_timerScheduled)
            return;

        _timerScheduled = true;
        window.EventLoop.QueueTimer(TickInterval, () => window.QueueGlobalTask(Tick));
    }

    /// <summary>Renders the render quanta whose time has elapsed on the clock of the event loop.</summary>
    private void Tick()
    {
        _timerScheduled = false;
        if (!_renderingRunning || Window is not { } window)
            return;

        if (!IsDocumentFullyActive)
        {
            StopRendering();
            return;
        }

        var timeProvider = window.EventLoop.TimeProvider;
        var now = timeProvider.GetTimestamp();
        var elapsed = (now - _renderStartTimestamp) / (double)timeProvider.TimestampFrequency;
        var target = _renderStartFrame + (long)(elapsed * SampleRate);
        var maxFrames = (long)(MaxRenderSeconds * SampleRate);
        if (target - CurrentFrame > maxFrames + _latencyFrames)
        {
            // The rendering is late (e.g. the event loop was blocked): the time beyond the maximum is skipped
            var skipped = target - CurrentFrame - maxFrames;
            _underrunSeconds += skipped / (double)SampleRate;
            _underrunEvents++;
            _renderStartFrame -= skipped;
            target -= skipped;
        }

        var quantum = RenderQuantumSizeInternal;
        while (_renderingRunning && CurrentFrame + quantum <= target)
        {
            if (!_playbackStarted)
            {
                // The playback of the run starts with its first render quantum
                _playbackStarted = true;
                _runningSinceTimestamp = now;
            }

            RenderQuantum();
            _hasRendered = true;
        }

        if (_renderingRunning)
        {
            if (_playbackStarted)
            {
                RecordLatency();
            }

            ScheduleTick(window);
        }
    }

    private void RecordLatency()
    {
        var latency = BaseLatency + OutputLatency;
        _latencySum += latency;
        _latencyCount++;
        _minimumLatency = _hasLatency ? Math.Min(_minimumLatency, latency) : latency;
        _maximumLatency = _hasLatency ? Math.Max(_maximumLatency, latency) : latency;
        _hasLatency = true;
    }

    /// <summary>The current values of the playback statistics.</summary>
    internal AudioPlaybackStats.Values GetPlaybackValues()
    {
        var total = _playedSeconds;
        if (_renderingRunning && _playbackStarted && Window is { } window)
        {
            var timeProvider = window.EventLoop.TimeProvider;
            total += (timeProvider.GetTimestamp() - _runningSinceTimestamp) / (double)timeProvider.TimestampFrequency;
        }

        var average = _latencyCount == 0 ? 0 : Math.Clamp(_latencySum / _latencyCount, _minimumLatency, _maximumLatency);
        return new AudioPlaybackStats.Values(Math.Min(_underrunSeconds, total), _underrunEvents, total, average, _minimumLatency, _maximumLatency);
    }

    /// <summary>Restarts the statistics of the latency from the current average latency.</summary>
    internal void ResetLatency()
    {
        if (!_hasLatency)
            return;

        var average = Math.Clamp(_latencySum / _latencyCount, _minimumLatency, _maximumLatency);
        _latencySum = average;
        _latencyCount = 1;
        _minimumLatency = average;
        _maximumLatency = average;
    }

    /// <summary>The [[control thread state]] of the context (https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-control-thread-state-slot).</summary>
    private enum ControlState
    {
        Suspended,
        Running,
        Closed,
    }
}

/// <summary>
/// The statistics of the playback of an <see cref="AudioContext"/> (https://webaudio.github.io/web-audio-api/#AudioPlaybackStats).
/// As there is no audio device, the played duration is the time during which the context is running, and the underruns are
/// the times skipped by a late rendering.
/// </summary>
/// <remarks>The values do not change during a task or a microtask of the scripts: they are read once, until the next microtask checkpoint.</remarks>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioPlaybackStats"/>
[IdlInterface("AudioPlaybackStats", "https://webaudio.github.io/web-audio-api/#AudioPlaybackStats")]
public sealed class AudioPlaybackStats
{
    private readonly AudioContext _context;
    private Values? _snapshot;

    internal AudioPlaybackStats(AudioContext context) => _context = context;

    /// <summary>Gets the duration in seconds of the underruns.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioplaybackstats-underrunduration"/>
    [IdlMember("underrunDuration", "https://webaudio.github.io/web-audio-api/#dom-audioplaybackstats-underrunduration")]
    public double UnderrunDuration => GetValues().UnderrunDuration;

    /// <summary>Gets the number of underruns.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioplaybackstats-underrunevents"/>
    [IdlMember("underrunEvents", "https://webaudio.github.io/web-audio-api/#dom-audioplaybackstats-underrunevents")]
    public uint UnderrunEvents => GetValues().UnderrunEvents;

    /// <summary>Gets the duration in seconds of the playback.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioplaybackstats-totalduration"/>
    [IdlMember("totalDuration", "https://webaudio.github.io/web-audio-api/#dom-audioplaybackstats-totalduration")]
    public double TotalDuration => GetValues().TotalDuration;

    /// <summary>Gets the average latency in seconds.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioplaybackstats-averagelatency"/>
    [IdlMember("averageLatency", "https://webaudio.github.io/web-audio-api/#dom-audioplaybackstats-averagelatency")]
    public double AverageLatency => GetValues().AverageLatency;

    /// <summary>Gets the minimum latency in seconds.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioplaybackstats-minimumlatency"/>
    [IdlMember("minimumLatency", "https://webaudio.github.io/web-audio-api/#dom-audioplaybackstats-minimumlatency")]
    public double MinimumLatency => GetValues().MinimumLatency;

    /// <summary>Gets the maximum latency in seconds.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioplaybackstats-maximumlatency"/>
    [IdlMember("maximumLatency", "https://webaudio.github.io/web-audio-api/#dom-audioplaybackstats-maximumlatency")]
    public double MaximumLatency => GetValues().MaximumLatency;

    /// <summary>Restarts the statistics of the latency: the minimum and the maximum become the average latency.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioplaybackstats-resetlatency"/>
    [IdlMember("resetLatency", "https://webaudio.github.io/web-audio-api/#dom-audioplaybackstats-resetlatency")]
    public void ResetLatency()
    {
        _context.ResetLatency();
        _snapshot = null;
    }

    private Values GetValues()
    {
        if (_snapshot is { } snapshot)
            return snapshot;

        var values = _context.GetPlaybackValues();
        if (_context.Window is { } window)
        {
            // The values are kept until the next microtask checkpoint
            _snapshot = values;
            window.EventLoop.QueueMicrotask(() => _snapshot = null);
        }

        return values;
    }

    internal sealed record Values(double UnderrunDuration, uint UnderrunEvents, double TotalDuration, double AverageLatency, double MinimumLatency, double MaximumLatency);
}
