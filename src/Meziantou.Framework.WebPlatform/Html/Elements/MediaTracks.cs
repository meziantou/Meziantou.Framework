using System.Collections;
using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

// The audio and video tracks of the media resources
// (https://html.spec.whatwg.org/multipage/media.html#media-resources-with-multiple-media-tracks): the tracks are the ones
// the player of the media backend reports (MediaPlayer.AudioTracks, MediaPlayer.VideoTracks).

/// <summary>https://html.spec.whatwg.org/multipage/media.html#audiotrack</summary>
[IdlInterface("AudioTrack", "https://html.spec.whatwg.org/multipage/media.html#audiotrack")]
public sealed class AudioTrack
{
    private bool _enabled;

    internal AudioTrack(MediaTrackInfo info)
    {
        Info = info;
    }

    internal MediaTrackInfo Info { get; }

    /// <summary>The list that contains the track, or null once the track is removed from it (e.g. another resource is loaded).</summary>
    internal AudioTrackList? List { get; set; }

    /// <summary>The track of the source buffer that created the track (Media Source Extensions), or null.</summary>
    internal SourceBufferTrack? MediaSourceTrack { get; set; }

    /// <summary>
    /// Gets the source buffer that created the track (https://w3c.github.io/media-source/#dom-audiotrack-sourcebuffer), or
    /// null: the track was not created by a source buffer, the source buffer was removed from its media source, or the media
    /// source was constructed in a dedicated worker.
    /// </summary>
    [IdlMember("sourceBuffer", "https://w3c.github.io/media-source/#dom-audiotrack-sourcebuffer")]
    public SourceBuffer? SourceBuffer { get; internal set; }

    /// <summary>Gets the identifier of the track, or an empty string.</summary>
    [IdlMember("id", "https://html.spec.whatwg.org/multipage/media.html#dom-audiotrack-id")]
    public string Id => Info.Id;

    /// <summary>Gets the category of the track (e.g. <c>main</c>), or an empty string.</summary>
    [IdlMember("kind", "https://html.spec.whatwg.org/multipage/media.html#dom-audiotrack-kind")]
    public string Kind => Info.Kind;

    /// <summary>Gets the label of the track, or an empty string.</summary>
    [IdlMember("label", "https://html.spec.whatwg.org/multipage/media.html#dom-audiotrack-label")]
    public string Label => Info.Label;

    /// <summary>Gets the BCP 47 language tag of the track, or an empty string.</summary>
    [IdlMember("language", "https://html.spec.whatwg.org/multipage/media.html#dom-audiotrack-language")]
    public string Language => Info.Language;

    /// <summary>
    /// Gets or sets a value indicating whether the track is enabled (https://html.spec.whatwg.org/multipage/media.html#dom-audiotrack-enabled).
    /// A <c>change</c> event is fired at the list of the track when the value changes.
    /// </summary>
    [IdlMember("enabled", "https://html.spec.whatwg.org/multipage/media.html#dom-audiotrack-enabled")]
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;

            _enabled = value;
            if (List is { Element: { } element } list)
            {
                element.OnAudioTrackEnabledChanged(this);
                element.QueueTrackListChange(list);
            }

            // The track is also in the list of the source buffer that created it
            SourceBuffer?.AudioTracks.QueueEvent(new Event("change"));
        }
    }

    /// <summary>Enables the track without firing an event (the resource fetch algorithm enables the first audio track).</summary>
    internal void SetEnabledSilently(bool value) => _enabled = value;
}

/// <summary>https://html.spec.whatwg.org/multipage/media.html#audiotracklist</summary>
[IdlInterface("AudioTrackList", "https://html.spec.whatwg.org/multipage/media.html#audiotracklist")]
public sealed class AudioTrackList : EventTarget, IReadOnlyList<AudioTrack>
{
    private readonly List<AudioTrack> _tracks = [];

    private readonly SourceBuffer? _sourceBuffer;

