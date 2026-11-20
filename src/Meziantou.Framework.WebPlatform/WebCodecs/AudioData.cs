using System.Buffers.Binary;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.WebCodecs;

/// <summary>The format of the audio samples of an <see cref="AudioData"/> (https://w3c.github.io/webcodecs/#enumdef-audiosampleformat).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#enumdef-audiosampleformat"/>
[IdlInterface("AudioSampleFormat", "https://w3c.github.io/webcodecs/#enumdef-audiosampleformat")]
public enum AudioSampleFormat
{
    /// <summary>Unsigned 8-bit integers (128 is silence), interleaved.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiosampleformat-u8"/>
    [IdlMember("u8", "https://w3c.github.io/webcodecs/#dom-audiosampleformat-u8")]
    U8,

    /// <summary>Signed 16-bit integers, interleaved.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiosampleformat-s16"/>
    [IdlMember("s16", "https://w3c.github.io/webcodecs/#dom-audiosampleformat-s16")]
    S16,

    /// <summary>Signed 32-bit integers, interleaved.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiosampleformat-s32"/>
    [IdlMember("s32", "https://w3c.github.io/webcodecs/#dom-audiosampleformat-s32")]
    S32,

    /// <summary>32-bit floats between -1 and 1, interleaved.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiosampleformat-f32"/>
    [IdlMember("f32", "https://w3c.github.io/webcodecs/#dom-audiosampleformat-f32")]
    F32,

    /// <summary>Unsigned 8-bit integers (128 is silence), one plane per channel.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiosampleformat-u8-planar"/>
    [IdlMember("u8-planar", "https://w3c.github.io/webcodecs/#dom-audiosampleformat-u8-planar")]
    U8Planar,

    /// <summary>Signed 16-bit integers, one plane per channel.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiosampleformat-s16-planar"/>
    [IdlMember("s16-planar", "https://w3c.github.io/webcodecs/#dom-audiosampleformat-s16-planar")]
    S16Planar,

    /// <summary>Signed 32-bit integers, one plane per channel.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiosampleformat-s32-planar"/>
    [IdlMember("s32-planar", "https://w3c.github.io/webcodecs/#dom-audiosampleformat-s32-planar")]
    S32Planar,

    /// <summary>32-bit floats between -1 and 1, one plane per channel.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiosampleformat-f32-planar"/>
    [IdlMember("f32-planar", "https://w3c.github.io/webcodecs/#dom-audiosampleformat-f32-planar")]
    F32Planar,
}

/// <summary>The description of the samples of an <see cref="AudioData"/> (https://w3c.github.io/webcodecs/#dictdef-audiodatainit).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-audiodatainit"/>
[IdlInterface("AudioDataInit", "https://w3c.github.io/webcodecs/#dictdef-audiodatainit")]
public sealed class AudioDataInit
{
    /// <summary>Gets the format of the samples.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodatainit-format"/>
    [IdlMember("format", "https://w3c.github.io/webcodecs/#dom-audiodatainit-format")]
    public required AudioSampleFormat Format { get; init; }

    /// <summary>Gets the sample rate in Hz.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodatainit-samplerate"/>
    [IdlMember("sampleRate", "https://w3c.github.io/webcodecs/#dom-audiodatainit-samplerate")]
    public required float SampleRate { get; init; }

    /// <summary>Gets the number of frames (samples per channel).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodatainit-numberofframes"/>
    [IdlMember("numberOfFrames", "https://w3c.github.io/webcodecs/#dom-audiodatainit-numberofframes")]
    public required uint NumberOfFrames { get; init; }

    /// <summary>Gets the number of channels.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodatainit-numberofchannels"/>
    [IdlMember("numberOfChannels", "https://w3c.github.io/webcodecs/#dom-audiodatainit-numberofchannels")]
    public required uint NumberOfChannels { get; init; }

    /// <summary>Gets the presentation timestamp in microseconds.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodatainit-timestamp"/>
    [IdlMember("timestamp", "https://w3c.github.io/webcodecs/#dom-audiodatainit-timestamp")]
    public required long Timestamp { get; init; }

    /// <summary>Gets the samples: a buffer source of the scripts, a <see cref="byte"/> array or a memory of bytes (the bytes of the samples, little-endian).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodatainit-data"/>
    [IdlMember("data", "https://w3c.github.io/webcodecs/#dom-audiodatainit-data")]
    public required object Data { get; init; }

