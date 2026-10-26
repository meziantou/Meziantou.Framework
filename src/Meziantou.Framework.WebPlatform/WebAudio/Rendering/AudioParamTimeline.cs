using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio.Rendering;

/// <summary>The types of the automation events of an <see cref="AudioParam"/> (https://webaudio.github.io/web-audio-api/#AudioParam).</summary>
internal enum AutomationEventType
{
    SetValue,
    LinearRamp,
    ExponentialRamp,
    SetTarget,
    SetValueCurve,
}

/// <summary>An automation event of an <see cref="AudioParam"/>.</summary>
internal sealed class AutomationEvent
{
    public AutomationEventType Type { get; init; }

    /// <summary>The time of the event: the end time of a ramp, the start time of the other events.</summary>
    public double Time { get; set; }

    /// <summary>The value of a SetValue event, the end value of a ramp, the target of a SetTarget event.</summary>
    public double Value { get; set; }

    public double TimeConstant { get; init; }

    public float[]? Curve { get; init; }

    /// <summary>The duration of a SetValueCurve event (the sampling of the curve always uses it).</summary>
    public double Duration { get; init; }

    /// <summary>The time after which a SetValueCurve event holds its value (the end of the curve, or the time of a cancelAndHoldAtTime()).</summary>
    public double CurveHoldTime { get; set; }

    /// <summary>The cached value of the parameter just before the event (the limit of the previous interval).</summary>
    public double StartValue { get; set; }

    public bool HasStartValue { get; set; }

    public bool IsRamp => Type is AutomationEventType.LinearRamp or AutomationEventType.ExponentialRamp;

    public AutomationEvent Clone() => new()
    {
        Type = Type,
        Time = Time,
        Value = Value,
        TimeConstant = TimeConstant,
        Curve = Curve,
        Duration = Duration,
        CurveHoldTime = CurveHoldTime,
    };
}

/// <summary>
/// The list of automation events of an <see cref="AudioParam"/> and the computation of its intrinsic value
/// (https://webaudio.github.io/web-audio-api/#computation-of-value). The events take effect at the first sample frame
/// whose time is greater than or equal to their time (the frame <c>ceil(time * sampleRate)</c>), and the formulas are
/// applied to the exact time of the frames, in double precision.
/// </summary>
internal sealed class AudioParamTimeline
{
    private readonly List<AutomationEvent> _events = [];
    private readonly double _sampleRate;

    // The index of the event whose interval contains the last computed frame (-1: before the first event), to compute the
    // frames in increasing order without searching the list
    private int _cursor = -1;

    public AudioParamTimeline(double sampleRate, double initialValue)
    {
        _sampleRate = sampleRate;
        Baseline = initialValue;
    }

    /// <summary>The value of the parameter before its first event.</summary>
    public double Baseline { get; set; }

    public bool HasEvents => _events.Count > 0;

    internal IReadOnlyList<AutomationEvent> Events => _events;

    /// <summary>Validates and inserts an event (https://webaudio.github.io/web-audio-api/#dom-audioparam-setvalueattime and the other automation methods).</summary>
    public void Insert(AutomationEvent automationEvent, double currentTime, double currentValue)
    {
        // A curve cannot overlap another event, and no event can be inside the interval of a curve
        foreach (var existing in _events)
        {
            if (existing.Type == AutomationEventType.SetValueCurve && automationEvent.Time >= existing.Time && automationEvent.Time < existing.Time + existing.Duration)
                throw new DomException($"The automation event at time {automationEvent.Time} overlaps the value curve at time {existing.Time} (duration {existing.Duration})", "NotSupportedError");

            if (automationEvent.Type == AutomationEventType.SetValueCurve && existing.Time > automationEvent.Time && existing.Time < automationEvent.Time + automationEvent.Duration)
                throw new DomException($"The value curve at time {automationEvent.Time} (duration {automationEvent.Duration}) overlaps the automation event at time {existing.Time}", "NotSupportedError");
        }

        // After the events of the same time
        var index = _events.Count;
        while (index > 0 && _events[index - 1].Time > automationEvent.Time)
        {
            index--;
        }

        if (automationEvent.IsRamp)
        {
            if (index == 0)
            {
                // https://webaudio.github.io/web-audio-api/#dom-audioparam-linearramptovalueattime: without a preceding event,
                // the ramp behaves as if setValueAtTime(value, currentTime) were called
                _events.Insert(0, new AutomationEvent { Type = AutomationEventType.SetValue, Time = currentTime, Value = currentValue });
                index = 1;
            }
            else if (_events[index - 1] is { Type: AutomationEventType.SetTarget } setTarget)
            {
                // A ramp that follows a SetTarget event starts from the value of the SetTarget automation: at its start time
                // when it has not started yet (the ramp replaces it), else at the current time
                if (setTarget.Time >= currentTime)
                {
                    var startValue = (float)GetStartValue(index - 1);
                    _events[index - 1] = new AutomationEvent { Type = AutomationEventType.SetValue, Time = setTarget.Time, Value = startValue };
                }
                else
                {
                    var value = (float)EvaluateInterval(index - 1, currentTime);
                    _events.Insert(index, new AutomationEvent { Type = AutomationEventType.SetValue, Time = currentTime, Value = value });
                    index++;
                }
            }
        }

        if (automationEvent.Type == AutomationEventType.SetValueCurve)
        {
            automationEvent.CurveHoldTime = automationEvent.Time + automationEvent.Duration;
        }

        _events.Insert(index, automationEvent);
        Invalidate(index - 1);
    }

