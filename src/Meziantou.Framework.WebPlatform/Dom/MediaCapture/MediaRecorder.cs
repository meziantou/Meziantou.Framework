using System.Buffers.Binary;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Media;
using Meziantou.Framework.WebPlatform.WebCodecs;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The state of a <see cref="MediaRecorder"/> (https://w3c.github.io/mediacapture-record/#enumdef-recordingstate).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-record/#enumdef-recordingstate"/>
[IdlInterface("RecordingState", "https://w3c.github.io/mediacapture-record/#enumdef-recordingstate")]
public enum RecordingState
{
    /// <summary>The recorder is not recording.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-recordingstate-inactive"/>
    [IdlMember("inactive", "https://w3c.github.io/mediacapture-record/#dom-recordingstate-inactive")]
    Inactive,

    /// <summary>The recorder records the media of the stream.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-recordingstate-recording"/>
    [IdlMember("recording", "https://w3c.github.io/mediacapture-record/#dom-recordingstate-recording")]
    Recording,

    /// <summary>The recording is paused.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-recordingstate-paused"/>
    [IdlMember("paused", "https://w3c.github.io/mediacapture-record/#dom-recordingstate-paused")]
    Paused,
}

/// <summary>The options of a <see cref="MediaRecorder"/> (https://w3c.github.io/mediacapture-record/#dictdef-mediarecorderoptions).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-record/#dictdef-mediarecorderoptions"/>
[IdlInterface("MediaRecorderOptions", "https://w3c.github.io/mediacapture-record/#dictdef-mediarecorderoptions")]
public sealed class MediaRecorderOptions
{
    /// <summary>Gets the container and the codecs of the recording (e.g. <c>video/webm;codecs=vp8,opus</c>), or an empty string to let the recorder choose.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorderoptions-mimetype"/>
    [IdlMember("mimeType", "https://w3c.github.io/mediacapture-record/#dom-mediarecorderoptions-mimetype")]
    public string MimeType { get; init; } = "";

    /// <summary>Gets the target bitrate of the audio, in bits per second.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorderoptions-audiobitspersecond"/>
    [IdlMember("audioBitsPerSecond", "https://w3c.github.io/mediacapture-record/#dom-mediarecorderoptions-audiobitspersecond")]
    public uint? AudioBitsPerSecond { get; init; }

    /// <summary>Gets the target bitrate of the video, in bits per second.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorderoptions-videobitspersecond"/>
    [IdlMember("videoBitsPerSecond", "https://w3c.github.io/mediacapture-record/#dom-mediarecorderoptions-videobitspersecond")]
    public uint? VideoBitsPerSecond { get; init; }

    /// <summary>Gets the target bitrate of the audio and the video together, in bits per second (it overrides the other bitrates).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorderoptions-bitspersecond"/>
    [IdlMember("bitsPerSecond", "https://w3c.github.io/mediacapture-record/#dom-mediarecorderoptions-bitspersecond")]
    public uint? BitsPerSecond { get; init; }

    /// <summary>Gets how the audio encoder controls its bitrate.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorderoptions-audiobitratemode"/>
    [IdlMember("audioBitrateMode", "https://w3c.github.io/mediacapture-record/#dom-mediarecorderoptions-audiobitratemode")]
    public BitrateMode AudioBitrateMode { get; init; } = BitrateMode.Variable;

    /// <summary>Gets the largest duration between two video key frames, in milliseconds.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorderoptions-videokeyframeintervalduration"/>
    [IdlMember("videoKeyFrameIntervalDuration", "https://w3c.github.io/mediacapture-record/#dom-mediarecorderoptions-videokeyframeintervalduration")]
    public double? VideoKeyFrameIntervalDuration { get; init; }

    /// <summary>Gets the largest number of video frames between two key frames.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorderoptions-videokeyframeintervalcount"/>
    [IdlMember("videoKeyFrameIntervalCount", "https://w3c.github.io/mediacapture-record/#dom-mediarecorderoptions-videokeyframeintervalcount")]
    public uint? VideoKeyFrameIntervalCount { get; init; }
}

/// <summary>The initialization dictionary of a <see cref="BlobEvent"/> (https://w3c.github.io/mediacapture-record/#dictdef-blobeventinit).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-record/#dictdef-blobeventinit"/>
[IdlInterface("BlobEventInit", "https://w3c.github.io/mediacapture-record/#dictdef-blobeventinit")]
public sealed class BlobEventInit : EventInit
{
    /// <summary>Gets the recorded data.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-blobeventinit-data"/>
    [IdlMember("data", "https://w3c.github.io/mediacapture-record/#dom-blobeventinit-data")]
    public required Blob Data { get; init; }

    /// <summary>Gets the time of the first data of the blob, in milliseconds since the start of the recording.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-blobeventinit-timecode"/>
    [IdlMember("timecode", "https://w3c.github.io/mediacapture-record/#dom-blobeventinit-timecode")]
    public double Timecode { get; init; } = double.NaN;
}

/// <summary>The event that delivers the data of a <see cref="MediaRecorder"/> (https://w3c.github.io/mediacapture-record/#blobevent-section).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-record/#blobevent-section"/>
[IdlInterface("BlobEvent", "https://w3c.github.io/mediacapture-record/#blobevent-section")]
public sealed class BlobEvent : Event
{
    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary, with the data.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-blobevent-blobevent"/>
    [IdlMember("constructor", "https://w3c.github.io/mediacapture-record/#dom-blobevent-blobevent")]
    public BlobEvent(string type, BlobEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        ArgumentNullException.ThrowIfNull(eventInitDict);
        Data = eventInitDict.Data;
        Timecode = eventInitDict.Timecode;
    }

    /// <summary>Gets the recorded data.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-blobevent-data"/>
    [IdlMember("data", "https://w3c.github.io/mediacapture-record/#dom-blobevent-data")]
    public Blob Data { get; }

    /// <summary>Gets the time of the first data of the blob, in milliseconds since the start of the recording.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-blobevent-timecode"/>
    [IdlMember("timecode", "https://w3c.github.io/mediacapture-record/#dom-blobevent-timecode")]
    public double Timecode { get; }
}

/// <summary>
/// Records the tracks of a <see cref="MediaStream"/> (https://w3c.github.io/mediacapture-record/): the video and the audio
/// are encoded by the codecs of <see cref="BrowsingContextOptions.MediaCodecs"/> (VP8 and Opus) and muxed into a WebM
/// stream, delivered in <c>dataavailable</c> events. The video frames are sampled at the frame rate of the track (30 frames
/// per second by default), on the clock of the event loop.
/// </summary>
/// <seealso href="https://w3c.github.io/mediacapture-record/#mediarecorder-api"/>
[IdlInterface("MediaRecorder", "https://w3c.github.io/mediacapture-record/#mediarecorder-api")]
public sealed class MediaRecorder : EventTarget
{
    private const uint DefaultAudioBitsPerSecond = 128_000;
    private const uint DefaultVideoBitsPerSecond = 2_500_000;
    private const int VideoTrackNumber = 1;

    /// <summary>The containers (as the subtypes of their MIME types) and their Matroska document types.</summary>
    private static readonly string[] Containers = ["webm", "x-matroska", "matroska"];

    /// <summary>The codecs that the recorder knows: a known codec that cannot be recorded makes the type unsupported; an unknown codec is detected when the recording starts.</summary>
    private static readonly string[] KnownCodecs = ["vp8", "vp9", "av1", "av01", "avc1", "avc3", "hvc1", "hev1", "h264", "opus", "vorbis", "pcm", "mp4a", "flac", "mp3"];

    private readonly GlobalScope _global;
    private readonly Action _tracksChanged;
    private readonly string _constrainedMimeType;
    private readonly uint? _constrainedBitsPerSecond;
    private Recording? _recording;

    /// <summary>Creates a recorder (https://w3c.github.io/mediacapture-record/#dom-mediarecorder-mediarecorder).</summary>
    /// <param name="stream">The stream to record.</param>
    /// <param name="options">The type, the bitrates and the key frame interval of the recording.</param>
    /// <exception cref="DomException">A <c>NotSupportedError</c> when the type is not supported, the options give both key frame intervals, or the document is not fully active.</exception>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-mediarecorder"/>
    [IdlMember("constructor", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-mediarecorder")]
    public MediaRecorder(MediaStream stream, MediaRecorderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= new MediaRecorderOptions();
        _global = GlobalScope.Current ?? throw new DomException("The recorder has no global object", "NotSupportedError");
        if (_global is Window { Document.IsFullyActive: false })
            throw new DomException("The document is not fully active", "NotSupportedError");

        if (options.MimeType.Length > 0 && !IsTypeAccepted(_global, options.MimeType))
            throw new DomException($"The type '{options.MimeType}' is not supported", "NotSupportedError");

        if (options.VideoKeyFrameIntervalDuration is not null && options.VideoKeyFrameIntervalCount is not null)
            throw new DomException("Only one of the key frame intervals can be given", "NotSupportedError");

        Stream = stream;
        _constrainedMimeType = options.MimeType;
        MimeType = options.MimeType;
        AudioBitrateMode = options.AudioBitrateMode;
        KeyFrameIntervalDuration = options.VideoKeyFrameIntervalDuration;
        KeyFrameIntervalCount = options.VideoKeyFrameIntervalCount;

        // https://w3c.github.io/mediacapture-record/#bitrate-algorithm: a total bitrate is shared by the audio and the video
        _constrainedBitsPerSecond = options.BitsPerSecond;
        if (options.BitsPerSecond is { } total)
        {
            AudioBitsPerSecond = Math.Max(1, Math.Min(total / 10, DefaultAudioBitsPerSecond));
            VideoBitsPerSecond = (uint)Math.Max(1L, (long)total - AudioBitsPerSecond);
        }
        else
        {
            AudioBitsPerSecond = options.AudioBitsPerSecond ?? DefaultAudioBitsPerSecond;
            VideoBitsPerSecond = options.VideoBitsPerSecond ?? DefaultVideoBitsPerSecond;
        }

        SetRelevantGlobalCore(_global);
        _tracksChanged = OnTracksChanged;
    }

    /// <summary>Gets the recorded stream.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-stream"/>
    [IdlMember("stream", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-stream")]
    public MediaStream Stream { get; }

    /// <summary>Gets the type of the recording: the one of the options, or the one chosen when the recording starts.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-mimetype"/>
    [IdlMember("mimeType", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-mimetype")]
    public string MimeType { get; private set; }

    /// <summary>Gets the state of the recorder.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-state"/>
    [IdlMember("state", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-state")]
    public RecordingState State { get; private set; }

    /// <summary>Gets the target bitrate of the video, in bits per second.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-videobitspersecond"/>
    [IdlMember("videoBitsPerSecond", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-videobitspersecond")]
    public uint VideoBitsPerSecond { get; private set; }

    /// <summary>Gets the target bitrate of the audio, in bits per second.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-audiobitspersecond"/>
    [IdlMember("audioBitsPerSecond", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-audiobitspersecond")]
    public uint AudioBitsPerSecond { get; private set; }

    /// <summary>Gets how the audio encoder controls its bitrate.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-audiobitratemode"/>
    [IdlMember("audioBitrateMode", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-audiobitratemode")]
    public BitrateMode AudioBitrateMode { get; }

    private double? KeyFrameIntervalDuration { get; }

    private uint? KeyFrameIntervalCount { get; }

    /// <summary>Gets or sets the event handler of the <c>start</c> event.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-onstart"/>
    [IdlMember("onstart", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-onstart")]
    public Action<Event>? OnStart
    {
        get => GetEventHandler("start");
        set => SetEventHandler("start", value);
    }

    /// <summary>Gets or sets the event handler of the <c>stop</c> event.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-onstop"/>
    [IdlMember("onstop", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-onstop")]
    public Action<Event>? OnStop
    {
        get => GetEventHandler("stop");
        set => SetEventHandler("stop", value);
    }

    /// <summary>Gets or sets the event handler of the <c>dataavailable</c> event.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-ondataavailable"/>
    [IdlMember("ondataavailable", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-ondataavailable")]
    public Action<Event>? OnDataAvailable
    {
        get => GetEventHandler("dataavailable");
        set => SetEventHandler("dataavailable", value);
    }

    /// <summary>Gets or sets the event handler of the <c>pause</c> event.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-onpause"/>
    [IdlMember("onpause", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-onpause")]
    public Action<Event>? OnPause
    {
        get => GetEventHandler("pause");
        set => SetEventHandler("pause", value);
    }

    /// <summary>Gets or sets the event handler of the <c>resume</c> event.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-onresume"/>
    [IdlMember("onresume", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-onresume")]
    public Action<Event>? OnResume
    {
        get => GetEventHandler("resume");
        set => SetEventHandler("resume", value);
    }

    /// <summary>Gets or sets the event handler of the <c>error</c> event.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-onerror"/>
    [IdlMember("onerror", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-onerror")]
    public Action<Event>? OnError
    {
        get => GetEventHandler("error");
        set => SetEventHandler("error", value);
    }

    /// <summary>Starts the recording (https://w3c.github.io/mediacapture-record/#dom-mediarecorder-start).</summary>
    /// <param name="timeslice">The duration in milliseconds of the data of each <c>dataavailable</c> event, or null to deliver all the data when the recording stops.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the recorder is recording, a <c>NotSupportedError</c> when the stream is inactive, a <c>SecurityError</c> when a track is not origin-clean.</exception>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-start"/>
    [IdlMember("start", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-start")]
    public void Start(uint? timeslice = null)
    {
        if (State != RecordingState.Inactive)
            throw new DomException("The recorder is already recording", "InvalidStateError");

        if (_global is Window { Document.IsFullyActive: false })
            throw new DomException("The document is not fully active", "NotSupportedError");

        if (!Stream.Active)
            throw new DomException("The stream is inactive", "NotSupportedError");

        var tracks = Stream.GetTracks();
        var videoTrack = tracks.FirstOrDefault(track => track.Kind == "video");
        var audioTrack = tracks.FirstOrDefault(track => track.Kind == "audio");
        // An audio type cannot record the video tracks
        if (_constrainedMimeType.Length > 0 && Infra.MimeType.Parse(_constrainedMimeType) is { Type: "audio" } && videoTrack is not null)
            throw new DomException("An audio type cannot record a video track", "NotSupportedError");

        var (container, videoCodec, audioCodec, unknownCodec) = ResolveType(_constrainedMimeType, videoTrack is not null, audioTrack is not null);

        // https://w3c.github.io/mediacapture-record/#bitrate-algorithm: the total bitrate goes to the kinds of tracks of the stream
        if (_constrainedBitsPerSecond is { } total)
        {
            if (videoTrack is null)
                (AudioBitsPerSecond, VideoBitsPerSecond) = (total, 0);
            else if (audioTrack is null)
                (AudioBitsPerSecond, VideoBitsPerSecond) = (0, total);
        }

        State = RecordingState.Recording;
        var recording = new Recording(this, container, videoTrack, audioTrack, videoCodec, audioCodec, timeslice);
        _recording = recording;
        Stream.AddTracksChangedCallback(_tracksChanged);
        foreach (var track in tracks)
        {
            track.AddStateChangedCallback(recording.OnTrackStateChanged);
        }

        _global.QueueGlobalTask(() =>
        {
            if (_recording != recording)
                return;

            // A codec that the recorder does not know, or that the codecs cannot encode, fails the recording
            if (unknownCodec || !recording.TryCreateEncoders())
            {
                recording.Close();
                _recording = null;
                State = RecordingState.Inactive;
                FireError(new DomException("The type cannot be recorded", "NotSupportedError"));
                return;
            }

            if (_constrainedMimeType.Length == 0)
                MimeType = recording.MimeType;

            recording.Begin();
            FireEvent(new Event("start"));
        });
    }

    /// <summary>Stops the recording (https://w3c.github.io/mediacapture-record/#dom-mediarecorder-stop): the data is delivered, then a <c>stop</c> event is fired.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-stop"/>
    [IdlMember("stop", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-stop")]
    public void Stop()
    {
        if (State == RecordingState.Inactive || _recording is not { } recording)
            return;

        State = RecordingState.Inactive;
        recording.StopGathering();
        _global.QueueGlobalTask(() => StopRecording(recording, error: null));
    }

    /// <summary>Pauses the recording (https://w3c.github.io/mediacapture-record/#dom-mediarecorder-pause).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the recorder is inactive.</exception>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-pause"/>
    [IdlMember("pause", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-pause")]
    public void Pause()
    {
        if (State == RecordingState.Inactive)
            throw new DomException("The recorder is inactive", "InvalidStateError");

        if (State == RecordingState.Paused)
            return;

        State = RecordingState.Paused;
        _recording?.Pause();
        _global.QueueGlobalTask(() => FireEvent(new Event("pause")));
    }

    /// <summary>Resumes a paused recording (https://w3c.github.io/mediacapture-record/#dom-mediarecorder-resume).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the recorder is inactive.</exception>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-resume"/>
    [IdlMember("resume", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-resume")]
    public void Resume()
    {
        if (State == RecordingState.Inactive)
            throw new DomException("The recorder is inactive", "InvalidStateError");

        if (State == RecordingState.Recording)
            return;

        State = RecordingState.Recording;
        _recording?.Resume();
        _global.QueueGlobalTask(() => FireEvent(new Event("resume")));
    }

    /// <summary>Delivers the data recorded so far in a <c>dataavailable</c> event (https://w3c.github.io/mediacapture-record/#dom-mediarecorder-requestdata).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the recorder is inactive.</exception>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-requestdata"/>
    [IdlMember("requestData", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-requestdata")]
    public void RequestData()
    {
        if (State == RecordingState.Inactive || _recording is not { } recording)
            throw new DomException("The recorder is inactive", "InvalidStateError");

        // The data of a recording that started is not empty: it has the header of the stream at least
        var started = recording.HasBegun;
        _global.QueueGlobalTask(() =>
        {
            if (_recording != recording)
                return;

            if (started)
                recording.WriteHeader();

            FireDataAvailable(recording);
        });
    }

    /// <summary>Returns whether the recorder can record a type (https://w3c.github.io/mediacapture-record/#dom-mediarecorder-istypesupported).</summary>
    /// <param name="type">The MIME type (e.g. <c>video/webm;codecs=vp8,opus</c>), or an empty string.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-record/#dom-mediarecorder-istypesupported"/>
    [IdlMember("isTypeSupported", "https://w3c.github.io/mediacapture-record/#dom-mediarecorder-istypesupported")]
    public static bool IsTypeSupported(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.Length == 0)
            return true;

        return GlobalScope.Current is { } global && ParseType(type) is { Container: not null, Unknown: false } parsed && CanEncode(global, parsed);
    }

    /// <summary>Whether the constructor accepts a type: a supported type, or a type whose unknown codecs are checked when the recording starts.</summary>
    private static bool IsTypeAccepted(GlobalScope global, string type) => ParseType(type) is { Container: not null } parsed && (parsed.Unknown || CanEncode(global, parsed));

    private static bool CanEncode(GlobalScope global, ParsedType parsed)
    {
        // A type without codecs records the tracks with the default codecs: one of them must be available
        if (!parsed.Video && !parsed.Audio)
            return CanEncodeVideo(global) || CanEncodeAudio(global);

        if (parsed.Video && !CanEncodeVideo(global))
            return false;

        return !parsed.Audio || CanEncodeAudio(global);
    }

    private static bool CanEncodeVideo(GlobalScope global) => global.Options.MediaCodecs?.IsConfigSupported(new VideoEncoderConfig { Codec = "vp8", Width = 640, Height = 480 }) == true;

    private static bool CanEncodeAudio(GlobalScope global) => global.Options.MediaCodecs?.IsConfigSupported(new AudioEncoderConfig { Codec = "opus", SampleRate = 48000, NumberOfChannels = 2 }) == true;

    /// <summary>Parses a type: its container and its codecs (VP8 and Opus are recorded; the profiles of the codecs are ignored).</summary>
    private static ParsedType? ParseType(string type)
    {
        if (Infra.MimeType.Parse(type) is not { } mimeType || mimeType.Type is not ("audio" or "video"))
            return null;

        var container = Array.IndexOf(Containers, mimeType.Subtype) >= 0 ? mimeType.Subtype : null;
        var codecs = mimeType.GetParameter("codecs") is { } value ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
        var video = false;
        var audio = false;
        var unknown = false;
        foreach (var codec in codecs)
        {
            var name = codec.Split('.')[0].ToLowerInvariant();
            switch (name)
            {
                case "vp8" when mimeType.Type == "video":
                    video = true;
                    break;

                case "opus":
                    audio = true;
                    break;

                default:
                    if (Array.IndexOf(KnownCodecs, name) >= 0)
                        return new ParsedType(Container: null, Video: false, Audio: false, Unknown: false);

                    unknown = true;
                    break;
            }
        }

        return new ParsedType(container, video, audio, unknown);
    }

    /// <summary>Resolves the container and the codecs of a recording (the type of the options, or WebM with VP8 and Opus).</summary>
    private static (string Container, bool VideoCodec, bool AudioCodec, bool Unknown) ResolveType(string mimeType, bool hasVideo, bool hasAudio)
    {
        if (mimeType.Length > 0 && ParseType(mimeType) is { Container: { } container } parsed)
        {
            // A type without codecs records the tracks of the stream with the default codecs
            var hasCodecs = parsed.Video || parsed.Audio || parsed.Unknown;
            return (container, hasCodecs ? parsed.Video && hasVideo : hasVideo, hasCodecs ? parsed.Audio && hasAudio : hasAudio, parsed.Unknown);
        }

        return ("webm", hasVideo, hasAudio, false);
    }

    private void OnTracksChanged()
    {
        // https://w3c.github.io/mediacapture-record/#dom-mediarecorder-start: a track added to or removed from the stream stops
        // the recording (unless it is stopped before the task runs)
        if (State == RecordingState.Inactive || _recording is not { } recording)
            return;

        _global.QueueGlobalTask(() =>
        {
            if (State == RecordingState.Inactive || _recording != recording)
                return;

            State = RecordingState.Inactive;
            recording.StopGathering();
            StopRecording(recording, new DomException("A track was added to or removed from the stream", "InvalidModificationError"));
        });
    }

    /// <summary>All the tracks of the recording ended: the recording stops.</summary>
    private void OnAllTracksEnded(Recording recording)
    {
        if (_recording != recording || recording.IsStopping)
            return;

        recording.StopGathering();
        _global.QueueGlobalTask(() =>
        {
            State = RecordingState.Inactive;
            StopRecording(recording, error: null);
        });
    }

    /// <summary>The codecs failed: the recording stops with an <c>UnknownError</c>.</summary>
    private void OnEncodingError(Recording recording, Exception exception)
    {
        if (_recording != recording || recording.IsStopping)
            return;

        recording.StopGathering();
        State = RecordingState.Inactive;
        _global.QueueGlobalTask(() => StopRecording(recording, new DomException(exception.Message, "UnknownError")));
    }

    private void StopRecording(Recording recording, DomException? error)
    {
        if (_recording != recording)
            return;

        _recording = null;
        Stream.RemoveTracksChangedCallback(_tracksChanged);
        recording.Flush();
        if (error is not null)
            FireError(error);

        FireDataAvailable(recording);
        recording.Close();
        FireEvent(new Event("stop"));
    }

    private void FireDataAvailable(Recording recording)
    {
        var (bytes, timecode) = recording.TakeData();
        var blob = Blob.Create(bytes, MimeType.Length > 0 ? MimeType : recording.MimeType);
        FireEvent(new BlobEvent("dataavailable", new BlobEventInit { Data = blob, Timecode = timecode }));
    }

    private void FireError(DomException error) => FireEvent(new ErrorEvent("error", new ErrorEventInit { Error = error, Message = error.Message }));

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct ParsedType(string? Container, bool Video, bool Audio, bool Unknown);

    /// <summary>
    /// A recording: the encoders of the tracks, the WebM writer, and the sampling of the tracks on the clock of the event
    /// loop. The encoders run in the tasks of the event loop.
    /// </summary>
    private sealed class Recording
    {
        private const int AudioTrackNumber = 2;
        private const int DefaultFrameRate = 30;

        private readonly MediaRecorder _recorder;
        private readonly string _container;
        private readonly MediaStreamTrack? _videoTrack;
        private readonly MediaStreamTrack? _audioTrack;
        private readonly bool _recordVideo;
        private readonly bool _recordAudio;
        private readonly uint? _timeslice;
        private readonly TimeProvider _clock;
        private MediaVideoEncoder? _videoEncoder;
        private MediaAudioEncoder? _audioEncoder;
        private WebMWriter? _writer;
        private (int Width, int Height) _videoSize;
        private int _audioChannels;
        private int _audioSampleRate;
        private long _startTimestamp;
        private long _pausedTicks;
        private long? _pausedAt;
        private long _audioFramesRead;
        private long _videoFrameCount;
        private long _lastKeyFrameTime = long.MinValue;
        private long _nextSliceTime;
        private double? _blobTimecode;
        private int _timer;
        private long _sampleCount;
        private long _blobCount;
        private bool _gathering;

        public Recording(MediaRecorder recorder, string container, MediaStreamTrack? videoTrack, MediaStreamTrack? audioTrack, bool recordVideo, bool recordAudio, uint? timeslice)
        {
            _recorder = recorder;
            _container = container;
            _videoTrack = videoTrack;
            _audioTrack = audioTrack;
            _recordVideo = recordVideo && videoTrack is not null;
            _recordAudio = recordAudio && audioTrack is not null;
            _timeslice = timeslice;
            _clock = recorder._global.EventLoop.TimeProvider;
        }

        public bool IsStopping { get; private set; }

        /// <summary>Whether the recording started (its <c>start</c> event was fired).</summary>
        public bool HasBegun { get; private set; }

        public void WriteHeader() => _writer?.WriteHeader();

        /// <summary>The type of the recording, e.g. <c>video/webm;codecs=vp8,opus</c>.</summary>
        public string MimeType
        {
            get
            {
                var codecs = (_recordVideo, _recordAudio) switch
                {
                    (true, true) => "vp8,opus",
                    (true, false) => "vp8",
                    (false, true) => "opus",
                    _ => "",
                };
                var type = (_recordVideo ? "video/" : "audio/") + _container;
                return codecs.Length > 0 ? type + ";codecs=" + codecs : type;
            }
        }

        /// <summary>Creates the encoders and the writer: returns false when the codecs cannot encode the tracks.</summary>
        public bool TryCreateEncoders()
        {
            if (!_recordVideo && !_recordAudio)
                return false;

            var backend = _recorder._global.Options.MediaCodecs;
            if (backend is null)
                return false;

            var tracks = new List<WebMTrack>();
            if (_recordVideo)
            {
                var frame = _videoTrack!.GetVideoFrame();
                var settings = _videoTrack.Settings;
                var width = frame?.Width ?? (settings.GetValueOrDefault("width") is double w && w >= 1 ? (int)w : 640);
                var height = frame?.Height ?? (settings.GetValueOrDefault("height") is double h && h >= 1 ? (int)h : 480);
                _videoSize = (width, height);
                _videoEncoder = CreateVideoEncoder(backend, width, height);
                if (_videoEncoder is null)
                    return false;

                tracks.Add(new WebMTrack(VideoTrackNumber, IsVideo: true, "V_VP8", Width: width, Height: height));
            }

            if (_recordAudio)
            {
                _audioChannels = Math.Clamp(_audioTrack!.AudioChannelCount, 1, 2);
                _audioSampleRate = Math.Clamp(_audioTrack.Source.AudioSampleRate, 3000, 384000);
                var config = new AudioEncoderConfig { Codec = "opus", SampleRate = (uint)_audioSampleRate, NumberOfChannels = (uint)_audioChannels, Bitrate = Math.Clamp(_recorder.AudioBitsPerSecond, 6000u, 510000u), BitrateMode = _recorder.AudioBitrateMode };
                _audioEncoder = backend.IsConfigSupported(config) ? backend.CreateAudioEncoder(config) : null;
                if (_audioEncoder is null)
                    return false;

                // https://datatracker.ietf.org/doc/html/rfc7845#section-5.1: the identification header of the stream
                var head = new byte[19];
                "OpusHead"u8.CopyTo(head);
                head[8] = 1;
                head[9] = (byte)_audioChannels;
                BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(12), (uint)_audioSampleRate);
                tracks.Add(new WebMTrack(AudioTrackNumber, IsVideo: false, "A_OPUS", head, SampleRate: 48000, Channels: _audioChannels));
            }

            _writer = new WebMWriter(tracks, _container == "webm" ? "webm" : "matroska");
            return true;
        }

        private MediaVideoEncoder? CreateVideoEncoder(MediaCodecBackend backend, int width, int height)
        {
            var config = new VideoEncoderConfig { Codec = "vp8", Width = (uint)width, Height = (uint)height, Bitrate = Math.Max(1u, _recorder.VideoBitsPerSecond), Framerate = DefaultFrameRate };
            return backend.IsConfigSupported(config) ? backend.CreateVideoEncoder(config) : null;
        }

        /// <summary>Starts the sampling of the tracks.</summary>
        public void Begin()
        {
            _gathering = true;
            HasBegun = true;
            _startTimestamp = _clock.GetTimestamp();
            _nextSliceTime = _timeslice ?? long.MaxValue;

            // The first sample writes the header of the stream (delivered alone with a timeslice); the media is recorded from the next one
            _timer = _recorder._global.SetTimeout(Sample, 0);
        }

        public void Pause() => _pausedAt ??= _clock.GetTimestamp();

        public void Resume()
        {
            if (_pausedAt is { } pausedAt)
            {
                _pausedTicks += _clock.GetTimestamp() - pausedAt;
                _pausedAt = null;
            }
        }

        public void StopGathering()
        {
            IsStopping = true;
            _gathering = false;
            if (_timer != 0)
            {
                _recorder._global.ClearTimeout(_timer);
                _timer = 0;
            }
        }

        public void OnTrackStateChanged()
        {
            var tracks = _recorder.Stream.GetTracks();
            if (tracks.Count > 0 && tracks.All(track => track.ReadyState == MediaStreamTrackState.Ended))
                _recorder.OnAllTracksEnded(this);
        }

        /// <summary>The time of the recording in milliseconds (the paused durations excluded).</summary>
        private long RecordingTime
        {
            get
            {
                var now = _pausedAt ?? _clock.GetTimestamp();
                return (long)((now - _startTimestamp - _pausedTicks) * 1000d / _clock.TimestampFrequency);
            }
        }

        /// <summary>Samples the tracks, then schedules the next sample (one video frame period later).</summary>
        private void Sample()
        {
            _timer = 0;
            if (!_gathering)
                return;

            if (_pausedAt is null)
            {
                var time = RecordingTime;
                try
                {
                    if (_sampleCount++ == 0)
                    {
                        _writer?.WriteHeader();
                    }
                    else
                    {
                        RecordVideo(time);
                        RecordAudio(time);
                    }
                }
#pragma warning disable CA1031 // A failure of the codecs stops the recording with an error
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    _recorder.OnEncodingError(this, ex);
                    return;
                }

                if (time >= _nextSliceTime && _recorder._recording == this)
                {
                    _nextSliceTime = time + _timeslice!.Value;
                    _recorder.FireDataAvailable(this);
                    if (!_gathering)
                        return;
                }
            }

            var frameRate = _videoTrack?.Settings.GetValueOrDefault("frameRate") is double rate && rate > 0 ? Math.Min(rate, 60) : DefaultFrameRate;
            _timer = _recorder._global.SetTimeout(Sample, (int)Math.Ceiling(1000 / frameRate));
        }

        private void RecordVideo(long time)
        {
            if (_videoEncoder is null || _writer is null || _videoTrack?.GetVideoFrame() is not { } mediaFrame)
                return;

            var backend = _recorder._global.Options.MediaCodecs;
            if ((mediaFrame.Width, mediaFrame.Height) != _videoSize && backend is not null)
            {
                // The size of the track changed: the frames are encoded with their new size
                _videoEncoder.Dispose();
                _videoEncoder = CreateVideoEncoder(backend, mediaFrame.Width, mediaFrame.Height);
                _videoSize = (mediaFrame.Width, mediaFrame.Height);
                if (_videoEncoder is null)
                    return;
            }

            var keyFrame = _videoFrameCount == 0
                || (_recorder.KeyFrameIntervalCount is { } count && _videoFrameCount % Math.Max(count, 1) == 0)
                || (_recorder.KeyFrameIntervalDuration is { } duration && time - _lastKeyFrameTime >= duration);
            _videoFrameCount++;
            var pixels = new BitmapPixels(mediaFrame.Width, mediaFrame.Height, mediaFrame.Pixels.ToArray());
            var frame = new VideoFrame(pixels, time * 1000, duration: null, rotation: 0, flip: false);
            try
            {
                foreach (var output in _videoEncoder.Encode(frame, new VideoEncoderEncodeOptions { KeyFrame = keyFrame }))
                {
                    WriteChunk(VideoTrackNumber, output.Chunk.Timestamp, output.Chunk.Type == EncodedVideoChunkType.Key, output.Chunk.Data.Span);
                    if (output.Chunk.Type == EncodedVideoChunkType.Key)
                        _lastKeyFrameTime = output.Chunk.Timestamp / 1000;
                }
            }
            finally
            {
                frame.Close();
            }
        }

        private void RecordAudio(long time)
        {
            if (_audioEncoder is null || _writer is null || _audioTrack is null)
                return;

            // The audio of the track since the last sample, at its sample rate
            var target = time * _audioSampleRate / 1000;
            var frames = (int)Math.Min(target - _audioFramesRead, _audioSampleRate);
            if (frames <= 0)
                return;

            var samples = new float[frames * _audioChannels];
            _audioTrack.ReadAudio(_audioFramesRead, samples);
            var bytes = new byte[samples.Length * 4];
            for (var i = 0; i < samples.Length; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), samples[i]);
            }

            var timestamp = _audioFramesRead * 1_000_000 / _audioSampleRate;
            _audioFramesRead += frames;
            var data = new AudioData(new AudioDataInit { Format = AudioSampleFormat.F32, SampleRate = _audioSampleRate, NumberOfFrames = (uint)frames, NumberOfChannels = (uint)_audioChannels, Timestamp = timestamp, Data = bytes });
            try
            {
                foreach (var output in _audioEncoder.Encode(data))
                {
                    WriteChunk(AudioTrackNumber, output.Chunk.Timestamp, isKeyFrame: true, output.Chunk.Data.Span);
                }
            }
            finally
            {
                data.Close();
            }
        }

        private void WriteChunk(int trackNumber, long timestampMicroseconds, bool isKeyFrame, ReadOnlySpan<byte> data)
        {
            var milliseconds = Math.Max(0, timestampMicroseconds / 1000);
            _blobTimecode ??= milliseconds;
            _writer!.WriteFrame(trackNumber, milliseconds, isKeyFrame, data);
        }

        /// <summary>Encodes the pending media (the recording stops).</summary>
        public void Flush()
        {
            if (_writer is null)
                return;

            _writer.WriteHeader();

            try
            {
                foreach (var output in _videoEncoder?.Flush() ?? [])
                {
                    WriteChunk(VideoTrackNumber, output.Chunk.Timestamp, output.Chunk.Type == EncodedVideoChunkType.Key, output.Chunk.Data.Span);
                }

                foreach (var output in _audioEncoder?.Flush() ?? [])
                {
                    WriteChunk(AudioTrackNumber, output.Chunk.Timestamp, isKeyFrame: true, output.Chunk.Data.Span);
                }
            }
#pragma warning disable CA1031 // A failure of the codecs ends the recording with the data recorded so far
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }

        /// <summary>Returns the data written since the last blob, and the time of its first frame.</summary>
        public (byte[] Bytes, double Timecode) TakeData()
        {
            var timecode = _blobCount++ == 0 ? 0 : _blobTimecode ?? RecordingTime;
            _blobTimecode = null;
            if (_writer is null)
                return ([], timecode);

            var bytes = _writer.TakeBytes();
            _writer.StartCluster();
            return (bytes, timecode);
        }

        public void Close()
        {
            StopGathering();
            _videoEncoder?.Dispose();
            _audioEncoder?.Dispose();
            _videoEncoder = null;
            _audioEncoder = null;
        }
    }
}
