using System.Numerics;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.WebAudio.Rendering;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The options of a <see cref="ConvolverNode"/> (https://webaudio.github.io/web-audio-api/#ConvolverOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#ConvolverOptions"/>
[IdlInterface("ConvolverOptions", "https://webaudio.github.io/web-audio-api/#ConvolverOptions")]
public sealed class ConvolverOptions : AudioNodeOptions
{
    /// <summary>Gets the impulse response.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-convolveroptions-buffer"/>
    [IdlMember("buffer", "https://webaudio.github.io/web-audio-api/#dom-convolveroptions-buffer")]
    public AudioBuffer? Buffer { get; init; }

    /// <summary>Gets a value indicating whether the impulse response is not normalized.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-convolveroptions-disablenormalization"/>
    [IdlMember("disableNormalization", "https://webaudio.github.io/web-audio-api/#dom-convolveroptions-disablenormalization")]
    public bool DisableNormalization { get; init; }
}

/// <summary>A node that convolves its input with an impulse response (https://webaudio.github.io/web-audio-api/#ConvolverNode).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#ConvolverNode"/>
[IdlInterface("ConvolverNode", "https://webaudio.github.io/web-audio-api/#ConvolverNode")]
public sealed class ConvolverNode : AudioNode
{
    /// <summary>The length of the impulse responses convolved directly (in the time domain).</summary>
    private const int MaxDirectLength = 64;

    private AudioBuffer? _buffer;
    private Convolver[] _convolvers = [];
    private int _responseChannels;
    private long _responseLength;
    private long _stereoUntilFrame = long.MinValue;
    private float[] _discardedOutput = [];

    /// <summary>Creates a convolver.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-convolvernode-convolvernode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-convolvernode-convolvernode")]
    public ConvolverNode(BaseAudioContext context, ConvolverOptions? options = null)
        : base(context, numberOfInputs: 1, numberOfOutputs: 1, channelCount: 2, ChannelCountMode.ClampedMax, ChannelInterpretation.Speakers)
    {
        Normalize = !(options?.DisableNormalization ?? false);
        if (options?.Buffer is { } buffer)
        {
            Buffer = buffer;
        }

        ApplyOptions(options);
    }

    /// <summary>Gets or sets the impulse response (its content is acquired when it is set).</summary>
    /// <exception cref="DomException">The buffer does not have 1, 2 or 4 channels, or its sample rate is not the one of the context (a <c>NotSupportedError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-convolvernode-buffer"/>
    [IdlMember("buffer", "https://webaudio.github.io/web-audio-api/#dom-convolvernode-buffer")]
    public AudioBuffer? Buffer
    {
        get => _buffer;
        set
        {
            if (value is null)
            {
                _buffer = null;
                _convolvers = [];
                _responseChannels = 0;
                _responseLength = 0;
                return;
            }

            if (value.NumberOfChannels is not (1 or 2 or 4))
                throw new DomException($"The impulse response must have 1, 2 or 4 channels, not {value.NumberOfChannels}", "NotSupportedError");

            if (value.SampleRate != Context.SampleRate)
                throw new DomException($"The sample rate of the impulse response ({value.SampleRate}) is not the sample rate of the context ({Context.SampleRate})", "NotSupportedError");

            var content = value.AcquireContent();
            var scale = Normalize ? CalculateNormalizationScale(content, value.SampleRate) : 1;
            var quantum = Context.RenderQuantumSizeInternal;
            var convolvers = new Convolver[content.Length];
            for (var i = 0; i < content.Length; i++)
            {
                convolvers[i] = new Convolver(content[i], scale, quantum);
            }

            _buffer = value;
            _convolvers = convolvers;
            _responseChannels = content.Length;
            _responseLength = value.Length;
        }
    }

    /// <summary>Gets or sets a value indicating whether the impulse response is normalized when <see cref="Buffer"/> is set.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-convolvernode-normalize"/>
    [IdlMember("normalize", "https://webaudio.github.io/web-audio-api/#dom-convolvernode-normalize")]
    public bool Normalize { get; set; }

    private protected override long TailFrames => _responseLength;

    private protected override void ValidateChannelCount(uint value)
    {
        if (value > 2)
            throw new DomException($"The channel count of a convolver cannot be greater than 2 ({value})", "NotSupportedError");

        base.ValidateChannelCount(value);
    }

    private protected override void ValidateChannelCountMode(ChannelCountMode value)
    {
        if (value == ChannelCountMode.Max)
            throw new DomException("The channel count mode of a convolver cannot be 'max'", "NotSupportedError");
    }

    private protected override void OnBecameInactive()
    {
        foreach (var convolver in _convolvers)
        {
            convolver.Reset();
        }
    }

    /// <summary>https://webaudio.github.io/web-audio-api/#dom-convolvernode-normalize: the scaled RMS-power normalization.</summary>
    private static double CalculateNormalizationScale(float[][] channels, float sampleRate)
    {
        const double GainCalibration = 0.00125;
        const double GainCalibrationSampleRate = 44100;
        const double MinPower = 0.000125;
        var power = 0.0;
        foreach (var channel in channels)
        {
            var channelPower = 0.0;
            foreach (var sample in channel)
            {
                channelPower += (double)sample * sample;
            }

            power += channelPower;
        }

        power = Math.Sqrt(power / (channels.Length * (double)channels[0].Length));
        if (!double.IsFinite(power) || power < MinPower)
        {
            power = MinPower;
        }

        var scale = 1 / power * GainCalibration * GainCalibrationSampleRate / sampleRate;
        if (channels.Length == 4)
        {
            scale *= 0.5;
        }

        return scale;
    }

    private protected override void Process(long startFrame, int length)
    {
        var input = GetInputBus(0);
        var output = GetOutputBus(0);
        if (_convolvers.Length == 0)
        {
            output.Zero(1);
            return;
        }

        if (_discardedOutput.Length < length)
        {
            _discardedOutput = new float[length];
        }

        var inputChannels = Math.Min(input.NumberOfChannels, 2);
        var stereoInput = inputChannels == 2;
        var left = input.IsSilent ? default : input.GetChannelSpan(0);
        var right = input.IsSilent ? default : (stereoInput ? input.GetChannelSpan(1) : left);

        // https://webaudio.github.io/web-audio-api/#Convolution-channel-configurations
        var outLeft = default(Span<float>);
        var outRight = default(Span<float>);
        if (_responseChannels == 1)
        {
            // A mono response: each channel of the input is convolved with it. The convolution is linear when the input
            // changes from mono to stereo and back: the right channel is fed with the up-mixed mono input, and the output
            // stays stereo while the tail of a stereo input rings
            EnsureConvolvers(2);
            if (stereoInput)
            {
                _stereoUntilFrame = startFrame + length + _responseLength;
            }

            var stereoOutput = startFrame < _stereoUntilFrame;
            var rightInput = stereoInput || ChannelInterpretation == ChannelInterpretation.Speakers ? right : default;
            output.SetNumberOfChannels(stereoOutput ? 2 : 1);
            outLeft = output.GetChannelSpan(0);
            _convolvers[0].Process(left, outLeft, accumulate: false);
            if (stereoOutput)
            {
                _convolvers[1].Process(rightInput, output.GetChannelSpan(1), accumulate: false);
            }
            else
            {
                _convolvers[1].Process(rightInput, _discardedOutput.AsSpan(0, length), accumulate: false);
            }

            output.MarkNonSilent();
            return;
        }

        output.SetNumberOfChannels(2);
        outLeft = output.GetChannelSpan(0);
        outRight = output.GetChannelSpan(1);
        if (_responseChannels == 2)
        {
            _convolvers[0].Process(left, outLeft, accumulate: false);
            _convolvers[1].Process(right, outRight, accumulate: false);
        }
        else
        {
            // True stereo: the channels of the response are L->L, L->R, R->L and R->R
            _convolvers[0].Process(left, outLeft, accumulate: false);
            _convolvers[2].Process(right, outLeft, accumulate: true);
            _convolvers[1].Process(left, outRight, accumulate: false);
            _convolvers[3].Process(right, outRight, accumulate: true);
        }

        output.MarkNonSilent();
    }

    /// <summary>A mono response with a stereo input needs one convolver (one input history) per input channel.</summary>
    private void EnsureConvolvers(int count)
    {
        if (_convolvers.Length < count)
        {
            var convolvers = new Convolver[count];
            for (var i = 0; i < count; i++)
            {
                convolvers[i] = i < _convolvers.Length ? _convolvers[i] : _convolvers[0].CloneResponse();
            }

            _convolvers = convolvers;
        }
    }

    /// <summary>
    /// The convolution of a channel with a response: in the time domain for a short response, else with a uniformly
    /// partitioned overlap-save convolution (the partitions have the size of a render quantum).
    /// </summary>
    private sealed class Convolver
    {
        private readonly float[] _response;
        private readonly double _scale;
        private readonly int _blockSize;

        // Direct convolution: the last input samples
        private readonly float[]? _history;
        private int _historyPosition;

        // Partitioned convolution: the spectra of the partitions of the response, and the spectra of the last input windows
        private readonly int _fftSize;
        private readonly double[][]? _partitionReal;
        private readonly double[][]? _partitionImaginary;
        private readonly double[][]? _inputReal;
        private readonly double[][]? _inputImaginary;
        private readonly double[]? _window;
        private readonly double[]? _real;
        private readonly double[]? _imaginary;
        private readonly double[]? _blockMaxima;
        private readonly double _responseSum;
        private int _inputPosition;
        private long _blockCount;

        public Convolver(float[] response, double scale, int blockSize)
        {
            _response = response;
            _scale = scale;
            _blockSize = blockSize;
            if (response.Length <= MaxDirectLength)
            {
                _history = new float[response.Length];
                return;
            }

            _fftSize = (int)BitOperations.RoundUpToPowerOf2((uint)(2 * blockSize));
            var partitions = (response.Length + blockSize - 1) / blockSize;
            var fft = Fft.Get(_fftSize);
            _partitionReal = new double[partitions][];
            _partitionImaginary = new double[partitions][];
            _inputReal = new double[partitions][];
            _inputImaginary = new double[partitions][];
            for (var p = 0; p < partitions; p++)
            {
                var real = new double[_fftSize];
                var imaginary = new double[_fftSize];
                var count = Math.Min(blockSize, response.Length - (p * blockSize));
                for (var i = 0; i < count; i++)
                {
                    real[i] = response[(p * blockSize) + i] * scale;
                }

                fft.Transform(real, imaginary, inverse: false);
                _partitionReal[p] = real;
                _partitionImaginary[p] = imaginary;
                _inputReal[p] = new double[_fftSize];
                _inputImaginary[p] = new double[_fftSize];
            }

            _window = new double[_fftSize];
            _real = new double[_fftSize];
            _imaginary = new double[_fftSize];
            _blockMaxima = new double[partitions + 1];
            foreach (var sample in response)
            {
                _responseSum += Math.Abs(sample * scale);
            }
        }

        public Convolver CloneResponse() => new(_response, _scale, _blockSize);

        public void Reset()
        {
            if (_history is not null)
            {
                Array.Clear(_history);
                return;
            }

            Array.Clear(_window!);
            Array.Clear(_blockMaxima!);
            foreach (var spectrum in _inputReal!)
            {
                Array.Clear(spectrum);
            }

            foreach (var spectrum in _inputImaginary!)
            {
                Array.Clear(spectrum);
            }
        }

        public void Process(ReadOnlySpan<float> input, Span<float> output, bool accumulate)
        {
            if (_history is not null)
            {
                ProcessDirect(input, output, accumulate);
            }
            else
            {
                ProcessPartitioned(input, output, accumulate);
            }
        }

        private void ProcessDirect(ReadOnlySpan<float> input, Span<float> output, bool accumulate)
        {
            var history = _history!;
            var length = history.Length;
            for (var i = 0; i < output.Length; i++)
            {
                history[_historyPosition] = input.IsEmpty ? 0 : input[i];
                var sum = 0.0;
                for (var k = 0; k < length; k++)
                {
                    sum += _response[k] * (double)history[(_historyPosition - k + length) % length];
                }

                _historyPosition = (_historyPosition + 1) % length;
                var value = (float)(sum * _scale);
                output[i] = accumulate ? output[i] + value : value;
            }
        }

        private void ProcessPartitioned(ReadOnlySpan<float> input, Span<float> output, bool accumulate)
        {
            var window = _window!;
            var blockSize = _blockSize;
            var fftSize = _fftSize;

            // The window holds the last fftSize input samples, the current block at its end
            Array.Copy(window, blockSize, window, 0, fftSize - blockSize);
            var blockMaximum = 0.0;
            for (var i = 0; i < blockSize; i++)
            {
                var sample = input.IsEmpty ? 0 : input[i];
                window[fftSize - blockSize + i] = sample;
                blockMaximum = Math.Max(blockMaximum, Math.Abs(sample));
            }

            var partitions = _partitionReal!.Length;
            _inputPosition = (_inputPosition + 1) % partitions;
            // The maxima of the input blocks that contribute to the output (the partitions and the block before them)
            var maxima = _blockMaxima!;
            maxima[_blockCount % maxima.Length] = blockMaximum;
            _blockCount++;
            var inputReal = _inputReal![_inputPosition];
            var inputImaginary = _inputImaginary![_inputPosition];
            window.CopyTo(inputReal, 0);
            Array.Clear(inputImaginary);
            var fft = Fft.Get(fftSize);
            fft.Transform(inputReal, inputImaginary, inverse: false);

            // Y = sum X[n - p] H[p]
            var real = _real!;
            var imaginary = _imaginary!;
            Array.Clear(real);
            Array.Clear(imaginary);
            for (var p = 0; p < partitions; p++)
            {
                var index = (_inputPosition - p + partitions) % partitions;
                var xr = _inputReal[index];
                var xi = _inputImaginary[index];
                var hr = _partitionReal[p];
                var hi = _partitionImaginary![p];
                for (var k = 0; k < fftSize; k++)
                {
                    real[k] += (xr[k] * hr[k]) - (xi[k] * hi[k]);
                    imaginary[k] += (xr[k] * hi[k]) + (xi[k] * hr[k]);
                }
            }

            fft.Transform(real, imaginary, inverse: true);

            // The rounding errors of the transforms are far below the bound of the output (the maximum of the inputs that
            // contribute to the block times the sum of the response): such values are 0
            var inputMaximum = 0.0;
            foreach (var maximum in maxima)
            {
                inputMaximum = Math.Max(inputMaximum, maximum);
            }

            var threshold = 1e-10 * inputMaximum * _responseSum;

            // Overlap-save: the last block of the circular convolution is the linear convolution of the current block
            for (var i = 0; i < blockSize; i++)
            {
                var sum = real[fftSize - blockSize + i] / fftSize;
                var value = Math.Abs(sum) <= threshold ? 0f : (float)sum;
                output[i] = accumulate ? output[i] + value : value;
            }
        }
    }
}
