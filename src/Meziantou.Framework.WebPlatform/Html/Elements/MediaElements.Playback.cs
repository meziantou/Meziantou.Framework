using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

// Ready states, playing the media resource and seeking (https://html.spec.whatwg.org/multipage/media.html#ready-states,
// #playing-the-media-resource, #seeking)
public partial class HtmlMediaElement
{
    /// <summary>The longest time between two <c>timeupdate</c> events while the element is playing (the specification requires 15 to 250ms).</summary>
    private static readonly TimeSpan TimeUpdateInterval = TimeSpan.FromMilliseconds(250);

    private bool _paused = true;
    private bool _seeking;
    private int _seekGeneration;
    private bool _loadedDataFired;
    private double _duration = double.NaN;
    private double _currentPlaybackPosition;
    private double _officialPlaybackPosition;
    private bool _officialPlaybackPositionIsStable;
    private double _defaultPlaybackStartPosition;
    private List<TaskCompletionSource>? _pendingPlayPromises;

    /// <summary>Whether the position of the player advances: the element is potentially playing and its document is fully active.</summary>
    private bool _isAdvancing;
    private int _playbackTimerGeneration;

    /// <summary>The ranges of the media timeline reached during normal playback; the last one is extended while the element is playing.</summary>
    private List<MediaTimeRange>? _playedRanges;
    private bool _playedRangeIsOpen;

