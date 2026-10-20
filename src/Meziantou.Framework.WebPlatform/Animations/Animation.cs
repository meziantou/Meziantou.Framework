using Meziantou.Framework.WebPlatform.Css.TypedOm;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>
/// An animation: plays an animation effect on a timeline (https://drafts.csswg.org/web-animations-1/#animations).
/// </summary>
/// <remarks>
/// The pending play and pause tasks run when the event loop updates the rendering (the animation is then ready), and the
/// <c>finish</c>, <c>cancel</c> and <c>remove</c> events are dispatched there (https://drafts.csswg.org/web-animations-1/#update-animations-and-send-events).
/// An animation without timeline, or whose timeline is inactive (e.g. its document has no browsing context), stays pending.
/// </remarks>
/// <seealso href="https://drafts.csswg.org/web-animations-1/#animation"/>
[IdlInterface("Animation", "https://drafts.csswg.org/web-animations-1/#animation")]
public partial class Animation : EventTarget, IRealmBoundPlatformObject
{
    private static long s_nextGlobalIndex;

    private AnimationTimeline? _timeline;
    private AnimationEffect? _effect;
    private double? _startTime;
    private double? _holdTime;
    private double _playbackRate = 1;
    private double? _pendingPlaybackRate;
    private PendingTask _pendingTask;
    private double? _previousCurrentTime;
    private TaskCompletionSource<Animation> _readyPromise;
    private TaskCompletionSource<Animation> _finishedPromise;
    private bool _finishNotificationQueued;
    private Window? _realmWindow;

    // https://drafts.csswg.org/web-animations-2/#auto-aligning-start-time: the start time is aligned with the animation
    // attachment range when the timeline is updated
    private bool _autoAlignStartTime;
    private TimelineRangeBoundary? _rangeStart;
    private TimelineRangeBoundary? _rangeEnd;

    private enum PendingTask
    {
        None,
        Play,
        Pause,
    }

    /// <summary>
    /// Creates an animation of the effect on the timeline (https://drafts.csswg.org/web-animations-1/#dom-animation-animation):
    /// the default timeline of the document of the current global object.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-animation"/>
    [IdlMember("constructor", "https://drafts.csswg.org/web-animations-1/#dom-animation-animation")]
    public Animation(AnimationEffect? effect = null)
        : this(effect, Document.CurrentGlobalDocument.Timeline)
    {
    }

    /// <summary>Creates an animation of the effect on the timeline (null for no timeline).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-animation"/>
    [IdlMember("constructor", "https://drafts.csswg.org/web-animations-1/#dom-animation-animation")]
    public Animation(AnimationEffect? effect, AnimationTimeline? timeline)
    {
        GlobalIndex = NextGlobalIndex();
        _realmWindow = Document.CurrentGlobalWindow ?? timeline?.Document?.RelevantWindow;

        // The current ready promise is initially resolved
        _readyPromise = CreatePromise();
        _readyPromise.SetResult(this);
        _finishedPromise = CreatePromise();
        SetTimelineCore(timeline);
        SetEffectCore(effect);
    }

    /// <summary>Returns the next position in the global animation list.</summary>
    private protected static long NextGlobalIndex() => Interlocked.Increment(ref s_nextGlobalIndex);

    /// <summary>The position of the animation in the global animation list (https://drafts.csswg.org/web-animations-1/#global-animation-list).</summary>
    internal long GlobalIndex { get; private protected set; }

    /// <summary>
    /// Whether the CSS transition or CSS animation was canceled by a style that the layout may compute again with the size of its
    /// query container: it has no effect, and it is canceled once the styles are settled, unless the style is discarded (it then
    /// runs again, see <see cref="Css.Cascade.StyleEngine.InvalidateQueryContainerDescendants"/>).
    /// </summary>
    internal bool IsCancellationPending { get; set; }

    internal override Window? RelevantGlobal => _realmWindow ?? _timeline?.Document?.RelevantWindow;

    Window? IRealmBoundPlatformObject.RealmWindow => _realmWindow;

    /// <summary>The window of the realm of the animation (e.g. the relevant realm of the target of Element.animate()).</summary>
    internal Window? RealmWindow
    {
        get => _realmWindow;
        init => _realmWindow = value;
    }

    /// <summary>The document for timing (https://drafts.csswg.org/web-animations-1/#document-for-timing).</summary>
    internal Document? DocumentForTiming => _timeline?.Document;

    /// <summary>Gets or sets the identifier of the animation.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-id"/>
    [IdlMember("id", "https://drafts.csswg.org/web-animations-1/#dom-animation-id")]
    public string Id { get; set; } = "";

    /// <summary>Gets or sets the effect of the animation (https://drafts.csswg.org/web-animations-1/#dom-animation-effect).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-effect"/>
    [IdlMember("effect", "https://drafts.csswg.org/web-animations-1/#dom-animation-effect")]
    public AnimationEffect? Effect
    {
        get
        {
            FlushPendingStyle();
            return _effect;
        }
        set
        {
            FlushPendingStyle();
            SetEffectCore(value);
        }
    }

    /// <summary>The associated effect, without flushing the pending style changes.</summary>
    internal AnimationEffect? EffectValue => _effect;

    /// <summary>Gets or sets the timeline of the animation (https://drafts.csswg.org/web-animations-1/#dom-animation-timeline).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-timeline"/>
    [IdlMember("timeline", "https://drafts.csswg.org/web-animations-1/#dom-animation-timeline")]
    public AnimationTimeline? Timeline
    {
        // An unresolved timeline name of a CSS animation is an inactive timeline, represented as null (https://github.com/w3c/csswg-drafts/issues/13807)
        get => _timeline is ScrollTimeline { IsUnresolved: true } ? null : _timeline;
        set
        {
            FlushPendingStyle();
            SetTimelineCore(value);
            OnTimelineSetByScript();
        }
    }

    /// <summary>The timeline of the animation, including the inactive timeline of an unresolved timeline name.</summary>
    internal AnimationTimeline? TimelineValue => _timeline;

