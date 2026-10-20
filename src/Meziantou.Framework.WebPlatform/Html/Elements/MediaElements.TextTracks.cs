using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

// The text tracks of a media element (https://html.spec.whatwg.org/multipage/media.html#timed-text-tracks): its list of
// text tracks, the automatic track selection, the list of pending text tracks, and the cues of the "time marches on" steps
// (the active flag of the cues, the enter, exit and cuechange events, the pause-on-exit flag).
public partial class HtmlMediaElement
{
    private TextTrackList? _textTracks;

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#list-of-newly-introduced-cues</summary>
    private HashSet<TextTrackCue>? _newlyIntroducedCues;

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#list-of-pending-text-tracks</summary>
    private List<TextTrack>? _pendingTextTracks;

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#blocked-on-parser</summary>
    private bool _blockedOnParser;

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#did-perform-automatic-track-selection</summary>
    private bool _didPerformAutomaticTrackSelection;

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#pending-text-track-change-notification-flag</summary>
    private bool _pendingTextTrackChangeNotification;

    /// <summary>The current playback position the last time the "time marches on" steps were run, or NaN.</summary>
    private double _lastTimeMarchesOnPosition = double.NaN;

    /// <summary>The missed cues of the last run of the "time marches on" steps.</summary>
    private List<TextTrackCue>? _missedCues;

    /// <summary>Whether the "time marches on" steps are running (the steps that run them again wait for their completion).</summary>
    private bool _isProcessingCues;
    private bool _processCuesAgain;

    /// <summary>Gets the text tracks of the media element (https://html.spec.whatwg.org/multipage/media.html#dom-media-texttracks).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-media-texttracks"/>
    [IdlMember("textTracks", "https://html.spec.whatwg.org/multipage/media.html#dom-media-texttracks")]
    public TextTrackList TextTracks => _textTracks ??= new TextTrackList(this);

    /// <summary>
    /// Creates a text track and adds it to the list of text tracks of the media element
    /// (https://html.spec.whatwg.org/multipage/media.html#dom-media-addtexttrack): the track is loaded, its mode is
    /// <see cref="TextTrackMode.Hidden"/> and its list of cues is empty. An <c>addtrack</c> event is fired at the list.
    /// </summary>
    /// <param name="kind">The kind of the track.</param>
    /// <param name="label">The label of the track.</param>
    /// <param name="language">The language of the track (a BCP 47 language tag).</param>
    /// <returns>The new track.</returns>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-media-addtexttrack"/>
    [IdlMember("addTextTrack", "https://html.spec.whatwg.org/multipage/media.html#dom-media-addtexttrack")]
    public TextTrack AddTextTrack(TextTrackKind kind, string label = "", string language = "")
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(language);
        var track = new TextTrack(this, kind, label, language);
        track.InitializeMode(TextTrackMode.Hidden);
        TextTracks.AddAddedTrack(track);
        QueueMediaElementTask(() => TextTracks.FireEvent(new TrackEvent("addtrack", new TrackEventInit { Track = track })));
        return track;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The text tracks of the track elements and of the media resource
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The parent of a track element changed and its new parent is the media element
    /// (https://html.spec.whatwg.org/multipage/media.html#sourcing-out-of-band-text-tracks): its text track is added to the
    /// list of text tracks, an <c>addtrack</c> event is fired, and the automatic track selection is performed.
    /// </summary>
    internal void OnTrackElementInserted(HtmlTrackElement element)
    {
        var track = element.Track;
        TextTracks.AddElementTrack(track);
        QueueMediaElementTask(() => TextTracks.FireEvent(new TrackEvent("addtrack", new TrackEventInit { Track = track })));
        QueueMediaElementTask(() =>
        {
            if (_blockedOnParser || _didPerformAutomaticTrackSelection)
                return;

            HonorUserPreferencesForAutomaticTextTrackSelection();

            // As in the browsers, the tracks that are enabled while the media resource is not ready to play yet are pending
            // text tracks: the specification only populates the list when the resource selection algorithm starts, which
            // is before this task when the track element and the src attribute are set by the same script, so the
            // playback would start before the default track is loaded (WPT autoplay-with-slow-text-tracks.html)
            if (_networkState != NetworkEmpty && _readyState < HaveFutureData)
            {
                PopulatePendingTextTracks();
            }
        });

        // All the cues of the track are added to the list of newly introduced cues
        IntroduceCues(track.CueList);
    }

