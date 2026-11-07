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
    private readonly ushort[] _luma = new ushort[2048];
    private readonly ushort[] _cb = new ushort[2048];
    private readonly ushort[] _cr = new ushort[2048];
    private readonly ushort[] _alpha = new ushort[2048];
    private readonly byte[] _widePixels = new byte[2048 * 8];
    private HeifYuvConverter _heifConverter = null!;
    private PaletteQuantizer _quantizer = null!;
    private int[] _colors = null!;

    [GlobalSetup]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded benchmark inputs.")]
    public void Setup()
    {
        var random = new Random(20);
        random.NextBytes(_row);
        random.NextBytes(_previous);
        random.NextBytes(_filtered);
        for (var b = 0; b < _blocks.Length; b++)
        {
            var block = _blocks[b] = new double[64];
            for (var i = 0; i < 64; i++)
            {
                // Typical quantized photo blocks: a DC term and a few low-frequency AC terms
                block[i] = i == 0 ? random.Next(-1024, 1024) : i < 20 && random.NextDouble() < 0.5 ? random.Next(-60, 61) : 0;
            }
        }

        _quantizer = new PaletteQuantizer(_scope);
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

        // A row of a 10-bit image with a 10-bit alpha plane (BT.709, limited range), converted to Rgba64 pixels
        for (var i = 0; i < _luma.Length; i++)
        {
            _luma[i] = (ushort)random.Next(64, 941);
            _cb[i] = (ushort)random.Next(64, 961);
            _cr[i] = (ushort)random.Next(64, 961);
            _alpha[i] = (ushort)random.Next(1024);
        }

        var color = new HeifBitstreamFormat(10, false, 0, 0, Av1Constants.CspUnknown, Av1Constants.McBt709, false, ColorDescriptionPresent: true, 1, 1);
        var alpha = new HeifBitstreamFormat(10, IsMonochrome: true, 0, 0, Av1Constants.CspUnknown, Av1Constants.McIdentity, true, ColorDescriptionPresent: false, 2, 2);
        _heifConverter = new HeifYuvConverter(AvifBinding.Instance, color, Av1Constants.McBt709, fullRange: false, alpha, PixelFormat.Rgba64);
    }

    [GlobalCleanup]
    public void Cleanup() => _quantizer?.Dispose();

    [Benchmark]
    public void PngPaethScalar() => PngFilters.FilterScalar(PngFilters.Paeth, _row, _previous, 3, _filtered);

    [Benchmark]
    public void PngPaeth() => PngFilters.Filter(PngFilters.Paeth, _row, _previous, 3, _filtered);

    // Unfilter rewrites its row in place: the reconstructed bytes are as random as the filtered ones, so every call costs the same
    [Benchmark]
    public void PngUnfilterSub1() => PngFilters.Unfilter(PngFilters.Sub, _filtered, _previous, 1);

    [Benchmark]
    public void PngUnfilterSub4() => PngFilters.Unfilter(PngFilters.Sub, _filtered, _previous, 4);

    [Benchmark]
    public void PngUnfilterAverage1() => PngFilters.Unfilter(PngFilters.Average, _filtered, _previous, 1);

    [Benchmark]
    public void PngUnfilterPaeth1() => PngFilters.Unfilter(PngFilters.Paeth, _filtered, _previous, 1);

    [Benchmark]
    public void PngUnfilterPaeth2() => PngFilters.Unfilter(PngFilters.Paeth, _filtered, _previous, 2);

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
    public uint Crc32() => Hashing.Crc32.Compute(_row);

    [Benchmark]
    public uint Adler32() => Hashing.Adler32.Compute(_row);

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

    [Benchmark]
    public void HeifYuvRgba64AlphaScalar() => _heifConverter.ConvertRowScalar(_luma, _cb, _cr, _alpha, _widePixels);

    [Benchmark]
    public void HeifYuvRgba64Alpha() => _heifConverter.ConvertRow(_luma, _cb, _cr, _alpha, _widePixels);

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }
}