    /// <summary>Gets or sets the playback rate (https://drafts.csswg.org/web-animations-1/#dom-animation-playbackrate).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-playbackrate"/>
    [IdlMember("playbackRate", "https://drafts.csswg.org/web-animations-1/#dom-animation-playbackrate")]
    public double PlaybackRate
    {
        get => _playbackRate;
        set
        {
            FlushPendingStyle();

            // https://drafts.csswg.org/web-animations-1/#setting-the-playback-rate-of-an-animation
            _pendingPlaybackRate = null;
            var previousTime = CurrentTimeValue;
            var previousPlaybackRate = EffectivePlaybackRate;
            _playbackRate = value;
            if (_timeline is not { IsMonotonicallyIncreasing: false })
            {
                if (previousTime is { } time)
                {
                    SetCurrentTime(time);
                }
            }
            else if (_startTime is { } startTime && !double.IsPositiveInfinity(EffectEnd) && (previousPlaybackRate < 0) != (value < 0))
            {
                // The start and the end of the animation are flipped on a timeline that is not monotonically increasing,
                // preserving the offset of the start time from the other direction
                _startTime = EffectEnd - startTime;
            }

            UpdateFinishedState(didSeek: false, synchronouslyNotify: false);
            OnPlaybackChanged();
        }
    }

    /// <summary>Gets or sets the start time of the animation, in milliseconds (https://drafts.csswg.org/web-animations-1/#dom-animation-starttime).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-animation-starttime"/>
    [IdlMember("startTime", "https://drafts.csswg.org/web-animations-2/#dom-animation-starttime")]
    public CssNumberish? StartTime
    {
        get
        {
            FlushPendingStyle();
            return _startTime is { } time ? ToCssNumberish(time) : (CssNumberish?)null;
        }
        set
        {
            FlushPendingStyle();
            SetStartTimeCore(ValidateTime(value));
            OnStartTimeSetByScript();
        }
    }

    /// <summary>The start time in milliseconds.</summary>
    internal double? StartTimeValue => _startTime;

    /// <summary>Gets or sets the current time of the animation, in milliseconds (https://drafts.csswg.org/web-animations-1/#dom-animation-currenttime).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-animation-currenttime"/>
    [IdlMember("currentTime", "https://drafts.csswg.org/web-animations-2/#dom-animation-currenttime")]
    public CssNumberish? CurrentTime
    {
        get
        {
            FlushPendingStyle();
            return CurrentTimeValue is { } time ? ToCssNumberish(time) : (CssNumberish?)null;
        }
        set
        {
            FlushPendingStyle();
            SetCurrentTime(ValidateTime(value));
            OnCurrentTimeSetByScript();
        }
    }

    /// <summary>The current time in milliseconds (https://drafts.csswg.org/web-animations-1/#animation-current-time).</summary>
    internal double? CurrentTimeValue => GetCurrentTime(ignoreHoldTime: false);

