using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.JpegXl;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using static Meziantou.Framework.Imaging.Tests.Conformance.LosslessEncoderAssertions;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// JPEG XL encoding of every raw reference of the golden corpus, in every pixel format, at three efforts. The structure of
/// each output is read by the independent reader of the harness (<see cref="ReferenceJpegXl"/>): the size, the depth, the
/// channels, the color space and a single Modular frame that is not stored in XYB, in a bare codestream for 8-bit
/// samples and in a container with a level box for 16-bit ones. The samples are read back by the library, whose decoder
/// is compared with independent references by <see cref="JpegXlDecoderConformanceTests"/>: every sample must be
/// reproduced, at 8 and at 16 bits, including the colors of fully transparent pixels. djxl, jxl-oxide and FFmpeg decode
/// the same kinds of output in <c>InteropTests/JpegXlEncoderInteropTests</c>.
/// </summary>
public sealed class JpegXlEncoderConformanceTests
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
        var index = 0;
        foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            // The effort changes the coding tools, never the samples: the cases share three of them
            var effort = ((id.Length + index++) % 3) switch
            {
                0 => 1,
                1 => 5,
                _ => 8,
            };
            var context = $"{id} {name} as {pixelFormat}, effort {effort}";
            using var image = EncoderSources.CreateImage(source, pixelFormat);
            var data = Encode(image, new JpegXlEncoder { Effort = effort, MetadataHandling = MetadataHandling.Strip });

            var reference = ReferenceJpegXl.Parse(data);
            Assert.Equal(is16Bit, reference.IsContainer);
            Assert.Equal(is16Bit ? 10 : null, reference.Level);
            Assert.Equal((source.Width, source.Height), (reference.Width, reference.Height));
            Assert.Equal((is16Bit ? 16 : 8, false), (reference.BitsPerSample, reference.HasFloatSamples));
            Assert.Equal(isGray ? 1 : 0, reference.ColorSpace);
            Assert.False(reference.IsXybEncoded, context);
            Assert.False(reference.HasIccProfile, context);
            Assert.False(reference.HasAnimation, context);
            Assert.Equal(1, reference.Orientation);
            if (PixelFormats.HasAlpha(pixelFormat))
            {
                var channel = Assert.Single(reference.ExtraChannels);
                Assert.Equal((0, is16Bit ? 16 : 8, 0, false), (channel.Type, channel.BitsPerSample, channel.DimShift, channel.IsAlphaAssociated));
            }
            else
            {
                Assert.Empty(reference.ExtraChannels);
            }

            Assert.NotNull(reference.Frames);
            var frame = Assert.Single(reference.Frames);
            Assert.True(frame is { Type: 0, IsModular: true, Flags: 0, IsYCbCr: false, Upsampling: 1, PassCount: 1, X: 0, Y: 0, BlendMode: 0, IsLast: true }, context);
            Assert.Equal((source.Width, source.Height), (frame.Width, frame.Height));

            // The library reads its output back to the same samples
            AssertLibraryReadsBack(data, is16Bit ? ToRgba16(source) : ToRgba8(source), context);
            Assert.Equal(pixelFormat is PixelFormat.Bgra32 ? PixelFormat.Rgba32 : pixelFormat is PixelFormat.Rgba64 ? PixelFormat.Rgba64 : pixelFormat, Image.Identify(data).PixelFormat);
        }
    }
}
