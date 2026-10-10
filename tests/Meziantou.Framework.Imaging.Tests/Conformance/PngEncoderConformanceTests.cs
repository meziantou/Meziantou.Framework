using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Png;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Static PNG encoding of every raw reference of the golden corpus (frames and posters as still images, in every
/// pixel format that holds them exactly: 8/16-bit RGBA, BGRA, RGB, gray 8/16), with fixed and adaptive filters, every
/// compression level and Adam7. The output is decoded by the independent harness reader (<see cref="ReferencePng"/>) and
/// compared exactly with the same reference; the IHDR color type and bit depth are checked against the pixel format.
/// Tool-free: FFmpeg decodes the same outputs in <c>InteropTests/PngEncoderInteropTests</c>.
/// </summary>
public sealed class PngEncoderConformanceTests
{
    private static readonly PngEncoder[] Encoders =
    [
        new PngEncoder(),
        new PngEncoder { Interlaced = true },
        new PngEncoder { Filter = PngFilter.None, CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression },
        new PngEncoder { Filter = PngFilter.Paeth, CompressionLevel = System.IO.Compression.CompressionLevel.SmallestSize, Interlaced = true },
        new PngEncoder { Filter = PngFilter.Sub, CompressionLevel = System.IO.Compression.CompressionLevel.Fastest },
        new PngEncoder { Filter = PngFilter.Up, Interlaced = true },
        new PngEncoder { Filter = PngFilter.Average },
    ];

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (fixture, format) in EncoderSources.GetCases(GoldenCorpus.Default))
        {
            data.Add(fixture.Id, format.ToString());
        }

        return data;
    }

    [Fact]
    public void CorpusCoversEveryStorageType()
    {
        var formats = EncoderSources.GetCases(GoldenCorpus.Default).Select(item => item.Format).ToHashSet();
        Assert.HasCount(6, formats);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EncodedCorpusReferencesDecodeIndependently(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var (name, expected) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            using var image = EncoderSources.CreateImage(expected, pixelFormat);
            foreach (var encoder in Encoders)
            {
                using var stream = new MemoryStream();
                image.Save(stream, encoder);
                var reference = ReferencePng.Parse(stream.ToArray());
                var (colorType, bitDepth) = pixelFormat switch
                {
                    PixelFormat.Gray8 => (0, 8),
                    PixelFormat.Gray16 => (0, 16),
                    PixelFormat.Rgb24 => (2, 8),
                    PixelFormat.Rgba64 => (6, 16),
                    _ => (6, 8),
                };

                var context = $"{id} {name} as {pixelFormat} (filter {encoder.Filter}, {encoder.CompressionLevel}, interlaced {encoder.Interlaced})";
                Assert.True(reference.ColorType == colorType && reference.BitDepth == bitDepth, $"{context}: color type {reference.ColorType}, bit depth {reference.BitDepth}.");
                Assert.Equal(encoder.Interlaced ? 1 : 0, reference.InterlaceMethod);
                var result = PixelBufferComparer.Compare(expected, reference.DecodePixels(), ComparisonPolicy.Exact, context);
                Assert.True(result.IsMatch, result.Describe());
            }
        }
    }
}
