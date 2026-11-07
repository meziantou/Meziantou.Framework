using System.ComponentModel;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.WebAudio.Rendering;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The oversampling of a <see cref="WaveShaperNode"/> (https://webaudio.github.io/web-audio-api/#enumdef-oversampletype).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#enumdef-oversampletype"/>
[IdlInterface("OverSampleType", "https://webaudio.github.io/web-audio-api/#enumdef-oversampletype")]
public enum OverSampleType
{
    /// <summary>No oversampling.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oversampletype-none"/>
    [IdlMember("none", "https://webaudio.github.io/web-audio-api/#dom-oversampletype-none")]
    None,

    /// <summary>The curve is applied at twice the sample rate.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oversampletype-2x"/>
    [IdlMember("2x", "https://webaudio.github.io/web-audio-api/#dom-oversampletype-2x")]
    TwoTimes,

    /// <summary>The curve is applied at four times the sample rate.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-oversampletype-4x"/>
    [IdlMember("4x", "https://webaudio.github.io/web-audio-api/#dom-oversampletype-4x")]
    FourTimes,
}

/// <summary>The options of a <see cref="WaveShaperNode"/> (https://webaudio.github.io/web-audio-api/#WaveShaperOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#WaveShaperOptions"/>
[IdlInterface("WaveShaperOptions", "https://webaudio.github.io/web-audio-api/#WaveShaperOptions")]
public sealed class WaveShaperOptions : AudioNodeOptions
{
    /// <summary>Gets the shaping curve.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-waveshaperoptions-curve"/>
    [IdlMember("curve", "https://webaudio.github.io/web-audio-api/#dom-waveshaperoptions-curve")]
    public IReadOnlyList<float>? Curve { get; init; }

    /// <summary>Gets the oversampling.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-waveshaperoptions-oversample"/>
    [IdlMember("oversample", "https://webaudio.github.io/web-audio-api/#dom-waveshaperoptions-oversample")]
    public OverSampleType Oversample { get; init; }
}

/// <summary>A node that applies a non-linear distortion curve to its input (https://webaudio.github.io/web-audio-api/#WaveShaperNode).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#WaveShaperNode"/>
[IdlInterface("WaveShaperNode", "https://webaudio.github.io/web-audio-api/#WaveShaperNode")]
public sealed class WaveShaperNode : AudioNode
{
    private object? _curveArray;
    private float[]? _curve;

    // [[curve set]]
    private bool _curveSet;
    private Resampler[] _resamplers = [];
    private float[] _oversampled = [];

    /// <summary>Creates a wave shaper.</summary>
    /// <exception cref="DomException">The curve has less than 2 values (an <c>InvalidStateError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-waveshapernode-waveshapernode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-waveshapernode-waveshapernode")]
    public WaveShaperNode(BaseAudioContext context, WaveShaperOptions? options = null)
        : base(context, numberOfInputs: 1, numberOfOutputs: 1, channelCount: 2, ChannelCountMode.Max, ChannelInterpretation.Speakers)
    {
        ApplyOptions(options);
        if (options is not null)
        {
            if (options.Curve is { } curve)
            {
                Curve = curve;
            }

            Oversample = options.Oversample;
        }
    }

    /// <summary>Gets or sets the <c>Float32Array</c> of the curve for the scripts: use <see cref="Curve"/> in .NET code.</summary>
    /// <exception cref="DomException">The curve has less than 2 values, or a curve was already set (an <c>InvalidStateError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-waveshapernode-curve"/>
    [IdlMember("curve", "https://webaudio.github.io/web-audio-api/#dom-waveshapernode-curve")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object? CurveArray
    {
        get => _curveArray;
        set
        {
            if (value is not JsBufferSource source)
            {
                SetCurve(null, null);
                return;
            }

            SetCurve(unsafe(MemoryMarshal.Cast<byte, float>(source.Bytes.Span)).ToArray(), source);
        }
    }

    /// <summary>Gets or sets the shaping curve (a copy): the input from -1 to 1 is mapped to the values, linearly interpolated.</summary>
    /// <exception cref="DomException">The curve has less than 2 values, or a curve was already set (an <c>InvalidStateError</c>).</exception>
    [NotInIdl("The curve for the .NET code: the curve attribute is CurveArray")]
    public IReadOnlyList<float>? Curve
    {
        get => _curve is null ? null : [.. _curve];
        set
        {
            if (value is null)
            {
                SetCurve(null, null);
                return;
            }

            float[] curve = [.. value];
            SetCurve(curve, new JsBufferSource(JsBufferKind.Float32Array, unsafe(MemoryMarshal.AsBytes(curve.AsSpan())).ToArray()));
        }
    }

    private void SetCurve(float[]? curve, object? array)
    {
        if (curve is not null)
        {
            if (curve.Length < 2)
                throw new DomException($"The curve must have at least 2 values, not {curve.Length}", "InvalidStateError");

            if (_curveSet)
                throw new DomException("The curve of the wave shaper was already set", "InvalidStateError");

            _curveSet = true;
        }

        _curve = curve;
        _curveArray = array;
    }

    /// <summary>Gets or sets the oversampling.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-waveshapernode-oversample"/>
    [IdlMember("oversample", "https://webaudio.github.io/web-audio-api/#dom-waveshapernode-oversample")]
    public OverSampleType Oversample { get; set; }

    // A curve whose value for 0 is not 0 produces a signal without input
    private protected override bool IsSourceActive(long startFrame, int length) => _curve is { } curve && Shape(curve, 0) != 0;

    private protected override long TailFrames => Oversample == OverSampleType.None ? 0 : Resampler.Latency;

    private protected override void Process(long startFrame, int length)
    {
        var input = GetInputBus(0);
        var output = GetOutputBus(0);
        var curve = _curve;
        var channels = input.NumberOfChannels;
        if (curve is null)
        {
            output.CopyFrom(input);
            return;
        }

        output.SetNumberOfChannels(channels);
        var factor = Oversample switch
        {
            OverSampleType.TwoTimes => 2,
            OverSampleType.FourTimes => 4,
            _ => 1,
        };

        if (factor == 1)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var source = input.GetChannelSpan(channel);
                var destination = output.GetChannelSpan(channel);
                for (var i = 0; i < length; i++)
                {
                    destination[i] = Shape(curve, input.IsSilent ? 0 : source[i]);
                }
            }
        }
        else
        {
            if (_resamplers.Length < channels)
            {
                var resamplers = new Resampler[channels];
                for (var i = 0; i < channels; i++)
                {
                    resamplers[i] = i < _resamplers.Length ? _resamplers[i] : new Resampler();
                }

                _resamplers = resamplers;
            }

            if (_oversampled.Length < length * factor)
            {
                _oversampled = new float[length * factor];
            }

            var oversampled = _oversampled.AsSpan(0, length * factor);
            for (var channel = 0; channel < channels; channel++)
            {
                var resampler = _resamplers[channel];
                resampler.Upsample(input.IsSilent ? default : input.GetChannelSpan(channel), length, factor, oversampled);
                for (var i = 0; i < oversampled.Length; i++)
                {
                    oversampled[i] = Shape(curve, oversampled[i]);
                }

                resampler.Downsample(oversampled, factor, output.GetChannelSpan(channel));
            }
        }

        output.MarkNonSilent();
    }

    /// <summary>Maps a sample with the curve (https://webaudio.github.io/web-audio-api/#dom-waveshapernode-curve).</summary>
    private static float Shape(float[] curve, float x)
    {
        var n = curve.Length;
        var v = (n - 1) / 2.0 * (x + 1);
        if (v < 0)
            return curve[0];

        if (v >= n - 1)
            return curve[n - 1];

        if (double.IsNaN(v))
            return curve[0];

        var k = (int)v;
        var f = v - k;
        return (float)(((1 - f) * curve[k]) + (f * curve[k + 1]));
    }

    /// <summary>The up-sampling and down-sampling of a channel (windowed-sinc FIR filters at the oversampled rate).</summary>
    private sealed class Resampler
    {
        private const int KernelSize = 64;

        /// <summary>The latency of the up-sampling and down-sampling in frames of the context.</summary>
        public const int Latency = KernelSize / 2;

        private static readonly Dictionary<int, double[]> Kernels = [];

        // The circular histories of the input samples (at the rate of the context) and of the shaped samples (oversampled)
        private readonly double[] _upHistory = new double[KernelSize];
        private readonly double[] _downHistory = new double[KernelSize * 4];
        private int _upPosition;
        private int _downPosition;

        private static double[] GetKernel(int factor)
        {
            lock (Kernels)
            {
                if (!Kernels.TryGetValue(factor, out var kernel))
                {
                    // A lowpass filter with its cutoff at the Nyquist frequency of the context (a Blackman window)
                    var size = KernelSize * factor;
                    kernel = new double[size];
                    var cutoff = 0.5 / factor;
                    var center = (size - 1) / 2.0;
                    var sum = 0.0;
                    for (var i = 0; i < size; i++)
                    {
                        var t = i - center;
                        var sinc = t == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * t) / (Math.PI * t);
                        var window = 0.42 - (0.5 * Math.Cos(2 * Math.PI * i / (size - 1))) + (0.08 * Math.Cos(4 * Math.PI * i / (size - 1)));
                        kernel[i] = sinc * window;
                        sum += kernel[i];
                    }

                    for (var i = 0; i < size; i++)
                    {
                        kernel[i] /= sum;
                    }

                    Kernels[factor] = kernel;
                }

                return kernel;
            }
        }

        /// <summary>Up-samples a render quantum (polyphase: the zeros inserted between the samples are not multiplied).</summary>
        public void Upsample(ReadOnlySpan<float> input, int length, int factor, Span<float> output)
        {
            var kernel = GetKernel(factor);
            for (var n = 0; n < length; n++)
            {
                _upHistory[_upPosition] = input.IsEmpty ? 0 : input[n];
                for (var phase = 0; phase < factor; phase++)
                {
                    var sum = 0.0;
                    for (var j = 0; j < KernelSize; j++)
                    {
                        sum += kernel[phase + (j * factor)] * _upHistory[(_upPosition - j + KernelSize) % KernelSize];
                    }

                    output[(n * factor) + phase] = (float)(sum * factor);
                }

                _upPosition = (_upPosition + 1) % KernelSize;
            }
        }

        /// <summary>Down-samples the shaped samples (only the kept samples are filtered).</summary>
        public void Downsample(ReadOnlySpan<float> input, int factor, Span<float> output)
        {
            var kernel = GetKernel(factor);
            var size = kernel.Length;
            for (var i = 0; i < input.Length; i++)
            {
                _downHistory[_downPosition] = input[i];
                if (i % factor == 0)
                {
                    var sum = 0.0;
                    for (var k = 0; k < size; k++)
                    {
                        sum += kernel[k] * _downHistory[(_downPosition - k + size) % size];
                    }

                    output[i / factor] = (float)sum;
                }

                _downPosition = (_downPosition + 1) % size;
            }
        }
    }
}
