using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.WebCodecs;

/// <summary>
/// Decodes audio (https://w3c.github.io/webcodecs/#audiodecoder-interface): the <see cref="EncodedAudioChunk"/> objects
/// given to <see cref="Decode"/> are decoded by the codecs of <see cref="BrowsingContextOptions.MediaCodecs"/>, and the
/// <see cref="AudioData"/> objects are given to the output callback.
/// </summary>
/// <seealso href="https://w3c.github.io/webcodecs/#audiodecoder-interface"/>
[IdlInterface("AudioDecoder", "https://w3c.github.io/webcodecs/#audiodecoder-interface")]
public sealed class AudioDecoder : EventTarget
{
    private readonly CodecEngine<MediaAudioDecoder> _engine;
    private readonly AudioDataOutputCallback _output;
    private bool _keyChunkRequired;

    /// <summary>Creates a decoder (https://w3c.github.io/webcodecs/#dom-audiodecoder-audiodecoder).</summary>
    /// <param name="init">The output and error callbacks.</param>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecoder-audiodecoder"/>
    [IdlMember("constructor", "https://w3c.github.io/webcodecs/#dom-audiodecoder-audiodecoder")]
    public AudioDecoder(AudioDecoderInit init)
    {
        ArgumentNullException.ThrowIfNull(init);
        _output = init.Output;
        _engine = new CodecEngine<MediaAudioDecoder>(this, init.Error, GlobalScope.Current);
    }

    /// <summary>Gets the state of the decoder.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecoder-state"/>
    [IdlMember("state", "https://w3c.github.io/webcodecs/#dom-audiodecoder-state")]
    public CodecState State => _engine.State;

    /// <summary>Gets the number of pending decode requests.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecoder-decodequeuesize"/>
    [IdlMember("decodeQueueSize", "https://w3c.github.io/webcodecs/#dom-audiodecoder-decodequeuesize")]
    public uint DecodeQueueSize => _engine.QueueSize;

    /// <summary>Gets or sets the event handler of the <c>dequeue</c> event (the queue size decreased).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecoder-ondequeue"/>
    [IdlMember("ondequeue", "https://w3c.github.io/webcodecs/#dom-audiodecoder-ondequeue")]
    public Action<Event>? OnDequeue
    {
        get => GetEventHandler("dequeue");
        set => SetEventHandler("dequeue", value);
    }

    /// <summary>Configures the decoder (https://w3c.github.io/webcodecs/#dom-audiodecoder-configure): a configuration that the codecs do not support closes it with a <c>NotSupportedError</c>.</summary>
    /// <param name="config">The configuration.</param>
    /// <exception cref="TypeErrorException">The configuration is invalid.</exception>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the decoder is closed.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecoder-configure"/>
    [IdlMember("configure", "https://w3c.github.io/webcodecs/#dom-audiodecoder-configure")]
    public void Configure(AudioDecoderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!WebCodecsConfigs.IsValid(config))
            throw new TypeErrorException("The configuration is invalid");

