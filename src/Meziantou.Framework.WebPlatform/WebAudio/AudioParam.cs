using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.WebAudio.Rendering;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>
/// A parameter of an <see cref="AudioNode"/> (https://webaudio.github.io/web-audio-api/#AudioParam): its value can be set
/// directly, automated with events scheduled on the timeline of the context, and modulated by the outputs of nodes connected
/// to it (summed with the intrinsic value).
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioParam"/>
[IdlInterface("AudioParam", "https://webaudio.github.io/web-audio-api/#AudioParam")]
public sealed class AudioParam
{
    private readonly AudioParamTimeline _timeline;
    private readonly bool _automationRateIsFixed;
    private readonly List<(AudioNode Node, int Output)> _inputs = [];
    private AudioBus? _inputBus;
    private float[]? _values;
    private AutomationRate _automationRate;
    private long _computedFrame = -1;

    // [[current value]] (https://webaudio.github.io/web-audio-api/#dom-audioparam-current-value-slot)
    private float _currentValue;

    internal AudioParam(BaseAudioContext context, AudioNode? owner, string name, float defaultValue, float minValue, float maxValue, AutomationRate automationRate, bool automationRateIsFixed = false)
    {
        Context = context;
        Owner = owner;
        Name = name;
        DefaultValue = defaultValue;
        MinValue = minValue;
        MaxValue = maxValue;
        _automationRate = automationRate;
        _automationRateIsFixed = automationRateIsFixed;
        _currentValue = defaultValue;
        _timeline = new AudioParamTimeline(context.SampleRate, defaultValue);
    }

    internal BaseAudioContext Context { get; }

    /// <summary>The node of the parameter (null for the parameters of an <see cref="AudioListener"/>).</summary>
    internal AudioNode? Owner { get; }

    /// <summary>The name of the parameter, for the error messages.</summary>
    internal string Name { get; }

    /// <summary>
    /// Gets or sets the value of the parameter: the intrinsic value at the start of the current render quantum, clamped to
    /// the nominal range. Setting it schedules a change of the value at the current time of the context.
    /// </summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparam-value"/>
    [IdlMember("value", "https://webaudio.github.io/web-audio-api/#dom-audioparam-value")]
    public float Value
    {
        get => Clamp(_currentValue);
        set
        {
            _currentValue = value;
            SetValueAtTime(value, Context.CurrentTime);
        }
    }

    /// <summary>Gets or sets the automation rate (a-rate: computed for each sample frame; k-rate: once per render quantum).</summary>
    /// <exception cref="DomException">The rate of the parameter cannot be changed (an <c>InvalidStateError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparam-automationrate"/>
    [IdlMember("automationRate", "https://webaudio.github.io/web-audio-api/#dom-audioparam-automationrate")]
    public AutomationRate AutomationRate
    {
        get => _automationRate;
        set
        {
            if (_automationRateIsFixed && value != _automationRate)
                throw new DomException($"The automation rate of {Name} cannot be changed", "InvalidStateError");

            _automationRate = value;
        }
    }

    /// <summary>Gets the initial value of the parameter.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparam-defaultvalue"/>
    [IdlMember("defaultValue", "https://webaudio.github.io/web-audio-api/#dom-audioparam-defaultvalue")]
    public float DefaultValue { get; }

    /// <summary>Gets the minimum of the nominal range.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparam-minvalue"/>
    [IdlMember("minValue", "https://webaudio.github.io/web-audio-api/#dom-audioparam-minvalue")]
    public float MinValue { get; }

    /// <summary>Gets the maximum of the nominal range.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparam-maxvalue"/>
    [IdlMember("maxValue", "https://webaudio.github.io/web-audio-api/#dom-audioparam-maxvalue")]
    public float MaxValue { get; }

    /// <summary>Schedules a change of the value at a time.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparam-setvalueattime"/>
    [IdlMember("setValueAtTime", "https://webaudio.github.io/web-audio-api/#dom-audioparam-setvalueattime")]
    public AudioParam SetValueAtTime(float value, double startTime)
    {
        ValidateTime(startTime, nameof(startTime));
        Insert(new AutomationEvent { Type = AutomationEventType.SetValue, Time = ClampTime(startTime), Value = value });
        return this;
    }