    internal AudioTrackList(HtmlMediaElement element)
    {
        Element = element;
    }

    /// <summary>Creates the list of the audio tracks created by a source buffer (https://w3c.github.io/media-source/#dom-sourcebuffer-audiotracks).</summary>
    internal AudioTrackList(SourceBuffer sourceBuffer)
    {
        _sourceBuffer = sourceBuffer;
    }

    /// <summary>The media element of the list, or null for the list of a source buffer.</summary>
    internal HtmlMediaElement? Element { get; }

    /// <summary>The relevant global object of the list is the one of its media element (or of its source buffer).</summary>
    internal override GlobalScope? RelevantGlobal => Element?.RelevantGlobal ?? _sourceBuffer?.RelevantGlobal;

    /// <summary>Queues a task to fire an event at the list.</summary>
    internal void QueueEvent(Event @event) => RelevantGlobal?.QueueGlobalTask(() => FireEvent(@event));

    /// <summary>
    /// Adds a track created by a source buffer (https://w3c.github.io/media-source/#sourcebuffer-init-segment-received): its
    /// enabled state is set by the source buffer, and a task is queued to fire the <c>addtrack</c> event.
    /// </summary>
    internal void AddMediaSourceTrack(AudioTrack track)
    {
        if (Element is not null)
        {
            track.List = this;
        }

        _tracks.Add(track);
        QueueEvent(new TrackEvent("addtrack", new TrackEventInit { Track = track }));
    }

    /// <summary>
    /// Removes a track created by a source buffer (https://w3c.github.io/media-source/#dom-mediasource-removesourcebuffer):
    /// tasks are queued to fire the <c>removetrack</c> event, and the <c>change</c> event when the track was enabled.
    /// </summary>
    internal void RemoveMediaSourceTrack(AudioTrack track)
    {
        if (!_tracks.Remove(track))
            return;

        if (ReferenceEquals(track.List, this))
        {
            track.List = null;
        }

        QueueEvent(new TrackEvent("removetrack", new TrackEventInit { Track = track }));
        if (track.Enabled)
        {
            QueueEvent(new Event("change"));
        }
    }

    /// <summary>Gets the number of tracks in the list.</summary>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/media.html#dom-audiotracklist-length")]
    public uint Length => (uint)_tracks.Count;

