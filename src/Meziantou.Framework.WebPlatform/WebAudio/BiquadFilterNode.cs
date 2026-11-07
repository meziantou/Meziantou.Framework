using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The types of a <see cref="BiquadFilterNode"/> (https://webaudio.github.io/web-audio-api/#enumdef-biquadfiltertype).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#enumdef-biquadfiltertype"/>
[IdlInterface("BiquadFilterType", "https://webaudio.github.io/web-audio-api/#enumdef-biquadfiltertype")]
public enum BiquadFilterType
{
    /// <summary>A second-order lowpass filter with a resonance (Q in dB).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfiltertype-lowpass"/>
    [IdlMember("lowpass", "https://webaudio.github.io/web-audio-api/#dom-biquadfiltertype-lowpass")]
    Lowpass,

    /// <summary>A second-order highpass filter with a resonance (Q in dB).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfiltertype-highpass"/>
    [IdlMember("highpass", "https://webaudio.github.io/web-audio-api/#dom-biquadfiltertype-highpass")]
    Highpass,

    /// <summary>A second-order bandpass filter.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfiltertype-bandpass"/>
    [IdlMember("bandpass", "https://webaudio.github.io/web-audio-api/#dom-biquadfiltertype-bandpass")]
    Bandpass,

    /// <summary>A second-order lowshelf filter.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfiltertype-lowshelf"/>
    [IdlMember("lowshelf", "https://webaudio.github.io/web-audio-api/#dom-biquadfiltertype-lowshelf")]
    Lowshelf,

    /// <summary>A second-order highshelf filter.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfiltertype-highshelf"/>
    [IdlMember("highshelf", "https://webaudio.github.io/web-audio-api/#dom-biquadfiltertype-highshelf")]
    Highshelf,

    /// <summary>A peaking filter.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfiltertype-peaking"/>
    [IdlMember("peaking", "https://webaudio.github.io/web-audio-api/#dom-biquadfiltertype-peaking")]
    Peaking,

    /// <summary>A notch (band-stop) filter.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfiltertype-notch"/>
    [IdlMember("notch", "https://webaudio.github.io/web-audio-api/#dom-biquadfiltertype-notch")]
    Notch,

    /// <summary>A second-order allpass filter.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfiltertype-allpass"/>
    [IdlMember("allpass", "https://webaudio.github.io/web-audio-api/#dom-biquadfiltertype-allpass")]
    Allpass,
}

/// <summary>The options of a <see cref="BiquadFilterNode"/> (https://webaudio.github.io/web-audio-api/#BiquadFilterOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#BiquadFilterOptions"/>
[IdlInterface("BiquadFilterOptions", "https://webaudio.github.io/web-audio-api/#BiquadFilterOptions")]
public sealed class BiquadFilterOptions : AudioNodeOptions
{
    /// <summary>Gets the type of the filter.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfilteroptions-type"/>
    [IdlMember("type", "https://webaudio.github.io/web-audio-api/#dom-biquadfilteroptions-type")]
    public BiquadFilterType Type { get; init; }

    /// <summary>Gets the initial Q.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfilteroptions-q"/>
    [IdlMember("Q", "https://webaudio.github.io/web-audio-api/#dom-biquadfilteroptions-q")]
    public float Q { get; init; } = 1;

    /// <summary>Gets the initial detune in cents.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfilteroptions-detune"/>
    [IdlMember("detune", "https://webaudio.github.io/web-audio-api/#dom-biquadfilteroptions-detune")]
    public float Detune { get; init; }

    /// <summary>Gets the initial frequency in Hz.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfilteroptions-frequency"/>
    [IdlMember("frequency", "https://webaudio.github.io/web-audio-api/#dom-biquadfilteroptions-frequency")]
    public float Frequency { get; init; } = 350;

    /// <summary>Gets the initial gain in dB.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfilteroptions-gain"/>
    [IdlMember("gain", "https://webaudio.github.io/web-audio-api/#dom-biquadfilteroptions-gain")]
    public float Gain { get; init; }
}

/// <summary>
/// A second-order filter (https://webaudio.github.io/web-audio-api/#BiquadFilterNode): lowpass, highpass, bandpass, shelves,
/// peaking, notch or allpass, with the coefficients of the Audio EQ Cookbook.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#BiquadFilterNode"/>
[IdlInterface("BiquadFilterNode", "https://webaudio.github.io/web-audio-api/#BiquadFilterNode")]
public sealed class BiquadFilterNode : AudioNode
{
    /// <summary>The value under which the state of a filter is considered silent.</summary>
    internal const double SilentState = 1e-30;

    private FilterState[] _states = [];
    private int _channels;

    /// <summary>Creates a biquad filter.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfilternode-biquadfilternode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-biquadfilternode-biquadfilternode")]
    public BiquadFilterNode(BaseAudioContext context, BiquadFilterOptions? options = null)
        : base(context, numberOfInputs: 1, numberOfOutputs: 1, channelCount: 2, ChannelCountMode.Max, ChannelInterpretation.Speakers)
    {
        var nyquist = context.SampleRate / 2;
        Frequency = new AudioParam(context, this, "BiquadFilterNode.frequency", 350, 0, nyquist, AutomationRate.ARate);
        Detune = new AudioParam(context, this, "BiquadFilterNode.detune", 0, -OscillatorNode.MaxDetune, OscillatorNode.MaxDetune, AutomationRate.ARate);
        Q = new AudioParam(context, this, "BiquadFilterNode.Q", 1, -float.MaxValue, float.MaxValue, AutomationRate.ARate);
        Gain = new AudioParam(context, this, "BiquadFilterNode.gain", 0, -float.MaxValue, (float)(40 * Math.Log10(float.MaxValue)), AutomationRate.ARate);
        ApplyOptions(options);
        if (options is not null)
        {
            Type = options.Type;
            Q.SetInitialValue(options.Q);
            Detune.SetInitialValue(options.Detune);
            Frequency.SetInitialValue(options.Frequency);
            Gain.SetInitialValue(options.Gain);
        }
    }

    /// <summary>Gets or sets the type of the filter.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfilternode-type"/>
    [IdlMember("type", "https://webaudio.github.io/web-audio-api/#dom-biquadfilternode-type")]
    public BiquadFilterType Type { get; set; }

    /// <summary>Gets the frequency in Hz.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfilternode-frequency"/>
    [IdlMember("frequency", "https://webaudio.github.io/web-audio-api/#dom-biquadfilternode-frequency")]
    public AudioParam Frequency { get; }

    /// <summary>Gets the detune of the frequency in cents.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfilternode-detune"/>
    [IdlMember("detune", "https://webaudio.github.io/web-audio-api/#dom-biquadfilternode-detune")]
    public AudioParam Detune { get; }

    /// <summary>Gets the quality factor.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfilternode-q"/>
    [IdlMember("Q", "https://webaudio.github.io/web-audio-api/#dom-biquadfilternode-q")]
    public AudioParam Q { get; }

    /// <summary>Gets the gain in dB (shelves and peaking).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfilternode-gain"/>
    [IdlMember("gain", "https://webaudio.github.io/web-audio-api/#dom-biquadfilternode-gain")]
    public AudioParam Gain { get; }

    /// <summary>Computes the frequency response of the filter for the current values of its parameters.</summary>
    /// <param name="frequencyHz">The frequencies in Hz (a <c>Float32Array</c>).</param>
    /// <param name="magResponse">Receives the linear magnitudes (a <c>Float32Array</c> of the same length).</param>
    /// <param name="phaseResponse">Receives the phases in radians (a <c>Float32Array</c> of the same length).</param>
    /// <exception cref="DomException">The arrays have different lengths (an <c>InvalidAccessError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-biquadfilternode-getfrequencyresponse"/>
    [IdlMember("getFrequencyResponse", "https://webaudio.github.io/web-audio-api/#dom-biquadfilternode-getfrequencyresponse")]
    public void GetFrequencyResponse(ReadOnlySpan<byte> frequencyHz, Span<byte> magResponse, Span<byte> phaseResponse)
    {
        var frequencies = unsafe(MemoryMarshal.Cast<byte, float>(frequencyHz));
        var magnitudes = unsafe(MemoryMarshal.Cast<byte, float>(magResponse));
        var phases = unsafe(MemoryMarshal.Cast<byte, float>(phaseResponse));
        if (magnitudes.Length != frequencies.Length || phases.Length != frequencies.Length)
            throw new DomException("The frequency, magnitude and phase arrays must have the same length", "InvalidAccessError");

        var time = Context.CurrentTime;
        var coefficients = ComputeCoefficients(Type, Frequency.GetIntrinsicValueAtTime(time), Detune.GetIntrinsicValueAtTime(time), Q.GetIntrinsicValueAtTime(time), Gain.GetIntrinsicValueAtTime(time), Context.SampleRate);
        var nyquist = Context.SampleRate / 2.0;
        for (var i = 0; i < frequencies.Length; i++)
        {
            var frequency = frequencies[i];
            if (!(frequency >= 0 && frequency <= nyquist))
            {
                magnitudes[i] = float.NaN;
                phases[i] = float.NaN;
                continue;
            }

            // H(z) at z = e^(jω), ω = π f / nyquist
            var omega = -Math.PI * frequency / nyquist;
            var z1 = new System.Numerics.Complex(Math.Cos(omega), Math.Sin(omega));
            var z2 = z1 * z1;
            var numerator = coefficients.B0 + (coefficients.B1 * z1) + (coefficients.B2 * z2);
            var denominator = 1 + (coefficients.A1 * z1) + (coefficients.A2 * z2);
            var response = numerator / denominator;
            magnitudes[i] = (float)response.Magnitude;
            phases[i] = (float)Math.Atan2(response.Imaginary, response.Real);
        }
    }

    private protected override IEnumerable<AudioParam> NodeParameters => [Frequency, Detune, Q, Gain];

    // The filter keeps producing output while its state is not silent
    private protected override bool IsSourceActive(long startFrame, int length)
    {
        foreach (var state in _states)
        {
            if (!state.IsSilent)
                return true;
        }

        return false;
    }

    private protected override void OnBecameInactive()
    {
        foreach (ref var state in _states.AsSpan())
        {
            state = default;
        }
    }

    private protected override void Process(long startFrame, int length)
    {
        var input = GetInputBus(0);
        var output = GetOutputBus(0);

        // The tail of the filter keeps the channels of the last input
        var channels = input.IsSilent ? Math.Max(input.NumberOfChannels, _channels) : input.NumberOfChannels;
        if (_states.Length < channels)
        {
            Array.Resize(ref _states, channels);
        }

        for (var channel = channels; channel < _channels; channel++)
        {
            _states[channel] = default;
        }

        _channels = channels;

        output.SetNumberOfChannels(channels);
        var sampleRate = Context.SampleRate;
        var constant = Frequency.IsConstant && Detune.IsConstant && Q.IsConstant && Gain.IsConstant;
        var coefficients = ComputeCoefficients(Type, Frequency.FinalValue, Detune.FinalValue, Q.FinalValue, Gain.FinalValue, sampleRate);
        var frequencies = Frequency.Values;
        var detunes = Detune.Values;
        var qs = Q.Values;
        var gains = Gain.Values;
        for (var channel = 0; channel < channels; channel++)
        {
            ref var state = ref _states[channel];
            var source = input.GetChannelSpan(channel);
            var destination = output.GetChannelSpan(channel);
            var silent = input.IsSilent || channel >= input.NumberOfChannels;
            for (var i = 0; i < length; i++)
            {
                if (!constant)
                {
                    coefficients = ComputeCoefficients(
                        Type,
                        Frequency.IsConstant ? Frequency.FinalValue : frequencies[i],
                        Detune.IsConstant ? Detune.FinalValue : detunes[i],
                        Q.IsConstant ? Q.FinalValue : qs[i],
                        Gain.IsConstant ? Gain.FinalValue : gains[i],
                        sampleRate);
                }

                var x = silent ? 0.0 : source[i];
                var y = (coefficients.B0 * x) + (coefficients.B1 * state.X1) + (coefficients.B2 * state.X2) - (coefficients.A1 * state.Y1) - (coefficients.A2 * state.Y2);
                state.X2 = state.X1;
                state.X1 = x;
                state.Y2 = state.Y1;
                state.Y1 = y;
                destination[i] = (float)y;
            }
        }

        output.MarkNonSilent();
    }

    /// <summary>The normalized coefficients (divided by a0) of a filter (https://webaudio.github.io/web-audio-api/#filters-characteristics).</summary>
    internal static Coefficients ComputeCoefficients(BiquadFilterType type, double frequency, double detune, double q, double gain, double sampleRate)
    {
        var nyquist = sampleRate / 2;
        var computedFrequency = detune == 0 ? frequency : frequency * Math.Pow(2, detune / 1200);

        // The frequency normalized to [0, 1] (1 is the Nyquist frequency)
        var f = computedFrequency / nyquist;
        f = double.IsNaN(f) ? 0 : Math.Clamp(f, 0, 1);
        switch (type)
        {
            case BiquadFilterType.Lowpass:
            {
                if (f == 1)
                    return new(1, 0, 0, 0, 0);

                var theta = Math.PI * f;
                var alpha = Math.Sin(theta) / (2 * Math.Pow(10, q / 20));
                var cosw = Math.Cos(theta);
                var beta = (1 - cosw) / 2;
                return Normalize(beta, 2 * beta, beta, 1 + alpha, -2 * cosw, 1 - alpha);
            }

            case BiquadFilterType.Highpass:
            {
                if (f == 1)
                    return new(0, 0, 0, 0, 0);

                if (f == 0)
                    return new(1, 0, 0, 0, 0);

                var theta = Math.PI * f;
                var alpha = Math.Sin(theta) / (2 * Math.Pow(10, q / 20));
                var cosw = Math.Cos(theta);
                var beta = (1 + cosw) / 2;
                return Normalize(beta, -2 * beta, beta, 1 + alpha, -2 * cosw, 1 - alpha);
            }

            case BiquadFilterType.Bandpass:
            {
                if (!(f > 0 && f < 1))
                    return new(0, 0, 0, 0, 0);

                if (!(q > 0))
                    return new(1, 0, 0, 0, 0);

                var w0 = Math.PI * f;
                var alpha = Math.Sin(w0) / (2 * q);
                var k = Math.Cos(w0);
                return Normalize(alpha, 0, -alpha, 1 + alpha, -2 * k, 1 - alpha);
            }

            case BiquadFilterType.Lowshelf:
            {
                var a = Math.Pow(10, gain / 40);
                if (f == 1)
                    return new(a * a, 0, 0, 0, 0);

                if (f == 0)
                    return new(1, 0, 0, 0, 0);

                var w0 = Math.PI * f;
                var alpha = 0.5 * Math.Sin(w0) * Math.Sqrt(((a + (1 / a)) * (1 / 1.0 - 1)) + 2);
                var k = Math.Cos(w0);
                var k2 = 2 * Math.Sqrt(a) * alpha;
                var ap1 = a + 1;
                var am1 = a - 1;
                return Normalize(a * (ap1 - (am1 * k) + k2), 2 * a * (am1 - (ap1 * k)), a * (ap1 - (am1 * k) - k2), ap1 + (am1 * k) + k2, -2 * (am1 + (ap1 * k)), ap1 + (am1 * k) - k2);
            }

            case BiquadFilterType.Highshelf:
            {
                var a = Math.Pow(10, gain / 40);
                if (f == 1)
                    return new(1, 0, 0, 0, 0);

                if (!(f > 0))
                    return new(a * a, 0, 0, 0, 0);

                var w0 = Math.PI * f;
                var alpha = 0.5 * Math.Sin(w0) * Math.Sqrt(((a + (1 / a)) * (1 / 1.0 - 1)) + 2);
                var k = Math.Cos(w0);
                var k2 = 2 * Math.Sqrt(a) * alpha;
                var ap1 = a + 1;
                var am1 = a - 1;
                return Normalize(a * (ap1 + (am1 * k) + k2), -2 * a * (am1 + (ap1 * k)), a * (ap1 + (am1 * k) - k2), ap1 - (am1 * k) + k2, 2 * (am1 - (ap1 * k)), ap1 - (am1 * k) - k2);
            }

            case BiquadFilterType.Peaking:
            {
                var a = Math.Pow(10, gain / 40);
                if (!(f > 0 && f < 1))
                    return new(1, 0, 0, 0, 0);

                if (!(q > 0))
                    return new(a * a, 0, 0, 0, 0);

                var w0 = Math.PI * f;
                var alpha = Math.Sin(w0) / (2 * q);
                var k = Math.Cos(w0);
                return Normalize(1 + (alpha * a), -2 * k, 1 - (alpha * a), 1 + (alpha / a), -2 * k, 1 - (alpha / a));
            }

            case BiquadFilterType.Notch:
            {
                if (!(f > 0 && f < 1))
                    return new(1, 0, 0, 0, 0);

                if (!(q > 0))
                    return new(0, 0, 0, 0, 0);

                var w0 = Math.PI * f;
                var alpha = Math.Sin(w0) / (2 * q);
                var k = Math.Cos(w0);
                return Normalize(1, -2 * k, 1, 1 + alpha, -2 * k, 1 - alpha);
            }

            default:
            {
                if (!(f > 0 && f < 1))
                    return new(1, 0, 0, 0, 0);

                if (!(q > 0))
                    return new(-1, 0, 0, 0, 0);

                var w0 = Math.PI * f;
                var alpha = Math.Sin(w0) / (2 * q);
                var k = Math.Cos(w0);
                return Normalize(1 - alpha, -2 * k, 1 + alpha, 1 + alpha, -2 * k, 1 - alpha);
            }
        }
    }

    private static Coefficients Normalize(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        var scale = 1 / a0;
        return new(b0 * scale, b1 * scale, b2 * scale, a1 * scale, a2 * scale);
    }

    /// <summary>The coefficients of a biquad, normalized by a0.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Coefficients(double B0, double B1, double B2, double A1, double A2);

    [StructLayout(LayoutKind.Auto)]
    private struct FilterState
    {
        public double X1;
        public double X2;
        public double Y1;
        public double Y2;

        public readonly bool IsSilent => Math.Abs(X1) < SilentState && Math.Abs(X2) < SilentState && Math.Abs(Y1) < SilentState && Math.Abs(Y2) < SilentState;
    }
}
