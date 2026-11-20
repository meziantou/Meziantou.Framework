namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The kind of playback of a decoding configuration (https://w3c.github.io/media-capabilities/#enumdef-mediadecodingtype).</summary>
/// <seealso href="https://w3c.github.io/media-capabilities/#enumdef-mediadecodingtype"/>
[IdlInterface("MediaDecodingType", "https://w3c.github.io/media-capabilities/#enumdef-mediadecodingtype")]
public enum MediaDecodingType
{
    /// <summary>The playback of a media resource by a media element (not a <c>MediaSource</c>).</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediadecodingtype-file"/>
    [IdlMember("file", "https://w3c.github.io/media-capabilities/#dom-mediadecodingtype-file")]
    File,

    /// <summary>The playback of a <c>MediaSource</c> (Media Source Extensions).</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediadecodingtype-media-source"/>
    [IdlMember("media-source", "https://w3c.github.io/media-capabilities/#dom-mediadecodingtype-media-source")]
    MediaSource,

    /// <summary>The media received by an <c>RTCPeerConnection</c>: the content types are RTP payload formats (e.g. <c>video/VP8</c>, <c>audio/opus</c>).</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediadecodingtype-webrtc"/>
    [IdlMember("webrtc", "https://w3c.github.io/media-capabilities/#dom-mediadecodingtype-webrtc")]
    WebRtc,
}

/// <summary>The kind of encoding of an encoding configuration (https://w3c.github.io/media-capabilities/#enumdef-mediaencodingtype).</summary>
/// <seealso href="https://w3c.github.io/media-capabilities/#enumdef-mediaencodingtype"/>
[IdlInterface("MediaEncodingType", "https://w3c.github.io/media-capabilities/#enumdef-mediaencodingtype")]
public enum MediaEncodingType
{
    /// <summary>The recording of media by <c>MediaRecorder</c>.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediaencodingtype-record"/>
    [IdlMember("record", "https://w3c.github.io/media-capabilities/#dom-mediaencodingtype-record")]
    Record,

    /// <summary>The media sent by an <c>RTCPeerConnection</c>: the content types are RTP payload formats (e.g. <c>video/VP8</c>, <c>audio/opus</c>).</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediaencodingtype-webrtc"/>
    [IdlMember("webrtc", "https://w3c.github.io/media-capabilities/#dom-mediaencodingtype-webrtc")]
    WebRtc,
}

/// <summary>The type of the HDR metadata of a video (https://w3c.github.io/media-capabilities/#enumdef-hdrmetadatatype).</summary>
/// <seealso href="https://w3c.github.io/media-capabilities/#enumdef-hdrmetadatatype"/>
[IdlInterface("HdrMetadataType", "https://w3c.github.io/media-capabilities/#enumdef-hdrmetadatatype")]
public enum HdrMetadataType
{
    /// <summary>The static metadata of SMPTE ST 2086.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-hdrmetadatatype-smptest2086"/>
    [IdlMember("smpteSt2086", "https://w3c.github.io/media-capabilities/#dom-hdrmetadatatype-smptest2086")]
    SmpteSt2086,

    /// <summary>The dynamic metadata of SMPTE ST 2094-10.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-hdrmetadatatype-smptest2094-10"/>
    [IdlMember("smpteSt2094-10", "https://w3c.github.io/media-capabilities/#dom-hdrmetadatatype-smptest2094-10")]
    SmpteSt2094Part10,

    /// <summary>The dynamic metadata of SMPTE ST 2094-40.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-hdrmetadatatype-smptest2094-40"/>
    [IdlMember("smpteSt2094-40", "https://w3c.github.io/media-capabilities/#dom-hdrmetadatatype-smptest2094-40")]
    SmpteSt2094Part40,
}

/// <summary>The color gamut of a video (https://w3c.github.io/media-capabilities/#enumdef-colorgamut).</summary>
/// <seealso href="https://w3c.github.io/media-capabilities/#enumdef-colorgamut"/>
[IdlInterface("ColorGamut", "https://w3c.github.io/media-capabilities/#enumdef-colorgamut")]
public enum ColorGamut
{
    /// <summary>The sRGB color gamut.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-colorgamut-srgb"/>
    [IdlMember("srgb", "https://w3c.github.io/media-capabilities/#dom-colorgamut-srgb")]
    Srgb,

    /// <summary>The DCI-P3 color gamut, which includes sRGB.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-colorgamut-p3"/>
    [IdlMember("p3", "https://w3c.github.io/media-capabilities/#dom-colorgamut-p3")]
    P3,

    /// <summary>The ITU-R BT.2020 color gamut, which includes DCI-P3.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-colorgamut-rec2020"/>
    [IdlMember("rec2020", "https://w3c.github.io/media-capabilities/#dom-colorgamut-rec2020")]
    Rec2020,
}

/// <summary>The transfer function of a video (https://w3c.github.io/media-capabilities/#enumdef-transferfunction).</summary>
/// <seealso href="https://w3c.github.io/media-capabilities/#enumdef-transferfunction"/>
[IdlInterface("TransferFunction", "https://w3c.github.io/media-capabilities/#enumdef-transferfunction")]
public enum TransferFunction
{
    /// <summary>The transfer function of sRGB.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-transferfunction-srgb"/>
    [IdlMember("srgb", "https://w3c.github.io/media-capabilities/#dom-transferfunction-srgb")]
    Srgb,

    /// <summary>The Perceptual Quantizer of SMPTE ST 2084.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-transferfunction-pq"/>
    [IdlMember("pq", "https://w3c.github.io/media-capabilities/#dom-transferfunction-pq")]
    Pq,

    /// <summary>The Hybrid Log-Gamma of ITU-R BT.2100.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-transferfunction-hlg"/>
    [IdlMember("hlg", "https://w3c.github.io/media-capabilities/#dom-transferfunction-hlg")]
    Hlg,
}

/// <summary>The tracks of a configuration of <see cref="MediaCapabilities"/> (https://w3c.github.io/media-capabilities/#dictdef-mediaconfiguration).</summary>
/// <remarks>A valid configuration has an audio track, a video track, or both.</remarks>
/// <seealso href="https://w3c.github.io/media-capabilities/#dictdef-mediaconfiguration"/>
[IdlInterface("MediaConfiguration", "https://w3c.github.io/media-capabilities/#dictdef-mediaconfiguration")]
public abstract class MediaConfiguration
{
    /// <summary>Gets the video track, or null.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediaconfiguration-video"/>
    [IdlMember("video", "https://w3c.github.io/media-capabilities/#dom-mediaconfiguration-video")]
    public VideoConfiguration? Video { get; init; }

    /// <summary>Gets the audio track, or null.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediaconfiguration-audio"/>
    [IdlMember("audio", "https://w3c.github.io/media-capabilities/#dom-mediaconfiguration-audio")]
    public AudioConfiguration? Audio { get; init; }
}

/// <summary>A configuration of <see cref="MediaCapabilities.DecodingInfo"/> (https://w3c.github.io/media-capabilities/#dictdef-mediadecodingconfiguration).</summary>
/// <seealso href="https://w3c.github.io/media-capabilities/#dictdef-mediadecodingconfiguration"/>
[IdlInterface("MediaDecodingConfiguration", "https://w3c.github.io/media-capabilities/#dictdef-mediadecodingconfiguration")]
public sealed class MediaDecodingConfiguration : MediaConfiguration
{
    /// <summary>Gets the kind of playback.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediadecodingconfiguration-type"/>
    [IdlMember("type", "https://w3c.github.io/media-capabilities/#dom-mediadecodingconfiguration-type")]
    public required MediaDecodingType Type { get; init; }

    /// <summary>Gets the key system of encrypted media, or null for media that is not encrypted.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediadecodingconfiguration-keysystemconfiguration"/>
    [IdlMember("keySystemConfiguration", "https://w3c.github.io/media-capabilities/#dom-mediadecodingconfiguration-keysystemconfiguration")]
    public MediaCapabilitiesKeySystemConfiguration? KeySystemConfiguration { get; init; }
}

/// <summary>A configuration of <see cref="MediaCapabilities.EncodingInfo"/> (https://w3c.github.io/media-capabilities/#dictdef-mediaencodingconfiguration).</summary>
/// <seealso href="https://w3c.github.io/media-capabilities/#dictdef-mediaencodingconfiguration"/>
[IdlInterface("MediaEncodingConfiguration", "https://w3c.github.io/media-capabilities/#dictdef-mediaencodingconfiguration")]
public sealed class MediaEncodingConfiguration : MediaConfiguration
{
    /// <summary>Gets the kind of encoding.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediaencodingconfiguration-type"/>
    [IdlMember("type", "https://w3c.github.io/media-capabilities/#dom-mediaencodingconfiguration-type")]
    public required MediaEncodingType Type { get; init; }
}

/// <summary>The video track of a configuration of <see cref="MediaCapabilities"/> (https://w3c.github.io/media-capabilities/#dictdef-videoconfiguration).</summary>
/// <seealso href="https://w3c.github.io/media-capabilities/#dictdef-videoconfiguration"/>
[IdlInterface("VideoConfiguration", "https://w3c.github.io/media-capabilities/#dictdef-videoconfiguration")]
public sealed class VideoConfiguration
{
    /// <summary>Gets the MIME type of the track: a container with the <c>codecs</c> parameter (e.g. <c>video/webm; codecs="vp8"</c>), or an RTP payload format for <c>webrtc</c> (e.g. <c>video/VP8</c>).</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-videoconfiguration-contenttype"/>
    [IdlMember("contentType", "https://w3c.github.io/media-capabilities/#dom-videoconfiguration-contenttype")]
    public required string ContentType { get; init; }

    /// <summary>Gets the width of the frames in pixels.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-videoconfiguration-width"/>
    [IdlMember("width", "https://w3c.github.io/media-capabilities/#dom-videoconfiguration-width")]
    public required uint Width { get; init; }

    /// <summary>Gets the height of the frames in pixels.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-videoconfiguration-height"/>
    [IdlMember("height", "https://w3c.github.io/media-capabilities/#dom-videoconfiguration-height")]
    public required uint Height { get; init; }

    /// <summary>Gets the bitrate in bits per second (the average of a constant bitrate, the maximum of a variable bitrate).</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-videoconfiguration-bitrate"/>
    [IdlMember("bitrate", "https://w3c.github.io/media-capabilities/#dom-videoconfiguration-bitrate")]
    public required ulong Bitrate { get; init; }

    /// <summary>Gets the number of frames per second: a finite positive number.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-videoconfiguration-framerate"/>
    [IdlMember("framerate", "https://w3c.github.io/media-capabilities/#dom-videoconfiguration-framerate")]
    public required double Framerate { get; init; }

    /// <summary>Gets whether the frames have an alpha channel, or null when it is not specified.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-videoconfiguration-hasalphachannel"/>
    [IdlMember("hasAlphaChannel", "https://w3c.github.io/media-capabilities/#dom-videoconfiguration-hasalphachannel")]
    public bool? HasAlphaChannel { get; init; }

    /// <summary>Gets the type of the HDR metadata of the track, or null. Only for the <c>file</c> and <c>media-source</c> decoding types.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-videoconfiguration-hdrmetadatatype"/>
    [IdlMember("hdrMetadataType", "https://w3c.github.io/media-capabilities/#dom-videoconfiguration-hdrmetadatatype")]
    public HdrMetadataType? HdrMetadataType { get; init; }

    /// <summary>Gets the color gamut of the track, or null. Only for the <c>file</c> and <c>media-source</c> decoding types.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-videoconfiguration-colorgamut"/>
    [IdlMember("colorGamut", "https://w3c.github.io/media-capabilities/#dom-videoconfiguration-colorgamut")]
    public ColorGamut? ColorGamut { get; init; }

    /// <summary>Gets the transfer function of the track, or null. Only for the <c>file</c> and <c>media-source</c> decoding types.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-videoconfiguration-transferfunction"/>
    [IdlMember("transferFunction", "https://w3c.github.io/media-capabilities/#dom-videoconfiguration-transferfunction")]
    public TransferFunction? TransferFunction { get; init; }

    /// <summary>Gets the scalability mode of WebRTC (e.g. <c>L1T2</c>), or null. Only for the <c>webrtc</c> encoding type.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-videoconfiguration-scalabilitymode"/>
    [IdlMember("scalabilityMode", "https://w3c.github.io/media-capabilities/#dom-videoconfiguration-scalabilitymode")]
    public string? ScalabilityMode { get; init; }

    /// <summary>Gets whether the decoder must decode the spatial scalability modes, or null. Only for the decoding types.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-videoconfiguration-spatialscalability"/>
    [IdlMember("spatialScalability", "https://w3c.github.io/media-capabilities/#dom-videoconfiguration-spatialscalability")]
    public bool? SpatialScalability { get; init; }
}

/// <summary>The audio track of a configuration of <see cref="MediaCapabilities"/> (https://w3c.github.io/media-capabilities/#dictdef-audioconfiguration).</summary>
/// <seealso href="https://w3c.github.io/media-capabilities/#dictdef-audioconfiguration"/>
[IdlInterface("AudioConfiguration", "https://w3c.github.io/media-capabilities/#dictdef-audioconfiguration")]
public sealed class AudioConfiguration
{
    /// <summary>Gets the MIME type of the track: a container with the <c>codecs</c> parameter (e.g. <c>audio/webm; codecs="opus"</c>), a type of a single codec (e.g. <c>audio/mpeg</c>), or an RTP payload format for <c>webrtc</c> (e.g. <c>audio/opus</c>).</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-audioconfiguration-contenttype"/>
    [IdlMember("contentType", "https://w3c.github.io/media-capabilities/#dom-audioconfiguration-contenttype")]
    public required string ContentType { get; init; }

    /// <summary>Gets the channels of the track (e.g. <c>2</c>, or <c>5.1</c> for 6 channels), or null.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-audioconfiguration-channels"/>
    [IdlMember("channels", "https://w3c.github.io/media-capabilities/#dom-audioconfiguration-channels")]
    public string? Channels { get; init; }

    /// <summary>Gets the average bitrate in bits per second, or null.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-audioconfiguration-bitrate"/>
    [IdlMember("bitrate", "https://w3c.github.io/media-capabilities/#dom-audioconfiguration-bitrate")]
    public ulong? Bitrate { get; init; }

    /// <summary>Gets the sample rate in Hz, or null.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-audioconfiguration-samplerate"/>
    [IdlMember("samplerate", "https://w3c.github.io/media-capabilities/#dom-audioconfiguration-samplerate")]
    public uint? Samplerate { get; init; }

    /// <summary>Gets whether the audio must be rendered spatially, or null.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-audioconfiguration-spatialrendering"/>
    [IdlMember("spatialRendering", "https://w3c.github.io/media-capabilities/#dom-audioconfiguration-spatialrendering")]
    public bool? SpatialRendering { get; init; }
}

/// <summary>The key system of encrypted media in a decoding configuration (Encrypted Media Extensions) (https://w3c.github.io/media-capabilities/#dictdef-mediacapabilitieskeysystemconfiguration).</summary>
/// <seealso href="https://w3c.github.io/media-capabilities/#dictdef-mediacapabilitieskeysystemconfiguration"/>
[IdlInterface("MediaCapabilitiesKeySystemConfiguration", "https://w3c.github.io/media-capabilities/#dictdef-mediacapabilitieskeysystemconfiguration")]
public sealed class MediaCapabilitiesKeySystemConfiguration
{
    /// <summary>Gets the name of the key system (e.g. <c>org.w3.clearkey</c>).</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediacapabilitieskeysystemconfiguration-keysystem"/>
    [IdlMember("keySystem", "https://w3c.github.io/media-capabilities/#dom-mediacapabilitieskeysystemconfiguration-keysystem")]
    public required string KeySystem { get; init; }

    /// <summary>Gets the initialization data type, or an empty string.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediacapabilitieskeysystemconfiguration-initdatatype"/>
    [IdlMember("initDataType", "https://w3c.github.io/media-capabilities/#dom-mediacapabilitieskeysystemconfiguration-initdatatype")]
    public string InitDataType { get; init; } = "";

    /// <summary>Gets the requirement of a distinctive identifier: a <c>MediaKeysRequirement</c> value (<c>required</c>, <c>optional</c> or <c>not-allowed</c>).</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediacapabilitieskeysystemconfiguration-distinctiveidentifier"/>
    [IdlMember("distinctiveIdentifier", "https://w3c.github.io/media-capabilities/#dom-mediacapabilitieskeysystemconfiguration-distinctiveidentifier")]
    public string DistinctiveIdentifier { get; init; } = "optional";

    /// <summary>Gets the requirement of a persistent state: a <c>MediaKeysRequirement</c> value (<c>required</c>, <c>optional</c> or <c>not-allowed</c>).</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediacapabilitieskeysystemconfiguration-persistentstate"/>
    [IdlMember("persistentState", "https://w3c.github.io/media-capabilities/#dom-mediacapabilitieskeysystemconfiguration-persistentstate")]
    public string PersistentState { get; init; } = "optional";

    /// <summary>Gets the required session types, or null.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediacapabilitieskeysystemconfiguration-sessiontypes"/>
    [IdlMember("sessionTypes", "https://w3c.github.io/media-capabilities/#dom-mediacapabilitieskeysystemconfiguration-sessiontypes")]
    public IReadOnlyList<string>? SessionTypes { get; init; }

    /// <summary>Gets the requirements of the audio track, or null.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediacapabilitieskeysystemconfiguration-audio"/>
    [IdlMember("audio", "https://w3c.github.io/media-capabilities/#dom-mediacapabilitieskeysystemconfiguration-audio")]
    public KeySystemTrackConfiguration? Audio { get; init; }

    /// <summary>Gets the requirements of the video track, or null.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediacapabilitieskeysystemconfiguration-video"/>
    [IdlMember("video", "https://w3c.github.io/media-capabilities/#dom-mediacapabilitieskeysystemconfiguration-video")]
    public KeySystemTrackConfiguration? Video { get; init; }
}

/// <summary>The requirements of a key system for a track (https://w3c.github.io/media-capabilities/#dictdef-keysystemtrackconfiguration).</summary>
/// <seealso href="https://w3c.github.io/media-capabilities/#dictdef-keysystemtrackconfiguration"/>
[IdlInterface("KeySystemTrackConfiguration", "https://w3c.github.io/media-capabilities/#dictdef-keysystemtrackconfiguration")]
public sealed class KeySystemTrackConfiguration
{
    /// <summary>Gets the robustness level, or an empty string.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-keysystemtrackconfiguration-robustness"/>
    [IdlMember("robustness", "https://w3c.github.io/media-capabilities/#dom-keysystemtrackconfiguration-robustness")]
    public string Robustness { get; init; } = "";

    /// <summary>Gets the encryption scheme (e.g. <c>cenc</c>, <c>cbcs</c>), or null.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-keysystemtrackconfiguration-encryptionscheme"/>
    [IdlMember("encryptionScheme", "https://w3c.github.io/media-capabilities/#dom-keysystemtrackconfiguration-encryptionscheme")]
    public string? EncryptionScheme { get; init; }
}

/// <summary>The answer of <see cref="MediaCapabilities"/> (https://w3c.github.io/media-capabilities/#dictdef-mediacapabilitiesinfo).</summary>
/// <seealso href="https://w3c.github.io/media-capabilities/#dictdef-mediacapabilitiesinfo"/>
[IdlInterface("MediaCapabilitiesInfo", "https://w3c.github.io/media-capabilities/#dictdef-mediacapabilitiesinfo")]
public abstract class MediaCapabilitiesInfo
{
    /// <summary>Gets whether the configuration is supported.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediacapabilitiesinfo-supported"/>
    [IdlMember("supported", "https://w3c.github.io/media-capabilities/#dom-mediacapabilitiesinfo-supported")]
    public bool Supported { get; init; }

    /// <summary>Gets whether the media is decoded or encoded at the frame rate of the configuration without dropping frames (false when the configuration is not supported).</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediacapabilitiesinfo-smooth"/>
    [IdlMember("smooth", "https://w3c.github.io/media-capabilities/#dom-mediacapabilitiesinfo-smooth")]
    public bool Smooth { get; init; }

    /// <summary>Gets whether the media is decoded or encoded in a power efficient manner (false when the configuration is not supported).</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediacapabilitiesinfo-powerefficient"/>
    [IdlMember("powerEfficient", "https://w3c.github.io/media-capabilities/#dom-mediacapabilitiesinfo-powerefficient")]
    public bool PowerEfficient { get; init; }
}

/// <summary>The answer of <see cref="MediaCapabilities.DecodingInfo"/> (https://w3c.github.io/media-capabilities/#dictdef-mediacapabilitiesdecodinginfo).</summary>
/// <seealso href="https://w3c.github.io/media-capabilities/#dictdef-mediacapabilitiesdecodinginfo"/>
[IdlInterface("MediaCapabilitiesDecodingInfo", "https://w3c.github.io/media-capabilities/#dictdef-mediacapabilitiesdecodinginfo")]
public sealed class MediaCapabilitiesDecodingInfo : MediaCapabilitiesInfo
{
    /// <summary>Gets the access to the key system of a supported encrypted configuration, or null. Encrypted Media Extensions are not implemented: it is always null.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediacapabilitiesdecodinginfo-keysystemaccess"/>
    [IdlMember("keySystemAccess", "https://w3c.github.io/media-capabilities/#dom-mediacapabilitiesdecodinginfo-keysystemaccess")]
    public object? KeySystemAccess { get; init; }

    /// <summary>Gets the configuration.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediacapabilitiesdecodinginfo-configuration"/>
    [IdlMember("configuration", "https://w3c.github.io/media-capabilities/#dom-mediacapabilitiesdecodinginfo-configuration")]
    public required MediaDecodingConfiguration Configuration { get; init; }
}

/// <summary>The answer of <see cref="MediaCapabilities.EncodingInfo"/> (https://w3c.github.io/media-capabilities/#dictdef-mediacapabilitiesencodinginfo).</summary>
/// <seealso href="https://w3c.github.io/media-capabilities/#dictdef-mediacapabilitiesencodinginfo"/>
[IdlInterface("MediaCapabilitiesEncodingInfo", "https://w3c.github.io/media-capabilities/#dictdef-mediacapabilitiesencodinginfo")]
public sealed class MediaCapabilitiesEncodingInfo : MediaCapabilitiesInfo
{
    /// <summary>Gets the configuration.</summary>
    /// <seealso href="https://w3c.github.io/media-capabilities/#dom-mediacapabilitiesencodinginfo-configuration"/>
    [IdlMember("configuration", "https://w3c.github.io/media-capabilities/#dom-mediacapabilitiesencodinginfo-configuration")]
    public required MediaEncodingConfiguration Configuration { get; init; }
}
