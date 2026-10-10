using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>Rational timing normalization and conversion costs.</summary>
[MemoryDiagnoser]
public class FrameDurationBenchmarks
{
    [Benchmark]
    public FrameDuration Normalize() => new(65_535, 1_000);

    [Benchmark]
    public TimeSpan ToTimeSpan() => new FrameDuration(1, 3).ToTimeSpan();
}
