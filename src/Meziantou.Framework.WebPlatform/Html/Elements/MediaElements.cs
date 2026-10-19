using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

// The media elements (https://html.spec.whatwg.org/multipage/media.html#media-elements). The processing model is
// implemented by the library on top of the players of the media backend of the browsing context
// (BrowsingContextOptions.MediaBackend), which reads the media resources and holds the playback position:
// - MediaElements.cs: the state of the elements and the members that do not depend on the media resource;
// - MediaElements.Loading.cs: the load algorithm, the resource selection algorithm and the resource fetch algorithm;
// - MediaElements.Playback.cs: the ready states, play() and pause(), the playback position, seeking and the end of playback;
// - MediaTracks.cs: the lists of audio and video tracks;
// - MediaElements.TextTracks.cs, TextTracks.cs and TrackElement.cs: the text tracks and their cues.
// Without a media backend, the elements have no media resource: nothing is fetched and no media event is fired.

/// <summary>https://html.spec.whatwg.org/multipage/media.html#timeranges</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/media.html#timeranges"/>
[IdlInterface("TimeRanges", "https://html.spec.whatwg.org/multipage/media.html#timeranges")]
public sealed class TimeRanges
{
    private readonly MediaTimeRange[] _ranges;

    internal TimeRanges()
    {
        _ranges = [];
    }

    /// <summary>
    /// Creates a normalized TimeRanges object (https://html.spec.whatwg.org/multipage/media.html#normalized-timeranges-object):
    /// the ranges are ordered, and the ranges that overlap or touch are merged.
    /// </summary>
    internal TimeRanges(IEnumerable<MediaTimeRange> ranges)
    {
        var sorted = new List<MediaTimeRange>();
        foreach (var range in ranges)
        {
            if (!double.IsNaN(range.Start) && !double.IsNaN(range.End) && range.End >= range.Start)
            {
                sorted.Add(range);
            }
        }

        sorted.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        var normalized = new List<MediaTimeRange>(sorted.Count);
        foreach (var range in sorted)
        {
            if (normalized.Count > 0 && range.Start <= normalized[^1].End)
            {
                normalized[^1] = new MediaTimeRange(normalized[^1].Start, Math.Max(normalized[^1].End, range.End));
            }
            else
            {
                normalized.Add(range);
            }
        }

        _ranges = [.. normalized];
    }

    internal ReadOnlySpan<MediaTimeRange> Ranges => _ranges;

    /// <summary>Gets the number of ranges in the object.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-timeranges-length"/>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/media.html#dom-timeranges-length")]
    public uint Length => (uint)_ranges.Length;

    /// <summary>
    /// Returns the start time, in seconds, of the range at the given index; throws when the index is out of range.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-timeranges-start"/>
    [IdlMember("start", "https://html.spec.whatwg.org/multipage/media.html#dom-timeranges-start")]
    public double Start(uint index) => index < (uint)_ranges.Length ? _ranges[index].Start : throw DomException.IndexSize($"The index {index} is out of range");

    /// <summary>
    /// Returns the end time, in seconds, of the range at the given index; throws when the index is out of range.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-timeranges-end"/>
    [IdlMember("end", "https://html.spec.whatwg.org/multipage/media.html#dom-timeranges-end")]
    public double End(uint index) => index < (uint)_ranges.Length ? _ranges[index].End : throw DomException.IndexSize($"The index {index} is out of range");
}

/// <summary>https://html.spec.whatwg.org/multipage/media.html#mediaerror</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/media.html#mediaerror"/>
[IdlInterface("MediaError", "https://html.spec.whatwg.org/multipage/media.html#mediaerror")]
public sealed class MediaError
{
    /// <summary>The fetching of the media resource was aborted by the user.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-mediaerror-media_err_aborted"/>
    [IdlMember("MEDIA_ERR_ABORTED", "https://html.spec.whatwg.org/multipage/media.html#dom-mediaerror-media_err_aborted")]
    public const ushort MediaErrAborted = 1;

