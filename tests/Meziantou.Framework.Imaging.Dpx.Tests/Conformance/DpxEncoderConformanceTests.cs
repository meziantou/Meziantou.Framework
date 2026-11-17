using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Dpx;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// DPX encoding of every raw reference of the golden corpus, with the default depths and big-endian 10-bit components
/// (filled with method A): the image data are the packing of <see cref="DpxFileBuilder.PackLine"/> for the code values of
/// the source, and the library reads the default depths back exactly. FFmpeg, ImageMagick and OpenImageIO read the output
/// in <c>InteropTests/DpxInteropTests</c>.
/// </summary>
public sealed class DpxEncoderConformanceTests
{
    public static TheoryData<string, string> Cases() => LosslessEncoderAssertions.Cases();

    [Theory]
    [MemberData(nameof(Cases))]
    public void ImageDataAreTheCodeValuesOfTheSource(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            var context = $"{id} {name} as {pixelFormat}";
            using var image = EncoderSources.CreateImage(source, pixelFormat);
            var data = LosslessEncoderAssertions.Encode(image, new DpxEncoder { MetadataHandling = MetadataHandling.Strip });
            var sixteen = source.Layout.MaxSampleValue > 255;
            LosslessEncoderAssertions.AssertLibraryReadsBack(data, sixteen ? LosslessEncoderAssertions.ToRgba16(source) : LosslessEncoderAssertions.ToRgba8(source), context);

            // The 10-bit code values of the source, round(v × 1023 / max), in the words of a big-endian file
            var tenBit = LosslessEncoderAssertions.Encode(image, new DpxEncoder { MetadataHandling = MetadataHandling.Strip, BitDepth = 10, AllowBitDepthReduction = true });
            var gray = tenBit[800] == 6;
            var alpha = tenBit[800] == 51;
            var rgba = LosslessEncoderAssertions.ToRgba16(source);
            var expected = new List<byte>();
            for (var y = 0; y < source.Height; y++)
            {
                var components = new List<uint>();
                for (var x = 0; x < source.Width; x++)
                {
                    for (var c = 0; c < (gray ? 1 : alpha ? 4 : 3); c++)
                    {
                        var value = (ulong)rgba.GetSample(x, y, c);
                        var sourceValue = sixteen ? value : value / 257;
                        var max = sixteen ? 65535UL : 255UL;
                        components.Add((uint)(((2 * sourceValue * 1023) + max) / (2 * max)));
                    }
                }

                expected.AddRange(DpxFileBuilder.PackLine(components, 10, packing: 1, multiComponent: !gray, bigEndian: true));
            }

            Assert.Equal(expected.ToArray(), tenBit.AsSpan(8192).ToArray(), context);
        }
    }
}