    /// <summary>https://webaudio.github.io/web-audio-api/#dom-audioparam-cancelscheduledvalues</summary>
    public void CancelScheduledValues(double cancelTime, double lastValue)
    {
        var removed = _events.RemoveAll(e => e.Time >= cancelTime || (e.Type == AutomationEventType.SetValueCurve && cancelTime >= e.Time && cancelTime <= e.Time + e.Duration));
        if (removed > 0)
        {
            if (_events.Count == 0)
            {
                Baseline = lastValue;
            }

            Invalidate(0);
        }
    }

    /// <summary>https://webaudio.github.io/web-audio-api/#dom-audioparam-cancelandholdattime</summary>
    public void CancelAndHoldAtTime(double cancelTime, double lastValue)
    {
        // E1: the last event at or before tc; E2: the first event after tc
        var e1Index = -1;
        for (var i = 0; i < _events.Count; i++)
        {
            if (_events[i].Time <= cancelTime)
            {
                e1Index = i;
            }
        }

        var e2Index = e1Index + 1 < _events.Count ? e1Index + 1 : -1;
        if (e2Index >= 0 && _events[e2Index].IsRamp)
        {
            // The ramp is rewritten to end at tc with the value it has at tc (the same curve)
            // The values of the events are single-precision floats
            var value = (float)EvaluateInterval(e1Index, cancelTime);
            var ramp = _events[e2Index].Clone();
            ramp.Time = cancelTime;
            ramp.Value = value;
            _events.RemoveRange(e2Index, _events.Count - e2Index);
            _events.Add(ramp);
        }
        else if (e1Index >= 0 && _events[e1Index].Type == AutomationEventType.SetTarget)
        {
            var value = (float)EvaluateInterval(e1Index, cancelTime);
            _events.RemoveRange(e1Index + 1, _events.Count - e1Index - 1);
            _events.Add(new AutomationEvent { Type = AutomationEventType.SetValue, Time = cancelTime, Value = value });
        }
        else if (e1Index >= 0 && _events[e1Index] is { Type: AutomationEventType.SetValueCurve } startingCurve && cancelTime <= startingCurve.Time)
        {
            // A curve that starts at the cancel time has no effect: the value before it is held
            _events.RemoveRange(e1Index, _events.Count - e1Index);
        }
        else if (e1Index >= 0 && _events[e1Index] is { Type: AutomationEventType.SetValueCurve } curve && cancelTime <= curve.Time + curve.Duration)
        {
            _events.RemoveRange(e1Index + 1, _events.Count - e1Index - 1);
            var truncated = curve.Clone();
            truncated.CurveHoldTime = Math.Min(curve.CurveHoldTime, cancelTime);
            _events[e1Index] = truncated;
        }
        else
        {
            _events.RemoveAll(e => e.Time > cancelTime);
        }

        if (_events.Count == 0)
        {
            Baseline = lastValue;
        }

        Invalidate(0);
    }

    private void Invalidate(int fromIndex)
    {
        for (var i = Math.Max(fromIndex, 0); i < _events.Count; i++)
        {
            _events[i].HasStartValue = false;
        }

        _cursor = -1;
    }

    /// <summary>Gets the intrinsic value at a time (the value of the interval that contains the time).</summary>
    public double GetValueAtTime(double time)
    {
        var index = -1;
        for (var i = 0; i < _events.Count; i++)
        {
            if (_events[i].Time <= time)
            {
                index = i;
            }
            else
            {
                break;
            }
        }

        return EvaluateInterval(index, time);
    }

    /// <summary>
    /// Computes the intrinsic values of the frames <c>[startFrame, startFrame + values.Length)</c>. Returns true when the
    /// value is the same for all of them.
    /// </summary>
    public bool ComputeValues(long startFrame, Span<float> values)
    {
        if (_events.Count == 0)
        {
            values.Fill((float)Baseline);
            return true;
        }

        if (_cursor < -1 || _cursor >= _events.Count)
        {
            _cursor = -1;
        }

        // The cursor only moves forward: a frame before the frame of the cursor event restarts the search
        if (_cursor >= 0 && GetFrame(_events[_cursor].Time) > startFrame)
        {
            _cursor = -1;
        }

        var constant = true;
        var k = 0;
        while (k < values.Length)
        {
            var frame = startFrame + k;
            while (_cursor + 1 < _events.Count && GetFrame(_events[_cursor + 1].Time) <= frame)
            {
                _cursor++;
            }

            // The frames of the interval of the cursor event in this block
            var end = values.Length;
            if (_cursor + 1 < _events.Count)
            {
                var nextFrame = GetFrame(_events[_cursor + 1].Time);
                if (nextFrame - startFrame < end)
                {
                    end = (int)(nextFrame - startFrame);
                }
            }

            if (IsIntervalConstant(_cursor, frame))
            {
                var value = (float)EvaluateInterval(_cursor, frame / _sampleRate);
                values[k..end].Fill(value);
                if (k > 0 && values[k - 1] != value)
                {
                    constant = false;
                }
            }
            else
            {
                constant = false;
                for (var i = k; i < end; i++)
                {
                    values[i] = (float)EvaluateInterval(_cursor, (startFrame + i) / _sampleRate);
                }
            }

            k = end;
        }

        return constant;
    }

