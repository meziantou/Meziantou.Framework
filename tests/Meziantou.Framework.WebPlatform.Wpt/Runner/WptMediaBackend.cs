using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The media backend of the tests (<see cref="Dom.BrowsingContextOptions.MediaBackend"/>): it decodes nothing. It recognizes
/// the media files of the test suite by the signature of their container (WebM, MP4, Ogg, WAV, MP3...), reads their duration,
/// their dimensions and their tracks (<see cref="WptMediaContainer"/>), plays them with the virtual clock of the test (the
/// default playback of <see cref="MediaPlayer"/>) and gives opaque black frames of the size of the video. The frames of the
/// videos of the test suite whose frames are all white (<c>/media/white.webm</c>, <c>/media/white.mp4</c>: the videos of the
/// reftests of the rendering of the cues, whose references have no video) are opaque white.
/// </summary>
internal sealed class WptMediaBackend : MediaBackend
{
    public static WptMediaBackend Instance { get; } = new();

    // The codecs of each container the "browser" of the tests plays, as the browsers do: the type of a container without
    // codecs is "maybe", a type whose codecs are all known is "probably", and a type with an unknown codec is not supported
    private static readonly string[] Mp4AudioCodecs = ["mp4a.", "opus", "flac", "iamf."];
    private static readonly string[] Mp4VideoCodecs = ["avc1.", "avc3.", "vp09.", "av01."];
    private static readonly string[] OggAudioCodecs = ["opus", "vorbis", "flac"];
    private static readonly string[] OggVideoCodecs = ["theora"];
    private static readonly string[] WebMAudioCodecs = ["opus", "vorbis"];
    private static readonly string[] WebMVideoCodecs = ["vp8", "vp8.0", "vp9", "vp9.0", "vp09.", "av01."];

    public override MediaTypeSupport CanPlayType(string mimeType, IReadOnlyList<string> codecs)
    {
        switch (mimeType)
        {
            case "audio/mp4" or "audio/x-m4a":
                return GetSupport(codecs, Mp4AudioCodecs, []);

            case "video/mp4" or "video/x-m4v":
                return GetSupport(codecs, Mp4AudioCodecs, Mp4VideoCodecs);

            case "audio/ogg":
                return GetSupport(codecs, OggAudioCodecs, []);

            case "video/ogg" or "application/ogg":
                return GetSupport(codecs, OggAudioCodecs, OggVideoCodecs);

            case "audio/webm":
                return GetSupport(codecs, WebMAudioCodecs, []);

            case "video/webm":
                return GetSupport(codecs, WebMAudioCodecs, WebMVideoCodecs);

            case "audio/wav" or "audio/wave" or "audio/x-wav" or "audio/vnd.wave":
                // The codec of a WAVE file is the format tag of RFC 2361 (1 is PCM)
                return GetSupport(codecs, ["1"], []);

            case "audio/mpeg" or "audio/mp3" or "audio/flac" or "audio/aac":
                // The types that do not allow the codecs parameter
                return codecs.Count == 0 ? MediaTypeSupport.Probably : GetSupport(codecs, ["mp3"], []);

            default:
                return MediaTypeSupport.NotSupported;
        }
    }

