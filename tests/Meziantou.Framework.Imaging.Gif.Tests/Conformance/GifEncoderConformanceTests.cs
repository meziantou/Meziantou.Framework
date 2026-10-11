using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Gif;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// GIF encoding of the golden corpus, tool-free. The independent harness reader (<see cref="ReferenceGif"/>, its
/// own LZW decoder and compositor) is first validated against every corpus GIF, then used as the oracle:
/// (1) every raw reference set (displayed frames of every valid fixture, in every pixel format that holds them exactly) is
/// imported as an animation (or a still image) with the fixture's durations and play count, encoded with several settings
/// and verified by <see cref="GifOutputVerifier"/>: exact pixels when the alpha-reduced frame fits the palette, otherwise
/// documented quantization error bounds, and always exact structure, transparency, timing and plays;
/// (2) every corpus GIF is decoded, edited (frames moved and removed, opaque regions made transparent) and saved: the encoded
/// displayed frames must be the corpus references permuted by the edit, independently of the source file's rectangles,
/// palettes and disposals. FFmpeg and Apple ImageIO decode the same kinds of output in <c>InteropTests/GifEncoderInteropTests</c>.
/// </summary>
public sealed class GifEncoderConformanceTests
{
    private static readonly Rgba64 Background = new(0x2000, 0x8000, 0xFFFF);

    private static readonly GifEncoder[] Encoders =
    [
        new GifEncoder(),
        new GifEncoder { Interlaced = true, Dithering = GifDithering.FloydSteinberg },
        new GifEncoder { MaxColors = 16, AlphaThreshold = 200 },
        new GifEncoder { MaxColors = 64, Dithering = GifDithering.FloydSteinberg, AlphaMode = GifAlphaMode.Flatten, BackgroundColor = Background },
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

    public static TheoryData<string> GifFixtures => [.. GoldenCorpus.Default.GetIds(format: "gif", kind: FixtureKinds.Valid)];

    public static TheoryData<string> InvalidGifFixtures => [.. GoldenCorpus.Default.GetIds(format: "gif", kind: FixtureKinds.Invalid)];

    [Theory]
    [MemberData(nameof(GifFixtures))]
    public void ReferenceReaderAgreesWithTheCorpusGifs(string id)
    {
        // The harness reader is the oracle of the encoder tests: it must read every corpus GIF like the corpus references
        var fixture = GoldenCorpus.Default.Get(id);
        var reference = ReferenceGif.Parse(fixture.ReadInput());
        var frames = reference.DecodeDisplayedFrames();
        Assert.Equal(fixture.Expected.FrameCount, frames.Count);
        Assert.Equal(fixture.Expected.Animation is { } animation ? animation.TotalPlays : 1, reference.TotalPlays);
        for (var i = 0; i < frames.Count; i++)
        {
            AssertSame(fixture.GetFrame(i, RawPixelLayout.Rgba8), frames[i], $"{id} frame {i}");
            if (fixture.Expected.Frames[i].Duration is not null)
            {
                Assert.Equal(fixture.GetDuration(i), RationalDuration.Create(reference.Images[i].Control?.DelayHundredths ?? 0, 100));
            }
        }
    }

    [Theory]
    [MemberData(nameof(InvalidGifFixtures))]
    public void ReferenceReaderRejectsTheCorpusGifErrors(string id)
    {
        var data = GoldenCorpus.Default.Get(id).ReadInput();
        Assert.Throws<InvalidDataException>(() => ReferenceGif.Parse(data).DecodeDisplayedFrames());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CorpusReferencesEncodeAsGif(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        var frames = EncoderSources.GetStills(fixture, pixelFormat).Where(still => still.Name != "poster").ToList();
        using var image = EncoderSources.CreateImage(frames[0].Pixels, pixelFormat);
        for (var i = 1; i < frames.Count; i++)
        {
            using var frame = EncoderSources.CreateImage(frames[i].Pixels, pixelFormat);
            image.AppendFrame(frame.Frames[0]);
        }

        for (var i = 0; i < frames.Count; i++)
        {
            image.Frames[i].Metadata.Duration = fixture.Expected.Frames[i].Duration is null ? new FrameDuration(1, 10) : ToFrameDuration(fixture.GetDuration(i));
        }

        if (fixture.Expected.Animation is { } animation)
        {
            image.Animation = new AnimationMetadata { TotalPlays = animation.TotalPlays is > AnimationTotalPlaysLimit ? null : animation.TotalPlays };
        }

        foreach (var encoder in Encoders)
        {
            using var stream = new MemoryStream();
            image.Save(stream, encoder);
            var context = $"{id} as {pixelFormat} (max colors {encoder.MaxColors}, {encoder.Dithering}, {encoder.AlphaMode}, interlaced {encoder.Interlaced})";
            var verification = GifOutputVerifier.Verify(stream.ToArray(), image, encoder, context: context);
            for (var i = 0; i < frames.Count; i++)
            {
                if (verification.Errors[i] is { } error)
                {
                    AssertQuality(error, encoder, $"{context}, frame {i}");
                }
            }

            // The library decoder reads the output like the reference reader
            using var decoded = Image.Load<Rgba32>(stream.ToArray());
            for (var i = 0; i < frames.Count; i++)
            {
                AssertSame(verification.Displayed[i], ImageSnapshots.CaptureFrame(decoded.Frames[i]), $"{context}, frame {i} decoded by the library");
            }

            // Deterministic: the same input and settings give the same bytes
            using var again = new MemoryStream();
            image.Save(again, encoder);
            Assert.Equal(stream.ToArray(), again.ToArray());
        }
    }

    [Theory]
    [MemberData(nameof(GifFixtures))]
    public void EditedCorpusGifsSaveTheIntendedDisplayedFrames(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var count = fixture.Expected.FrameCount;
        using var image = Image.Load(fixture.ReadInput());

        // Reorder: the last frame first; remove the (new) second frame when there are three or more
        var order = Enumerable.Range(0, count).ToList();
        if (count > 1)
        {
            image.MoveFrame(count - 1, 0);
            order.Insert(0, order[^1]);
            order.RemoveAt(order.Count - 1);
        }

        if (count > 2)
        {
            image.RemoveFrame(1);
            order.RemoveAt(1);
        }

        var expected = order.Select(index => fixture.GetFrame(index, RawPixelLayout.Rgba8)).ToList();
        AssertSaved(image, expected, "reordered");

        // Opaque-to-transparent: the top-left quarter of the first frame becomes transparent, the next frame stays as decoded
        var (w, h) = (Math.Max(1, image.Width / 2), Math.Max(1, image.Height / 2));
        image.Frames[0].ProcessPixelBytes(pixels =>
        {
            for (var y = 0; y < h; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < w; x++)
                {
                    row[(4 * x) + 3] = 0;
                }
            }
        });

        var transparent = expected[0];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                transparent = transparent.WithSample(x, y, 0, 0).WithSample(x, y, 1, 0).WithSample(x, y, 2, 0).WithSample(x, y, 3, 0);
            }
        }

