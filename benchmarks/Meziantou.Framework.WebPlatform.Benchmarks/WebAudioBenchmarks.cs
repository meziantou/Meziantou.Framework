using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.WebAudio;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>Renders one second of audio graphs with an <see cref="OfflineAudioContext"/>: gains, channel mixing and convolution.</summary>
[MemoryDiagnoser]
public class WebAudioBenchmarks
{
    private const float SampleRate = 48000;
    private const uint Length = 48000;

    private AudioBuffer _stereo = null!;
    private AudioBuffer _surround = null!;
    private AudioBuffer _shortResponse = null!;
    private AudioBuffer _longResponse = null!;

    [GlobalSetup]
    public void Setup()
    {
        _stereo = CreateNoise(2, Length, seed: 1);
        _surround = CreateNoise(6, Length, seed: 2);
        _shortResponse = CreateNoise(2, 64, seed: 3);
        _longResponse = CreateNoise(2, 48000, seed: 4);
    }

    /// <summary>A stereo source through 16 gains with constant values.</summary>
    [Benchmark]
    public float GainChain()
    {
        var context = new OfflineAudioContext(2, Length, SampleRate);
        AudioNode node = Source(context, _stereo);
        for (var i = 0; i < 16; i++)
        {
            node = node.Connect(new GainNode(context, new GainOptions { Gain = 0.99f }));
        }

        node.Connect(context.Destination);
        return Render(context);
    }

    /// <summary>A stereo source through 16 gains automated with a ramp (a value per sample).</summary>
    [Benchmark]
    public float AutomatedGainChain()
    {
        var context = new OfflineAudioContext(2, Length, SampleRate);
        AudioNode node = Source(context, _stereo);
        for (var i = 0; i < 16; i++)
        {
            var gain = new GainNode(context);
            gain.Gain.SetValueAtTime(0.5f, 0);
            gain.Gain.LinearRampToValueAtTime(1, 1);
            node = node.Connect(gain);
        }

        node.Connect(context.Destination);
        return Render(context);
    }

    /// <summary>Eight 5.1 sources mixed into a stereo gain (down-mixing and summing of the inputs).</summary>
    [Benchmark]
    public float Mixing()
    {
        var context = new OfflineAudioContext(2, Length, SampleRate);
        var bus = new GainNode(context, new GainOptions { ChannelCount = 2, ChannelCountMode = ChannelCountMode.Explicit });
        for (var i = 0; i < 8; i++)
        {
            Source(context, _surround).Connect(bus);
        }

        bus.Connect(context.Destination);
        return Render(context);
    }

    /// <summary>A stereo source through a 64-frame response (direct convolution).</summary>
    [Benchmark]
    public float ShortConvolution() => Convolve(_shortResponse);

    /// <summary>A stereo source through a one-second response (partitioned convolution).</summary>
    [Benchmark]
    public float LongConvolution() => Convolve(_longResponse);

    private float Convolve(AudioBuffer response)
    {
        var context = new OfflineAudioContext(2, Length, SampleRate);
        var convolver = new ConvolverNode(context, new ConvolverOptions { Buffer = response, DisableNormalization = true });
        Source(context, _stereo).Connect(convolver).Connect(context.Destination);
        return Render(context);
    }

    private static AudioBufferSourceNode Source(OfflineAudioContext context, AudioBuffer buffer)
    {
        var source = new AudioBufferSourceNode(context, new AudioBufferSourceOptions { Buffer = buffer });
        source.Start();
        return source;
    }

    private static float Render(OfflineAudioContext context)
    {
        var buffer = context.StartRendering().Result;
        return buffer.GetChannelData(0)[^1];
    }

    private static AudioBuffer CreateNoise(uint channels, uint length, int seed)
    {
        var random = new Random(seed);
        var buffer = new AudioBuffer(new AudioBufferOptions { NumberOfChannels = channels, Length = length, SampleRate = SampleRate });
        for (var channel = 0; channel < channels; channel++)
        {
            var samples = buffer.GetChannelData(channel);
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = (random.NextSingle() * 2) - 1;
            }
        }

        return buffer;
    }
}