    /// <summary>Schedules a linear ramp from the previous event to a value at a time.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparam-linearramptovalueattime"/>
    [IdlMember("linearRampToValueAtTime", "https://webaudio.github.io/web-audio-api/#dom-audioparam-linearramptovalueattime")]
    public AudioParam LinearRampToValueAtTime(float value, double endTime)
    {
        ValidateTime(endTime, nameof(endTime));
        Insert(new AutomationEvent { Type = AutomationEventType.LinearRamp, Time = ClampTime(endTime), Value = value });
        return this;
    }

    /// <summary>Schedules an exponential ramp from the previous event to a value at a time.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparam-exponentialramptovalueattime"/>
    [IdlMember("exponentialRampToValueAtTime", "https://webaudio.github.io/web-audio-api/#dom-audioparam-exponentialramptovalueattime")]
    public AudioParam ExponentialRampToValueAtTime(float value, double endTime)
    {
        if (value == 0)
            throw new RangeErrorException("The value of an exponential ramp cannot be 0");

        ValidateTime(endTime, nameof(endTime));
        Insert(new AutomationEvent { Type = AutomationEventType.ExponentialRamp, Time = ClampTime(endTime), Value = value });
        return this;
    }

    /// <summary>Schedules an exponential approach to a target value from a time.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparam-settargetattime"/>
    [IdlMember("setTargetAtTime", "https://webaudio.github.io/web-audio-api/#dom-audioparam-settargetattime")]
    public AudioParam SetTargetAtTime(float target, double startTime, float timeConstant)
    {
        ValidateTime(startTime, nameof(startTime));
        if (timeConstant < 0)
            throw new RangeErrorException($"The time constant {timeConstant} cannot be negative");

        Insert(new AutomationEvent { Type = AutomationEventType.SetTarget, Time = ClampTime(startTime), Value = target, TimeConstant = timeConstant });
        return this;
    }

    /// <summary>Schedules a curve of values, linearly interpolated, from a time for a duration.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparam-setvaluecurveattime"/>
    [IdlMember("setValueCurveAtTime", "https://webaudio.github.io/web-audio-api/#dom-audioparam-setvaluecurveattime")]
    public AudioParam SetValueCurveAtTime(IReadOnlyList<float> values, double startTime, double duration)
    {
        ArgumentNullException.ThrowIfNull(values);
        ValidateTime(startTime, nameof(startTime));
        if (!double.IsFinite(duration) || duration <= 0)
            throw new RangeErrorException($"The duration {duration} must be strictly positive");

        if (values.Count < 2)
            throw new DomException("The curve must have at least 2 values", "InvalidStateError");

        Insert(new AutomationEvent { Type = AutomationEventType.SetValueCurve, Time = ClampTime(startTime), Curve = [.. values], Duration = duration });
        return this;
    }

    /// <summary>Cancels the events at or after a time.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparam-cancelscheduledvalues"/>
    [IdlMember("cancelScheduledValues", "https://webaudio.github.io/web-audio-api/#dom-audioparam-cancelscheduledvalues")]
    public AudioParam CancelScheduledValues(double cancelTime)
    {
        ValidateTime(cancelTime, nameof(cancelTime));
        _timeline.CancelScheduledValues(ClampTime(cancelTime), LastIntrinsicValue);
        return this;
    }

    /// <summary>Cancels the events after a time, and holds the value the automation has at that time.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparam-cancelandholdattime"/>
    [IdlMember("cancelAndHoldAtTime", "https://webaudio.github.io/web-audio-api/#dom-audioparam-cancelandholdattime")]
    public AudioParam CancelAndHoldAtTime(double cancelTime)
    {
        ValidateTime(cancelTime, nameof(cancelTime));
        _timeline.CancelAndHoldAtTime(ClampTime(cancelTime), LastIntrinsicValue);
        return this;
    }

    /// <summary>Sets the value given to the constructor of the node (the value before any automation event).</summary>
    internal void SetInitialValue(float value)
    {
        _currentValue = value;
        _timeline.Baseline = value;
    }

