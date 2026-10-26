using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.WebAudio.Rendering;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The options of a <see cref="DelayNode"/> (https://webaudio.github.io/web-audio-api/#DelayOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#DelayOptions"/>
[IdlInterface("DelayOptions", "https://webaudio.github.io/web-audio-api/#DelayOptions")]
public sealed class DelayOptions : AudioNodeOptions
{
    /// <summary>Gets the maximum delay time in seconds.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-delayoptions-maxdelaytime"/>
    [IdlMember("maxDelayTime", "https://webaudio.github.io/web-audio-api/#dom-delayoptions-maxdelaytime")]
    public double MaxDelayTime { get; init; } = 1;

    /// <summary>Gets the initial delay time in seconds.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-delayoptions-delaytime"/>
    [IdlMember("delayTime", "https://webaudio.github.io/web-audio-api/#dom-delayoptions-delaytime")]
    public double DelayTime { get; init; }
}

/// <summary>
/// A node that delays its input (https://webaudio.github.io/web-audio-api/#DelayNode): the output at a time is the input at
/// that time minus the delay time (linearly interpolated). A delay node in a cycle of the graph delays by at least one
/// render quantum.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#DelayNode"/>
[IdlInterface("DelayNode", "https://webaudio.github.io/web-audio-api/#DelayNode")]
public sealed class DelayNode : AudioNode
{
    /// <summary>The smallest absolute value of a sample that keeps a delay node of a cycle actively processing (2^-126).</summary>
    private const float SilenceThreshold = 1.17549435e-38f;

    private readonly int _bufferLength;
    private float[][] _buffers = [];
    private int _channels;
    private long _writeFrame;

    // The frames from which the input had a number of channels: the output has the channels of the delayed input
    private readonly List<(long Frame, int Channels)> _channelHistory = [];
    private long _cycleActiveUntilFrame = long.MinValue;

    /// <summary>Creates a delay node.</summary>
    /// <exception cref="DomException">The maximum delay time is not between 0 (exclusive) and 180 seconds (exclusive) (a <c>NotSupportedError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-delaynode-delaynode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-delaynode-delaynode")]
    public DelayNode(BaseAudioContext context, DelayOptions? options = null)
        : base(context, numberOfInputs: 1, numberOfOutputs: ValidateMaxDelayTime(options), channelCount: 2, ChannelCountMode.Max, ChannelInterpretation.Speakers)
    {
        var maxDelayTime = options?.MaxDelayTime ?? 1;
        MaxDelayTime = maxDelayTime;
        DelayTime = new AudioParam(context, this, "DelayNode.delayTime", 0, 0, (float)maxDelayTime, AutomationRate.ARate);
        ApplyOptions(options);
        if (options is not null)
        {
            DelayTime.SetInitialValue((float)options.DelayTime);
        }

        // The delay line holds the maximum delay, a render quantum (written before it is read), and a frame to interpolate
        _bufferLength = (int)Math.Ceiling(maxDelayTime * context.SampleRate) + context.RenderQuantumSizeInternal + 2;
    }

    /// <summary>Validates the maximum delay time before the node is created (a node that fails to be created is not part of the graph). Returns the number of outputs.</summary>
    private static int ValidateMaxDelayTime(DelayOptions? options)
    {
        var maxDelayTime = options?.MaxDelayTime ?? 1;
        if (!(maxDelayTime > 0 && maxDelayTime < 180))
            throw new DomException($"The maximum delay time {maxDelayTime} is not between 0 and 180 seconds", "NotSupportedError");

        return 1;
    }

    /// <summary>The maximum delay time in seconds.</summary>
    internal double MaxDelayTime { get; }

    /// <summary>Gets the delay time in seconds.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-delaynode-delaytime"/>
    [IdlMember("delayTime", "https://webaudio.github.io/web-audio-api/#dom-delaynode-delaytime")]
    public AudioParam DelayTime { get; }

    private protected override IEnumerable<AudioParam> NodeParameters => [DelayTime];

    internal override bool CanBreakCycles => true;

    private protected override long TailFrames => (long)Math.Ceiling(MaxDelayTime * Context.SampleRate) + Context.RenderQuantumSizeInternal;

    private protected override void OnBecameInactive()
    {
        foreach (var buffer in _buffers)
        {
            Array.Clear(buffer);
        }
    }

    private protected override void Process(long startFrame, int length)
    {
        var input = GetInputBus(0);
        Write(input, length);
        Read(GetOutputBus(0), startFrame, length, minimumDelayFrames: 0);
        _writeFrame += length;
    }

    private protected override bool ProcessCycleOutput(long startFrame, int length)
    {
        // The input of the render quantum is not known yet: the delay is at least one render quantum
        var output = GetOutputBus(0);
        Read(output, startFrame, length, minimumDelayFrames: length);
        if (startFrame < _cycleActiveUntilFrame)
            return true;

        if (!output.IsSilent)
        {
            for (var channel = 0; channel < output.NumberOfChannels; channel++)
            {
                foreach (var sample in output.GetChannelSpan(channel))
                {
                    if (Math.Abs(sample) >= SilenceThreshold)
                        return true;
                }
            }
        }

        output.Zero(1);
        return false;
    }

    private protected override void ProcessCycleInput(long startFrame, int length, bool anyActiveInput)
    {
        if (anyActiveInput)
        {
            _cycleActiveUntilFrame = startFrame + length + TailFrames;
        }

        Write(GetInputBus(0), length);
        _writeFrame += length;
    }

    private void EnsureChannels(int channels)
    {
        if (channels > _buffers.Length)
        {
            var buffers = new float[channels][];
            Array.Copy(_buffers, buffers, _buffers.Length);
            for (var i = _buffers.Length; i < channels; i++)
            {
                buffers[i] = new float[_bufferLength];
            }

            _buffers = buffers;
        }
    }

    /// <summary>Writes a render quantum of input to the delay line, at <see cref="_writeFrame"/>.</summary>
    private void Write(AudioBus input, int length)
    {
        var channels = input.NumberOfChannels;
        EnsureChannels(channels);

        // A new channel layout: the delayed samples of the previous layout are kept in the channels they have in common
        if (channels < _channels)
        {
            for (var channel = channels; channel < _channels; channel++)
            {
                Array.Clear(_buffers[channel]);
            }
        }

        if (_channelHistory.Count == 0 || _channelHistory[^1].Channels != channels)
        {
            _channelHistory.Add((_writeFrame, channels));
            if (_channelHistory.Count > 1 && _channelHistory[1].Frame < _writeFrame - _bufferLength)
            {
                _channelHistory.RemoveAt(0);
            }
        }

        _channels = channels;
        var position = (int)(_writeFrame % _bufferLength);
        for (var channel = 0; channel < channels; channel++)
        {
            var buffer = _buffers[channel];
            if (input.IsSilent)
            {
                ClearRing(buffer, position, length);
            }
            else
            {
                var source = input.GetChannelSpan(channel);
                var first = Math.Min(length, _bufferLength - position);
                source[..first].CopyTo(buffer.AsSpan(position));
                source[first..].CopyTo(buffer);
            }
        }
    }

    private void ClearRing(float[] buffer, int position, int length)
    {
        var first = Math.Min(length, _bufferLength - position);
        buffer.AsSpan(position, first).Clear();
        buffer.AsSpan(0, length - first).Clear();
    }

    /// <summary>Reads a render quantum from the delay line: the frame <c>_writeFrame + i</c> is delayed by the delay time.</summary>
    private void Read(AudioBus output, long startFrame, int length, int minimumDelayFrames)
    {
        var sampleRate = (double)Context.SampleRate;
        var maxDelayFrames = MaxDelayTime * sampleRate;
        var constant = DelayTime.IsConstant;
        var constantDelay = Math.Clamp(DelayTime.FinalValue * sampleRate, minimumDelayFrames, maxDelayFrames);
        var delays = DelayTime.Values;

        // The number of channels of the input at the time of the last delayed frame of the render quantum (before the first
        // input: mono silence)
        var lastDelay = constant ? constantDelay : Math.Clamp(delays[length - 1] * sampleRate, minimumDelayFrames, maxDelayFrames);
        var lastFrame = (long)Math.Floor(_writeFrame + length - 1 - lastDelay);
        var channels = GetChannelCount(lastFrame);
        if (channels == 0)
        {
            output.Zero(1);
            return;
        }

        channels = Math.Min(channels, _buffers.Length);
        output.SetNumberOfChannels(channels);
        for (var channel = 0; channel < channels; channel++)
        {
            var buffer = _buffers[channel];
            var destination = output.GetChannelSpan(channel);
            for (var i = 0; i < length; i++)
            {
                var delay = constant ? constantDelay : Math.Clamp(delays[i] * sampleRate, minimumDelayFrames, maxDelayFrames);
                var position = _writeFrame + i - delay;
                var index = Math.Floor(position);
                var fraction = position - index;
                var k = (long)index;
                var s0 = Sample(buffer, k);
                destination[i] = fraction == 0 ? s0 : (float)(s0 + ((Sample(buffer, k + 1) - s0) * fraction));
            }
        }

        output.MarkNonSilent();
    }

    /// <summary>The number of channels of the input at a frame (0 before the first input).</summary>
    private int GetChannelCount(long frame)
    {
        if (_channelHistory.Count == 0 || frame < _channelHistory[0].Frame)
            return 0;

        var channels = _channelHistory[0].Channels;
        foreach (var (start, count) in _channelHistory)
        {
            if (start > frame)
                break;

            channels = count;
        }

        return channels;
    }

    private float Sample(float[] buffer, long frame)
    {
        if (frame < 0)
            return 0;

        return buffer[(int)(frame % _bufferLength)];
    }
}