    /// <summary>The first frame at or after a time.</summary>
    private double GetFrame(double time) => Math.Ceiling(time * _sampleRate);

    private bool IsIntervalConstant(int index, long frame)
    {
        if (index < 0)
            return _events.Count == 0 || !_events[0].IsRamp;

        var next = index + 1 < _events.Count ? _events[index + 1] : null;
        if (next is { IsRamp: true })
            return false;

        var current = _events[index];
        return current.Type switch
        {
            AutomationEventType.SetTarget => current.TimeConstant == 0,
            AutomationEventType.SetValueCurve => frame / _sampleRate >= current.CurveHoldTime,
            _ => true,
        };
    }

    /// <summary>
    /// The value at a time in the interval of an event (from the event to the next one), or before the first event when the
    /// index is -1.
    /// </summary>
    private double EvaluateInterval(int index, double t)
    {
        if (index < 0)
            return Baseline;

        var current = _events[index];
        var next = index + 1 < _events.Count ? _events[index + 1] : null;
        if (next is { IsRamp: true })
        {
            // The ramp starts at the end of the current event
            double t0;
            double v0;
            switch (current.Type)
            {
                case AutomationEventType.SetValueCurve:
                    if (t < current.CurveHoldTime)
                        return EvaluateCurve(current, t);

                    t0 = current.CurveHoldTime;
                    v0 = EvaluateCurve(current, current.CurveHoldTime);
                    break;

                case AutomationEventType.SetTarget:
                    t0 = current.Time;
                    v0 = GetStartValue(index);
                    break;

                default:
                    t0 = current.Time;
                    v0 = current.Value;
                    break;
            }

            var t1 = next.Time;
            var v1 = next.Value;
            if (t >= t1)
                return v1;

            if (t1 <= t0)
                return v1;

            if (next.Type == AutomationEventType.LinearRamp)
            {
                // In single precision, as the browsers: the value of a ramp from v0 is v0 plus the change, so that an
                // automation and the same automation from 0 added to v0 by an input give the same values
                var start = (float)v0;
                var change = (float)((float)((float)v1 - start) * ((t - t0) / (t1 - t0)));
                return start + change;
            }

            // https://webaudio.github.io/web-audio-api/#dom-audioparam-exponentialramptovalueattime: when the values have
            // opposite signs or the start value is zero, the value stays at the start value
            if (v0 == 0 || (v0 < 0) != (v1 < 0))
                return v0;

            return v0 * Math.Pow(v1 / v0, (t - t0) / (t1 - t0));
        }

        switch (current.Type)
        {
            case AutomationEventType.SetTarget:
            {
                if (current.TimeConstant == 0)
                    return current.Value;

                var v0 = GetStartValue(index);
                return current.Value + ((v0 - current.Value) * Math.Exp(-(t - current.Time) / current.TimeConstant));
            }

            case AutomationEventType.SetValueCurve:
                return EvaluateCurve(current, Math.Min(t, current.CurveHoldTime));

            default:
                return current.Value;
        }
    }

    /// <summary>The value of a curve at a time (https://webaudio.github.io/web-audio-api/#dom-audioparam-setvaluecurveattime).</summary>
    private static double EvaluateCurve(AutomationEvent curveEvent, double t)
    {
        var curve = curveEvent.Curve!;
        var n = curve.Length;
        if (t >= curveEvent.Time + curveEvent.Duration)
            return curve[n - 1];

        var position = (n - 1) / curveEvent.Duration * (t - curveEvent.Time);
        if (position <= 0)
            return curve[0];

        var k = (int)Math.Floor(position);
        if (k + 1 >= n)
            return curve[n - 1];

        var v0 = (double)curve[k];
        var v1 = (double)curve[k + 1];
        return v0 + ((v1 - v0) * (position - k));
    }

    /// <summary>The value just before an event (the limit of the interval of the previous event at its time).</summary>
    private double GetStartValue(int index)
    {
        // The nearest event whose start value is known
        var first = index;
        while (first > 0 && !_events[first].HasStartValue)
        {
            first--;
        }

        if (!_events[first].HasStartValue)
        {
            _events[first].StartValue = first == 0 ? Baseline : EvaluateInterval(first - 1, _events[first].Time);
            _events[first].HasStartValue = true;
        }

        for (var i = first + 1; i <= index; i++)
        {
            _events[i].StartValue = EvaluateInterval(i - 1, _events[i].Time);
            _events[i].HasStartValue = true;
        }

        return _events[index].StartValue;
    }
}
