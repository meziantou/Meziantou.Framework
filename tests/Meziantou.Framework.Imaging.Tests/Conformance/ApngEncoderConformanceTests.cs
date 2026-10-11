using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Png;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// APNG encoding of the golden corpus, tool-free. (1) Every raw reference set (displayed frames and separate
/// poster of every valid fixture, in every pixel format that holds them exactly) is imported as an animation with the
/// fixture's durations and play count, encoded with several settings, and verified by the independent harness reader
/// (<see cref="ApngOutputVerifier"/>) against the same references. (2) Every corpus APNG is decoded, edited (frames moved and
/// removed, opaque regions made transparent, the poster removed or replaced, resized) and saved: the encoded displayed frames
/// must be the corpus references permuted by the edit (independently of the source file's regions, blending and disposal),
/// or, after a resize, the intended in-memory frames. FFmpeg decodes the same kinds of output in
/// <c>InteropTests/ApngEncoderInteropTests</c>.
/// </summary>
public sealed class ApngEncoderConformanceTests
{
    private static readonly PngEncoder[] Encoders =
    [
        new PngEncoder { AnimationMode = PngAnimationMode.Animated },
        new PngEncoder { AnimationMode = PngAnimationMode.Animated, Interlaced = true, Filter = PngFilter.Paeth },
        new PngEncoder { AnimationMode = PngAnimationMode.Animated, Filter = PngFilter.None, CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression },
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

    public static TheoryData<string> ApngFixtures => [.. GoldenCorpus.Default.GetIds(format: "png", kind: FixtureKinds.Valid, feature: "apng")];

    public static TheoryData<string> InvalidApngFixtures => [.. GoldenCorpus.Default.GetIds(format: "png", kind: FixtureKinds.Invalid).Where(id => id.Contains("/apng-", StringComparison.Ordinal))];

    [Theory]
    [MemberData(nameof(ApngFixtures))]
    public void ReferenceReaderAgreesWithTheCorpusAnimations(string id)
    {
        // The harness reader is the oracle of the encoder tests: it must read every non-palette corpus APNG like the corpus
        var fixture = GoldenCorpus.Default.Get(id);
        var data = fixture.ReadInput();
        if (data.AsSpan().IndexOf("PLTE"u8) >= 0)
        {
            Assert.Throws<InvalidDataException>(() => ReferencePng.Parse(data)); // palettes are outside the reader's scope
            return;
        }

        var reference = ReferencePng.Parse(data);
        if (reference is { ColorType: 2, BitDepth: 16 })
        {
            Assert.Throws<NotSupportedException>(() => reference.Layout); // 16-bit RGB is outside the reader's scope (never written by the encoder)
            return;
        }

        Assert.Equal(fixture.Expected.FrameCount, reference.Animation!.NumFrames);
        Assert.Equal(fixture.Expected.Poster is not null, reference.Animation.HasSeparatePoster);
        Assert.Equal(fixture.Expected.Animation!.TotalPlays ?? 0, reference.Animation.NumPlays);
        for (var i = 0; i < reference.Animation.Frames.Count; i++)
        {
            var frame = reference.Animation.Frames[i];
            Assert.Equal(fixture.GetDuration(i), RationalDuration.Create(frame.DelayNumerator, frame.DelayDenominator == 0 ? 100 : frame.DelayDenominator));
            _ = reference.DecodeFrame(i); // every region datastream decodes
            if (frame.IsFullCanvasSource(reference.Width, reference.Height) && i == 0 && fixture.Layouts.Contains(reference.Layout))
            {
                AssertSame(fixture.GetFrame(0, reference.Layout), reference.DecodeDisplayedFrame(0), id + " frame 0");
            }
        }

        if (fixture.Expected.Poster is not null && fixture.Layouts.Contains(reference.Layout))
        {
            AssertSame(fixture.GetPoster(reference.Layout), reference.DecodePoster(), id + " poster");
        }
    }

    [Theory]
    [MemberData(nameof(InvalidApngFixtures))]
    public void ReferenceReaderRejectsTheCorpusApngErrors(string id)
    {
        // Control-data errors fail the structure check; image-data errors fail when the frame datastream is decoded
        var reference = default(ReferencePng);
        try
        {
            reference = ReferencePng.Parse(GoldenCorpus.Default.Get(id).ReadInput());
        }
        catch (InvalidDataException)
        {
            return;
        }

        Assert.Throws<InvalidDataException>(() =>
        {
            for (var i = 0; i < reference.Animation!.Frames.Count; i++)
            {
                _ = reference.DecodeFrame(i);
            }
        });
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CorpusReferencesEncodeAsAnimations(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        var stills = EncoderSources.GetStills(fixture, pixelFormat);
        var frames = stills.Where(still => still.Name != "poster").ToList();
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

        if (stills.FirstOrDefault(still => still.Name == "poster") is { Pixels: { } posterPixels })
        {
            using var poster = EncoderSources.CreateImage(posterPixels, pixelFormat);
            image.SetPosterFrame(poster.Frames[0]);
        }

        image.Animation = new AnimationMetadata { TotalPlays = fixture.Expected.Animation?.TotalPlays };
        foreach (var encoder in Encoders)
        {
            using var stream = new MemoryStream();
            image.Save(stream, encoder);
            var context = $"{id} as {pixelFormat} (filter {encoder.Filter}, {encoder.CompressionLevel}, interlaced {encoder.Interlaced})";
            ApngOutputVerifier.Verify(stream.ToArray(), image, context: context);

            // The verifier compares with the imported pixels; compare with the corpus references themselves too
            var reference = ReferencePng.Parse(stream.ToArray());
            for (var i = 0; i < frames.Count; i++)
            {
                AssertSame(frames[i].Pixels, reference.DecodeDisplayedFrame(i), $"{context}, {frames[i].Name}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(ApngFixtures))]
    public void EditedCorpusAnimationsSaveTheIntendedDisplayedFrames(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var layout = fixture.CanonicalLayout;
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

        var expected = order.Select(index => fixture.GetFrame(index, layout)).ToList();
        AssertSaved(fixture, image, expected, fixture.Expected.Poster is null ? null : fixture.GetPoster(layout), "reordered");

        // Opaque-to-transparent: the top-left quarter of frame 0 becomes fully transparent (its colors kept); the poster is removed
        var (w, h) = (Math.Max(1, image.Width / 2), Math.Max(1, image.Height / 2));
        ClearAlpha(image.Frames[0], w, h);
        var transparent = expected[0];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                transparent = transparent.WithSample(x, y, 3, 0);
            }
        }

        expected[0] = transparent;
        image.RemovePosterFrame();
        AssertSaved(fixture, image, expected, poster: null, "transparent region, poster removed");

        // A new poster, then a resize of every frame and the poster: the saved frames are the resized in-memory frames
        using (var poster = image.CloneFrame(image.Frames.Count - 1))
        {
            image.SetPosterFrame(poster.Frames[0]);
        }

        image.Resize(new ResizeOptions(image.Width + 2, (image.Height * 2) + 1) { Mode = ResizeMode.Stretch }, XunitCancellationToken);
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        ApngOutputVerifier.Verify(stream.ToArray(), image, context: id + " resized");
    }

    private static void AssertSaved(GoldenFixture fixture, Image image, List<RawPixelBuffer> expectedFrames, RawPixelBuffer? poster, string step)
    {
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        var context = $"{fixture.Id} {step}";
        var reference = ApngOutputVerifier.Verify(stream.ToArray(), image, context: context);
        Assert.Equal(expectedFrames.Count, reference.Animation!.NumFrames);
        Assert.Equal(fixture.Expected.Animation!.TotalPlays ?? 0, reference.Animation.NumPlays);
        for (var i = 0; i < expectedFrames.Count; i++)
        {
            AssertSame(expectedFrames[i], reference.DecodeDisplayedFrame(i), $"{context}, frame {i}");
        }

        Assert.Equal(poster is not null, reference.Animation.HasSeparatePoster);
        if (poster is not null)
        {
            AssertSame(poster, reference.DecodePoster(), context + ", poster");
        }
    }

    private static void ClearAlpha(ImageFrame frame, int width, int height)
    {
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(frame.PixelFormat);
        var alphaBytes = bytesPerPixel / 4;
        frame.ProcessPixelBytes(pixels =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    row.Slice((x * bytesPerPixel) + (3 * alphaBytes), alphaBytes).Clear();
                }
            }
        });
    }

    private static void AssertSame(RawPixelBuffer expected, RawPixelBuffer actual, string context)
    {
        var result = PixelBufferComparer.Compare(expected, actual, ComparisonPolicy.Exact, context);
        if (!result.IsMatch)
            throw new GoldenAssertionException(result.Describe());
    }

    private static FrameDuration ToFrameDuration(RationalDuration duration) => duration.Numerator == 0 ? FrameDuration.Zero : new FrameDuration(duration.Numerator, duration.Denominator);
}