    /// <summary>Gets the play state of the animation (https://drafts.csswg.org/web-animations-1/#dom-animation-playstate).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-playstate"/>
    [IdlMember("playState", "https://drafts.csswg.org/web-animations-1/#dom-animation-playstate")]
    public AnimationPlayState PlayState
    {
        get
        {
            FlushPendingStyle();
            return PlayStateValue;
        }
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#play-states</summary>
    internal AnimationPlayState PlayStateValue
    {
        get
        {
            var currentTime = CurrentTimeValue;
            if (currentTime is null && _startTime is null && _pendingTask == PendingTask.None)
                return AnimationPlayState.Idle;

            if (_pendingTask == PendingTask.Pause || (_startTime is null && _pendingTask != PendingTask.Play))
                return AnimationPlayState.Paused;

            if (currentTime is { } time && ((EffectivePlaybackRate > 0 && time >= EffectEnd) || (EffectivePlaybackRate < 0 && time <= 0)))
                return AnimationPlayState.Finished;

            return AnimationPlayState.Running;
        }
    }

    /// <summary>Gets the replace state of the animation (https://drafts.csswg.org/web-animations-1/#dom-animation-replacestate).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-replacestate"/>
    [IdlMember("replaceState", "https://drafts.csswg.org/web-animations-1/#dom-animation-replacestate")]
    public AnimationReplaceState ReplaceState { get; internal set; }

    /// <summary>Gets whether the animation has a pending play or pause task (https://drafts.csswg.org/web-animations-1/#dom-animation-pending).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-pending"/>
    [IdlMember("pending", "https://drafts.csswg.org/web-animations-1/#dom-animation-pending")]
    public bool Pending
    {
        get
        {
            FlushPendingStyle();
            return _pendingTask != PendingTask.None;
        }
    }

    internal bool HasPendingTask => _pendingTask != PendingTask.None;

    /// <summary>Gets the overall progress of the animation, from 0 to 1 (https://drafts.csswg.org/web-animations-2/#dom-animation-overallprogress).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-animation-overallprogress"/>
    [IdlMember("overallProgress", "https://drafts.csswg.org/web-animations-2/#dom-animation-overallprogress")]
    public double? OverallProgress
    {
        get
        {
            FlushPendingStyle();
            if (_effect is null || CurrentTimeValue is not { } currentTime)
                return null;

            var end = EffectEnd;
            if (end == 0)
                return currentTime < 0 ? 0 : 1;

            if (double.IsPositiveInfinity(end))
                return 0;

            return Math.Clamp(currentTime / end, 0, 1);
        }
    }

    /// <summary>
    /// Gets or sets the start of the animation attachment range (https://drafts.csswg.org/web-animations-2/#dom-animation-rangestart):
    /// "normal" or a <see cref="TimelineRangeOffset"/>. A string is parsed as an <c>animation-range-start</c> value.
    /// </summary>
    /// <exception cref="TypeErrorException">The value is invalid.</exception>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-animation-rangestart"/>
    [IdlMember("rangeStart", "https://drafts.csswg.org/web-animations-2/#dom-animation-rangestart")]
    public TimelineRangeValue RangeStart
    {
        get
        {
            FlushPendingStyle();
            return TimelineRangeBoundary.ToScriptValue(_rangeStart);
        }
        set
        {
            FlushPendingStyle();
            SetRange(TimelineRangeBoundary.FromScriptValue(value, isEnd: false), _rangeEnd);
            OnRangeSetByScript(isEnd: false);
        }
    }

    /// <summary>
    /// Gets or sets the end of the animation attachment range (https://drafts.csswg.org/web-animations-2/#dom-animation-rangeend):
    /// "normal" or a <see cref="TimelineRangeOffset"/>. A string is parsed as an <c>animation-range-end</c> value.
    /// </summary>
    /// <exception cref="TypeErrorException">The value is invalid.</exception>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-animation-rangeend"/>
    [IdlMember("rangeEnd", "https://drafts.csswg.org/web-animations-2/#dom-animation-rangeend")]
    public TimelineRangeValue RangeEnd
    {
        get
        {
            FlushPendingStyle();
            return TimelineRangeBoundary.ToScriptValue(_rangeEnd);
        }
        set
        {
            FlushPendingStyle();
            SetRange(_rangeStart, TimelineRangeBoundary.FromScriptValue(value, isEnd: true));
            OnRangeSetByScript(isEnd: true);
        }
    }

    /// <summary>The start of the animation attachment range, or null for normal.</summary>
    internal TimelineRangeBoundary? RangeStartValue => _rangeStart;

    /// <summary>The end of the animation attachment range, or null for normal.</summary>
    internal TimelineRangeBoundary? RangeEndValue => _rangeEnd;

    /// <summary>
    /// The duration of the animation attachment range of a progress-based timeline, a percentage of the timeline: the timing of
    /// the effect is normalized to it (https://drafts.csswg.org/web-animations-2/#normalize-specified-timing). Null for the
    /// other timelines.
    /// </summary>
    internal double? TimelineDuration
    {
        get
        {
            if (_timeline is not { IsProgressBased: true } timeline)
                return null;

            var start = timeline.GetAttachmentFraction(_rangeStart, isEnd: false);
            var end = timeline.GetAttachmentFraction(_rangeEnd, isEnd: true);
            return Math.Max(0, (end * 100) - (start * 100));
        }
    }

    /// <summary>
    /// Whether the current time of the animation follows a progress-based timeline that is at its minimum or maximum time
    /// (https://drafts.csswg.org/web-animations-2/#at-timeline-boundary): the boundaries of the active interval are then
    /// inclusive. A current time clamped by a hold time (e.g. a finished animation whose timeline went past its end) is not at
    /// the boundary.
    /// </summary>
    internal bool IsAtTimelineBoundary
    {
        get
        {
            if (_timeline is not { IsAtTimelineBoundary: true } || CurrentTimeValue is not { } currentTime)
                return false;

            return _holdTime is null || (GetCurrentTime(ignoreHoldTime: true) is { } unconstrained && Math.Abs(unconstrained - currentTime) < 1e-9);
        }
    }

    /// <summary>Returns a time value of the animation as a <c>CSSNumberish</c>: a percentage for a progress-based timeline, milliseconds otherwise.</summary>
    internal CssNumberish ToCssNumberish(double time) => _timeline is { IsProgressBased: true } ? new CssUnitValue(time, "percent") : time;

    /// <summary>Gets the promise resolved when the animation is ready to play (https://drafts.csswg.org/web-animations-1/#dom-animation-ready).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-ready"/>
    [IdlMember("ready", "https://drafts.csswg.org/web-animations-1/#dom-animation-ready")]
    public Task<Animation> Ready
    {
        get
        {
            FlushPendingStyle();
            return _readyPromise.Task;
        }
    }

    /// <summary>Gets the promise resolved when the animation finishes (https://drafts.csswg.org/web-animations-1/#dom-animation-finished).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-finished"/>
    [IdlMember("finished", "https://drafts.csswg.org/web-animations-1/#dom-animation-finished")]
    public Task<Animation> Finished
    {
        get
        {
            FlushPendingStyle();
            return _finishedPromise.Task;
        }
    }

    /// <summary>Gets or sets the event handler of the <c>finish</c> event.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-onfinish"/>
    [IdlMember("onfinish", "https://drafts.csswg.org/web-animations-1/#dom-animation-onfinish")]
    public Action<Event>? OnFinish
    {
        get => GetEventHandler("finish");
        set => SetEventHandler("finish", value);
    }

    /// <summary>Gets or sets the event handler of the <c>cancel</c> event.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-oncancel"/>
    [IdlMember("oncancel", "https://drafts.csswg.org/web-animations-1/#dom-animation-oncancel")]
    public Action<Event>? OnCancel
    {
        get => GetEventHandler("cancel");
        set => SetEventHandler("cancel", value);
    }

    /// <summary>Gets or sets the event handler of the <c>remove</c> event.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-onremove"/>
    [IdlMember("onremove", "https://drafts.csswg.org/web-animations-1/#dom-animation-onremove")]
    public Action<Event>? OnRemove
    {
        get => GetEventHandler("remove");
        set => SetEventHandler("remove", value);
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#effective-playback-rate</summary>
    internal double EffectivePlaybackRate => _pendingPlaybackRate ?? _playbackRate;

    /// <summary>The end time of the associated effect, or 0 (https://drafts.csswg.org/web-animations-1/#associated-effect-end).</summary>
    internal double EffectEnd => _effect?.EndTime ?? 0;

    /// <summary>Cancels the animation (https://drafts.csswg.org/web-animations-1/#dom-animation-cancel).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-cancel"/>
    [IdlMember("cancel", "https://drafts.csswg.org/web-animations-1/#dom-animation-cancel")]
    public void Cancel()
    {
        FlushPendingStyle();
        CancelCore();
        OnCanceledByScript();
    }

    /// <summary>Seeks the animation to the end of its effect (https://drafts.csswg.org/web-animations-1/#dom-animation-finish).</summary>
    /// <exception cref="DomException">The playback rate is 0, or the effect is infinite and the playback rate is positive (InvalidStateError).</exception>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-finish"/>
    [IdlMember("finish", "https://drafts.csswg.org/web-animations-1/#dom-animation-finish")]
    public void Finish()
    {
        FlushPendingStyle();

        // https://drafts.csswg.org/web-animations-1/#finishing-an-animation-section
        var rate = EffectivePlaybackRate;
        if (rate == 0)
            throw new DomException("Cannot finish an animation whose playback rate is 0.", "InvalidStateError");

        if (rate > 0 && double.IsPositiveInfinity(EffectEnd))
            throw new DomException("Cannot finish an animation whose effect is infinite.", "InvalidStateError");

        ApplyPendingPlaybackRate();
        var limit = _playbackRate > 0 ? EffectEnd : 0;
        SilentlySetCurrentTime(limit);
        if (_startTime is null && _timeline?.CurrentTimeValue is { } timelineTime)
        {
            _startTime = timelineTime - (limit / _playbackRate);
        }

        if (_pendingTask == PendingTask.Pause && _startTime is not null)
        {
            _holdTime = null;
            _pendingTask = PendingTask.None;
            ResolveReadyPromise();
        }

        if (_pendingTask == PendingTask.Play && _startTime is not null)
        {
            _pendingTask = PendingTask.None;
            ResolveReadyPromise();
        }

        UpdateFinishedState(didSeek: true, synchronouslyNotify: true);
        OnPlaybackChanged();
        OnFinishedByScript();
    }

    /// <summary>Plays or resumes the animation (https://drafts.csswg.org/web-animations-1/#dom-animation-play).</summary>
    /// <exception cref="DomException">The animation plays backwards and its effect is infinite (InvalidStateError).</exception>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-play"/>
    [IdlMember("play", "https://drafts.csswg.org/web-animations-1/#dom-animation-play")]
    public void Play()
    {
        FlushPendingStyle();
        PlayCore(autoRewind: true);
        OnPlayedByScript();
    }

    /// <summary>Pauses the animation (https://drafts.csswg.org/web-animations-1/#dom-animation-pause).</summary>
    /// <exception cref="DomException">The current time is unresolved, the animation plays backwards and its effect is infinite (InvalidStateError).</exception>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-pause"/>
    [IdlMember("pause", "https://drafts.csswg.org/web-animations-1/#dom-animation-pause")]
    public void Pause()
    {
        FlushPendingStyle();
        PauseCore();
        OnPausedByScript();
    }

    /// <summary>
    /// Sets the playback rate without a jump of the current time, once the animation is ready
    /// (https://drafts.csswg.org/web-animations-1/#dom-animation-updateplaybackrate).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-updateplaybackrate"/>
    [IdlMember("updatePlaybackRate", "https://drafts.csswg.org/web-animations-1/#dom-animation-updateplaybackrate")]
    public void UpdatePlaybackRate(double playbackRate)
    {
        FlushPendingStyle();

        // https://drafts.csswg.org/web-animations-1/#seamlessly-update-the-playback-rate
        var previousPlayState = PlayStateValue;
        _pendingPlaybackRate = playbackRate;
        if (_pendingTask != PendingTask.None)
        {
            // The pending playback rate is applied when the animation is ready
        }
        else if (previousPlayState is AnimationPlayState.Idle or AnimationPlayState.Paused || CurrentTimeValue is null)
        {
            ApplyPendingPlaybackRate();
        }
        else if (previousPlayState == AnimationPlayState.Finished)
        {
            var unconstrainedCurrentTime = GetCurrentTime(ignoreHoldTime: true);
            var timelineTime = _timeline?.CurrentTimeValue;
            double? newStartTime = playbackRate == 0 ? timelineTime : timelineTime - (unconstrainedCurrentTime / playbackRate);
            ApplyPendingPlaybackRate();
            _startTime = newStartTime;
            UpdateFinishedState(didSeek: false, synchronouslyNotify: false);
        }
        else
        {
            PlayCore(autoRewind: false);
        }

        OnPlaybackChanged();
    }

    /// <summary>Reverses the playback direction of the animation (https://drafts.csswg.org/web-animations-1/#dom-animation-reverse).</summary>
    /// <exception cref="DomException">The animation has no active timeline, or its effect is infinite (InvalidStateError).</exception>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-reverse"/>
    [IdlMember("reverse", "https://drafts.csswg.org/web-animations-1/#dom-animation-reverse")]
    public void Reverse()
    {
        FlushPendingStyle();

        // https://drafts.csswg.org/web-animations-1/#reverse-an-animation
        if (_timeline is not { IsActive: true })
            throw new DomException("Cannot reverse an animation that has no active timeline.", "InvalidStateError");

        OnReversingByScript();
        var originalPendingPlaybackRate = _pendingPlaybackRate;
        _pendingPlaybackRate = 0d - EffectivePlaybackRate;
        try
        {
            PlayCore(autoRewind: true);
        }
        catch
        {
            _pendingPlaybackRate = originalPendingPlaybackRate;
            throw;
        }

        OnReversedByScript();
    }

    /// <summary>Prevents the animation from being removed when it is replaced (https://drafts.csswg.org/web-animations-1/#dom-animation-persist).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animation-persist"/>
    [IdlMember("persist", "https://drafts.csswg.org/web-animations-1/#dom-animation-persist")]
    public void Persist()
    {
        if (ReplaceState == AnimationReplaceState.Removed)
        {
            ReplaceState = AnimationReplaceState.Persisted;
            OnPlaybackChanged();
        }
        else
        {
            ReplaceState = AnimationReplaceState.Persisted;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Hooks of the CSS animations and transitions (https://drafts.csswg.org/css-animations-2/#cssanimation-interface)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Applies the pending style changes that may update the animation (CSS animations and transitions).</summary>
    internal virtual void FlushPendingStyle()
    {
    }

    private protected virtual void OnTimelineSetByScript()
    {
    }

    private protected virtual void OnRangeSetByScript(bool isEnd)
    {
    }

    private protected virtual void OnStartTimeSetByScript()
    {
    }

    private protected virtual void OnCurrentTimeSetByScript()
    {
    }

    private protected virtual void OnCanceledByScript()
    {
    }

    private protected virtual void OnFinishedByScript()
    {
    }

    private protected virtual void OnPlayedByScript()
    {
    }

    private protected virtual void OnPausedByScript()
    {
    }

    private protected virtual void OnReversedByScript()
    {
    }

    private protected virtual void OnReversingByScript()
    {
    }

    /// <summary>Called when the timing of the effect is updated by updateTiming().</summary>
    internal virtual void OnEffectTimingUpdated(OptionalEffectTiming timing)
    {
    }

    /// <summary>Called when the keyframes or the composite operation of the effect are set by script.</summary>
    internal virtual void OnKeyframesSetByScript()
    {
    }

    /// <summary>Called when the effect changes (its timing or its keyframes).</summary>
    internal void OnEffectChanged()
    {
        UpdateFinishedState(didSeek: false, synchronouslyNotify: false);
        OnPlaybackChanged();
    }

    /// <summary>Called when the animation is removed because other animations replace its effect.</summary>
    internal void OnRemoved() => OnPlaybackChanged();

    /// <summary>Queues the events of the declarative animations (e.g. animationstart) after the animation is updated.</summary>
    internal virtual void QueueDeclarativeEvents()
    {
    }

    /// <summary>
    /// The class of the animation in the composite order (https://drafts.csswg.org/web-animations-1/#animation-class): 0 for
    /// the CSS transitions, 1 for the CSS animations, 2 for the other animations.
    /// </summary>
    internal virtual int CompositeOrderClass => 2;

    /// <summary>Compares two animations of the same class in the composite order (https://drafts.csswg.org/web-animations-1/#animation-composite-order).</summary>
    internal virtual int CompareCompositeOrderWithinClass(Animation other) => GlobalIndex.CompareTo(other.GlobalIndex);

    /// <summary>Compares two animations in the composite order: a negative number when this animation is applied first.</summary>
    internal int CompareCompositeOrder(Animation other)
    {
        var classA = CompositeOrderClass;
        var classB = other.CompositeOrderClass;
        if (classA != classB)
            return classA.CompareTo(classB);

        return CompareCompositeOrderWithinClass(other);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Algorithms (https://drafts.csswg.org/web-animations-1/#playing-an-animation-section)
    // ----------------------------------------------------------------------------------------------------------------

    private double? GetCurrentTime(bool ignoreHoldTime)
    {
        if (!ignoreHoldTime && _holdTime is { } holdTime)
            return holdTime;

        if (_timeline?.CurrentTimeValue is not { } timelineTime || _startTime is not { } startTime)
            return null;

        // The negative zero (e.g. with a negative playback rate) is zero
        return ((timelineTime - startTime) * _playbackRate) + 0d;
    }

    /// <summary>
    /// https://drafts.csswg.org/web-animations-2/#validate-a-cssnumberish-time: the time values of an animation of a
    /// progress-based timeline are percentages, the other ones are milliseconds.
    /// </summary>
    private double? ValidateTime(CssNumberish? value)
    {
        if (value?.Value is null)
            return null;

        if (_timeline is { IsProgressBased: true })
        {
            if (value.Value.Value is CssNumericValue numeric && ToPercent(numeric) is { } percent)
                return percent;

            throw new TypeErrorException("The time value of an animation of a progress-based timeline must be a percentage.");
        }

        switch (value.Value.Value)
        {
            case double number:
                return number;
            case CssUnitValue { Unit: "ms" or "number" } unit:
                return unit.Value;
            case CssUnitValue { Unit: "s" } unit:
                return unit.Value * 1000;
            default:
                throw new TypeErrorException("The time value must be a number of milliseconds.");
        }

        static double? ToPercent(CssNumericValue numeric)
        {
            if (numeric is CssUnitValue unit)
                return unit.Unit == "percent" ? unit.Value : null;

            try
            {
                return numeric.To("percent").Value;
            }
            catch (TypeErrorException)
            {
                return null;
            }
        }
    }

    /// <summary>https://drafts.csswg.org/web-animations-2/#setting-the-timeline</summary>
    private protected void SetTimelineCore(AnimationTimeline? timeline)
    {
        var oldTimeline = _timeline;
        if (ReferenceEquals(oldTimeline, timeline))
            return;

        var previousPlayState = PlayStateValue;
        var previousCurrentTime = CurrentTimeValue;
        var previousEndTime = EffectEnd;
        double? previousProgress = previousCurrentTime is not { } currentTime ? null : previousEndTime == 0 ? 0 : currentTime / previousEndTime;
        var fromFiniteTimeline = oldTimeline is { IsMonotonicallyIncreasing: false };
        var toFiniteTimeline = timeline is { IsMonotonicallyIncreasing: false };

        oldTimeline?.OnAnimationDetached(this);
        _timeline = timeline;
        timeline?.OnAnimationAttached(this);
        if (toFiniteTimeline)
        {
            ApplyPendingPlaybackRate();
            _autoAlignStartTime = true;
            _startTime = null;
            _holdTime = null;
            if (previousPlayState is AnimationPlayState.Finished or AnimationPlayState.Running)
            {
                // Schedule a pending play task
                if (_pendingTask == PendingTask.None)
                {
                    _readyPromise = CreatePromise();
                }

                _pendingTask = PendingTask.Play;
            }

            if (previousPlayState == AnimationPlayState.Paused && previousProgress is { } pausedProgress)
            {
                _holdTime = pausedProgress * EffectEnd;
            }
        }
        else if (fromFiniteTimeline && previousProgress is { } progress)
        {
            SetCurrentTime(progress * EffectEnd);
        }
        else if (fromFiniteTimeline && _pendingTask == PendingTask.Play && _startTime is null && _holdTime is null)
        {
            // A play-pending animation of an inactive progress-based timeline has no current time to keep: it starts from its
            // beginning on the monotonic timeline, as an idle animation that is played (https://drafts.csswg.org/web-animations-1/#playing-an-animation-section)
            _holdTime = EffectivePlaybackRate < 0 ? EffectEnd : 0;
        }

        if (_startTime is not null)
        {
            _holdTime = null;
        }

        UpdateFinishedState(didSeek: false, synchronouslyNotify: false);
        OnPlaybackChanged();
    }

    /// <summary>Sets the animation attachment range of the options of <c>animate()</c>.</summary>
    internal void SetRangeFromOptions(TimelineRangeBoundary? start, TimelineRangeBoundary? end) => SetRange(start, end);

    /// <summary>
    /// Sets the animation attachment range (https://drafts.csswg.org/web-animations-2/#dom-animation-rangestart): the timing of
    /// the effect is normalized again, and an auto-aligned start time is aligned with the new range.
    /// </summary>
    private protected void SetRange(TimelineRangeBoundary? start, TimelineRangeBoundary? end)
    {
        if (Equals(_rangeStart, start) && Equals(_rangeEnd, end))
            return;

        _rangeStart = start;
        _rangeEnd = end;
        UpdateAutoAlignedStartTime();
        UpdateFinishedState(didSeek: false, synchronouslyNotify: false);
        OnPlaybackChanged();
    }

    /// <summary>
    /// https://drafts.csswg.org/web-animations-2/#calculating-an-auto-aligned-start-time: when the timeline is updated, the start
    /// time of an animation of a progress-based timeline is aligned with the start of its attachment range (its end when it
    /// plays backwards). Returns whether the start time or the hold time changed.
    /// </summary>
    internal bool UpdateAutoAlignedStartTime()
    {
        if (!_autoAlignStartTime || _timeline is not { IsActive: true } timeline)
            return false;

        var playState = PlayStateValue;
        if (playState == AnimationPlayState.Idle || (playState == AnimationPlayState.Paused && _holdTime is not null))
            return false;

        var startOffset = timeline.GetAttachmentFraction(_rangeStart, isEnd: false) * 100;
        var endOffset = timeline.GetAttachmentFraction(_rangeEnd, isEnd: true) * 100;
        double newStartTime = EffectivePlaybackRate >= 0 ? startOffset : endOffset;
        var changed = _startTime != newStartTime || _holdTime is not null || _pendingPlaybackRate is not null;
        _startTime = newStartTime;
        ApplyPendingPlaybackRate();
        _holdTime = null;
        return changed;
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#setting-the-associated-effect</summary>
    private protected void SetEffectCore(AnimationEffect? effect)
    {
        var oldEffect = _effect;
        if (ReferenceEquals(oldEffect, effect))
            return;

        if (effect is null && oldEffect is not null)
        {
            ResetPendingTasks();
        }

        if (effect?.Animation is { } previousAnimation && !ReferenceEquals(previousAnimation, this))
        {
            previousAnimation.SetEffectCore(null);
        }

        var targetsBefore = oldEffect;
        _effect = effect;
        if (oldEffect is not null)
        {
            oldEffect.Animation = null;
            oldEffect.OnAnimationChanged(this);
        }

        if (effect is not null)
        {
            effect.Animation = this;
            effect.OnAnimationChanged(null);
        }

        _ = targetsBefore;
        UpdateFinishedState(didSeek: false, synchronouslyNotify: false);
        OnPlaybackChanged();
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#silently-set-the-current-time</summary>
    private void SilentlySetCurrentTime(double? seekTime)
    {
        if (seekTime is not { } time)
        {
            if (CurrentTimeValue is not null)
                throw new TypeErrorException("The current time cannot be set to null.");

            return;
        }

        _autoAlignStartTime = false;
        if (_holdTime is not null || _startTime is null || _timeline is not { IsActive: true } || _playbackRate == 0)
        {
            _holdTime = time;
        }
        else
        {
            _startTime = _timeline.CurrentTimeValue!.Value - (time / _playbackRate);
        }

        if (_timeline is not { IsActive: true })
        {
            _startTime = null;
        }

        _previousCurrentTime = null;
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#setting-the-current-time-of-an-animation</summary>
    private protected void SetCurrentTime(double? seekTime)
    {
        SilentlySetCurrentTime(seekTime);
        if (_pendingTask == PendingTask.Pause)
        {
            _holdTime = seekTime;
            ApplyPendingPlaybackRate();
            _startTime = null;
            _pendingTask = PendingTask.None;
            ResolveReadyPromise();
        }

        UpdateFinishedState(didSeek: true, synchronouslyNotify: false);
        OnPlaybackChanged();
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#setting-the-start-time-of-an-animation</summary>
    private protected void SetStartTimeCore(double? newStartTime)
    {
        _autoAlignStartTime = false;
        var timelineTime = _timeline?.CurrentTimeValue;
        if (timelineTime is null && newStartTime is not null)
        {
            _holdTime = null;
        }

        var previousCurrentTime = CurrentTimeValue;
        ApplyPendingPlaybackRate();
        _startTime = newStartTime;
        if (newStartTime is not null)
        {
            if (_playbackRate != 0)
            {
                _holdTime = null;
            }
        }
        else
        {
            _holdTime = previousCurrentTime;
        }

        if (_pendingTask != PendingTask.None)
        {
            _pendingTask = PendingTask.None;
            ResolveReadyPromise();
        }

        UpdateFinishedState(didSeek: true, synchronouslyNotify: false);
        OnPlaybackChanged();
    }

    /// <summary>https://drafts.csswg.org/web-animations-2/#playing-an-animation-section</summary>
    private protected void PlayCore(bool autoRewind)
    {
        var abortedPause = _pendingTask == PendingTask.Pause;
        var hasPendingReadyPromise = false;
        double? seekTime = null;
        var rate = EffectivePlaybackRate;
        var currentTime = CurrentTimeValue;
        var effectEnd = EffectEnd;
        var hasFiniteTimeline = _timeline is { IsMonotonicallyIncreasing: false };
        if (autoRewind && !hasFiniteTimeline)
        {
            if (rate >= 0 && (currentTime is not { } time || time < 0 || time >= effectEnd))
            {
                seekTime = 0;
            }
            else if (rate < 0 && (currentTime is not { } time2 || time2 <= 0 || time2 > effectEnd))
            {
                if (double.IsPositiveInfinity(effectEnd))
                    throw new DomException("Cannot play an infinite animation backwards.", "InvalidStateError");

                seekTime = effectEnd;
            }
        }

        if (seekTime is null && rate == 0 && currentTime is null)
        {
            seekTime = 0;
        }

        if (seekTime is not null)
        {
            _holdTime = seekTime;
        }

        // The start time of an animation of a progress-based timeline is aligned with its attachment range once the timeline is
        // updated (https://drafts.csswg.org/web-animations-2/#auto-aligning-start-time)
        var autoAligned = false;
        if (hasFiniteTimeline && autoRewind)
        {
            _autoAlignStartTime = true;
            _holdTime = currentTime;
            autoAligned = true;
        }

        if (_holdTime is not null)
        {
            _startTime = null;
        }

        if (_pendingTask != PendingTask.None)
        {
            _pendingTask = PendingTask.None;
            hasPendingReadyPromise = true;
        }

        // An animation whose start time is auto-aligned waits for it even when it has no current time
        if (_holdTime is null && seekTime is null && !abortedPause && _pendingPlaybackRate is null && !(autoAligned && _startTime is null))
        {
            OnPlaybackChanged();
            return;
        }

        if (!hasPendingReadyPromise)
        {
            _readyPromise = CreatePromise();
        }

        _pendingTask = PendingTask.Play;
        UpdateFinishedState(didSeek: false, synchronouslyNotify: false);
        OnPlaybackChanged();
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#pause-an-animation</summary>
    private protected void PauseCore()
    {
        if (_pendingTask == PendingTask.Pause || PlayStateValue == AnimationPlayState.Paused)
            return;

        // https://drafts.csswg.org/web-animations-2/#pausing-an-animation-section: the hold time of an animation of a
        // progress-based timeline is set once its start time is aligned with its attachment range
        var hasFiniteTimeline = _timeline is { IsMonotonicallyIncreasing: false };
        if (hasFiniteTimeline && CurrentTimeValue is null)
        {
            _autoAlignStartTime = true;
        }
        else if (CurrentTimeValue is null)
        {
            if (_playbackRate >= 0)
            {
                _holdTime = 0;
            }
            else
            {
                if (double.IsPositiveInfinity(EffectEnd))
                    throw new DomException("Cannot pause an infinite animation that plays backwards.", "InvalidStateError");

                _holdTime = EffectEnd;
            }
        }

        var hasPendingReadyPromise = false;
        if (_pendingTask == PendingTask.Play)
        {
            _pendingTask = PendingTask.None;
            hasPendingReadyPromise = true;
        }

        if (!hasPendingReadyPromise)
        {
            _readyPromise = CreatePromise();
        }

        _pendingTask = PendingTask.Pause;
        UpdateFinishedState(didSeek: false, synchronouslyNotify: false);
        OnPlaybackChanged();
    }

    /// <summary>
    /// Runs the pending play or pause task when the animation is ready (https://drafts.csswg.org/web-animations-1/#ready):
    /// the ready time is the current time of the timeline. Returns whether a task ran.
    /// </summary>
    internal bool RunPendingTask()
    {
        // https://drafts.csswg.org/web-animations-2/#ready: the animation also needs a start time or a hold time
        if (_pendingTask == PendingTask.None || _timeline?.CurrentTimeValue is not { } readyTime || (_startTime is null && _holdTime is null))
            return false;

        if (_pendingTask == PendingTask.Play)
        {
            // https://drafts.csswg.org/web-animations-1/#playing-an-animation-section (the pending play task)
            if (_holdTime is { } holdTime)
            {
                ApplyPendingPlaybackRate();
                _startTime = _playbackRate == 0 ? readyTime : readyTime - (holdTime / _playbackRate);
                if (_playbackRate != 0)
                {
                    _holdTime = null;
                }
            }
            else if (_startTime is { } startTime && _pendingPlaybackRate is not null)
            {
                var currentTimeToMatch = (readyTime - startTime) * _playbackRate;
                ApplyPendingPlaybackRate();
                if (_playbackRate == 0)
                {
                    _holdTime = currentTimeToMatch;
                }

                _startTime = _playbackRate == 0 ? readyTime : readyTime - (currentTimeToMatch / _playbackRate);
            }
        }
        else
        {
            // https://drafts.csswg.org/web-animations-1/#pausing-an-animation-section (the pending pause task)
            if (_startTime is { } startTime && _holdTime is null)
            {
                _holdTime = (readyTime - startTime) * _playbackRate;
            }

            ApplyPendingPlaybackRate();
            _startTime = null;
        }

        _pendingTask = PendingTask.None;
        ResolveReadyPromise();
        UpdateFinishedState(didSeek: false, synchronouslyNotify: false);
        OnPlaybackChanged();
        return true;
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#apply-any-pending-playback-rate</summary>
    private void ApplyPendingPlaybackRate()
    {
        if (_pendingPlaybackRate is { } rate)
        {
            _playbackRate = rate;
            _pendingPlaybackRate = null;
        }
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#reset-an-animations-pending-tasks</summary>
    private void ResetPendingTasks()
    {
        if (_pendingTask == PendingTask.None)
            return;

        _pendingTask = PendingTask.None;
        ApplyPendingPlaybackRate();
        RejectPromise(_readyPromise);
        _readyPromise = CreatePromise();
        _readyPromise.SetResult(this);
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#cancel-an-animation</summary>
    private protected void CancelCore()
    {
        if (PlayStateValue != AnimationPlayState.Idle)
        {
            ResetPendingTasks();
            RejectPromise(_finishedPromise);
            _finishedPromise = CreatePromise();
            _finishNotificationQueued = false;

            var timelineTime = _timeline?.CurrentTimeValue;
            var cancelEvent = new AnimationPlaybackEvent("cancel", new AnimationPlaybackEventInit { CurrentTime = null, TimelineTime = timelineTime is { } t ? ToCssNumberish(t) : (CssNumberish?)null }) { RealmWindow = _realmWindow };
            QueuePlaybackEvent(cancelEvent, _timeline?.ToOriginRelativeTime(timelineTime));
        }

        _holdTime = null;
        _startTime = null;
        OnPlaybackChanged();
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#update-an-animations-finished-state</summary>
    internal void UpdateFinishedState(bool didSeek, bool synchronouslyNotify)
    {
        var unconstrainedCurrentTime = didSeek ? CurrentTimeValue : GetCurrentTime(ignoreHoldTime: true);
        if (unconstrainedCurrentTime is { } unconstrained && _startTime is not null && _pendingTask == PendingTask.None)
        {
            // The current time of an animation whose start time is aligned with the attachment range of a progress-based timeline
            // is clamped to the range: its timeline may move backwards, so the previous current time is not kept (as in the
            // browsers, WPT change-animation-range-updates-play-state.html)
            var effectEnd = EffectEnd;
            var progressBased = _autoAlignStartTime && _timeline is { IsProgressBased: true };
            if (_playbackRate > 0 && unconstrained >= effectEnd)
            {
                _holdTime = didSeek ? unconstrained : progressBased ? effectEnd : Math.Max(_previousCurrentTime ?? effectEnd, effectEnd);
            }
            else if (_playbackRate < 0 && unconstrained <= 0)
            {
                _holdTime = didSeek ? unconstrained : progressBased ? 0 : Math.Min(_previousCurrentTime ?? 0, 0);
            }
            else if (_playbackRate != 0 && _timeline?.CurrentTimeValue is { } timelineTime)
            {
                if (didSeek && _holdTime is { } holdTime)
                {
                    _startTime = timelineTime - (holdTime / _playbackRate);
                }

                _holdTime = null;
            }
        }

        _previousCurrentTime = CurrentTimeValue;
        var finished = PlayStateValue == AnimationPlayState.Finished;
        if (finished && !_finishedPromise.Task.IsCompleted)
        {
            if (synchronouslyNotify)
            {
                _finishNotificationQueued = false;
                RunFinishNotificationSteps();
            }
            else if (!_finishNotificationQueued)
            {
                _finishNotificationQueued = true;
                var promise = _finishedPromise;
                QueueMicrotask(() =>
                {
                    if (!_finishNotificationQueued || !ReferenceEquals(promise, _finishedPromise))
                        return;

                    _finishNotificationQueued = false;
                    RunFinishNotificationSteps();
                });
            }
        }

        if (!finished && _finishedPromise.Task.IsCompleted)
        {
            _finishedPromise = CreatePromise();
        }
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#finish-notification-steps</summary>
    private void RunFinishNotificationSteps()
    {
        if (PlayStateValue != AnimationPlayState.Finished)
            return;

        _finishedPromise.TrySetResult(this);
        var timelineTime = _timeline?.CurrentTimeValue;
        var finishEvent = new AnimationPlaybackEvent("finish", new AnimationPlaybackEventInit
        {
            CurrentTime = CurrentTimeValue is { } currentTime ? ToCssNumberish(currentTime) : (CssNumberish?)null,
            TimelineTime = timelineTime is { } t ? ToCssNumberish(t) : (CssNumberish?)null,
        })
        { RealmWindow = _realmWindow };

        // The scheduled event time is the effect end converted to an origin-relative time
        double? scheduledTime = null;
        if (_startTime is { } startTime && _playbackRate != 0)
        {
            scheduledTime = _timeline?.ToOriginRelativeTime(startTime + (EffectEnd / _playbackRate));
        }

        QueuePlaybackEvent(finishEvent, scheduledTime);
    }

    /// <summary>Appends a playback event to the pending animation event queue of the document for timing, or queues a task to dispatch it.</summary>
    private void QueuePlaybackEvent(AnimationPlaybackEvent playbackEvent, double? scheduledTime)
    {
        if (DocumentForTiming is { } document)
        {
            document.Animations.QueueEvent(playbackEvent, this, this, scheduledTime);
            return;
        }

        if (RelevantGlobal is { } window)
        {
            window.QueueGlobalTask(() => FireEvent(playbackEvent));
        }
    }

    /// <summary>Queues a microtask on the event loop of the animation; without event loop, the callback runs synchronously.</summary>
    private void QueueMicrotask(Action callback)
    {
        if ((DocumentForTiming?.EventLoop ?? RelevantGlobal?.EventLoop) is { } eventLoop)
        {
            eventLoop.QueueMicrotask(callback);
        }
    }

    private void ResolveReadyPromise() => _readyPromise.TrySetResult(this);

    private static TaskCompletionSource<Animation> CreatePromise()
    {
        var promise = new TaskCompletionSource<Animation>(TaskCreationOptions.None);
        PromiseSlots.Register(promise.Task);
        return promise;
    }

    /// <summary>Rejects a promise with an AbortError and marks it as handled.</summary>
    private static void RejectPromise(TaskCompletionSource<Animation> promise)
    {
        if (promise.Task.IsCompleted)
            return;

        PromiseSlots.MarkAsHandled(promise.Task);
        promise.TrySetException(new DomException("The animation was aborted.", "AbortError"));
    }

    /// <summary>
    /// Called after any change of the playback state, the timeline or the effect: the animation is tracked by its document
    /// while it may change, and the styles of its target are invalidated.
    /// </summary>
    private protected void OnPlaybackChanged()
    {
        if (DocumentForTiming is { } document)
        {
            document.Animations.OnAnimationChanged(this);
        }

        if (_effect is KeyframeEffect { TargetElement: { } target } && !ReferenceEquals(target.NodeDocument, DocumentForTiming))
        {
            target.NodeDocument.Animations.OnAnimationChanged(this);
        }
    }

    /// <summary>Whether the animation needs the next frames: it has a pending task, or its current time changes with the timeline.</summary>
    internal bool NeedsFrames
    {
        get
        {
            // The current time of a progress-based timeline changes with the layout and the scroll positions, not with the frames:
            // its updates track the animation again (see DocumentAnimations.UpdateScrollDrivenTimelines)
            if (_pendingTask != PendingTask.None)
                return _timeline is null or { IsMonotonicallyIncreasing: true } or { IsActive: true };

            if (_startTime is null || _holdTime is not null || _playbackRate == 0)
                return false;

            return _timeline is { IsActive: true, IsMonotonicallyIncreasing: true };
        }
    }
}
