using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// The AV1 post-processing filters on their own, at sizes that the decoded corpus of <see cref="AvifBenchmarks"/> is too small
/// to weigh: the self-guided loop restoration of 64x64 blocks with every parameter set. Inputs are seeded random data;
/// <c>VectorizedKernelTests</c> checks the vector paths against their scalar references.
/// </summary>
[MemoryDiagnoser]
public class Av1FilterBenchmarks
{
    private const int BlockSize = Av1LoopRestoration.MaxBlockWidth;
    private const int SetCount = 16;
    private const int SourceStride = BlockSize + (2 * Av1LoopRestoration.Padding);
    private readonly ushort[] _source8 = new ushort[SourceStride * SourceStride];
    private readonly ushort[] _source10 = new ushort[SourceStride * SourceStride];
    private readonly ushort[] _destination = new ushort[BlockSize * BlockSize];
    private readonly int[] _scratch = new int[Av1LoopRestoration.ScratchLength];
    private readonly sbyte[][] _units = new sbyte[SetCount][];

    [GlobalSetup]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded benchmark inputs.")]
    public void Setup()
    {
        // A textured block: a smooth ramp with noise, so that the boxes have every variance
        var random = new Random(42);
        for (var i = 0; i < _source8.Length; i++)
        {
            var ramp = (i % SourceStride) + (i / SourceStride);
            _source8[i] = (ushort)Math.Clamp((ramp * 2) + random.Next(-12, 13), 0, 255);
            _source10[i] = (ushort)Math.Clamp((ramp * 8) + random.Next(-48, 49), 0, 1023);
        }

        // One restoration unit per self-guided set, with the default projection weights
        for (var set = 0; set < SetCount; set++)
        {
            _units[set] = [Av1Constants.RestoreSgrproj, 0, 0, 0, 0, 0, 0, (sbyte)set, Av1Tables.SgrprojXqdMid[0], Av1Tables.SgrprojXqdMid[1]];
        }
    }

    [Benchmark(OperationsPerInvoke = SetCount)]
    public void SelfGuided8() => SelfGuided(_source8, bitDepth: 8);

    [Benchmark(OperationsPerInvoke = SetCount)]
    public void SelfGuided10() => SelfGuided(_source10, bitDepth: 10);

    private void SelfGuided(ushort[] source, int bitDepth)
    {
        const int Offset = (Av1LoopRestoration.Padding * SourceStride) + Av1LoopRestoration.Padding;
        foreach (var unit in _units)
        {
            Av1LoopRestoration.ApplySelfGuided(source, SourceStride, Offset, _destination, BlockSize, BlockSize, BlockSize, unit, bitDepth, _scratch);
        }
    }
}
