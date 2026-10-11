using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.AutoCrop;
using Meziantou.Framework.Imaging.TestHarness.Convolution;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Resampling;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Whole-image operations on <em>decoded</em> APNGs: every corpus APNG is loaded with the library decoder, then
/// cropped, auto-cropped, rotated, resized, convolved and reordered. Each displayed frame and the separate poster must stay a full-canvas image of the
/// same size and pixel format, frame identities, durations, the poster and the animation settings are preserved (or follow
/// the moved frame), and the pixels equal the corpus references transformed by the independent harness
/// (<see cref="RawPixelBuffer"/> geometry, <see cref="ReferenceAutoCrop"/>, <see cref="ReferenceResampler"/>,
/// <see cref="ReferenceConvolver"/>).
/// </summary>
public sealed class ApngProcessingConformanceTests
{
    public static TheoryData<string> Fixtures => [.. GoldenCorpus.Default.GetIds(format: "png", kind: FixtureKinds.Valid, feature: "apng")];

    [Fact]
    public void CorpusCoversPostersAndSixteenBitAnimations()
    {
        var fixtures = GoldenCorpus.Default.GetIds(format: "png", kind: FixtureKinds.Valid, feature: "apng").Select(GoldenCorpus.Default.Get).ToArray();
        Assert.Contains(fixtures, fixture => fixture.Expected.Poster is not null && fixture.CanonicalLayout == RawPixelLayout.Rgba16Le);
        Assert.Contains(fixtures, fixture => fixture.Expected.Poster is not null && fixture.Expected.FrameCount == 1);
        Assert.Contains(fixtures, fixture => fixture.Expected.Poster is null && fixture.Expected.FrameCount > 3);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void GeometryKeepsEveryFrameAndThePosterConsistent(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var expected = fixture.Expected;
        var (width, height) = (expected.Width, expected.Height);
        var crop = new Rectangle(width > 1 ? 1 : 0, 0, Math.Max(1, width - 1), Math.Max(1, height - (height > 2 ? 1 : 0)));
        Check(fixture, "crop", image => image.Crop(crop, XunitCancellationToken), buffer => buffer.Crop(crop.X, crop.Y, crop.Width, crop.Height));
        Check(fixture, "rotate90", image => image.Rotate(RotateMode.Rotate90, XunitCancellationToken), buffer => buffer.Rotate90Clockwise());

        // The box is the union over the displayed frames and the poster; the reference decides whether there is one
        var buffers = Enumerable.Range(0, expected.FrameCount).Select(i => fixture.GetFrame(i, fixture.CanonicalLayout)).ToList();
        if (expected.Poster is not null)
        {
            buffers.Add(fixture.GetPoster(fixture.CanonicalLayout));
        }

        var autoCrop = new ReferenceAutoCropOptions(PaddingX: 2, PaddingY: 1);
        var analysis = ReferenceAutoCrop.Analyze(buffers, autoCrop);
        var changes = !ReferenceEquals(ReferenceAutoCrop.Apply(buffers[0], analysis, autoCrop), buffers[0]);
        Check(fixture, "auto-crop", image => Assert.Equal(changes, image.AutoCrop(new AutoCropOptions { PaddingX = 2, PaddingY = 1 }, XunitCancellationToken)), buffer => ReferenceAutoCrop.Apply(buffer, analysis, autoCrop));
        Check(fixture, "flip", image => image.Flip(FlipMode.Vertical, XunitCancellationToken), buffer => buffer.FlipVertical());

        var resize = new ReferenceResizeOptions((width * 2) + 1, height + 2, ReferenceResizeMode.Stretch, ReferenceKernel.CatmullRom);
        using var image = Load(fixture);
        var frames = ((IEnumerable<ImageFrame>)image.Frames).ToArray();
        image.Resize(new ResizeOptions(resize.TargetWidth, resize.TargetHeight) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bicubic }, XunitCancellationToken);
        AssertInvariants(fixture, image, frames, "resize");
        for (var i = 0; i < frames.Length; i++)
        {
            AssertFiltered(ReferenceResampler.Resize(fixture.GetFrame(i, fixture.CanonicalLayout), resize), image.Frames[i], $"{id} resize, frame {i}");
        }

        if (image.PosterFrame is { } poster)
        {
            AssertFiltered(ReferenceResampler.Resize(fixture.GetPoster(fixture.CanonicalLayout), resize), poster, id + " resize, poster");
        }

        // In place: the frames keep their storage as well as their identity
        var convolution = new ReferenceConvolutionOptions(3, 3, [0, -0.25m, 0, -0.25m, 2, -0.25m, 0, -0.25m, 0], ReferenceEdgeMode.Mirror);
        using var convolved = Load(fixture);
        var convolvedFrames = ((IEnumerable<ImageFrame>)convolved.Frames).ToArray();
        convolved.Convolve(new ConvolutionOptions(new ConvolutionKernel(3, 3, [.. convolution.Weights.Select(weight => (double)weight)])) { EdgeMode = ConvolutionEdgeMode.Mirror }, XunitCancellationToken);
        AssertInvariants(fixture, convolved, convolvedFrames, "convolve");
        Assert.Equal(new Size(width, height), convolved.Size);
        for (var i = 0; i < convolvedFrames.Length; i++)
        {
            AssertFiltered(ReferenceConvolver.Convolve(fixture.GetFrame(i, fixture.CanonicalLayout), convolution), convolved.Frames[i], $"{id} convolve, frame {i}");
        }

        if (convolved.PosterFrame is { } convolvedPoster)
        {
            AssertFiltered(ReferenceConvolver.Convolve(fixture.GetPoster(fixture.CanonicalLayout), convolution), convolvedPoster, id + " convolve, poster");
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ReorderingAndExtractionKeepTheModelInvariants(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var layout = fixture.CanonicalLayout;
        var count = fixture.Expected.FrameCount;
        using var image = Load(fixture);
        var frames = ((IEnumerable<ImageFrame>)image.Frames).ToArray();

        // Move the first frame to the end: frame objects keep their identity, pixels and durations follow them
        image.MoveFrame(0, count - 1);
        Assert.Same(frames[0], image.Frames[count - 1]);
        for (var i = 0; i < count; i++)
        {
            var source = (i + 1) % count;
            AssertFrame(fixture.GetFrame(source, layout), image.Frames[i], $"{id} moved, frame {i}");
            Assert.Equal(ToFrameDuration(fixture.GetDuration(source)), image.Frames[i].Metadata.Duration);
        }

        Assert.Equal(fixture.Expected.Poster is not null, image.PosterFrame is not null);
        Assert.True(image.IsAnimated);
        Assert.NotNull(image.Animation);
        Assert.Equal(fixture.Expected.Animation!.TotalPlays, image.Animation.TotalPlays);

        // Extracted frames are still images; the extracted poster too
        using (var still = image.CloneFrame(count - 1))
        {
            Assert.False(still.IsAnimated);
            Assert.Null(still.PosterFrame);
            AssertFrame(fixture.GetFrame(0, layout), still.Frames[0], id + " clone of frame 0");
            Assert.Equal(ToFrameDuration(fixture.GetDuration(0)), still.Frames[0].Metadata.Duration);
        }

        if (fixture.Expected.Poster is not null)
        {
            using var poster = image.ClonePosterFrame();
            AssertFrame(fixture.GetPoster(layout), poster.Frames[0], id + " poster clone");
            Assert.True(image.RemovePosterFrame());
            Assert.Null(image.PosterFrame);
            Assert.True(image.IsAnimated); // animation settings stay
        }

        // Removing frames down to one keeps the animation settings
        while (image.Frames.Count > 1)
        {
            image.RemoveFrame(0);
        }

        Assert.NotNull(image.Animation);
        AssertFrame(fixture.GetFrame(0, layout), image.Frames[0], id + " last remaining frame");
    }

    private static void Check(GoldenFixture fixture, string name, Action<Image> operation, Func<RawPixelBuffer, RawPixelBuffer> reference)
    {
        using var image = Load(fixture);
        var frames = ((IEnumerable<ImageFrame>)image.Frames).ToArray();
        operation(image);
        AssertInvariants(fixture, image, frames, name);
        for (var i = 0; i < frames.Length; i++)
        {
            AssertFrame(reference(fixture.GetFrame(i, fixture.CanonicalLayout)), image.Frames[i], $"{fixture.Id} {name}, frame {i}");
        }

        if (image.PosterFrame is { } poster)
        {
            AssertFrame(reference(fixture.GetPoster(fixture.CanonicalLayout)), poster, $"{fixture.Id} {name}, poster");
        }
    }

    private static Image Load(GoldenFixture fixture)
    {
        var image = Image.Load(fixture.ReadInput());
        Assert.Equal(fixture.CanonicalLayout == RawPixelLayout.Rgba16Le ? PixelFormat.Rgba64 : PixelFormat.Rgba32, image.PixelFormat);
        return image;
    }

    private static void AssertInvariants(GoldenFixture fixture, Image image, ImageFrame[] frames, string name)
    {
        Assert.Equal(fixture.Expected.FrameCount, image.Frames.Count);
        Assert.Equal(fixture.Expected.Poster is not null, image.PosterFrame is not null);
        Assert.Equal(fixture.Expected.Animation!.TotalPlays, image.Animation?.TotalPlays);
        for (var i = 0; i < frames.Length; i++)
        {
            Assert.Same(frames[i], image.Frames[i]);
            Assert.Equal(image.Size, new Size(image.Frames[i].Width, image.Frames[i].Height));
            Assert.Equal(image.PixelFormat, image.Frames[i].PixelFormat);
            Assert.Equal(ToFrameDuration(fixture.GetDuration(i)), image.Frames[i].Metadata.Duration);
        }

        if (image.PosterFrame is { } poster)
        {
            Assert.True(new Size(poster.Width, poster.Height) == image.Size, $"{fixture.Id} {name}: the poster must have the canvas size.");
            Assert.Equal(image.PixelFormat, poster.PixelFormat);
        }
    }

    private static void AssertFrame(RawPixelBuffer expected, ImageFrame frame, string context)
    {
        var result = PixelBufferComparer.Compare(expected, ImageSnapshots.CaptureFrame(frame), ComparisonPolicy.Exact, context);
        if (!result.IsMatch)
            throw new GoldenAssertionException(result.Describe());
    }

    private static void AssertFiltered(ReferenceFilterResult reference, ImageFrame frame, string context)
    {
        var result = reference.Compare(ImageSnapshots.CaptureFrame(frame), context);
        if (!result.IsMatch)
            throw new GoldenAssertionException(result.Describe());
    }

    private static FrameDuration ToFrameDuration(RationalDuration duration) => duration.Numerator == 0 ? FrameDuration.Zero : new FrameDuration(duration.Numerator, duration.Denominator);
}