        _engine.ThrowIfClosed();
        _keyChunkRequired = true;
        var clone = WebCodecsConfigs.Clone(config);
        var backend = _engine.Backend;
        _engine.Configure(() => backend?.IsConfigSupported(clone) == true ? backend.CreateAudioDecoder(clone) : null, implementation => Output(implementation.Flush()));
    }

    /// <summary>Decodes a chunk (https://w3c.github.io/webcodecs/#dom-audiodecoder-decode).</summary>
    /// <param name="chunk">The chunk.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the decoder is not configured, a <c>DataError</c> when a key chunk is required.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecoder-decode"/>
    [IdlMember("decode", "https://w3c.github.io/webcodecs/#dom-audiodecoder-decode")]
    public void Decode(EncodedAudioChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        _engine.ThrowIfNotConfigured();
        if (_keyChunkRequired)
        {
            if (chunk.Type != EncodedAudioChunkType.Key)
                throw new DomException("A key chunk is required", "DataError");

            _keyChunkRequired = false;
        }

        _engine.EnqueueWork(implementation => Output(implementation.Decode(chunk)));
    }

    /// <summary>Decodes the pending chunks (https://w3c.github.io/webcodecs/#dom-audiodecoder-flush): the promise resolves once their outputs are delivered.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecoder-flush"/>
    [IdlMember("flush", "https://w3c.github.io/webcodecs/#dom-audiodecoder-flush")]
    public Task Flush()
    {
        if (_engine.State == CodecState.Configured)
            _keyChunkRequired = true;

        return _engine.Flush(implementation => Output(implementation.Flush()));
    }

    /// <summary>Discards the pending chunks and the configuration (https://w3c.github.io/webcodecs/#dom-audiodecoder-reset).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the decoder is closed.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecoder-reset"/>
    [IdlMember("reset", "https://w3c.github.io/webcodecs/#dom-audiodecoder-reset")]
    public void Reset() => _engine.Reset(WebCodecsConfigs.CreateAbortError());

    /// <summary>Closes the decoder (https://w3c.github.io/webcodecs/#dom-audiodecoder-close).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the decoder is closed.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecoder-close"/>
    [IdlMember("close", "https://w3c.github.io/webcodecs/#dom-audiodecoder-close")]
    public void Close()
    {
        _engine.ThrowIfClosed();
        _engine.Close(WebCodecsConfigs.CreateAbortError());
    }

    /// <summary>Returns whether the codecs support a configuration (https://w3c.github.io/webcodecs/#dom-audiodecoder-isconfigsupported).</summary>
    /// <param name="config">The configuration.</param>
    /// <returns>A task that completes with the support and the configuration with the recognized members, or fails with a <see cref="TypeErrorException"/> for an invalid configuration.</returns>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecoder-isconfigsupported"/>
    [IdlMember("isConfigSupported", "https://w3c.github.io/webcodecs/#dom-audiodecoder-isconfigsupported")]
    public static Task<AudioDecoderSupport> IsConfigSupported(AudioDecoderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!WebCodecsConfigs.IsValid(config))
            return Task.FromException<AudioDecoderSupport>(new TypeErrorException("The configuration is invalid"));

        var clone = WebCodecsConfigs.Clone(config);
        var supported = WebCodecsConfigs.CheckSupport(backend => backend.IsConfigSupported(clone));
        return WebCodecsConfigs.Resolve(new AudioDecoderSupport { Supported = supported, Config = clone });
    }

    private void Output(IReadOnlyList<AudioData> outputs)
    {
        var generation = _engine.Generation;
        foreach (var output in outputs)
        {
            if (generation != _engine.Generation)
            {
                output.Close();
                continue;
            }

            _engine.InvokeCallback(() => _output(output));
        }
    }
}

/// <summary>
/// Decodes video (https://w3c.github.io/webcodecs/#videodecoder-interface): the <see cref="EncodedVideoChunk"/> objects
/// given to <see cref="Decode"/> are decoded by the codecs of <see cref="BrowsingContextOptions.MediaCodecs"/>, and the
/// <see cref="VideoFrame"/> objects are given to the output callback, with the orientation, the display aspect ratio and
/// the color space of the configuration.
/// </summary>
/// <seealso href="https://w3c.github.io/webcodecs/#videodecoder-interface"/>
[IdlInterface("VideoDecoder", "https://w3c.github.io/webcodecs/#videodecoder-interface")]
public sealed class VideoDecoder : EventTarget
{
    private readonly CodecEngine<MediaVideoDecoder> _engine;
    private readonly VideoFrameOutputCallback _output;
    private VideoDecoderConfig? _activeConfig;
    private bool _keyChunkRequired;

    /// <summary>Creates a decoder (https://w3c.github.io/webcodecs/#dom-videodecoder-videodecoder).</summary>
    /// <param name="init">The output and error callbacks.</param>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoder-videodecoder"/>
    [IdlMember("constructor", "https://w3c.github.io/webcodecs/#dom-videodecoder-videodecoder")]
    public VideoDecoder(VideoDecoderInit init)
    {
        ArgumentNullException.ThrowIfNull(init);
        _output = init.Output;
        _engine = new CodecEngine<MediaVideoDecoder>(this, init.Error, GlobalScope.Current);
    }