    /// <summary>A network error occurred while fetching the media resource.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-mediaerror-media_err_network"/>
    [IdlMember("MEDIA_ERR_NETWORK", "https://html.spec.whatwg.org/multipage/media.html#dom-mediaerror-media_err_network")]
    public const ushort MediaErrNetwork = 2;

    /// <summary>An error occurred while decoding the media resource.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-mediaerror-media_err_decode"/>
    [IdlMember("MEDIA_ERR_DECODE", "https://html.spec.whatwg.org/multipage/media.html#dom-mediaerror-media_err_decode")]
    public const ushort MediaErrDecode = 3;

    /// <summary>The media resource is not supported.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-mediaerror-media_err_src_not_supported"/>
    [IdlMember("MEDIA_ERR_SRC_NOT_SUPPORTED", "https://html.spec.whatwg.org/multipage/media.html#dom-mediaerror-media_err_src_not_supported")]
    public const ushort MediaErrSrcNotSupported = 4;

    internal MediaError(ushort code, string message)
    {
        Code = code;
        Message = message;
    }

    /// <summary>Gets the code of the error.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-mediaerror-code"/>
    [IdlMember("code", "https://html.spec.whatwg.org/multipage/media.html#dom-mediaerror-code")]
    public ushort Code { get; }

    /// <summary>Gets a message that describes the error.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-mediaerror-message"/>
    [IdlMember("message", "https://html.spec.whatwg.org/multipage/media.html#dom-mediaerror-message")]
    public string Message { get; }
}

public partial class HtmlMediaElement
{
    private const double MinimumAudiblePlaybackRate = 0.25;
    private const double MaximumAudiblePlaybackRate = 4;

    private ushort _networkState = NetworkEmpty;
    private ushort _readyState = HaveNothing;
    private MediaError? _error;
    private string _currentSrc = "";
    private object? _srcObject;
    private double _defaultPlaybackRate = 1;
    private double _playbackRate = 1;
    private double _volume = 1;
    private bool _preservesPitch = true;
    private bool? _muted;

    /// <summary>The player of the media resource of the element, once its metadata is known (the ready state is not <c>HAVE_NOTHING</c>), or null.</summary>
    private MediaPlayer? _player;

    /// <summary>
    /// The media backend of the browsing context of the document (or of the document that created it, for a document without
    /// browsing context, e.g. <c>createHTMLDocument()</c>), or null: the element then has no media resource.
    /// </summary>
    private MediaBackend? Backend => (NodeDocument.BrowsingContext ?? NodeDocument.OriginDocument?.BrowsingContext)?.Options.MediaBackend;

    /// <summary>The player of the media resource, or null (see <see cref="HtmlVideoElement"/>, which paints its frames).</summary>
    internal MediaPlayer? Player => _player;

    // ----------------------------------------------------------------------------------------------------------------
    // Media element event task source (https://html.spec.whatwg.org/multipage/media.html#media-element-event-task-source)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>A task of the media element event task source: the load algorithm removes the pending tasks, and settles the promises they would settle.</summary>
    private sealed class MediaTask(Action run, Action? settlePromises)
    {
        public Action Run { get; } = run;

        public Action? SettlePromises { get; } = settlePromises;

        public bool IsCanceled { get; set; }
    }

    private List<MediaTask>? _mediaTasks;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/media.html#queue-a-media-element-task. <paramref name="settlePromises"/>
    /// resolves or rejects the pending play promises of the task when the load algorithm removes it before it runs (the task
    /// itself settles them otherwise).
    /// </summary>
    private void QueueMediaElementTask(Action run, Action? settlePromises = null)
    {
        var task = new MediaTask(run, settlePromises);
        (_mediaTasks ??= []).Add(task);
        NodeDocument.QueueElementTask(() =>
        {
            if (task.IsCanceled)
                return;

            _mediaTasks.Remove(task);
            task.Run();
        });
    }

    /// <summary>Queues a media element task to fire an event at the element.</summary>
    private void QueueMediaEvent(string type) => QueueMediaElementTask(() => FireEvent(new Event(type)));