    /// <summary>The parent of a track element changed and its old parent was the media element: its text track is removed from the list of text tracks.</summary>
    internal void OnTrackElementRemoved(HtmlTrackElement element)
    {
        var track = element.Track;
        if (_textTracks is not { } list || !list.RemoveElementTrack(track))
            return;

        // The active flag of the cues is unset synchronously when the track is removed from the list of text tracks
        track.ClearActiveCues(track.Mode == TextTrackMode.Showing);
        track.MediaElement = null;
        RemovePendingTextTrack(track);
        QueueMediaElementTask(() => list.FireEvent(new TrackEvent("removetrack", new TrackEventInit { Track = track })));
        if (!_showPoster)
        {
            TimeMarchesOn(normalPlayback: false);
        }
    }

    /// <summary>
    /// Exposes the text tracks of the media resource (https://html.spec.whatwg.org/multipage/media.html#steps-to-expose-a-media-resource-specific-text-track):
    /// the tracks the player of the media backend reports, with their cues. The tracks are loaded and disabled.
    /// </summary>
    private void ExposeMediaResourceSpecificTextTracks(MediaPlayer player)
    {
        foreach (var info in player.TextTracks)
        {
            var track = new TextTrack(this, info.Kind, info.Label, info.Language, info.Id, info.Kind is TextTrackKind.Chapters or TextTrackKind.Metadata ? info.InBandMetadataTrackDispatchType : "");
            foreach (var cueInfo in info.Cues)
            {
                if (double.IsNaN(cueInfo.EndTime) || double.IsNegativeInfinity(cueInfo.EndTime) || !double.IsFinite(cueInfo.StartTime))
                    continue;

                var cue = new VttCue(cueInfo.StartTime, cueInfo.EndTime, cueInfo.Text) { Id = cueInfo.Id };
                WebVtt.WebVttParser.ParseCueSettings(cueInfo.Settings, track.Regions, cue);
                track.AddCueCore(cue);
            }

            TextTracks.AddResourceTrack(track);
            TextTracks.FireEvent(new TrackEvent("addtrack", new TrackEventInit { Track = track }));
        }
    }

    /// <summary>The text tracks of the media resource are removed from the list of text tracks, and no event is fired.</summary>
    private void ForgetMediaResourceSpecificTextTracks()
    {
        if (_textTracks is not { } list)
            return;

        foreach (var track in list.TakeResourceTracks())
        {
            track.ClearActiveCues(track.Mode == TextTrackMode.Showing);
            track.MediaElement = null;
        }
    }

