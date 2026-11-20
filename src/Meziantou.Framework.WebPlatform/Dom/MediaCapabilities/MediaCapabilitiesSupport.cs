using System.Globalization;
using Meziantou.Framework.WebPlatform.Media;
using Meziantou.Framework.WebPlatform.WebCodecs;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The validation of the configurations of Media Capabilities, and their support by the backends of a global object.</summary>
internal static class MediaCapabilitiesSupport
{
    /// <summary>The containers that allow several codecs: a content type of such a container needs a <c>codecs</c> parameter that gives a single codec.</summary>
    private static readonly string[] MultipleCodecContainers = ["webm", "mp4", "ogg", "x-matroska", "matroska", "quicktime", "3gpp", "3gpp2", "mp2t", "x-m4a", "x-m4v", "wav", "wave", "x-wav", "vnd.wave"];

    /// <summary>The audio types of a single codec: their content type has no parameter.</summary>
    private static readonly string[] SingleCodecAudioTypes = ["mpeg", "mp3", "x-mp3", "flac", "x-flac", "aac", "aacp", "x-aac"];

    /// <summary>The RTP payload formats of audio (https://www.iana.org/assignments/rtp-parameters/rtp-parameters.xhtml), in lowercase.</summary>
    private static readonly string[] RtpAudioTypes = ["opus", "pcmu", "pcma", "g722", "g729", "isac", "ilbc", "l16", "cn", "telephone-event", "red"];

    /// <summary>The RTP payload formats of video, in lowercase.</summary>
    private static readonly string[] RtpVideoTypes = ["vp8", "vp9", "h264", "h265", "av1", "rtx", "red", "ulpfec", "flexfec-03"];

    /// <summary>The codecs (the part before the first dot, in lowercase) whose kind is known though they are not in the WebCodecs codec registry.</summary>
    private static readonly string[] VideoCodecNames = ["vp8", "vp9", "vp09", "av1", "av01", "avc1", "avc3", "hvc1", "hev1", "dvh1", "dvhe", "dva1", "dvav", "theora", "mp4v", "h264", "h265"];

    private static readonly string[] AudioCodecNames = ["opus", "vorbis", "flac", "mp3", "mp4a", "ac-3", "ec-3", "alac", "pcm", "ulaw", "alaw", "mhm1", "mha1", "iamf"];

    /// <summary>The containers of <see cref="MediaRecorder"/>.</summary>
    private static readonly string[] RecordContainers = ["webm", "x-matroska", "matroska"];

    /// <summary>The number of channels of an audio configuration without <c>channels</c>.</summary>
    private const uint DefaultChannelCount = 2;

    /// <summary>The sample rate of an audio configuration without <c>samplerate</c>.</summary>
    private const uint DefaultSampleRate = 48000;

    /// <summary>https://w3c.github.io/media-capabilities/#valid-mediadecodingconfiguration</summary>
    public static bool IsValid(MediaDecodingConfiguration configuration)
    {
        if (configuration.KeySystemConfiguration is { } keySystem)
        {
            if (configuration.Type is not (MediaDecodingType.File or MediaDecodingType.MediaSource))
                return false;

            if ((keySystem.Audio is not null && configuration.Audio is null) || (keySystem.Video is not null && configuration.Video is null))
                return false;
        }

        return IsValid(configuration, configuration.Type, encodingType: null);
    }

    /// <summary>https://w3c.github.io/media-capabilities/#valid-mediaconfiguration</summary>
    public static bool IsValid(MediaEncodingConfiguration configuration) => IsValid(configuration, decodingType: null, configuration.Type);

    private static bool IsValid(MediaConfiguration configuration, MediaDecodingType? decodingType, MediaEncodingType? encodingType)
    {
        if (configuration.Audio is null && configuration.Video is null)
            return false;

        var webRtc = decodingType == MediaDecodingType.WebRtc || encodingType == MediaEncodingType.WebRtc;
        if (configuration.Audio is { } audio && !IsValidContentType(audio.ContentType, video: false, webRtc))
            return false;

        return configuration.Video is not { } video || IsValid(video, decodingType, encodingType, webRtc);
    }

