using System.Globalization;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The content types of Media Capabilities (navigator.mediaCapabilities): the strict parser of the valid MIME type strings,
// the validity of the content types of the audio and video tracks, the RTP payload formats of WebRTC and the channels
public static partial class WebPlatformFuzzTargets
{
    /// <summary>The tokens of the media-content-type target.</summary>
    private static string[] MediaContentTypeTokens =>
    [
        "video/", "audio/", "application/", "webm", "mp4", "ogg", "mpeg", "x-matroska", "/", ";", "=", "\"", "\\", ",", " ", "\t", ".",
        "codecs", "codecs=\"", "vp8", "vp09.00.10.08", "vp09.00.10.08.00.09.16.09.00", "av01.0.04M.08", "av01.0.04M.10.0.112.09.16.09.0",
        "opus", "vorbis", "theora", "avc1.42001E", "mp4a.40.2", "VP8", "VP9", "H264", "H265", "AV1", "PCMU", "PCMA",
        "profile-id=", "profile-level-id=", "level-idx=", "tier=", "tier-flag=", "level-id=", "profile=", "5.1", "2",
    ];

    /// <summary>
    /// Parses a content type strictly: a parsed content type is also a MIME type of the lenient parser of MIME Sniffing
    /// with the same essence and parameters, its serialization parses to the same content type, and the validation of the
    /// tracks, the conversion of the RTP payload formats to codec strings and the color spaces of the codec strings do not
    /// throw (the codec strings of VP9, AV1 and HEVC are valid codec strings of the WebCodecs registry).
    /// </summary>
    public static void MediaContentTypeTarget(string input)
    {
        var channels = MediaCapabilitiesSupport.ParseChannels(input);
        FuzzAssert.True(channels is null or > 0, "The number of channels is 0");

        if (MediaContentType.Parse(input) is not { } contentType)
        {
            foreach (var (video, webRtc) in (ReadOnlySpan<(bool, bool)>)[(false, false), (false, true), (true, false), (true, true)])
            {
                FuzzAssert.False(MediaCapabilitiesSupport.IsValidContentType(input, video, webRtc), "A string that is not a content type is valid");
            }

            return;
        }

        FuzzAssert.Equal(contentType.Essence.ToLowerInvariant(), contentType.Essence, "The essence of the content type is not lowercase");
        var serialized = contentType.ToString();
        FuzzAssert.Equal(serialized, MediaContentType.Parse(serialized)?.ToString(), "The serialization of the content type parses to another content type");

        // A valid MIME type string is a MIME type of the lenient parser, with the first value of each parameter
        if (MimeType.Parse(input) is not { } mimeType)
        {
            FuzzAssert.True(false, "A content type is not a MIME type");
            return;
        }

        FuzzAssert.Equal(contentType.Essence, mimeType.Essence, "The essence differs from the one of the MIME type");
        foreach (var name in contentType.Parameters.Select(parameter => parameter.Key).Distinct(StringComparer.Ordinal))
        {
            FuzzAssert.Equal(contentType.Parameters.First(parameter => parameter.Key == name).Value, mimeType.GetParameter(name), $"The parameter '{name}' differs from the one of the MIME type");
        }

        foreach (var video in (ReadOnlySpan<bool>)[false, true])
        {
            _ = MediaCapabilitiesSupport.IsValidContentType(contentType, video, webRtc: false);
            _ = MediaCapabilitiesSupport.IsValidContentType(contentType, video, webRtc: true);
            if (MediaCapabilitiesSupport.GetRtpCodec(contentType, video) is { } codec)
            {
                FuzzAssert.True(contentType.Type == (video ? "video" : "audio"), "An RTP payload format of another kind has a codec");
                FuzzAssert.True(codec.StartsWith("avc1.", StringComparison.Ordinal) || MediaCodecString.TryParse(codec, out _), $"The codec string '{codec}' of an RTP payload format is not valid");
            }
        }

        if (contentType.GetParameter("codecs") is { } codecs)
        {
            foreach (var codec in codecs.Split(','))
            {
                foreach (var colorGamut in (ReadOnlySpan<ColorGamut?>)[null, ColorGamut.Srgb, ColorGamut.P3, ColorGamut.Rec2020])
                {
                    foreach (var transferFunction in (ReadOnlySpan<TransferFunction?>)[null, TransferFunction.Srgb, TransferFunction.Pq, TransferFunction.Hlg])
                    {
                        var valid = MediaCapabilitiesSupport.IsColorSpaceValid(codec.Trim(), colorGamut, transferFunction);
                        FuzzAssert.True(valid || (colorGamut, transferFunction) != (null, null), "A codec without color space is not valid");
                    }
                }
            }
        }
    }

    /// <summary>The content types of the unit tests and of the tests of WPT.</summary>
    private static IEnumerable<FuzzSeed> GetMediaContentTypeSeeds()
    {
        string[] seeds =
        [
            "video/webm; codecs=\"vp8\"",
            "video/webm; codecs=\"vp09.00.10.08\"",
            "video/webm; codecs=\"vp09.00.10.08.00.09.16.09.00\"",
            "video/webm; codecs=\"vp09.00.10.08, vp8\"",
            "video/webm; codecs=\"vp09.00.10.08\"; foo=\"bar\"",
            "video/webm; foo=\"bar\"",
            "video/webm;",
            "video/webm",
            "audio/webm; codecs=\"opus\"",
            "audio/webm; codecs=\"vorbis, opus\"",
            "audio/mpeg;",
            "audio/mp3",
            "application/ogg; codecs=vorbis",
            "application/ogg; codecs=theora",
            "video/mp4; codecs=\"av01.0.04M.10.0.112.09.16.09.0\"",
            "audio/matroska; codecs=\"pcm\"",
            "video/x-matroska; codecs=\"av02\"",
            "fgeoa",
            "audio/fgeoa",
            "video/VP9\"",
            "video/VP9; profile-level=\"0\"",
            "video/VP9; profile-id=2",
            "video/H264; level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42001f",
            "video/AV1; level-idx=5; profile=0; tier=0",
            "video/H265; profile-id=1; tier-flag=0; level-id=93",
            "audio/opus",
            "audio/PCMU",
            " video/webm ; codecs=\"vp8\" ",
            "video/webm; codecs=\"a\\\"b\"; foo=bar",
            " Video/WebM;CODECS=\"vp09.00.10.08, opus\" ",
            "video/webm; codecs=\"\"",
            "video/webm; codecs",
            "video/webm; codecs=",
            "video/webm; codecs=\"vp8",
            "video/webm; codecs=a b",
            "video /webm",
            "audio/mpeg; codecs=mp3",
            "audio/webm; codecs=\"vp09.00.10.08, opus\"",
            "5.1",
            "2",
        ];

        for (var i = 0; i < seeds.Length; i++)
        {
            yield return FuzzSeed.FromText("media-content-type-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }
}
