namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A stream of media (https://w3c.github.io/mediacapture-main/#mediastream): a set of audio and video tracks, played by
/// the media elements (<see cref="HtmlMediaElement.SrcObject"/>), the Web Audio API and the other sinks.
/// </summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastream"/>
[IdlInterface("MediaStream", "https://w3c.github.io/mediacapture-main/#dom-mediastream")]
public sealed class MediaStream : EventTarget
{
    private readonly List<MediaStreamTrack> _tracks = [];
    private Action? _tracksChanged;

    /// <summary>Creates a stream without track (https://w3c.github.io/mediacapture-main/#dom-mediastream-constructor).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastream-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/mediacapture-main/#dom-mediastream-constructor")]
    public MediaStream()
    {
        Id = Guid.NewGuid().ToString();
    }

    /// <summary>Creates a stream with the tracks of another stream (the same track objects).</summary>
    /// <param name="stream">The stream whose tracks are added.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastream-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/mediacapture-main/#dom-mediastream-constructor")]
    public MediaStream(MediaStream stream)
        : this()
    {
        ArgumentNullException.ThrowIfNull(stream);
        foreach (var track in stream._tracks)
        {
            AddTrackCore(track);
        }
    }

    /// <summary>Creates a stream with tracks (a track given several times is added once).</summary>
    /// <param name="tracks">The tracks.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastream-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/mediacapture-main/#dom-mediastream-constructor")]
    public MediaStream(IEnumerable<MediaStreamTrack> tracks)
        : this()
    {
        ArgumentNullException.ThrowIfNull(tracks);
        foreach (var track in tracks)
        {
            ArgumentNullException.ThrowIfNull(track, nameof(tracks));
            AddTrackCore(track);
        }
    }

    internal MediaStream(GlobalScope global, IEnumerable<MediaStreamTrack> tracks)
        : this(tracks)
    {
        SetRelevantGlobalCore(global);
    }

    /// <summary>Gets the identifier of the stream (a UUID).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastream-id"/>
    [IdlMember("id", "https://w3c.github.io/mediacapture-main/#dom-mediastream-id")]
    public string Id { get; }

    /// <summary>Gets whether the stream has a track that is not ended.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastream-active"/>
    [IdlMember("active", "https://w3c.github.io/mediacapture-main/#dom-mediastream-active")]
    public bool Active => _tracks.Exists(track => track.ReadyState == MediaStreamTrackState.Live);

    /// <summary>Gets or sets the event handler of the <c>addtrack</c> event.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastream-onaddtrack"/>
    [IdlMember("onaddtrack", "https://w3c.github.io/mediacapture-main/#dom-mediastream-onaddtrack")]
    public Action<Event>? OnAddTrack
    {
        get => GetEventHandler("addtrack");
        set => SetEventHandler("addtrack", value);
    }

    /// <summary>Gets or sets the event handler of the <c>removetrack</c> event.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastream-onremovetrack"/>
    [IdlMember("onremovetrack", "https://w3c.github.io/mediacapture-main/#dom-mediastream-onremovetrack")]
    public Action<Event>? OnRemoveTrack
    {
        get => GetEventHandler("removetrack");
        set => SetEventHandler("removetrack", value);
    }

    /// <summary>The tracks of the stream, in the order they were added.</summary>
    internal IReadOnlyList<MediaStreamTrack> Tracks => _tracks;

    /// <summary>Returns the audio tracks of the stream.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastream-getaudiotracks"/>
    [IdlMember("getAudioTracks", "https://w3c.github.io/mediacapture-main/#dom-mediastream-getaudiotracks")]
    public IReadOnlyList<MediaStreamTrack> GetAudioTracks() => _tracks.FindAll(track => track.Source.Kind == MediaTrackKinds.Audio);

    /// <summary>Returns the video tracks of the stream.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastream-getvideotracks"/>
    [IdlMember("getVideoTracks", "https://w3c.github.io/mediacapture-main/#dom-mediastream-getvideotracks")]
    public IReadOnlyList<MediaStreamTrack> GetVideoTracks() => _tracks.FindAll(track => track.Source.Kind == MediaTrackKinds.Video);

    /// <summary>Returns the tracks of the stream.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastream-gettracks"/>
    [IdlMember("getTracks", "https://w3c.github.io/mediacapture-main/#dom-mediastream-gettracks")]
    public IReadOnlyList<MediaStreamTrack> GetTracks() => [.. _tracks];

    /// <summary>Returns the track of the stream with an identifier, or null.</summary>
    /// <param name="trackId">The identifier of the track.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastream-gettrackbyid"/>
    [IdlMember("getTrackById", "https://w3c.github.io/mediacapture-main/#dom-mediastream-gettrackbyid")]
    public MediaStreamTrack? GetTrackById(string trackId) => _tracks.Find(track => track.Id == trackId);

    /// <summary>Adds a track to the stream, unless it is already in the stream. No event is fired.</summary>
    /// <param name="track">The track.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastream-addtrack"/>
    [IdlMember("addTrack", "https://w3c.github.io/mediacapture-main/#dom-mediastream-addtrack")]
    public void AddTrack(MediaStreamTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (AddTrackCore(track))
        {
            _tracksChanged?.Invoke();
        }
    }

    /// <summary>Removes a track from the stream. No event is fired.</summary>
    /// <param name="track">The track.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastream-removetrack"/>
    [IdlMember("removeTrack", "https://w3c.github.io/mediacapture-main/#dom-mediastream-removetrack")]
    public void RemoveTrack(MediaStreamTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (_tracks.Remove(track))
        {
            _tracksChanged?.Invoke();
        }
    }

    /// <summary>Creates a stream with the clones of the tracks of the stream (https://w3c.github.io/mediacapture-main/#dom-mediastream-clone).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastream-clone"/>
    [IdlMember("clone", "https://w3c.github.io/mediacapture-main/#dom-mediastream-clone")]
    public MediaStream Clone() => new([.. _tracks.Select(track => track.Clone())]);

    private bool AddTrackCore(MediaStreamTrack track)
    {
        if (_tracks.Contains(track))
            return false;

        _tracks.Add(track);
        return true;
    }

    /// <summary>Registers the callback of a sink (e.g. a media element) that is notified when the tracks of the stream change.</summary>
    internal void AddTracksChangedCallback(Action callback) => _tracksChanged += callback;

    internal void RemoveTracksChangedCallback(Action callback) => _tracksChanged -= callback;

    /// <summary>The user agent adds tracks to the stream (e.g. remote tracks): an <c>addtrack</c> event is fired for each of them once they are all added (https://w3c.github.io/mediacapture-main/#add-track).</summary>
    internal void AddTracksByUserAgent(IReadOnlyList<MediaStreamTrack> tracks)
    {
        var added = tracks.Where(AddTrackCore).ToList();
        if (added.Count == 0)
            return;

        _tracksChanged?.Invoke();
        foreach (var track in added)
        {
            FireEvent(new MediaStreamTrackEvent("addtrack", new MediaStreamTrackEventInit { Track = track }));
        }
    }

    /// <summary>The user agent removes tracks from the stream: a <c>removetrack</c> event is fired for each of them once they are all removed (https://w3c.github.io/mediacapture-main/#remove-track).</summary>
    internal void RemoveTracksByUserAgent(IReadOnlyList<MediaStreamTrack> tracks)
    {
        var removed = tracks.Where(_tracks.Remove).ToList();
        if (removed.Count == 0)
            return;

        _tracksChanged?.Invoke();
        foreach (var track in removed)
        {
            FireEvent(new MediaStreamTrackEvent("removetrack", new MediaStreamTrackEventInit { Track = track }));
        }
    }
}

/// <summary>The initialization dictionary of a <see cref="MediaStreamTrackEvent"/> (https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackeventinit).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackeventinit"/>
[IdlInterface("MediaStreamTrackEventInit", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackeventinit")]
public sealed class MediaStreamTrackEventInit : EventInit
{
    /// <summary>Gets the track of the event.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackeventinit-track"/>
    [IdlMember("track", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackeventinit-track")]
    public required MediaStreamTrack Track { get; init; }
}

/// <summary>The event fired when a track is added to a stream or removed from it (<c>addtrack</c>, <c>removetrack</c>, https://w3c.github.io/mediacapture-main/#mediastreamtrackevent).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackevent"/>
[IdlInterface("MediaStreamTrackEvent", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackevent")]
public sealed class MediaStreamTrackEvent : Event
{
    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary, with the track.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackevent-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackevent-constructor")]
    public MediaStreamTrackEvent(string type, MediaStreamTrackEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        ArgumentNullException.ThrowIfNull(eventInitDict);
        Track = eventInitDict.Track;
    }

    /// <summary>Gets the track that was added or removed.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackevent-track"/>
    [IdlMember("track", "https://w3c.github.io/mediacapture-main/#dom-mediastreamtrackevent-track")]
    public MediaStreamTrack Track { get; }
}

/// <summary>
/// The error of a constraint that cannot be satisfied (https://w3c.github.io/mediacapture-main/#overconstrainederror-interface):
/// a <see cref="DomException"/> named <c>OverconstrainedError</c>, with the name of the constraint.
/// </summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-overconstrainederror"/>
[IdlInterface("OverconstrainedError", "https://w3c.github.io/mediacapture-main/#dom-overconstrainederror")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "The constructor with one string is the one of the IDL, whose argument is the name of the constraint")]
public sealed class OverconstrainedError : DomException, Scripting.IStructuredSerializable
{
    /// <summary>Creates an error.</summary>
    /// <param name="constraint">The name of the constraint that cannot be satisfied, or an empty string.</param>
    /// <param name="message">The message of the error.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-overconstrainederror-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/mediacapture-main/#dom-overconstrainederror-constructor")]
    public OverconstrainedError(string constraint, string message = "")
        : base(message ?? "", "OverconstrainedError")
    {
        ArgumentNullException.ThrowIfNull(constraint);
        Constraint = constraint;
    }

    /// <summary>Creates an error without constraint name.</summary>
    [NotInIdl]
    public OverconstrainedError()
        : this("", "")
    {
    }

    /// <summary>Creates an error with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    [NotInIdl]
    public OverconstrainedError(string? message, Exception? innerException)
        : base(message, innerException)
    {
        Name = "OverconstrainedError";
        Constraint = "";
    }

    /// <summary>Gets the name of the constraint that cannot be satisfied, or an empty string.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-overconstrainederror-constraint"/>
    [IdlMember("constraint", "https://w3c.github.io/mediacapture-main/#dom-overconstrainederror-constraint")]
    public string Constraint { get; }

    object Scripting.IStructuredSerializable.CloneForStructuredSerialization() => new OverconstrainedError(Constraint, Message);
}