    /// <summary>Gets the <c>ArrayBuffer</c> objects detached by the construction.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodatainit-transfer"/>
    [IdlMember("transfer", "https://w3c.github.io/webcodecs/#dom-audiodatainit-transfer")]
    public IReadOnlyList<object>? Transfer { get; init; }
}

/// <summary>The options of <see cref="AudioData.CopyTo"/> and <see cref="AudioData.AllocationSize"/> (https://w3c.github.io/webcodecs/#dictdef-audiodatacopytooptions).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-audiodatacopytooptions"/>
[IdlInterface("AudioDataCopyToOptions", "https://w3c.github.io/webcodecs/#dictdef-audiodatacopytooptions")]
public sealed class AudioDataCopyToOptions
{
    /// <summary>Gets the index of the plane to copy: 0 for an interleaved format, the channel for a planar format.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodatacopytooptions-planeindex"/>
    [IdlMember("planeIndex", "https://w3c.github.io/webcodecs/#dom-audiodatacopytooptions-planeindex")]
    public required uint PlaneIndex { get; init; }

    /// <summary>Gets the index of the first frame to copy.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodatacopytooptions-frameoffset"/>
    [IdlMember("frameOffset", "https://w3c.github.io/webcodecs/#dom-audiodatacopytooptions-frameoffset")]
    public uint FrameOffset { get; init; }

    /// <summary>Gets the number of frames to copy (the frames from the offset by default).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodatacopytooptions-framecount"/>
    [IdlMember("frameCount", "https://w3c.github.io/webcodecs/#dom-audiodatacopytooptions-framecount")]
    public uint? FrameCount { get; init; }

    /// <summary>Gets the format of the copied samples (the format of the data by default).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodatacopytooptions-format"/>
    [IdlMember("format", "https://w3c.github.io/webcodecs/#dom-audiodatacopytooptions-format")]
    public AudioSampleFormat? Format { get; init; }
}

/// <summary>
/// Audio samples (https://w3c.github.io/webcodecs/#audiodata): the decoded audio of an <see cref="AudioDecoder"/>, the input
/// of an <see cref="AudioEncoder"/>, or the audio created by the scripts from a buffer. The samples are immutable and shared
/// by the clones; they can be copied to a buffer in any sample format, and posted to a worker (serialized or transferred).
/// </summary>
/// <seealso href="https://w3c.github.io/webcodecs/#audiodata"/>
[IdlInterface("AudioData", "https://w3c.github.io/webcodecs/#audiodata")]
public sealed class AudioData : IStructuredSerializable, IStructuredTransferable, IAgentClusterSerializable
{
    /// <summary>The largest number of samples (frames by channels) of an audio data, to bound the memory used by the scripts.</summary>
    internal const long MaxSampleCount = 1L << 28;

    private byte[]? _resource;
    private AudioSampleFormat _resourceFormat;
    private AudioSampleFormat? _format;
    private float _sampleRate;
    private uint _numberOfFrames;
    private uint _numberOfChannels;

    /// <summary>Creates audio samples from a buffer (https://w3c.github.io/webcodecs/#dom-audiodata-audiodata): the samples are copied.</summary>
    /// <param name="init">The description of the samples.</param>
    /// <exception cref="TypeErrorException">The description is invalid, or the buffer is too small.</exception>
    /// <exception cref="DomException">A <c>DataCloneError</c> when the transfer list has a detached buffer or a buffer twice.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodata-audiodata"/>
    [IdlMember("constructor", "https://w3c.github.io/webcodecs/#dom-audiodata-audiodata")]
    public AudioData(AudioDataInit init)
    {
        ArgumentNullException.ThrowIfNull(init);
        var bytes = WebCodecsBuffers.GetBytes(init.Data);

        // https://w3c.github.io/webcodecs/#valid-audiodatainit
        if (!(init.SampleRate > 0) || !float.IsFinite(init.SampleRate))
            throw new TypeErrorException("The sample rate must be positive");

        if (init.NumberOfFrames == 0)
            throw new TypeErrorException("The number of frames must not be zero");

        if (init.NumberOfChannels == 0)
            throw new TypeErrorException("The number of channels must not be zero");

        var sampleCount = (long)init.NumberOfFrames * init.NumberOfChannels;
        if (sampleCount > MaxSampleCount)
            throw new TypeErrorException("The audio data is too large");

        var size = sampleCount * GetBytesPerSample(init.Format);
        if (bytes.Length < size)
            throw new TypeErrorException("The data is smaller than the samples");

        var transfer = WebCodecsBuffers.ValidateTransfer(init.Transfer);
        _resource = bytes[..(int)size].ToArray();
        WebCodecsBuffers.DetachTransfer(transfer);
        _resourceFormat = init.Format;
        _format = init.Format;
        _sampleRate = init.SampleRate;
        _numberOfFrames = init.NumberOfFrames;
        _numberOfChannels = init.NumberOfChannels;
        Timestamp = init.Timestamp;
    }

