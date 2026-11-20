using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebCodecs;

/// <summary>The state of a codec (https://w3c.github.io/webcodecs/#enumdef-codecstate).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#enumdef-codecstate"/>
[IdlInterface("CodecState", "https://w3c.github.io/webcodecs/#enumdef-codecstate")]
public enum CodecState
{
    /// <summary>The codec is not configured: it cannot decode or encode.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-codecstate-unconfigured"/>
    [IdlMember("unconfigured", "https://w3c.github.io/webcodecs/#dom-codecstate-unconfigured")]
    Unconfigured,

    /// <summary>The codec is configured: it decodes or encodes.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-codecstate-configured"/>
    [IdlMember("configured", "https://w3c.github.io/webcodecs/#dom-codecstate-configured")]
    Configured,

    /// <summary>The codec is closed: it cannot be used anymore.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-codecstate-closed"/>
    [IdlMember("closed", "https://w3c.github.io/webcodecs/#dom-codecstate-closed")]
    Closed,
}

/// <summary>The preference of a codec for a hardware implementation (https://w3c.github.io/webcodecs/#enumdef-hardwareacceleration).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#enumdef-hardwareacceleration"/>
[IdlInterface("HardwareAcceleration", "https://w3c.github.io/webcodecs/#enumdef-hardwareacceleration")]
public enum HardwareAcceleration
{
    /// <summary>The implementation is chosen by the user agent.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-hardwareacceleration-no-preference"/>
    [IdlMember("no-preference", "https://w3c.github.io/webcodecs/#dom-hardwareacceleration-no-preference")]
    NoPreference,

    /// <summary>A hardware implementation is preferred.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-hardwareacceleration-prefer-hardware"/>
    [IdlMember("prefer-hardware", "https://w3c.github.io/webcodecs/#dom-hardwareacceleration-prefer-hardware")]
    PreferHardware,

    /// <summary>A software implementation is preferred.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-hardwareacceleration-prefer-software"/>
    [IdlMember("prefer-software", "https://w3c.github.io/webcodecs/#dom-hardwareacceleration-prefer-software")]
    PreferSoftware,
}

/// <summary>The latency preferred by a video encoder (https://w3c.github.io/webcodecs/#enumdef-latencymode).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#enumdef-latencymode"/>
[IdlInterface("LatencyMode", "https://w3c.github.io/webcodecs/#enumdef-latencymode")]
public enum LatencyMode
{
    /// <summary>The quality is preferred to the latency.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-latencymode-quality"/>
    [IdlMember("quality", "https://w3c.github.io/webcodecs/#dom-latencymode-quality")]
    Quality,

    /// <summary>A low latency is preferred (e.g. a call).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-latencymode-realtime"/>
    [IdlMember("realtime", "https://w3c.github.io/webcodecs/#dom-latencymode-realtime")]
    Realtime,
}

/// <summary>How a video encoder controls its bitrate (https://w3c.github.io/webcodecs/#enumdef-videoencoderbitratemode).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#enumdef-videoencoderbitratemode"/>
[IdlInterface("VideoEncoderBitrateMode", "https://w3c.github.io/webcodecs/#enumdef-videoencoderbitratemode")]
public enum VideoEncoderBitrateMode
{
    /// <summary>A constant bitrate.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderbitratemode-constant"/>
    [IdlMember("constant", "https://w3c.github.io/webcodecs/#dom-videoencoderbitratemode-constant")]
    Constant,

    /// <summary>A variable bitrate, around the target bitrate.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderbitratemode-variable"/>
    [IdlMember("variable", "https://w3c.github.io/webcodecs/#dom-videoencoderbitratemode-variable")]
    Variable,

    /// <summary>The quantizer of each frame is given by the encode options.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderbitratemode-quantizer"/>
    [IdlMember("quantizer", "https://w3c.github.io/webcodecs/#dom-videoencoderbitratemode-quantizer")]
    Quantizer,
}

/// <summary>How an audio encoder controls its bitrate (https://w3c.github.io/mediacapture-record/#enumdef-bitratemode).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-record/#enumdef-bitratemode"/>
[IdlInterface("BitrateMode", "https://w3c.github.io/mediacapture-record/#enumdef-bitratemode")]
public enum BitrateMode
{
    /// <summary>A constant bitrate.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-bitratemode-constant"/>
    [IdlMember("constant", "https://w3c.github.io/mediacapture-record/#dom-bitratemode-constant")]
    Constant,

    /// <summary>A variable bitrate.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-bitratemode-variable"/>
    [IdlMember("variable", "https://w3c.github.io/mediacapture-record/#dom-bitratemode-variable")]
    Variable,
}

/// <summary>The bitstream format of the AAC encoder (https://w3c.github.io/webcodecs/aac_codec_registration.html#enumdef-aacbitstreamformat).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/aac_codec_registration.html#enumdef-aacbitstreamformat"/>
[IdlInterface("AacBitstreamFormat", "https://w3c.github.io/webcodecs/aac_codec_registration.html#enumdef-aacbitstreamformat")]
public enum AacBitstreamFormat
{
    /// <summary>Raw AAC frames, the description is the AudioSpecificConfig.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/aac_codec_registration.html#dom-aacbitstreamformat-aac"/>
    [IdlMember("aac", "https://w3c.github.io/webcodecs/aac_codec_registration.html#dom-aacbitstreamformat-aac")]
    Aac,

    /// <summary>ADTS frames.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/aac_codec_registration.html#dom-aacbitstreamformat-adts"/>
    [IdlMember("adts", "https://w3c.github.io/webcodecs/aac_codec_registration.html#dom-aacbitstreamformat-adts")]
    Adts,
}