    private static MediaTypeSupport GetSupport(IReadOnlyList<string> codecs, string[] audioCodecs, string[] videoCodecs)
    {
        if (codecs.Count == 0)
            return MediaTypeSupport.Maybe;

        foreach (var codec in codecs)
        {
            if (!IsKnown(codec, audioCodecs) && !IsKnown(codec, videoCodecs))
                return MediaTypeSupport.NotSupported;
        }

        return MediaTypeSupport.Probably;

        // A codec is its exact name, or a name with parameters (e.g. "avc1.42E01E") when the known name ends with a dot
        static bool IsKnown(string codec, string[] known)
        {
            foreach (var name in known)
            {
                if (name.EndsWith('.', StringComparison.Ordinal) ? codec.StartsWith(name, StringComparison.OrdinalIgnoreCase) && codec.Length > name.Length : codec.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return name != "mp4a." || IsValidMp4AudioCodec(codec);
            }

            return false;
        }
    }

    /// <summary>
    /// The byte stream formats of the Media Source Extensions: ISO BMFF, WebM and MPEG audio (which does not allow the codecs
    /// parameter), with the codecs the "browser" of the tests plays.
    /// </summary>
    public override MediaSegmentParser? CreateSegmentParser(string mimeType, IReadOnlyList<string> codecs)
    {
        var container = mimeType switch
        {
            "audio/mp4" or "video/mp4" => "mp4",
            "audio/webm" or "video/webm" => "webm",
            "audio/mpeg" when codecs.Count == 0 => "mpeg",
            "audio/aac" when codecs.Count == 0 => "aac",
            _ => null,
        };

        if (container is null || CanPlayType(mimeType, codecs) == MediaTypeSupport.NotSupported)
            return null;

        // The codecs of the tracks of the initialization segments: the sample entry types of MP4, the codec IDs of WebM
        return WptMediaContainer.CreateSegmentParser(container, static codec => codec is "avc1" or "avc3" or "mp4a" or "vp09" or "av01" or "Opus" or "fLaC" or "iamf" or "V_VP8" or "V_VP9" or "V_AV1" or "A_VORBIS" or "A_OPUS");
    }

    /// <summary>
    /// The codec ID of the MPEG-4 audio (RFC 6381): <c>mp4a.</c>, the object type indication in hexadecimal, then the audio
    /// object type for MPEG-4 audio (<c>mp4a.40.2</c>: AAC-LC); the other object types (<c>mp4a.67</c>: MPEG-2 AAC-LC,
    /// <c>mp4a.69</c> and <c>mp4a.6B</c>: MP3) have no audio object type.
    /// </summary>
    private static bool IsValidMp4AudioCodec(string codec)
    {
        var parts = codec.Split('.');
        if (parts.Length < 2 || parts[1].Length != 2 || !parts[1].All(char.IsAsciiHexDigit))
            return false;

        if (parts[1] == "40")
            return parts.Length == 3 && parts[2].Length > 0 && parts[2].All(char.IsAsciiDigit);

        return parts.Length == 2;
    }

    public override MediaPlayer? CreatePlayer(MediaResource resource)
    {
        // The format is sniffed: the Content-Type of the response is ignored (e.g. the files served as text/plain)
        if (WptMediaContainer.Parse(resource.Content.Span) is not { } info || info.Tracks.Count == 0)
            return null;

        // An audio element plays the audio of a video file; a file whose tracks cannot be played at all is not supported
        return new WptMediaPlayer(resource.TimeProvider, info, resource.IsComplete, IsWhiteVideo(resource.Url));
    }

    /// <summary>Whether the resource is one of the videos of the test suite whose frames are all white.</summary>
    private static bool IsWhiteVideo(string url)
    {
        var end = url.AsSpan().IndexOfAny('?', '#');
        var path = end < 0 ? url.AsSpan() : url.AsSpan(0, end);
        return path.EndsWith("/media/white.webm", StringComparison.Ordinal) || path.EndsWith("/media/white.mp4", StringComparison.Ordinal);
    }

    /// <summary>The player of a media file of the test suite: the metadata of its container, played with the virtual clock.</summary>
    private sealed class WptMediaPlayer : MediaPlayer
    {
        private readonly MediaTrackInfo[] _audioTracks;
        private readonly MediaTrackInfo[] _videoTracks;
        private readonly bool _isWhite;
        private WptMediaInfo _info;
        private bool _isComplete;
        private MediaVideoFrame? _frame;

        public WptMediaPlayer(TimeProvider timeProvider, WptMediaInfo info, bool isComplete, bool isWhite)
            : base(timeProvider)
        {
            _info = info;
            _isComplete = isComplete;
            _isWhite = isWhite;

            // The first track of each type is the main track
            _audioTracks = [.. info.Tracks.Where(static track => !track.IsVideo).Select(static (track, index) => new MediaTrackInfo(track.Id, index == 0 ? "main" : "", track.Label, track.Language))];
            _videoTracks = [.. info.Tracks.Where(static track => track.IsVideo).Select(static (track, index) => new MediaTrackInfo(track.Id, index == 0 ? "main" : "", track.Label, track.Language))];
        }

        // A file whose duration cannot be read is a stream that is not known to be bounded
        public override double Duration => double.IsNaN(_info.Duration) ? double.PositiveInfinity : _info.Duration;

        public override int VideoWidth => GetVideoSize().Width;

        public override int VideoHeight => GetVideoSize().Height;

        public override IReadOnlyList<MediaTrackInfo> AudioTracks => _audioTracks;

        // Nothing is decoded: the player cannot tell which part of an incomplete resource can be played, so it only has the
        // metadata until the entire resource is received
        public override MediaReadyState ReadyState => _isComplete ? MediaReadyState.HaveEnoughData : MediaReadyState.HaveMetadata;

        public override IReadOnlyList<MediaTimeRange> Buffered => _isComplete ? base.Buffered : [];

        public override void OnDataReceived(ReadOnlyMemory<byte> content, bool isComplete)
        {
            if (!isComplete)
                return;

            // The duration read from the first bytes of some containers is the one of the data received so far
            _isComplete = true;
            if (WptMediaContainer.Parse(content.Span) is { } info)
            {
                _info = info;
            }
        }

        public override IReadOnlyList<MediaTrackInfo> VideoTracks => _videoTracks;

        /// <summary>The size of the frames of the video at the current playback position (the size of a video can change).</summary>
        private (int Width, int Height) GetVideoSize()
        {
            var sizes = _info.VideoSizes;
            if (sizes.Count == 0)
                return (0, 0);

            var time = CurrentTime;
            var size = sizes[0];
            for (var i = 1; i < sizes.Count && sizes[i].Time <= time; i++)
            {
                size = sizes[i];
            }

            return (size.Width, size.Height);
        }

        /// <summary>Nothing is decoded: the frames are opaque black (opaque white for the white videos of the test suite).</summary>
        public override MediaVideoFrame? GetVideoFrame()
        {
            var (width, height) = GetVideoSize();
            if (width <= 0 || height <= 0 || (long)width * height > 16 * 1024 * 1024)
                return null;

            if (_frame is null || _frame.Width != width || _frame.Height != height)
            {
                var pixels = new byte[width * height * 4];
                if (_isWhite)
                {
                    pixels.AsSpan().Fill(0xFF);
                }
                else
                {
                    for (var i = 3; i < pixels.Length; i += 4)
                    {
                        pixels[i] = 0xFF;
                    }
                }

                _frame = new MediaVideoFrame(width, height, pixels);
            }

            return _frame;
        }
    }
}