    /// <summary>https://w3c.github.io/media-capabilities/#valid-video-configuration</summary>
    private static bool IsValid(VideoConfiguration video, MediaDecodingType? decodingType, MediaEncodingType? encodingType, bool webRtc)
    {
        if (!double.IsFinite(video.Framerate) || video.Framerate <= 0)
            return false;

        // The members that do not apply to the type
        var isPlayback = decodingType is MediaDecodingType.File or MediaDecodingType.MediaSource;
        if (!isPlayback && (video.HdrMetadataType is not null || video.ColorGamut is not null || video.TransferFunction is not null))
            return false;

        if (video.ScalabilityMode is not null && encodingType != MediaEncodingType.WebRtc)
            return false;

        if (video.SpatialScalability is not null && decodingType is null)
            return false;

        return IsValidContentType(video.ContentType, video: true, webRtc);
    }

    /// <summary>Parses a content type and checks its validity (https://w3c.github.io/media-capabilities/#check-mime-type-validity).</summary>
    public static bool IsValidContentType(string contentType, bool video, bool webRtc) => MediaContentType.Parse(contentType) is { } mimeType && IsValidContentType(mimeType, video, webRtc);

    /// <summary>
    /// https://w3c.github.io/media-capabilities/#check-mime-type-validity: the type is the kind of the track (or
    /// <c>application</c>), a type of a single codec has no parameter, a container has a single <c>codecs</c> parameter with
    /// a single codec of the kind of the track. For <c>webrtc</c>, the parameters of the RTP payload formats are the format
    /// parameters of the codec (e.g. <c>video/VP9; profile-id=2</c>), as in Chromium.
    /// </summary>
    public static bool IsValidContentType(MediaContentType mimeType, bool video, bool webRtc)
    {
        if (mimeType.Type != (video ? "video" : "audio") && mimeType.Type != "application")
            return false;

        if (webRtc && IsRtpType(mimeType, video))
            return true;

        if (!video && mimeType.Type == "audio" && Array.IndexOf(SingleCodecAudioTypes, mimeType.Subtype) >= 0)
            return mimeType.Parameters.Count == 0;

        if (mimeType.Parameters.Count == 0)
            return Array.IndexOf(MultipleCodecContainers, mimeType.Subtype) < 0;

        if (mimeType.Parameters is not [{ Key: "codecs", Value: var codecs }])
            return false;

        var list = SplitCodecs(codecs);
        if (list is not [var codec])
            return false;

        return GetCodecKind(codec) is not { } isVideoCodec || isVideoCodec == video;
    }

    /// <summary>Returns whether a configuration that is valid is supported by the backends of a global object.</summary>
    public static bool IsSupported(GlobalScope global, MediaDecodingConfiguration configuration)
    {
        // https://w3c.github.io/media-capabilities/#is-encrypted-decode-supported: no key system is supported
        if (configuration.KeySystemConfiguration is not null)
            return false;

        if (configuration.Video is { } video && !IsSupported(global, video, configuration.Type))
            return false;

        return configuration.Audio is not { } audio || IsSupported(global, audio, configuration.Type);
    }

    /// <summary>Returns whether a configuration that is valid is supported by the backends of a global object.</summary>
    public static bool IsSupported(GlobalScope global, MediaEncodingConfiguration configuration)
    {
        if (configuration.Video is { } video && !IsSupported(global, video, configuration.Type))
            return false;

        return configuration.Audio is not { } audio || IsSupported(global, audio, configuration.Type);
    }

    private static bool IsSupported(GlobalScope global, VideoConfiguration video, MediaDecodingType type)
    {
        var mimeType = MediaContentType.Parse(video.ContentType)!;
        if (type == MediaDecodingType.WebRtc)
        {
            if (global.Options.MediaCodecs is not { } codecs || GetRtpCodec(mimeType, isVideo: true) is not { } codec)
                return false;

            var hasSize = video.Width > 0 && video.Height > 0;
            var config = new VideoDecoderConfig { Codec = codec, CodedWidth = hasSize ? video.Width : null, CodedHeight = hasSize ? video.Height : null };
            return WebCodecsConfigs.IsValid(config) && codecs.IsConfigSupported(config);
        }

        // The rendering is in sRGB: the HDR metadata cannot be interpreted
        if (video.HdrMetadataType is not null)
            return false;

        var codecList = GetCodecs(mimeType);
        if (codecList is [var codecString] && !IsColorSpaceValid(codecString, video.ColorGamut, video.TransferFunction))
            return false;

        return IsPlaybackSupported(global, mimeType, codecList, type);
    }