    /// <summary>Gets the track at the given index, or null when the index is out of range.</summary>
    [IdlMember("getter", "https://html.spec.whatwg.org/multipage/media.html#dom-tracklist-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public AudioTrack? this[uint index] => index < (uint)_tracks.Count ? _tracks[(int)index] : null;

    /// <summary>Gets the track at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public AudioTrack this[int index] => _tracks[index];

    /// <summary>Gets the number of tracks in the list.</summary>
    [NotInIdl]
    public int Count => _tracks.Count;

    /// <summary>Returns the first track of the list with the given identifier, or null.</summary>
    [IdlMember("getTrackById", "https://html.spec.whatwg.org/multipage/media.html#dom-audiotracklist-gettrackbyid")]
    public AudioTrack? GetTrackById(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _tracks.Find(track => track.Id == id);
    }

    /// <summary>Gets or sets the event handler of the <c>change</c> event.</summary>
    [IdlMember("onchange", "https://html.spec.whatwg.org/multipage/media.html#handler-tracklist-onchange")]
    public Action<Event>? OnChange
    {
        get => GetEventHandler("change");
        set => SetEventHandler("change", value);
    }

    /// <summary>Gets or sets the event handler of the <c>addtrack</c> event.</summary>
    [IdlMember("onaddtrack", "https://html.spec.whatwg.org/multipage/media.html#handler-tracklist-onaddtrack")]
    public Action<Event>? OnAddTrack
    {
        get => GetEventHandler("addtrack");
        set => SetEventHandler("addtrack", value);
    }

    /// <summary>Gets or sets the event handler of the <c>removetrack</c> event.</summary>
    [IdlMember("onremovetrack", "https://html.spec.whatwg.org/multipage/media.html#handler-tracklist-onremovetrack")]
    public Action<Event>? OnRemoveTrack
    {
        get => GetEventHandler("removetrack");
        set => SetEventHandler("removetrack", value);
    }

    /// <summary>
    /// Adds an audio track of the media resource (https://html.spec.whatwg.org/multipage/media.html#media-data-processing-steps-list):
    /// the track is enabled when no track is enabled yet, and an <c>addtrack</c> event is fired at the list.
    /// </summary>
    internal void Add(AudioTrack track)
    {
        track.List = this;
        if (!_tracks.Exists(static other => other.Enabled))
        {
            track.SetEnabledSilently(true);
        }

        _tracks.Add(track);
        FireEvent(new TrackEvent("addtrack", new TrackEventInit { Track = track }));
    }

    /// <summary>Empties the list without firing events.</summary>
    internal void Clear()
    {
        foreach (var track in _tracks)
        {
            track.List = null;
        }

        _tracks.Clear();
    }

    /// <inheritdoc/>
    [NotInIdl("IEnumerable implementation")]
    public IEnumerator<AudioTrack> GetEnumerator() => _tracks.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>https://html.spec.whatwg.org/multipage/media.html#videotrack</summary>
[IdlInterface("VideoTrack", "https://html.spec.whatwg.org/multipage/media.html#videotrack")]
public sealed class VideoTrack
{
    private bool _selected;

    internal VideoTrack(MediaTrackInfo info)
    {
        Info = info;
    }

    internal MediaTrackInfo Info { get; }

    /// <summary>The list that contains the track, or null once the track is removed from it (e.g. another resource is loaded).</summary>
    internal VideoTrackList? List { get; set; }

    /// <summary>The track of the source buffer that created the track (Media Source Extensions), or null.</summary>
    internal SourceBufferTrack? MediaSourceTrack { get; set; }

    /// <summary>
    /// Gets the source buffer that created the track (https://w3c.github.io/media-source/#dom-videotrack-sourcebuffer), or
    /// null: the track was not created by a source buffer, the source buffer was removed from its media source, or the media
    /// source was constructed in a dedicated worker.
    /// </summary>
    [IdlMember("sourceBuffer", "https://w3c.github.io/media-source/#dom-videotrack-sourcebuffer")]
    public SourceBuffer? SourceBuffer { get; internal set; }

    /// <summary>Gets the identifier of the track, or an empty string.</summary>
    [IdlMember("id", "https://html.spec.whatwg.org/multipage/media.html#dom-videotrack-id")]
    public string Id => Info.Id;

    /// <summary>Gets the category of the track (e.g. <c>main</c>), or an empty string.</summary>
    [IdlMember("kind", "https://html.spec.whatwg.org/multipage/media.html#dom-videotrack-kind")]
    public string Kind => Info.Kind;

    /// <summary>Gets the label of the track, or an empty string.</summary>
    [IdlMember("label", "https://html.spec.whatwg.org/multipage/media.html#dom-videotrack-label")]
    public string Label => Info.Label;

    /// <summary>Gets the BCP 47 language tag of the track, or an empty string.</summary>
    [IdlMember("language", "https://html.spec.whatwg.org/multipage/media.html#dom-videotrack-language")]
    public string Language => Info.Language;

    /// <summary>
    /// Gets or sets a value indicating whether the track is selected (https://html.spec.whatwg.org/multipage/media.html#dom-videotrack-selected).
    /// Selecting a track unselects the other tracks of its list; a <c>change</c> event is fired at the list when a track is
    /// selected, or when the selected track is unselected.
    /// </summary>
    [IdlMember("selected", "https://html.spec.whatwg.org/multipage/media.html#dom-videotrack-selected")]
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;

            _selected = value;
            if (List is not { } list)
                return;

            if (value)
            {
                foreach (var other in list)
                {
                    if (!ReferenceEquals(other, this))
                    {
                        other._selected = false;
                    }
                }
            }

            // The task that fires the change event is queued before the one that fires the resize event, if any
            SourceBuffer?.VideoTracks.QueueEvent(new Event("change"));
            if (list.Element is { } element)
            {
                element.QueueTrackListChange(list);
                element.OnSelectedVideoTrackChanged(value ? this : null);
            }
        }
    }

    /// <summary>Selects the track without firing an event (the resource fetch algorithm selects the first video track).</summary>
    internal void SetSelectedSilently(bool value) => _selected = value;
}

/// <summary>https://html.spec.whatwg.org/multipage/media.html#videotracklist</summary>
[IdlInterface("VideoTrackList", "https://html.spec.whatwg.org/multipage/media.html#videotracklist")]
public sealed class VideoTrackList : EventTarget, IReadOnlyList<VideoTrack>
{
    private readonly List<VideoTrack> _tracks = [];