/// <summary>The bitstream format of the AVC encoder (https://w3c.github.io/webcodecs/avc_codec_registration.html#enumdef-avcbitstreamformat).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/avc_codec_registration.html#enumdef-avcbitstreamformat"/>
[IdlInterface("AvcBitstreamFormat", "https://w3c.github.io/webcodecs/avc_codec_registration.html#enumdef-avcbitstreamformat")]
public enum AvcBitstreamFormat
{
    /// <summary>Annex B NAL units, with the parameter sets in the key chunks.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/avc_codec_registration.html#dom-avcbitstreamformat-annexb"/>
    [IdlMember("annexb", "https://w3c.github.io/webcodecs/avc_codec_registration.html#dom-avcbitstreamformat-annexb")]
    AnnexB,

    /// <summary>Length-prefixed NAL units, with the parameter sets in the description.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/avc_codec_registration.html#dom-avcbitstreamformat-avc"/>
    [IdlMember("avc", "https://w3c.github.io/webcodecs/avc_codec_registration.html#dom-avcbitstreamformat-avc")]
    Avc,
}

/// <summary>The bitstream format of the HEVC encoder (https://w3c.github.io/webcodecs/hevc_codec_registration.html#enumdef-hevcbitstreamformat).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/hevc_codec_registration.html#enumdef-hevcbitstreamformat"/>
[IdlInterface("HevcBitstreamFormat", "https://w3c.github.io/webcodecs/hevc_codec_registration.html#enumdef-hevcbitstreamformat")]
public enum HevcBitstreamFormat
{
    /// <summary>Annex B NAL units, with the parameter sets in the key chunks.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/hevc_codec_registration.html#dom-hevcbitstreamformat-annexb"/>
    [IdlMember("annexb", "https://w3c.github.io/webcodecs/hevc_codec_registration.html#dom-hevcbitstreamformat-annexb")]
    AnnexB,

    /// <summary>Length-prefixed NAL units, with the parameter sets in the description.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/hevc_codec_registration.html#dom-hevcbitstreamformat-hevc"/>
    [IdlMember("hevc", "https://w3c.github.io/webcodecs/hevc_codec_registration.html#dom-hevcbitstreamformat-hevc")]
    Hevc,
}

