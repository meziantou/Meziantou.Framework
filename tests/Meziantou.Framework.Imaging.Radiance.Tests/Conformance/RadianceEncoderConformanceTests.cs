using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Radiance;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Radiance HDR encoding of every raw reference of the golden corpus, with both scanline encodings: the RGBE pixels of the
/// output, read by <see cref="ReferenceRadiance"/>, are those "Real Pixels" gives for the colors of the source (its samples as
/// fractions of one, composited over black), and the library reads them as the reference reader does. FFmpeg and Apple
/// ImageIO read the output in <c>InteropTests/RadianceInteropTests</c>.
/// </summary>
public sealed class RadianceEncoderConformanceTests
{
    public static TheoryData<string, string> Cases() => LosslessEncoderAssertions.Cases();

    [Theory]
    [MemberData(nameof(Cases))]
    public void PixelsAreTheConversionOfRealPixels(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var compression in Enum.GetValues<RadianceCompression>())
        {
            var encoder = new RadianceEncoder { Compression = compression, MetadataHandling = MetadataHandling.Strip, BackgroundColor = new Rgba64(0, 0, 0, 65535) };
            foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
            {
                var context = $"{id} {name} as {pixelFormat} ({compression})";
                using var image = EncoderSources.CreateImage(source, pixelFormat);
                var data = LosslessEncoderAssertions.Encode(image, encoder);
                var picture = ReferenceRadiance.Read(data);
                Assert.Equal(image.Size, new Size(picture.Width, picture.Height), context);

                using var floats = image.CloneAs<RgbaFloat>();
                var expected = new byte[image.Width * image.Height * 4];
                for (var y = 0; y < image.Height; y++)
                {
                    for (var x = 0; x < image.Width; x++)
                    {
                        // The colors of a pixel that is not opaque are composited over black, in double precision, then stored as floats
                        var pixel = floats.Frames[0][x, y];
                        var alpha = Math.Clamp((double)pixel.A, 0, 1);
                        var (r, g, b) = pixel.A == 1f ? (pixel.R, pixel.G, pixel.B) : ((float)(alpha * pixel.R), (float)(alpha * pixel.G), (float)(alpha * pixel.B));
                        RadianceFileBuilder.ToRgbe(r, g, b).CopyTo(expected, ((y * image.Width) + x) * 4);
                    }
                }

                Assert.Equal(expected, picture.Rgbe.ToArray(), context);
                using var decoded = Image.Load<RgbaFloat>(data);
                var bytes = new byte[image.Width * image.Height * 16];
                decoded.Frames[0].CopyPixelBytesTo(bytes);
                Assert.Equal(picture.GetFloatPixels(), bytes, context);
            }
        }
    }
}