    private static bool IsSupported(GlobalScope global, AudioConfiguration audio, MediaDecodingType type)
    {
        var mimeType = MediaContentType.Parse(audio.ContentType)!;
        if (type == MediaDecodingType.WebRtc)
        {
            if (global.Options.MediaCodecs is not { } codecs || GetRtpCodec(mimeType, isVideo: false) is not { } codec)
                return false;

            var (sampleRate, channels) = GetRtpAudioFormat(codec);
            var config = new AudioDecoderConfig { Codec = codec, SampleRate = sampleRate, NumberOfChannels = channels };
            return WebCodecsConfigs.IsValid(config) && codecs.IsConfigSupported(config);
        }

        return IsPlaybackSupported(global, mimeType, GetCodecs(mimeType), type);
    }

    private static bool IsSupported(GlobalScope global, VideoConfiguration video, MediaEncodingType type)
    {
        if (global.Options.MediaCodecs is not { } codecs)
            return false;

        var mimeType = MediaContentType.Parse(video.ContentType)!;
        string codec;
        if (type == MediaEncodingType.WebRtc)
        {
            if (GetRtpCodec(mimeType, isVideo: true) is not { } rtpCodec)
                return false;

            codec = rtpCodec;
        }
        else
        {
            // MediaRecorder records VP8 (the profile is ignored) in WebM or Matroska, without the alpha channel
            if (mimeType.Type != "video" || Array.IndexOf(RecordContainers, mimeType.Subtype) < 0 || GetCodecs(mimeType) is not [var name] || GetCodecName(name) != "vp8" || video.HasAlphaChannel == true)
                return false;

            codec = "vp8";
        }

        var config = new VideoEncoderConfig
        {
            Codec = codec,
            Width = video.Width,
            Height = video.Height,
            Bitrate = video.Bitrate > 0 ? video.Bitrate : null,
            Framerate = video.Framerate,
            ScalabilityMode = video.ScalabilityMode,
            Alpha = video.HasAlphaChannel == true ? AlphaOption.Keep : AlphaOption.Discard,
        };

        return WebCodecsConfigs.IsValid(config) && codecs.IsConfigSupported(config);
    }

    private static bool IsSupported(GlobalScope global, AudioConfiguration audio, MediaEncodingType type)
    {
        if (global.Options.MediaCodecs is not { } codecs)
            return false;

        var mimeType = MediaContentType.Parse(audio.ContentType)!;
        string codec;
        uint sampleRate;
        uint channels;
        if (type == MediaEncodingType.WebRtc)
        {
            if (GetRtpCodec(mimeType, isVideo: false) is not { } rtpCodec)
                return false;

            codec = rtpCodec;
            (sampleRate, channels) = GetRtpAudioFormat(codec);
        }
        else
        {
            // MediaRecorder records Opus in WebM or Matroska
            if (mimeType.Type != "audio" || Array.IndexOf(RecordContainers, mimeType.Subtype) < 0 || GetCodecs(mimeType) is not [var name] || GetCodecName(name) != "opus")
                return false;

            codec = "opus";
            sampleRate = DefaultSampleRate;
            channels = DefaultChannelCount;
        }

        var config = new AudioEncoderConfig
        {
            Codec = codec,
            SampleRate = audio.Samplerate ?? sampleRate,
            NumberOfChannels = ParseChannels(audio.Channels) ?? channels,
            Bitrate = audio.Bitrate is > 0 ? audio.Bitrate : null,
        };

        return WebCodecsConfigs.IsValid(config) && codecs.IsConfigSupported(config);
    }

