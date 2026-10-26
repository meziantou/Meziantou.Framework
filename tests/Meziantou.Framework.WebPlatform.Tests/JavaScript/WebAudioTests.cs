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
        Assert.Equal(8f, output[18]);
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
