using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// Internal kernels, scalar reference versus the path used by the library (vectorized when the hardware supports it): the
/// before/after evidence of the kernel optimizations. Inputs are seeded random data; the unit tests
/// (<c>VectorizedKernelTests</c>) check that both paths give identical results.
/// </summary>
[MemoryDiagnoser]
public class KernelBenchmarks : IDisposable
{
    private const int RowLength = 2048 * 3;
    private readonly byte[] _row = new byte[RowLength];
    private readonly byte[] _previous = new byte[RowLength];
    private readonly byte[] _filtered = new byte[RowLength];
    private readonly double[][] _blocks = new double[256][];
    private readonly double[] _block = new double[64];
    private readonly byte[] _samples = new byte[8 * 64];
    private readonly AllocationScope _scope = new(new ImageResourceLimits());
    private GifQuantizer _quantizer = null!;
    private int[] _colors = null!;

    [GlobalSetup]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded benchmark inputs.")]
    public void Setup()
    {
        var random = new Random(20);
        random.NextBytes(_row);
        random.NextBytes(_previous);
        for (var b = 0; b < _blocks.Length; b++)
        {
            var block = _blocks[b] = new double[64];
            for (var i = 0; i < 64; i++)
            {
                // Typical quantized photo blocks: a DC term and a few low-frequency AC terms
                block[i] = i == 0 ? random.Next(-1024, 1024) : i < 20 && random.NextDouble() < 0.5 ? random.Next(-60, 61) : 0;
            }
        }

        _quantizer = new GifQuantizer(_scope);
        _quantizer.Begin(256);
        var palette = new int[256];
        for (var i = 0; i < palette.Length; i++)
        {
            palette[i] = random.Next(1 << 24);
        }

        _quantizer.Add(palette);
        _quantizer.BuildPalette(256);
        _colors = new int[4096];
        for (var i = 0; i < _colors.Length; i++)
        {
            _colors[i] = random.Next(1 << 24);
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _quantizer?.Dispose();

    [Benchmark]
    public void PngPaethScalar() => PngFilters.FilterScalar(PngFilters.Paeth, _row, _previous, 3, _filtered);

    [Benchmark]
    public void PngPaeth() => PngFilters.Filter(PngFilters.Paeth, _row, _previous, 3, _filtered);

    [Benchmark]
    public long PngAdaptiveCostScalar() => PngFilters.GetAdaptiveCostScalar(_row);

    [Benchmark]
    public long PngAdaptiveCost() => PngFilters.GetAdaptiveCost(_row);

    [Benchmark(OperationsPerInvoke = 256)]
    public void JpegIdctScalar()
    {
        foreach (var block in _blocks)
        {
            block.CopyTo(_block, 0);
            Internals.JpegIdct.TransformScalar(_block, _samples, 64);
        }
    }

    [Benchmark(OperationsPerInvoke = 256)]
    public void JpegIdct()
    {
        foreach (var block in _blocks)
        {
            block.CopyTo(_block, 0);
            Internals.JpegIdct.Transform(_block, _samples, 64);
        }
    }

    [Benchmark]
    public uint Crc32() => Internals.Crc32.Compute(_row);

    [Benchmark]
    public uint Adler32() => BoundedInflater.ComputeAdler32(_row);

    [Benchmark(OperationsPerInvoke = 4096)]
    public int GifNearestScalar()
    {
        var sum = 0;
        foreach (var color in _colors)
        {
            sum += _quantizer.FindNearestScalar(color >> 16, (color >> 8) & 0xFF, color & 0xFF);
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = 4096)]
    public int GifNearest()
    {
        var sum = 0;
        foreach (var color in _colors)
        {
            sum += _quantizer.FindNearest(color >> 16, (color >> 8) & 0xFF, color & 0xFF);
        }

        return sum;
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }
}