    private readonly SourceBuffer? _sourceBuffer;

    internal VideoTrackList(HtmlMediaElement element)
    {
        Element = element;
    }

    /// <summary>Creates the list of the video tracks created by a source buffer (https://w3c.github.io/media-source/#dom-sourcebuffer-videotracks).</summary>
    internal VideoTrackList(SourceBuffer sourceBuffer)
    {
        _sourceBuffer = sourceBuffer;
    }

    /// <summary>The media element of the list, or null for the list of a source buffer.</summary>
    internal HtmlMediaElement? Element { get; }

    /// <summary>The relevant global object of the list is the one of its media element (or of its source buffer).</summary>
    internal override GlobalScope? RelevantGlobal => Element?.RelevantGlobal ?? _sourceBuffer?.RelevantGlobal;

    /// <summary>Queues a task to fire an event at the list.</summary>
    internal void QueueEvent(Event @event) => RelevantGlobal?.QueueGlobalTask(() => FireEvent(@event));

    /// <summary>
    /// Adds a track created by a source buffer (https://w3c.github.io/media-source/#sourcebuffer-init-segment-received): its
    /// selected state is set by the source buffer (the other tracks of the list of a media element are then unselected), and
    /// a task is queued to fire the <c>addtrack</c> event. Returns whether another track was unselected.
    /// </summary>
    internal bool AddMediaSourceTrack(VideoTrack track)
    {
        var unselected = false;
        if (Element is not null)
        {
            track.List = this;
            if (track.Selected)
            {
                foreach (var other in _tracks)
                {
                    if (other.Selected)
                    {
                        other.SetSelectedSilently(false);
                        unselected = true;
                    }
                }
            }
        }

        _tracks.Add(track);
        QueueEvent(new TrackEvent("addtrack", new TrackEventInit { Track = track }));
        return unselected;
    }

    /// <summary>
    /// Removes a track created by a source buffer (https://w3c.github.io/media-source/#dom-mediasource-removesourcebuffer):
    /// tasks are queued to fire the <c>removetrack</c> event, and the <c>change</c> event when the track was selected.
    /// </summary>
    internal void RemoveMediaSourceTrack(VideoTrack track)
    {
        if (!_tracks.Remove(track))
            return;

        if (ReferenceEquals(track.List, this))
        {
            track.List = null;
        }

        QueueEvent(new TrackEvent("removetrack", new TrackEventInit { Track = track }));
        if (track.Selected)
        {
            QueueEvent(new Event("change"));
        }
    }

    /// <summary>Gets the number of tracks in the list.</summary>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/media.html#dom-videotracklist-length")]
    public uint Length => (uint)_tracks.Count;

