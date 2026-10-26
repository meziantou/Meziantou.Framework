using System.Numerics;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.WebAudio.Rendering;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The options of an <see cref="AnalyserNode"/> (https://webaudio.github.io/web-audio-api/#AnalyserOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AnalyserOptions"/>
[IdlInterface("AnalyserOptions", "https://webaudio.github.io/web-audio-api/#AnalyserOptions")]
public sealed class AnalyserOptions : AudioNodeOptions
{
    /// <summary>Gets the size of the FFT.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-analyseroptions-fftsize"/>
    [IdlMember("fftSize", "https://webaudio.github.io/web-audio-api/#dom-analyseroptions-fftsize")]
    public uint FftSize { get; init; } = 2048;

    /// <summary>Gets the maximum power in dB of the byte frequency data.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-analyseroptions-maxdecibels"/>
    [IdlMember("maxDecibels", "https://webaudio.github.io/web-audio-api/#dom-analyseroptions-maxdecibels")]
    public double MaxDecibels { get; init; } = -30;

    /// <summary>Gets the minimum power in dB of the byte frequency data.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-analyseroptions-mindecibels"/>
    [IdlMember("minDecibels", "https://webaudio.github.io/web-audio-api/#dom-analyseroptions-mindecibels")]
    public double MinDecibels { get; init; } = -100;

    /// <summary>Gets the smoothing over time of the frequency data, from 0 to 1.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-analyseroptions-smoothingtimeconstant"/>
    [IdlMember("smoothingTimeConstant", "https://webaudio.github.io/web-audio-api/#dom-analyseroptions-smoothingtimeconstant")]
    public double SmoothingTimeConstant { get; init; } = 0.8;
}

/// <summary>
/// A node that provides the time-domain and frequency-domain data of its input (https://webaudio.github.io/web-audio-api/#AnalyserNode).
/// Its output is its input.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AnalyserNode"/>
[IdlInterface("AnalyserNode", "https://webaudio.github.io/web-audio-api/#AnalyserNode")]
public sealed class AnalyserNode : AudioNode
{
    /// <summary>The number of frames kept for the analysis (the maximum FFT size).</summary>
    private const int HistorySize = 32768;

    private readonly float[] _history = new float[HistorySize];
    private long _writtenFrames;
    private uint _fftSize = 2048;
    private double _minDecibels = -100;
    private double _maxDecibels = -30;
    private double _smoothingTimeConstant = 0.8;
    private double[] _smoothed = new double[1024];
    private long _frequencyDataFrame = -1;

    /// <summary>Creates an analyser.</summary>
    /// <exception cref="DomException">An option is out of its range (an <c>IndexSizeError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-analysernode-analysernode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-analysernode-analysernode")]
    public AnalyserNode(BaseAudioContext context, AnalyserOptions? options = null)
        : base(context, numberOfInputs: 1, numberOfOutputs: 1, channelCount: 2, ChannelCountMode.Max, ChannelInterpretation.Speakers)
    {
        ApplyOptions(options);
        if (options is not null)
        {
            FftSize = options.FftSize;
            if (options.MinDecibels >= options.MaxDecibels)
                throw new DomException($"The minimum decibels ({options.MinDecibels}) must be less than the maximum decibels ({options.MaxDecibels})", "IndexSizeError");

            _minDecibels = options.MinDecibels;
            _maxDecibels = options.MaxDecibels;
            SmoothingTimeConstant = options.SmoothingTimeConstant;
        }
    }

    /// <summary>Gets or sets the size of the FFT (a power of two from 32 to 32768).</summary>
    /// <exception cref="DomException">The value is not a power of two from 32 to 32768 (an <c>IndexSizeError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-analysernode-fftsize"/>
    [IdlMember("fftSize", "https://webaudio.github.io/web-audio-api/#dom-analysernode-fftsize")]
    public uint FftSize
    {
        get => _fftSize;
        set
        {
            if (value is < 32 or > HistorySize || !BitOperations.IsPow2(value))
                throw new DomException($"The FFT size {value} is not a power of two from 32 to {HistorySize}", "IndexSizeError");

            if (value != _fftSize)
            {
                _fftSize = value;
                _smoothed = new double[value / 2];
                _frequencyDataFrame = -1;
            }
        }
    }

    /// <summary>Gets the number of frequency bins (half the FFT size).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-analysernode-frequencybincount"/>
    [IdlMember("frequencyBinCount", "https://webaudio.github.io/web-audio-api/#dom-analysernode-frequencybincount")]
    public uint FrequencyBinCount => _fftSize / 2;

    /// <summary>Gets or sets the minimum power in dB of the byte frequency data.</summary>
    /// <exception cref="DomException">The value is not less than <see cref="MaxDecibels"/> (an <c>IndexSizeError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-analysernode-mindecibels"/>
    [IdlMember("minDecibels", "https://webaudio.github.io/web-audio-api/#dom-analysernode-mindecibels")]
    public double MinDecibels
    {
        get => _minDecibels;
        set
        {
            if (value >= _maxDecibels)
                throw new DomException($"The minimum decibels ({value}) must be less than the maximum decibels ({_maxDecibels})", "IndexSizeError");

            _minDecibels = value;
        }
    }

    /// <summary>Gets or sets the maximum power in dB of the byte frequency data.</summary>
    /// <exception cref="DomException">The value is not greater than <see cref="MinDecibels"/> (an <c>IndexSizeError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-analysernode-maxdecibels"/>
    [IdlMember("maxDecibels", "https://webaudio.github.io/web-audio-api/#dom-analysernode-maxdecibels")]
    public double MaxDecibels
    {
        get => _maxDecibels;
        set
        {
            if (value <= _minDecibels)
                throw new DomException($"The maximum decibels ({value}) must be greater than the minimum decibels ({_minDecibels})", "IndexSizeError");

            _maxDecibels = value;
        }
    }

    /// <summary>Gets or sets the smoothing over time of the frequency data, from 0 to 1.</summary>
    /// <exception cref="DomException">The value is not between 0 and 1 (an <c>IndexSizeError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-analysernode-smoothingtimeconstant"/>
    [IdlMember("smoothingTimeConstant", "https://webaudio.github.io/web-audio-api/#dom-analysernode-smoothingtimeconstant")]
    public double SmoothingTimeConstant
    {
        get => _smoothingTimeConstant;
        set
        {
            if (!(value >= 0 && value <= 1))
                throw new DomException($"The smoothing time constant {value} is not between 0 and 1", "IndexSizeError");

            _smoothingTimeConstant = value;
        }
    }

    /// <summary>Copies the current frequency data in dB to an array (a <c>Float32Array</c>).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-analysernode-getfloatfrequencydata"/>
    [IdlMember("getFloatFrequencyData", "https://webaudio.github.io/web-audio-api/#dom-analysernode-getfloatfrequencydata")]
    public void GetFloatFrequencyData(Span<byte> array)
    {
        var destination = unsafe(MemoryMarshal.Cast<byte, float>(array));
        var smoothed = ComputeFrequencyData();
        var count = Math.Min(destination.Length, smoothed.Length);
        for (var k = 0; k < count; k++)
        {
            destination[k] = (float)(20 * Math.Log10(smoothed[k]));
        }
    }

    /// <summary>Copies the current frequency data, scaled from <see cref="MinDecibels"/> to <see cref="MaxDecibels"/>, to an array of bytes (a <c>Uint8Array</c>).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-analysernode-getbytefrequencydata"/>
    [IdlMember("getByteFrequencyData", "https://webaudio.github.io/web-audio-api/#dom-analysernode-getbytefrequencydata")]
    public void GetByteFrequencyData(Span<byte> array)
    {
        var smoothed = ComputeFrequencyData();
        var count = Math.Min(array.Length, smoothed.Length);
        var range = _maxDecibels - _minDecibels;
        for (var k = 0; k < count; k++)
        {
            var db = 20 * Math.Log10(smoothed[k]);
            var value = Math.Floor(255 / range * (db - _minDecibels));
            array[k] = (byte)Math.Clamp(double.IsNaN(value) ? 0 : value, 0, 255);
        }
    }

    /// <summary>Copies the current time-domain data (down-mixed to mono) to an array (a <c>Float32Array</c>).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-analysernode-getfloattimedomaindata"/>
    [IdlMember("getFloatTimeDomainData", "https://webaudio.github.io/web-audio-api/#dom-analysernode-getfloattimedomaindata")]
    public void GetFloatTimeDomainData(Span<byte> array)
    {
        var destination = unsafe(MemoryMarshal.Cast<byte, float>(array));
        var count = (int)Math.Min(destination.Length, _fftSize);
        CopyTimeDomainData(destination[..count]);
    }

    /// <summary>Copies the current time-domain data (down-mixed to mono), scaled from -1 to 1, to an array of bytes (a <c>Uint8Array</c>).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-analysernode-getbytetimedomaindata"/>
    [IdlMember("getByteTimeDomainData", "https://webaudio.github.io/web-audio-api/#dom-analysernode-getbytetimedomaindata")]
    public void GetByteTimeDomainData(Span<byte> array)
    {
        var count = (int)Math.Min(array.Length, _fftSize);
        var samples = new float[count];
        CopyTimeDomainData(samples);
        for (var k = 0; k < count; k++)
        {
            array[k] = (byte)Math.Clamp(Math.Floor(128 * (1 + (double)samples[k])), 0, 255);
        }
    }

    /// <summary>
    /// Copies the most recent frames to a span: the frames that end the current time-domain data of the size of the FFT
    /// (the span is its first frames when it is shorter than the FFT).
    /// </summary>
    private void CopyTimeDomainData(Span<float> destination)
    {
        WriteSilenceUntil(Context.CurrentFrame);
        var first = _writtenFrames - _fftSize;
        for (var i = 0; i < destination.Length; i++)
        {
            var frame = first + i;
            destination[i] = frame < 0 ? 0 : _history[frame % HistorySize];
        }
    }

    /// <summary>The smoothed magnitudes of the current frequency data (computed once per render quantum).</summary>
    private double[] ComputeFrequencyData()
    {
        var currentFrame = Context.CurrentFrame;
        if (_frequencyDataFrame == currentFrame)
            return _smoothed;

        _frequencyDataFrame = currentFrame;
        var n = (int)_fftSize;
        var real = new double[n];
        var imaginary = new double[n];
        var samples = new float[n];
        CopyTimeDomainData(samples);

        // https://webaudio.github.io/web-audio-api/#blackman-window
        const double Alpha = 0.16;
        const double A0 = (1 - Alpha) / 2;
        const double A1 = 0.5;
        const double A2 = Alpha / 2;
        for (var i = 0; i < n; i++)
        {
            var window = A0 - (A1 * Math.Cos(2 * Math.PI * i / n)) + (A2 * Math.Cos(4 * Math.PI * i / n));
            real[i] = samples[i] * window;
        }

        Fft.Get(n).Transform(real, imaginary, inverse: false);
        var tau = _smoothingTimeConstant;
        for (var k = 0; k < n / 2; k++)
        {
            var magnitude = Math.Sqrt((real[k] * real[k]) + (imaginary[k] * imaginary[k])) / n;
            var value = (tau * _smoothed[k]) + ((1 - tau) * magnitude);
            _smoothed[k] = double.IsFinite(value) ? value : 0;
        }

        return _smoothed;
    }

    /// <summary>The frames during which the node was not processed are silent.</summary>
    private void WriteSilenceUntil(long frame)
    {
        var count = Math.Min(frame - _writtenFrames, HistorySize);
        for (var i = frame - count; i < frame; i++)
        {
            _history[i % HistorySize] = 0;
        }

        _writtenFrames = Math.Max(_writtenFrames, frame);
    }

    private protected override void Process(long startFrame, int length)
    {
        var input = GetInputBus(0);
        GetOutputBus(0).CopyFrom(input);
        WriteSilenceUntil(startFrame);

        // The time-domain data are down-mixed to mono with the speakers interpretation
        var channels = input.NumberOfChannels;
        for (var i = 0; i < length; i++)
        {
            double sample = 0;
            if (!input.IsSilent)
            {
                sample = channels switch
                {
                    1 => input.GetChannel(0)[i],
                    2 => 0.5 * (input.GetChannel(0)[i] + input.GetChannel(1)[i]),
                    4 => 0.25 * (input.GetChannel(0)[i] + input.GetChannel(1)[i] + input.GetChannel(2)[i] + input.GetChannel(3)[i]),
                    6 => (Math.Sqrt(0.5) * (input.GetChannel(0)[i] + input.GetChannel(1)[i])) + input.GetChannel(2)[i] + (0.5 * (input.GetChannel(4)[i] + input.GetChannel(5)[i])),
                    _ => input.GetChannel(0)[i],
                };
            }

            _history[(startFrame + i) % HistorySize] = (float)sample;
        }

        _writtenFrames = startFrame + length;
    }
}
