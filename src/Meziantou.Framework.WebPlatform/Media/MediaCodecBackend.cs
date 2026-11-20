using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.WebCodecs;

namespace Meziantou.Framework.WebPlatform.Media;

/// <summary>
/// The audio and video codecs plugged into the browsing contexts (<see cref="BrowsingContextOptions.MediaCodecs"/>): the
/// implementations of the decoders and the encoders of WebCodecs (https://w3c.github.io/webcodecs/), also used by
/// <c>MediaRecorder</c>. The backend tells which configurations it supports and creates the codec implementations; the
/// library implements the codec processing model on top of them (the states, the control message queue, the queue sizes
/// and the <c>dequeue</c> events, <c>flush()</c>, <c>reset()</c>, <c>close()</c>, the output and error callbacks, the
/// validation and the cloning of the configurations, the decoder configuration of the encoded chunks).
/// </summary>
/// <remarks>
/// <para>
/// Without a backend (the default), there is no codec: <c>isConfigSupported()</c> resolves with <c>supported: false</c>, and
/// <c>configure()</c> closes the codec with a <c>NotSupportedError</c>.
/// </para>
/// <para>
/// A backend can be shared by several browsing contexts and by their workers: its methods may be called from several
/// threads, each codec implementation being used by the thread of the event loop of its codec. The codec implementations
/// run in the tasks of that event loop, one operation at a time. <see cref="MediaCodecString"/> parses the codec strings of
/// the WebCodecs codec registry.
/// </para>
/// </remarks>
public abstract class MediaCodecBackend
{
    /// <summary>
    /// Returns whether the backend can decode the audio of a configuration
    /// (https://w3c.github.io/webcodecs/#check-configuration-support). The configuration is valid (its codec string is not
    /// empty); the codec string is case-sensitive and must not have leading or trailing spaces. The default implementation
    /// returns false.
    /// </summary>
    /// <param name="config">The configuration.</param>
    public virtual bool IsConfigSupported(AudioDecoderConfig config) => false;

    /// <summary>Returns whether the backend can decode the video of a configuration. The default implementation returns false.</summary>
    /// <param name="config">The configuration.</param>
    public virtual bool IsConfigSupported(VideoDecoderConfig config) => false;

    /// <summary>Returns whether the backend can encode audio with a configuration. The default implementation returns false.</summary>
    /// <param name="config">The configuration.</param>
    public virtual bool IsConfigSupported(AudioEncoderConfig config) => false;

    /// <summary>Returns whether the backend can encode video with a configuration. The default implementation returns false.</summary>
    /// <param name="config">The configuration.</param>
    public virtual bool IsConfigSupported(VideoEncoderConfig config) => false;

    /// <summary>
    /// Creates an audio decoder for a configuration that <see cref="IsConfigSupported(AudioDecoderConfig)"/> accepts, or
    /// returns null when it cannot (the codec is closed with a <c>NotSupportedError</c>). The default implementation returns null.
    /// </summary>
    /// <param name="config">The configuration.</param>
    public virtual MediaAudioDecoder? CreateAudioDecoder(AudioDecoderConfig config) => null;

    /// <summary>Creates a video decoder for a supported configuration, or returns null. The default implementation returns null.</summary>
    /// <param name="config">The configuration.</param>
    public virtual MediaVideoDecoder? CreateVideoDecoder(VideoDecoderConfig config) => null;

    /// <summary>Creates an audio encoder for a supported configuration, or returns null. The default implementation returns null.</summary>
    /// <param name="config">The configuration.</param>
    public virtual MediaAudioEncoder? CreateAudioEncoder(AudioEncoderConfig config) => null;

    /// <summary>Creates a video encoder for a supported configuration, or returns null. The default implementation returns null.</summary>
    /// <param name="config">The configuration.</param>
    public virtual MediaVideoEncoder? CreateVideoEncoder(VideoEncoderConfig config) => null;
}

/// <summary>
/// The base class of the codec implementations of a <see cref="MediaCodecBackend"/>. An operation that throws closes the
/// codec: a <see cref="Dom.DomException"/> is given to the error callback as is, another exception as an <c>EncodingError</c>.
/// </summary>
public abstract class MediaCodecImplementation : IDisposable
{
    /// <summary>
    /// Discards the pending outputs and the state of the previous inputs (<c>reset()</c>, or a key chunk is required after
    /// <c>flush()</c>). The default implementation does nothing.
    /// </summary>
    public virtual void Reset()
    {
    }

    /// <summary>Releases the codec.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the codec.</summary>
    /// <param name="disposing">Whether the method is called by <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
    }
}

