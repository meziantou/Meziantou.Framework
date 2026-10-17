using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// QOI decoding and encoding: the 2048x1536 photo-like RGB source of the PNG and WebP workloads (3 channels) and the
/// 1024x768 RGBA source with opaque, translucent and transparent areas (4 channels), from a span and from a non-seekable
/// stream read in 64 KiB pieces (bounded input buffering). The PNG workloads encode the same RGB source
/// (<c>PngBenchmarks.LoadLarge8</c>/<c>SaveLarge8</c>), so sizes and times can be compared on one machine; no speedup over
/// other formats or libraries is claimed beyond what a run measures.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class QoiBenchmarks : IDisposable
{
    private readonly MemoryStream _output = new();
    private Image<Rgb24> _photo = null!;
    private Image<Rgba32> _transparent = null!;

    [GlobalSetup]
    public void Setup()
    {
        _photo = Image.ImportPixelData<Rgb24>(BenchmarkInputs.LargeRgb24Source, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight);
        _transparent = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ServiceRgba32Source, BenchmarkInputs.ServiceWidth, BenchmarkInputs.ServiceHeight);

        // Untimed correctness gates: both inputs are lossless
        WorkloadChecks.RequireLossless(BenchmarkInputs.QoiRgb, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.QoiRgba, BenchmarkInputs.ServiceRgba32Source);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _photo?.Dispose();
        _transparent?.Dispose();
    }

    [Benchmark]
    [Workload("2048x1536 QOI RGB -> Rgb24")]
    public int DecodeRgb()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.QoiRgb);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 QOI RGB -> Rgb24 from a non-seekable stream (64 KiB reads)")]
    public int DecodeRgbStream()
    {
        using var stream = new ChunkedStream(BenchmarkInputs.QoiRgb, 64 * 1024);
        using var image = Image.Load<Rgb24>(stream);
        return image.Width;
    }

    [Benchmark]
    [Workload("1024x768 QOI RGBA with transparency -> Rgba32")]
    public int DecodeRgba()
    {
        using var image = Image.Load<Rgba32>(BenchmarkInputs.QoiRgba);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> QOI RGB", ReturnsOutputBytes = true)]
    public long EncodeRgb() => ImageWorkloads.Encode(_photo, new QoiEncoder(), _output);

    [Benchmark]
    [Workload("1024x768 Rgba32 with transparency -> QOI RGBA", ReturnsOutputBytes = true)]
    public long EncodeRgba() => ImageWorkloads.Encode(_transparent, new QoiEncoder(), _output);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>A read-only, non-seekable view of a buffer that returns at most <c>chunk</c> bytes per read.</summary>
    private sealed class ChunkedStream(byte[] data, int chunk) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(Math.Min(buffer.Length, chunk), data.Length - _position);
            data.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
