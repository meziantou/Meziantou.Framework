using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Jpeg2000;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using static Meziantou.Framework.Imaging.Tests.Conformance.LosslessEncoderAssertions;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Lossless JPEG 2000 encoding of every raw reference of the golden corpus, in every pixel format, as a JP2 file and as a
/// codestream. The structure of each output is read by the independent reader of the harness
/// (<see cref="ReferenceJpeg2000"/>): the size, the depth, the components, the reversible transformations, one tile in
/// one tile-part, and for a file its boxes and its color space. The samples are read back by the library, whose decoder
/// is compared with independent references by <see cref="Jpeg2000DecoderConformanceTests"/>: every sample must be
/// reproduced, at 8 and at 16 bits, including the colors of fully transparent pixels. OpenJPEG and FFmpeg decode the same
/// kinds of output in <c>InteropTests/Jpeg2000EncoderInteropTests</c>.
/// </summary>
public sealed class Jpeg2000EncoderConformanceTests
{
    public static TheoryData<string, string> Cases() => LosslessEncoderAssertions.Cases();

    [Theory]
    [MemberData(nameof(Cases))]
    public void EncodingPreservesEverySample(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        var isGray = pixelFormat is PixelFormat.Gray8 or PixelFormat.Gray16;
        var is16Bit = PixelFormats.GetBitsPerComponent(pixelFormat) == 16;
        var hasAlpha = PixelFormats.HasAlpha(pixelFormat);
        var index = 0;
        foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            // The container changes the boxes around the codestream, never the samples: the cases alternate
            var container = ((id.Length + index++) % 2) == 0 ? Jpeg2000Container.Jp2 : Jpeg2000Container.Codestream;
            var context = $"{id} {name} as {pixelFormat}, {container}";
            using var image = EncoderSources.CreateImage(source, pixelFormat);
            var data = Encode(image, new Jpeg2000Encoder { Container = container, MetadataHandling = MetadataHandling.Strip });

            var reference = ReferenceJpeg2000.Parse(data);
            Assert.Equal(container == Jpeg2000Container.Jp2, reference.IsFile);
            Assert.Equal((source.Width, source.Height), (reference.Width, reference.Height));
            var componentCount = (isGray ? 1 : 3) + (hasAlpha ? 1 : 0);
            Assert.Equal(componentCount, reference.Components.Count);
            Assert.All(reference.Components, component => Assert.Equal((is16Bit ? 16 : 8, false, 1, 1), (component.Precision, component.IsSigned, component.XSubsampling, component.YSubsampling)));
            Assert.Equal(0, reference.Capabilities);
            Assert.Equal((0, 0, 0, 0), (reference.ImageXOffset, reference.ImageYOffset, reference.TileXOffset, reference.TileYOffset));
            Assert.Equal(1, reference.TileCount);
            var tilePart = Assert.Single(reference.TileParts);
            Assert.Equal((0, 0, 1), (tilePart.Tile, tilePart.Part, tilePart.PartCount));
            Assert.True(reference.HasEndOfCodestream, context);

            // The reversible transformations, without quantization; the component transformation for three colors
            var style = Assert.Single(reference.CodingStyles);
            Assert.True(style is { Component: -1, IsReversible: true, CodeBlockStyle: 0, HasPrecincts: false, CodeBlockWidth: 64, CodeBlockHeight: 64 }, context);
            Assert.Equal(0, reference.QuantizationStyle);
            Assert.Equal(!isGray, reference.UsesComponentTransform);
            Assert.Equal((0, 1), (reference.ProgressionOrder, reference.LayerCount));
            Assert.False(reference.UsesSop || reference.UsesEph, context);
            if (reference.IsFile)
            {
                Assert.Equal("jp2", reference.Brand?.Trim());
                Assert.Equal((componentCount, (is16Bit ? 16 : 8) - 1), (reference.HeaderComponentCount, reference.HeaderBitsPerComponent));
                Assert.Equal((1, isGray ? 17 : 16), (reference.ColorMethod, reference.EnumeratedColorSpace));
                Assert.Equal(1, reference.ColorSpecificationCount);
                Assert.Equal(1, reference.CodestreamCount);
                if (hasAlpha)
                {
                    // The last channel is the opacity of the whole image
                    Assert.Equal(componentCount, reference.ChannelDefinitions.Count);
                    Assert.Equal(new ReferenceJp2Channel(componentCount - 1, 1, 0), reference.ChannelDefinitions[^1]);
                }
                else
                {
                    Assert.Empty(reference.ChannelDefinitions);
                }
            }

            // The library reads its output back to the same samples
            AssertLibraryReadsBack(data, is16Bit ? ToRgba16(source) : ToRgba8(source), context);
            Assert.Equal(pixelFormat is PixelFormat.Bgra32 ? PixelFormat.Rgba32 : pixelFormat, Image.Identify(data).PixelFormat);
        }
    }
}