    // ----------------------------------------------------------------------------------------------------------------
    // Ready states
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Gets the readiness of the element to render the current playback position (https://html.spec.whatwg.org/multipage/media.html#dom-media-readystate).</summary>
    [IdlMember("readyState", "https://html.spec.whatwg.org/multipage/media.html#dom-media-readystate")]
    public ushort ReadyState => _readyState;

    /// <summary>
    /// The ready state of the element follows the one of the player, once the metadata is known. It does not reach
    /// <c>HAVE_FUTURE_DATA</c> until the text tracks are ready (https://html.spec.whatwg.org/multipage/media.html#the-text-tracks-are-ready).
    /// </summary>
    /// <param name="canRise">Whether the ready state can rise (false when the state of an attached media source changes: its ready state only rises asynchronously, see <see cref="ScheduleMediaSourceMonitoring"/>).</param>
    private void UpdateReadyStateFromPlayer(bool canRise = true)
    {
        if (_player is { } player && _readyState != HaveNothing)
        {
            var state = Math.Max((int)player.ReadyState, HaveMetadata);
            if (!AreTextTracksReady)
            {
                state = Math.Min(state, HaveCurrentData);
            }

            // While the element attached to a media source is seeking, the media data of the new playback position is not
            // established to be available (as in the browsers, the seek completes before the ready state changes)
            if (_seeking && player is MediaSourcePlayer)
            {
                state = HaveMetadata;
            }

            if (!canRise && state > _readyState)
                return;

            SetReadyState((ushort)state);
        }
    }

    /// <summary>The steps run when the ready state changes (https://html.spec.whatwg.org/multipage/media.html#ready-states).</summary>
    private void SetReadyState(ushort value)
    {
        var previous = _readyState;
        if (previous == value)
            return;

        var wasPotentiallyPlaying = IsPotentiallyPlaying;
        _readyState = value;
        NodeDocument.IncrementElementStateVersion();
        if (_mediaSource is { } source && _mediaSourceGlobal is { } sourceElementGlobal && (previous == HaveEnoughData || value == HaveEnoughData))
        {
            // A managed media source tells the application whether more data is needed
            source.SetElementCanPlayUninterrupted(sourceElementGlobal, value == HaveEnoughData);
        }

        if (previous == HaveNothing && value >= HaveMetadata)
        {
            _loadedDataFired = false;
            QueueMediaEvent("loadedmetadata");
            OnRenderingChanged();
            previous = HaveMetadata;
            if (value == HaveMetadata)
            {
                UpdatePlayback();
                return;
            }
        }

        if (previous == HaveMetadata && value >= HaveCurrentData)
        {
            if (!_loadedDataFired)
            {
                // Once the ready state reaches HAVE_CURRENT_DATA, after the loadeddata event has been fired, the element no
                // longer delays the load event
                _loadedDataFired = true;
                QueueMediaElementTask(() =>
                {
                    FireEvent(new Event("loadeddata"));
                    SetDelayingTheLoadEvent(false);
                });
            }

            OnRenderingChanged();
        }

        if (previous >= HaveFutureData && value <= HaveCurrentData)
        {
            if (wasPotentiallyPlaying && !HasEndedPlayback)
            {
                QueueMediaEvent("timeupdate");
                QueueMediaEvent("waiting");
            }
        }
        else if (previous <= HaveCurrentData && value == HaveFutureData)
        {
            QueueMediaEvent("canplay");
            if (!_paused)
            {
                NotifyAboutPlaying();
            }
        }
        else if (value == HaveEnoughData)
        {
            if (previous <= HaveCurrentData)
            {
                QueueMediaEvent("canplay");
                if (!_paused)
                {
                    NotifyAboutPlaying();
                }
            }

            QueueMediaEvent("canplaythrough");
            if (IsEligibleForAutoplay && IsAllowedToPlay)
            {
                SetPaused(false);
                if (_showPoster)
                {
                    SetShowPoster(false);
                    TimeMarchesOn(normalPlayback: false);
                }

                QueueMediaEvent("play");
                NotifyAboutPlaying();
            }
        }

        UpdatePlayback();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Offsets into the media resource
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets the length of the media resource in seconds (https://html.spec.whatwg.org/multipage/media.html#dom-media-duration):
    /// NaN when no media data is available, positive infinity for an unbounded stream.
    /// </summary>
    [IdlMember("duration", "https://html.spec.whatwg.org/multipage/media.html#dom-media-duration")]
    public double Duration => _duration;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/media.html#dom-media-currenttime: the official playback position (or the
    /// default playback start position, without media data). Setting it seeks to the new position, limited to the seekable
    /// ranges of the media resource.
    /// </summary>
    [IdlMember("currentTime", "https://html.spec.whatwg.org/multipage/media.html#dom-media-currenttime")]
    public double CurrentTime
    {
        get => _defaultPlaybackStartPosition != 0 ? _defaultPlaybackStartPosition : OfficialPlaybackPosition;
        set
        {
            if (_readyState == HaveNothing)
            {
                _defaultPlaybackStartPosition = value;
                return;
            }

            // The specification sets the official playback position to the new value, which the seek algorithm then limits
            // to the seekable ranges in parallel; as in the browsers, currentTime is at once the position of the seek (WPT
            // seek-to-max-value.htm, seek-to-negative-time.htm)
            SetOfficialPlaybackPosition(value, keepUntilStableState: true);
            Seek(value);
            if (_seeking)
            {
                _officialPlaybackPosition = _currentPlaybackPosition;
            }
        }
    }

    /// <summary>Seeks to near the given time, in seconds, as fast as possible (https://html.spec.whatwg.org/multipage/media.html#dom-media-fastseek).</summary>
    [IdlMember("fastSeek", "https://html.spec.whatwg.org/multipage/media.html#dom-media-fastseek")]
    public void FastSeek(double time)
    {
        if (Backend is null)
        {
            _defaultPlaybackStartPosition = time;
            return;
        }

        Seek(time);
    }

    /// <summary>
    /// Returns the date and time of the zero time of the media timeline (https://html.spec.whatwg.org/multipage/media.html#dom-media-getstartdate),
    /// or null when the media resource has none (the timeline offset is NaN: the scripts get a <c>Date</c> object whose time
    /// value is NaN, an "Invalid Date").
    /// </summary>
    [IdlMember("getStartDate", "https://html.spec.whatwg.org/multipage/media.html#dom-media-getstartdate")]
    public DateTime? GetStartDate() => _player?.StartDate?.UtcDateTime;

    /// <summary>
    /// The current playback position (https://html.spec.whatwg.org/multipage/media.html#current-playback-position): the
    /// position of the player while it advances.
    /// </summary>
    private double CurrentPlaybackPosition
    {
        get
        {
            UpdateCurrentPlaybackPosition();
            return _currentPlaybackPosition;
        }
    }

    /// <summary>Reads the position of the player while it advances, and extends the range of the timeline that is being played.</summary>
    private void UpdateCurrentPlaybackPosition()
    {
        if (!_isAdvancing || _seeking || _player is not { } player)
            return;

        var position = player.CurrentTime;
        if (double.IsNaN(position))
            return;

        _currentPlaybackPosition = position;
        if (_playedRangeIsOpen && _playedRanges is { Count: > 0 } ranges)
        {
            var range = ranges[^1];
            ranges[^1] = new MediaTimeRange(Math.Min(range.Start, position), Math.Max(range.End, position));
        }
    }

    /// <summary>
    /// The official playback position (https://html.spec.whatwg.org/multipage/media.html#official-playback-position): an
    /// approximation of the current playback position that is kept stable while scripts are running. It is set to the
    /// current playback position when it is read for the first time since the last stable state.
    /// </summary>
    private double OfficialPlaybackPosition
    {
        get
        {
            if (!_officialPlaybackPositionIsStable)
            {
                // The position does not change while a script is running
                SetOfficialPlaybackPosition(CurrentPlaybackPosition, keepUntilStableState: _isAdvancing);
            }

            return _officialPlaybackPosition;
        }
    }

    private void SetOfficialPlaybackPosition(double value, bool keepUntilStableState)
    {
        _officialPlaybackPosition = value;
        if (keepUntilStableState && !_officialPlaybackPositionIsStable)
        {
            // Any time the user agent provides a stable state, the official playback position is set to the current playback position
            _officialPlaybackPositionIsStable = AwaitStableState(() => _officialPlaybackPositionIsStable = false);
        }
    }

    /// <summary>The earliest possible position (https://html.spec.whatwg.org/multipage/media.html#earliest-possible-position): the start of the first seekable range, or 0.</summary>
    private double EarliestPossiblePosition => _player is { Seekable: { Count: > 0 } seekable } ? Math.Max(seekable[0].Start, 0) : 0;

    /// <summary>Gets the time ranges of the media resource that are buffered (https://html.spec.whatwg.org/multipage/media.html#dom-media-buffered).</summary>
    [IdlMember("buffered", "https://html.spec.whatwg.org/multipage/media.html#dom-media-buffered")]
    public TimeRanges Buffered
    {
        get
        {
            if (_mediaSource is not { } source)
                return _player is { } player ? new TimeRanges(player.Buffered) : new TimeRanges();

            // The same object is returned while the ranges of the attached media source do not change
            // (https://w3c.github.io/media-source/#dom-htmlmediaelement.buffered)
            var ranges = new TimeRanges(source.IsWorkerClosing ? [] : source.Snapshot.Buffered);
            if (_mediaSourceBuffered is null || !MediaTimeRanges.AreEqual(ranges.Ranges, _mediaSourceBuffered.Ranges))
            {
                _mediaSourceBuffered = ranges;
            }

            return _mediaSourceBuffered;
        }
    }

    /// <summary>Gets the time ranges of the media resource that the user agent can seek to (https://html.spec.whatwg.org/multipage/media.html#dom-media-seekable).</summary>
    [IdlMember("seekable", "https://html.spec.whatwg.org/multipage/media.html#dom-media-seekable")]
    public TimeRanges Seekable
    {
        get
        {
            // The seekable ranges of an attached media source do not depend on the ready state of the element
            // (https://w3c.github.io/media-source/#dom-htmlmediaelement.seekable)
            if (_mediaSource is { } source)
                return new TimeRanges(source.IsWorkerClosing ? [] : source.Snapshot.GetSeekable());

            return _player is { } player ? new TimeRanges(player.Seekable) : new TimeRanges();
        }
    }

    /// <summary>
    /// Gets the time ranges of the media resource that were reached through the usual monotonic increase of the current
    /// playback position during normal playback (https://html.spec.whatwg.org/multipage/media.html#dom-media-played).
    /// </summary>
    [IdlMember("played", "https://html.spec.whatwg.org/multipage/media.html#dom-media-played")]
    public TimeRanges Played
    {
        get
        {
            UpdateCurrentPlaybackPosition();
            return _playedRanges is { } ranges ? new TimeRanges(ranges) : new TimeRanges();
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Playing the media resource
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Gets a value indicating whether the playback is paused (https://html.spec.whatwg.org/multipage/media.html#dom-media-paused).</summary>
    [IdlMember("paused", "https://html.spec.whatwg.org/multipage/media.html#dom-media-paused")]
    public bool Paused => _paused;

    private void SetPaused(bool value)
    {
        if (_paused == value)
            return;

        _paused = value;

        // :playing and :paused
        NodeDocument.IncrementElementStateVersion();
    }

    /// <summary>Gets a value indicating whether the element is seeking (https://html.spec.whatwg.org/multipage/media.html#dom-media-seeking).</summary>
    [IdlMember("seeking", "https://html.spec.whatwg.org/multipage/media.html#dom-media-seeking")]
    public bool Seeking => _seeking;

    /// <summary>Gets a value indicating whether the playback reached the end of the media resource, playing forwards (https://html.spec.whatwg.org/multipage/media.html#dom-media-ended).</summary>
    [IdlMember("ended", "https://html.spec.whatwg.org/multipage/media.html#dom-media-ended")]
    public bool Ended => HasEndedPlayback && IsPlayingForwards;

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#direction-of-playback</summary>
    private bool IsPlayingForwards => _playbackRate >= 0;

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#ended-playback</summary>
    private bool HasEndedPlayback
    {
        get
        {
            if (_readyState < HaveMetadata)
                return false;

            var position = CurrentPlaybackPosition;
            return IsPlayingForwards
                ? position >= _duration && !HasAttributeWithLocalName("loop")
                : position <= EarliestPossiblePosition;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#blocked-media-element</summary>
    private bool IsBlocked => _readyState <= HaveCurrentData;

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#potentially-playing</summary>
    private bool IsPotentiallyPlaying => !_paused && !IsBlocked && _error is null && !HasEndedPlayback;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/media.html#eligible-for-autoplay: the document must be allowed to use the
    /// "autoplay" policy-controlled feature.
    /// </summary>
    private bool IsEligibleForAutoplay
    {
        get
        {
            if (!_canAutoplay || !_paused || !HasAttributeWithLocalName("autoplay"))
                return false;

            var document = NodeDocument;
            if (LazyLoading.IsLazy(GetAttributeValue("loading")) && !_lazyLoadStarted && document.ScriptingEnabled)
                return false;

            if (document.ActiveSandboxingFlags.HasFlag(SandboxingFlags.AutomaticFeatures))
                return false;

            // The media elements of the documents without browsing context are loaded and played with the context of the
            // document they come from (see IsAllowedToPlay)
            return document.BrowsingContext is null || PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.Autoplay);
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/media.html#allowed-to-play: the autoplay policy of the browsing context
    /// (<see cref="BrowsingContextOptions.MediaAutoplayPolicy"/>) and, as in the browsers, the "autoplay" policy-controlled
    /// feature: the element of a document that is not allowed to use the feature only plays when it is muted or when its
    /// window has transient activation (<c>play()</c> in a user gesture).
    /// </summary>
    private bool IsAllowedToPlay
    {
        get
        {
            var document = NodeDocument;
            if (document.BrowsingContext is not null && !IsMuted && document.RelevantWindow is not { HasTransientActivation: true } && !PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.Autoplay))
                return false;

            var browsingContext = document.BrowsingContext ?? document.OriginDocument?.BrowsingContext;
            if (browsingContext is null || browsingContext.Options.MediaAutoplayPolicy == MediaAutoplayPolicy.Allowed)
                return true;

            // The playback is allowed while muted, or once the user interacted with the page
            return IsMuted || (document.RelevantWindow ?? document.OriginDocument?.RelevantWindow) is { HasStickyActivation: true };
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/media.html#dom-media-play: returns a task that completes when the playback
    /// starts, or fails with a <c>NotAllowedError</c> (the autoplay policy), a <c>NotSupportedError</c> (the media resource is
    /// not suitable, or there is no media backend) or an <c>AbortError</c> (the element was paused or loaded another resource).
    /// </summary>
    [IdlMember("play", "https://html.spec.whatwg.org/multipage/media.html#dom-media-play")]
    public Task Play()
    {
        if (!IsAllowedToPlay)
            return Task.FromException(DomException.NotAllowed("The playback is not allowed without a user gesture"));

        if (Backend is null)
            return Task.FromException(DomException.NotSupported("The element has no supported source"));

        if (_error is { Code: MediaError.MediaErrSrcNotSupported })
            return Task.FromException(DomException.NotSupported("The element has no supported source"));

        RunLazyLoadResumptionSteps();
        var promise = new TaskCompletionSource();
        (_pendingPlayPromises ??= []).Add(promise);
        InternalPlaySteps();
        return promise.Task;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#internal-play-steps</summary>
    private void InternalPlaySteps()
    {
        if (_networkState == NetworkEmpty)
        {
            InvokeResourceSelection();
        }

        // The user requests the playback: the fetch deferred by preload="none" starts
        ResumeDeferredFetch();
        if (HasEndedPlayback && IsPlayingForwards)
        {
            Seek(EarliestPossiblePosition);
        }

        if (_paused)
        {
            SetPaused(false);
            if (_showPoster)
            {
                SetShowPoster(false);
                TimeMarchesOn(normalPlayback: false);
            }

            QueueMediaEvent("play");
            if (_readyState <= HaveCurrentData)
            {
                QueueMediaEvent("waiting");
            }
            else
            {
                NotifyAboutPlaying();
            }
        }
        else if (_readyState >= HaveFutureData)
        {
            var promises = TakePendingPlayPromises();
            QueueMediaElementTask(() => ResolvePendingPlayPromises(promises), () => ResolvePendingPlayPromises(promises));
        }

        _canAutoplay = false;
        UpdatePlayback();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#dom-media-pause</summary>
    [IdlMember("pause", "https://html.spec.whatwg.org/multipage/media.html#dom-media-pause")]
    public void Pause()
    {
        if (Backend is null)
            return;

        if (_networkState == NetworkEmpty)
        {
            InvokeResourceSelection();
        }

        InternalPauseSteps();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#internal-pause-steps</summary>
    private void InternalPauseSteps()
    {
        _canAutoplay = false;
        if (_paused)
            return;

        UpdateCurrentPlaybackPosition();
        SetPaused(true);
        var promises = TakePendingPlayPromises();
        QueueMediaElementTask(
            () =>
            {
                FireEvent(new Event("timeupdate"));
                FireEvent(new Event("pause"));
                RejectPendingPlayPromises(promises, "AbortError");
            },
            () => RejectPendingPlayPromises(promises, "AbortError"));
        UpdatePlayback();
        _officialPlaybackPosition = _currentPlaybackPosition;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#take-pending-play-promises</summary>
    private List<TaskCompletionSource> TakePendingPlayPromises()
    {
        var promises = _pendingPlayPromises ?? [];
        _pendingPlayPromises = null;
        return promises;
    }

    private static void ResolvePendingPlayPromises(List<TaskCompletionSource> promises)
    {
        foreach (var promise in promises)
        {
            promise.TrySetResult();
        }
    }

    private static void RejectPendingPlayPromises(List<TaskCompletionSource> promises, string errorName)
    {
        foreach (var promise in promises)
        {
            promise.TrySetException(new DomException(errorName == "AbortError" ? "The play() request was interrupted" : "The element has no supported source", errorName));
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#notify-about-playing</summary>
    private void NotifyAboutPlaying()
    {
        var promises = TakePendingPlayPromises();
        QueueMediaElementTask(
            () =>
            {
                FireEvent(new Event("playing"));
                ResolvePendingPlayPromises(promises);
            },
            () => ResolvePendingPlayPromises(promises));
    }

    /// <summary>
    /// Starts or stops the playback of the player: its position advances while the element is potentially playing and its
    /// document is fully active. While it advances, a timer of the event loop reads the position of the player to fire
    /// <c>timeupdate</c> and to detect the end of the resource.
    /// </summary>
    /// <param name="notifyEnd">Whether the steps of the end of the media resource run when the position reached it since it was last read (false when they are already running).</param>
    private void UpdatePlayback(bool notifyEnd = true)
    {
        if (_player is not { } player)
            return;

        var advancing = IsPotentiallyPlaying && !_seeking && NodeDocument.EventLoop is not null && IsDocumentPlayable;
        if (advancing == _isAdvancing)
            return;

        if (advancing)
        {
            _isAdvancing = true;

            // The range of the timeline that is played from the current position
            (_playedRanges ??= []).Add(new MediaTimeRange(_currentPlaybackPosition, _currentPlaybackPosition));
            _playedRangeIsOpen = true;
            player.Play();
            SchedulePlaybackTimer();
        }
        else
        {
            UpdateCurrentPlaybackPosition();

            // The position can reach the end of the media resource before the timer of the playback is due (the timer is
            // then canceled): the element is no longer potentially playing because its playback has ended
            var reachedEnd = notifyEnd && !_paused && !_seeking && _error is null && !IsBlocked && IsPlayingForwards && _currentPlaybackPosition >= _duration;
            _isAdvancing = false;
            _playedRangeIsOpen = false;
            _playbackTimerGeneration++;
            player.Pause();
            if (reachedEnd)
            {
                OnReachedEnd();
            }
        }
    }

    /// <summary>
    /// If the node document of the element stops being a fully active document, the playback stops. A document without
    /// browsing context (e.g. <c>createHTMLDocument()</c>) plays as long as the document that created it is fully active.
    /// </summary>
    private bool IsDocumentPlayable
    {
        get
        {
            var document = NodeDocument;
            return document.BrowsingContext is null ? document.OriginDocument is { IsFullyActive: true } : document.IsFullyActive;
        }
    }

    private void SchedulePlaybackTimer()
    {
        if (NodeDocument.EventLoop is not { } eventLoop || _player is null)
            return;

        // The timer is due when the next timeupdate event is, or when the position reaches the end of the resource
        var delay = TimeUpdateInterval;
        var rate = Math.Abs(_playbackRate);
        if (rate > 0)
        {
            var remaining = IsPlayingForwards ? _duration - _currentPlaybackPosition : _currentPlaybackPosition - EarliestPossiblePosition;
            if (remaining >= 0 && remaining / rate < delay.TotalSeconds)
            {
                delay = TimeSpan.FromSeconds(remaining / rate);
            }

            // The playback of a media source stops at the end of the buffered data
            if (IsPlayingForwards && _player is MediaSourcePlayer { TimeToBufferedEnd: { } buffered } && buffered >= 0 && buffered / rate < delay.TotalSeconds)
            {
                delay = TimeSpan.FromSeconds(buffered / rate);
            }

            // The cue events are fired as close as possible to the position of the cues on the media timeline
            if (IsPlayingForwards && GetTimeToNextCueBoundary(_currentPlaybackPosition) is { } boundary && boundary / rate < delay.TotalSeconds)
            {
                delay = TimeSpan.FromSeconds(boundary / rate);
            }
        }
        else
        {
            // The position does not move: the element is still playing, and the timer only checks that its document is active
            delay = TimeSpan.FromSeconds(1);
        }

        var generation = ++_playbackTimerGeneration;
        eventLoop.QueueTimer(delay, () =>
        {
            if (generation != _playbackTimerGeneration || !_isAdvancing)
                return;

            OnPlaybackPositionChanged();
            if (_isAdvancing && generation == _playbackTimerGeneration)
            {
                SchedulePlaybackTimer();
            }
        });
    }

    /// <summary>The current playback position changed during normal playback: the time marches on, and the playback may have reached the end of the resource.</summary>
    private void OnPlaybackPositionChanged()
    {
        if (!_isAdvancing)
            return;

        if (!IsDocumentPlayable)
        {
            UpdatePlayback();
            return;
        }

        var previous = _currentPlaybackPosition;
        UpdateCurrentPlaybackPosition();
        UpdateVideoDimensions();
        var position = _currentPlaybackPosition;
        if (_player is MediaSourcePlayer && !(IsPlayingForwards && position >= _duration))
        {
            // The ready state follows the data buffered at the new position (https://w3c.github.io/media-source/#buffer-monitoring):
            // the playback is suspended when it reaches the end of the buffered data
            UpdateReadyStateFromPlayer();
            if (!_isAdvancing)
            {
                if (position != previous)
                {
                    TimeMarchesOn(normalPlayback: true);
                }

                return;
            }
        }

        if (IsPlayingForwards && position >= _duration)
        {
            OnReachedEnd();
        }
        else if (!IsPlayingForwards && position <= EarliestPossiblePosition)
        {
            // The playback reaches the earliest possible position when the direction of playback is backwards
            QueueMediaEvent("timeupdate");
            UpdatePlayback();
        }
        else if (position != previous)
        {
            TimeMarchesOn(normalPlayback: true);
        }
    }

    /// <summary>
    /// The steps run when the current playback position reaches the end of the media resource and the direction of playback
    /// is forwards (https://html.spec.whatwg.org/multipage/media.html#reaches-the-end).
    /// </summary>
    private void OnReachedEnd()
    {
        if (HasAttributeWithLocalName("loop"))
        {
            Seek(EarliestPossiblePosition);
            return;
        }

        UpdatePlayback(notifyEnd: false);
        QueueMediaElementTask(() =>
        {
            FireEvent(new Event("timeupdate"));
            if (HasEndedPlayback && IsPlayingForwards && !_paused)
            {
                SetPaused(true);
                FireEvent(new Event("pause"));
                RejectPendingPlayPromises(TakePendingPlayPromises(), "AbortError");
            }

            FireEvent(new Event("ended"));
        });
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/media.html#time-marches-on: a <c>timeupdate</c> event is fired when the time
    /// was reached through the usual monotonic increase of the current playback position (the timer of the playback limits
    /// the events to one every 250ms), then the cues of the text tracks are processed (see <see cref="ProcessCues"/>).
    /// </summary>
    private void TimeMarchesOn(bool normalPlayback)
    {
        if (normalPlayback)
        {
            QueueMediaEvent("timeupdate");
        }

        ProcessCues(normalPlayback);
        OnCuePlaybackPositionChanged();
        OnRenderingChanged();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Seeking
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#dom-media-seek</summary>
    private void Seek(double newPlaybackPosition)
    {
        SetShowPoster(false);
        if (_readyState == HaveNothing || _player is not { } player)
            return;

        // Another instance of the algorithm that is running is aborted
        var generation = ++_seekGeneration;
        UpdateCurrentPlaybackPosition();
        _playedRangeIsOpen = false;
        if (!_seeking)
        {
            _seeking = true;
            NodeDocument.IncrementElementStateVersion();
        }

        if (newPlaybackPosition > _duration)
        {
            newPlaybackPosition = _duration;
        }

        var earliest = EarliestPossiblePosition;
        if (newPlaybackPosition < earliest || double.IsNaN(newPlaybackPosition))
        {
            newPlaybackPosition = earliest;
        }

        // The position in one of the seekable ranges that is the nearest to the new playback position
        var seekable = new TimeRanges(player.Seekable).Ranges;
        if (seekable.Length == 0)
        {
            _seeking = false;
            NodeDocument.IncrementElementStateVersion();
            UpdatePlayback();
            return;
        }

        var nearest = double.NaN;
        foreach (var range in seekable)
        {
            var candidate = Math.Clamp(newPlaybackPosition, range.Start, range.End);
            if (double.IsNaN(nearest)
                || Math.Abs(candidate - newPlaybackPosition) < Math.Abs(nearest - newPlaybackPosition)
                || (Math.Abs(candidate - newPlaybackPosition) == Math.Abs(nearest - newPlaybackPosition) && Math.Abs(candidate - _currentPlaybackPosition) < Math.Abs(nearest - _currentPlaybackPosition)))
            {
                nearest = candidate;
            }
        }

        newPlaybackPosition = nearest;
        QueueMediaEvent("seeking");
        _isAdvancing = false;
        _playbackTimerGeneration++;
        player.Pause();
        player.Seek(newPlaybackPosition);
        _currentPlaybackPosition = newPlaybackPosition;
        if (player is MediaSourcePlayer)
        {
            // The ready state is HAVE_METADATA until the media data of the new position is appended
            // (https://w3c.github.io/media-source/#mediasource-seeking)
            UpdateReadyStateFromPlayer();
        }

        // The user agent waits until it has established whether the media data of the new playback position is available:
        // a player that seeks asynchronously notifies the element (see OnPlayerStateChanged)
        QueueMediaElementTask(() =>
        {
            if (generation == _seekGeneration && _player is { IsSeeking: false })
            {
                CompleteSeek(generation);
            }
        });
    }

    /// <summary>The last steps of the seek algorithm, once the data of the new playback position is available.</summary>
    private void CompleteSeek(int generation)
    {
        if (generation != _seekGeneration || !_seeking)
            return;

        _seeking = false;
        NodeDocument.IncrementElementStateVersion();
        TimeMarchesOn(normalPlayback: false);
        QueueMediaEvent("timeupdate");
        QueueMediaEvent("seeked");
        if (_player is MediaSourcePlayer)
        {
            // The ready state follows the data buffered at the new playback position
            UpdateReadyStateFromPlayer();
        }

        // The playback continues from the new position; "reaches the end" does not imply that the position changed during
        // normal playback: it could be via seeking
        UpdatePlayback();
        if (!_paused && _readyState >= HaveFutureData && IsPlayingForwards && _currentPlaybackPosition >= _duration && !HasAttributeWithLocalName("loop"))
        {
            OnReachedEnd();
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Dimensions of the video
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The natural dimensions of the video of the media resource, in CSS pixels (0 when it has no video).</summary>
    private protected int MediaVideoWidth { get; private set; }

    private protected int MediaVideoHeight { get; private set; }

    private void SetVideoDimensions(int width, int height)
    {
        MediaVideoWidth = Math.Max(width, 0);
        MediaVideoHeight = Math.Max(height, 0);
        OnRenderingChanged();
    }

    /// <summary>Whenever the natural dimensions of the video change, a <c>resize</c> event is fired at the video element.</summary>
    private void UpdateVideoDimensions()
    {
        if (_player is not { } player)
            return;

        var width = Math.Max(player.VideoWidth, 0);
        var height = Math.Max(player.VideoHeight, 0);
        if (width == MediaVideoWidth && height == MediaVideoHeight)
            return;

        SetVideoDimensions(width, height);
        if (this is HtmlVideoElement && _readyState != HaveNothing)
        {
            QueueMediaEvent("resize");
        }
    }
}
