using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Qoi;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// QOI encoding of every raw reference of the golden corpus, verified without external tools by the independent harness
/// reader <see cref="ReferenceQoi"/> (validated against the qoi.h- and FFmpeg-checked corpus by
/// <see cref="QoiDecoderConformanceTests"/>): every 8-bit sample must be reproduced, including the colors of fully transparent
/// pixels, with the channel count of the pixel format. The encoder's chunk selection is the one the specification describes
/// for the reference encoder, so encoding a reference-encoded fixture's pixels must reproduce the committed qoi.h output byte
/// for byte (its length and SHA-256 are in the manifest). FFmpeg decodes the same kinds of output in
/// <c>InteropTests/QoiInteropTests</c>.
/// </summary>
public sealed class QoiEncoderConformanceTests
{
    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (fixture, format) in EncoderSources.GetCases(GoldenCorpus.Default))
        {
            data.Add(fixture.Id, format.ToString());
        }

        return data;
    }

    public static TheoryData<string> ReferenceEncodedFixtures => [.. GoldenCorpus.Default.GetIds(format: "qoi", kind: FixtureKinds.Valid)
        .Where(id => GoldenCorpus.Default.Get(id).Entry.Provenance.Commands?.Any(command => command.StartsWith("qoi_driver encode", StringComparison.Ordinal)) == true)];

    [Theory]
    [MemberData(nameof(Cases))]
    public void EncodingPreservesEverySample(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        var encoder = new QoiEncoder { AllowBitDepthReduction = true, MetadataHandling = MetadataHandling.Strip };
        foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            var context = $"{id} {name} as {pixelFormat}";
            using var image = EncoderSources.CreateImage(source, pixelFormat);
            var data = Encode(image, encoder);
            var reference = ReferenceQoi.Parse(data);
            Assert.Equal(((uint)source.Width, (uint)source.Height), (reference.Width, reference.Height));
            Assert.Equal(PixelFormats.HasAlpha(pixelFormat) ? 4 : 3, reference.Channels);
            Assert.Equal(0, reference.ColorSpace);
            Assert.Equal(0, reference.TrailingBytes);
            AssertExact(ToRgba8(source), RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba8, reference.Rgba.Span), context);

            // The library reads its output back to the same samples
            using var decoded = Image.Load<Rgba32>(data);
            AssertExact(ToRgba8(source), ImageSnapshots.CaptureFrame(decoded.Frames[0]), context + " decoded by the library");
        }
    }

    [Theory]
    [MemberData(nameof(ReferenceEncodedFixtures))]
    public void OutputMatchesTheReferenceEncoderByteForByte(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var input = fixture.ReadInput();
        var format = fixture.Expected.PixelFormat == "Rgba32" ? PixelFormat.Rgba32 : PixelFormat.Rgb24;
        var layout = format == PixelFormat.Rgba32 ? RawPixelLayout.Rgba8 : RawPixelLayout.Rgb8;
        using var image = EncoderSources.CreateImage(fixture.GetFrame(0, layout), format);
        if (fixture.Expected.EffectiveTransferFunction == "linear")
        {
            image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        }

        var output = Encode(image, new QoiEncoder());
        Assert.True(input.AsSpan().SequenceEqual(output), $"{id}: the output ({output.Length} bytes) differs from the qoi.h reference encoder output ({input.Length} bytes).");
    }

    [Theory]
    [MemberData(nameof(ReferenceEncodedFixtures))]
    public void RoundTripsAreStable(string id)
    {
        // Decoding and re-encoding a QOI file reproduces it: the transfer function label and the samples survive
        var input = GoldenCorpus.Default.Get(id).ReadInput();
        using var image = Image.Load(input);
        Assert.Equal(input, Encode(image, new QoiEncoder()));
    }

    private static byte[] Encode(Image image, QoiEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    /// <summary>Straight 8-bit RGBA of a reference in any layout (gray replicated, 16-bit samples rounded with <c>(v * 255 + 32767) / 65535</c>).</summary>
    private static RawPixelBuffer ToRgba8(RawPixelBuffer source)
    {
        var result = new byte[source.Width * source.Height * 4];
        var channels = source.Layout.ChannelCount;
        var sixteen = source.Layout.BytesPerSample == 2;
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var offset = 4 * ((y * source.Width) + x);
                for (var c = 0; c < 4; c++)
                {
                    int value;
                    if (channels >= 3)
                    {
                        value = c < channels ? source.GetSample(x, y, c) : (sixteen ? 65535 : 255);
                    }
                    else
                    {
                        value = c < 3 ? source.GetSample(x, y, 0) : channels == 2 ? source.GetSample(x, y, 1) : (sixteen ? 65535 : 255);
                    }

                    result[offset + c] = sixteen ? (byte)(((value * 255) + 32767) / 65535) : (byte)value;
                }
            }
        }

        return RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba8, result);
    }

    private static void AssertExact(RawPixelBuffer expected, RawPixelBuffer actual, string context)
    {
        var result = PixelBufferComparer.Compare(expected, actual, ComparisonPolicy.Exact, context);
        Assert.True(result.IsMatch, result.Describe());
    }
}