    /// <summary>Gets the state of the decoder.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoder-state"/>
    [IdlMember("state", "https://w3c.github.io/webcodecs/#dom-videodecoder-state")]
    public CodecState State => _engine.State;

    /// <summary>Gets the number of pending decode requests.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoder-decodequeuesize"/>
    [IdlMember("decodeQueueSize", "https://w3c.github.io/webcodecs/#dom-videodecoder-decodequeuesize")]
    public uint DecodeQueueSize => _engine.QueueSize;

    /// <summary>Gets or sets the event handler of the <c>dequeue</c> event (the queue size decreased).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoder-ondequeue"/>
    [IdlMember("ondequeue", "https://w3c.github.io/webcodecs/#dom-videodecoder-ondequeue")]
    public Action<Event>? OnDequeue
    {
        get => GetEventHandler("dequeue");
        set => SetEventHandler("dequeue", value);
    }

    /// <summary>Configures the decoder (https://w3c.github.io/webcodecs/#dom-videodecoder-configure): a configuration that the codecs do not support closes it with a <c>NotSupportedError</c>.</summary>
    /// <param name="config">The configuration.</param>
    /// <exception cref="TypeErrorException">The configuration is invalid.</exception>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the decoder is closed.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoder-configure"/>
    [IdlMember("configure", "https://w3c.github.io/webcodecs/#dom-videodecoder-configure")]
    public void Configure(VideoDecoderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!WebCodecsConfigs.IsValid(config))
            throw new TypeErrorException("The configuration is invalid");

