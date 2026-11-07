using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The options of an <see cref="IIRFilterNode"/> (https://webaudio.github.io/web-audio-api/#IIRFilterOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#IIRFilterOptions"/>
[IdlInterface("IIRFilterOptions", "https://webaudio.github.io/web-audio-api/#IIRFilterOptions")]
public sealed class IIRFilterOptions : AudioNodeOptions
{
    /// <summary>Gets the coefficients of the numerator of the transfer function.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-iirfilteroptions-feedforward"/>
    [IdlMember("feedforward", "https://webaudio.github.io/web-audio-api/#dom-iirfilteroptions-feedforward")]
    public required IReadOnlyList<double> Feedforward { get; init; }

    /// <summary>Gets the coefficients of the denominator of the transfer function.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-iirfilteroptions-feedback"/>
    [IdlMember("feedback", "https://webaudio.github.io/web-audio-api/#dom-iirfilteroptions-feedback")]
    public required IReadOnlyList<double> Feedback { get; init; }
}

/// <summary>A general infinite impulse response filter (https://webaudio.github.io/web-audio-api/#IIRFilterNode).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#IIRFilterNode"/>
[IdlInterface("IIRFilterNode", "https://webaudio.github.io/web-audio-api/#IIRFilterNode")]
public sealed class IIRFilterNode : AudioNode
{
    /// <summary>The maximum number of coefficients.</summary>
    private const int MaxCoefficients = 20;

    // The histories have 32 entries (a power of two greater than the number of coefficients)
    private const int MaxCoefficientsMask = 31;

    private readonly double[] _feedforward;
    private readonly double[] _feedback;
    private double[][] _inputHistory = [];
    private double[][] _outputHistory = [];
    private int _channels;
    private int _position;

    /// <summary>Creates an IIR filter.</summary>
    /// <exception cref="DomException">
    /// An array is empty or has more than 20 coefficients (a <c>NotSupportedError</c>), the feedforward coefficients are all
    /// zero or the first feedback coefficient is zero (an <c>InvalidStateError</c>).
    /// </exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-iirfilternode-iirfilternode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-iirfilternode-iirfilternode")]
    public IIRFilterNode(BaseAudioContext context, IIRFilterOptions options)
        : base(context, numberOfInputs: 1, numberOfOutputs: 1, channelCount: 2, ChannelCountMode.Max, ChannelInterpretation.Speakers)
    {
        ArgumentNullException.ThrowIfNull(options);
        var feedforward = options.Feedforward;
        var feedback = options.Feedback;
        if (feedforward.Count is 0 or > MaxCoefficients)
            throw new DomException($"The number of feedforward coefficients ({feedforward.Count}) is not between 1 and {MaxCoefficients}", "NotSupportedError");

        if (feedback.Count is 0 or > MaxCoefficients)
            throw new DomException($"The number of feedback coefficients ({feedback.Count}) is not between 1 and {MaxCoefficients}", "NotSupportedError");

        if (feedforward.All(c => c == 0))
            throw new DomException("The feedforward coefficients cannot all be zero", "InvalidStateError");

        if (feedback[0] == 0)
            throw new DomException("The first feedback coefficient cannot be zero", "InvalidStateError");

        // The coefficients are normalized by the first feedback coefficient
        var a0 = feedback[0];
        _feedforward = [.. feedforward.Select(c => c / a0)];
        _feedback = [.. feedback.Select(c => c / a0)];
        ApplyOptions(options);
    }

    /// <summary>Computes the frequency response of the filter.</summary>
    /// <param name="frequencyHz">The frequencies in Hz (a <c>Float32Array</c>).</param>
    /// <param name="magResponse">Receives the linear magnitudes (a <c>Float32Array</c> of the same length).</param>
    /// <param name="phaseResponse">Receives the phases in radians (a <c>Float32Array</c> of the same length).</param>
    /// <exception cref="DomException">The arrays have different lengths (an <c>InvalidAccessError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-iirfilternode-getfrequencyresponse"/>
    [IdlMember("getFrequencyResponse", "https://webaudio.github.io/web-audio-api/#dom-iirfilternode-getfrequencyresponse")]
    public void GetFrequencyResponse(ReadOnlySpan<byte> frequencyHz, Span<byte> magResponse, Span<byte> phaseResponse)
    {
        var frequencies = unsafe(MemoryMarshal.Cast<byte, float>(frequencyHz));
        var magnitudes = unsafe(MemoryMarshal.Cast<byte, float>(magResponse));
        var phases = unsafe(MemoryMarshal.Cast<byte, float>(phaseResponse));
        if (magnitudes.Length != frequencies.Length || phases.Length != frequencies.Length)
            throw new DomException("The frequency, magnitude and phase arrays must have the same length", "InvalidAccessError");

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

            var omega = -Math.PI * frequency / nyquist;
            var z = new System.Numerics.Complex(Math.Cos(omega), Math.Sin(omega));
            var response = EvaluatePolynomial(_feedforward, z) / EvaluatePolynomial(_feedback, z);
            magnitudes[i] = (float)response.Magnitude;
            phases[i] = (float)Math.Atan2(response.Imaginary, response.Real);
        }
    }

    /// <summary>The value of <c>sum c[k] z^k</c> (with z = e^(-jω)).</summary>
    private static System.Numerics.Complex EvaluatePolynomial(double[] coefficients, System.Numerics.Complex z)
    {
        var result = System.Numerics.Complex.Zero;
        for (var k = coefficients.Length - 1; k >= 0; k--)
        {
            result = (result * z) + coefficients[k];
        }

        return result;
    }

    // The filter keeps producing output while its state is not silent
    private protected override bool IsSourceActive(long startFrame, int length)
    {
        foreach (var history in _outputHistory)
        {
            foreach (var value in history)
            {
                if (Math.Abs(value) >= BiquadFilterNode.SilentState)
                    return true;
            }
        }

        return false;
    }

    private protected override void OnBecameInactive()
    {
        foreach (var history in _inputHistory)
        {
            Array.Clear(history);
        }

        foreach (var history in _outputHistory)
        {
            Array.Clear(history);
        }
    }

    private protected override void Process(long startFrame, int length)
    {
        var input = GetInputBus(0);
        var output = GetOutputBus(0);
        var channels = input.IsSilent ? Math.Max(input.NumberOfChannels, _channels) : input.NumberOfChannels;
        if (_inputHistory.Length < channels)
        {
            var size = MaxCoefficientsMask + 1;
            var inputHistory = new double[channels][];
            var outputHistory = new double[channels][];
            for (var i = 0; i < channels; i++)
            {
                inputHistory[i] = i < _inputHistory.Length ? _inputHistory[i] : new double[size];
                outputHistory[i] = i < _outputHistory.Length ? _outputHistory[i] : new double[size];
            }

            _inputHistory = inputHistory;
            _outputHistory = outputHistory;
        }

        for (var channel = channels; channel < _channels; channel++)
        {
            Array.Clear(_inputHistory[channel]);
            Array.Clear(_outputHistory[channel]);
        }

        _channels = channels;
        output.SetNumberOfChannels(channels);
        var b = _feedforward;
        var a = _feedback;
        var position = _position;
        for (var channel = 0; channel < channels; channel++)
        {
            var x = _inputHistory[channel];
            var y = _outputHistory[channel];
            var silent = input.IsSilent || channel >= input.NumberOfChannels;
            var source = silent ? default : input.GetChannelSpan(channel);
            var destination = output.GetChannelSpan(channel);
            position = _position;
            for (var i = 0; i < length; i++)
            {
                // y[n] = sum b[k] x[n-k] - sum a[k] y[n-k] (a[0] = 1), with circular histories of the past values
                var sample = silent ? 0.0 : source[i];
                var sum = b[0] * sample;
                for (var k = 1; k < b.Length; k++)
                {
                    sum += b[k] * x[(position - k) & MaxCoefficientsMask];
                }

                for (var k = 1; k < a.Length; k++)
                {
                    sum -= a[k] * y[(position - k) & MaxCoefficientsMask];
                }

                x[position] = sample;
                y[position] = sum;
                position = (position + 1) & MaxCoefficientsMask;
                destination[i] = (float)sum;
            }
        }

        _position = position;
        output.MarkNonSilent();
    }
}