    /// <summary>
    /// The support of <c>file</c> and <c>media-source</c> by the media backend: a container with codecs is <c>probably</c>
    /// played, a type of a single codec at least <c>maybe</c>; a <c>MediaSource</c> also needs a byte stream parser.
    /// </summary>
    private static bool IsPlaybackSupported(GlobalScope global, MediaContentType mimeType, string[] codecs, MediaDecodingType type)
    {
        if (global.Options.MediaBackend is not { } backend)
            return false;

        var support = backend.CanPlayType(mimeType.Essence, codecs);
        if (codecs.Length > 0 ? support != MediaTypeSupport.Probably : support == MediaTypeSupport.NotSupported)
            return false;

        if (type == MediaDecodingType.MediaSource)
        {
            using var parser = backend.CreateSegmentParser(mimeType.Essence, codecs);
            return parser is not null;
        }

        return true;
    }

    /// <summary>
    /// Whether the color gamut and the transfer function of a configuration match the color space of a VP9 or AV1 codec
    /// string (https://www.webmproject.org/vp9/mp4/#codecs-parameter-string, https://aomediacodec.github.io/av1-isobmff/#codecsparam):
    /// its color primaries and transfer characteristics (ISO/IEC 23091-2), BT.709 when the string does not give them.
    /// The other codecs accept any color space.
    /// </summary>
    internal static bool IsColorSpaceValid(string codec, ColorGamut? colorGamut, TransferFunction? transferFunction)
    {
        if ((colorGamut is null && transferFunction is null) || !MediaCodecString.TryParse(codec, out var parsed))
            return true;

        var parts = codec.Split('.');
        var (primariesIndex, transferIndex) = parsed.Codec switch
        {
            MediaCodec.Vp9 => (5, 6),
            MediaCodec.Av1 => (6, 7),
            _ => (-1, -1),
        };

        if (primariesIndex < 0)
            return true;

        var primaries = parts.Length > primariesIndex ? int.Parse(parts[primariesIndex], NumberStyles.None, CultureInfo.InvariantCulture) : 1;
        var transfer = parts.Length > transferIndex ? int.Parse(parts[transferIndex], NumberStyles.None, CultureInfo.InvariantCulture) : 1;
        var gamutMatches = colorGamut switch
        {
            null => true,
            ColorGamut.Srgb => primaries is 1 or 5 or 6,
            ColorGamut.P3 => primaries is 11 or 12,
            _ => primaries == 9,
        };

        var transferMatches = transferFunction switch
        {
            null => true,
            TransferFunction.Srgb => transfer is 1 or 6 or 13 or 14 or 15,
            TransferFunction.Pq => transfer == 16,
            _ => transfer == 18,
        };

        return gamutMatches && transferMatches;
    }

    /// <summary>
    /// Returns the WebCodecs codec string of an RTP payload format and its format parameters
    /// (https://www.iana.org/assignments/rtp-parameters/rtp-parameters.xhtml), or null when the type is not an RTP payload
    /// format with a codec (e.g. <c>video/webm</c>, <c>audio/telephone-event</c>).
    /// </summary>
    internal static string? GetRtpCodec(MediaContentType mimeType, bool isVideo)
    {
        if (mimeType.Type != (isVideo ? "video" : "audio"))
            return null;

        return mimeType.Subtype switch
        {
            "opus" when !isVideo => "opus",
            "pcmu" when !isVideo => "ulaw",
            "pcma" when !isVideo => "alaw",
            "vp8" when isVideo => "vp8",

            // RFC 9628: profile-id (0 to 3, 0 by default)
            "vp9" when isVideo => GetIntegerParameter(mimeType, "profile-id", 0) is var profile and >= 0 and <= 3
                ? string.Create(CultureInfo.InvariantCulture, $"vp09.{profile:00}.10.{(profile >= 2 ? 10 : 8):00}")
                : null,

            // RFC 6184: profile-level-id (3 bytes in hexadecimal, 42001f by default)
            "h264" when isVideo => mimeType.GetParameter("profile-level-id") switch
            {
                null => "avc1.42001f",
                { Length: 6 } profileLevelId when profileLevelId.All(char.IsAsciiHexDigit) => "avc1." + profileLevelId,
                _ => null,
            },

            // https://aomediacodec.github.io/av1-rtp-spec/: profile (0 by default), level-idx (5 by default), tier (0 by default)
            "av1" when isVideo => GetIntegerParameter(mimeType, "profile", 0) is var profile and >= 0 and <= 2 && GetIntegerParameter(mimeType, "level-idx", 5) is var level and (>= 0 and <= 23 or 31) && GetIntegerParameter(mimeType, "tier", 0) is var tier and (0 or 1)
                ? string.Create(CultureInfo.InvariantCulture, $"av01.{profile}.{level:00}{(tier == 1 ? 'H' : 'M')}.{(profile == 2 ? 10 : 8):00}")
                : null,

            // RFC 7798: profile-id (1 by default), level-id (93 by default), tier-flag (0 by default)
            "h265" when isVideo => GetIntegerParameter(mimeType, "profile-id", 1) is var profile and >= 1 and <= 11 && GetIntegerParameter(mimeType, "level-id", 93) is var level and >= 1 and <= 255 && GetIntegerParameter(mimeType, "tier-flag", 0) is var tier and (0 or 1)
                ? string.Create(CultureInfo.InvariantCulture, $"hev1.{profile}.6.{(tier == 1 ? 'H' : 'L')}{level}.B0")
                : null,

            _ => null,
        };
    }