        expected[0] = transparent;
        AssertSaved(image, expected, "transparent region");
    }

    private const int AnimationTotalPlaysLimit = 65_536;

    /// <summary>
    /// Quantization criteria of quantized (not exactly representable) frames, measured on the opaque pixels of the corpus
    /// references: the palette must track the colors (bounded mean error and channel bias); dithering trades a larger
    /// per-pixel error for an unbiased average.
    /// </summary>
    private static void AssertQuality(ReconstructionError error, GifEncoder encoder, string context)
    {
        var (minimumPsnr, maximumMean, maximumBias) = (encoder.MaxColors, encoder.Dithering) switch
        {
            (256, GifDithering.None) => (33d, 4.5, 1d),
            (256, _) => (31d, 5.5, 1d),
            (64, _) => (22.5, 14d, 1.5),
            _ => (17.5, 27d, 3d),
        };

        Assert.True(error.Psnr >= minimumPsnr && error.MeanAbsoluteError <= maximumMean && error.MaxChannelBias <= maximumBias, $"{context}: {error.Describe()}");
    }

    private static void AssertSaved(Image image, List<RawPixelBuffer> expectedFrames, string step)
    {
        var encoder = new GifEncoder();
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        var verification = GifOutputVerifier.Verify(stream.ToArray(), image, encoder, context: step);
        for (var i = 0; i < expectedFrames.Count; i++)
        {
            // Corpus GIF frames are composited from palettes of at most 256 colors; a frame mixing several palettes may exceed
            // 256 colors, then only the transparency and the quantization error are checked (by the verifier)
            if (verification.Errors[i] is null)
            {
                AssertSame(expectedFrames[i], verification.Displayed[i], $"{step}, frame {i}");
            }
        }

        Assert.Equal(image.Animation is { } animation ? animation.TotalPlays : 1, verification.Reference.TotalPlays);
    }

    private static void AssertSame(RawPixelBuffer expected, RawPixelBuffer actual, string context)
    {
        var result = PixelBufferComparer.Compare(expected.WithTransparentColorsCleared(), actual.WithTransparentColorsCleared(), ComparisonPolicy.Exact, context);
        if (!result.IsMatch)
            throw new GoldenAssertionException(result.Describe());
    }

    private static FrameDuration ToFrameDuration(RationalDuration duration) => duration.Numerator == 0 ? FrameDuration.Zero : new FrameDuration(duration.Numerator, duration.Denominator);
}