        _engine.ThrowIfClosed();
        _keyChunkRequired = true;
        var clone = WebCodecsConfigs.Clone(config);
        var backend = _engine.Backend;
        _engine.Configure(
            () =>
            {
                var implementation = backend?.IsConfigSupported(clone) == true ? backend.CreateVideoDecoder(clone) : null;
                if (implementation is not null)
                    _activeConfig = clone;

                return implementation;
            },
            implementation => Output(implementation.Flush()));
    }

    /// <summary>Decodes a chunk (https://w3c.github.io/webcodecs/#dom-videodecoder-decode).</summary>
    /// <param name="chunk">The chunk.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the decoder is not configured, a <c>DataError</c> when a key chunk is required.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoder-decode"/>
    [IdlMember("decode", "https://w3c.github.io/webcodecs/#dom-videodecoder-decode")]
    public void Decode(EncodedVideoChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        _engine.ThrowIfNotConfigured();
        if (_keyChunkRequired)
        {
            if (chunk.Type != EncodedVideoChunkType.Key)
                throw new DomException("A key chunk is required", "DataError");

            _keyChunkRequired = false;
        }

        _engine.EnqueueWork(implementation => Output(implementation.Decode(chunk)));
    }

    /// <summary>Decodes the pending chunks (https://w3c.github.io/webcodecs/#dom-videodecoder-flush): the promise resolves once their frames are delivered.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoder-flush"/>
    [IdlMember("flush", "https://w3c.github.io/webcodecs/#dom-videodecoder-flush")]
    public Task Flush()
    {
        if (_engine.State == CodecState.Configured)
            _keyChunkRequired = true;

        return _engine.Flush(implementation => Output(implementation.Flush()));
    }

    /// <summary>Discards the pending chunks and the configuration (https://w3c.github.io/webcodecs/#dom-videodecoder-reset).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the decoder is closed.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoder-reset"/>
    [IdlMember("reset", "https://w3c.github.io/webcodecs/#dom-videodecoder-reset")]
    public void Reset() => _engine.Reset(WebCodecsConfigs.CreateAbortError());

    /// <summary>Closes the decoder (https://w3c.github.io/webcodecs/#dom-videodecoder-close).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the decoder is closed.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoder-close"/>
    [IdlMember("close", "https://w3c.github.io/webcodecs/#dom-videodecoder-close")]
    public void Close()
    {
        _engine.ThrowIfClosed();
        _engine.Close(WebCodecsConfigs.CreateAbortError());
    }

    /// <summary>Returns whether the codecs support a configuration (https://w3c.github.io/webcodecs/#dom-videodecoder-isconfigsupported).</summary>
    /// <param name="config">The configuration.</param>
    /// <returns>A task that completes with the support and the configuration with the recognized members, or fails with a <see cref="TypeErrorException"/> for an invalid configuration.</returns>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoder-isconfigsupported"/>
    [IdlMember("isConfigSupported", "https://w3c.github.io/webcodecs/#dom-videodecoder-isconfigsupported")]
    public static Task<VideoDecoderSupport> IsConfigSupported(VideoDecoderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!WebCodecsConfigs.IsValid(config))
            return Task.FromException<VideoDecoderSupport>(new TypeErrorException("The configuration is invalid"));

        var clone = WebCodecsConfigs.Clone(config);
        var supported = WebCodecsConfigs.CheckSupport(backend => backend.IsConfigSupported(clone));
        return WebCodecsConfigs.Resolve(new VideoDecoderSupport { Supported = supported, Config = clone });
    }

    private void Output(IReadOnlyList<VideoFrame> outputs)
    {
        var generation = _engine.Generation;
        foreach (var decoded in outputs)
        {
            if (generation != _engine.Generation)
            {
                decoded.Close();
                continue;
            }

            var output = _activeConfig is { } config ? ApplyConfig(decoded, config) : decoded;
            _engine.InvokeCallback(() => _output(output));
        }
    }

    /// <summary>https://w3c.github.io/webcodecs/#output-videoframes: the orientation, the display aspect ratio and the color space of the configuration.</summary>
    private static VideoFrame ApplyConfig(VideoFrame frame, VideoDecoderConfig config)
    {
        if (frame.IsDetached)
            return frame;

        var result = frame;
        var hasAspect = config.DisplayAspectWidth is > 0 && config.DisplayAspectHeight is > 0;
        if (config.Rotation != 0 || config.Flip || hasAspect)
        {
            uint? displayWidth = null;
            uint? displayHeight = null;
            if (hasAspect && frame.VisibleRect is { } visible)
            {
                displayHeight = (uint)visible.Height;
                displayWidth = (uint)Math.Max(1, Math.Round(visible.Height * config.DisplayAspectWidth!.Value / config.DisplayAspectHeight!.Value, MidpointRounding.AwayFromZero));
                var rotation = config.Rotation;
                if (Math.Abs(Math.Round(rotation / 90, MidpointRounding.AwayFromZero) % 2) == 1)
                    (displayWidth, displayHeight) = (displayHeight, displayWidth);
            }

            result = new VideoFrame(frame, new VideoFrameInit { Rotation = config.Rotation, Flip = config.Flip, DisplayWidth = displayWidth, DisplayHeight = displayHeight });
            frame.Close();
        }

        if (config.ColorSpace is { } colorSpace)
            result.OverrideColorSpace(new VideoColorSpace(colorSpace));

        return result;
    }
}

/// <summary>
/// Encodes audio (https://w3c.github.io/webcodecs/#audioencoder-interface): the <see cref="AudioData"/> objects given to
/// <see cref="Encode"/> are encoded by the codecs of <see cref="BrowsingContextOptions.MediaCodecs"/>, and the
/// <see cref="EncodedAudioChunk"/> objects are given to the output callback with the configuration of their decoder.
/// </summary>
/// <seealso href="https://w3c.github.io/webcodecs/#audioencoder-interface"/>
[IdlInterface("AudioEncoder", "https://w3c.github.io/webcodecs/#audioencoder-interface")]
public sealed class AudioEncoder : EventTarget
{
    private readonly CodecEngine<MediaAudioEncoder> _engine;
    private readonly EncodedAudioChunkOutputCallback _output;
    private AudioEncoderConfig? _activeConfig;
    private AudioDecoderConfig? _activeOutputConfig;