    /// <summary>
    /// The pending tasks of the media element event task source are removed by the load algorithm
    /// (https://html.spec.whatwg.org/multipage/media.html#media-element-load-algorithm): the promises they would resolve or
    /// reject are settled immediately, in the order the tasks were queued.
    /// </summary>
    private void RemovePendingMediaTasks()
    {
        if (_mediaTasks is not { Count: > 0 } tasks)
            return;

        var pending = tasks.ToArray();
        tasks.Clear();
        foreach (var task in pending)
        {
            task.IsCanceled = true;
        }

        foreach (var task in pending)
        {
            task.SettlePromises?.Invoke();
        }
    }

    /// <summary>
    /// Awaits a stable state (https://html.spec.whatwg.org/multipage/webappapis.html#await-a-stable-state): the synchronous
    /// section runs in a microtask, as specified (the browsers run the synchronous sections of the resource selection
    /// algorithm later, each in its own way: e.g. a script that follows the element in the markup runs first; the tests of
    /// the algorithm in WPT expect the specified timing). Returns false when the document has no event loop (nothing runs).
    /// </summary>
    private bool AwaitStableState(Action synchronousSection)
    {
        if (NodeDocument.EventLoop is not { } eventLoop)
            return false;

        eventLoop.QueueMicrotask(synchronousSection);
        return true;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Error, network state and location of the media resource
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Gets the last error of the element since its resource selection algorithm was last invoked, or null (https://html.spec.whatwg.org/multipage/media.html#dom-media-error).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-media-error"/>
    [IdlMember("error", "https://html.spec.whatwg.org/multipage/media.html#dom-media-error")]
    public MediaError? Error => _error;

    /// <summary>
    /// Gets the absolute URL of the current media resource (https://html.spec.whatwg.org/multipage/media.html#dom-media-currentsrc),
    /// or an empty string when there is none, or when it is a media provider object.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-media-currentsrc"/>
    [IdlMember("currentSrc", "https://html.spec.whatwg.org/multipage/media.html#dom-media-currentsrc")]
    public string CurrentSrc => _currentSrc;

    /// <summary>
    /// Gets or sets the assigned media provider object (https://html.spec.whatwg.org/multipage/media.html#dom-media-srcobject):
    /// a <see cref="Blob"/> that contains the media resource, a <see cref="MediaSource"/>, or the
    /// <see cref="MediaSourceHandle"/> of a media source constructed in a dedicated worker (<c>MediaStream</c> is not
    /// implemented). It takes priority over the <c>src</c> attribute and the <c>source</c> elements. Setting it runs the
    /// media element load algorithm.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a media provider object.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-media-srcobject"/>
    [IdlMember("srcObject", "https://html.spec.whatwg.org/multipage/media.html#dom-media-srcobject")]
    public object? SrcObject
    {
        get => _srcObject;
        set
        {
            if (value is not (null or Blob or MediaSource or MediaSourceHandle))
                throw new ArgumentException("The value must be a Blob, a MediaSource or a MediaSourceHandle", nameof(value));

            // A handle that was assigned to srcObject cannot be transferred anymore
            if (value is MediaSourceHandle handle)
            {
                handle.HasEverBeenAssignedAsSrcObject = true;
            }

            _srcObject = value;
            if (Backend is not null)
            {
                RunLoadAlgorithm();
            }
        }
    }

    /// <summary>Gets the current state of the network activity of the element (https://html.spec.whatwg.org/multipage/media.html#dom-media-networkstate).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-media-networkstate"/>
    [IdlMember("networkState", "https://html.spec.whatwg.org/multipage/media.html#dom-media-networkstate")]
    public ushort NetworkState => _networkState;

    private void SetNetworkState(ushort value)
    {
        if (_networkState == value)
            return;

        _networkState = value;

        // :buffering and :stalled depend on the network state
        NodeDocument.IncrementElementStateVersion();
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/media.html#dom-navigator-canplaytype: the answer of the media backend
    /// (<see cref="MediaBackend.CanPlayType"/>) for a valid MIME type other than <c>application/octet-stream</c>, else the
    /// empty string. Without a media backend, no media type can be played.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-navigator-canplaytype"/>
    [IdlMember("canPlayType", "https://html.spec.whatwg.org/multipage/media.html#dom-navigator-canplaytype")]
    public string CanPlayType(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return GetTypeSupport(type) switch
        {
            MediaTypeSupport.Probably => "probably",
            MediaTypeSupport.Maybe => "maybe",
            _ => "",
        };
    }

    /// <summary>
    /// How confident the media backend is that it can play a type. A string that is not a valid MIME type, and
    /// <c>application/octet-stream</c> without parameters, are not supported for <c>canPlayType()</c>.
    /// </summary>
    private MediaTypeSupport GetTypeSupport(string type)
    {
        if (Backend is not { } backend || MimeType.Parse(type) is not { } mimeType)
            return MediaTypeSupport.NotSupported;

        if (mimeType.Essence == "application/octet-stream" && mimeType.Parameters.Count == 0)
            return MediaTypeSupport.NotSupported;

        string[] codecs = [];
        if (mimeType.GetParameter("codecs") is { } parameter)
        {
            codecs = parameter.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }

        return backend.CanPlayType(mimeType.Essence, codecs);
    }

    /// <summary>
    /// Whether the type of a <c>source</c> element is a type that the user agent knows it cannot render
    /// (https://html.spec.whatwg.org/multipage/media.html#a-type-that-the-user-agent-knows-it-cannot-render).
    /// <c>application/octet-stream</c> without parameters is never such a type.
    /// </summary>
    private bool IsTypeKnownToBeUnsupported(string type)
    {
        if (MimeType.Parse(type) is not { } mimeType)
            return true;

        if (mimeType.Essence == "application/octet-stream" && mimeType.Parameters.Count == 0)
            return false;

        return GetTypeSupport(type) == MediaTypeSupport.NotSupported;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Playback rate, volume
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Gets or sets the default playback rate of the media (https://html.spec.whatwg.org/multipage/media.html#dom-media-defaultplaybackrate).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-media-defaultplaybackrate"/>
    [IdlMember("defaultPlaybackRate", "https://html.spec.whatwg.org/multipage/media.html#dom-media-defaultplaybackrate")]
    public double DefaultPlaybackRate
    {
        get => _defaultPlaybackRate;
        set
        {
            if (_defaultPlaybackRate == value)
                return;

            _defaultPlaybackRate = value;
            QueueRateChange();
        }
    }

    /// <summary>
    /// Gets or sets the effective playback rate of the media (https://html.spec.whatwg.org/multipage/media.html#dom-media-playbackrate).
    /// The setter throws a <c>NotSupportedError</c> when the player does not support the rate
    /// (<see cref="MediaPlayer.SupportsPlaybackRate"/>).
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-media-playbackrate"/>
    [IdlMember("playbackRate", "https://html.spec.whatwg.org/multipage/media.html#dom-media-playbackrate")]
    public double PlaybackRate
    {
        get => _playbackRate;
        set
        {
            if (_player is { } player && !player.SupportsPlaybackRate(value))
                throw DomException.NotSupported("The playback rate is not supported");

            if (_playbackRate == value)
                return;

            SetPlaybackRate(value);
            QueueRateChange();
        }
    }

    private void SetPlaybackRate(double value)
    {
        // The position reached at the previous rate, before the direction of playback may change
        UpdateCurrentPlaybackPosition();
        _playbackRate = value;
        if (_player is { } player)
        {
            player.PlaybackRate = value;
        }

        // An extreme playbackRate mutes the element (:muted)
        NodeDocument.IncrementElementStateVersion();
        UpdatePlayerVolume();
        UpdatePlayback();
    }

    private void QueueRateChange()
    {
        if (Backend is not null)
        {
            QueueMediaEvent("ratechange");
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the pitch of the audio is preserved when the playback rate changes.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-media-preservespitch"/>
    [IdlMember("preservesPitch", "https://html.spec.whatwg.org/multipage/media.html#dom-media-preservespitch")]
    public bool PreservesPitch
    {
        get => _preservesPitch;
        set
        {
            _preservesPitch = value;
            _player?.SetPreservesPitch(value);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#dom-media-volume</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-media-volume"/>
    [IdlMember("volume", "https://html.spec.whatwg.org/multipage/media.html#dom-media-volume")]
    public double Volume
    {
        get => _volume;
        set
        {
            if (value is < 0 or > 1)
                throw DomException.IndexSize("The volume must be in the range [0, 1]");

            if (_volume == value)
                return;

            _volume = value;
            UpdatePlayerVolume();
            QueueVolumeChange();
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/media.html#dom-media-muted: whether the element is muted (see <see cref="IsMuted"/>).
    /// The setter sets the muted state (initially "default": the muted content attribute).
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-media-muted"/>
    [IdlMember("muted", "https://html.spec.whatwg.org/multipage/media.html#dom-media-muted")]
    public bool Muted
    {
        get => IsMuted;
        set
        {
            var changed = (_muted ?? HasAttributeWithLocalName("muted")) != value;
            _muted = value;
            NodeDocument.IncrementElementStateVersion();
            if (changed)
            {
                UpdatePlayerVolume();
                QueueVolumeChange();
            }
        }
    }

    private void QueueVolumeChange()
    {
        if (Backend is not null)
        {
            QueueMediaEvent("volumechange");
        }
    }

    /// <summary>
    /// Whether the media element is muted (https://html.spec.whatwg.org/multipage/media.html#concept-media-muted): its muted
    /// state is true (or "default" and it has a muted content attribute), the direction of playback is backwards, or its
    /// playbackRate is so low or so high that the audio cannot be played usefully: outside [0.25, 4], as in Firefox.
    /// </summary>
    internal bool IsMuted => (_muted ?? HasAttributeWithLocalName("muted")) || _playbackRate is < MinimumAudiblePlaybackRate or > MaximumAudiblePlaybackRate;

    /// <summary>The player plays the audio at the effective media volume (https://html.spec.whatwg.org/multipage/media.html#effective-media-volume).</summary>
    private void UpdatePlayerVolume() => _player?.SetVolume(IsMuted ? 0 : _volume);

    // ----------------------------------------------------------------------------------------------------------------
    // Tracks
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Gets the audio tracks of the media resource (https://html.spec.whatwg.org/multipage/media.html#dom-media-audiotracks).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-media-audiotracks"/>
    [IdlMember("audioTracks", "https://html.spec.whatwg.org/multipage/media.html#dom-media-audiotracks")]
    public AudioTrackList AudioTracks => field ??= new AudioTrackList(this);

    /// <summary>Gets the video tracks of the media resource (https://html.spec.whatwg.org/multipage/media.html#dom-media-videotracks).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-media-videotracks"/>
    [IdlMember("videoTracks", "https://html.spec.whatwg.org/multipage/media.html#dom-media-videotracks")]
    public VideoTrackList VideoTracks => field ??= new VideoTrackList(this);

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/media.html#forget-the-media-element's-media-resource-specific-tracks: the lists
    /// of audio and video tracks are emptied and the text tracks of the media resource are removed from the list of text
    /// tracks, and no event is fired.
    /// </summary>
    private void ForgetMediaResourceSpecificTracks()
    {
        AudioTracks.Clear();
        VideoTracks.Clear();
        ForgetMediaResourceSpecificTextTracks();
    }

    /// <summary>Queues a media element task to fire a <c>change</c> event at a list of tracks.</summary>
    internal void QueueTrackListChange(EventTarget list) => QueueMediaElementTask(() => list.FireEvent(new Event("change")));

    /// <summary>The enabled state of an audio track of the resource changed.</summary>
    internal void OnAudioTrackEnabledChanged(AudioTrack track)
    {
        _player?.SetAudioTrackEnabled(track.Info, track.Enabled);
        OnMediaSourceTrackStateChanged();
    }

    /// <summary>The selected video track of the resource changed: the natural dimensions of the video may change.</summary>
    internal void OnSelectedVideoTrackChanged(VideoTrack? track)
    {
        OnMediaSourceTrackStateChanged();
        if (_player is not { } player)
            return;

        player.SelectVideoTrack(track?.Info);
        UpdateVideoDimensions();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Attributes and tree mutations
    // ----------------------------------------------------------------------------------------------------------------

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is not null)
            return;

        switch (localName)
        {
            case "src":
                // If a src attribute of a media element is set or changed, the user agent must invoke the load algorithm
                // (removing the attribute does not, even if there are source elements)
                if (value is not null && Backend is not null)
                {
                    // The media source of a blob: URL is the one the URL references when the attribute is set
                    _srcMediaSource = value.Length == 0 ? null : ResolveMediaSourceUrl(Reflection.EncodingParseUrl(NodeDocument, value));
                    _srcDocument = NodeDocument;
                    RunLoadAlgorithm();
                }

                break;

            case "loading":
                LazyLoading.OnLoadingAttributeChanged(this, oldValue, value);
                OnLoadingAttributeChanged(oldValue, value);
                break;

            case "preload" or "autoplay":
                // The fetch deferred by preload="none" starts once the hint no longer applies
                if (!IsFetchDeferredByPreload())
                {
                    ResumeDeferredFetch();
                }

                break;

            case "muted" when _muted is null:
                NodeDocument.IncrementElementStateVersion();
                break;

        }
    }

    /// <summary>The steps of the subclasses when the lazy loading attribute changes (the poster of a video).</summary>
    private protected virtual void OnLoadingAttributeChanged(string? oldValue, string? value)
    {
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/media.html#playing-the-media-resource: "When a media element is removed from a
    /// Document": the element is paused at the next stable state unless it is in a document again.
    /// </summary>
    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        if (Backend is null || !oldAncestor.IsConnected || oldAncestor.GetRoot() is not Document)
            return;

        AwaitStableState(() =>
        {
            if (IsInDocument)
                return;

            InternalPauseSteps();
        });
    }

    /// <summary>
    /// An element created in a document that has no media backend (e.g. the inert document of the contents of a
    /// <c>template</c> element, where nothing is fetched) selects its resource when it is adopted by a document that has
    /// one, as if it was created there: the specification invokes the resource selection algorithm when the element is
    /// created with a <c>src</c> attribute or when a <c>source</c> element is inserted. Its URLs are resolved relative to
    /// its new document, as in the browsers.
    /// </summary>
    internal override void AdoptingSteps(Document oldDocument)
    {
        base.AdoptingSteps(oldDocument);
        if (_networkState != NetworkEmpty || Backend is null || (oldDocument.BrowsingContext ?? oldDocument.OriginDocument?.BrowsingContext)?.Options.MediaBackend is not null)
            return;

        if (HasAttributeWithLocalName("src"))
        {
            _srcDocument = null;
            RunLoadAlgorithm();
        }
        else if (_srcObject is not null)
        {
            RunLoadAlgorithm();
        }
        else
        {
            for (var child = FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is HtmlSourceElement)
                {
                    InvokeResourceSelection();
                    break;
                }
            }
        }
    }

    /// <summary>Whether the element is in a document (https://dom.spec.whatwg.org/#in-a-document): its root is a document.</summary>
    private bool IsInDocument => IsConnected && GetRoot() is Document;

    internal override void ChildrenChangedSteps(bool childrenInserted)
    {
        base.ChildrenChangedSteps(childrenInserted);
        UpdateSourcePointer();
    }
}

public partial class HtmlSourceElement
{
    /// <summary>
    /// The steps of a <c>source</c> element inserted in (or moved to) a media element
    /// (https://html.spec.whatwg.org/multipage/embedded-content.html#the-source-element): the resource selection algorithm of
    /// a media element that has no <c>src</c> attribute and whose network state is <c>NETWORK_EMPTY</c> is invoked.
    /// </summary>
    private void InvokeResourceSelectionOfParentMediaElement()
    {
        if (ParentNode is HtmlMediaElement media)
        {
            media.OnSourceElementInserted();
        }
    }
}
