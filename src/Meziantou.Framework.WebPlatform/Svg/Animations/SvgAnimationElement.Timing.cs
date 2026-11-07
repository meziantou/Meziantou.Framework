using Meziantou.Framework.WebPlatform.Svg;
using Meziantou.Framework.WebPlatform.Svg.Animations;

namespace Meziantou.Framework.WebPlatform.Dom;

// The timing model of the animation elements (https://svgwg.org/specs/animations/#TimingAttributes, which refers to SMIL
// Animation, https://www.w3.org/TR/2001/REC-smil-animation-20010904/, and to the timing model of SMIL 3,
// https://www.w3.org/TR/SMIL3/smil-timing.html): the begin and end instance lists, the resolution of the intervals, the
// active state and the timing events.
public partial class SvgAnimationElement
{
    private enum InstanceOrigin
    {
        Attribute,
        Script,
        Event,
        SyncBase,
    }

    private enum ConditionKind
    {
        /// <summary>An event of an element (<c>id.click</c>, <c>click</c>).</summary>
        Event,

        /// <summary>The begin or the end of an interval of an animation element (<c>id.begin</c>, <c>id.end</c>).</summary>
        SyncBase,

        /// <summary>An iteration of an animation element (<c>id.repeat(2)</c>).</summary>
        Repeat,

        /// <summary>A key (<c>accessKey(a)</c>).</summary>
        AccessKey,
    }

    private sealed class Condition
    {
        public required ConditionKind Kind { get; init; }

        public required bool IsBegin { get; init; }

        /// <summary>The identifier of the element the condition refers to, or an empty string (the target element for an event, the element itself for a repeat).</summary>
        public required string BaseId { get; init; }

        /// <summary>The name of the event, <c>begin</c> or <c>end</c> for a syncbase value, the character of an access key.</summary>
        public required string Name { get; init; }

        public required SmilTime Offset { get; init; }

        public int Repeat { get; init; }

        /// <summary>The element the condition is connected to (the document for an access key).</summary>
        public EventTarget? Base { get; set; }

        public EventListenerEntry? Listener { get; set; }

        /// <summary>The instance time created from the current interval of the syncbase element.</summary>
        public SmilTime? SyncBaseInstance { get; set; }
    }

    private readonly List<(SmilTime Time, InstanceOrigin Origin)> _beginTimes = [];
    private readonly List<(SmilTime Time, InstanceOrigin Origin)> _endTimes = [];
    private readonly List<Condition> _conditions = [];
    private bool _hasEndConditions;
    private bool _timingAttributesParsed;
    private bool _instanceListsChanged;
    private SmilInterval _interval = SmilInterval.Unresolved;
    private SmilInterval _previousInterval = SmilInterval.Unresolved;
    private bool _waitingForFirstInterval = true;
    private SmilActiveState _activeState;
    private SmilTime _activeBegin = SmilTime.Unresolved;
    private long _lastRepeat;

    internal enum SmilActiveState
    {
        Inactive,
        Active,
        Frozen,
    }

    /// <summary>The time container of the document fragment of the element, or null when the element is not in an svg element.</summary>
    internal SmilTimeContainer? TimeContainer { get; set; }

    /// <summary>The index of the element in the document order of the animation elements.</summary>
    internal int DocumentOrder { get; set; }

    internal bool IsActive => _activeState == SmilActiveState.Active;

    /// <summary>The begin of the interval the element is active in.</summary>
    internal SmilTime ActiveBegin => _activeBegin;

    // ----------------------------------------------------------------------------------------------------------------
    // Lifecycle
    // ----------------------------------------------------------------------------------------------------------------