    /// <summary>Creates an encoder (https://w3c.github.io/webcodecs/#dom-audioencoder-audioencoder).</summary>
    /// <param name="init">The output and error callbacks.</param>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoder-audioencoder"/>
    [IdlMember("constructor", "https://w3c.github.io/webcodecs/#dom-audioencoder-audioencoder")]
    public AudioEncoder(AudioEncoderInit init)
    {
        ArgumentNullException.ThrowIfNull(init);
        _output = init.Output;
        _engine = new CodecEngine<MediaAudioEncoder>(this, init.Error, GlobalScope.Current);
    }

    /// <summary>Gets the state of the encoder.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoder-state"/>
    [IdlMember("state", "https://w3c.github.io/webcodecs/#dom-audioencoder-state")]
    public CodecState State => _engine.State;

    /// <summary>Gets the number of pending encode requests.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoder-encodequeuesize"/>
    [IdlMember("encodeQueueSize", "https://w3c.github.io/webcodecs/#dom-audioencoder-encodequeuesize")]
    public uint EncodeQueueSize => _engine.QueueSize;

    /// <summary>Gets or sets the event handler of the <c>dequeue</c> event (the queue size decreased).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoder-ondequeue"/>
    [IdlMember("ondequeue", "https://w3c.github.io/webcodecs/#dom-audioencoder-ondequeue")]
    public Action<Event>? OnDequeue
    {
        get => GetEventHandler("dequeue");
        set => SetEventHandler("dequeue", value);
    }

    /// <summary>Configures the encoder (https://w3c.github.io/webcodecs/#dom-audioencoder-configure): a configuration that the codecs do not support closes it with a <c>NotSupportedError</c>.</summary>
    /// <param name="config">The configuration.</param>
    /// <exception cref="TypeErrorException">The configuration is invalid.</exception>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the encoder is closed.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoder-configure"/>
    [IdlMember("configure", "https://w3c.github.io/webcodecs/#dom-audioencoder-configure")]
    public void Configure(AudioEncoderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!WebCodecsConfigs.IsValid(config))
            throw new TypeErrorException("The configuration is invalid");