    /// <summary>The sample rate and the number of channels of the RTP payload formats of audio.</summary>
    private static (uint SampleRate, uint Channels) GetRtpAudioFormat(string codec) => codec == "opus" ? (DefaultSampleRate, DefaultChannelCount) : (8000, 1);

    private static bool IsRtpType(MediaContentType mimeType, bool video) => mimeType.Type == (video ? "video" : "audio") && Array.IndexOf(video ? RtpVideoTypes : RtpAudioTypes, mimeType.Subtype) >= 0;

    /// <summary>The value of a decimal parameter, its default value when it is missing, or -1 when it is invalid.</summary>
    private static int GetIntegerParameter(MediaContentType mimeType, string name, int defaultValue)
    {
        if (mimeType.GetParameter(name) is not { } value)
            return defaultValue;

        return value.Length is > 0 and <= 3 && value.All(char.IsAsciiDigit) ? int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture) : -1;
    }

    /// <summary>The codecs of the <c>codecs</c> parameter, or none.</summary>
    private static string[] GetCodecs(MediaContentType mimeType) => mimeType.GetParameter("codecs") is { } codecs ? SplitCodecs(codecs) : [];

    private static string[] SplitCodecs(string codecs) => codecs.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The name of a codec: the part before the first dot, in lowercase (e.g. <c>vp09</c> for <c>vp09.00.10.08</c>).</summary>
    private static string GetCodecName(string codec)
    {
        var dot = codec.IndexOf('.', StringComparison.Ordinal);
        return (dot < 0 ? codec : codec[..dot]).ToLowerInvariant();
    }

    /// <summary>Whether a codec is a video codec (true), an audio codec (false), or unknown (null).</summary>
    private static bool? GetCodecKind(string codec)
    {
        if (MediaCodecString.TryParse(codec, out var parsed))
            return !parsed.IsAudio;

        var name = GetCodecName(codec);
        if (Array.IndexOf(VideoCodecNames, name) >= 0)
            return true;

        if (Array.IndexOf(AudioCodecNames, name) >= 0 || name.StartsWith("pcm-", StringComparison.Ordinal))
            return false;

        return null;
    }

    /// <summary>
    /// The number of channels of <c>channels</c>: a number of channels (<c>2</c>), or a layout whose decimal part is the
    /// number of low-frequency channels (<c>5.1</c> is 6 channels); null when it is missing or not understood.
    /// </summary>
    internal static uint? ParseChannels(string? channels)
    {
        if (channels is null)
            return null;

        var parts = channels.Trim().Split('.');
        if (parts.Length is 0 or > 2 || parts.Any(part => part.Length is 0 or > 2 || !part.All(char.IsAsciiDigit)))
            return null;

        var count = uint.Parse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture) + (parts.Length == 2 ? uint.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture) : 0);
        return count == 0 ? null : count;
    }
}