    private static void ValidateTime(double time, string name)
    {
        if (!double.IsFinite(time) || time < 0)
            throw new RangeErrorException($"The {name} {time} must be a non-negative finite number");
    }

    private double ClampTime(double time) => Math.Max(time, Context.CurrentTime);

    private void Insert(AutomationEvent automationEvent) => _timeline.Insert(automationEvent, Context.CurrentTime, _currentValue);

    private float Clamp(float value) => value < MinValue ? MinValue : value > MaxValue ? MaxValue : value;

    // ----------------------------------------------------------------------------------------------------------------
    // Rendering
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The intrinsic value of the last computed frame.</summary>
    internal double LastIntrinsicValue { get; private set; }

    /// <summary>The connections of the outputs of nodes to the parameter.</summary>
    internal List<(AudioNode Node, int Output)> Inputs => _inputs;

    /// <summary>Whether the computed values of the current render quantum are all equal to <see cref="FinalValue"/>.</summary>
    internal bool IsConstant { get; private set; } = true;

    /// <summary>The computed value of the first frame of the current render quantum.</summary>
    internal float FinalValue { get; private set; }

    /// <summary>The computed values of the frames of the current render quantum (valid when <see cref="IsConstant"/> is false).</summary>
    internal ReadOnlySpan<float> Values => _values.AsSpan(0, Context.RenderQuantumSizeInternal);

    /// <summary>The intrinsic value of a time (for the nodes that read their parameters outside of the rendering, e.g. the frequency response of a filter).</summary>
    internal float GetIntrinsicValueAtTime(double time) => _timeline.HasEvents ? (float)_timeline.GetValueAtTime(time) : _currentValue;

    /// <summary>
    /// Computes the values of the render quantum (https://webaudio.github.io/web-audio-api/#computation-of-value): the intrinsic
    /// value of the automation, plus the inputs (down-mixed to mono), clamped to the nominal range.
    /// </summary>
    internal void Compute(long startFrame)
    {
        // A parameter shared by several nodes (e.g. the parameters of the listener) is computed once per render quantum
        if (_computedFrame == startFrame)
            return;

        _computedFrame = startFrame;
        var length = Context.RenderQuantumSizeInternal;
        if (_values is null || _values.Length < length)
        {
            _values = new float[length];
        }

        var values = _values.AsSpan(0, length);
        var hasInputs = MixInputs(length);
        var isKRate = _automationRate == AutomationRate.KRate || Owner?.ForcesKRate(this) == true;
        bool constant;
        if (isKRate)
        {
            constant = true;
            _timeline.ComputeValues(startFrame, values[..1]);
        }
        else
        {
            constant = _timeline.ComputeValues(startFrame, values);
        }

        // [[current value]] is the intrinsic value at the start of the render quantum
        if (_timeline.HasEvents)
        {
            _currentValue = values[0];
            LastIntrinsicValue = isKRate ? values[0] : values[^1];
        }
        else
        {
            LastIntrinsicValue = _currentValue;
        }

        if (hasInputs)
        {
            var input = _inputBus!.GetChannelSpan(0);
            if (isKRate)
            {
                values[0] += input[0];
            }
            else
            {
                for (var i = 0; i < values.Length; i++)
                {
                    values[i] += input[i];
                }

                constant = false;
            }
        }

        // NaN is replaced by the default value, and the value is clamped to the nominal range
        if (constant)
        {
            var value = values[0];
            FinalValue = float.IsNaN(value) ? DefaultValue : Clamp(value);
            IsConstant = true;
            return;
        }

        var min = MinValue;
        var max = MaxValue;
        var defaultValue = DefaultValue;
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            values[i] = float.IsNaN(value) ? defaultValue : value < min ? min : value > max ? max : value;
        }

        FinalValue = values[0];
        IsConstant = false;
    }

    private bool MixInputs(int length)
    {
        if (_inputs.Count == 0)
            return false;

        _inputBus ??= new AudioBus(1, length);
        _inputBus.Zero(1);
        foreach (var (node, output) in _inputs)
        {
            _inputBus.SumFrom(node.GetOutputBus(output), ChannelInterpretation.Speakers);
        }

        return !_inputBus.IsSilent;
    }
}
