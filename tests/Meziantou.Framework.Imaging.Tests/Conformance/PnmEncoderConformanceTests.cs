using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Pnm;
using static Meziantou.Framework.Imaging.Tests.Conformance.LosslessEncoderAssertions;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Netpbm encoding of every raw reference of the golden corpus, verified without external tools by the independent harness
/// reader <see cref="ReferencePnm"/> (validated against the FFmpeg-checked corpus by the decoder conformance tests). The
/// encoder is lossless for the samples it stores, so every reference must come back exactly, in every option combination;
/// FFmpeg reads the same outputs in <c>InteropTests/PnmInteropTests</c>.
/// </summary>
public sealed class PnmEncoderConformanceTests
{
    public static TheoryData<string, string> Cases() => LosslessEncoderAssertions.Cases();

    [Theory]
    [MemberData(nameof(Cases))]
    public void PnmEncodingPreservesEverySample(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        var encoder = new PnmEncoder { MetadataHandling = MetadataHandling.Strip };
        foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            var context = $"{id} {name} as {pixelFormat}";
            using var image = EncoderSources.CreateImage(source, pixelFormat);
            var data = Encode(image, encoder);
            var reference = ReferencePnm.Parse(data);
            Assert.Equal((source.Width, source.Height), (reference.Width, reference.Height));
            Assert.Equal(0, reference.TrailingBytes);
            var sixteen = PixelFormats.GetBitsPerComponent(pixelFormat) > 8;
            Assert.Equal(sixteen ? 65535 : 255, reference.MaxValue);
            var expectedMagic = (PixelFormats.IsGrayscale(pixelFormat), PixelFormats.HasAlpha(pixelFormat)) switch
            {
                (true, _) => 5,
                (false, false) => 6,
                _ => 7,
            };

            Assert.Equal(expectedMagic, reference.Magic);

            // Netpbm stores the samples at their own precision: compare at the precision of the source
            if (sixteen)
            {
                AssertExact(ToRgba16(source), RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba16Le, reference.GetRgba16Le()), context);
            }
            else
            {
                AssertExact(ToRgba8(source), RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba8, reference.GetRgba8()), context);
            }

            AssertLibraryReadsBack(data, sixteen ? ToRgba16(source) : ToRgba8(source), context);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void PlainNetpbmOutputHasTheSameSamplesAsBinaryOutput(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        var encoder = new PnmEncoder { Encoding = PnmEncoding.Plain, BackgroundColor = new Rgba64(0, 0, 0), MetadataHandling = MetadataHandling.Strip };
        foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            var context = $"{id} {name} as {pixelFormat} (plain)";
            using var image = EncoderSources.CreateImage(source, pixelFormat);
            var data = Encode(image, encoder);
            var reference = ReferencePnm.Parse(data);
            Assert.Equal(PixelFormats.IsGrayscale(pixelFormat) ? 2 : 3, reference.Magic);
            var longest = SplitLines(data).Max(line => line.Length);
            Assert.True(longest <= PlainLineLimit, $"{context}: the longest plain raster line is {longest} characters.");

            // Plain output has no alpha (PAM has no plain form): the opaque pixels must still be exact
            var expected = Flatten(PixelFormats.GetBitsPerComponent(pixelFormat) > 8 ? ToRgba16(source) : ToRgba8(source));
            var actual = PixelFormats.GetBitsPerComponent(pixelFormat) > 8
                ? RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba16Le, reference.GetRgba16Le())
                : RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba8, reference.GetRgba8());
            AssertExact(expected, actual, context);
        }
    }

    /// <summary>The longest line the Netpbm formats allow in a plain raster.</summary>
    private const int PlainLineLimit = 70;

    private static string[] SplitLines(byte[] data) => System.Text.Encoding.ASCII.GetString(data).Split('\n');

    /// <summary>Composites a reference over opaque black, which is what plain Netpbm output does with a black background.</summary>
    private static RawPixelBuffer Flatten(RawPixelBuffer source)
    {
        var maximum = source.Layout.MaxSampleValue;
        var builder = new RawPixelBufferBuilder(source.Width, source.Height, source.Layout);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var alpha = source.GetSample(x, y, 3);
                for (var c = 0; c < 3; c++)
                {
                    builder.SetSample(x, y, c, (int)((((long)source.GetSample(x, y, c) * alpha) + (maximum / 2)) / maximum));
                }

                builder.SetSample(x, y, 3, maximum);
            }
        }

        return builder.Build();
    }
}
