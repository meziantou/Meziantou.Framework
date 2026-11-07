using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// OpenEXR decoding and encoding of a 3840x2160 photo-like image of half samples, with an alpha channel (it loads as
/// <see cref="RgbaFloat"/>, its colors divided by alpha) and without one (it loads as <see cref="RgbaHalf"/>, the samples
/// copied): the files are written by <see cref="OpenExrEncoder"/> with each of its compressions. The colors of the image
/// with alpha go up to 4, as in high dynamic range renders; the image without alpha is written from 8-bit pixels.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class ExrBenchmarks : IDisposable
{
    private const int Width = 3840;
    private const int Height = 2160;
    private readonly MemoryStream _output = new();
    private ImageConfiguration _configuration = null!;
    private Image<RgbaHalf> _rgba = null!;
    private byte[] _none = null!;
    private byte[] _rle = null!;
    private byte[] _zips = null!;
    private byte[] _zip = null!;
    private byte[] _piz = null!;
    private byte[] _zipRgb = null!;

    /// <summary>Gets or sets <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> (1 is the default configuration).</summary>
    [Params(1, 8)]
    public int Workers { get; set; } = 1;

    [GlobalSetup]
    public void Setup()
    {
        _configuration = new ImageConfiguration { MaxDegreeOfParallelism = Workers };
        var source = BenchmarkPixels.Rgba64(Width, Height, seed: 31, alpha: true);
        var pixels = new RgbaHalf[source.Length];
        var opaque = new Rgb24[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            var pixel = source[i];
            pixels[i] = new RgbaHalf(ToHalf(pixel.R, 4), ToHalf(pixel.G, 4), ToHalf(pixel.B, 4), ToHalf(pixel.A, 1));
            opaque[i] = new Rgb24((byte)(pixel.R >> 8), (byte)(pixel.G >> 8), (byte)(pixel.B >> 8));
        }

        _rgba = Image.ImportPixelData<RgbaHalf>(pixels, Width, Height, configuration: _configuration);
        _rgba.Metadata.TransferFunction = ColorTransferFunction.Linear;
        _none = Encode(_rgba, OpenExrCompression.None);
        _rle = Encode(_rgba, OpenExrCompression.Rle);
        _zips = Encode(_rgba, OpenExrCompression.Zips);
        _zip = Encode(_rgba, OpenExrCompression.Zip);
        _piz = Encode(_rgba, OpenExrCompression.Piz);
        using (var rgb = Image.ImportPixelData<Rgb24>(opaque, Width, Height, configuration: _configuration))
        {
            // 8-bit pixels without alpha are written as half samples, without an alpha channel
            rgb.Metadata.TransferFunction = ColorTransferFunction.Linear;
            _zipRgb = Encode(rgb, OpenExrCompression.Zip);
        }

        // Untimed gates: the compressions are lossless, so every file decodes to the pixels of the uncompressed one
        var expected = GetPixelBytes(_none);
        foreach (var file in (byte[][])[_rle, _zips, _zip, _piz])
        {
            if (!GetPixelBytes(file).AsSpan().SequenceEqual(expected))
                throw new InvalidOperationException("An OpenEXR workload does not decode to the pixels of the uncompressed file.");
        }

        if (Image.Identify(_zipRgb).PixelFormat != PixelFormat.RgbaHalf)
            throw new InvalidOperationException("The RGB OpenEXR workload does not load as RgbaHalf.");
    }

    [GlobalCleanup]
    public void Cleanup() => _rgba?.Dispose();

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    [Workload("3840x2160 OpenEXR RGBA half, NONE -> RgbaFloat")]
    public int DecodeNone() => Decode(_none);

    [Benchmark]
    [Workload("3840x2160 OpenEXR RGBA half, RLE -> RgbaFloat")]
    public int DecodeRle() => Decode(_rle);

    [Benchmark]
    [Workload("3840x2160 OpenEXR RGBA half, ZIPS -> RgbaFloat")]
    public int DecodeZips() => Decode(_zips);

    [Benchmark]
    [Workload("3840x2160 OpenEXR RGBA half, ZIP -> RgbaFloat")]
    public int DecodeZip() => Decode(_zip);

    [Benchmark]
    [Workload("3840x2160 OpenEXR RGBA half, PIZ -> RgbaFloat")]
    public int DecodePiz() => Decode(_piz);

    [Benchmark]
    [Workload("3840x2160 OpenEXR RGB half, ZIP -> RgbaHalf")]
    public int DecodeZipRgb() => Decode(_zipRgb);

    [Benchmark]
    [Workload("3840x2160 RgbaHalf -> OpenEXR NONE")]
    public long EncodeNone() => ImageWorkloads.Encode(_rgba, new OpenExrEncoder { Compression = OpenExrCompression.None }, _output);

    [Benchmark]
    [Workload("3840x2160 RgbaHalf -> OpenEXR RLE")]
    public long EncodeRle() => ImageWorkloads.Encode(_rgba, new OpenExrEncoder { Compression = OpenExrCompression.Rle }, _output);

    [Benchmark]
    [Workload("3840x2160 RgbaHalf -> OpenEXR ZIPS")]
    public long EncodeZips() => ImageWorkloads.Encode(_rgba, new OpenExrEncoder { Compression = OpenExrCompression.Zips }, _output);

    [Benchmark]
    [Workload("3840x2160 RgbaHalf -> OpenEXR ZIP")]
    public long EncodeZip() => ImageWorkloads.Encode(_rgba, new OpenExrEncoder { Compression = OpenExrCompression.Zip }, _output);

    [Benchmark]
    [Workload("3840x2160 RgbaHalf -> OpenEXR PIZ")]
    public long EncodePiz() => ImageWorkloads.Encode(_rgba, new OpenExrEncoder { Compression = OpenExrCompression.Piz }, _output);

    private static byte[] GetPixelBytes(byte[] data)
    {
        using var image = Image.Load(data);
        if (image.Width != Width || image.Height != Height)
            throw new InvalidOperationException("The OpenEXR workload does not decode to its size.");

        var bytes = new byte[Width * Height * 16];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static Half ToHalf(ushort value, float scale) => (Half)(value / 65535f * scale);

    private byte[] Encode(Image image, OpenExrCompression compression)
    {
        ImageWorkloads.Encode(image, new OpenExrEncoder { Compression = compression }, _output);
        return _output.ToArray();
    }

    private int Decode(byte[] data)
    {
        using var image = Image.Load(data, new ImageDecodeOptions { Configuration = _configuration });
        return image.Width;
    }
}
