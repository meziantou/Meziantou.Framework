using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Tga;
using static Meziantou.Framework.Imaging.Tests.Conformance.LosslessEncoderAssertions;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// TGA encoding of every raw reference of the golden corpus, verified without external tools by the independent harness
/// reader <see cref="ReferenceTga"/> (validated against the FFmpeg-checked corpus by the decoder conformance tests). The
/// encoder is lossless for the samples it stores, so every reference must come back exactly, in every option combination;
/// FFmpeg reads the same outputs in <c>InteropTests/TgaInteropTests</c>.
/// </summary>
public sealed class TgaEncoderConformanceTests
{
    public static TheoryData<string, string> Cases() => LosslessEncoderAssertions.Cases();

    [Theory]
    [MemberData(nameof(Cases))]
    public void TgaEncodingPreservesEverySample(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var compression in new[] { TgaCompression.None, TgaCompression.RunLength })
        {
            var encoder = new TgaEncoder { Compression = compression, AllowBitDepthReduction = true, MetadataHandling = MetadataHandling.Strip };
            foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
            {
                var context = $"{id} {name} as {pixelFormat} ({compression})";
                using var image = EncoderSources.CreateImage(source, pixelFormat);
                var data = Encode(image, encoder);
                var reference = ReferenceTga.Parse(data);
                Assert.Equal((source.Width, source.Height), (reference.Width, reference.Height));
                var grayscale = PixelFormats.IsGrayscale(pixelFormat);
                var expectedType = (grayscale, compression) switch
                {
                    (true, TgaCompression.None) => 3,
                    (true, _) => 11,
                    (false, TgaCompression.None) => 2,
                    _ => 10,
                };

                Assert.Equal(expectedType, reference.ImageType);
                Assert.Equal(grayscale ? 8 : (PixelFormats.HasAlpha(pixelFormat) ? 32 : 24), reference.PixelDepth);
                Assert.Equal(0, reference.Descriptor & 0x30); // origin at the bottom left
                Assert.Equal(26, reference.TrailingBytes); // the TGA 2.0 footer, with no developer or extension area
                AssertExact(ToRgba8(source), RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba8, reference.Rgba.Span), context);
                AssertLibraryReadsBack(data, ToRgba8(source), context);
            }
        }
    }
}
