using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Meziantou.Framework.WebPlatform.WebAudio;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>The Web Audio API (https://webaudio.github.io/web-audio-api/).</summary>
public sealed class WebAudioTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/page.html", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors.Select(error => error.Message));
        return document;
    }

    private static Span<byte> Bytes(float[] values) => unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));

    private static AudioBuffer Render(OfflineAudioContext context)
    {
        var task = context.StartRendering();
        Assert.True(task.IsCompletedSuccessfully);
        return task.Result;
    }

    [Fact]
    public void OfflineAudioContext_WithoutWindow_RendersSynchronously()
    {
        var context = new OfflineAudioContext(1, 256, 8000);
        var source = new ConstantSourceNode(context, new ConstantSourceOptions { Offset = 0.25f });
        var gain = new GainNode(context, new GainOptions { Gain = 2 });
        source.Connect(gain).Connect(context.Destination);
        source.Start(64 / 8000.0);
        source.Stop(192 / 8000.0);

        var buffer = Render(context);

        Assert.Equal(256u, buffer.Length);
        Assert.Equal(AudioContextState.Closed, context.State);
        Assert.Equal(256 / 8000.0, context.CurrentTime);
        var samples = buffer.GetChannelData(0);
        Assert.True(samples[..64].IndexOfAnyExcept(0f) < 0);
        Assert.True(samples[64..192].IndexOfAnyExcept(0.5f) < 0);
        Assert.True(samples[192..].IndexOfAnyExcept(0f) < 0);
    }

    [Fact]
    public void AudioParam_Automation_ComputesTheValuesOfTheEvents()
    {
        var context = new OfflineAudioContext(1, 16000, 4000);
        var source = new ConstantSourceNode(context);
        source.Offset.SetValueAtTime(1, 0);
        source.Offset.LinearRampToValueAtTime(2, 1);
        source.Offset.ExponentialRampToValueAtTime(8, 2);
        source.Offset.SetTargetAtTime(0, 2, 0.5f);
        source.Offset.SetValueCurveAtTime([1, 3], 3, 1);
        source.Connect(context.Destination);
        source.Start();

        var samples = Render(context).GetChannelData(0);

        Assert.Equal(1f, samples[0]);
        Assert.Equal(1.5f, samples[2000]);
        Assert.Equal(2f, samples[4000]);
        Assert.Equal((float)(2 * Math.Pow(4, 0.5)), samples[6000]);
        Assert.Equal(8f, samples[8000]);
        Assert.Equal((float)(8 * Math.Exp(-1)), samples[10000]);
        Assert.Equal(1f, samples[12000]);
        Assert.Equal(2f, samples[14000]);
    }

    [Fact]
    public void AudioParam_CancelAndHoldAtTime_HoldsTheValueOfTheRamp()
    {
        var context = new OfflineAudioContext(1, 8000, 4000);
        var source = new ConstantSourceNode(context);
        source.Offset.SetValueAtTime(0, 0);
        source.Offset.LinearRampToValueAtTime(1, 1);
        source.Offset.CancelAndHoldAtTime(0.5);
        source.Connect(context.Destination);
        source.Start();

        var samples = Render(context).GetChannelData(0);

        Assert.Equal(0.25f, samples[1000]);
        Assert.Equal(0.5f, samples[2000]);
        Assert.Equal(0.5f, samples[6000]);
    }

    [Fact]
    public void AudioParam_Exceptions()
    {
        var context = new OfflineAudioContext(1, 128, 8000);
        var gain = new GainNode(context);

        Assert.Throws<RangeErrorException>(() => gain.Gain.SetValueAtTime(1, -1));
        Assert.Throws<RangeErrorException>(() => gain.Gain.ExponentialRampToValueAtTime(0, 1));
        Assert.Throws<RangeErrorException>(() => gain.Gain.SetTargetAtTime(1, 0, -1));
        gain.Gain.SetValueCurveAtTime([0, 1], 1, 1);
        Assert.Equal("NotSupportedError", Assert.Throws<DomException>(() => gain.Gain.SetValueAtTime(1, 1.5)).Name);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => gain.Gain.SetValueCurveAtTime([0], 3, 1)).Name);

        var source = new AudioBufferSourceNode(context);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => source.PlaybackRate.AutomationRate = AutomationRate.ARate).Name);
    }

    [Fact]
    public void ChannelMixing_UpMixesMonoToStereo()
    {
        var context = new OfflineAudioContext(2, 128, 8000);
        var source = new ConstantSourceNode(context);
        source.Connect(context.Destination);
        source.Start();

        var buffer = Render(context);

        Assert.Equal(1f, buffer.GetChannelData(0)[0]);
        Assert.Equal(1f, buffer.GetChannelData(1)[0]);
    }

    [Fact]
    public void AudioBufferSourceNode_PlaysTheBufferWithSubSampleStart()
    {
        var context = new OfflineAudioContext(1, 128, 8000);
        var buffer = new AudioBuffer(new AudioBufferOptions { Length = 8, SampleRate = 8000 });
        var samples = buffer.GetChannelData(0);
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = i + 1;
        }

        var source = new AudioBufferSourceNode(context, new AudioBufferSourceOptions { Buffer = buffer });
        source.Connect(context.Destination);
        source.Start(10.5 / 8000);

        var output = Render(context).GetChannelData(0);

        Assert.Equal(0f, output[10]);
        Assert.Equal(1.5f, output[11]);
        Assert.Equal(7.5f, output[17]);

        // After the last frame, the signal is extrapolated from the last two frames
        Assert.Equal(8.5f, output[18]);
        Assert.Equal(0f, output[19]);
    }

    [Fact]
    public void Graph_CycleWithoutDelayIsMuted()
    {
        var context = new OfflineAudioContext(1, 128, 8000);
        var source = new ConstantSourceNode(context);
        var gain1 = new GainNode(context);
        var gain2 = new GainNode(context);
        source.Connect(gain1).Connect(gain2).Connect(gain1);
        gain2.Connect(context.Destination);
        source.Start();

        var output = Render(context).GetChannelData(0);

        Assert.True(output.IndexOfAnyExcept(0f) < 0);
    }

    [Fact]
    public void AudioNode_ConnectionErrors()
    {
        var context1 = new OfflineAudioContext(1, 128, 8000);
        var context2 = new OfflineAudioContext(1, 128, 8000);
        var gain = new GainNode(context1);

        Assert.Equal("InvalidAccessError", Assert.Throws<DomException>(() => gain.Connect(context2.Destination)).Name);
        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => gain.Connect(context1.Destination, 1)).Name);
        Assert.Equal("InvalidAccessError", Assert.Throws<DomException>(() => gain.Disconnect(context1.Destination)).Name);
        Assert.Equal("NotSupportedError", Assert.Throws<DomException>(() => gain.ChannelCount = 0).Name);
        Assert.Equal("NotSupportedError", Assert.Throws<DomException>(() => context1.Destination.ChannelCount = 2).Name);
    }

    [Fact]
    public void OscillatorNode_SineMatchesTheMathematicalSine()
    {
        var context = new OfflineAudioContext(1, 256, 48000);
        var oscillator = new OscillatorNode(context, new OscillatorOptions { Frequency = 1000 });
        oscillator.Connect(context.Destination);
        oscillator.Start();

        var samples = Render(context).GetChannelData(0);

        for (var i = 0; i < samples.Length; i++)
        {
            Assert.True(Math.Abs(samples[i] - Math.Sin(2 * Math.PI * 1000 * i / 48000)) < 1e-6, $"Sample {i}");
        }
    }

    [Fact]
    public void OscillatorNode_SquareIsBandLimitedAndNormalized()
    {
        var context = new OfflineAudioContext(1, 4800, 48000);
        var oscillator = new OscillatorNode(context, new OscillatorOptions { Type = OscillatorType.Square, Frequency = 100 });
        oscillator.Connect(context.Destination);
        oscillator.Start();

        var samples = Render(context).GetChannelData(0);

        var max = 0f;
        foreach (var sample in samples)
        {
            max = Math.Max(max, Math.Abs(sample));
        }

        // The normalization of the full waveform applies to the band-limited one (its Gibbs oscillations are smaller)
        Assert.True(max is > 0.95f and <= 1.0001f, $"Maximum {max}");
        // The flat parts are below the overshoot of the Gibbs phenomenon, which is normalized to 1
        Assert.True(samples[120] is > 0.8f and < 0.9f, $"Sample {samples[120]}");
        Assert.True(samples[360] is < -0.8f and > -0.9f, $"Sample {samples[360]}");
    }

    [Fact]
    public void DelayNode_DelaysTheInput()
    {
        // The delay time is a float: 200 frames at 8192 Hz is exact
        var context = new OfflineAudioContext(1, 512, 8192);
        var buffer = new AudioBuffer(new AudioBufferOptions { Length = 1, SampleRate = 8192 });
        buffer.GetChannelData(0)[0] = 1;
        var source = new AudioBufferSourceNode(context, new AudioBufferSourceOptions { Buffer = buffer });
        var delay = new DelayNode(context, new DelayOptions { DelayTime = 200 / 8192.0 });
        source.Connect(delay).Connect(context.Destination);
        source.Start();

        var samples = Render(context).GetChannelData(0);

        Assert.Equal(200, samples.IndexOfAnyExcept(0f));
        Assert.Equal(1f, samples[200]);
        Assert.Equal(-1, samples[201..].IndexOfAnyExcept(0f));
    }

    [Fact]
    public void DelayNode_InACycleDelaysByARenderQuantum()
    {
        var context = new OfflineAudioContext(1, 512, 8000);
        var buffer = new AudioBuffer(new AudioBufferOptions { Length = 1, SampleRate = 8000 });
        buffer.GetChannelData(0)[0] = 1;
        var source = new AudioBufferSourceNode(context, new AudioBufferSourceOptions { Buffer = buffer });
        var gain = new GainNode(context, new GainOptions { Gain = 0.5f });
        var delay = new DelayNode(context, new DelayOptions { DelayTime = 0 });
        source.Connect(gain);
        gain.Connect(delay).Connect(gain);
        gain.Connect(context.Destination);
        source.Start();

        var samples = Render(context).GetChannelData(0);

        Assert.Equal(0.5f, samples[0]);
        Assert.Equal(0.25f, samples[128]);
        Assert.Equal(0.125f, samples[256]);
    }

    [Fact]
    public void ChannelSplitterAndMerger_SwapTheChannels()
    {
        var context = new OfflineAudioContext(2, 128, 8000);
        var left = new ConstantSourceNode(context, new ConstantSourceOptions { Offset = 1 });
        var right = new ConstantSourceNode(context, new ConstantSourceOptions { Offset = 2 });
        var merger1 = new ChannelMergerNode(context, new ChannelMergerOptions { NumberOfInputs = 2 });
        var splitter = new ChannelSplitterNode(context, new ChannelSplitterOptions { NumberOfOutputs = 2 });
        var merger2 = new ChannelMergerNode(context, new ChannelMergerOptions { NumberOfInputs = 2 });
        left.Connect(merger1, 0, 0);
        right.Connect(merger1, 0, 1);
        merger1.Connect(splitter);
        splitter.Connect(merger2, 0, 1);
        splitter.Connect(merger2, 1, 0);
        merger2.Connect(context.Destination);
        left.Start();
        right.Start();

        var buffer = Render(context);

        Assert.Equal(2f, buffer.GetChannelData(0)[0]);
        Assert.Equal(1f, buffer.GetChannelData(1)[0]);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => merger1.ChannelCount = 2).Name);
        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => new ChannelSplitterNode(context, new ChannelSplitterOptions { NumberOfOutputs = 33 })).Name);
    }

    [Fact]
    public void BiquadFilterNode_FiltersAndComputesTheFrequencyResponse()
    {
        var context = new OfflineAudioContext(1, 4096, 8000);
        var filter = new BiquadFilterNode(context, new BiquadFilterOptions { Type = BiquadFilterType.Lowpass, Frequency = 500 });
        float[] frequencies = [0, 500, 3999, 5000];
        var magnitudes = new float[4];
        var phases = new float[4];
        filter.GetFrequencyResponse(Bytes(frequencies), Bytes(magnitudes), Bytes(phases));

        Assert.True(Math.Abs(magnitudes[0] - 1) < 1e-6);
        Assert.True(magnitudes[2] < 1e-3);
        Assert.True(float.IsNaN(magnitudes[3]));

        // A constant input converges to the DC gain of the filter
        var source = new ConstantSourceNode(context);
        source.Connect(filter).Connect(context.Destination);
        source.Start();
        var samples = Render(context).GetChannelData(0);
        Assert.True(Math.Abs(samples[^1] - 1) < 1e-4);
    }

    [Fact]
    public void IIRFilterNode_MatchesTheEquivalentBiquad()
    {
        var context = new OfflineAudioContext(2, 1024, 8000);
        var coefficients = BiquadFilterNode.ComputeCoefficients(BiquadFilterType.Highpass, 1000, 0, 1, 0, 8000);
        var iir = new IIRFilterNode(context, new IIRFilterOptions { Feedforward = [coefficients.B0, coefficients.B1, coefficients.B2], Feedback = [1, coefficients.A1, coefficients.A2] });
        var biquad = new BiquadFilterNode(context, new BiquadFilterOptions { Type = BiquadFilterType.Highpass, Frequency = 1000 });
        var merger = new ChannelMergerNode(context, new ChannelMergerOptions { NumberOfInputs = 2 });
        var oscillator = new OscillatorNode(context, new OscillatorOptions { Type = OscillatorType.Sawtooth, Frequency = 300 });
        oscillator.Connect(iir).Connect(merger, 0, 0);
        oscillator.Connect(biquad).Connect(merger, 0, 1);
        merger.Connect(context.Destination);
        oscillator.Start();

        var buffer = Render(context);

        var iirSamples = buffer.GetChannelData(0);
        var biquadSamples = buffer.GetChannelData(1);
        for (var i = 0; i < iirSamples.Length; i++)
        {
            Assert.True(Math.Abs(iirSamples[i] - biquadSamples[i]) < 1e-5, $"Sample {i}");
        }

        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => new IIRFilterNode(context, new IIRFilterOptions { Feedforward = [0], Feedback = [1] })).Name);
        Assert.Equal("NotSupportedError", Assert.Throws<DomException>(() => new IIRFilterNode(context, new IIRFilterOptions { Feedforward = [1], Feedback = [] })).Name);
    }

    [Fact]
    public void ConvolverNode_WithAnImpulseOutputsItsInput()
    {
        var context = new OfflineAudioContext(1, 1024, 8000);
        var impulse = new AudioBuffer(new AudioBufferOptions { Length = 300, SampleRate = 8000 });
        impulse.GetChannelData(0)[100] = 1;
        var convolver = new ConvolverNode(context, new ConvolverOptions { Buffer = impulse, DisableNormalization = true });
        var input = new AudioBuffer(new AudioBufferOptions { Length = 256, SampleRate = 8000 });
        var inputSamples = input.GetChannelData(0);
        for (var i = 0; i < inputSamples.Length; i++)
        {
            inputSamples[i] = (float)Math.Sin(i / 10.0);
        }

        var source = new AudioBufferSourceNode(context, new AudioBufferSourceOptions { Buffer = input });
        source.Connect(convolver).Connect(context.Destination);
        source.Start();

        var output = Render(context).GetChannelData(0);

        Assert.Equal(-1, output[..100].IndexOfAnyExcept(0f));
        for (var i = 0; i < 256; i++)
        {
            Assert.True(Math.Abs(output[100 + i] - inputSamples[i]) < 1e-6, $"Sample {i}");
        }

        Assert.Equal(-1, output[356..].IndexOfAnyExcept(0f));
    }

    [Fact]
    public void WaveShaperNode_MapsTheInputWithTheCurve()
    {
        var context = new OfflineAudioContext(1, 128, 8000);
        var source = new ConstantSourceNode(context, new ConstantSourceOptions { Offset = 0.5f });
        var shaper = new WaveShaperNode(context, new WaveShaperOptions { Curve = [-1, 0, 2] });
        source.Connect(shaper).Connect(context.Destination);
        source.Start();

        Assert.Equal(1f, Render(context).GetChannelData(0)[0]);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => shaper.Curve = [0, 1]).Name);
    }

    [Fact]
    public void Panners_PanTheInput()
    {
        var context = new OfflineAudioContext(2, 128, 8000);
        var source = new ConstantSourceNode(context);
        var stereoPanner = new StereoPannerNode(context, new StereoPannerOptions { Pan = -1 });
        var panner = new PannerNode(context, new PannerOptions { PositionX = 1, RolloffFactor = 0 });
        var merger = new ChannelMergerNode(context, new ChannelMergerOptions { NumberOfInputs = 2 });
        var splitter1 = new ChannelSplitterNode(context, new ChannelSplitterOptions { NumberOfOutputs = 2 });
        var splitter2 = new ChannelSplitterNode(context, new ChannelSplitterOptions { NumberOfOutputs = 2 });
        source.Connect(stereoPanner).Connect(splitter1);
        source.Connect(panner).Connect(splitter2);

        // Channel 0: the right channel of the stereo panner (silent); channel 1: the right channel of the panner (the whole signal)
        splitter1.Connect(merger, 1, 0);
        splitter2.Connect(merger, 1, 1);
        merger.Connect(context.Destination);
        source.Start();

        var buffer = Render(context);

        Assert.True(Math.Abs(buffer.GetChannelData(0)[0]) < 1e-7);
        Assert.True(Math.Abs(buffer.GetChannelData(1)[0] - 1) < 1e-6);
    }

    [Fact]
    public void AnalyserNode_ProvidesTheTimeDomainAndFrequencyData()
    {
        var context = new OfflineAudioContext(1, 2048, 8000);
        var oscillator = new OscillatorNode(context, new OscillatorOptions { Frequency = 1000 });
        var analyser = new AnalyserNode(context, new AnalyserOptions { FftSize = 256, SmoothingTimeConstant = 0 });
        oscillator.Connect(analyser).Connect(context.Destination);
        oscillator.Start();

        var output = Render(context).GetChannelData(0);
        var timeDomain = new float[256];
        analyser.GetFloatTimeDomainData(Bytes(timeDomain));
        var frequencies = new float[128];
        analyser.GetFloatFrequencyData(Bytes(frequencies));

        Assert.Equal(output[^1], timeDomain[^1]);
        Assert.Equal(output[^256], timeDomain[0]);

        // The peak is the bin of 1000 Hz (128 bins from 0 to 4000 Hz)
        var peak = 0;
        for (var i = 1; i < frequencies.Length; i++)
        {
            if (frequencies[i] > frequencies[peak])
            {
                peak = i;
            }
        }

        Assert.Equal(32, peak);
        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => analyser.FftSize = 100).Name);
    }

    [Fact]
    public void DynamicsCompressorNode_ReducesTheGainOfALoudSignal()
    {
        var context = new OfflineAudioContext(1, 8192, 8000);
        var source = new ConstantSourceNode(context);
        var compressor = new DynamicsCompressorNode(context);
        source.Connect(compressor).Connect(context.Destination);
        source.Start();

        var samples = Render(context).GetChannelData(0);

        Assert.True(compressor.Reduction < -5, $"Reduction {compressor.Reduction}");
        Assert.True(samples[^1] < 1);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => compressor.Ratio.AutomationRate = AutomationRate.ARate).Name);
    }

    [Fact]
    public void Script_RendersAnOfflineContext()
    {
        var document = Load("""
            <script>
              const context = new OfflineAudioContext({ numberOfChannels: 1, length: 256, sampleRate: 8000 });
              const events = [];
              context.onstatechange = () => events.push('statechange:' + context.state);
              context.oncomplete = e => events.push('complete:' + e.renderedBuffer.length);
              const source = new ConstantSourceNode(context, { offset: 0.5 });
              source.connect(context.destination);
              source.start();
              source.onended = () => events.push('ended');
              source.stop(0.01);
              const promise = context.startRendering();
              events.push('state:' + context.state);
              promise.then(buffer => {
                const data = buffer.getChannelData(0);
                document.title = [data[0], data[79], data[80], buffer.duration, context.currentTime, data === buffer.getChannelData(0), ...events].join();
              });
            </script>
            """);

        Assert.Equal("0.5,0.5,0,0.032,0.032,true,state:running,statechange:running,ended", document.Title);
    }

    [Fact]
    public void Script_GetChannelDataSharesTheSamplesUntilTheContentIsAcquired()
    {
        var document = Load("""
            <script>
              const context = new OfflineAudioContext(1, 128, 8000);
              const buffer = context.createBuffer(1, 4, 8000);
              const data = buffer.getChannelData(0);
              data[0] = 1;
              const copy = new Float32Array(4);
              buffer.copyFromChannel(copy, 0);
              const source = new AudioBufferSourceNode(context, { buffer });
              source.connect(context.destination);
              source.start();
              data[0] = 2;
              context.startRendering().then(rendered => {
                document.title = [copy[0], buffer.getChannelData(0)[0], buffer.getChannelData(0) === data, rendered.getChannelData(0)[0]].join();
              });
            </script>
            """);

        Assert.Equal("1,1,false,1", document.Title);
    }

    [Fact]
    public void Script_SuspendAndResume()
    {
        var document = Load("""
            <script>
              const context = new OfflineAudioContext(1, 512, 8000);
              const source = new ConstantSourceNode(context);
              source.connect(context.destination);
              source.start();
              const times = [];
              context.suspend(256 / 8000).then(() => {
                times.push(context.currentTime * 8000, context.state);
                source.offset.value = 2;
                return context.resume();
              });
              context.startRendering().then(buffer => {
                const data = buffer.getChannelData(0);
                document.title = [...times, data[255], data[256]].join();
              });
            </script>
            """);

        Assert.Equal("256,suspended,1,2", document.Title);
    }
}
