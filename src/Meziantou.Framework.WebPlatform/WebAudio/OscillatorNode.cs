using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The waveforms of an <see cref="OscillatorNode"/> (https://webaudio.github.io/web-audio-api/#enumdef-oscillatortype).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#enumdef-oscillatortype"/>
[IdlInterface("OscillatorType", "https://webaudio.github.io/web-audio-api/#enumdef-oscillatortype")]
public enum OscillatorType
{
    /// <summary>A sine wave.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oscillatortype-sine"/>
    [IdlMember("sine", "https://webaudio.github.io/web-audio-api/#dom-oscillatortype-sine")]
    Sine,

    /// <summary>A square wave.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oscillatortype-square"/>
    [IdlMember("square", "https://webaudio.github.io/web-audio-api/#dom-oscillatortype-square")]
    Square,

    /// <summary>A sawtooth wave.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oscillatortype-sawtooth"/>
    [IdlMember("sawtooth", "https://webaudio.github.io/web-audio-api/#dom-oscillatortype-sawtooth")]
    Sawtooth,

    /// <summary>A triangle wave.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oscillatortype-triangle"/>
    [IdlMember("triangle", "https://webaudio.github.io/web-audio-api/#dom-oscillatortype-triangle")]
    Triangle,

    /// <summary>A <see cref="PeriodicWave"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oscillatortype-custom"/>
    [IdlMember("custom", "https://webaudio.github.io/web-audio-api/#dom-oscillatortype-custom")]
    Custom,
}

/// <summary>The options of an <see cref="OscillatorNode"/> (https://webaudio.github.io/web-audio-api/#OscillatorOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#OscillatorOptions"/>
[IdlInterface("OscillatorOptions", "https://webaudio.github.io/web-audio-api/#OscillatorOptions")]
public sealed class OscillatorOptions : AudioNodeOptions
{
    /// <summary>Gets the waveform.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oscillatoroptions-type"/>
    [IdlMember("type", "https://webaudio.github.io/web-audio-api/#dom-oscillatoroptions-type")]
    public OscillatorType Type { get; init; }

    /// <summary>Gets the initial frequency in Hz.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oscillatoroptions-frequency"/>
    [IdlMember("frequency", "https://webaudio.github.io/web-audio-api/#dom-oscillatoroptions-frequency")]
    public float Frequency { get; init; } = 440;

    /// <summary>Gets the initial detune in cents.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oscillatoroptions-detune"/>
    [IdlMember("detune", "https://webaudio.github.io/web-audio-api/#dom-oscillatoroptions-detune")]
    public float Detune { get; init; }

    /// <summary>Gets the custom waveform (the type is then <see cref="OscillatorType.Custom"/>).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oscillatoroptions-periodicwave"/>
    [IdlMember("periodicWave", "https://webaudio.github.io/web-audio-api/#dom-oscillatoroptions-periodicwave")]
    public PeriodicWave? PeriodicWave { get; init; }
}

/// <summary>
/// A source of a periodic waveform (https://webaudio.github.io/web-audio-api/#OscillatorNode): a sine, square, sawtooth or
/// triangle wave, or a <see cref="PeriodicWave"/>, band-limited to the Nyquist frequency.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#OscillatorNode"/>
[IdlInterface("OscillatorNode", "https://webaudio.github.io/web-audio-api/#OscillatorNode")]
public sealed class OscillatorNode : AudioScheduledSourceNode
{
    /// <summary>The maximum detune in cents (1200 log2(FLT_MAX)).</summary>
    internal static readonly float MaxDetune = (float)(1200 * Math.Log2(float.MaxValue));

    private OscillatorType _type;
    private PeriodicWave _wave;

    // The phase in cycles, and whether the playback started (the phase of the first frame accounts for the sub-sample start)
    private double _phase;
    private bool _playbackStarted;

    /// <summary>Creates an oscillator.</summary>
    /// <exception cref="DomException">The type is <c>custom</c> without periodic wave (an <c>InvalidStateError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oscillatornode-oscillatornode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-oscillatornode-oscillatornode")]
    public OscillatorNode(BaseAudioContext context, OscillatorOptions? options = null)
        : base(context)
    {
        var nyquist = context.SampleRate / 2;
        Frequency = new AudioParam(context, this, "OscillatorNode.frequency", 440, -nyquist, nyquist, AutomationRate.ARate);
        Detune = new AudioParam(context, this, "OscillatorNode.detune", 0, -MaxDetune, MaxDetune, AutomationRate.ARate);
        _wave = PeriodicWave.CreateBuiltIn(context, OscillatorType.Sine);
        if (options is not null)
        {
            if (options.Type == OscillatorType.Custom && options.PeriodicWave is null)
                throw new DomException("A custom oscillator requires a periodic wave", "InvalidStateError");

            ApplyOptions(options);
            Frequency.SetInitialValue(options.Frequency);
            Detune.SetInitialValue(options.Detune);
            if (options.PeriodicWave is { } wave)
            {
                SetPeriodicWave(wave);
            }
            else
            {
                Type = options.Type;
            }
        }
    }

    /// <summary>Gets or sets the waveform. It cannot be set to <see cref="OscillatorType.Custom"/>: use <see cref="SetPeriodicWave"/>.</summary>
    /// <exception cref="DomException">The value is <see cref="OscillatorType.Custom"/> (an <c>InvalidStateError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oscillatornode-type"/>
    [IdlMember("type", "https://webaudio.github.io/web-audio-api/#dom-oscillatornode-type")]
    public OscillatorType Type
    {
        get => _type;
        set
        {
            if (value == OscillatorType.Custom)
                throw new DomException("The type of an oscillator cannot be set to 'custom': use setPeriodicWave()", "InvalidStateError");

            if (value != _type || _type == OscillatorType.Custom)
            {
                _type = value;
                _wave = PeriodicWave.CreateBuiltIn(Context, value);
            }
        }
    }

    /// <summary>Gets the frequency in Hz.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oscillatornode-frequency"/>
    [IdlMember("frequency", "https://webaudio.github.io/web-audio-api/#dom-oscillatornode-frequency")]
    public AudioParam Frequency { get; }

    /// <summary>Gets the detune in cents.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oscillatornode-detune"/>
    [IdlMember("detune", "https://webaudio.github.io/web-audio-api/#dom-oscillatornode-detune")]
    public AudioParam Detune { get; }

    /// <summary>Sets a custom waveform: the type becomes <see cref="OscillatorType.Custom"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oscillatornode-setperiodicwave"/>
    [IdlMember("setPeriodicWave", "https://webaudio.github.io/web-audio-api/#dom-oscillatornode-setperiodicwave")]
    public void SetPeriodicWave(PeriodicWave periodicWave)
    {
        ArgumentNullException.ThrowIfNull(periodicWave);
        _wave = periodicWave;
        _type = OscillatorType.Custom;
    }

    private protected override IEnumerable<AudioParam> NodeParameters => [Frequency, Detune];

    private protected override void Process(long startFrame, int length)
    {
        var output = GetOutputBus(0);
        var (offset, count) = GetPlayingRange(startFrame, length);
        if (count == 0)
        {
            output.Zero(1);
            return;
        }

        output.SetNumberOfChannels(1);
        var destination = output.GetChannelSpan(0);
        destination.Clear();

        var sampleRate = (double)Context.SampleRate;
        var nyquist = sampleRate / 2;
        var wave = _wave;
        var constant = Frequency.IsConstant && Detune.IsConstant;
        var frequencies = Frequency.Values;
        var detunes = Detune.Values;
        var constantFrequency = constant ? ComputeFrequency(Frequency.FinalValue, Detune.FinalValue, nyquist) : 0;
        var constantPartials = constant ? GetPartialCount(constantFrequency, nyquist) : 0;
        for (var i = offset; i < offset + count; i++)
        {
            var frequency = constant ? constantFrequency : ComputeFrequency(Frequency.IsConstant ? Frequency.FinalValue : frequencies[i], Detune.IsConstant ? Detune.FinalValue : detunes[i], nyquist);
            if (!_playbackStarted)
            {
                // The phase is 0 at the exact start time: the first frame is after it
                var delay = Math.Max(0, ((startFrame + i) / sampleRate) - StartTime);
                _phase = frequency * delay;
                _playbackStarted = true;
            }

            destination[i] = wave.GetSample(_phase, constant ? constantPartials : GetPartialCount(frequency, nyquist));
            _phase += frequency / sampleRate;
            if (_phase is > 1e6 or < -1e6)
            {
                _phase -= Math.Floor(_phase);
            }
        }

        output.MarkNonSilent();
    }

    /// <summary>computedOscFrequency, clamped to its nominal range [-Nyquist, Nyquist].</summary>
    private static double ComputeFrequency(double frequency, double detune, double nyquist)
    {
        var computed = detune == 0 ? frequency : frequency * Math.Pow(2, detune / 1200);
        if (double.IsNaN(computed))
            return 0;

        return Math.Clamp(computed, -nyquist, nyquist);
    }

    /// <summary>The number of partials below the Nyquist frequency.</summary>
    private int GetPartialCount(double frequency, double nyquist)
    {
        var absolute = Math.Abs(frequency);
        if (absolute == 0)
            return _wave.PartialCount;

        // The partials at or above the Nyquist frequency are removed
        var partials = Math.Ceiling(nyquist / absolute) - 1;
        return partials >= PeriodicWave.TableSize ? _wave.PartialCount : (int)partials;
    }
}
