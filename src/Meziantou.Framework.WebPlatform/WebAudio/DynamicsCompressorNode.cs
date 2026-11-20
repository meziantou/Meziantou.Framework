using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The options of a <see cref="DynamicsCompressorNode"/> (https://webaudio.github.io/web-audio-api/#DynamicsCompressorOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#DynamicsCompressorOptions"/>
[IdlInterface("DynamicsCompressorOptions", "https://webaudio.github.io/web-audio-api/#DynamicsCompressorOptions")]
public sealed class DynamicsCompressorOptions : AudioNodeOptions
{
    /// <summary>Gets the initial attack time in seconds.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-dynamicscompressoroptions-attack"/>
    [IdlMember("attack", "https://webaudio.github.io/web-audio-api/#dom-dynamicscompressoroptions-attack")]
    public float Attack { get; init; } = 0.003f;

    /// <summary>Gets the initial knee in dB.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-dynamicscompressoroptions-knee"/>
    [IdlMember("knee", "https://webaudio.github.io/web-audio-api/#dom-dynamicscompressoroptions-knee")]
    public float Knee { get; init; } = 30;

    /// <summary>
    /// Gets the initial makeup gain in dB, or <see cref="MakeupGainOptions.Automatic"/> (the default) for a makeup gain
    /// computed from the threshold, the knee and the ratio.
    /// </summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-dynamicscompressoroptions-makeupgain"/>
    [IdlMember("makeupGain", "https://webaudio.github.io/web-audio-api/#dom-dynamicscompressoroptions-makeupgain")]
    public FloatOrMakeupGainOptions MakeupGain { get; init; } = MakeupGainOptions.Automatic;

    /// <summary>Gets the initial ratio.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-dynamicscompressoroptions-ratio"/>
    [IdlMember("ratio", "https://webaudio.github.io/web-audio-api/#dom-dynamicscompressoroptions-ratio")]
    public float Ratio { get; init; } = 12;

    /// <summary>Gets the initial release time in seconds.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-dynamicscompressoroptions-release"/>
    [IdlMember("release", "https://webaudio.github.io/web-audio-api/#dom-dynamicscompressoroptions-release")]
    public float Release { get; init; } = 0.25f;

    /// <summary>Gets the initial threshold in dB.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-dynamicscompressoroptions-threshold"/>
    [IdlMember("threshold", "https://webaudio.github.io/web-audio-api/#dom-dynamicscompressoroptions-threshold")]
    public float Threshold { get; init; } = -24;
}

/// <summary>
/// A dynamics compressor (https://webaudio.github.io/web-audio-api/#DynamicsCompressorNode): it lowers the level of the loud
/// parts of its input, with a fixed look-ahead delay, a soft knee, and a makeup gain.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#DynamicsCompressorNode"/>
[IdlInterface("DynamicsCompressorNode", "https://webaudio.github.io/web-audio-api/#DynamicsCompressorNode")]
public sealed class DynamicsCompressorNode : AudioNode
{
    /// <summary>The look-ahead delay in seconds.</summary>
    private const double PreDelay = 0.006;

    /// <summary>The maximum of the makeup gain in dB: 20 log10(FLT_MAX), about 771.</summary>
    private static readonly float MaxMakeupGain = (float)(20 * Math.Log10(float.MaxValue));

    // [[automatic makeup gain]]
    private readonly bool _automaticMakeupGain;
    private readonly int _delayFrames;
    private float[][] _delayLines = [];
    private int _delayPosition;
    private int _channels;

    // [[detector average]] and [[compressor gain]]
    private double _detectorAverage;
    private double _compressorGain = 1;

    /// <summary>Creates a compressor.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-dynamicscompressornode-dynamicscompressornode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-dynamicscompressornode-dynamicscompressornode")]
    public DynamicsCompressorNode(BaseAudioContext context, DynamicsCompressorOptions? options = null)
        : base(context, numberOfInputs: 1, numberOfOutputs: 1, channelCount: 2, ChannelCountMode.ClampedMax, ChannelInterpretation.Speakers)
    {
        Threshold = new AudioParam(context, this, "DynamicsCompressorNode.threshold", -24, -100, 0, AutomationRate.KRate, automationRateIsFixed: true);
        Knee = new AudioParam(context, this, "DynamicsCompressorNode.knee", 30, 0, 40, AutomationRate.KRate, automationRateIsFixed: true);
        Ratio = new AudioParam(context, this, "DynamicsCompressorNode.ratio", 12, 1, 20, AutomationRate.KRate, automationRateIsFixed: true);
        Attack = new AudioParam(context, this, "DynamicsCompressorNode.attack", 0.003f, 0, 1, AutomationRate.KRate, automationRateIsFixed: true);
        Release = new AudioParam(context, this, "DynamicsCompressorNode.release", 0.25f, 0, 1, AutomationRate.KRate, automationRateIsFixed: true);
        _automaticMakeupGain = options?.MakeupGain.Decibels is null;
        MakeupGain = new AudioParam(context, this, "DynamicsCompressorNode.makeupGain", 0, float.MinValue, MaxMakeupGain, AutomationRate.KRate, automationRateIsFixed: true)
        {
            IsComputedByNode = _automaticMakeupGain,
        };
        ApplyOptions(options);
        if (options is not null)
        {
            Attack.SetInitialValue(options.Attack);
            Knee.SetInitialValue(options.Knee);
            Ratio.SetInitialValue(options.Ratio);
            Release.SetInitialValue(options.Release);
            Threshold.SetInitialValue(options.Threshold);
            if (options.MakeupGain.Decibels is { } makeupGain)
            {
                MakeupGain.SetInitialValue(makeupGain);
            }
        }

        if (_automaticMakeupGain)
        {
            MakeupGain.SetComputedValue((float)LinearToDecibels(ComputeMakeupGain(Threshold.Value, Knee.Value, Ratio.Value)));
        }

        _delayFrames = (int)Math.Round(PreDelay * context.SampleRate, MidpointRounding.AwayFromZero);
    }

    /// <summary>Gets the threshold in dB above which the compression starts.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-dynamicscompressornode-threshold"/>
    [IdlMember("threshold", "https://webaudio.github.io/web-audio-api/#dom-dynamicscompressornode-threshold")]
    public AudioParam Threshold { get; }

    /// <summary>Gets the range in dB above the threshold where the curve transitions to the ratio.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-dynamicscompressornode-knee"/>
    [IdlMember("knee", "https://webaudio.github.io/web-audio-api/#dom-dynamicscompressornode-knee")]
    public AudioParam Knee { get; }

    /// <summary>Gets the dB change of the input for a 1 dB change of the output.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-dynamicscompressornode-ratio"/>
    [IdlMember("ratio", "https://webaudio.github.io/web-audio-api/#dom-dynamicscompressornode-ratio")]
    public AudioParam Ratio { get; }

    /// <summary>Gets the current gain reduction in dB.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-dynamicscompressornode-reduction"/>
    [IdlMember("reduction", "https://webaudio.github.io/web-audio-api/#dom-dynamicscompressornode-reduction")]
    public float Reduction { get; private set; }

    /// <summary>Gets the time in seconds to reduce the gain by 10 dB.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-dynamicscompressornode-attack"/>
    [IdlMember("attack", "https://webaudio.github.io/web-audio-api/#dom-dynamicscompressornode-attack")]
    public AudioParam Attack { get; }

    /// <summary>Gets the time in seconds to increase the gain by 10 dB.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-dynamicscompressornode-release"/>
    [IdlMember("release", "https://webaudio.github.io/web-audio-api/#dom-dynamicscompressornode-release")]
    public AudioParam Release { get; }

    /// <summary>
    /// Gets the makeup gain in dB applied to the signal. When the makeup gain is automatic (the default), its value is
    /// computed from the threshold, the knee and the ratio, and it cannot be modified: setting its value, scheduling or
    /// canceling automation events, and connecting a node to it throw an <c>InvalidStateError</c>.
    /// </summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-dynamicscompressornode-makeupgain"/>
    [IdlMember("makeupGain", "https://webaudio.github.io/web-audio-api/#dom-dynamicscompressornode-makeupgain")]
    public AudioParam MakeupGain { get; }

    private protected override IEnumerable<AudioParam> NodeParameters => [Threshold, Knee, Ratio, Attack, Release, MakeupGain];

    private protected override long TailFrames => _delayFrames + (long)Math.Ceiling(Context.SampleRate * 0.25);

    private protected override void ValidateChannelCount(uint value)
    {
        if (value > 2)
            throw new DomException($"The channel count of a compressor cannot be greater than 2 ({value})", "NotSupportedError");

        base.ValidateChannelCount(value);
    }

    private protected override void ValidateChannelCountMode(ChannelCountMode value)
    {
        if (value == ChannelCountMode.Max)
            throw new DomException("The channel count mode of a compressor cannot be 'max'", "NotSupportedError");
    }

    private protected override void OnBecameInactive()
    {
        foreach (var line in _delayLines)
        {
            Array.Clear(line);
        }

        _detectorAverage = 0;
        _compressorGain = 1;
        Reduction = 0;
    }

    private protected override void Process(long startFrame, int length)
    {
        var input = GetInputBus(0);
        var output = GetOutputBus(0);
        var channels = input.IsSilent ? Math.Max(input.NumberOfChannels, _channels) : input.NumberOfChannels;
        _channels = channels;
        if (_delayLines.Length < channels)
        {
            var lines = new float[channels][];
            for (var i = 0; i < channels; i++)
            {
                lines[i] = i < _delayLines.Length ? _delayLines[i] : new float[Math.Max(_delayFrames, 1)];
            }

            _delayLines = lines;
        }

        output.SetNumberOfChannels(channels);
        var sampleRate = (double)Context.SampleRate;
        var threshold = (double)Threshold.FinalValue;
        var knee = (double)Knee.FinalValue;
        var ratio = (double)Ratio.FinalValue;
        var attackFrames = Math.Max(Attack.FinalValue * sampleRate, 1);
        var releaseFrames = Math.Max(Release.FinalValue * sampleRate, 1);

        // The envelope: the attack approaches the target gain, the release increases the gain by 10 dB in the release time
        var attackRate = 1 - Math.Exp(-1 / attackFrames);
        var releaseRate = Math.Pow(10, 10.0 / 20 / releaseFrames);
        var detectorRate = 1 - Math.Exp(-1 / Math.Max(0.002 * sampleRate, 1));
        var makeupGain = _automaticMakeupGain ? ComputeMakeupGain(threshold, knee, ratio) : DecibelsToLinear(MakeupGain.FinalValue);
        for (var i = 0; i < length; i++)
        {
            // The level of the frame is the maximum of the channels
            var level = 0.0;
            if (!input.IsSilent)
            {
                for (var channel = 0; channel < input.NumberOfChannels; channel++)
                {
                    level = Math.Max(level, Math.Abs(input.GetChannel(channel)[i]));
                }
            }

            var attenuation = level < 0.0001 ? 1.0 : CompressionCurve(level, threshold, knee, ratio) / level;
            var releasing = attenuation > _compressorGain;
            _detectorAverage += (attenuation - _detectorAverage) * detectorRate;
            _detectorAverage = Math.Min(_detectorAverage, 1);
            if (releasing)
            {
                _compressorGain = Math.Min(_compressorGain * releaseRate, 1);
            }
            else
            {
                _compressorGain += (_detectorAverage - _compressorGain) * attackRate;
            }

            var gain = _compressorGain * makeupGain;
            for (var channel = 0; channel < channels; channel++)
            {
                var line = _delayLines[channel];
                var sample = input.IsSilent || channel >= input.NumberOfChannels ? 0 : input.GetChannel(channel)[i];
                var delayed = _delayFrames == 0 ? sample : line[_delayPosition];
                if (_delayFrames > 0)
                {
                    line[_delayPosition] = sample;
                }

                output.GetChannel(channel)[i] = (float)(delayed * gain);
            }

            if (_delayFrames > 0)
            {
                _delayPosition = (_delayPosition + 1) % _delayFrames;
            }
        }

        // The metering gain is updated once per render quantum
        Reduction = (float)(_compressorGain <= 0 ? -1000 : 20 * Math.Log10(_compressorGain));
        if (_automaticMakeupGain)
        {
            MakeupGain.SetComputedValue((float)LinearToDecibels(makeupGain));
        }

        output.MarkNonSilent();
    }

    /// <summary>
    /// https://webaudio.github.io/web-audio-api/#computing-the-makeup-gain: the 0.6 power of the inverse of the compression
    /// curve applied to 1.
    /// </summary>
    private static double ComputeMakeupGain(double threshold, double knee, double ratio) => Math.Pow(1 / CompressionCurve(1, threshold, knee, ratio), 0.6);

    private static double LinearToDecibels(double value) => 20 * Math.Log10(value);

    private static double DecibelsToLinear(double value) => Math.Pow(10, value / 20);

    /// <summary>
    /// The compression curve (https://webaudio.github.io/web-audio-api/#compression-curve): the identity up to the threshold,
    /// a quadratic soft knee in dB, then the ratio.
    /// </summary>
    private static double CompressionCurve(double x, double threshold, double knee, double ratio)
    {
        var db = 20 * Math.Log10(x);
        double outputDb;
        if (db <= threshold)
        {
            outputDb = db;
        }
        else if (knee > 0 && db <= threshold + knee)
        {
            var over = db - threshold;
            outputDb = db + (((1 / ratio) - 1) * over * over / (2 * knee));
        }
        else
        {
            outputDb = threshold + knee + ((db - threshold - knee) / ratio) + (((1 / ratio) - 1) * knee / 2);
        }

        return Math.Pow(10, outputDb / 20);
    }
}

/// <summary>The makeup gain options of a <see cref="DynamicsCompressorNode"/> (https://webaudio.github.io/web-audio-api/#enumdef-makeupgainoptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#enumdef-makeupgainoptions"/>
[IdlInterface("MakeupGainOptions", "https://webaudio.github.io/web-audio-api/#enumdef-makeupgainoptions")]
public enum MakeupGainOptions
{
    /// <summary>The makeup gain is computed from the threshold, the knee and the ratio of the compressor.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-makeupgainoptions-automatic"/>
    [IdlMember("automatic", "https://webaudio.github.io/web-audio-api/#dom-makeupgainoptions-automatic")]
    Automatic,
}

/// <summary>
/// The <c>(float or MakeupGainOptions)</c> union of the makeup gain of a <see cref="DynamicsCompressorOptions"/>
/// (https://webaudio.github.io/web-audio-api/#dom-dynamicscompressoroptions-makeupgain): a gain in dB, or the automatic
/// makeup gain.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#dom-dynamicscompressoroptions-makeupgain"/>
[IdlInterface("(float or MakeupGainOptions)", "https://webaudio.github.io/web-audio-api/#dom-dynamicscompressoroptions-makeupgain")]
public readonly struct FloatOrMakeupGainOptions : IUnionValue, IEquatable<FloatOrMakeupGainOptions>
{
    // Null for the default value of the struct: the automatic makeup gain
    private readonly object? _value;

    private FloatOrMakeupGainOptions(object value) => _value = value;

    /// <summary>Gets the value: a <see cref="MakeupGainOptions"/> or a <see cref="float"/>.</summary>
    [NotInIdl]
    public object Value => _value ?? MakeupGainOptions.Automatic;

    /// <summary>Gets the makeup gain in dB when the value is a number, otherwise null.</summary>
    [NotInIdl]
    public float? Decibels => Value as float?;

    /// <summary>Gets the option when the value is a <see cref="MakeupGainOptions"/>, otherwise null.</summary>
    [NotInIdl]
    public MakeupGainOptions? Option => Value as MakeupGainOptions?;

    /// <summary>Creates a union value from an option.</summary>
    [NotInIdl]
    public static implicit operator FloatOrMakeupGainOptions(MakeupGainOptions value) => new(value);

    /// <summary>Creates a union value from a gain in dB.</summary>
    [NotInIdl]
    public static implicit operator FloatOrMakeupGainOptions(float value) => new(value);

    /// <summary>Creates a union value from an option.</summary>
    [NotInIdl]
    public static FloatOrMakeupGainOptions FromMakeupGainOptions(MakeupGainOptions value) => value;

    /// <summary>Creates a union value from a gain in dB.</summary>
    [NotInIdl]
    public static FloatOrMakeupGainOptions FromSingle(float value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(FloatOrMakeupGainOptions other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is FloatOrMakeupGainOptions other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Returns whether the two values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(FloatOrMakeupGainOptions left, FloatOrMakeupGainOptions right) => left.Equals(right);

    /// <summary>Returns whether the two values are different.</summary>
    [NotInIdl]
    public static bool operator !=(FloatOrMakeupGainOptions left, FloatOrMakeupGainOptions right) => !left.Equals(right);
}
