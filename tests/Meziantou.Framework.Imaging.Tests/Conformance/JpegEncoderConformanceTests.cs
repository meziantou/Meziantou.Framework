using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Jpeg;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Baseline JPEG encoding of every raw reference of the golden corpus (frames and posters as still images, in
/// every pixel format that holds them exactly), with five quality/subsampling settings. Non-opaque references are flattened
/// onto an explicit background and 16-bit references reduced explicitly (<see cref="JpegEncoder.AllowBitDepthReduction"/>).
/// The output is parsed by the independent harness reader (<see cref="ReferenceJpeg"/>: strict baseline marker structure,
/// its own Huffman decoding) and its quantized coefficients are compared with the independent encoder model
/// (<see cref="JpegEncoderModel"/>) computed from the reference pixels with the test-side flattening and reduction
/// (<see cref="JpegTestPatterns.ToEncodedSamples"/>). Tool-free: FFmpeg decodes the same kind of outputs in
/// <c>InteropTests/JpegEncoderInteropTests</c>.
/// </summary>
public sealed class JpegEncoderConformanceTests
{
    private static readonly Rgba64 Background = new(0xFFFF, 0x4000, 0x0080);

    private static readonly JpegEncoder[] Encoders =
    [
        new JpegEncoder { BackgroundColor = Background, AllowBitDepthReduction = true },
        new JpegEncoder { Quality = 75, ChromaSubsampling = JpegChromaSubsampling.Ratio444, BackgroundColor = Background, AllowBitDepthReduction = true },
        new JpegEncoder { Quality = 50, ChromaSubsampling = JpegChromaSubsampling.Ratio422, BackgroundColor = Background, AllowBitDepthReduction = true },
        new JpegEncoder { Quality = 100, ChromaSubsampling = JpegChromaSubsampling.Ratio444, BackgroundColor = Background, AllowBitDepthReduction = true },
        new JpegEncoder { Quality = 30, ChromaSubsampling = JpegChromaSubsampling.Ratio420, BackgroundColor = Background, AllowBitDepthReduction = true },
    ];

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (fixture, format) in EncoderSources.GetCases(GoldenCorpus.Default))
        {
            data.Add(fixture.Id, format.ToString());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EncodedCorpusReferencesMatchTheEncoderModel(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var (name, expected) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            var samples = JpegTestPatterns.ToEncodedSamples(expected, (Background.R, Background.G, Background.B));
            using var image = EncoderSources.CreateImage(expected, pixelFormat);
            foreach (var encoder in Encoders)
            {
                using var stream = new MemoryStream();
                image.Save(stream, encoder);
                var context = $"{id} {name} as {pixelFormat} (quality {encoder.Quality}, {encoder.ChromaSubsampling})";
                var reference = ReferenceJpeg.Parse(stream.ToArray());
                Assert.True(reference.Width == expected.Width && reference.Height == expected.Height && reference.HasJfifFirst, context);
                var gray = samples.Layout == RawPixelLayout.Gray8;
                var (h, v) = gray ? (1, 1) : encoder.ChromaSubsampling switch
                {
                    JpegChromaSubsampling.Ratio444 => (1, 1),
                    JpegChromaSubsampling.Ratio422 => (2, 1),
                    _ => (2, 2),
                };

                Assert.True(reference.Components.Count == (gray ? 1 : 3) && reference.Components[0].H == h && reference.Components[0].V == v, context);
                var model = JpegEncoderModel.ComputeCoefficients(samples, h, v, JpegEncoderModel.ScaleTable(JpegEncoderModel.AnnexKLuminance, encoder.Quality), JpegEncoderModel.ScaleTable(JpegEncoderModel.AnnexKChrominance, encoder.Quality));
                var comparison = JpegEncoderModel.Compare(model, reference.DecodeCoefficients());
                Assert.True(comparison.IsMatch, context + ": " + comparison.Describe());

                // Near-lossless setting: the plain reconstruction stays within a few units of the flattened, reduced source
                // (the corpus references are tiny, so a per-channel bias is not meaningful here)
                if (encoder.Quality == 100 && encoder.ChromaSubsampling == JpegChromaSubsampling.Ratio444)
                {
                    var error = ReconstructionError.Measure(samples, reference.DecodePixels());
                    Assert.True(error.Psnr > 40 && error.MaxError <= 4, context + ": " + error.Describe());
                }
            }
        }
    }
}