    private AudioData(AudioData data)
    {
        _resource = data._resource;
        _resourceFormat = data._resourceFormat;
        _format = data._format;
        _sampleRate = data._sampleRate;
        _numberOfFrames = data._numberOfFrames;
        _numberOfChannels = data._numberOfChannels;
        Timestamp = data.Timestamp;
    }

    /// <summary>Gets the format of the samples, or null when the data is closed.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodata-format"/>
    [IdlMember("format", "https://w3c.github.io/webcodecs/#dom-audiodata-format")]
    public AudioSampleFormat? Format => _format;

    /// <summary>Gets the sample rate in Hz (0 when the data is closed).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodata-samplerate"/>
    [IdlMember("sampleRate", "https://w3c.github.io/webcodecs/#dom-audiodata-samplerate")]
    public float SampleRate => _sampleRate;

    /// <summary>Gets the number of frames (samples per channel; 0 when the data is closed).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodata-numberofframes"/>
    [IdlMember("numberOfFrames", "https://w3c.github.io/webcodecs/#dom-audiodata-numberofframes")]
    public uint NumberOfFrames => _numberOfFrames;

    /// <summary>Gets the number of channels (0 when the data is closed).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodata-numberofchannels"/>
    [IdlMember("numberOfChannels", "https://w3c.github.io/webcodecs/#dom-audiodata-numberofchannels")]
    public uint NumberOfChannels => _numberOfChannels;

    /// <summary>Gets the duration in microseconds (0 when the data is closed).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodata-duration"/>
    [IdlMember("duration", "https://w3c.github.io/webcodecs/#dom-audiodata-duration")]
    public ulong Duration => _sampleRate > 0 ? (ulong)(_numberOfFrames * 1_000_000d / _sampleRate) : 0;

    /// <summary>Gets the presentation timestamp in microseconds.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodata-timestamp"/>
    [IdlMember("timestamp", "https://w3c.github.io/webcodecs/#dom-audiodata-timestamp")]
    public long Timestamp { get; }

    /// <summary>Gets whether the data is closed ([[Detached]]).</summary>
    internal bool IsDetached => _resource is null;

    /// <summary>The size in bytes of the samples (to bound the memory used by the queues of the codecs).</summary>
    internal long ByteLength => _resource?.LongLength ?? 0;

    /// <summary>Returns the number of bytes <see cref="CopyTo"/> writes with the options (https://w3c.github.io/webcodecs/#dom-audiodata-allocationsize).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the data is closed, a <c>NotSupportedError</c> for an unsupported conversion.</exception>
    /// <exception cref="RangeErrorException">The plane or the frames do not exist.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodata-allocationsize"/>
    [IdlMember("allocationSize", "https://w3c.github.io/webcodecs/#dom-audiodata-allocationsize")]
    public uint AllocationSize(AudioDataCopyToOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (IsDetached)
            throw new DomException("The audio data is closed", "InvalidStateError");

        var elementCount = ComputeCopyElementCount(options);
        return (uint)(elementCount * GetBytesPerSample(options.Format ?? _format!.Value));
    }

    /// <summary>
    /// Copies the samples of a plane into a buffer (https://w3c.github.io/webcodecs/#dom-audiodata-copyto), converted to the
    /// format of the options. Any format can be converted to any format.
    /// </summary>
    /// <param name="destination">The buffer.</param>
    /// <param name="options">The plane, the frames and the format.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the data is closed.</exception>
    /// <exception cref="RangeErrorException">The plane or the frames do not exist, or the buffer is too small.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodata-copyto"/>
    [IdlMember("copyTo", "https://w3c.github.io/webcodecs/#dom-audiodata-copyto")]
    public void CopyTo(Span<byte> destination, AudioDataCopyToOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_resource is not { } resource)
            throw new DomException("The audio data is closed", "InvalidStateError");