        _engine.ThrowIfClosed();
        var clone = WebCodecsConfigs.Clone(config);
        var backend = _engine.Backend;
        _engine.Configure(
            () =>
            {
                var implementation = backend?.IsConfigSupported(clone) == true ? backend.CreateAudioEncoder(clone) : null;
                if (implementation is not null)
                {
                    _activeConfig = clone;
                    _activeOutputConfig = null;
                }

                return implementation;
            },
            implementation => Output(implementation.Flush()));
    }

    /// <summary>Encodes audio (https://w3c.github.io/webcodecs/#dom-audioencoder-encode): a clone of the data is encoded.</summary>
    /// <param name="data">The audio.</param>
    /// <exception cref="TypeErrorException">The data is closed.</exception>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the encoder is not configured.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoder-encode"/>
    [IdlMember("encode", "https://w3c.github.io/webcodecs/#dom-audioencoder-encode")]
    public void Encode(AudioData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.IsDetached)
            throw new TypeErrorException("The audio data is closed");

        _engine.ThrowIfNotConfigured();
        var clone = data.Clone();
        _engine.EnqueueWork(
            implementation =>
            {
                try
                {
                    Output(implementation.Encode(clone));
                }
                finally
                {
                    clone.Close();
                }
            },
            clone.Close);
    }

    /// <summary>Encodes the pending audio (https://w3c.github.io/webcodecs/#dom-audioencoder-flush): the promise resolves once the chunks are delivered.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoder-flush"/>
    [IdlMember("flush", "https://w3c.github.io/webcodecs/#dom-audioencoder-flush")]
    public Task Flush() => _engine.Flush(implementation => Output(implementation.Flush()));

    /// <summary>Discards the pending audio and the configuration (https://w3c.github.io/webcodecs/#dom-audioencoder-reset).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the encoder is closed.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoder-reset"/>
    [IdlMember("reset", "https://w3c.github.io/webcodecs/#dom-audioencoder-reset")]
    public void Reset() => _engine.Reset(WebCodecsConfigs.CreateAbortError());

    /// <summary>Closes the encoder (https://w3c.github.io/webcodecs/#dom-audioencoder-close).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the encoder is closed.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoder-close"/>
    [IdlMember("close", "https://w3c.github.io/webcodecs/#dom-audioencoder-close")]
    public void Close()
    {
        _engine.ThrowIfClosed();
        _engine.Close(WebCodecsConfigs.CreateAbortError());
    }

    /// <summary>Returns whether the codecs support a configuration (https://w3c.github.io/webcodecs/#dom-audioencoder-isconfigsupported).</summary>
    /// <param name="config">The configuration.</param>
    /// <returns>A task that completes with the support and the configuration with the recognized members, or fails with a <see cref="TypeErrorException"/> for an invalid configuration.</returns>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoder-isconfigsupported"/>
    [IdlMember("isConfigSupported", "https://w3c.github.io/webcodecs/#dom-audioencoder-isconfigsupported")]
    public static Task<AudioEncoderSupport> IsConfigSupported(AudioEncoderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!WebCodecsConfigs.IsValid(config))
            return Task.FromException<AudioEncoderSupport>(new TypeErrorException("The configuration is invalid"));

        var clone = WebCodecsConfigs.Clone(config);
        var supported = WebCodecsConfigs.CheckSupport(backend => backend.IsConfigSupported(clone));
        return WebCodecsConfigs.Resolve(new AudioEncoderSupport { Supported = supported, Config = clone });
    }

    private void Output(IReadOnlyList<MediaEncodedAudio> outputs)
    {
        var generation = _engine.Generation;
        foreach (var output in outputs)
        {
            if (generation != _engine.Generation)
                return;

            // https://w3c.github.io/webcodecs/#output-encodedaudiochunks: the decoder configuration of the first chunk and of the chunks after a change
            var metadata = new EncodedAudioChunkMetadata();
            var outputConfig = output.DecoderConfig ?? (_activeOutputConfig is null && _activeConfig is { } config ? new AudioDecoderConfig { Codec = config.Codec, SampleRate = config.SampleRate, NumberOfChannels = config.NumberOfChannels } : null);
            if (outputConfig is not null && (_activeOutputConfig is null || !WebCodecsConfigs.AreEqual(outputConfig, _activeOutputConfig)))
            {
                _activeOutputConfig = outputConfig;
                metadata = new EncodedAudioChunkMetadata { DecoderConfig = WebCodecsConfigs.Clone(outputConfig) };
            }

            _engine.InvokeCallback(() => _output(output.Chunk, metadata));
        }
    }
}

/// <summary>
/// Encodes video (https://w3c.github.io/webcodecs/#videoencoder-interface): the <see cref="VideoFrame"/> objects given to
/// <see cref="Encode"/> are encoded by the codecs of <see cref="BrowsingContextOptions.MediaCodecs"/>, and the
/// <see cref="EncodedVideoChunk"/> objects are given to the output callback with the configuration of their decoder.
/// </summary>
/// <seealso href="https://w3c.github.io/webcodecs/#videoencoder-interface"/>
[IdlInterface("VideoEncoder", "https://w3c.github.io/webcodecs/#videoencoder-interface")]
public sealed class VideoEncoder : EventTarget
{
    private readonly CodecEngine<MediaVideoEncoder> _engine;
    private readonly EncodedVideoChunkOutputCallback _output;
    private VideoEncoderConfig? _activeConfig;
    private VideoDecoderConfig? _activeOutputConfig;
    private (double Rotation, bool Flip)? _activeOrientation;

    /// <summary>Creates an encoder (https://w3c.github.io/webcodecs/#dom-videoencoder-videoencoder).</summary>
    /// <param name="init">The output and error callbacks.</param>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoder-videoencoder"/>
    [IdlMember("constructor", "https://w3c.github.io/webcodecs/#dom-videoencoder-videoencoder")]
    public VideoEncoder(VideoEncoderInit init)
    {
        ArgumentNullException.ThrowIfNull(init);
        _output = init.Output;
        _engine = new CodecEngine<MediaVideoEncoder>(this, init.Error, GlobalScope.Current);
    }

