using System.Collections;
using Meziantou.Framework.WebPlatform.Css.Om;

namespace Meziantou.Framework.WebPlatform.Dom;

// The text tracks of the media elements (https://html.spec.whatwg.org/multipage/media.html#timed-text-tracks): the tracks
// of the track element children (HtmlTrackElement, whose cues are parsed from a WebVTT file), the tracks created by
// addTextTrack() and the tracks of the media resource reported by the player of the media backend.
// - TextTracks.cs: the model (TextTrack, TextTrackList, TextTrackCue, TextTrackCueList);
// - MediaElements.TextTracks.cs: the list of text tracks of a media element, the automatic track selection and the cues
//   of the "time marches on" steps;
// - TrackElement.cs: the track processing model (the fetch of the resource of a track element);
// - WebVtt/: the WebVTT parser, the cues (VttCue, VttRegion) and their rendering.

/// <summary>https://html.spec.whatwg.org/multipage/media.html#texttrackmode</summary>
[IdlInterface("TextTrackMode", "https://html.spec.whatwg.org/multipage/media.html#texttrackmode")]
public enum TextTrackMode
{
    /// <summary>The track is not active: no cue is active, no event is fired and its cues are not obtained.</summary>
    [IdlMember("disabled", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-disabled")]
    Disabled,

    /// <summary>The track is active (its cues are obtained, the events of its cues are fired), but its cues are not displayed.</summary>
    [IdlMember("hidden", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-hidden")]
    Hidden,

    /// <summary>The track is active and its cues are displayed.</summary>
    [IdlMember("showing", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-showing")]
    Showing,
}

/// <summary>https://html.spec.whatwg.org/multipage/media.html#texttrackkind</summary>
[IdlInterface("TextTrackKind", "https://html.spec.whatwg.org/multipage/media.html#texttrackkind")]
public enum TextTrackKind
{
    /// <summary>The transcription or the translation of the dialog.</summary>
    [IdlMember("subtitles", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-subtitles")]
    Subtitles,

    /// <summary>The transcription or the translation of the dialog, the sound effects and the other relevant audio information.</summary>
    [IdlMember("captions", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-captions")]
    Captions,

    /// <summary>The textual descriptions of the video component of the media resource.</summary>
    [IdlMember("descriptions", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-descriptions")]
    Descriptions,

    /// <summary>The chapter titles, used to navigate the media resource.</summary>
    [IdlMember("chapters", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-chapters")]
    Chapters,

    /// <summary>The tracks intended for use from script, which are not displayed.</summary>
    [IdlMember("metadata", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-metadata")]
    Metadata,
}

/// <summary>The readiness state of a text track (https://html.spec.whatwg.org/multipage/media.html#text-track-readiness-state).</summary>
internal enum TextTrackReadinessState
{
    NotLoaded = 0,
    Loading = 1,
    Loaded = 2,
    FailedToLoad = 3,
}

/// <summary>https://html.spec.whatwg.org/multipage/media.html#texttrack</summary>
[IdlInterface("TextTrack", "https://html.spec.whatwg.org/multipage/media.html#texttrack")]
public sealed class TextTrack : EventTarget
{
    // The text track list of cues, in text track cue order (https://html.spec.whatwg.org/multipage/media.html#text-track-cue-order)
    private readonly List<TextTrackCue> _cues = [];
    private readonly TextTrackKind _kind;
    private readonly string _label;
    private readonly string _language;
    private readonly string _id;
    private TextTrackMode _mode;
    private TextTrackReadinessState _readinessState;
    private long _nextCueOrder;

    /// <summary>The text track of a track element: its kind, label, language and identifier are the ones of the element.</summary>
    internal TextTrack(HtmlTrackElement element)
    {
        TrackElement = element;
        _label = _language = _id = "";
    }

    /// <summary>A text track created by <c>addTextTrack()</c>, or a text track of the media resource.</summary>
    internal TextTrack(HtmlMediaElement element, TextTrackKind kind, string label, string language, string id = "", string inBandMetadataTrackDispatchType = "")
    {
        MediaElement = element;
        _kind = kind;
        _label = label;
        _language = language;
        _id = id;
        InBandMetadataTrackDispatchType = inBandMetadataTrackDispatchType;
        _readinessState = TextTrackReadinessState.Loaded;
    }

    /// <summary>The track element of the text track, or null (a track created by <c>addTextTrack()</c>, or a track of the media resource).</summary>
    internal HtmlTrackElement? TrackElement { get; }

    /// <summary>The media element whose list of text tracks contains the text track, or null.</summary>
    internal HtmlMediaElement? MediaElement { get; set; }

    /// <summary>The text track list of cues, in text track cue order.</summary>
    internal List<TextTrackCue> CueList => _cues;

    /// <summary>Incremented when the list of cues, the order of the cues or the active flag of a cue changes (the live lists of cues are computed again).</summary>
    internal int CuesVersion { get; private set; }

    /// <summary>The regions of the WebVTT file of the track (https://w3c.github.io/webvtt/#text-track-list-of-regions).</summary>
    internal List<VttRegion> Regions { get; } = [];

    /// <summary>The style sheets of the STYLE blocks of the WebVTT file of the track, which apply to its cues.</summary>
    internal List<CssStyleSheet> StyleSheets { get; } = [];

    /// <summary>The relevant global object of the track is the one of its track element, or of its media element.</summary>
    internal override GlobalScope? RelevantGlobal => TrackElement?.RelevantGlobal ?? MediaElement?.RelevantGlobal ?? base.RelevantGlobal;

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#text-track-readiness-state</summary>
    internal TextTrackReadinessState ReadinessState
    {
        get => _readinessState;
        set
        {
            if (_readinessState == value)
                return;

            _readinessState = value;

            // Whenever the readiness state changes to either loaded or failed to load, the track is removed from the list of
            // pending text tracks it is in
            if (value is TextTrackReadinessState.Loaded or TextTrackReadinessState.FailedToLoad)
            {
                MediaElement?.OnTextTrackNoLongerLoading(this);
            }
        }
    }

    /// <summary>Gets the kind of the track (https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-kind).</summary>
    [IdlMember("kind", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-kind")]
    public TextTrackKind Kind => TrackElement is { } element ? element.TrackKind : _kind;

    /// <summary>Gets the label of the track, or an empty string (https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-label).</summary>
    [IdlMember("label", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-label")]
    public string Label => TrackElement is { } element ? element.GetAttributeValue("label") ?? "" : _label;

    /// <summary>Gets the language of the track (a BCP 47 language tag), or an empty string (https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-language).</summary>
    [IdlMember("language", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-language")]
    public string Language => TrackElement is { } element ? element.GetAttributeValue("srclang") ?? "" : _language;

    /// <summary>
    /// Gets the identifier of the track (https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-id): the <c>id</c>
    /// attribute of its track element, the identifier of a track of the media resource, or an empty string.
    /// </summary>
    [IdlMember("id", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-id")]
    public string Id => TrackElement is { } element ? element.GetAttributeValue("id") ?? "" : _id;

    /// <summary>
    /// Gets the source buffer that created the track (https://w3c.github.io/media-source/#dom-texttrack-sourcebuffer): always
    /// null (the text tracks of the byte streams of the Media Source Extensions are not supported).
    /// </summary>
    [IdlMember("sourceBuffer", "https://w3c.github.io/media-source/#dom-texttrack-sourcebuffer")]
    public SourceBuffer? SourceBuffer => null;

    /// <summary>Gets the in-band metadata track dispatch type of a metadata track of the media resource, or an empty string.</summary>
    [IdlMember("inBandMetadataTrackDispatchType", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-inbandmetadatatrackdispatchtype")]
    public string InBandMetadataTrackDispatchType { get; } = "";

    /// <summary>
    /// Gets or sets the mode of the track (https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-mode). A track
    /// whose mode is not <see cref="TextTrackMode.Disabled"/> obtains the cues of its track element; a <c>change</c> event
    /// is fired at the list of text tracks of the media element when the mode changes.
    /// </summary>
    [IdlMember("mode", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-mode")]
    public TextTrackMode Mode
    {
        get => _mode;
        set => SetMode(value);
    }

    /// <summary>Sets the initial mode of a track that is not in a list of text tracks yet, without the steps of a change of the mode.</summary>
    internal void InitializeMode(TextTrackMode value) => _mode = value;

    internal void SetMode(TextTrackMode value)
    {
        if (_mode == value)
            return;

        var wasShowing = _mode == TextTrackMode.Showing;
        _mode = value;
        if (value == TextTrackMode.Disabled)
        {
            // The active flag of the cues is unset synchronously when the mode changes to disabled
            ClearActiveCues(wasShowing);
        }
        else if (wasShowing || value == TextTrackMode.Showing)
        {
            MediaElement?.OnCueRenderingChanged();
        }

        MediaElement?.OnTextTrackModeChanged();
        TrackElement?.OnTextTrackModeChanged();
    }

    /// <summary>
    /// Gets the cues of the track, in text track cue order (https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-cues),
    /// or null when the track is disabled. The list is live.
    /// </summary>
    [IdlMember("cues", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-cues")]
    public TextTrackCueList? Cues => _mode == TextTrackMode.Disabled ? null : field ??= new TextTrackCueList(this, activeOnly: false);

    /// <summary>
    /// Gets the cues of the track that are active (the current playback position of the media element is in their range), in
    /// text track cue order (https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-activecues), or null when the
    /// track is disabled. The list is live.
    /// </summary>
    [IdlMember("activeCues", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-activecues")]
    public TextTrackCueList? ActiveCues => _mode == TextTrackMode.Disabled ? null : field ??= new TextTrackCueList(this, activeOnly: true);

    /// <summary>
    /// Adds a cue to the list of cues of the track (https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-addcue);
    /// the cue is first removed from the track it belongs to.
    /// </summary>
    [IdlMember("addCue", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-addcue")]
    public void AddCue(TextTrackCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);

        // The rules for updating the text track rendering are the ones of WebVTT for every cue (there is no other kind of cue)
        cue.Track?.RemoveCueCore(cue);
        AddCueCore(cue);
        if (cue is VttCue { Region: { } region } && !Regions.Contains(region))
        {
            Regions.Add(region);
        }

        MediaElement?.OnCueAdded(cue);
    }

    /// <summary>Adds a cue of the resource of the track (the parser of the WebVTT file), without running the steps of a media element.</summary>
    internal void AddCueCore(TextTrackCue cue)
    {
        cue.Track = this;
        cue.Order = _nextCueOrder++;
        InsertSorted(cue);
        CuesVersion++;
    }

    /// <summary>
    /// Removes a cue from the list of cues of the track (https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-removecue).
    /// </summary>
    /// <exception cref="DomException">A <c>NotFoundError</c> when the cue is not in the list of cues of the track.</exception>
    [IdlMember("removeCue", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrack-removecue")]
    public void RemoveCue(TextTrackCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        if (!ReferenceEquals(cue.Track, this))
            throw DomException.NotFound("The cue is not in the list of cues of the text track");

        RemoveCueCore(cue);
    }

    private void RemoveCueCore(TextTrackCue cue)
    {
        _cues.Remove(cue);
        cue.Track = null;
        CuesVersion++;

        // The active flag is unset synchronously when the cue is removed from the list of cues of its track; the rendering
        // of a track that was showing is updated
        var wasActive = cue.IsActive;
        cue.SetActive(false);
        if (MediaElement is { } media)
        {
            if (wasActive && _mode == TextTrackMode.Showing)
            {
                media.OnCueRenderingChanged();
            }

            media.OnCueRemoved();
        }
    }

    /// <summary>
    /// Empties the list of cues of the track (the <c>src</c> attribute of its track element is set, changed or removed),
    /// with the regions and the style sheets of the previous resource.
    /// </summary>
    internal void ClearCues()
    {
        Regions.Clear();
        StyleSheets.Clear();
        if (_cues.Count == 0)
            return;

        var wasActive = false;
        foreach (var cue in _cues)
        {
            wasActive |= cue.IsActive;
            cue.Track = null;
            cue.SetActive(false);
        }

        _cues.Clear();
        CuesVersion++;
        if (MediaElement is { } media)
        {
            if (wasActive && _mode == TextTrackMode.Showing)
            {
                media.OnCueRenderingChanged();
            }

            media.OnCueRemoved();
        }
    }

    /// <summary>
    /// Unsets the active flag of the cues of the track (its mode changes to disabled, it is removed from the list of text
    /// tracks of its media element, or the ready state of the media element changes back to <c>HAVE_NOTHING</c>): the
    /// rendering of a track that was showing is updated.
    /// </summary>
    internal void ClearActiveCues(bool wasShowing)
    {
        var changed = false;
        foreach (var cue in _cues)
        {
            if (cue.IsActive)
            {
                cue.SetActive(false);
                changed = true;
            }
        }

        if (changed)
        {
            CuesVersion++;
            if (wasShowing)
            {
                MediaElement?.OnCueRenderingChanged();
            }
        }
    }

    /// <summary>The active flag of a cue of the track changed.</summary>
    internal void OnCueActiveChanged() => CuesVersion++;

    /// <summary>The start time or the end time of a cue of the track changed: the cue is moved to its place in the text track cue order.</summary>
    internal void OnCueTimesChanged(TextTrackCue cue)
    {
        _cues.Remove(cue);
        InsertSorted(cue);
        CuesVersion++;
        MediaElement?.OnCueTimesChanged();
    }

    /// <summary>
    /// The cues of a track are sorted by their start time, earliest first; then by their end time, latest first; then in the
    /// order they were last added to the list of cues, oldest first.
    /// </summary>
    private void InsertSorted(TextTrackCue cue)
    {
        var low = 0;
        var high = _cues.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (TextTrackCue.CompareOrder(_cues[middle], cue) <= 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        _cues.Insert(low, cue);
    }

    /// <summary>Gets or sets the event handler of the <c>cuechange</c> event.</summary>
    [IdlMember("oncuechange", "https://html.spec.whatwg.org/multipage/media.html#handler-texttrack-oncuechange")]
    public Action<Event>? OnCueChange
    {
        get => GetEventHandler("cuechange");
        set => SetEventHandler("cuechange", value);
    }
}

/// <summary>https://html.spec.whatwg.org/multipage/media.html#texttracklist</summary>
[IdlInterface("TextTrackList", "https://html.spec.whatwg.org/multipage/media.html#texttracklist")]
public sealed class TextTrackList : EventTarget, IReadOnlyList<TextTrack>
{
    // The text tracks are sorted as follows: the tracks of the track element children of the media element, in tree order;
    // the tracks added by addTextTrack(), oldest first; then the tracks of the media resource
    private readonly List<TextTrack> _elementTracks = [];
    private readonly List<TextTrack> _addedTracks = [];
    private readonly List<TextTrack> _resourceTracks = [];
    private List<TextTrack>? _tracks;

    private readonly SourceBuffer? _sourceBuffer;

    internal TextTrackList(HtmlMediaElement element)
    {
        Element = element;
    }

    /// <summary>
    /// Creates the list of the text tracks created by a source buffer (https://w3c.github.io/media-source/#dom-sourcebuffer-texttracks):
    /// it is always empty (the text tracks of the byte streams of the Media Source Extensions are not supported).
    /// </summary>
    internal TextTrackList(SourceBuffer sourceBuffer)
    {
        _sourceBuffer = sourceBuffer;
    }

    /// <summary>The media element of the list, or null for the list of a source buffer.</summary>
    internal HtmlMediaElement? Element { get; }

    /// <summary>The relevant global object of the list is the one of its media element (or of its source buffer).</summary>
    internal override GlobalScope? RelevantGlobal => Element?.RelevantGlobal ?? _sourceBuffer?.RelevantGlobal;

    private List<TextTrack> Tracks
    {
        get
        {
            if (_tracks is { } tracks)
                return tracks;

            tracks = new List<TextTrack>(_elementTracks.Count + _addedTracks.Count + _resourceTracks.Count);
            if (_elementTracks.Count > 0)
            {
                for (var child = Element?.FirstChild; child is not null; child = child.NextSibling)
                {
                    if (child is HtmlTrackElement track && _elementTracks.Contains(track.Track))
                    {
                        tracks.Add(track.Track);
                    }
                }
            }

            tracks.AddRange(_addedTracks);
            tracks.AddRange(_resourceTracks);
            return _tracks = tracks;
        }
    }

    /// <summary>The order of the track element children of the media element may have changed.</summary>
    internal void InvalidateOrder() => _tracks = null;

    /// <summary>Gets the number of tracks in the list.</summary>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/media.html#dom-texttracklist-length")]
    public uint Length => (uint)Tracks.Count;

    /// <summary>Gets the track at the given index, or null when the index is out of range.</summary>
    [IdlMember("getter", "https://html.spec.whatwg.org/multipage/media.html#dom-texttracklist-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public TextTrack? this[uint index] => index < (uint)Tracks.Count ? Tracks[(int)index] : null;

    /// <summary>Gets the track at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public TextTrack this[int index] => Tracks[index];

    /// <summary>Gets the number of tracks in the list.</summary>
    [NotInIdl]
    public int Count => Tracks.Count;

    /// <summary>Returns the first track of the list with the given identifier, or null.</summary>
    [IdlMember("getTrackById", "https://html.spec.whatwg.org/multipage/media.html#dom-texttracklist-gettrackbyid")]
    public TextTrack? GetTrackById(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Tracks.Find(track => track.Id == id);
    }

    /// <summary>Gets or sets the event handler of the <c>change</c> event.</summary>
    [IdlMember("onchange", "https://html.spec.whatwg.org/multipage/media.html#handler-texttracklist-onchange")]
    public Action<Event>? OnChange
    {
        get => GetEventHandler("change");
        set => SetEventHandler("change", value);
    }

    /// <summary>Gets or sets the event handler of the <c>addtrack</c> event.</summary>
    [IdlMember("onaddtrack", "https://html.spec.whatwg.org/multipage/media.html#handler-texttracklist-onaddtrack")]
    public Action<Event>? OnAddTrack
    {
        get => GetEventHandler("addtrack");
        set => SetEventHandler("addtrack", value);
    }

    /// <summary>Gets or sets the event handler of the <c>removetrack</c> event.</summary>
    [IdlMember("onremovetrack", "https://html.spec.whatwg.org/multipage/media.html#handler-texttracklist-onremovetrack")]
    public Action<Event>? OnRemoveTrack
    {
        get => GetEventHandler("removetrack");
        set => SetEventHandler("removetrack", value);
    }

    internal bool Contains(TextTrack track) => ReferenceEquals(track.MediaElement, Element) && (_elementTracks.Contains(track) || _addedTracks.Contains(track) || _resourceTracks.Contains(track));

    /// <summary>Adds the text track of a track element child of the media element.</summary>
    internal void AddElementTrack(TextTrack track)
    {
        track.MediaElement = Element;
        _elementTracks.Add(track);
        _tracks = null;
    }

    /// <summary>Adds a text track created by <c>addTextTrack()</c>.</summary>
    internal void AddAddedTrack(TextTrack track)
    {
        track.MediaElement = Element;
        _addedTracks.Add(track);
        _tracks = null;
    }

    /// <summary>Adds a text track of the media resource.</summary>
    internal void AddResourceTrack(TextTrack track)
    {
        track.MediaElement = Element;
        _resourceTracks.Add(track);
        _tracks = null;
    }

    /// <summary>Removes the text track of a track element that is no longer a child of the media element.</summary>
    internal bool RemoveElementTrack(TextTrack track)
    {
        if (!_elementTracks.Remove(track))
            return false;

        _tracks = null;
        return true;
    }

    /// <summary>Removes the text tracks of the media resource from the list, and returns them.</summary>
    internal List<TextTrack> TakeResourceTracks()
    {
        var tracks = new List<TextTrack>(_resourceTracks);
        _resourceTracks.Clear();
        _tracks = null;
        return tracks;
    }

    /// <inheritdoc/>
    [NotInIdl("IEnumerable implementation")]
    public IEnumerator<TextTrack> GetEnumerator() => Tracks.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>https://html.spec.whatwg.org/multipage/media.html#texttrackcuelist</summary>
[IdlInterface("TextTrackCueList", "https://html.spec.whatwg.org/multipage/media.html#texttrackcuelist")]
public sealed class TextTrackCueList : IReadOnlyList<TextTrackCue>
{
    private readonly TextTrack _track;
    private readonly bool _activeOnly;
    private List<TextTrackCue>? _activeCues;
    private int _activeCuesVersion = -1;

    internal TextTrackCueList(TextTrack track, bool activeOnly)
    {
        _track = track;
        _activeOnly = activeOnly;
    }

    private List<TextTrackCue> Cues
    {
        get
        {
            if (!_activeOnly)
                return _track.CueList;

            if (_activeCues is null || _activeCuesVersion != _track.CuesVersion)
            {
                _activeCues = _track.CueList.FindAll(static cue => cue.IsActive);
                _activeCuesVersion = _track.CuesVersion;
            }

            return _activeCues;
        }
    }

    /// <summary>Gets the number of cues in the list.</summary>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrackcuelist-length")]
    public uint Length => (uint)Cues.Count;

    /// <summary>Gets the cue at the given index (the cues are in text track cue order), or null when the index is out of range.</summary>
    [IdlMember("getter", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrackcuelist-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public TextTrackCue? this[uint index] => index < (uint)Cues.Count ? Cues[(int)index] : null;

    /// <summary>Gets the cue at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public TextTrackCue this[int index] => Cues[index];

    /// <summary>Gets the number of cues in the list.</summary>
    [NotInIdl]
    public int Count => Cues.Count;

    /// <summary>
    /// Returns the first cue of the list (in text track cue order) with the given identifier, or null when there is none or
    /// when the identifier is the empty string.
    /// </summary>
    [IdlMember("getCueById", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrackcuelist-getcuebyid")]
    public TextTrackCue? GetCueById(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id.Length == 0 ? null : Cues.Find(cue => cue.Id == id);
    }

    /// <inheritdoc/>
    [NotInIdl("IEnumerable implementation")]
    public IEnumerator<TextTrackCue> GetEnumerator() => Cues.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// https://html.spec.whatwg.org/multipage/media.html#texttrackcue: the unit of time-sensitive data of a text track. The
/// cues are <see cref="VttCue"/> objects.
/// </summary>
[IdlInterface("TextTrackCue", "https://html.spec.whatwg.org/multipage/media.html#texttrackcue")]
public abstract class TextTrackCue : EventTarget
{
    private double _startTime;
    private double _endTime;

    private protected TextTrackCue(double startTime, double endTime)
    {
        _startTime = startTime;
        _endTime = endTime;
    }

    /// <summary>The text track cue active flag (https://html.spec.whatwg.org/multipage/media.html#text-track-cue-active-flag).</summary>
    internal bool IsActive { get; private set; }

    /// <summary>The order in which the cue was last added to the list of cues of its track.</summary>
    internal long Order { get; set; }

    /// <summary>Sets the active flag of the cue; the text track cue display state is emptied when the flag is unset.</summary>
    internal void SetActive(bool value)
    {
        if (IsActive == value)
            return;

        IsActive = value;
        if (!value)
        {
            OnDisplayStateEmptied();
        }
    }

    /// <summary>The text track cue display state is emptied (https://html.spec.whatwg.org/multipage/media.html#text-track-cue-display-state).</summary>
    private protected virtual void OnDisplayStateEmptied()
    {
    }

    /// <summary>The text track cue order of two cues of the same track.</summary>
    internal static int CompareOrder(TextTrackCue x, TextTrackCue y)
    {
        var result = x._startTime.CompareTo(y._startTime);
        if (result != 0)
            return result;

        result = y._endTime.CompareTo(x._endTime);
        return result != 0 ? result : x.Order.CompareTo(y.Order);
    }

    /// <summary>Gets the text track the cue belongs to, or null (https://html.spec.whatwg.org/multipage/media.html#dom-texttrackcue-track).</summary>
    [IdlMember("track", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrackcue-track")]
    public TextTrack? Track { get; internal set; }

    /// <summary>Gets or sets the identifier of the cue (https://html.spec.whatwg.org/multipage/media.html#dom-texttrackcue-id).</summary>
    [IdlMember("id", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrackcue-id")]
    public string Id
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
            OnCueChanged();
        }
    } = "";

    /// <summary>Gets or sets the start time of the cue, in seconds (https://html.spec.whatwg.org/multipage/media.html#dom-texttrackcue-starttime).</summary>
    [IdlMember("startTime", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrackcue-starttime")]
    public double StartTime
    {
        get => _startTime;
        set
        {
            if (!double.IsFinite(value))
                throw new ArgumentException("The start time of a cue must be a finite number", nameof(value));

            if (_startTime == value)
                return;

            _startTime = value;
            Track?.OnCueTimesChanged(this);
        }
    }

    /// <summary>
    /// Gets or sets the end time of the cue, in seconds (https://html.spec.whatwg.org/multipage/media.html#dom-texttrackcue-endtime):
    /// positive infinity for an unbounded cue.
    /// </summary>
    /// <exception cref="ArgumentException">The value is negative infinity or NaN.</exception>
    [IdlMember("endTime", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrackcue-endtime")]
    public double EndTime
    {
        get => _endTime;
        set
        {
            if (double.IsNaN(value) || double.IsNegativeInfinity(value))
                throw new ArgumentException("The end time of a cue cannot be negative infinity or NaN", nameof(value));

            if (_endTime == value)
                return;

            _endTime = value;
            Track?.OnCueTimesChanged(this);
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the playback of the media resource pauses when the end of the cue is reached
    /// (https://html.spec.whatwg.org/multipage/media.html#dom-texttrackcue-pauseonexit).
    /// </summary>
    [IdlMember("pauseOnExit", "https://html.spec.whatwg.org/multipage/media.html#dom-texttrackcue-pauseonexit")]
    public bool PauseOnExit { get; set; }

    /// <summary>Gets or sets the event handler of the <c>enter</c> event.</summary>
    [IdlMember("onenter", "https://html.spec.whatwg.org/multipage/media.html#handler-texttrackcue-onenter")]
    public Action<Event>? OnEnter
    {
        get => GetEventHandler("enter");
        set => SetEventHandler("enter", value);
    }

    /// <summary>Gets or sets the event handler of the <c>exit</c> event.</summary>
    [IdlMember("onexit", "https://html.spec.whatwg.org/multipage/media.html#handler-texttrackcue-onexit")]
    public Action<Event>? OnExit
    {
        get => GetEventHandler("exit");
        set => SetEventHandler("exit", value);
    }

    /// <summary>A property of the cue that its rendering depends on changed: the display of an active cue of a showing track is updated.</summary>
    private protected void OnCueChanged()
    {
        OnDisplayStateEmptied();
        if (IsActive && Track is { Mode: TextTrackMode.Showing, MediaElement: { } media })
        {
            media.OnCueRenderingChanged();
        }
    }
}