    /// <summary>Gets the track at the given index, or null when the index is out of range.</summary>
    [IdlMember("getter", "https://html.spec.whatwg.org/multipage/media.html#dom-tracklist-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public VideoTrack? this[uint index] => index < (uint)_tracks.Count ? _tracks[(int)index] : null;

    /// <summary>Gets the track at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public VideoTrack this[int index] => _tracks[index];

    /// <summary>Gets the number of tracks in the list.</summary>
    [NotInIdl]
    public int Count => _tracks.Count;

    /// <summary>Returns the first track of the list with the given identifier, or null.</summary>
    [IdlMember("getTrackById", "https://html.spec.whatwg.org/multipage/media.html#dom-videotracklist-gettrackbyid")]
    public VideoTrack? GetTrackById(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _tracks.Find(track => track.Id == id);
    }

    /// <summary>Gets the index of the selected track, or -1 when no track is selected.</summary>
    [IdlMember("selectedIndex", "https://html.spec.whatwg.org/multipage/media.html#dom-videotracklist-selectedindex")]
    public int SelectedIndex => _tracks.FindIndex(static track => track.Selected);

    /// <summary>Gets or sets the event handler of the <c>change</c> event.</summary>
    [IdlMember("onchange", "https://html.spec.whatwg.org/multipage/media.html#handler-tracklist-onchange")]
    public Action<Event>? OnChange
    {
        get => GetEventHandler("change");
        set => SetEventHandler("change", value);
    }

    /// <summary>Gets or sets the event handler of the <c>addtrack</c> event.</summary>
    [IdlMember("onaddtrack", "https://html.spec.whatwg.org/multipage/media.html#handler-tracklist-onaddtrack")]
    public Action<Event>? OnAddTrack
    {
        get => GetEventHandler("addtrack");
        set => SetEventHandler("addtrack", value);
    }

    /// <summary>Gets or sets the event handler of the <c>removetrack</c> event.</summary>
    [IdlMember("onremovetrack", "https://html.spec.whatwg.org/multipage/media.html#handler-tracklist-onremovetrack")]
    public Action<Event>? OnRemoveTrack
    {
        get => GetEventHandler("removetrack");
        set => SetEventHandler("removetrack", value);
    }

    /// <summary>
    /// Adds a video track of the media resource (https://html.spec.whatwg.org/multipage/media.html#media-data-processing-steps-list):
    /// the track is selected when no track is selected yet, and an <c>addtrack</c> event is fired at the list.
    /// </summary>
    internal void Add(VideoTrack track)
    {
        track.List = this;
        if (!_tracks.Exists(static other => other.Selected))
        {
            track.SetSelectedSilently(true);
        }

        _tracks.Add(track);
        FireEvent(new TrackEvent("addtrack", new TrackEventInit { Track = track }));
    }

    /// <summary>Empties the list without firing events.</summary>
    internal void Clear()
    {
        foreach (var track in _tracks)
        {
            track.List = null;
        }

        _tracks.Clear();
    }

    /// <inheritdoc/>
    [NotInIdl("IEnumerable implementation")]
    public IEnumerator<VideoTrack> GetEnumerator() => _tracks.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>https://html.spec.whatwg.org/multipage/media.html#trackeventinit</summary>
[IdlInterface("TrackEventInit", "https://html.spec.whatwg.org/multipage/media.html#trackeventinit")]
public sealed class TrackEventInit : EventInit
{
    /// <summary>Gets the track of the event: an <see cref="AudioTrack"/> or a <see cref="VideoTrack"/> (the text tracks are not implemented), or null.</summary>
    [IdlMember("track", "https://html.spec.whatwg.org/multipage/media.html#dom-trackeventinit-track")]
    public object? Track { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/media.html#trackevent</summary>
[IdlInterface("TrackEvent", "https://html.spec.whatwg.org/multipage/media.html#trackevent")]
public sealed class TrackEvent : Event
{
    /// <summary>Creates a track event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/media.html#dom-trackevent")]
    public TrackEvent(string type, TrackEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Track = eventInitDict?.Track;
    }

    /// <summary>Gets the track of the event: an <see cref="AudioTrack"/> or a <see cref="VideoTrack"/>, or null.</summary>
    [IdlMember("track", "https://html.spec.whatwg.org/multipage/media.html#dom-trackevent-track")]
    public object? Track { get; }
}