        var elementCount = ComputeCopyElementCount(options);
        var destinationFormat = options.Format ?? _format!.Value;
        var bytesPerSample = GetBytesPerSample(destinationFormat);
        if (elementCount * bytesPerSample > destination.Length)
            throw new RangeErrorException("The destination is smaller than the copied samples");

        var channels = (int)_numberOfChannels;
        var frames = (int)_numberOfFrames;
        var frameOffset = (int)options.FrameOffset;
        if (IsPlanar(destinationFormat))
        {
            var channel = (int)options.PlaneIndex;
            for (var i = 0; i < elementCount; i++)
            {
                WriteSample(destination, i, destinationFormat, ReadSample(resource, frameOffset + i, channel, channels, frames));
            }
        }
        else
        {
            for (var i = 0; i < elementCount; i++)
            {
                var frame = frameOffset + (i / channels);
                WriteSample(destination, i, destinationFormat, ReadSample(resource, frame, i % channels, channels, frames));
            }
        }
    }

    /// <summary>Creates audio data that references the same samples (https://w3c.github.io/webcodecs/#dom-audiodata-clone).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the data is closed.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodata-clone"/>
    [IdlMember("clone", "https://w3c.github.io/webcodecs/#dom-audiodata-clone")]
    public AudioData Clone()
    {
        if (IsDetached)
            throw new DomException("The audio data is closed", "InvalidStateError");

        return new AudioData(this);
    }

    /// <summary>Releases the reference to the samples (https://w3c.github.io/webcodecs/#close-audiodata): the data cannot be used anymore.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodata-close"/>
    [IdlMember("close", "https://w3c.github.io/webcodecs/#dom-audiodata-close")]
    public void Close()
    {
        _resource = null;
        _format = null;
        _sampleRate = 0;
        _numberOfFrames = 0;
        _numberOfChannels = 0;
    }

    /// <summary>Returns the samples as 32-bit floats, interleaved (for the encoders and the sinks).</summary>
    internal float[] GetInterleavedFloats()
    {
        if (_resource is not { } resource)
            return [];

        var channels = (int)_numberOfChannels;
        var frames = (int)_numberOfFrames;
        var result = new float[(long)channels * frames];
        for (var frame = 0; frame < frames; frame++)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                result[(frame * channels) + channel] = (float)ReadSample(resource, frame, channel, channels, frames).Float;
            }
        }

        return result;
    }

    /// <summary>The number of bytes of a sample of a format.</summary>
    internal static int GetBytesPerSample(AudioSampleFormat format) => format switch
    {
        AudioSampleFormat.U8 or AudioSampleFormat.U8Planar => 1,
        AudioSampleFormat.S16 or AudioSampleFormat.S16Planar => 2,
        _ => 4,
    };

    /// <summary>Whether a format has one plane per channel.</summary>
    internal static bool IsPlanar(AudioSampleFormat format) => format is AudioSampleFormat.U8Planar or AudioSampleFormat.S16Planar or AudioSampleFormat.S32Planar or AudioSampleFormat.F32Planar;

    /// <summary>https://w3c.github.io/webcodecs/#compute-copy-element-count</summary>
    private long ComputeCopyElementCount(AudioDataCopyToOptions options)
    {
        var destinationFormat = options.Format ?? _format!.Value;
        if (!IsPlanar(destinationFormat) && options.PlaneIndex > 0)
            throw new RangeErrorException("An interleaved format has one plane");

        if (IsPlanar(destinationFormat) && options.PlaneIndex >= _numberOfChannels)
            throw new RangeErrorException("The plane index is not a channel of the data");

        var frameCount = (long)_numberOfFrames;
        if (options.FrameOffset >= frameCount)
            throw new RangeErrorException("The frame offset is not a frame of the data");

        var copyFrameCount = frameCount - options.FrameOffset;
        if (options.FrameCount is { } requested)
        {
            if (requested > copyFrameCount)
                throw new RangeErrorException("The frame count exceeds the frames of the data");

            copyFrameCount = requested;
        }

        return IsPlanar(destinationFormat) ? copyFrameCount : copyFrameCount * _numberOfChannels;
    }

    /// <summary>Reads a sample of the resource as its integer value scaled to 32 bits (for the integer formats) and as a float.</summary>
    private Sample ReadSample(byte[] resource, int frame, int channel, int channels, int frames)
    {
        var index = IsPlanar(_resourceFormat) ? ((long)channel * frames) + frame : ((long)frame * channels) + channel;
        switch (_resourceFormat)
        {
            case AudioSampleFormat.U8 or AudioSampleFormat.U8Planar:
            {
                var value = resource[index] - 128;
                return new Sample(value << 24, value / 128d, IsInteger: true);
            }

            case AudioSampleFormat.S16 or AudioSampleFormat.S16Planar:
            {
                var value = BinaryPrimitives.ReadInt16LittleEndian(resource.AsSpan((int)(index * 2)));
                return new Sample(value << 16, value / 32768d, IsInteger: true);
            }

            case AudioSampleFormat.S32 or AudioSampleFormat.S32Planar:
            {
                var value = BinaryPrimitives.ReadInt32LittleEndian(resource.AsSpan((int)(index * 4)));
                return new Sample(value, value / 2147483648d, IsInteger: true);
            }

            default:
            {
                var value = BinaryPrimitives.ReadSingleLittleEndian(resource.AsSpan((int)(index * 4)));
                return new Sample(0, value, IsInteger: false);
            }
        }
    }

    /// <summary>Writes a sample converted to a format (https://w3c.github.io/webcodecs/#audio-sample-format): the integer formats are converted by shifts, the floats are scaled and clamped.</summary>
    private static void WriteSample(Span<byte> destination, int index, AudioSampleFormat format, Sample sample)
    {
        switch (format)
        {
            case AudioSampleFormat.U8 or AudioSampleFormat.U8Planar:
                destination[index] = sample.IsInteger ? (byte)((sample.Integer >> 24) + 128) : (byte)Math.Clamp(Math.Floor((sample.Float * 128) + 128), 0, 255);
                break;

            case AudioSampleFormat.S16 or AudioSampleFormat.S16Planar:
                BinaryPrimitives.WriteInt16LittleEndian(destination[(index * 2)..], sample.IsInteger ? (short)(sample.Integer >> 16) : (short)Math.Clamp(Math.Floor(sample.Float * 32768), short.MinValue, short.MaxValue));
                break;

            case AudioSampleFormat.S32 or AudioSampleFormat.S32Planar:
                BinaryPrimitives.WriteInt32LittleEndian(destination[(index * 4)..], sample.IsInteger ? sample.Integer : (int)Math.Clamp(Math.Floor(sample.Float * 2147483648d), int.MinValue, int.MaxValue));
                break;

            default:
                BinaryPrimitives.WriteSingleLittleEndian(destination[(index * 4)..], (float)sample.Float);
                break;
        }
    }

    /// <summary>The serialization steps (https://w3c.github.io/webcodecs/#audiodata-transfer-serialization): the serialized data shares the samples.</summary>
    object IStructuredSerializable.CloneForStructuredSerialization()
    {
        if (IsDetached)
            throw new DomException("The audio data is closed", "DataCloneError");

        return new AudioData(this);
    }

    void IStructuredTransferable.ValidateTransfer(bool serialized)
    {
        if (serialized && IsDetached)
            throw new DomException("The audio data is closed", "DataCloneError");
    }

    /// <summary>The transfer steps: the data is closed, the received data has its samples.</summary>
    ITransferredData IStructuredTransferable.Transfer()
    {
        var clone = new AudioData(this);
        Close();
        return new TransferredAudioData(clone, GlobalScope.Current?.AgentClusterKey);
    }

    private sealed class TransferredAudioData(AudioData data, object? agentCluster) : ITransferredData
    {
        public object Receive(GlobalScope? target)
        {
            // The samples cannot leave the agent cluster
            if (agentCluster is not null && target?.AgentClusterKey is { } targetAgentCluster && !ReferenceEquals(agentCluster, targetAgentCluster))
                throw new DomException("The audio data cannot be received in another agent cluster", "DataCloneError");

            return data;
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct Sample(int Integer, double Float, bool IsInteger);
}