    internal override void InsertionSteps()
    {
        base.InsertionSteps();
        if (IsConnected && NodeDocument.SmilEnabled)
        {
            NodeDocument.Smil.Register(this);
        }
    }

    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        if (!IsConnected && oldAncestor.NodeDocument.SmilOrNull is { } smil)
        {
            smil.Unregister(this);
            ResetTiming(keepInstanceTimes: false);
            DisconnectConditions();
            _target = null;
            _targetKind = SmilTargetKind.None;
        }
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is not null && !(localName == "href" && @namespace == Namespaces.XLink))
            return;

        switch (localName)
        {
            case "begin" or "end":
                if (_timingAttributesParsed)
                {
                    ParseBeginOrEnd(value, isBegin: localName == "begin");
                    _instanceListsChanged = true;
                    _referencesResolved = false;
                }

                break;

            case "dur" or "repeatDur" or "repeatCount" or "min" or "max" or "restart":
                // The end of the current interval is computed again
                _instanceListsChanged = true;
                break;
        }

        _valuesVersion++;
        if (IsConnected)
        {
            NodeDocument.SmilOrNull?.SetDirty();
        }
    }

    internal override void ChildrenChangedSteps(bool childrenInserted)
    {
        base.ChildrenChangedSteps(childrenInserted);

        // The mpath child of an animateMotion element
        _valuesVersion++;
        if (IsConnected)
        {
            NodeDocument.SmilOrNull?.SetDirty();
        }
    }

    /// <summary>Resets the intervals and the active state of the element.</summary>
    /// <param name="keepInstanceTimes">Whether the instance times are kept (a seek), or parsed again from the attributes.</param>
    internal void ResetTiming(bool keepInstanceTimes)
    {
        _interval = SmilInterval.Unresolved;
        _previousInterval = SmilInterval.Unresolved;
        _waitingForFirstInterval = true;
        _activeState = SmilActiveState.Inactive;
        _activeBegin = SmilTime.Unresolved;
        _lastRepeat = 0;
        _instanceListsChanged = false;
        foreach (var condition in _conditions)
        {
            condition.SyncBaseInstance = null;
        }

        if (!keepInstanceTimes)
        {
            _timingAttributesParsed = false;
            _referencesResolved = false;
        }
    }

    private void EnsureTimingAttributesParsed()
    {
        if (_timingAttributesParsed)
            return;

        _timingAttributesParsed = true;
        DisconnectConditions();
        _conditions.Clear();
        _beginTimes.Clear();
        _endTimes.Clear();
        _hasEndConditions = false;
        ParseBeginOrEnd(GetAttributeValue("begin"), isBegin: true);
        ParseBeginOrEnd(GetAttributeValue("end"), isBegin: false);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The begin and end attributes (https://svgwg.org/specs/animations/#BeginValueListSyntax)
    // ----------------------------------------------------------------------------------------------------------------

    private void ParseBeginOrEnd(string? value, bool isBegin)
    {
        var times = isBegin ? _beginTimes : _endTimes;
        times.RemoveAll(static time => time.Origin is InstanceOrigin.Attribute or InstanceOrigin.SyncBase);
        foreach (var condition in _conditions)
        {
            if (condition.IsBegin == isBegin)
            {
                Disconnect(condition);
            }
        }

        _conditions.RemoveAll(condition => condition.IsBegin == isBegin);
        if (!isBegin)
        {
            _hasEndConditions = false;
        }

        if (value is null)
        {
            // "If no attribute is present, the default begin value (an offset-value of 0) must be evaluated"
            if (isBegin)
            {
                AddInstanceTime(times, SmilTime.Zero, InstanceOrigin.Attribute);
            }

            return;
        }

        foreach (var item in value.Split(';'))
        {
            var text = item.Trim(' ', '\t', '\n', '\f', '\r');
            if (text.Length == 0)
                continue;

            if (text == "indefinite")
                continue;

            // An offset value: a clock value with an optional sign
            var sign = text[0] is '+' or '-' ? 1 : 0;
            var clock = SmilTime.ParseClockValue(sign == 1 ? text[1..].TrimStart() : text, allowIndefinite: false);
            if (!clock.IsUnresolved)
            {
                AddInstanceTime(times, text[0] == '-' ? -clock : clock, InstanceOrigin.Attribute);
                continue;
            }

            if (ParseCondition(text, isBegin) is { } condition)
            {
                _conditions.Add(condition);

                // The conditions that may generate more end times (as in the browsers, the syncbase values do not)
                if (!isBegin && condition.Kind != ConditionKind.SyncBase)
                {
                    _hasEndConditions = true;
                }
            }
        }
    }

    private static Condition? ParseCondition(string text, bool isBegin)
    {
        // The offset: the last sign that is not escaped nor in parentheses
        var offset = SmilTime.Zero;
        var name = text;
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\')
            {
                i++;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
            }
            else if (c is '+' or '-' && depth == 0 && i > 0)
            {
                // An identifier can contain a hyphen: the offset is a sign followed by a clock value
                var clock = SmilTime.ParseClockValue(text[(i + 1)..], allowIndefinite: false);
                if (clock.IsUnresolved)
                {
                    if (c == '+')
                        return null;

                    continue;
                }

                offset = c == '-' ? -clock : clock;
                name = text[..i].TrimEnd(' ', '\t', '\n', '\f', '\r');
                break;
            }
        }

        if (name.Length == 0)
            return null;

        if (name.StartsWith("accessKey(", StringComparison.Ordinal) && name.EndsWith(')', StringComparison.Ordinal) && name.Length > 11)
            return new Condition { Kind = ConditionKind.AccessKey, IsBegin = isBegin, BaseId = "", Name = name[10..^1], Offset = offset };

        // The identifier and the name are separated by the last dot that is not escaped
        var baseId = "";
        var dot = -1;
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '\\')
            {
                i++;
            }
            else if (name[i] == '.')
            {
                dot = i;
            }
            else if (name[i] == '(')
            {
                break;
            }
        }

        if (dot >= 0)
        {
            baseId = Unescape(name[..dot]);
            name = name[(dot + 1)..];
            if (baseId.Length == 0)
                return null;
        }

        name = Unescape(name);
        if (name.Length == 0)
            return null;

        if (name.StartsWith("repeat(", StringComparison.Ordinal) && name.EndsWith(')', StringComparison.Ordinal))
        {
            if (!int.TryParse(name.AsSpan(7, name.Length - 8), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var repeat))
                return null;

            return new Condition { Kind = ConditionKind.Repeat, IsBegin = isBegin, BaseId = baseId, Name = "repeat", Offset = offset, Repeat = repeat };
        }

        if (name is "begin" or "end" && baseId.Length > 0)
            return new Condition { Kind = ConditionKind.SyncBase, IsBegin = isBegin, BaseId = baseId, Name = name, Offset = offset };

        return new Condition { Kind = ConditionKind.Event, IsBegin = isBegin, BaseId = baseId, Name = name, Offset = offset };
    }

    private static string Unescape(string value) => value.Contains('\\', StringComparison.Ordinal) ? value.Replace("\\", "", StringComparison.Ordinal) : value;

    private bool AddInstanceTime(List<(SmilTime Time, InstanceOrigin Origin)> times, SmilTime time, InstanceOrigin origin)
    {
        if (!time.IsFinite)
            return false;

        var index = 0;
        while (index < times.Count && times[index].Time < time)
        {
            index++;
        }

        // The instance times are unique
        if (index < times.Count && times[index].Time == time)
            return false;

        times.Insert(index, (time, origin));
        _instanceListsChanged = true;
        return true;
    }

    private void RemoveInstanceTime(List<(SmilTime Time, InstanceOrigin Origin)> times, SmilTime time, InstanceOrigin origin)
    {
        for (var i = 0; i < times.Count; i++)
        {
            if (times[i].Time == time && times[i].Origin == origin)
            {
                times.RemoveAt(i);
                _instanceListsChanged = true;
                return;
            }
        }
    }

    /// <summary>Adds an instance time created by an event or a script, and updates the timeline.</summary>
    private void AddInstanceTimeAndUpdate(bool isBegin, SmilTime offset, InstanceOrigin origin)
    {
        if (!IsConnected || NodeDocument.SmilOrNull is not { } smil)
            return;

        // The time containers of the elements and the structure are resolved, and the timeline is updated to the current time
        smil.UpdateTimelines();
        if (TimeContainer is not { } container)
            return;

        EnsureTimingAttributesParsed();

        // https://www.w3.org/TR/SMIL3/smil-timing.html#q72 (event sensitivity): while the element is not active, the end events are
        // ignored; while it is active, the begin events are ignored unless the element can restart
        if (!isBegin && _activeState != SmilActiveState.Active)
            return;

        if (isBegin && _activeState == SmilActiveState.Active && GetRestart() != RestartMode.Always)
            return;

        if (isBegin && GetRestart() == RestartMode.Never && !_waitingForFirstInterval)
            return;

        // Before the timeline begins, the current time is the time it begins at
        var time = container.Elapsed + offset;
        if (AddInstanceTime(isBegin ? _beginTimes : _endTimes, time, origin))
        {
            smil.SetDirty();
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The conditions: events, syncbase values and repeat values
    // ----------------------------------------------------------------------------------------------------------------

    private bool _referencesResolved;

    /// <summary>Resolves the target element and the elements the conditions refer to, after a change of the document.</summary>
    internal void ResolveReferences()
    {
        EnsureTimingAttributesParsed();
        ResolveTarget();
        foreach (var condition in _conditions)
        {
            EventTarget? element = condition.Kind switch
            {
                ConditionKind.AccessKey => NodeDocument,
                ConditionKind.Event when condition.BaseId.Length == 0 => TargetElement,
                ConditionKind.Repeat when condition.BaseId.Length == 0 => this,
                ConditionKind.Event => GetElementInTree(condition.BaseId),
                _ => GetElementInTree(condition.BaseId) as SvgAnimationElement,
            };

            if (ReferenceEquals(element, condition.Base) && _referencesResolved)
                continue;

            Disconnect(condition);
            condition.Base = element;
            switch (condition.Kind)
            {
                case ConditionKind.Event when element is not null:
                    condition.Listener = new EventListenerEntry
                    {
                        Type = condition.Name,
                        Callback = new Action<Event>(_ => AddInstanceTimeAndUpdate(condition.IsBegin, condition.Offset, InstanceOrigin.Event)),
                    };
                    element.AddEventListener(condition.Listener);
                    break;

                case ConditionKind.AccessKey when element is not null:
                    // https://www.w3.org/TR/SMIL3/smil-timing.html#q20: the key that the user presses
                    condition.Listener = new EventListenerEntry
                    {
                        Type = "keydown",
                        Callback = new Action<Event>(e =>
                        {
                            if (e is KeyboardEvent keyboardEvent && string.Equals(keyboardEvent.Key, condition.Name, StringComparison.Ordinal))
                            {
                                AddInstanceTimeAndUpdate(condition.IsBegin, condition.Offset, InstanceOrigin.Event);
                            }
                        }),
                    };
                    element.AddEventListener(condition.Listener);
                    break;

                case ConditionKind.SyncBase when element is SvgAnimationElement syncBase && syncBase._interval.IsResolved:
                    // The current interval of the syncbase element creates an instance time
                    CreateSyncBaseInstance(condition, syncBase._interval);
                    break;
            }
        }

        _referencesResolved = true;
    }

    private protected Element? GetElementInTree(string id) => GetRoot() switch
    {
        Document document => document.GetElementById(id),
        DocumentFragment fragment => fragment.GetElementById(id),
        _ => null,
    };

    private void DisconnectConditions()
    {
        foreach (var condition in _conditions)
        {
            Disconnect(condition);
        }
    }

    private void Disconnect(Condition condition)
    {
        if (condition.Listener is { } listener)
        {
            condition.Base?.RemoveEventListener(listener);
            condition.Listener = null;
        }

        if (condition.SyncBaseInstance is { } instance)
        {
            RemoveInstanceTime(condition.IsBegin ? _beginTimes : _endTimes, instance, InstanceOrigin.SyncBase);
            condition.SyncBaseInstance = null;
        }

        condition.Base = null;
    }

    private void CreateSyncBaseInstance(Condition condition, SmilInterval interval)
    {
        var times = condition.IsBegin ? _beginTimes : _endTimes;
        var time = (condition.Name == "begin" ? interval.Begin : interval.End) + condition.Offset;
        if (condition.SyncBaseInstance is { } previous)
        {
            if (previous == time)
                return;

            // The instance time of the same interval changes with it
            RemoveInstanceTime(times, previous, InstanceOrigin.SyncBase);
            condition.SyncBaseInstance = null;
        }

        if (AddInstanceTime(times, time, InstanceOrigin.SyncBase))
        {
            condition.SyncBaseInstance = time;
        }
    }

    /// <summary>Notifies the elements whose conditions depend on this element that it has a new interval, or that the end of its interval changed.</summary>
    private void NotifyDependents(bool newInterval)
    {
        if (NodeDocument.SmilOrNull is not { } smil)
            return;

        foreach (var dependent in smil.Elements)
        {
            foreach (var condition in dependent._conditions)
            {
                if (condition.Kind != ConditionKind.SyncBase || !ReferenceEquals(condition.Base, this))
                    continue;

                if (newInterval)
                {
                    // The instance times of the previous intervals are kept
                    condition.SyncBaseInstance = null;
                }

                dependent.CreateSyncBaseInstance(condition, _interval);
            }
        }
    }

    /// <summary>Notifies the dependents that the interval that created instance times is discarded before it began.</summary>
    private void NotifyDependentsOfDiscardedInterval()
    {
        if (NodeDocument.SmilOrNull is not { } smil)
            return;

        foreach (var dependent in smil.Elements)
        {
            foreach (var condition in dependent._conditions)
            {
                if (condition.Kind == ConditionKind.SyncBase && ReferenceEquals(condition.Base, this) && condition.SyncBaseInstance is { } instance)
                {
                    dependent.RemoveInstanceTime(condition.IsBegin ? dependent._beginTimes : dependent._endTimes, instance, InstanceOrigin.SyncBase);
                    condition.SyncBaseInstance = null;
                }
            }
        }
    }

    private void NotifyDependentsOfRepeat(long repeat, SmilTime time)
    {
        if (NodeDocument.SmilOrNull is not { } smil)
            return;

        foreach (var dependent in smil.Elements)
        {
            foreach (var condition in dependent._conditions)
            {
                if (condition.Kind == ConditionKind.Repeat && ReferenceEquals(condition.Base, this) && condition.Repeat == repeat)
                {
                    dependent.AddInstanceTime(condition.IsBegin ? dependent._beginTimes : dependent._endTimes, time + condition.Offset, InstanceOrigin.SyncBase);
                }
            }
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The timing attributes
    // ----------------------------------------------------------------------------------------------------------------

    private enum RestartMode
    {
        Always,
        WhenNotActive,
        Never,
    }

    private RestartMode GetRestart() => GetAttributeValue("restart") switch
    {
        "whenNotActive" => RestartMode.WhenNotActive,
        "never" => RestartMode.Never,
        _ => RestartMode.Always,
    };

    private bool IsFrozenFill => GetAttributeValue("fill") == "freeze";

    /// <summary>The simple duration (https://www.w3.org/TR/SMIL3/smil-timing.html#adef-dur): indefinite when it is not specified or invalid.</summary>
    internal SmilTime SimpleDuration
    {
        get
        {
            var duration = SmilTime.ParseClockValue(GetAttributeValue("dur"));
            return duration.IsUnresolved || duration <= SmilTime.Zero ? SmilTime.Indefinite : duration;
        }
    }

    private SmilTime RepeatDuration
    {
        get
        {
            var duration = SmilTime.ParseClockValue(GetAttributeValue("repeatDur"));
            return duration.IsUnresolved || duration <= SmilTime.Zero ? SmilTime.Unresolved : duration;
        }
    }

    /// <summary>The repeat count: a positive number, infinity for <c>indefinite</c>, or null when it is not specified or invalid.</summary>
    private double? RepeatCount
    {
        get
        {
            var value = GetAttributeValue("repeatCount");
            if (value is null)
                return null;

            if (value.AsSpan().Trim(" \t\n\f\r") is "indefinite")
                return double.PositiveInfinity;

            return SvgParsing.TryParseSingleNumber(value, out var count) && count > 0 ? count : null;
        }
    }

    /// <summary>https://www.w3.org/TR/SMIL3/smil-timing.html#q82: the duration of the repeated simple duration.</summary>
    private SmilTime RepeatingDuration
    {
        get
        {
            var repeatCount = RepeatCount;
            var repeatDuration = RepeatDuration;
            var simpleDuration = SimpleDuration;
            if (simpleDuration.IsZero || (repeatDuration.IsUnresolved && repeatCount is null))
                return simpleDuration;

            repeatDuration = SmilTime.Min(repeatDuration, SmilTime.Indefinite);
            if (repeatCount is { } count)
                return SmilTime.Min(repeatDuration, simpleDuration.Repeat(count));

            return repeatDuration;
        }
    }

    /// <summary>https://www.w3.org/TR/SMIL3/smil-timing.html#q84: computes the end of the active duration of an interval that begins at a time.</summary>
    private SmilTime ResolveActiveEnd(SmilTime begin)
    {
        var end = SmilTime.Indefinite;
        if (_endTimes.Count > 0)
        {
            var next = SmilTime.Unresolved;
            foreach (var (time, _) in _endTimes)
            {
                if (time > begin)
                {
                    next = time;
                    break;
                }
            }

            if (!next.IsUnresolved)
            {
                end = next;
            }
            else if (!_hasEndConditions)
            {
                // "If the instance list is empty (i.e. if there are no end conditions that may generate more times), there
                // is no valid interval": all the end times are before the begin
                return SmilTime.Unresolved;
            }
        }

        SmilTime activeDuration;
        if (end.IsFinite && GetAttributeValue("dur") is null && GetAttributeValue("repeatDur") is null && GetAttributeValue("repeatCount") is null)
        {
            activeDuration = end - begin;
        }
        else if (!end.IsFinite)
        {
            activeDuration = RepeatingDuration;
        }
        else
        {
            activeDuration = SmilTime.Min(RepeatingDuration, end - begin);
        }

        // https://www.w3.org/TR/2001/REC-smil-animation-20010904/#MinMax: both are ignored when min is greater than max
        var min = SmilTime.ParseClockValue(GetAttributeValue("min"));
        if (min.IsUnresolved || min < SmilTime.Zero || !min.IsFinite)
        {
            min = SmilTime.Zero;
        }

        var max = SmilTime.ParseClockValue(GetAttributeValue("max"));
        if (max.IsUnresolved || max <= SmilTime.Zero)
        {
            max = SmilTime.Indefinite;
        }

        if (min > max)
        {
            min = SmilTime.Zero;
            max = SmilTime.Indefinite;
        }

        return begin + SmilTime.Min(max, SmilTime.Max(min, activeDuration));
    }

    /// <summary>
    /// https://www.w3.org/TR/SMIL3/smil-timing.html#q90: returns the first interval that begins at or after a time and ends after
    /// another time.
    /// </summary>
    private SmilInterval ResolveInterval(SmilTime beginAfter, SmilTime endAfter)
    {
        var index = 0;
        for (var guard = 0; guard < 10_000; guard++)
        {
            while (index < _beginTimes.Count && _beginTimes[index].Time < beginAfter)
            {
                index++;
            }

            if (index >= _beginTimes.Count)
                break;

            var begin = _beginTimes[index].Time;
            var end = ResolveActiveEnd(begin);
            if (end.IsUnresolved)
                break;

            // The interval must not end in the past
            if (end > endAfter)
                return new SmilInterval(begin, end);

            // Ensure forward progress
            beginAfter = end == beginAfter ? beginAfter + SmilTime.Epsilon : end;
        }

        return SmilInterval.Unresolved;
    }

    private SmilTime GetNextBeginAfter(SmilTime time)
    {
        foreach (var (begin, _) in _beginTimes)
        {
            if (begin > time)
                return begin;
        }

        return SmilTime.Unresolved;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The intervals and the active state
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Updates the current interval at a time of the timeline: its end is computed again when the instance lists changed, it is
    /// ended by a restart, and the next interval is resolved when it ended. Returns whether the interval changed (the elements
    /// that depend on it got new instance times).
    /// </summary>
    internal bool UpdateInterval(SmilTime time)
    {
        EnsureTimingAttributesParsed();
        var changed = false;
        if (_instanceListsChanged)
        {
            _instanceListsChanged = false;
            if (_interval.IsResolved)
            {
                if (_interval.Begin > time)
                {
                    // The interval did not begin yet: it is discarded and resolved again
                    _interval = SmilInterval.Unresolved;
                    NotifyDependentsOfDiscardedInterval();
                    changed = true;
                }
                else if (_interval.End > time)
                {
                    var end = ResolveActiveEnd(_interval.Begin);
                    if (end.IsUnresolved)
                    {
                        // There is no active duration anymore
                        _interval = SmilInterval.Unresolved;
                        NotifyDependentsOfDiscardedInterval();
                        changed = true;
                    }
                    else if (end != _interval.End)
                    {
                        _interval = _interval with { End = end };
                        NotifyDependents(newInterval: false);
                        changed = true;
                    }
                }
            }
        }

        var restart = GetRestart();
        for (var guard = 0; guard < 10_000; guard++)
        {
            if (_interval.IsResolved && _interval.End > time)
            {
                // https://www.w3.org/TR/SMIL3/smil-timing.html#adef-restart: a begin time during the active duration ends the interval
                if (restart == RestartMode.Always)
                {
                    var nextBegin = GetNextBeginAfter(_interval.Begin);
                    if (nextBegin < _interval.End)
                    {
                        _interval = _interval with { End = nextBegin };
                        NotifyDependents(newInterval: false);
                        changed = true;
                    }
                }

                if (_interval.End > time)
                    break;
            }

            SmilInterval next;
            if (_interval.IsResolved)
            {
                // The interval ended: the next one begins after it
                next = restart == RestartMode.Never ? SmilInterval.Unresolved : ResolveInterval(_interval.End, _interval.End);
                _previousInterval = _interval;
                _waitingForFirstInterval = false;
            }
            else if (_waitingForFirstInterval)
            {
                // https://www.w3.org/TR/SMIL3/smil-timing.html#q90: the first interval ends after the begin of the timeline
                next = ResolveInterval(SmilTime.Earliest, SmilTime.Zero);
            }
            else
            {
                if (restart == RestartMode.Never)
                    break;

                next = ResolveInterval(_previousInterval.IsResolved ? _previousInterval.End : SmilTime.Earliest, time);
            }

            if (next == _interval)
                break;

            _interval = next;
            changed = true;
            if (!next.IsResolved)
                break;

            NotifyDependents(newInterval: true);
        }

        return changed;
    }

    /// <summary>Updates the active state at a time of the timeline, and queues the timing events.</summary>
    internal void UpdateActiveState(SmilTime time, bool events)
    {
        var wasActive = _activeState == SmilActiveState.Active;
        SmilActiveState state;
        if (_interval.Contains(time))
        {
            state = SmilActiveState.Active;
        }
        else if (!_waitingForFirstInterval && IsFrozenFill && _previousInterval.IsResolved)
        {
            state = SmilActiveState.Frozen;
        }
        else
        {
            state = SmilActiveState.Inactive;
        }

        var isActive = state == SmilActiveState.Active;
        var restarted = wasActive && isActive && _activeBegin != _interval.Begin;
        if (wasActive && (!isActive || restarted) && events)
        {
            QueueTimeEvent("endEvent", 0);
        }

        if (isActive && (!wasActive || restarted))
        {
            _activeBegin = _interval.Begin;
            _waitingForFirstInterval = false;
            _lastRepeat = CalculateProgress(time).Repeat;
            if (events)
            {
                QueueTimeEvent("beginEvent", 0);
            }
        }
        else if (isActive)
        {
            var repeat = CalculateProgress(time).Repeat;
            if (repeat != _lastRepeat)
            {
                _lastRepeat = repeat;
                if (events && repeat > 0)
                {
                    NotifyDependentsOfRepeat(repeat, time);
                    QueueTimeEvent("repeatEvent", (int)Math.Min(repeat, int.MaxValue));
                }
            }
        }

        _activeState = state;
    }

    /// <summary>The next time after a time when the interval begins, repeats or ends, or a begin time may restart it.</summary>
    /// <param name="target">The time the timeline is updated to: the iterations of a short simple duration before it are skipped.</param>
    internal SmilTime GetNextIntervalTime(SmilTime time, SmilTime target, bool includeRepeats)
    {
        var next = SmilTime.Unresolved;
        if (_interval.IsResolved)
        {
            if (_interval.Begin > time)
            {
                next = _interval.Begin;
            }
            else if (_interval.End > time)
            {
                next = _interval.End;
                if (includeRepeats)
                {
                    var simpleDuration = SimpleDuration;
                    if (simpleDuration.IsFinite && !simpleDuration.IsZero)
                    {
                        // The next iteration; when many iterations end before the target time, only the last ones fire events
                        var repeat = ((time - _interval.Begin).Ticks / simpleDuration.Ticks) + 1;
                        if (target.IsFinite && target > time)
                        {
                            repeat = Math.Max(repeat, ((target - _interval.Begin).Ticks / simpleDuration.Ticks) - 64);
                        }

                        if (repeat < long.MaxValue / 4 / simpleDuration.Ticks)
                        {
                            var nextRepeat = _interval.Begin + SmilTime.FromTicks(simpleDuration.Ticks * repeat);
                            if (nextRepeat > time && nextRepeat < next && nextRepeat - _interval.Begin < RepeatingDuration)
                            {
                                next = nextRepeat;
                            }
                        }
                    }
                }
            }
        }

        var nextBegin = GetNextBeginAfter(time);
        if (_interval.IsResolved && _interval.Contains(time) && GetRestart() == RestartMode.Always)
        {
            next = SmilTime.Min(next, nextBegin);
        }

        return next.IsFinite ? next : SmilTime.Unresolved;
    }

    /// <summary>Whether the animated value changes with the time, or an interval will begin or end.</summary>
    internal bool NeedsFrames(SmilTime time)
    {
        if (!_interval.IsResolved)
            return false;

        if (_interval.Begin > time || (_interval.End.IsFinite && _interval.End > time))
            return true;

        return _interval.Contains(time) && this is not SvgSetElement && SimpleDuration.IsFinite;
    }

    /// <summary>
    /// Whether the element contributes to the animated value of its target: it is active (and not past its repeating duration,
    /// unless it freezes), or frozen.
    /// </summary>
    internal bool IsContributing
    {
        get
        {
            if (_activeState == SmilActiveState.Frozen)
                return true;

            if (_activeState != SmilActiveState.Active || TimeContainer is not { } container)
                return false;

            return IsFrozenFill || container.LatestUpdateTime < _interval.Begin + RepeatingDuration;
        }
    }

    /// <summary>
    /// https://www.w3.org/TR/SMIL3/smil-animation.html#q34: the animations are applied in the order of the begin time of their
    /// interval, then in document order.
    /// </summary>
    internal int ComparePriority(SvgAnimationElement other)
    {
        var begin = _activeState == SmilActiveState.Frozen ? _previousInterval.Begin : _interval.Begin;
        var otherBegin = other._activeState == SmilActiveState.Frozen ? other._previousInterval.Begin : other._interval.Begin;
        var result = begin.CompareTo(otherBegin);
        return result != 0 ? result : DocumentOrder.CompareTo(other.DocumentOrder);
    }

    /// <summary>The progress in the simple duration (0 to 1) and the iteration of the element at a time of its active or frozen interval.</summary>
    internal (double Progress, long Repeat) CalculateProgress(SmilTime time)
    {
        var simpleDuration = SimpleDuration;
        if (simpleDuration.IsIndefinite)
            return (0, 0);

        if (simpleDuration.IsZero)
            return (1, 0);

        var interval = _interval.Contains(time) || !_previousInterval.IsResolved ? _interval : _previousInterval;
        if (!interval.IsResolved)
            return (0, 0);

        var activeTime = time - interval.Begin;
        var repeatingDuration = RepeatingDuration;
        long repeat;
        SmilTime simpleTime;
        if (time >= interval.End || activeTime > repeatingDuration)
        {
            // Past the end of the interval (a frozen element) or of the repeating duration: the last value of the active duration
            var lastActiveDuration = time >= interval.End ? interval.End - interval.Begin : repeatingDuration;
            if (!lastActiveDuration.IsFinite)
                return (0, 0);

            // The last value of an active duration that is a multiple of the simple duration is the end of the simple duration;
            // otherwise it is a value within the simple duration
            repeat = lastActiveDuration.Ticks / simpleDuration.Ticks;
            var remainder = lastActiveDuration.Ticks % simpleDuration.Ticks;
            if (remainder != 0)
            {
                simpleTime = SmilTime.FromTicks(remainder - 1);
            }
            else
            {
                simpleTime = simpleDuration;
                repeat--;
            }
        }
        else
        {
            repeat = activeTime.Ticks / simpleDuration.Ticks;
            simpleTime = SmilTime.FromTicks(activeTime.Ticks % simpleDuration.Ticks);
        }

        return (Math.Clamp((double)simpleTime.Ticks / simpleDuration.Ticks, 0, 1), Math.Max(repeat, 0));
    }

    /// <summary>The progress and the iteration at the time of the last update of the timeline.</summary>
    internal (double Progress, long Repeat) CurrentProgress => TimeContainer is { } container ? CalculateProgress(container.LatestUpdateTime) : (0, 0);

    internal void QueueTimeEvent(string type, int detail)
    {
        var document = NodeDocument;
        document.QueueElementTask(() => FireEvent(new TimeEvent(type, document.DefaultView, detail)));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // SVGAnimationElement (https://svgwg.org/specs/animations/#InterfaceSVGAnimationElement)
    // ----------------------------------------------------------------------------------------------------------------

    private SmilTimeContainer? GetUpToDateContainer()
    {
        if (IsConnected && NodeDocument.SmilOrNull is { } smil)
        {
            smil.UpdateTimelines(force: false);
        }

        return TimeContainer;
    }

    /// <summary>https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__getStartTime: the begin time, in seconds, of the current interval.</summary>
    /// <seealso href="https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__getStartTime"/>
    [IdlMember("getStartTime", "https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__getStartTime")]
    public float GetStartTime()
    {
        GetUpToDateContainer();
        if (!_interval.IsResolved)
            throw DomException.InvalidState("The element has no current interval");

        return (float)_interval.Begin.Seconds;
    }

    /// <summary>https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__getCurrentTime: the current time, in seconds, of the timeline of the document fragment.</summary>
    /// <seealso href="https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__getCurrentTime"/>
    [IdlMember("getCurrentTime", "https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__getCurrentTime")]
    public float GetCurrentTime() => GetUpToDateContainer() is { } container ? (float)container.Elapsed.Seconds : 0;

    /// <summary>https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__getSimpleDuration: the simple duration, in seconds.</summary>
    /// <seealso href="https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__getSimpleDuration"/>
    [IdlMember("getSimpleDuration", "https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__getSimpleDuration")]
    public float GetSimpleDuration()
    {
        var duration = SimpleDuration;
        if (!duration.IsFinite)
            throw DomException.NotSupported("The simple duration of the element is not defined");

        return (float)duration.Seconds;
    }

    /// <summary>https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__beginElement: begins the element at the current time.</summary>
    /// <seealso href="https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__beginElement"/>
    [IdlMember("beginElement", "https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__beginElement")]
    public void BeginElement() => BeginElementAt(0);

    /// <summary>https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__beginElementAt: begins the element at an offset, in seconds, from the current time.</summary>
    /// <seealso href="https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__beginElementAt"/>
    [IdlMember("beginElementAt", "https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__beginElementAt")]
    public void BeginElementAt(float offset) => AddInstanceTimeAndUpdate(isBegin: true, SmilTime.FromSeconds(offset), InstanceOrigin.Script);

    /// <summary>https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__endElement: ends the element at the current time.</summary>
    /// <seealso href="https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__endElement"/>
    [IdlMember("endElement", "https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__endElement")]
    public void EndElement() => EndElementAt(0);

    /// <summary>https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__endElementAt: ends the element at an offset, in seconds, from the current time.</summary>
    /// <seealso href="https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__endElementAt"/>
    [IdlMember("endElementAt", "https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__endElementAt")]
    public void EndElementAt(float offset) => AddInstanceTimeAndUpdate(isBegin: false, SmilTime.FromSeconds(offset), InstanceOrigin.Script);
}
