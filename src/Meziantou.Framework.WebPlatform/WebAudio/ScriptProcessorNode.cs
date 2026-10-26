using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The initialization dictionary of an <see cref="AudioProcessingEvent"/> (https://webaudio.github.io/web-audio-api/#AudioProcessingEventInit).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioProcessingEventInit"/>
[IdlInterface("AudioProcessingEventInit", "https://webaudio.github.io/web-audio-api/#AudioProcessingEventInit")]
public sealed class AudioProcessingEventInit : EventInit
{
    /// <summary>Gets the time when the output buffer will be played.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioprocessingeventinit-playbacktime"/>
    [IdlMember("playbackTime", "https://webaudio.github.io/web-audio-api/#dom-audioprocessingeventinit-playbacktime")]
    public required double PlaybackTime { get; init; }

    /// <summary>Gets the input buffer.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioprocessingeventinit-inputbuffer"/>
    [IdlMember("inputBuffer", "https://webaudio.github.io/web-audio-api/#dom-audioprocessingeventinit-inputbuffer")]
    public required AudioBuffer InputBuffer { get; init; }

    /// <summary>Gets the output buffer.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioprocessingeventinit-outputbuffer"/>
    [IdlMember("outputBuffer", "https://webaudio.github.io/web-audio-api/#dom-audioprocessingeventinit-outputbuffer")]
    public required AudioBuffer OutputBuffer { get; init; }
}

/// <summary>The <c>audioprocess</c> event of a <see cref="ScriptProcessorNode"/> (https://webaudio.github.io/web-audio-api/#AudioProcessingEvent).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioProcessingEvent"/>
[IdlInterface("AudioProcessingEvent", "https://webaudio.github.io/web-audio-api/#AudioProcessingEvent")]
public sealed class AudioProcessingEvent : Event
{
    /// <summary>Creates an audio processing event.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioprocessingevent-audioprocessingevent"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-audioprocessingevent-audioprocessingevent")]
    public AudioProcessingEvent(string type, AudioProcessingEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        ArgumentNullException.ThrowIfNull(eventInitDict);
        PlaybackTime = eventInitDict.PlaybackTime;
        InputBuffer = eventInitDict.InputBuffer;
        OutputBuffer = eventInitDict.OutputBuffer;
    }

    /// <summary>Gets the time of the context when the output buffer will be played.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioprocessingevent-playbacktime"/>
    [IdlMember("playbackTime", "https://webaudio.github.io/web-audio-api/#dom-audioprocessingevent-playbacktime")]
    public double PlaybackTime { get; }

    /// <summary>Gets the buffer of the input samples.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioprocessingevent-inputbuffer"/>
    [IdlMember("inputBuffer", "https://webaudio.github.io/web-audio-api/#dom-audioprocessingevent-inputbuffer")]
    public AudioBuffer InputBuffer { get; }

    /// <summary>Gets the buffer the handler fills with the output samples.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioprocessingevent-outputbuffer"/>
    [IdlMember("outputBuffer", "https://webaudio.github.io/web-audio-api/#dom-audioprocessingevent-outputbuffer")]
    public AudioBuffer OutputBuffer { get; }
}

/// <summary>
/// A node that processes its input with a script (https://webaudio.github.io/web-audio-api/#ScriptProcessorNode, deprecated):
/// each time a buffer of input frames is collected, an <c>audioprocess</c> event gives it to the script with a buffer to
/// fill, which is played two buffers later.
/// </summary>
/// <remarks>The event is fired synchronously during the rendering, on the thread of the event loop.</remarks>
/// <seealso href="https://webaudio.github.io/web-audio-api/#ScriptProcessorNode"/>
[IdlInterface("ScriptProcessorNode", "https://webaudio.github.io/web-audio-api/#ScriptProcessorNode")]
public sealed class ScriptProcessorNode : AudioNode
{
    private readonly int _inputChannels;
    private readonly int _outputChannels;
    private readonly float[][] _input;
    private readonly Queue<float[][]> _outputs = new();
    private float[][]? _playing;
    private int _position;

    internal ScriptProcessorNode(BaseAudioContext context, int bufferSize, int numberOfInputChannels, int numberOfOutputChannels)
        : base(context, numberOfInputs: 1, numberOfOutputs: 1, channelCount: (uint)Math.Max(numberOfInputChannels, 1), ChannelCountMode.Explicit, ChannelInterpretation.Speakers)
    {
        BufferSize = bufferSize;
        _inputChannels = numberOfInputChannels;
        _outputChannels = numberOfOutputChannels;
        _input = new float[Math.Max(numberOfInputChannels, 1)][];
        for (var i = 0; i < _input.Length; i++)
        {
            _input[i] = new float[bufferSize];
        }

        // The output is delayed by two buffers (double buffering): two buffers of silence are played first
        _outputs.Enqueue(CreateOutputBuffers());
        _outputs.Enqueue(CreateOutputBuffers());
    }

    /// <summary>Gets or sets the event handler of the <c>audioprocess</c> event.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-scriptprocessornode-onaudioprocess"/>
    [IdlMember("onaudioprocess", "https://webaudio.github.io/web-audio-api/#dom-scriptprocessornode-onaudioprocess")]
    public Action<Event>? OnAudioProcess
    {
        get => GetEventHandler("audioprocess");
        set => SetEventHandler("audioprocess", value);
    }

    /// <summary>Gets the number of frames of the buffers of the events.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-scriptprocessornode-buffersize"/>
    [IdlMember("bufferSize", "https://webaudio.github.io/web-audio-api/#dom-scriptprocessornode-buffersize")]
    public int BufferSize { get; }

    /// <summary>Validates the arguments of <see cref="BaseAudioContext.CreateScriptProcessor"/> and computes the buffer size.</summary>
    internal static int ValidateArguments(BaseAudioContext context, uint bufferSize, uint numberOfInputChannels, uint numberOfOutputChannels)
    {
        if (bufferSize == 0)
        {
            // The implementation chooses the size: the render quantum size, rounded to a power of two from 256 to 16384
            var nearest = Math.Pow(2, Math.Round(Math.Log2(context.RenderQuantumSizeInternal), MidpointRounding.AwayFromZero));
            bufferSize = (uint)Math.Clamp(nearest, 256, 16384);
        }
        else if (bufferSize is not (256 or 512 or 1024 or 2048 or 4096 or 8192 or 16384))
        {
            throw new DomException($"The buffer size {bufferSize} is not a power of two from 256 to 16384", "IndexSizeError");
        }

        if (numberOfInputChannels == 0 && numberOfOutputChannels == 0)
            throw new DomException("The numbers of input and output channels cannot both be 0", "IndexSizeError");

        if (numberOfInputChannels > MaxChannels)
            throw new DomException($"The number of input channels {numberOfInputChannels} is greater than {MaxChannels}", "IndexSizeError");

        if (numberOfOutputChannels > MaxChannels)
            throw new DomException($"The number of output channels {numberOfOutputChannels} is greater than {MaxChannels}", "IndexSizeError");

        if (bufferSize % context.RenderQuantumSizeInternal != 0)
            throw new DomException($"The buffer size {bufferSize} is not a multiple of the render quantum size {context.RenderQuantumSizeInternal}", "NotSupportedError");

        return (int)bufferSize;
    }

    private float[][] CreateOutputBuffers()
    {
        var buffers = new float[Math.Max(_outputChannels, 1)][];
        for (var i = 0; i < buffers.Length; i++)
        {
            buffers[i] = new float[BufferSize];
        }

        return buffers;
    }

    private protected override void ValidateChannelCount(uint value)
    {
        if (value != ChannelCount)
            throw new DomException("The channel count of a script processor cannot be changed", "NotSupportedError");
    }

    private protected override void ValidateChannelCountMode(ChannelCountMode value)
    {
        if (value != ChannelCountMode.Explicit)
            throw new DomException("The channel count mode of a script processor cannot be changed", "NotSupportedError");
    }

    // https://webaudio.github.io/web-audio-api/#AudioNode-actively-processing: a script processor is actively processing when its input or output is connected
    private protected override bool IsSourceActive(long startFrame, int length) => HasConnections;

    private protected override void Process(long startFrame, int length)
    {
        var input = GetInputBus(0);
        var output = GetOutputBus(0);
        _playing ??= _outputs.Dequeue();
        for (var channel = 0; channel < _inputChannels; channel++)
        {
            var destination = _input[channel].AsSpan(_position, length);
            if (input.IsSilent || channel >= input.NumberOfChannels)
            {
                destination.Clear();
            }
            else
            {
                input.GetChannelSpan(channel).CopyTo(destination);
            }
        }

        output.SetNumberOfChannels(Math.Max(_outputChannels, 1));
        for (var channel = 0; channel < output.NumberOfChannels; channel++)
        {
            _playing[channel].AsSpan(_position, length).CopyTo(output.GetChannelSpan(channel));
        }

        output.MarkNonSilent();
        _position += length;
        if (_position < BufferSize)
            return;

        // A buffer is complete: the script processes it, and its output is played after the buffers already queued
        _position = 0;
        var sampleRate = Context.SampleRate;
        var inputBuffer = new AudioBuffer((uint)Math.Max(_inputChannels, 1), (uint)BufferSize, sampleRate);
        for (var channel = 0; channel < _inputChannels; channel++)
        {
            _input[channel].CopyTo(inputBuffer.GetWritableChannel(channel));
        }

        var outputBuffer = new AudioBuffer((uint)Math.Max(_outputChannels, 1), (uint)BufferSize, sampleRate);
        var playbackTime = (startFrame + length + (_outputs.Count * (long)BufferSize)) / (double)sampleRate;
        FireEvent(new AudioProcessingEvent("audioprocess", new AudioProcessingEventInit { PlaybackTime = playbackTime, InputBuffer = inputBuffer, OutputBuffer = outputBuffer }));

        var buffers = CreateOutputBuffers();
        for (var channel = 0; channel < buffers.Length; channel++)
        {
            outputBuffer.CopyFromChannel(unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(buffers[channel].AsSpan())), (uint)channel);
        }

        _outputs.Enqueue(buffers);
        _playing = _outputs.Dequeue();
    }
}