    /// <summary>Gets the state of the encoder.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoder-state"/>
    [IdlMember("state", "https://w3c.github.io/webcodecs/#dom-videoencoder-state")]
    public CodecState State => _engine.State;

    /// <summary>Gets the number of pending encode requests.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoder-encodequeuesize"/>
    [IdlMember("encodeQueueSize", "https://w3c.github.io/webcodecs/#dom-videoencoder-encodequeuesize")]
    public uint EncodeQueueSize => _engine.QueueSize;

    /// <summary>Gets or sets the event handler of the <c>dequeue</c> event (the queue size decreased).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoder-ondequeue"/>
    [IdlMember("ondequeue", "https://w3c.github.io/webcodecs/#dom-videoencoder-ondequeue")]
    public Action<Event>? OnDequeue
    {
        get => GetEventHandler("dequeue");
        set => SetEventHandler("dequeue", value);
    }

    /// <summary>Configures the encoder (https://w3c.github.io/webcodecs/#dom-videoencoder-configure): a configuration that the codecs do not support closes it with a <c>NotSupportedError</c>.</summary>
    /// <param name="config">The configuration.</param>
    /// <exception cref="TypeErrorException">The configuration is invalid.</exception>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the encoder is closed.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoder-configure"/>
    [IdlMember("configure", "https://w3c.github.io/webcodecs/#dom-videoencoder-configure")]
    public void Configure(VideoEncoderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!WebCodecsConfigs.IsValid(config))
            throw new TypeErrorException("The configuration is invalid");

