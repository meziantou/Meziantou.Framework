using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

/// <summary>
/// Live accounting equals the actual rented capacity for the real codecs: at every observation
/// point (a loaded image, a reader holding its state and a returned frame, a writer between frames) the live bytes of every
/// scope equal the total capacity of the pooled buffers that are still rented, so nothing is charged at a requested size
/// smaller than its buffer, and nothing rented is uncharged. Disposing everything returns every scope to zero.
/// </summary>
public sealed class AccountingConformanceTests
{
    private static readonly PixelConversionOptions Conversion = new() { DiscardIncompatibleColorProfile = true };

    public static TheoryData<string> ValidIds => [.. GoldenCorpus.Default.GetIds(kind: FixtureKinds.Valid).Where(id => GoldenCorpus.Default.Get(id).Entry.DecodeOptions is null)];

    [Theory]
    [MemberData(nameof(ValidIds))]
    public void LiveBytesEqualTheRentedCapacity(string id)
    {
        var data = GoldenCorpus.Default.Get(id).ReadInput();
        using var audit = new PoolAudit();
        using (var image = Image.Load(data, new ImageDecodeOptions { Conversion = Conversion }))
        {
            AssertBalanced(audit, "loaded image");
            Assert.True(audit.LiveBytes >= (long)image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat) * image.Frames.Count);

            using (var reader = Image.OpenReader<Rgba64>(new MemoryStream(data), new ImageReaderOptions { Conversion = Conversion }))
            {
                AssertBalanced(audit, "open reader");
                using var first = reader.ReadFrame();
                Assert.NotNull(first);
                AssertBalanced(audit, "reader and its first frame");
            }

            AssertBalanced(audit, "disposed reader");

            using var output = new MemoryStream();
            var encoder = Image.DetectFormat(data) switch
            {
                ImageFormat.Gif => (ImageEncoder)new GifEncoder(),
                ImageFormat.Jpeg => new JpegEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true, BackgroundColor = new Rgba64(0, 0, 0, 65535) },
                _ => new PngEncoder { MetadataHandling = MetadataHandling.Strip, AnimationMode = image.Frames.Count > 1 ? PngAnimationMode.Animated : PngAnimationMode.Auto },
            };
            using var typed = image.CloneAs<Rgba64>(Conversion);
            using (var writer = Image.CreateWriter<Rgba64>(output, new ImageWriterOptions(image.Size) { Encoder = encoder, ExpectedFrameCount = encoder is GifEncoder ? null : image.Frames.Count, Configuration = image.Configuration }))
            {
                writer.WriteFrame(typed.Frames[0]);
                AssertBalanced(audit, "writer after its first frame");
            }

            AssertBalanced(audit, "aborted writer");
        }

        audit.AssertClean(id);
    }

    private static void AssertBalanced(PoolAudit audit, string context)
    {
        Assert.Equal(audit.OutstandingBytes, audit.LiveBytes);
        audit.AssertClean(context, expectReleased: false);
    }
}
