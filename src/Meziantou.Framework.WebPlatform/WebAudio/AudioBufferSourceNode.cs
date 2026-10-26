using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The options of an <see cref="AudioBufferSourceNode"/> (https://webaudio.github.io/web-audio-api/#AudioBufferSourceOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioBufferSourceOptions"/>
[IdlInterface("AudioBufferSourceOptions", "https://webaudio.github.io/web-audio-api/#AudioBufferSourceOptions")]
public sealed class AudioBufferSourceOptions
{
    /// <summary>Gets the buffer to play.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffersourceoptions-buffer"/>
    [IdlMember("buffer", "https://webaudio.github.io/web-audio-api/#dom-audiobuffersourceoptions-buffer")]
    public AudioBuffer? Buffer { get; init; }

    /// <summary>Gets the initial value of <see cref="AudioBufferSourceNode.Detune"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffersourceoptions-detune"/>
    [IdlMember("detune", "https://webaudio.github.io/web-audio-api/#dom-audiobuffersourceoptions-detune")]
    public float Detune { get; init; }

    /// <summary>Gets the initial value of <see cref="AudioBufferSourceNode.Loop"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffersourceoptions-loop"/>
    [IdlMember("loop", "https://webaudio.github.io/web-audio-api/#dom-audiobuffersourceoptions-loop")]
    public bool Loop { get; init; }

    /// <summary>Gets the initial value of <see cref="AudioBufferSourceNode.LoopEnd"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffersourceoptions-loopend"/>
    [IdlMember("loopEnd", "https://webaudio.github.io/web-audio-api/#dom-audiobuffersourceoptions-loopend")]
    public double LoopEnd { get; init; }

    /// <summary>Gets the initial value of <see cref="AudioBufferSourceNode.LoopStart"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffersourceoptions-loopstart"/>
    [IdlMember("loopStart", "https://webaudio.github.io/web-audio-api/#dom-audiobuffersourceoptions-loopstart")]
    public double LoopStart { get; init; }

    /// <summary>Gets the initial value of <see cref="AudioBufferSourceNode.PlaybackRate"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffersourceoptions-playbackrate"/>
    [IdlMember("playbackRate", "https://webaudio.github.io/web-audio-api/#dom-audiobuffersourceoptions-playbackrate")]
    public float PlaybackRate { get; init; } = 1;
}

/// <summary>
/// A source that plays an <see cref="AudioBuffer"/> (https://webaudio.github.io/web-audio-api/#AudioBufferSourceNode), with
/// a playback rate, a detune, a loop, and sub-sample accurate start and stop times (the samples are linearly interpolated).
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioBufferSourceNode"/>
[IdlInterface("AudioBufferSourceNode", "https://webaudio.github.io/web-audio-api/#AudioBufferSourceNode")]
public sealed class AudioBufferSourceNode : AudioScheduledSourceNode
{
    private AudioBuffer? _buffer;

    // [[buffer set]]
    private bool _bufferSet;

    // The content of the buffer acquired when the node starts
    private float[][]? _content;
    private float _contentSampleRate;

    // The arguments of start()
    private double _offset;
    private double _duration = double.PositiveInfinity;

    // The playback state: the playhead position and the buffer content played, in frames of the buffer
    private bool _playbackStarted;
    private bool _enteredLoop;
    private double _bufferPosition;
    private double _bufferTimeElapsed;
    private bool _finished;

    /// <summary>Creates a buffer source.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffersourcenode-audiobuffersourcenode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-audiobuffersourcenode-audiobuffersourcenode")]
    public AudioBufferSourceNode(BaseAudioContext context, AudioBufferSourceOptions? options = null)
        : base(context)
    {
        PlaybackRate = new AudioParam(context, this, "AudioBufferSourceNode.playbackRate", 1, -float.MaxValue, float.MaxValue, AutomationRate.KRate, automationRateIsFixed: true);
        Detune = new AudioParam(context, this, "AudioBufferSourceNode.detune", 0, -float.MaxValue, float.MaxValue, AutomationRate.KRate, automationRateIsFixed: true);
        if (options is not null)
        {
            if (options.Buffer is not null)
            {
                Buffer = options.Buffer;
            }

            Detune.SetInitialValue(options.Detune);
            Loop = options.Loop;
            LoopEnd = options.LoopEnd;
            LoopStart = options.LoopStart;
            PlaybackRate.SetInitialValue(options.PlaybackRate);
        }
    }

    /// <summary>Gets or sets the buffer to play. A buffer can only be set once.</summary>
    /// <exception cref="DomException">A buffer was already set (an <c>InvalidStateError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffersourcenode-buffer"/>
    [IdlMember("buffer", "https://webaudio.github.io/web-audio-api/#dom-audiobuffersourcenode-buffer")]
    public AudioBuffer? Buffer
    {
        get => _buffer;
        set
        {
            if (value is not null && _bufferSet)
                throw new DomException("The buffer of the source was already set", "InvalidStateError");

            if (value is not null)
            {
                _bufferSet = true;
            }

            _buffer = value;
            if (IsStarted)
            {
                AcquireContent();
            }
        }
    }

    /// <summary>Gets the speed of the playback (k-rate).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffersourcenode-playbackrate"/>
    [IdlMember("playbackRate", "https://webaudio.github.io/web-audio-api/#dom-audiobuffersourcenode-playbackrate")]
    public AudioParam PlaybackRate { get; }

    /// <summary>Gets the detune of the playback in cents (k-rate).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffersourcenode-detune"/>
    [IdlMember("detune", "https://webaudio.github.io/web-audio-api/#dom-audiobuffersourcenode-detune")]
    public AudioParam Detune { get; }

    /// <summary>Gets or sets a value indicating whether the region from <see cref="LoopStart"/> to <see cref="LoopEnd"/> is played in a loop.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffersourcenode-loop"/>
    [IdlMember("loop", "https://webaudio.github.io/web-audio-api/#dom-audiobuffersourcenode-loop")]
    public bool Loop { get; set; }

    /// <summary>Gets or sets the playhead position in seconds where the loop starts.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffersourcenode-loopstart"/>
    [IdlMember("loopStart", "https://webaudio.github.io/web-audio-api/#dom-audiobuffersourcenode-loopstart")]
    public double LoopStart { get; set; }

    /// <summary>Gets or sets the playhead position in seconds where the loop ends (exclusive); 0 for the end of the buffer.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffersourcenode-loopend"/>
    [IdlMember("loopEnd", "https://webaudio.github.io/web-audio-api/#dom-audiobuffersourcenode-loopend")]
    public double LoopEnd { get; set; }

    /// <summary>Schedules the start of the playback at a time, from a position of the buffer, for a duration of buffer content.</summary>
    /// <param name="when">The time of the context when the playback starts.</param>
    /// <param name="offset">The playhead position in seconds where the playback starts.</param>
    /// <param name="duration">The duration in seconds of buffer content to play (including the loop iterations), or null to play until the end.</param>
    /// <exception cref="DomException">The source was already started (an <c>InvalidStateError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffersourcenode-start"/>
    [IdlMember("start", "https://webaudio.github.io/web-audio-api/#dom-audiobuffersourcenode-start")]
    public void Start(double when = 0, double? offset = null, double? duration = null)
    {
        StartCore(when);
        if (offset < 0)
            throw new RangeErrorException($"The offset {offset} cannot be negative");

        if (duration < 0)
            throw new RangeErrorException($"The duration {duration} cannot be negative");

        _offset = offset ?? 0;
        _duration = duration ?? double.PositiveInfinity;
        MarkStarted(when);
    }

    private protected override void OnStarted() => AcquireContent();

    private void AcquireContent()
    {
        if (_buffer is { } buffer)
        {
            _content = buffer.AcquireContent();
            _contentSampleRate = buffer.SampleRate;
        }
        else
        {
            _content = null;
        }
    }

    private protected override IEnumerable<AudioParam> Parameters => [PlaybackRate, Detune];

    // https://webaudio.github.io/web-audio-api/#playback-AudioBufferSourceNode: without buffer, the source stops at once
    private protected override bool HasFinishedPlaying => _finished || _content is null;

    private protected override bool IsSourceActive(long startFrame, int length) => !_finished && base.IsSourceActive(startFrame, length);

    private protected override void Process(long startFrame, int length)
    {
        var output = GetOutputBus(0);
        var (offset, count) = GetPlayingRange(startFrame, length);
        if (_content is not { } content || count == 0)
        {
            // https://webaudio.github.io/web-audio-api/#playback-AudioBufferSourceNode: without buffer, the source outputs silence for all time
            if (_content is null && count > 0)
            {
                _finished = true;
            }

            output.Zero(1);
            return;
        }

        var channels = content.Length;
        var bufferLength = content[0].Length;
        output.SetNumberOfChannels(channels);
        for (var c = 0; c < channels; c++)
        {
            output.GetChannelSpan(c).Clear();
        }

        var sampleRate = (double)Context.SampleRate;
        var computedPlaybackRate = PlaybackRate.FinalValue * Math.Pow(2, Detune.FinalValue / 1200.0);
        if (!double.IsFinite(computedPlaybackRate))
        {
            computedPlaybackRate = 0;
        }

        // The increment of the playhead per output frame, in frames of the buffer
        var step = computedPlaybackRate * _contentSampleRate / sampleRate;
        var bufferDuration = bufferLength / (double)_contentSampleRate;

        // The loop endpoints in frames of the buffer
        var loop = Loop;
        double loopStartFrame = 0;
        double loopEndFrame = bufferLength;
        if (loop)
        {
            // A negative loop start is clamped to 0, a loop end after the end of the buffer to its end
            var loopStart = Math.Clamp(LoopStart, 0, bufferDuration);
            var loopEnd = Math.Min(LoopEnd, bufferDuration);
            if (LoopEnd > 0 && loopStart < loopEnd)
            {
                loopStartFrame = loopStart * _contentSampleRate;
                loopEndFrame = loopEnd * _contentSampleRate;
            }
        }
        else
        {
            _enteredLoop = false;
        }

        var offsetFrame = _offset * _contentSampleRate;
        var durationFrames = _duration * _contentSampleRate;
        for (var index = offset; index < offset + count; index++)
        {
            if (_bufferTimeElapsed >= durationFrames)
            {
                _finished = true;
                break;
            }

            if (!_playbackStarted)
            {
                if (loop && computedPlaybackRate >= 0 && offsetFrame >= loopEndFrame)
                {
                    offsetFrame = loopEndFrame;
                }

                if (computedPlaybackRate < 0 && loop && offsetFrame < loopStartFrame)
                {
                    offsetFrame = loopStartFrame;
                }

                offsetFrame = Math.Clamp(offsetFrame, 0, bufferLength);
                _offset = offsetFrame / _contentSampleRate;

                // Sub-sample accurate start: the first frame is the frame at or after the start time, and the playhead
                // has already advanced by the time elapsed since the start time
                var frameTime = (startFrame + index) / sampleRate;
                var delay = Math.Max(0, frameTime - StartTime) * sampleRate;
                _bufferPosition = offsetFrame + (delay * step);
                _bufferTimeElapsed = Math.Abs(delay * step);
                _playbackStarted = true;
            }

            if (loop)
            {
                if (!_enteredLoop)
                {
                    if (offsetFrame < loopEndFrame && _bufferPosition >= loopStartFrame)
                    {
                        _enteredLoop = true;
                    }

                    if (offsetFrame >= loopEndFrame && _bufferPosition < loopEndFrame)
                    {
                        _enteredLoop = true;
                    }
                }

                if (_enteredLoop)
                {
                    var loopLength = loopEndFrame - loopStartFrame;
                    if (loopLength > 0)
                    {
                        if (_bufferPosition >= loopEndFrame)
                        {
                            _bufferPosition = loopEndFrame - loopLength + ((_bufferPosition - loopEndFrame) % loopLength);
                        }

                        if (_bufferPosition < loopStartFrame)
                        {
                            _bufferPosition = loopStartFrame + loopLength - ((loopStartFrame - _bufferPosition) % loopLength);
                            if (_bufferPosition >= loopEndFrame)
                            {
                                _bufferPosition -= loopLength;
                            }
                        }
                    }
                }
            }
            else if ((step >= 0 && _bufferPosition >= bufferLength) || (step < 0 && _bufferPosition < 0))
            {
                // The whole buffer was played
                _finished = true;
                break;
            }

            if (_bufferPosition >= 0 && _bufferPosition < bufferLength)
            {
                var useLoop = loop && _enteredLoop;
                for (var c = 0; c < channels; c++)
                {
                    output.GetChannel(c)[index] = Interpolate(content[c], _bufferPosition, useLoop, loopStartFrame, loopEndFrame);
                }
            }

            _bufferPosition += step;
            _bufferTimeElapsed += Math.Abs(step);
        }

        // A source that is not looping ends once its playhead is past the end of the buffer
        if (!loop && ((step > 0 && _bufferPosition >= bufferLength) || (step < 0 && _bufferPosition < 0)))
        {
            _finished = true;
        }

        if (_bufferTimeElapsed >= durationFrames)
        {
            _finished = true;
        }

        output.MarkNonSilent();
    }

    /// <summary>
    /// The value of the playback signal at a playhead position (https://webaudio.github.io/web-audio-api/#playback-AudioBufferSourceNode):
    /// the samples are linearly interpolated, and the frames after the end of the loop are the frames from its start.
    /// </summary>
    private static float Interpolate(float[] samples, double position, bool loop, double loopStart, double loopEnd)
    {
        var k0 = (int)position;
        var fraction = position - k0;
        var s0 = samples[k0];
        if (fraction == 0)
            return s0;

        var s1 = GetFrame(samples, k0 + 1, loop, loopStart, loopEnd);
        return (float)(s0 + ((s1 - s0) * fraction));
    }

    private static float GetFrame(float[] samples, int frame, bool loop, double loopStart, double loopEnd)
    {
        if (loop && frame >= loopEnd)
        {
            // The frame continues at the start of the loop
            var position = frame - (loopEnd - loopStart);
            if (position < 0)
            {
                position = 0;
            }

            var k = (int)position;
            var fraction = position - k;
            var a = samples[Math.Min(k, samples.Length - 1)];
            if (fraction == 0)
                return a;

            var b = samples[Math.Min(k + 1, samples.Length - 1)];
            return (float)(a + ((b - a) * fraction));
        }

        if (frame < samples.Length)
            return samples[frame];

        // After the last frame of a buffer that is not looping, the signal is extrapolated from its last two frames
        if (samples.Length < 2)
            return samples[^1];

        var last = samples[^1];
        return last + ((last - samples[^2]) * (frame - (samples.Length - 1)));
    }
}