        _engine.ThrowIfClosed();
        _activeOrientation = null;
        var clone = WebCodecsConfigs.Clone(config);
        var backend = _engine.Backend;
        _engine.Configure(
            () =>
            {
                var implementation = backend?.IsConfigSupported(clone) == true ? backend.CreateVideoEncoder(clone) : null;
                if (implementation is not null)
                {
                    _activeConfig = clone;
                    _activeOutputConfig = null;
                }

                return implementation;
            },
            implementation => Output(implementation.Flush()));
    }

    /// <summary>Encodes a frame (https://w3c.github.io/webcodecs/#dom-videoencoder-encode): a clone of the frame is encoded.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="options">The options of the encoding of the frame.</param>
    /// <exception cref="TypeErrorException">The frame is closed.</exception>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the encoder is not configured, a <c>DataError</c> when the orientation of the frame is not the one of the first frame.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoder-encode"/>
    [IdlMember("encode", "https://w3c.github.io/webcodecs/#dom-videoencoder-encode")]
    public void Encode(VideoFrame frame, VideoEncoderEncodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        options ??= new VideoEncoderEncodeOptions();
        if (frame.IsDetached)
            throw new TypeErrorException("The video frame is closed");

        _engine.ThrowIfNotConfigured();

        // The frames encoded after a configuration have the orientation of the first one
        var orientation = (frame.Rotation, frame.Flip);
        if (_activeOrientation is { } active && active != orientation)
            throw new DomException("The orientation of the frame is not the one of the previous frames", "DataError");

        _activeOrientation = orientation;
        var colorSpace = frame.ColorSpace.ToJson();
        var clone = frame.Clone();
        _engine.EnqueueWork(
            implementation =>
            {
                try
                {
                    Output(implementation.Encode(clone, options), orientation, colorSpace);
                }
                finally
                {
                    clone.Close();
                }
            },
            clone.Close);
    }

    /// <summary>Encodes the pending frames (https://w3c.github.io/webcodecs/#dom-videoencoder-flush): the promise resolves once the chunks are delivered.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoder-flush"/>
    [IdlMember("flush", "https://w3c.github.io/webcodecs/#dom-videoencoder-flush")]
    public Task Flush() => _engine.Flush(implementation => Output(implementation.Flush()));

    /// <summary>Discards the pending frames and the configuration (https://w3c.github.io/webcodecs/#dom-videoencoder-reset).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the encoder is closed.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoder-reset"/>
    [IdlMember("reset", "https://w3c.github.io/webcodecs/#dom-videoencoder-reset")]
    public void Reset()
    {
        _engine.Reset(WebCodecsConfigs.CreateAbortError());
        _activeOrientation = null;
    }

    /// <summary>Closes the encoder (https://w3c.github.io/webcodecs/#dom-videoencoder-close).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the encoder is closed.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoder-close"/>
    [IdlMember("close", "https://w3c.github.io/webcodecs/#dom-videoencoder-close")]
    public void Close()
    {
        _engine.ThrowIfClosed();
        _engine.Close(WebCodecsConfigs.CreateAbortError());
    }

    /// <summary>Returns whether the codecs support a configuration (https://w3c.github.io/webcodecs/#dom-videoencoder-isconfigsupported).</summary>
    /// <param name="config">The configuration.</param>
    /// <returns>A task that completes with the support and the configuration with the recognized members, or fails with a <see cref="TypeErrorException"/> for an invalid configuration.</returns>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoder-isconfigsupported"/>
    [IdlMember("isConfigSupported", "https://w3c.github.io/webcodecs/#dom-videoencoder-isconfigsupported")]
    public static Task<VideoEncoderSupport> IsConfigSupported(VideoEncoderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!WebCodecsConfigs.IsValid(config))
            return Task.FromException<VideoEncoderSupport>(new TypeErrorException("The configuration is invalid"));

        var clone = WebCodecsConfigs.Clone(config);
        var supported = WebCodecsConfigs.CheckSupport(backend => backend.IsConfigSupported(clone));
        return WebCodecsConfigs.Resolve(new VideoEncoderSupport { Supported = supported, Config = clone });
    }

    private void Output(IReadOnlyList<MediaEncodedVideo> outputs, (double Rotation, bool Flip)? orientation = null, VideoColorSpaceInit? colorSpace = null)
    {
        var generation = _engine.Generation;
        foreach (var output in outputs)
        {
            if (generation != _engine.Generation)
                return;

            // https://w3c.github.io/webcodecs/#output-encodedvideochunks: the decoder configuration of the first chunk and
            // of the chunks after a change, with the display size and the orientation of the frames
            var outputConfig = output.DecoderConfig;
            if (outputConfig is null && _activeOutputConfig is null && _activeConfig is { } config)
                outputConfig = new VideoDecoderConfig { Codec = config.Codec, CodedWidth = config.Width, CodedHeight = config.Height };

            VideoDecoderConfig? metadataConfig = null;
            if (outputConfig is not null && _activeConfig is { } activeConfig)
            {
                var (rotation, flip) = orientation ?? _activeOrientation ?? (0, false);
                outputConfig = new VideoDecoderConfig
                {
                    Codec = outputConfig.Codec,
                    Description = outputConfig.Description,
                    CodedWidth = outputConfig.CodedWidth ?? activeConfig.Width,
                    CodedHeight = outputConfig.CodedHeight ?? activeConfig.Height,
                    DisplayAspectWidth = outputConfig.DisplayAspectWidth ?? activeConfig.DisplayWidth ?? activeConfig.Width,
                    DisplayAspectHeight = outputConfig.DisplayAspectHeight ?? activeConfig.DisplayHeight ?? activeConfig.Height,
                    ColorSpace = outputConfig.ColorSpace ?? colorSpace ?? _activeOutputConfig?.ColorSpace,
                    HardwareAcceleration = activeConfig.HardwareAcceleration,
                    Rotation = rotation,
                    Flip = flip,
                };
                if (_activeOutputConfig is null || !WebCodecsConfigs.AreEqual(outputConfig, _activeOutputConfig))
                {
                    _activeOutputConfig = outputConfig;
                    metadataConfig = WebCodecsConfigs.Clone(outputConfig);
                }
            }

            var metadata = new EncodedVideoChunkMetadata
            {
                DecoderConfig = metadataConfig,
                Svc = output.TemporalLayerId is { } layer ? new SvcOutputMetadata { TemporalLayerId = layer } : null,
            };
            _engine.InvokeCallback(() => _output(output.Chunk, metadata));
        }
    }
}
