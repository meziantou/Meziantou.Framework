using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The basic processing operations checked against the independent raw references of the golden corpus.
/// Every reference frame and poster is imported (no codec involved), the operation is applied to the whole image, and
/// each frame and the poster are compared exactly, row by row, with the reference transformed by the test harness
/// (<see cref="RawPixelBuffer"/> crop/flip/rotate/transpose/transverse, which are written from the definitions and checked
/// against hand-written literals in <see cref="PixelBufferTests"/>). The expected output is never produced by the code
/// under test. 16-bit fixtures run with <see cref="Rgba64"/> (and <see cref="Gray16"/>), so low bits and alpha must
/// survive; animated fixtures and separate posters must transform consistently. Metadata expectations come from the
/// manifest and from the EXIF payloads that identification extracts from the corpus inputs.
/// </summary>
public sealed class ProcessingGoldenTests
{
    private static readonly string[] Operations =
    [
        "crop-inner", "crop-bottom-right", "rotate90", "rotate180", "rotate270", "flip-horizontal", "flip-vertical",
        "orient2", "orient3", "orient4", "orient5", "orient6", "orient7", "orient8",
    ];

    public static TheoryData<string, string, bool> Cases()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var (fixture, format) in CaseList())
        {
            data.Add(fixture.Id, format.ToString(), fixture.Expected.FrameCount > 1 || fixture.Expected.Poster is not null);
        }

        return data;
    }

    [Fact]
    public void CorpusCoversAnimationsPostersAndSixteenBitAlpha()
    {
        var cases = CaseList().Select(item => (item.Fixture.Id, item.Format)).ToArray();
        Assert.Contains(("apng/separate-poster", PixelFormat.Rgba32), cases);
        Assert.Contains(("apng/ffmpeg-rgba16", PixelFormat.Rgba64), cases);
        Assert.Contains(("png/rgba16-low-bit-gradient", PixelFormat.Rgba64), cases);
        Assert.Contains(("png/gray16-low-bit-gradient", PixelFormat.Gray16), cases);
        Assert.Contains(("png/rgba8-corner-markers", PixelFormat.Rgba32), cases);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void OperationsMatchTheTransformedReferences(string id, string format, bool animated)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        Assert.Equal(animated, fixture.Expected.FrameCount > 1 || fixture.Expected.Poster is not null);
        foreach (var operation in Operations)
        {
            foreach (var segmented in new[] { false, true })
            {
                switch (Enum.Parse<PixelFormat>(format))
                {
                    case PixelFormat.Rgba32: Check<Rgba32>(fixture, operation, segmented); break;
                    case PixelFormat.Rgba64: Check<Rgba64>(fixture, operation, segmented); break;
                    case PixelFormat.Gray16: Check<Gray16>(fixture, operation, segmented); break;
                    default: throw new ArgumentOutOfRangeException(nameof(format));
                }
            }
        }
    }

    [Fact]
    public void AutoOrientReconcilesTheMetadataOfAPngWithExifThumbnailAndProfiles()
    {
        // 3x2 RGB, orientation 8 (manifest), EXIF with PixelXDimension/PixelYDimension and an IFD1 thumbnail, RGB ICC profile
        var fixture = GoldenCorpus.Default.Get("png/metadata-rgb8-profiles");
        Assert.Equal(8, fixture.Expected.Orientation);
        var reference = RawImport.DropOpaqueAlpha(fixture.GetFrame(0, RawPixelLayout.Rgba8));
        var metadata = Image.Identify(fixture.ReadInput()).Metadata;
        Assert.Equal(ExifOrientation.LeftBottom, metadata.Orientation);
        Assert.True(ExifTiff.HasThumbnail(metadata.ExifProfile!.Data.Span));
        Assert.Equal(((uint?)3, (uint?)2), ExifTiff.ReadPixelDimensions(metadata.ExifProfile.Data.Span));

        // Rotation: stored-pixel coordinates, the typed and serialized orientation are kept
        using (var image = Build<Rgb24>(reference, metadata))
        {
            image.Rotate(RotateMode.Rotate90, TestContext.Current.CancellationToken);
            AssertExact(reference.Rotate90Clockwise(), ImageSnapshots.CaptureFrame(image.Frames[0]), "rotate90");
            AssertMetadata(image, ExifOrientation.LeftBottom, metadata, (2, 3));
        }

        // Auto-orient of orientation 8 (0th row = visual left, 0th column = visual bottom) is a counterclockwise quarter turn
        using (var image = Build<Rgb24>(reference, metadata))
        {
            image.AutoOrient(TestContext.Current.CancellationToken);
            AssertExact(reference.Rotate90CounterClockwise(), ImageSnapshots.CaptureFrame(image.Frames[0]), "auto-orient");
            AssertMetadata(image, ExifOrientation.TopLeft, metadata, (2, 3));
        }

        // Crop: dimension tags follow the canvas
        using (var image = Build<Rgb24>(reference, metadata))
        {
            image.Crop(new Rectangle(1, 0, 2, 1), TestContext.Current.CancellationToken);
            AssertExact(reference.Crop(1, 0, 2, 1), ImageSnapshots.CaptureFrame(image.Frames[0]), "crop");
            AssertMetadata(image, ExifOrientation.LeftBottom, metadata, (2, 1));
        }

        // Pixel-only operations: the thumbnail is stale, dimensions are unchanged, the RGB profile stays compatible
        using (var image = Build<Rgb24>(reference, metadata))
        {
            image.Flip(FlipMode.Horizontal, TestContext.Current.CancellationToken);
            image.Grayscale(TestContext.Current.CancellationToken);
            AssertExact(Luma(reference.FlipHorizontal()), ImageSnapshots.CaptureFrame(image.Frames[0]), "flip + grayscale");
            AssertMetadata(image, ExifOrientation.LeftBottom, metadata, (3, 2));
        }
    }

    [Fact]
    public void AutoOrientOfTheExifOrientationSixJpegReference()
    {
        var fixture = GoldenCorpus.Default.Get("jpeg/exif-orientation-6");
        Assert.Equal(6, fixture.Expected.Orientation);
        var reference = fixture.GetFrame(0, RawPixelLayout.Rgba8);
        var metadata = Image.Identify(fixture.ReadInput()).Metadata;
        Assert.Equal(ExifOrientation.RightTop, metadata.Orientation);

        using var image = Build<Rgba32>(reference, metadata);
        image.AutoOrient(TestContext.Current.CancellationToken);

        // Orientation 6 (0th row = visual right, 0th column = visual top) is a clockwise quarter turn
        AssertExact(reference.Rotate90Clockwise(), ImageSnapshots.CaptureFrame(image.Frames[0]), "auto-orient");
        Assert.Equal(new Size(fixture.Expected.Height, fixture.Expected.Width), image.Size);
        Assert.Equal(ExifOrientation.TopLeft, image.Metadata.Orientation);
        Assert.Equal(ExifOrientation.TopLeft, ExifTiff.ReadOrientation(image.Metadata.ExifProfile!.Data.Span));
        Assert.Equal(fixture.Expected.Profiles.Exif!.Length, image.Metadata.ExifProfile.Data.Length); // patched in place
    }

    private static IEnumerable<(GoldenFixture Fixture, PixelFormat Format)> CaseList()
    {
        foreach (var fixture in GoldenCorpus.Default.Fixtures.Where(fixture => fixture.IsValid))
        {
            yield return (fixture, fixture.CanonicalLayout == RawPixelLayout.Rgba16Le ? PixelFormat.Rgba64 : PixelFormat.Rgba32);
            if (fixture.Layouts.Contains(RawPixelLayout.Gray16Le))
            {
                yield return (fixture, PixelFormat.Gray16);
            }
        }
    }

    private static void Check<TPixel>(GoldenFixture fixture, string operation, bool segmented)
        where TPixel : unmanaged
    {
        var format = PixelFormats.GetPixelFormat<TPixel>();
        var layout = ImageSnapshots.GetLayout(format);
        var expected = fixture.Expected;
        var width = expected.Width;
        var height = expected.Height;
        var frames = Enumerable.Range(0, expected.FrameCount).Select(i => fixture.GetFrame(i, layout)).ToArray();
        var poster = expected.Poster is null ? null : fixture.GetPoster(layout);

        using var image = BuildAnimated<TPixel>(fixture, frames, poster, segmented);
        var frameObjects = ((IEnumerable<ImageFrame<TPixel>>)image.Frames).ToArray();
        var posterObject = image.PosterFrame;
        var token = TestContext.Current.CancellationToken;

        // The crop rectangles are inside the canvas by construction: an inner rectangle and the bottom-right pixel
        var innerX = width > 1 ? 1 : 0;
        var innerY = height > 1 ? 1 : 0;
        var innerWidth = Math.Max(1, width - innerX - (width > 2 ? 1 : 0));
        var innerHeight = Math.Max(1, height - innerY - (height > 2 ? 1 : 0));
        Func<RawPixelBuffer, RawPixelBuffer> reference;
        switch (operation)
        {
            case "crop-inner":
                image.Crop(new Rectangle(innerX, innerY, innerWidth, innerHeight), token);
                reference = buffer => buffer.Crop(innerX, innerY, innerWidth, innerHeight);
                break;
            case "crop-bottom-right":
                image.Crop(new Rectangle(width - 1, height - 1, 1, 1), token);
                reference = buffer => buffer.Crop(width - 1, height - 1, 1, 1);
                break;
            case "rotate90":
                image.Rotate(RotateMode.Rotate90, token);
                reference = buffer => buffer.Rotate90Clockwise();
                break;
            case "rotate180":
                image.Rotate(RotateMode.Rotate180, token);
                reference = buffer => buffer.Rotate180();
                break;
            case "rotate270":
                image.Rotate(RotateMode.Rotate270, token);
                reference = buffer => buffer.Rotate90CounterClockwise();
                break;
            case "flip-horizontal":
                image.Flip(FlipMode.Horizontal, token);
                reference = buffer => buffer.FlipHorizontal();
                break;
            case "flip-vertical":
                image.Flip(FlipMode.Vertical, token);
                reference = buffer => buffer.FlipVertical();
                break;
            default:
                var orientation = (ExifOrientation)int.Parse(operation["orient".Length..], CultureInfo.InvariantCulture);
                image.Metadata.Orientation = orientation;
                image.AutoOrient(token);
                Assert.Equal(ExifOrientation.TopLeft, image.Metadata.Orientation);
                reference = orientation switch
                {
                    ExifOrientation.TopRight => buffer => buffer.FlipHorizontal(),
                    ExifOrientation.BottomRight => buffer => buffer.Rotate180(),
                    ExifOrientation.BottomLeft => buffer => buffer.FlipVertical(),
                    ExifOrientation.LeftTop => buffer => buffer.Transpose(),
                    ExifOrientation.RightTop => buffer => buffer.Rotate90Clockwise(),
                    ExifOrientation.RightBottom => buffer => buffer.Transverse(),
                    ExifOrientation.LeftBottom => buffer => buffer.Rotate90CounterClockwise(),
                    _ => throw new InvalidOperationException(operation),
                };
                break;
        }

        var context = $"Fixture '{fixture.Id}' ({format}, {operation}, segmented: {segmented})";
        var expectedFirst = reference(frames[0]);
        Assert.Equal(new Size(expectedFirst.Width, expectedFirst.Height), image.Size);
        Assert.Equal(frames.Length, image.Frames.Count);
        for (var i = 0; i < frames.Length; i++)
        {
            Assert.Same(frameObjects[i], image.Frames[i]);
            Assert.Equal(fixture.GetDuration(i), ImageSnapshots.ToRational(frameObjects[i].Metadata.Duration));
            AssertExact(reference(frames[i]), ImageSnapshots.CaptureFrameRows(frameObjects[i]), string.Create(CultureInfo.InvariantCulture, $"{context}, frame {i}"));
        }

        Assert.Same(posterObject, image.PosterFrame);
        if (poster is not null)
        {
            AssertExact(reference(poster), ImageSnapshots.CaptureFrameRows(image.PosterFrame!), context + ", poster");
        }
    }

    private static Image<TPixel> BuildAnimated<TPixel>(GoldenFixture fixture, RawPixelBuffer[] frames, RawPixelBuffer? poster, bool segmented)
        where TPixel : unmanaged
    {
        var format = PixelFormats.GetPixelFormat<TPixel>();
        var first = frames[0];
        var layoutOptions = segmented ? new PixelStorageLayoutOptions { RowAlignment = 16, TargetSlabBytes = 2 * ((first.RowBytes + 15) / 16 * 16) } : null;
        var image = Image.ImportPixelBytesCore<TPixel>(RawImport.ToPixelBytes(first, format), first.Width, first.Height, first.RowBytes, ImageConfiguration.Default, layoutOptions);
        try
        {
            image.Frames[0].Metadata.Duration = ToFrameDuration(fixture.GetDuration(0));
            for (var i = 1; i < frames.Length; i++)
            {
                using var single = Image.ImportPixelBytes<TPixel>(RawImport.ToPixelBytes(frames[i], format), first.Width, first.Height);
                single.Frames[0].Metadata.Duration = ToFrameDuration(fixture.GetDuration(i));
                image.AppendFrame(single.Frames[0]);
            }

            if (poster is not null)
            {
                using var single = Image.ImportPixelBytes<TPixel>(RawImport.ToPixelBytes(poster, format), first.Width, first.Height);
                image.SetPosterFrame(single.Frames[0]);
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static Image<TPixel> Build<TPixel>(RawPixelBuffer reference, ImageMetadata metadata)
        where TPixel : unmanaged
    {
        var image = Image.ImportPixelBytes<TPixel>(RawImport.ToPixelBytes(reference, PixelFormats.GetPixelFormat<TPixel>()), reference.Width, reference.Height);
        image.Metadata.Orientation = metadata.Orientation;
        image.Metadata.ExifProfile = metadata.ExifProfile;
        image.Metadata.IccProfile = metadata.IccProfile;
        image.Metadata.XmpProfile = metadata.XmpProfile;
        return image;
    }

    private static void AssertMetadata(Image image, ExifOrientation orientation, ImageMetadata source, (uint Width, uint Height) dimensions)
    {
        Assert.Equal(orientation, image.Metadata.Orientation);
        var exif = image.Metadata.ExifProfile!.Data.Span;
        Assert.True(ExifTiff.IsValid(exif));
        Assert.Equal(orientation, ExifTiff.ReadOrientation(exif));
        Assert.Equal(((uint?)dimensions.Width, (uint?)dimensions.Height), ExifTiff.ReadPixelDimensions(exif));
        Assert.False(ExifTiff.HasThumbnail(exif));
        Assert.Same(source.IccProfile, image.Metadata.IccProfile);
        Assert.Same(source.XmpProfile, image.Metadata.XmpProfile);
    }

    /// <summary>The normative Rec. 709 rule of the library on 8-bit encoded values: Y = (2126 R + 7152 G + 722 B + 5000) / 10000.</summary>
    private static RawPixelBuffer Luma(RawPixelBuffer source)
    {
        var builder = new RawPixelBufferBuilder(source.Width, source.Height, source.Layout);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var luma = ((2126 * source.GetSample(x, y, 0)) + (7152 * source.GetSample(x, y, 1)) + (722 * source.GetSample(x, y, 2)) + 5000) / 10000;
                for (var channel = 0; channel < 3; channel++)
                {
                    builder.SetSample(x, y, channel, luma);
                }
            }
        }

        return builder.Build();
    }

    private static void AssertExact(RawPixelBuffer expected, RawPixelBuffer actual, string context)
    {
        var result = PixelBufferComparer.Compare(expected, actual, ComparisonPolicy.Exact, context);
        if (!result.IsMatch)
            throw new GoldenAssertionException(result.Describe());
    }

    private static FrameDuration ToFrameDuration(RationalDuration duration) => duration.Numerator == 0 ? FrameDuration.Zero : new FrameDuration(duration.Numerator, duration.Denominator);
}
