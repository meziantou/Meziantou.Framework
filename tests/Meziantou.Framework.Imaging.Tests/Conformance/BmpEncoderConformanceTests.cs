using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Bmp;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using static Meziantou.Framework.Imaging.Tests.Conformance.LosslessEncoderAssertions;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// BMP encoding of every raw reference of the golden corpus, verified without external tools by the independent harness
/// reader <see cref="ReferenceBmp"/> (validated against the FFmpeg-checked corpus by the decoder conformance tests). The
/// encoder is lossless for the samples it stores, so every reference must come back exactly, in every option combination;
/// FFmpeg reads the same outputs in <c>InteropTests/BmpInteropTests</c>.
/// </summary>
public sealed class BmpEncoderConformanceTests
{
    public static TheoryData<string, string> Cases() => LosslessEncoderAssertions.Cases();

    [Theory]
    [MemberData(nameof(Cases))]
    public void BmpEncodingPreservesEverySample(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var layout in new[] { BmpPixelLayout.Auto, BmpPixelLayout.Bgra32 })
        {
            var encoder = new BmpEncoder { PixelLayout = layout, AllowBitDepthReduction = true, MetadataHandling = MetadataHandling.Strip };
            foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
            {
                var context = $"{id} {name} as {pixelFormat} ({layout})";
                using var image = EncoderSources.CreateImage(source, pixelFormat);
                var data = Encode(image, encoder);
                var reference = ReferenceBmp.Parse(data);
                Assert.Equal((source.Width, source.Height), (reference.Width, reference.Height));
                Assert.False(reference.IsTopDown, context + ": the rows must be bottom-up");
                var hasAlpha = PixelFormats.HasAlpha(pixelFormat);
                Assert.Equal(layout == BmpPixelLayout.Bgra32 || hasAlpha ? 32 : 24, reference.BitsPerPixel);
                Assert.Equal(reference.BitsPerPixel == 32, reference.HasAlphaMask);

                var expected = ToRgba8(source);
                AssertExact(expected, RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba8, reference.Rgba.Span), context);
                AssertLibraryReadsBack(data, expected, context);
            }
        }
    }
}
