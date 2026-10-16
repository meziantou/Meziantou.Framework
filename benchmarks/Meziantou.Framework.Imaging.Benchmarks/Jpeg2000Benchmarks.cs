using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// JPEG 2000 decoding and encoding of a 1024x768 photo-like still: lossless (5-3 wavelet, reversible color transform) and
/// lossy (9-7 wavelet, irreversible color transform, quality 90), both written by the encoder of the library, which codes
/// one tile and one layer with 64x64 code-blocks and none of the code-block style options.
/// </summary>
/// <remarks>
/// The high-throughput block coder (Part 15) has no encoder in the library: its decoding workload reads the codestreams of
/// the golden corpus written by <c>ojph_compress</c> (lossless and lossy, 8 and 16 bits, precincts, tiles, and a 600x9 image
/// of 4x1024 code-blocks). These inputs are small: the workload measures the decoder on its own corpus and says nothing
/// about photographs.
/// </remarks>
[Config(typeof(WorkloadConfig))]
public class Jpeg2000Benchmarks : IDisposable
{
    private static readonly (string Name, int Width, int Height)[] HighThroughputInputs =
    [
        ("jp2/ht-blocks-4x1024.j2c", 600, 9),
        ("jp2/ht-lossless-rgb8.j2c", 13, 9),
        ("jp2/ht-lossless-rgb16.j2c", 9, 7),
        ("jp2/ht-lossy-rgb8.j2c", 33, 17),
        ("jp2/ht-precincts.j2c", 33, 17),
        ("jp2/ht-tiles.j2c", 33, 17),
    ];

    private static readonly Jpeg2000Encoder Lossy = new() { Compression = Jpeg2000Compression.Lossy };
    private readonly MemoryStream _output = new();
    private Image<Rgb24> _photo = null!;
    private byte[] _lossless = null!;
    private byte[] _lossy = null!;
    private byte[][] _highThroughput = null!;

    private static int Width => BenchmarkInputs.ServiceWidth;

    private static int Height => BenchmarkInputs.ServiceHeight;

    [GlobalSetup]
    public void Setup()
    {
        var source = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Rgb24>(BenchmarkPixels.Rgb24(Width, Height, seed: 5))).ToArray();
        _photo = Image.ImportPixelData<Rgb24>(source, Width, Height);
        _lossless = Encode(_photo, new Jpeg2000Encoder());
        _lossy = Encode(_photo, Lossy);
        _highThroughput = [.. HighThroughputInputs.Select(input => Read(input.Name))];

        // Untimed correctness gates: the inputs are what the workloads say
        WorkloadChecks.RequireLossless(_lossless, source);
        WorkloadChecks.RequirePsnr(_lossy, source, minimum: 35);
        for (var i = 0; i < _highThroughput.Length; i++)
        {
            using var image = Image.Load(_highThroughput[i]);
            if (image.Width != HighThroughputInputs[i].Width || image.Height != HighThroughputInputs[i].Height)
                throw new InvalidOperationException($"Unexpected HTJ2K input {HighThroughputInputs[i].Name}: {image.Width}x{image.Height}.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _photo?.Dispose();
    }

    [Benchmark]
    [Workload("1024x768 lossless JPEG 2000 (5-3, RCT) -> Rgb24")]
    public int DecodeLossless()
    {
        using var image = Image.Load<Rgb24>(_lossless);
        return image.Width;
    }

    [Benchmark]
    [Workload("1024x768 lossy JPEG 2000 (9-7, ICT, q90) -> Rgb24")]
    public int DecodeLossy()
    {
        using var image = Image.Load<Rgb24>(_lossy);
        return image.Width;
    }

    [Benchmark]
    [Workload("6 HTJ2K codestreams of the golden corpus (ojph_compress, up to 600x9) -> native pixel types")]
    public int DecodeHighThroughput()
    {
        var width = 0;
        foreach (var input in _highThroughput)
        {
            using var image = Image.Load(input);
            width += image.Width;
        }

        return width;
    }

    [Benchmark]
    [Workload("1024x768 Rgb24 -> lossless JPEG 2000 (5-3, RCT)", ReturnsOutputBytes = true)]
    public long EncodeLossless() => ImageWorkloads.Encode(_photo, new Jpeg2000Encoder(), _output);

    [Benchmark]
    [Workload("1024x768 Rgb24 -> lossy JPEG 2000 (9-7, ICT, q90)", ReturnsOutputBytes = true)]
    public long EncodeLossy() => ImageWorkloads.Encode(_photo, Lossy, _output);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }

    private static byte[] Read(string resourceName)
    {
        using var resource = typeof(Jpeg2000Benchmarks).Assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"The resource '{resourceName}' is missing.");
        using var stream = new MemoryStream();
        resource.CopyTo(stream);
        return stream.ToArray();
    }

    private byte[] Encode(Image image, ImageEncoder encoder)
    {
        ImageWorkloads.Encode(image, encoder, _output);
        return _output.ToArray();
    }
}