/// <summary>The bitstream format of the Opus encoder (https://w3c.github.io/webcodecs/opus_codec_registration.html#enumdef-opusbitstreamformat).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#enumdef-opusbitstreamformat"/>
[IdlInterface("OpusBitstreamFormat", "https://w3c.github.io/webcodecs/opus_codec_registration.html#enumdef-opusbitstreamformat")]
public enum OpusBitstreamFormat
{
    /// <summary>Opus packets.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusbitstreamformat-opus"/>
    [IdlMember("opus", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusbitstreamformat-opus")]
    Opus,

    /// <summary>Opus packets with an Ogg identification header in the description.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusbitstreamformat-ogg"/>
    [IdlMember("ogg", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusbitstreamformat-ogg")]
    Ogg,
}

/// <summary>The type of signal encoded by the Opus encoder (https://w3c.github.io/webcodecs/opus_codec_registration.html#enumdef-opussignal).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#enumdef-opussignal"/>
[IdlInterface("OpusSignal", "https://w3c.github.io/webcodecs/opus_codec_registration.html#enumdef-opussignal")]
public enum OpusSignal
{
    /// <summary>The type is detected.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opussignal-auto"/>
    [IdlMember("auto", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opussignal-auto")]
    Auto,

    /// <summary>Music.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opussignal-music"/>
    [IdlMember("music", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opussignal-music")]
    Music,

    /// <summary>Voice.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opussignal-voice"/>
    [IdlMember("voice", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opussignal-voice")]
    Voice,
}

/// <summary>The intended application of the Opus encoder (https://w3c.github.io/webcodecs/opus_codec_registration.html#enumdef-opusapplication).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#enumdef-opusapplication"/>
[IdlInterface("OpusApplication", "https://w3c.github.io/webcodecs/opus_codec_registration.html#enumdef-opusapplication")]
public enum OpusApplication
{
    /// <summary>Voice over IP.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusapplication-voip"/>
    [IdlMember("voip", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusapplication-voip")]
    Voip,

    /// <summary>Music or mixed content.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusapplication-audio"/>
    [IdlMember("audio", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusapplication-audio")]
    Audio,

    /// <summary>The lowest latency.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusapplication-lowdelay"/>
    [IdlMember("lowdelay", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusapplication-lowdelay")]
    LowDelay,
}

/// <summary>The configuration of an AudioDecoder (https://w3c.github.io/webcodecs/#dictdef-audiodecoderconfig).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-audiodecoderconfig"/>
[IdlInterface("AudioDecoderConfig", "https://w3c.github.io/webcodecs/#dictdef-audiodecoderconfig")]
public sealed class AudioDecoderConfig
{
    /// <summary>Gets the codec string (e.g. <c>opus</c>, <c>pcm-s16</c>, <c>mp4a.40.2</c>).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecoderconfig-codec"/>
    [IdlMember("codec", "https://w3c.github.io/webcodecs/#dom-audiodecoderconfig-codec")]
    public required string Codec { get; init; }

    /// <summary>Gets the sample rate in Hz.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecoderconfig-samplerate"/>
    [IdlMember("sampleRate", "https://w3c.github.io/webcodecs/#dom-audiodecoderconfig-samplerate")]
    public required uint SampleRate { get; init; }

    /// <summary>Gets the number of channels.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecoderconfig-numberofchannels"/>
    [IdlMember("numberOfChannels", "https://w3c.github.io/webcodecs/#dom-audiodecoderconfig-numberofchannels")]
    public required uint NumberOfChannels { get; init; }

    /// <summary>Gets the codec-specific bytes (e.g. the AudioSpecificConfig of AAC): a buffer source of the scripts, a <see cref="byte"/> array or a memory of bytes, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecoderconfig-description"/>
    [IdlMember("description", "https://w3c.github.io/webcodecs/#dom-audiodecoderconfig-description")]
    public object? Description { get; init; }
}

/// <summary>The configuration of a VideoDecoder (https://w3c.github.io/webcodecs/#dictdef-videodecoderconfig).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-videodecoderconfig"/>
[IdlInterface("VideoDecoderConfig", "https://w3c.github.io/webcodecs/#dictdef-videodecoderconfig")]
public sealed class VideoDecoderConfig
{
    /// <summary>Gets the codec string (e.g. <c>vp8</c>, <c>vp09.00.10.08</c>, <c>av01.0.04M.08</c>, <c>avc1.42001E</c>).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoderconfig-codec"/>
    [IdlMember("codec", "https://w3c.github.io/webcodecs/#dom-videodecoderconfig-codec")]
    public required string Codec { get; init; }

    /// <summary>Gets the codec-specific bytes (e.g. the avcC box of AVC): a buffer source of the scripts, a <see cref="byte"/> array or a memory of bytes, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoderconfig-description"/>
    [IdlMember("description", "https://w3c.github.io/webcodecs/#dom-videodecoderconfig-description")]
    public object? Description { get; init; }

    /// <summary>Gets the width of the frames in pixels, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoderconfig-codedwidth"/>
    [IdlMember("codedWidth", "https://w3c.github.io/webcodecs/#dom-videodecoderconfig-codedwidth")]
    public uint? CodedWidth { get; init; }

    /// <summary>Gets the height of the frames in pixels, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoderconfig-codedheight"/>
    [IdlMember("codedHeight", "https://w3c.github.io/webcodecs/#dom-videodecoderconfig-codedheight")]
    public uint? CodedHeight { get; init; }

    /// <summary>Gets the horizontal component of the display aspect ratio, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoderconfig-displayaspectwidth"/>
    [IdlMember("displayAspectWidth", "https://w3c.github.io/webcodecs/#dom-videodecoderconfig-displayaspectwidth")]
    public uint? DisplayAspectWidth { get; init; }

    /// <summary>Gets the vertical component of the display aspect ratio, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoderconfig-displayaspectheight"/>
    [IdlMember("displayAspectHeight", "https://w3c.github.io/webcodecs/#dom-videodecoderconfig-displayaspectheight")]
    public uint? DisplayAspectHeight { get; init; }

    /// <summary>Gets the color space of the frames (it overrides the one of the bitstream), or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoderconfig-colorspace"/>
    [IdlMember("colorSpace", "https://w3c.github.io/webcodecs/#dom-videodecoderconfig-colorspace")]
    public VideoColorSpaceInit? ColorSpace { get; init; }

    /// <summary>Gets the preference for a hardware implementation.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoderconfig-hardwareacceleration"/>
    [IdlMember("hardwareAcceleration", "https://w3c.github.io/webcodecs/#dom-videodecoderconfig-hardwareacceleration")]
    public HardwareAcceleration HardwareAcceleration { get; init; } = HardwareAcceleration.NoPreference;

    /// <summary>Gets whether the decoder should output the frames as soon as possible.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoderconfig-optimizeforlatency"/>
    [IdlMember("optimizeForLatency", "https://w3c.github.io/webcodecs/#dom-videodecoderconfig-optimizeforlatency")]
    public bool OptimizeForLatency { get; init; }

    /// <summary>Gets the rotation of the decoded frames in degrees.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoderconfig-rotation"/>
    [IdlMember("rotation", "https://w3c.github.io/webcodecs/#dom-videodecoderconfig-rotation")]
    public double Rotation { get; init; }

    /// <summary>Gets whether the decoded frames are flipped horizontally.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoderconfig-flip"/>
    [IdlMember("flip", "https://w3c.github.io/webcodecs/#dom-videodecoderconfig-flip")]
    public bool Flip { get; init; }
}

/// <summary>The configuration of an AudioEncoder (https://w3c.github.io/webcodecs/#dictdef-audioencoderconfig).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-audioencoderconfig"/>
[IdlInterface("AudioEncoderConfig", "https://w3c.github.io/webcodecs/#dictdef-audioencoderconfig")]
public sealed class AudioEncoderConfig
{
    /// <summary>Gets the codec string (e.g. <c>opus</c>, <c>mp4a.40.2</c>).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoderconfig-codec"/>
    [IdlMember("codec", "https://w3c.github.io/webcodecs/#dom-audioencoderconfig-codec")]
    public required string Codec { get; init; }

    /// <summary>Gets the sample rate of the audio in Hz.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoderconfig-samplerate"/>
    [IdlMember("sampleRate", "https://w3c.github.io/webcodecs/#dom-audioencoderconfig-samplerate")]
    public required uint SampleRate { get; init; }

    /// <summary>Gets the number of channels of the audio.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoderconfig-numberofchannels"/>
    [IdlMember("numberOfChannels", "https://w3c.github.io/webcodecs/#dom-audioencoderconfig-numberofchannels")]
    public required uint NumberOfChannels { get; init; }

    /// <summary>Gets the target bitrate in bits per second, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoderconfig-bitrate"/>
    [IdlMember("bitrate", "https://w3c.github.io/webcodecs/#dom-audioencoderconfig-bitrate")]
    public ulong? Bitrate { get; init; }

    /// <summary>Gets how the encoder controls its bitrate.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoderconfig-bitratemode"/>
    [IdlMember("bitrateMode", "https://w3c.github.io/webcodecs/#dom-audioencoderconfig-bitratemode")]
    public BitrateMode BitrateMode { get; init; } = BitrateMode.Variable;

    /// <summary>Gets the options of the Opus encoder, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoderconfig-opus"/>
    [IdlMember("opus", "https://w3c.github.io/webcodecs/#dom-audioencoderconfig-opus")]
    public OpusEncoderConfig? Opus { get; init; }

    /// <summary>Gets the options of the AAC encoder, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoderconfig-aac"/>
    [IdlMember("aac", "https://w3c.github.io/webcodecs/#dom-audioencoderconfig-aac")]
    public AacEncoderConfig? Aac { get; init; }

    /// <summary>Gets the options of the FLAC encoder, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoderconfig-flac"/>
    [IdlMember("flac", "https://w3c.github.io/webcodecs/#dom-audioencoderconfig-flac")]
    public FlacEncoderConfig? Flac { get; init; }
}

/// <summary>The configuration of a VideoEncoder (https://w3c.github.io/webcodecs/#dictdef-videoencoderconfig).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-videoencoderconfig"/>
[IdlInterface("VideoEncoderConfig", "https://w3c.github.io/webcodecs/#dictdef-videoencoderconfig")]
public sealed class VideoEncoderConfig
{
    /// <summary>Gets the codec string (e.g. <c>vp8</c>, <c>vp09.00.10.08</c>, <c>av01.0.04M.08</c>, <c>avc1.42001E</c>).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderconfig-codec"/>
    [IdlMember("codec", "https://w3c.github.io/webcodecs/#dom-videoencoderconfig-codec")]
    public required string Codec { get; init; }

    /// <summary>Gets the width of the encoded frames in pixels (the frames are scaled to it).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderconfig-width"/>
    [IdlMember("width", "https://w3c.github.io/webcodecs/#dom-videoencoderconfig-width")]
    public required uint Width { get; init; }

    /// <summary>Gets the height of the encoded frames in pixels (the frames are scaled to it).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderconfig-height"/>
    [IdlMember("height", "https://w3c.github.io/webcodecs/#dom-videoencoderconfig-height")]
    public required uint Height { get; init; }

    /// <summary>Gets the display width of the decoded frames, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderconfig-displaywidth"/>
    [IdlMember("displayWidth", "https://w3c.github.io/webcodecs/#dom-videoencoderconfig-displaywidth")]
    public uint? DisplayWidth { get; init; }

    /// <summary>Gets the display height of the decoded frames, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderconfig-displayheight"/>
    [IdlMember("displayHeight", "https://w3c.github.io/webcodecs/#dom-videoencoderconfig-displayheight")]
    public uint? DisplayHeight { get; init; }

    /// <summary>Gets the target bitrate in bits per second, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderconfig-bitrate"/>
    [IdlMember("bitrate", "https://w3c.github.io/webcodecs/#dom-videoencoderconfig-bitrate")]
    public ulong? Bitrate { get; init; }

    /// <summary>Gets the expected number of frames per second, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderconfig-framerate"/>
    [IdlMember("framerate", "https://w3c.github.io/webcodecs/#dom-videoencoderconfig-framerate")]
    public double? Framerate { get; init; }

    /// <summary>Gets the preference for a hardware implementation.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderconfig-hardwareacceleration"/>
    [IdlMember("hardwareAcceleration", "https://w3c.github.io/webcodecs/#dom-videoencoderconfig-hardwareacceleration")]
    public HardwareAcceleration HardwareAcceleration { get; init; } = HardwareAcceleration.NoPreference;

    /// <summary>Gets whether the alpha channel of the frames is encoded.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderconfig-alpha"/>
    [IdlMember("alpha", "https://w3c.github.io/webcodecs/#dom-videoencoderconfig-alpha")]
    public AlphaOption Alpha { get; init; } = AlphaOption.Discard;

    /// <summary>Gets the scalability mode (e.g. <c>L1T2</c>), or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderconfig-scalabilitymode"/>
    [IdlMember("scalabilityMode", "https://w3c.github.io/webcodecs/#dom-videoencoderconfig-scalabilitymode")]
    public string? ScalabilityMode { get; init; }

    /// <summary>Gets how the encoder controls its bitrate.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderconfig-bitratemode"/>
    [IdlMember("bitrateMode", "https://w3c.github.io/webcodecs/#dom-videoencoderconfig-bitratemode")]
    public VideoEncoderBitrateMode BitrateMode { get; init; } = VideoEncoderBitrateMode.Variable;

    /// <summary>Gets the latency preferred by the encoder.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderconfig-latencymode"/>
    [IdlMember("latencyMode", "https://w3c.github.io/webcodecs/#dom-videoencoderconfig-latencymode")]
    public LatencyMode LatencyMode { get; init; } = LatencyMode.Quality;

    /// <summary>Gets the hint of the type of content (e.g. <c>motion</c>, <c>detail</c>, <c>text</c>), or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderconfig-contenthint"/>
    [IdlMember("contentHint", "https://w3c.github.io/webcodecs/#dom-videoencoderconfig-contenthint")]
    public string? ContentHint { get; init; }

    /// <summary>Gets the options of the AVC encoder, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderconfig-avc"/>
    [IdlMember("avc", "https://w3c.github.io/webcodecs/#dom-videoencoderconfig-avc")]
    public AvcEncoderConfig? Avc { get; init; }

    /// <summary>Gets the options of the HEVC encoder, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderconfig-hevc"/>
    [IdlMember("hevc", "https://w3c.github.io/webcodecs/#dom-videoencoderconfig-hevc")]
    public HevcEncoderConfig? Hevc { get; init; }
}

/// <summary>The options of the encoding of a frame (https://w3c.github.io/webcodecs/#dictdef-videoencoderencodeoptions).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-videoencoderencodeoptions"/>
[IdlInterface("VideoEncoderEncodeOptions", "https://w3c.github.io/webcodecs/#dictdef-videoencoderencodeoptions")]
public sealed class VideoEncoderEncodeOptions
{
    /// <summary>Gets whether the frame must be encoded as a key frame.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderencodeoptions-keyframe"/>
    [IdlMember("keyFrame", "https://w3c.github.io/webcodecs/#dom-videoencoderencodeoptions-keyframe")]
    public bool KeyFrame { get; init; }

    /// <summary>Gets the options of the VP9 encoder, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderencodeoptions-vp9"/>
    [IdlMember("vp9", "https://w3c.github.io/webcodecs/#dom-videoencoderencodeoptions-vp9")]
    public VideoEncoderEncodeOptionsForVp9? Vp9 { get; init; }

    /// <summary>Gets the options of the AV1 encoder, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderencodeoptions-av1"/>
    [IdlMember("av1", "https://w3c.github.io/webcodecs/#dom-videoencoderencodeoptions-av1")]
    public VideoEncoderEncodeOptionsForAv1? Av1 { get; init; }

    /// <summary>Gets the options of the AVC encoder, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderencodeoptions-avc"/>
    [IdlMember("avc", "https://w3c.github.io/webcodecs/#dom-videoencoderencodeoptions-avc")]
    public VideoEncoderEncodeOptionsForAvc? Avc { get; init; }

    /// <summary>Gets the options of the HEVC encoder, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderencodeoptions-hevc"/>
    [IdlMember("hevc", "https://w3c.github.io/webcodecs/#dom-videoencoderencodeoptions-hevc")]
    public VideoEncoderEncodeOptionsForHevc? Hevc { get; init; }
}

/// <summary>The options of the VP9 encoder for a frame (https://w3c.github.io/webcodecs/vp9_codec_registration.html#dictdef-videoencoderencodeoptionsforvp9).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/vp9_codec_registration.html#dictdef-videoencoderencodeoptionsforvp9"/>
[IdlInterface("VideoEncoderEncodeOptionsForVp9", "https://w3c.github.io/webcodecs/vp9_codec_registration.html#dictdef-videoencoderencodeoptionsforvp9")]
public sealed class VideoEncoderEncodeOptionsForVp9
{
    /// <summary>Gets the quantizer of the frame when the bitrate mode is <c>quantizer</c>, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/vp9_codec_registration.html#dom-videoencoderencodeoptionsforvp9-quantizer"/>
    [IdlMember("quantizer", "https://w3c.github.io/webcodecs/vp9_codec_registration.html#dom-videoencoderencodeoptionsforvp9-quantizer")]
    public ushort? Quantizer { get; init; }
}

/// <summary>The options of the AV1 encoder for a frame (https://w3c.github.io/webcodecs/av1_codec_registration.html#dictdef-videoencoderencodeoptionsforav1).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/av1_codec_registration.html#dictdef-videoencoderencodeoptionsforav1"/>
[IdlInterface("VideoEncoderEncodeOptionsForAv1", "https://w3c.github.io/webcodecs/av1_codec_registration.html#dictdef-videoencoderencodeoptionsforav1")]
public sealed class VideoEncoderEncodeOptionsForAv1
{
    /// <summary>Gets the quantizer of the frame when the bitrate mode is <c>quantizer</c>, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/av1_codec_registration.html#dom-videoencoderencodeoptionsforav1-quantizer"/>
    [IdlMember("quantizer", "https://w3c.github.io/webcodecs/av1_codec_registration.html#dom-videoencoderencodeoptionsforav1-quantizer")]
    public ushort? Quantizer { get; init; }
}

/// <summary>The options of the AVC encoder for a frame (https://w3c.github.io/webcodecs/avc_codec_registration.html#dictdef-videoencoderencodeoptionsforavc).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/avc_codec_registration.html#dictdef-videoencoderencodeoptionsforavc"/>
[IdlInterface("VideoEncoderEncodeOptionsForAvc", "https://w3c.github.io/webcodecs/avc_codec_registration.html#dictdef-videoencoderencodeoptionsforavc")]
public sealed class VideoEncoderEncodeOptionsForAvc
{
    /// <summary>Gets the quantizer of the frame when the bitrate mode is <c>quantizer</c>, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/avc_codec_registration.html#dom-videoencoderencodeoptionsforavc-quantizer"/>
    [IdlMember("quantizer", "https://w3c.github.io/webcodecs/avc_codec_registration.html#dom-videoencoderencodeoptionsforavc-quantizer")]
    public ushort? Quantizer { get; init; }
}

/// <summary>The options of the HEVC encoder for a frame (https://w3c.github.io/webcodecs/hevc_codec_registration.html#dictdef-videoencoderencodeoptionsforhevc).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/hevc_codec_registration.html#dictdef-videoencoderencodeoptionsforhevc"/>
[IdlInterface("VideoEncoderEncodeOptionsForHevc", "https://w3c.github.io/webcodecs/hevc_codec_registration.html#dictdef-videoencoderencodeoptionsforhevc")]
public sealed class VideoEncoderEncodeOptionsForHevc
{
    /// <summary>Gets the quantizer of the frame when the bitrate mode is <c>quantizer</c>, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/hevc_codec_registration.html#dom-videoencoderencodeoptionsforhevc-quantizer"/>
    [IdlMember("quantizer", "https://w3c.github.io/webcodecs/hevc_codec_registration.html#dom-videoencoderencodeoptionsforhevc-quantizer")]
    public ushort? Quantizer { get; init; }
}

/// <summary>The options of the AAC encoder (https://w3c.github.io/webcodecs/aac_codec_registration.html#dictdef-aacencoderconfig).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/aac_codec_registration.html#dictdef-aacencoderconfig"/>
[IdlInterface("AacEncoderConfig", "https://w3c.github.io/webcodecs/aac_codec_registration.html#dictdef-aacencoderconfig")]
public sealed class AacEncoderConfig
{
    /// <summary>Gets the bitstream format.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/aac_codec_registration.html#dom-aacencoderconfig-format"/>
    [IdlMember("format", "https://w3c.github.io/webcodecs/aac_codec_registration.html#dom-aacencoderconfig-format")]
    public AacBitstreamFormat Format { get; init; } = AacBitstreamFormat.Aac;
}

/// <summary>The options of the AVC encoder (https://w3c.github.io/webcodecs/avc_codec_registration.html#dictdef-avcencoderconfig).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/avc_codec_registration.html#dictdef-avcencoderconfig"/>
[IdlInterface("AvcEncoderConfig", "https://w3c.github.io/webcodecs/avc_codec_registration.html#dictdef-avcencoderconfig")]
public sealed class AvcEncoderConfig
{
    /// <summary>Gets the bitstream format.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/avc_codec_registration.html#dom-avcencoderconfig-format"/>
    [IdlMember("format", "https://w3c.github.io/webcodecs/avc_codec_registration.html#dom-avcencoderconfig-format")]
    public AvcBitstreamFormat Format { get; init; } = AvcBitstreamFormat.Avc;
}

/// <summary>The options of the HEVC encoder (https://w3c.github.io/webcodecs/hevc_codec_registration.html#dictdef-hevcencoderconfig).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/hevc_codec_registration.html#dictdef-hevcencoderconfig"/>
[IdlInterface("HevcEncoderConfig", "https://w3c.github.io/webcodecs/hevc_codec_registration.html#dictdef-hevcencoderconfig")]
public sealed class HevcEncoderConfig
{
    /// <summary>Gets the bitstream format.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/hevc_codec_registration.html#dom-hevcencoderconfig-format"/>
    [IdlMember("format", "https://w3c.github.io/webcodecs/hevc_codec_registration.html#dom-hevcencoderconfig-format")]
    public HevcBitstreamFormat Format { get; init; } = HevcBitstreamFormat.Hevc;
}

/// <summary>The options of the FLAC encoder (https://w3c.github.io/webcodecs/flac_codec_registration.html#dictdef-flacencoderconfig).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/flac_codec_registration.html#dictdef-flacencoderconfig"/>
[IdlInterface("FlacEncoderConfig", "https://w3c.github.io/webcodecs/flac_codec_registration.html#dictdef-flacencoderconfig")]
public sealed class FlacEncoderConfig
{
    /// <summary>Gets the number of samples per block (0: chosen by the encoder).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/flac_codec_registration.html#dom-flacencoderconfig-blocksize"/>
    [IdlMember("blockSize", "https://w3c.github.io/webcodecs/flac_codec_registration.html#dom-flacencoderconfig-blocksize")]
    public uint BlockSize { get; init; }

    /// <summary>Gets the compression level.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/flac_codec_registration.html#dom-flacencoderconfig-compresslevel"/>
    [IdlMember("compressLevel", "https://w3c.github.io/webcodecs/flac_codec_registration.html#dom-flacencoderconfig-compresslevel")]
    public uint CompressLevel { get; init; } = 5;
}

/// <summary>The options of the Opus encoder (https://w3c.github.io/webcodecs/opus_codec_registration.html#dictdef-opusencoderconfig).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dictdef-opusencoderconfig"/>
[IdlInterface("OpusEncoderConfig", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dictdef-opusencoderconfig")]
public sealed class OpusEncoderConfig
{
    /// <summary>Gets the bitstream format.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusencoderconfig-format"/>
    [IdlMember("format", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusencoderconfig-format")]
    public OpusBitstreamFormat Format { get; init; } = OpusBitstreamFormat.Opus;

    /// <summary>Gets the type of signal.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusencoderconfig-signal"/>
    [IdlMember("signal", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusencoderconfig-signal")]
    public OpusSignal Signal { get; init; } = OpusSignal.Auto;

    /// <summary>Gets the intended application.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusencoderconfig-application"/>
    [IdlMember("application", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusencoderconfig-application")]
    public OpusApplication Application { get; init; } = OpusApplication.Audio;

    /// <summary>Gets the duration of a frame in microseconds.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusencoderconfig-frameduration"/>
    [IdlMember("frameDuration", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusencoderconfig-frameduration")]
    public ulong FrameDuration { get; init; } = 20000;

    /// <summary>Gets the computational complexity (0 to 10), or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusencoderconfig-complexity"/>
    [IdlMember("complexity", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusencoderconfig-complexity")]
    public uint? Complexity { get; init; }

    /// <summary>Gets the expected packet loss in percent.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusencoderconfig-packetlossperc"/>
    [IdlMember("packetlossperc", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusencoderconfig-packetlossperc")]
    public uint PacketLossPercentage { get; init; }

    /// <summary>Gets whether the in-band forward error correction is used.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusencoderconfig-useinbandfec"/>
    [IdlMember("useinbandfec", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusencoderconfig-useinbandfec")]
    public bool UseInBandFec { get; init; }

    /// <summary>Gets whether the discontinuous transmission is used.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusencoderconfig-usedtx"/>
    [IdlMember("usedtx", "https://w3c.github.io/webcodecs/opus_codec_registration.html#dom-opusencoderconfig-usedtx")]
    public bool UseDtx { get; init; }
}

/// <summary>The result of <see cref="AudioDecoder.IsConfigSupported"/> (https://w3c.github.io/webcodecs/#dictdef-audiodecodersupport).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-audiodecodersupport"/>
[IdlInterface("AudioDecoderSupport", "https://w3c.github.io/webcodecs/#dictdef-audiodecodersupport")]
public sealed class AudioDecoderSupport
{
    /// <summary>Gets whether the configuration is supported.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecodersupport-supported"/>
    [IdlMember("supported", "https://w3c.github.io/webcodecs/#dom-audiodecodersupport-supported")]
    public bool? Supported { get; init; }

    /// <summary>Gets the configuration, with only the members recognized by the user agent.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecodersupport-config"/>
    [IdlMember("config", "https://w3c.github.io/webcodecs/#dom-audiodecodersupport-config")]
    public AudioDecoderConfig? Config { get; init; }
}

/// <summary>The result of <see cref="VideoDecoder.IsConfigSupported"/> (https://w3c.github.io/webcodecs/#dictdef-videodecodersupport).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-videodecodersupport"/>
[IdlInterface("VideoDecoderSupport", "https://w3c.github.io/webcodecs/#dictdef-videodecodersupport")]
public sealed class VideoDecoderSupport
{
    /// <summary>Gets whether the configuration is supported.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecodersupport-supported"/>
    [IdlMember("supported", "https://w3c.github.io/webcodecs/#dom-videodecodersupport-supported")]
    public bool? Supported { get; init; }

    /// <summary>Gets the configuration, with only the members recognized by the user agent.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecodersupport-config"/>
    [IdlMember("config", "https://w3c.github.io/webcodecs/#dom-videodecodersupport-config")]
    public VideoDecoderConfig? Config { get; init; }
}

/// <summary>The result of <see cref="AudioEncoder.IsConfigSupported"/> (https://w3c.github.io/webcodecs/#dictdef-audioencodersupport).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-audioencodersupport"/>
[IdlInterface("AudioEncoderSupport", "https://w3c.github.io/webcodecs/#dictdef-audioencodersupport")]
public sealed class AudioEncoderSupport
{
    /// <summary>Gets whether the configuration is supported.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencodersupport-supported"/>
    [IdlMember("supported", "https://w3c.github.io/webcodecs/#dom-audioencodersupport-supported")]
    public bool? Supported { get; init; }

    /// <summary>Gets the configuration, with only the members recognized by the user agent.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencodersupport-config"/>
    [IdlMember("config", "https://w3c.github.io/webcodecs/#dom-audioencodersupport-config")]
    public AudioEncoderConfig? Config { get; init; }
}

/// <summary>The result of <see cref="VideoEncoder.IsConfigSupported"/> (https://w3c.github.io/webcodecs/#dictdef-videoencodersupport).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-videoencodersupport"/>
[IdlInterface("VideoEncoderSupport", "https://w3c.github.io/webcodecs/#dictdef-videoencodersupport")]
public sealed class VideoEncoderSupport
{
    /// <summary>Gets whether the configuration is supported.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencodersupport-supported"/>
    [IdlMember("supported", "https://w3c.github.io/webcodecs/#dom-videoencodersupport-supported")]
    public bool? Supported { get; init; }

    /// <summary>Gets the configuration, with only the members recognized by the user agent.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencodersupport-config"/>
    [IdlMember("config", "https://w3c.github.io/webcodecs/#dom-videoencodersupport-config")]
    public VideoEncoderConfig? Config { get; init; }
}

/// <summary>The scalability metadata of an encoded chunk (https://w3c.github.io/webcodecs/#dictdef-svcoutputmetadata).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-svcoutputmetadata"/>
[IdlInterface("SvcOutputMetadata", "https://w3c.github.io/webcodecs/#dictdef-svcoutputmetadata")]
public sealed class SvcOutputMetadata
{
    /// <summary>Gets the temporal layer of the chunk, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-svcoutputmetadata-temporallayerid"/>
    [IdlMember("temporalLayerId", "https://w3c.github.io/webcodecs/#dom-svcoutputmetadata-temporallayerid")]
    public uint? TemporalLayerId { get; init; }
}

/// <summary>The metadata of an encoded audio chunk (https://w3c.github.io/webcodecs/#dictdef-encodedaudiochunkmetadata).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-encodedaudiochunkmetadata"/>
[IdlInterface("EncodedAudioChunkMetadata", "https://w3c.github.io/webcodecs/#dictdef-encodedaudiochunkmetadata")]
public sealed class EncodedAudioChunkMetadata
{
    /// <summary>Gets the configuration of a decoder of the chunks, given with the first chunk and when it changes, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedaudiochunkmetadata-decoderconfig"/>
    [IdlMember("decoderConfig", "https://w3c.github.io/webcodecs/#dom-encodedaudiochunkmetadata-decoderconfig")]
    public AudioDecoderConfig? DecoderConfig { get; init; }
}

/// <summary>The metadata of an encoded video chunk (https://w3c.github.io/webcodecs/#dictdef-encodedvideochunkmetadata).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-encodedvideochunkmetadata"/>
[IdlInterface("EncodedVideoChunkMetadata", "https://w3c.github.io/webcodecs/#dictdef-encodedvideochunkmetadata")]
public sealed class EncodedVideoChunkMetadata
{
    /// <summary>Gets the configuration of a decoder of the chunks, given with the first chunk and when it changes, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedvideochunkmetadata-decoderconfig"/>
    [IdlMember("decoderConfig", "https://w3c.github.io/webcodecs/#dom-encodedvideochunkmetadata-decoderconfig")]
    public VideoDecoderConfig? DecoderConfig { get; init; }

    /// <summary>Gets the scalability metadata of the chunk, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedvideochunkmetadata-svc"/>
    [IdlMember("svc", "https://w3c.github.io/webcodecs/#dom-encodedvideochunkmetadata-svc")]
    public SvcOutputMetadata? Svc { get; init; }

    /// <summary>Gets the encoded alpha channel of the frame, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedvideochunkmetadata-alphasidedata"/>
    [IdlMember("alphaSideData", "https://w3c.github.io/webcodecs/#dom-encodedvideochunkmetadata-alphasidedata")]
    public object? AlphaSideData { get; init; }
}

/// <summary>The callbacks of an AudioDecoder (https://w3c.github.io/webcodecs/#dictdef-audiodecoderinit).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-audiodecoderinit"/>
[IdlInterface("AudioDecoderInit", "https://w3c.github.io/webcodecs/#dictdef-audiodecoderinit")]
public sealed class AudioDecoderInit
{
    /// <summary>Gets the callback that receives the decoded audio.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecoderinit-output"/>
    [IdlMember("output", "https://w3c.github.io/webcodecs/#dom-audiodecoderinit-output")]
    public required AudioDataOutputCallback Output { get; init; }

    /// <summary>Gets the callback that receives the error that closes the decoder.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audiodecoderinit-error"/>
    [IdlMember("error", "https://w3c.github.io/webcodecs/#dom-audiodecoderinit-error")]
    public required WebCodecsErrorCallback Error { get; init; }
}

/// <summary>The callbacks of a VideoDecoder (https://w3c.github.io/webcodecs/#dictdef-videodecoderinit).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-videodecoderinit"/>
[IdlInterface("VideoDecoderInit", "https://w3c.github.io/webcodecs/#dictdef-videodecoderinit")]
public sealed class VideoDecoderInit
{
    /// <summary>Gets the callback that receives the decoded frames.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoderinit-output"/>
    [IdlMember("output", "https://w3c.github.io/webcodecs/#dom-videodecoderinit-output")]
    public required VideoFrameOutputCallback Output { get; init; }

    /// <summary>Gets the callback that receives the error that closes the decoder.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videodecoderinit-error"/>
    [IdlMember("error", "https://w3c.github.io/webcodecs/#dom-videodecoderinit-error")]
    public required WebCodecsErrorCallback Error { get; init; }
}

/// <summary>The callbacks of an AudioEncoder (https://w3c.github.io/webcodecs/#dictdef-audioencoderinit).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-audioencoderinit"/>
[IdlInterface("AudioEncoderInit", "https://w3c.github.io/webcodecs/#dictdef-audioencoderinit")]
public sealed class AudioEncoderInit
{
    /// <summary>Gets the callback that receives the encoded chunks.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoderinit-output"/>
    [IdlMember("output", "https://w3c.github.io/webcodecs/#dom-audioencoderinit-output")]
    public required EncodedAudioChunkOutputCallback Output { get; init; }

    /// <summary>Gets the callback that receives the error that closes the encoder.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-audioencoderinit-error"/>
    [IdlMember("error", "https://w3c.github.io/webcodecs/#dom-audioencoderinit-error")]
    public required WebCodecsErrorCallback Error { get; init; }
}

/// <summary>The callbacks of a VideoEncoder (https://w3c.github.io/webcodecs/#dictdef-videoencoderinit).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-videoencoderinit"/>
[IdlInterface("VideoEncoderInit", "https://w3c.github.io/webcodecs/#dictdef-videoencoderinit")]
public sealed class VideoEncoderInit
{
    /// <summary>Gets the callback that receives the encoded chunks.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderinit-output"/>
    [IdlMember("output", "https://w3c.github.io/webcodecs/#dom-videoencoderinit-output")]
    public required EncodedVideoChunkOutputCallback Output { get; init; }

    /// <summary>Gets the callback that receives the error that closes the encoder.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoencoderinit-error"/>
    [IdlMember("error", "https://w3c.github.io/webcodecs/#dom-videoencoderinit-error")]
    public required WebCodecsErrorCallback Error { get; init; }
}

/// <summary>The callback that receives the audio decoded by an AudioDecoder (https://w3c.github.io/webcodecs/#callbackdef-audiodataoutputcallback).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#callbackdef-audiodataoutputcallback"/>
[IdlInterface("AudioDataOutputCallback", "https://w3c.github.io/webcodecs/#callbackdef-audiodataoutputcallback")]
public delegate void AudioDataOutputCallback(AudioData output);

/// <summary>The callback that receives the frames decoded by a VideoDecoder (https://w3c.github.io/webcodecs/#callbackdef-videoframeoutputcallback).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#callbackdef-videoframeoutputcallback"/>
[IdlInterface("VideoFrameOutputCallback", "https://w3c.github.io/webcodecs/#callbackdef-videoframeoutputcallback")]
public delegate void VideoFrameOutputCallback(VideoFrame output);

/// <summary>The callback that receives the chunks encoded by an AudioEncoder (https://w3c.github.io/webcodecs/#callbackdef-encodedaudiochunkoutputcallback).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#callbackdef-encodedaudiochunkoutputcallback"/>
[IdlInterface("EncodedAudioChunkOutputCallback", "https://w3c.github.io/webcodecs/#callbackdef-encodedaudiochunkoutputcallback")]
public delegate void EncodedAudioChunkOutputCallback(EncodedAudioChunk output, EncodedAudioChunkMetadata metadata);

/// <summary>The callback that receives the chunks encoded by a VideoEncoder (https://w3c.github.io/webcodecs/#callbackdef-encodedvideochunkoutputcallback).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#callbackdef-encodedvideochunkoutputcallback"/>
[IdlInterface("EncodedVideoChunkOutputCallback", "https://w3c.github.io/webcodecs/#callbackdef-encodedvideochunkoutputcallback")]
public delegate void EncodedVideoChunkOutputCallback(EncodedVideoChunk chunk, EncodedVideoChunkMetadata metadata);

/// <summary>The callback that receives the error that closes a codec (https://w3c.github.io/webcodecs/#callbackdef-webcodecserrorcallback).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#callbackdef-webcodecserrorcallback"/>
[IdlInterface("WebCodecsErrorCallback", "https://w3c.github.io/webcodecs/#callbackdef-webcodecserrorcallback")]
public delegate void WebCodecsErrorCallback(DomException error);