/// <summary>An audio decoder created by <see cref="MediaCodecBackend.CreateAudioDecoder"/>.</summary>
public abstract class MediaAudioDecoder : MediaCodecImplementation
{
    /// <summary>
    /// Decodes a chunk and returns the audio that is ready (possibly none, when the decoder has a delay). The timestamps of
    /// the outputs are the ones of their chunks. The library closes the outputs that the codec cannot deliver (e.g. after a
    /// reset).
    /// </summary>
    /// <param name="chunk">The chunk.</param>
    public abstract IReadOnlyList<AudioData> Decode(EncodedAudioChunk chunk);

    /// <summary>Returns the pending outputs (<c>flush()</c>). The default implementation returns none.</summary>
    public virtual IReadOnlyList<AudioData> Flush() => [];
}

/// <summary>A video decoder created by <see cref="MediaCodecBackend.CreateVideoDecoder"/>.</summary>
public abstract class MediaVideoDecoder : MediaCodecImplementation
{
    /// <summary>
    /// Decodes a chunk and returns the frames that are ready (possibly none, when the decoder reorders the frames). The
    /// timestamp and the duration of the frames are the ones of their chunks. The library applies the rotation, the flip,
    /// the display aspect ratio and the color space of the configuration.
    /// </summary>
    /// <param name="chunk">The chunk.</param>
    public abstract IReadOnlyList<VideoFrame> Decode(EncodedVideoChunk chunk);

    /// <summary>Returns the pending frames (<c>flush()</c>). The default implementation returns none.</summary>
    public virtual IReadOnlyList<VideoFrame> Flush() => [];
}

/// <summary>An audio encoder created by <see cref="MediaCodecBackend.CreateAudioEncoder"/>.</summary>
public abstract class MediaAudioEncoder : MediaCodecImplementation
{
    /// <summary>Encodes audio and returns the chunks that are ready (possibly none, e.g. while a frame of the codec is incomplete). The library closes the data afterwards.</summary>
    /// <param name="data">The audio, with the sample rate and the number of channels of the configuration.</param>
    public abstract IReadOnlyList<MediaEncodedAudio> Encode(AudioData data);

    /// <summary>Encodes the pending audio and returns the remaining chunks (<c>flush()</c>). The default implementation returns none.</summary>
    public virtual IReadOnlyList<MediaEncodedAudio> Flush() => [];
}

/// <summary>A video encoder created by <see cref="MediaCodecBackend.CreateVideoEncoder"/>.</summary>
public abstract class MediaVideoEncoder : MediaCodecImplementation
{
    /// <summary>Encodes a frame and returns the chunks that are ready (possibly none). The library closes the frame afterwards.</summary>
    /// <param name="frame">The frame (a clone of the frame given to <c>encode()</c>).</param>
    /// <param name="options">The options of the encoding of the frame (e.g. a key frame is requested).</param>
    public abstract IReadOnlyList<MediaEncodedVideo> Encode(VideoFrame frame, VideoEncoderEncodeOptions options);

    /// <summary>Encodes the pending frames and returns the remaining chunks (<c>flush()</c>). The default implementation returns none.</summary>
    public virtual IReadOnlyList<MediaEncodedVideo> Flush() => [];
}

/// <summary>A chunk produced by a <see cref="MediaAudioEncoder"/>.</summary>
/// <param name="Chunk">The chunk.</param>
/// <param name="DecoderConfig">
/// The configuration of a decoder of the chunk (the codec string, the sample rate, the number of channels and the
/// description), or null when it did not change. The library gives it to the output callback with the first chunk after
/// <c>configure()</c> and when it changes.
/// </param>
public sealed record MediaEncodedAudio(EncodedAudioChunk Chunk, AudioDecoderConfig? DecoderConfig = null);

/// <summary>A chunk produced by a <see cref="MediaVideoEncoder"/>.</summary>
/// <param name="Chunk">The chunk.</param>
/// <param name="DecoderConfig">
/// The configuration of a decoder of the chunk (the codec string, the coded size, the description...), or null when it did
/// not change. The library gives it to the output callback with the first chunk after <c>configure()</c> and when it
/// changes, with the display aspect ratio, the color space and the orientation of the frames.
/// </param>
/// <param name="TemporalLayerId">The temporal layer of the chunk when the configuration has a scalability mode, or null.</param>
public sealed record MediaEncodedVideo(EncodedVideoChunk Chunk, VideoDecoderConfig? DecoderConfig = null, uint? TemporalLayerId = null);
