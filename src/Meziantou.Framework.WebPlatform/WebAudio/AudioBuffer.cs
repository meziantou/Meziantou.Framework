using System.ComponentModel;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The options of an <see cref="AudioBuffer"/> (https://webaudio.github.io/web-audio-api/#AudioBufferOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioBufferOptions"/>
[IdlInterface("AudioBufferOptions", "https://webaudio.github.io/web-audio-api/#AudioBufferOptions")]
public sealed class AudioBufferOptions
{
    /// <summary>Gets the number of channels.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobufferoptions-numberofchannels"/>
    [IdlMember("numberOfChannels", "https://webaudio.github.io/web-audio-api/#dom-audiobufferoptions-numberofchannels")]
    public uint NumberOfChannels { get; init; } = 1;

    /// <summary>Gets the length of the buffer in sample frames.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobufferoptions-length"/>
    [IdlMember("length", "https://webaudio.github.io/web-audio-api/#dom-audiobufferoptions-length")]
    public required uint Length { get; init; }

    /// <summary>Gets the sample rate of the buffer in Hz.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobufferoptions-samplerate"/>
    [IdlMember("sampleRate", "https://webaudio.github.io/web-audio-api/#dom-audiobufferoptions-samplerate")]
    public required float SampleRate { get; init; }
}

/// <summary>
/// An audio asset in memory (https://webaudio.github.io/web-audio-api/#AudioBuffer): one or more channels of linear PCM
/// samples (32-bit floats, nominally between -1 and 1), at a sample rate.
/// </summary>
/// <remarks>
/// The arrays returned to the scripts by <c>getChannelData()</c> share their memory with the buffer, until its content is
/// acquired by a node (https://webaudio.github.io/web-audio-api/#acquire-the-content, e.g. when an
/// <see cref="AudioBufferSourceNode"/> starts): the node then plays a copy, and the next calls of <c>getChannelData()</c>
/// return new arrays.
/// </remarks>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioBuffer"/>
[IdlInterface("AudioBuffer", "https://webaudio.github.io/web-audio-api/#AudioBuffer")]
public sealed class AudioBuffer
{
    /// <summary>The minimum supported sample rate (https://webaudio.github.io/web-audio-api/#supported-sample-rates).</summary>
    internal const float MinSampleRate = 3000;

    /// <summary>The maximum supported sample rate.</summary>
    internal const float MaxSampleRate = 768000;

    // The channels shared with the scripts (null: not created since the creation or the last acquisition)
    private readonly JsBufferSource?[] _channels;

    // The content acquired by the nodes: an immutable copy, valid until a channel is shared with the scripts again
    private float[][]? _acquired;

    /// <summary>Creates a buffer of silence.</summary>
    /// <exception cref="DomException">An option is out of its supported range (a <c>NotSupportedError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffer-audiobuffer"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-audiobuffer-audiobuffer")]
    public AudioBuffer(AudioBufferOptions options)
        : this(Validate(options).NumberOfChannels, options.Length, options.SampleRate)
    {
    }

    internal AudioBuffer(uint numberOfChannels, uint length, float sampleRate)
    {
        Validate(numberOfChannels, length, sampleRate);
        NumberOfChannels = numberOfChannels;
        Length = length;
        SampleRate = sampleRate;
        _channels = new JsBufferSource?[numberOfChannels];
    }

    private static AudioBufferOptions Validate(AudioBufferOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options;
    }

    /// <summary>Validates the arguments of a buffer (https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createbuffer).</summary>
    internal static void Validate(uint numberOfChannels, uint length, float sampleRate)
    {
        if (numberOfChannels is 0 or > AudioNode.MaxChannels)
            throw new DomException($"The number of channels {numberOfChannels} is not between 1 and {AudioNode.MaxChannels}", "NotSupportedError");

        if (length == 0)
            throw new DomException("The length of the buffer cannot be 0", "NotSupportedError");

        if (!(sampleRate >= MinSampleRate && sampleRate <= MaxSampleRate))
            throw new DomException($"The sample rate {sampleRate} is not between {MinSampleRate} and {MaxSampleRate}", "NotSupportedError");

        // The memory of the samples must be allocatable
        if ((long)length * sizeof(float) > Array.MaxLength)
            throw new DomException($"The buffer of {length} frames is too large", "NotSupportedError");
    }

    /// <summary>Gets the sample rate in Hz.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffer-samplerate"/>
    [IdlMember("sampleRate", "https://webaudio.github.io/web-audio-api/#dom-audiobuffer-samplerate")]
    public float SampleRate { get; }

    /// <summary>Gets the length in sample frames.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffer-length"/>
    [IdlMember("length", "https://webaudio.github.io/web-audio-api/#dom-audiobuffer-length")]
    public uint Length { get; }

    /// <summary>Gets the duration in seconds.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffer-duration"/>
    [IdlMember("duration", "https://webaudio.github.io/web-audio-api/#dom-audiobuffer-duration")]
    public double Duration => Length / (double)SampleRate;

    /// <summary>Gets the number of channels.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffer-numberofchannels"/>
    [IdlMember("numberOfChannels", "https://webaudio.github.io/web-audio-api/#dom-audiobuffer-numberofchannels")]
    public uint NumberOfChannels { get; }

    /// <summary>Gets the <c>Float32Array</c> of the samples of a channel for the scripts: use <see cref="GetChannelData"/> in .NET code.</summary>
    /// <exception cref="DomException">The channel does not exist (an <c>IndexSizeError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffer-getchanneldata"/>
    [IdlMember("getChannelData", "https://webaudio.github.io/web-audio-api/#dom-audiobuffer-getchanneldata")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object GetChannelDataArray(uint channel)
    {
        ValidateChannel(channel);
        return GetLiveChannel((int)channel);
    }

    /// <summary>Gets the samples of a channel: the writes are visible to the scripts (until the content is acquired by a node).</summary>
    /// <exception cref="DomException">The channel does not exist (an <c>IndexSizeError</c>).</exception>
    [NotInIdl("The samples for the .NET code: the getChannelData operation is GetChannelDataArray")]
    public Span<float> GetChannelData(int channel)
    {
        ValidateChannel((uint)channel);
        return AsFloats(GetLiveChannel(channel).Bytes.Span);
    }

    /// <summary>Copies the samples of a channel, from a frame, to an array.</summary>
    /// <exception cref="DomException">The channel does not exist (an <c>IndexSizeError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffer-copyfromchannel"/>
    [IdlMember("copyFromChannel", "https://webaudio.github.io/web-audio-api/#dom-audiobuffer-copyfromchannel")]
    public void CopyFromChannel(Span<byte> destination, uint channelNumber, uint bufferOffset = 0)
    {
        ValidateChannel(channelNumber);
        var target = AsFloats(destination);
        if (bufferOffset >= Length)
            return;

        var source = GetChannelContent((int)channelNumber);
        var count = (int)Math.Min(Length - bufferOffset, (uint)target.Length);
        if (source.IsEmpty)
        {
            target[..count].Clear();
        }
        else
        {
            source.Slice((int)bufferOffset, count).CopyTo(target);
        }
    }

    /// <summary>Copies the samples of an array to a channel, from a frame.</summary>
    /// <exception cref="DomException">The channel does not exist (an <c>IndexSizeError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiobuffer-copytochannel"/>
    [IdlMember("copyToChannel", "https://webaudio.github.io/web-audio-api/#dom-audiobuffer-copytochannel")]
    public void CopyToChannel(ReadOnlySpan<byte> source, uint channelNumber, uint bufferOffset = 0)
    {
        ValidateChannel(channelNumber);
        var samples = AsFloats(source);
        if (bufferOffset >= Length)
            return;

        var target = AsFloats(GetLiveChannel((int)channelNumber).Bytes.Span);
        var count = (int)Math.Min(Length - bufferOffset, (uint)samples.Length);
        samples[..count].CopyTo(target[(int)bufferOffset..]);
    }

    private void ValidateChannel(uint channel)
    {
        if (channel >= NumberOfChannels)
            throw new DomException($"The channel {channel} does not exist (the buffer has {NumberOfChannels} channels)", "IndexSizeError");
    }

    /// <summary>The channel shared with the scripts, created with the current content when needed.</summary>
    private JsBufferSource GetLiveChannel(int channel)
    {
        if (_acquired is { } acquired)
        {
            // The channels shared with the scripts can be modified at any time: the acquired content is no longer the
            // current one, so every channel gets its own copy of it
            for (var i = 0; i < _channels.Length; i++)
            {
                if (_channels[i] is null)
                {
                    var bytes = new byte[checked((int)Length * sizeof(float))];
                    var source = unsafe(MemoryMarshal.AsBytes(acquired[i].AsSpan()));
                    source.CopyTo(bytes);
                    _channels[i] = new JsBufferSource(JsBufferKind.Float32Array, bytes);
                }
            }

            _acquired = null;
        }

        return _channels[channel] ??= new JsBufferSource(JsBufferKind.Float32Array, new byte[checked((int)Length * sizeof(float))]);
    }

    /// <summary>The current samples of a channel without sharing it with the scripts, or an empty span for silence.</summary>
    private ReadOnlySpan<float> GetChannelContent(int channel)
    {
        if (_channels[channel] is { } live)
            return AsFloats(live.Bytes.Span);

        return _acquired is { } acquired ? acquired[channel] : default;
    }

    /// <summary>
    /// Acquires the content of the buffer (https://webaudio.github.io/web-audio-api/#acquire-the-content): returns the samples
    /// of the channels, which are not modified afterwards. The arrays given to the scripts no longer share their memory
    /// with the buffer.
    /// </summary>
    internal float[][] AcquireContent()
    {
        if (_acquired is { } acquired)
            return acquired;

        // https://webaudio.github.io/web-audio-api/#acquire-the-content: when the buffer of an array given to the scripts is
        // detached, the content cannot be acquired: the nodes play silence
        var detached = false;
        foreach (var channel in _channels)
        {
            detached |= channel?.IsDetached == true;
        }

        if (detached)
        {
            var silence = new float[NumberOfChannels][];
            for (var i = 0; i < silence.Length; i++)
            {
                silence[i] = new float[Length];
            }

            return silence;
        }

        acquired = new float[NumberOfChannels][];
        for (var i = 0; i < acquired.Length; i++)
        {
            var content = GetChannelContent(i);
            acquired[i] = content.IsEmpty ? new float[Length] : content.ToArray();
            _channels[i] = null;
        }

        _acquired = acquired;
        return acquired;
    }

    /// <summary>Gets the samples of a channel for the .NET code that fills the buffer (e.g. the rendering of an offline context).</summary>
    internal Span<float> GetWritableChannel(int channel) => AsFloats(GetLiveChannel(channel).Bytes.Span);

    private static Span<float> AsFloats(Span<byte> bytes) => unsafe(MemoryMarshal.Cast<byte, float>(bytes));

    private static ReadOnlySpan<float> AsFloats(ReadOnlySpan<byte> bytes) => unsafe(MemoryMarshal.Cast<byte, float>(bytes));
}
