using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>The cases and exact comparisons shared by the conformance tests of the lossless BMP, TGA and Netpbm encoders.</summary>
internal static class LosslessEncoderAssertions
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

    public static void AssertLibraryReadsBack(byte[] data, RawPixelBuffer expected, string context)
    {
        if (expected.Layout == RawPixelLayout.Rgba16Le)
        {
            using var decoded = Image.Load<Rgba64>(data);
            AssertExact(expected, ImageSnapshots.CaptureFrame(decoded.Frames[0]), context + " decoded by the library");
            return;
        }

        using var image = Image.Load<Rgba32>(data);
        AssertExact(expected, ImageSnapshots.CaptureFrame(image.Frames[0]), context + " decoded by the library");
    }

    public static byte[] Encode(Image image, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    /// <summary>Straight 8-bit RGBA of a reference in any layout (gray replicated, 16-bit samples rounded).</summary>
    public static RawPixelBuffer ToRgba8(RawPixelBuffer source) => Convert(source, RawPixelLayout.Rgba8);

    /// <summary>Straight 16-bit RGBA of a reference in any layout (gray replicated, 8-bit samples scaled by 257).</summary>
    public static RawPixelBuffer ToRgba16(RawPixelBuffer source) => Convert(source, RawPixelLayout.Rgba16Le);

    private static RawPixelBuffer Convert(RawPixelBuffer source, RawPixelLayout layout)
    {
        var builder = new RawPixelBufferBuilder(source.Width, source.Height, layout);
        var channels = source.Layout.ChannelCount;
        var sourceMaximum = source.Layout.MaxSampleValue;
        var targetMaximum = layout.MaxSampleValue;
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                for (var c = 0; c < 4; c++)
                {
                    int value;
                    if (channels >= 3)
                    {
                        value = c < channels ? source.GetSample(x, y, c) : sourceMaximum;
                    }
                    else
                    {
                        value = c < 3 ? source.GetSample(x, y, 0) : channels == 2 ? source.GetSample(x, y, 1) : sourceMaximum;
                    }

                    builder.SetSample(x, y, c, sourceMaximum == targetMaximum ? value : (int)((((long)value * targetMaximum) + (sourceMaximum / 2)) / sourceMaximum));
                }
            }
        }

        return builder.Build();
    }

    public static void AssertExact(RawPixelBuffer expected, RawPixelBuffer actual, string context)
    {
        var result = PixelBufferComparer.Compare(expected, actual, ComparisonPolicy.Exact, context);
        Assert.True(result.IsMatch, result.Describe());
    }
}