    /// <summary>The ready state of the element changed back to <c>HAVE_NOTHING</c>: the active flag of all the cues is unset.</summary>
    private void ClearActiveCues()
    {
        _lastTimeMarchesOnPosition = double.NaN;
        if (_textTracks is not { } list)
            return;

        foreach (var track in list)
        {
            track.ClearActiveCues(track.Mode == TextTrackMode.Showing);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Automatic text track selection, pending text tracks
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The element is created by an HTML parser or an XML parser: its blocked-on-parser flag is set until it is popped off the stack of open elements.</summary>
    internal void OnCreatedByParser() => _blockedOnParser = true;

    /// <summary>
    /// The element is popped off the stack of open elements of an HTML parser or an XML parser: the automatic text track
    /// selection is performed, the list of pending text tracks is populated and the blocked-on-parser flag is unset.
    /// </summary>
    internal void OnPoppedOffStackOfOpenElements()
    {
        if (!_blockedOnParser)
            return;

        HonorUserPreferencesForAutomaticTextTrackSelection();
        PopulatePendingTextTracks();
        _blockedOnParser = false;
        OnTextTracksReadyChanged();
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/media.html#honor-user-preferences-for-automatic-text-track-selection. The
    /// user has no preference: the tracks of the track elements that have a <c>default</c> attribute are enabled.
    /// As in the browsers, the selection is not performed for an element that has no text track yet (the specification
    /// sets the did-perform-automatic-track-selection flag of an element created by the parser without track element, so
    /// a default track added later by a script would never be enabled: WPT src-empty-string.html).
    /// </summary>
    private void HonorUserPreferencesForAutomaticTextTrackSelection()
    {
        if (_textTracks is not { Count: > 0 } || !AreTextTracksEnabled)
            return;

        PerformAutomaticTextTrackSelection(TextTrackKind.Subtitles, TextTrackKind.Captions);
        PerformAutomaticTextTrackSelection(TextTrackKind.Descriptions, TextTrackKind.Descriptions);
        if (_textTracks is { } list)
        {
            foreach (var track in list.ToArray())
            {
                if (track.Kind is TextTrackKind.Chapters or TextTrackKind.Metadata && track is { Mode: TextTrackMode.Disabled, TrackElement: { } element } && element.HasAttributeWithLocalName("default"))
                {
                    track.SetMode(TextTrackMode.Hidden);
                }
            }
        }

        _didPerformAutomaticTrackSelection = true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#perform-automatic-text-track-selection</summary>
    private void PerformAutomaticTextTrackSelection(TextTrackKind kind1, TextTrackKind kind2)
    {
        if (_textTracks is not { } list)
            return;

        TextTrack? defaultTrack = null;
        foreach (var track in list)
        {
            if (track.Kind != kind1 && track.Kind != kind2)
                continue;

            if (track.Mode == TextTrackMode.Showing)
                return;

            if (defaultTrack is null && track is { Mode: TextTrackMode.Disabled, TrackElement: { } element } && element.HasAttributeWithLocalName("default"))
            {
                defaultTrack = track;
            }
        }

        defaultTrack?.SetMode(TextTrackMode.Showing);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/media.html#populate-the-list-of-pending-text-tracks: the text tracks whose mode
    /// is not disabled and whose readiness state is loading. As in the browsers, a track that was just enabled is pending
    /// too: its readiness state changes to loading at the next stable state (e.g. the default track of a media element that
    /// is popped off the stack of open elements, WPT track-default-load-timer-during-parser-block.html).
    /// </summary>
    private void PopulatePendingTextTracks()
    {
        if (_textTracks is not { } list)
            return;

        foreach (var track in list)
        {
            if (track.Mode != TextTrackMode.Disabled && (track.ReadinessState == TextTrackReadinessState.Loading || track.TrackElement is { IsAboutToLoad: true }) && _pendingTextTracks?.Contains(track) != true)
            {
                (_pendingTextTracks ??= []).Add(track);
            }
        }
    }

    /// <summary>The readiness state of a text track changed to loaded or failed to load: it is removed from the list of pending text tracks.</summary>
    internal void OnTextTrackNoLongerLoading(TextTrack track) => RemovePendingTextTrack(track);

    private void RemovePendingTextTrack(TextTrack track)
    {
        if (_pendingTextTracks is { } pending && pending.Remove(track) && pending.Count == 0)
        {
            OnTextTracksReadyChanged();
        }
    }

    /// <summary>
    /// Whether the text tracks are ready (https://html.spec.whatwg.org/multipage/media.html#the-text-tracks-are-ready): the
    /// list of pending text tracks is empty and the blocked-on-parser flag is false. The ready state of the element does not
    /// reach <c>HAVE_FUTURE_DATA</c> before.
    /// </summary>
    private bool AreTextTracksReady => !_blockedOnParser && _pendingTextTracks is not { Count: > 0 };

    private void OnTextTracksReadyChanged()
    {
        if (AreTextTracksReady)
        {
            UpdateReadyStateFromPlayer();
        }
    }

    /// <summary>
    /// The mode of a text track of the element changed (https://html.spec.whatwg.org/multipage/media.html#pending-text-track-change-notification-flag):
    /// a <c>change</c> event is fired at the list of text tracks, and the "time marches on" steps are run.
    /// </summary>
    internal void OnTextTrackModeChanged()
    {
        if (_pendingTextTrackChangeNotification)
            return;

        _pendingTextTrackChangeNotification = true;
        QueueMediaElementTask(
            () =>
            {
                _pendingTextTrackChangeNotification = false;
                TextTracks.FireEvent(new Event("change"));
                if (!_showPoster)
                {
                    TimeMarchesOn(normalPlayback: false);
                }
            },
            () => _pendingTextTrackChangeNotification = false);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Cues
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>A cue was added to the list of cues of a text track of the element: it is a newly introduced cue.</summary>
    internal void OnCueAdded(TextTrackCue cue)
    {
        (_newlyIntroducedCues ??= []).Add(cue);
        if (!_showPoster)
        {
            TimeMarchesOn(normalPlayback: false);
        }
    }

    /// <summary>Cues were added to the list of cues of a text track of the element (the cues of a resource, or the cues of a track added to the list).</summary>
    internal void IntroduceCues(IReadOnlyCollection<TextTrackCue> cues)
    {
        if (cues.Count == 0)
            return;

        _newlyIntroducedCues ??= [];
        foreach (var cue in cues)
        {
            _newlyIntroducedCues.Add(cue);
        }

        if (!_showPoster)
        {
            TimeMarchesOn(normalPlayback: false);
        }
    }

    /// <summary>A cue was removed from the list of cues of a text track of the element.</summary>
    internal void OnCueRemoved()
    {
        if (!_showPoster)
        {
            TimeMarchesOn(normalPlayback: false);
        }
    }

    /// <summary>The start time or the end time of a cue of a text track of the element changed.</summary>
    internal void OnCueTimesChanged()
    {
        if (!_showPoster)
        {
            TimeMarchesOn(normalPlayback: false);
        }

        // The timer of the playback is due at the next time a cue starts or ends
        if (_isAdvancing)
        {
            SchedulePlaybackTimer();
        }
    }

    /// <summary>
    /// The rules for updating the text track rendering (https://html.spec.whatwg.org/multipage/media.html#rules-for-updating-the-text-track-rendering):
    /// the cues displayed over a video element changed (see <see cref="HtmlVideoElement"/>).
    /// </summary>
    internal virtual void OnCueRenderingChanged()
    {
    }

    /// <summary>The current playback position changed (the steps of "time marches on" were run).</summary>
    private protected virtual void OnCuePlaybackPositionChanged()
    {
    }

    /// <summary>Whether the text tracks are enabled (see <see cref="WebPlatformFeatures.TextTracksEnabled"/>): the resources of the track elements are fetched, and the default tracks are enabled.</summary>
    internal bool AreTextTracksEnabled => (NodeDocument.BrowsingContext ?? NodeDocument.OriginDocument?.BrowsingContext)?.Options.EffectiveFeatures.TextTracksEnabled ?? true;

    /// <summary>Whether the element has text tracks.</summary>
    internal bool HasTextTracks => _textTracks is { Count: > 0 };

    /// <summary>The current playback position, for the cues that are rendered.</summary>
    internal double CuePlaybackPosition => _currentPlaybackPosition;

    /// <summary>
    /// The time until a cue of an active text track starts or ends while the playback advances forwards, in seconds of the
    /// media timeline, or null: the cue events are fired as close as possible to their position on the media timeline.
    /// </summary>
    private double? GetTimeToNextCueBoundary(double position)
    {
        if (_textTracks is not { } list)
            return null;

        double? result = null;
        foreach (var track in list)
        {
            if (track.Mode == TextTrackMode.Disabled)
                continue;

            foreach (var cue in track.CueList)
            {
                // The cues are sorted by their start time
                var start = cue.StartTime - position;
                if (start > 0)
                {
                    if (result is null || start < result)
                    {
                        result = start;
                    }

                    break;
                }

                var end = cue.EndTime - position;
                if (end > 0 && (result is null || end < result))
                {
                    result = end;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The steps of "time marches on" that process the cues (https://html.spec.whatwg.org/multipage/media.html#time-marches-on):
    /// the active flag of the cues of the text tracks that are not disabled is updated, the <c>enter</c> and <c>exit</c>
    /// events are fired at the cues and the <c>cuechange</c> events at their tracks (in tasks), and the element is paused when
    /// the playback goes past the end of a cue that has its pause-on-exit flag set.
    /// </summary>
    private void ProcessCues(bool normalPlayback)
    {
        // If the current playback position changes while the steps are running, the steps are run again once they complete
        if (_isProcessingCues)
        {
            _processCuesAgain = true;
            return;
        }

        _isProcessingCues = true;
        try
        {
            ProcessCuesCore(normalPlayback);
            while (_processCuesAgain)
            {
                _processCuesAgain = false;
                ProcessCuesCore(normalPlayback: false);
            }
        }
        finally
        {
            _isProcessingCues = false;
        }
    }

    private void ProcessCuesCore(bool normalPlayback)
    {
        var position = _currentPlaybackPosition;
        var lastTime = _lastTimeMarchesOnPosition;
        _lastTimeMarchesOnPosition = position;
        var newlyIntroduced = _newlyIntroducedCues;
        var previouslyMissed = _missedCues;
        if (_textTracks is not { Count: > 0 } list)
        {
            newlyIntroduced?.Clear();
            _missedCues = null;
            return;
        }

        // The current cues: the cues of the hidden and showing tracks whose start time is less than or equal to the current
        // playback position and whose end time is greater than it; the other cues that take part in the events: the ones
        // that are active, and the missed cues
        List<(TextTrackCue Cue, int TrackIndex)>? entered = null;
        List<(TextTrackCue Cue, int TrackIndex)>? exited = null;
        List<(TextTrackCue Cue, int TrackIndex)>? missed = null;
        var pause = false;
        var trackIndex = -1;
        foreach (var track in list)
        {
            trackIndex++;
            if (track.Mode == TextTrackMode.Disabled)
                continue;

            foreach (var cue in track.CueList)
            {
                if (cue.StartTime <= position && cue.EndTime > position)
                {
                    if (!cue.IsActive)
                    {
                        (entered ??= []).Add((cue, trackIndex));
                    }

                    continue;
                }

                // The missed cues: the other cues whose start time is greater than or equal to the last time and whose end
                // time is less than or equal to the current playback position, when the position only changed through its
                // usual monotonic increase during normal playback; the newly introduced cues are not missed. As in the
                // browsers, a cue whose end time is less than its start time ends at its start time (the time of its exit
                // event): the specification would miss it again each time the steps run before its start time
                var isMissed = normalPlayback && !double.IsNaN(lastTime) && cue.StartTime >= lastTime && Math.Max(cue.EndTime, cue.StartTime) <= position && newlyIntroduced?.Contains(cue) != true;

                // A cue that ends at the position of the previous run was missed by that run: it is not missed again (the
                // events are fired at the positions of the cues, so the previous run may be at the end of the cue)
                if (isMissed && previouslyMissed?.Contains(cue) == true)
                {
                    isMissed = false;
                }

                if (isMissed)
                {
                    (missed ??= []).Add((cue, trackIndex));
                }

                if (cue.IsActive || isMissed)
                {
                    (exited ??= []).Add((cue, trackIndex));
                    pause |= normalPlayback && cue.PauseOnExit;
                }
            }
        }

        newlyIntroduced?.Clear();
        _missedCues = missed?.ConvertAll(static entry => entry.Cue);
        if (entered is null && exited is null && missed is null)
            return;

        // The playback is paused by going past the end time of a cue that has its pause-on-exit flag set, but not by a seek
        if (pause)
        {
            InternalPauseSteps();
        }

        // The tasks that fire the events are sorted by their time, then by the text track cue order of their cues, the
        // enter events before the exit events
        var events = new List<(double Time, int TrackIndex, TextTrackCue Cue, bool Exit)>();
        if (missed is not null)
        {
            foreach (var (cue, index) in missed)
            {
                events.Add((cue.StartTime, index, cue, false));
            }
        }

        if (exited is not null)
        {
            foreach (var (cue, index) in exited)
            {
                events.Add((Math.Max(cue.EndTime, cue.StartTime), index, cue, true));
            }
        }

        if (entered is not null)
        {
            foreach (var (cue, index) in entered)
            {
                events.Add((cue.StartTime, index, cue, false));
            }
        }

        events.Sort(static (x, y) =>
        {
            var result = x.Time.CompareTo(y.Time);
            if (result != 0)
                return result;

            result = x.TrackIndex.CompareTo(y.TrackIndex);
            if (result != 0)
                return result;

            result = ReferenceEquals(x.Cue, y.Cue) ? 0 : TextTrackCue.CompareOrder(x.Cue, y.Cue);
            return result != 0 ? result : x.Exit.CompareTo(y.Exit);
        });

        var affectedTracks = new SortedDictionary<int, TextTrack>();
        foreach (var (_, index, cue, exit) in events)
        {
            affectedTracks[index] = cue.Track!;
            QueueMediaElementTask(() => cue.FireEvent(new Event(exit ? "exit" : "enter")));
        }

        // A cuechange event is fired at each affected track, then at its track element
        foreach (var track in affectedTracks.Values)
        {
            QueueMediaElementTask(() =>
            {
                track.FireEvent(new Event("cuechange"));
                track.TrackElement?.FireEvent(new Event("cuechange"));
            });
        }

        // The active flag of the current cues is set, the one of the other cues is unset
        if (exited is not null)
        {
            foreach (var (cue, _) in exited)
            {
                cue.SetActive(false);
            }
        }

        if (entered is not null)
        {
            foreach (var (cue, _) in entered)
            {
                cue.SetActive(true);
            }
        }

        // The rules for updating the text track rendering of the affected tracks that are showing
        var updateRendering = false;
        foreach (var track in affectedTracks.Values)
        {
            track.OnCueActiveChanged();
            updateRendering |= track.Mode == TextTrackMode.Showing;
        }

        if (updateRendering)
        {
            OnCueRenderingChanged();
        }
    }
}
