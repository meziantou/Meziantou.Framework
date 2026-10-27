using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using static Meziantou.Framework.Imaging.Tests.Conformance.LosslessEncoderAssertions;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// OpenEXR encoding of every raw reference of the golden corpus, with every compression: the library reads every 8-bit
/// and 16-bit sample back exactly (the colors of fully transparent pixels included), through the floating-point samples
/// the file stores (half samples for 8-bit pixels, float samples for 16-bit ones, colors premultiplied by alpha). OpenEXR
/// and FFmpeg read the output in <c>InteropTests/ExrInteropTests</c>.
/// </summary>
public sealed class ExrEncoderConformanceTests
{
    public static TheoryData<string, string> Cases() => LosslessEncoderAssertions.Cases();

    [Theory]
    [MemberData(nameof(Cases))]
    public void EncodingPreservesEverySample(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var compression in Enum.GetValues<OpenExrCompression>())
        {
            var encoder = new OpenExrEncoder { Compression = compression, MetadataHandling = MetadataHandling.Strip };
            foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
            {
                var context = $"{id} {name} as {pixelFormat} ({compression})";
                using var image = EncoderSources.CreateImage(source, pixelFormat);
                var data = Encode(image, encoder);
                AssertLibraryReadsBack(data, PixelFormats.GetBitsPerComponent(pixelFormat) > 8 ? ToRgba16(source) : ToRgba8(source), context);
            }
        }
    }
}
