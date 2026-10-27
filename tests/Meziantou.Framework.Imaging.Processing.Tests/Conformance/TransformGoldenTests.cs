using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Resampling;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Rotations, skews, affine and projective transformations checked against the independent high-precision reference of
/// the test harness (<see cref="ReferenceTransformer"/>: decimal geometry, kernels, weights and direct two-dimensional
/// sums, the pixels beyond the edges being the background), over every raw reference of the golden corpus (frames and
/// posters, 8- and 16-bit, alpha, odd sizes) and over the synthetic sources of <see cref="ResizeGoldenTests"/>.
/// </summary>
/// <remarks>
/// Tolerance: none beyond rounding, as for resize. Each sample must be the correctly rounded exact value (ties upward);
/// only when the exact value lies within <see cref="ReferenceFilterResult.TieWindow"/> of a rounding boundary is the other
/// neighbor accepted. The geometry of the library is computed in double precision (its sines and cosines too), which moves
/// a weight by about 1e-15: far below that window. Filtered alpha follows the same rule and a zero output alpha requires
/// transparent black; nearest neighbor must copy the source pixel exactly.
/// </remarks>
public sealed class TransformGoldenTests
{
    // An opaque color whose 8-bit samples are exact (multiples of 257), and an opaque gray for the gray formats
    private static readonly Rgba64 Colored = new(0x3333, 0x9999, 0xCCCC, 0xFFFF);
    private static readonly Rgba64 Gray = new(0x6666, 0x6666, 0x6666, 0xFFFF);
    private static readonly Rgba64 Translucent = new(0xFFFF, 0x0000, 0x8080, 0x8080);

    private static readonly TransformCase[] Cases =
    [
        new("rotate-30-expand-bicubic", (image, options) => image.Rotate(30, options, Ct), (w, h) => Centered(w, h, ReferenceMatrix.CreateRotation(30)), ReferenceCanvasMode.Expand, ReferenceKernel.CatmullRom, Centered: true),
        new("rotate-minus-17.5-keep-bilinear-colored", (image, options) => image.Rotate(-17.5, options, Ct), (w, h) => Centered(w, h, ReferenceMatrix.CreateRotation(-17.5m)), ReferenceCanvasMode.Keep, ReferenceKernel.Triangle, Centered: true, Background: BackgroundKind.Colored),
        new("rotate-135-explicit-lanczos3", (image, options) => image.Rotate(135, options, Ct), (w, h) => Centered(w, h, ReferenceMatrix.CreateRotation(135)), ReferenceCanvasMode.Explicit, ReferenceKernel.Lanczos3, Centered: true, CanvasWidth: 9, CanvasHeight: 6),
        new("rotate-90-keep-bicubic", (image, options) => image.Rotate(90, options, Ct), (w, h) => Centered(w, h, ReferenceMatrix.CreateRotation(90)), ReferenceCanvasMode.Keep, ReferenceKernel.CatmullRom, Centered: true, Background: BackgroundKind.Colored),
        new("rotate-200-expand-bilinear", (image, options) => image.Rotate(200, options, Ct), (w, h) => Centered(w, h, ReferenceMatrix.CreateRotation(200)), ReferenceCanvasMode.Expand, ReferenceKernel.Triangle, Centered: true),

        // Nearest neighbor takes the pixel that contains a point: a point on the edge of two pixels in exact arithmetic may
        // land on either side in double precision. The coefficients of this transformation and of its inverse (its
        // determinant is 1) are dyadic, so both computations are exact
        new("affine-dyadic-keep-nearest", (image, options) => image.Transform(new AffineTransform(1, 0.5, -0.5, 0.75, 1.25, -0.75), options, Ct), (_, _) => ReferenceMatrix.CreateAffine(1, 0.5m, -0.5m, 0.75m, 1.25m, -0.75m), ReferenceCanvasMode.Keep, ReferenceKernel.Nearest, Background: BackgroundKind.Colored),
        new("affine-dyadic-expand-nearest", (image, options) => image.Transform(new AffineTransform(1, 0.5, -0.5, 0.75, 0, 0), options, Ct), (_, _) => ReferenceMatrix.CreateAffine(1, 0.5m, -0.5m, 0.75m, 0, 0), ReferenceCanvasMode.Expand, ReferenceKernel.Nearest),
        new("rotate-25-expand-linear-translucent", (image, options) => image.Rotate(25, options, Ct), (w, h) => Centered(w, h, ReferenceMatrix.CreateRotation(25)), ReferenceCanvasMode.Expand, ReferenceKernel.CatmullRom, Centered: true, Linear: true, Background: BackgroundKind.Translucent),
        new("skew-20-minus-10-expand-bicubic", (image, options) => image.Skew(20, -10, options, Ct), (w, h) => Centered(w, h, ReferenceMatrix.CreateSkew(20, -10)), ReferenceCanvasMode.Expand, ReferenceKernel.CatmullRom, Centered: true),
        new("affine-shrink-rotate-expand-lanczos3", (image, options) => image.Transform(new AffineTransform(0.375, 0.125, -0.25, 0.5, 3, -2), options, Ct), (_, _) => ReferenceMatrix.CreateAffine(0.375m, 0.125m, -0.25m, 0.5m, 3, -2), ReferenceCanvasMode.Expand, ReferenceKernel.Lanczos3),
        new("affine-enlarge-keep-bilinear", (image, options) => image.Transform(new AffineTransform(2.25, 0, 0.5, 1.75, -3.5, 1.25), options, Ct), (_, _) => ReferenceMatrix.CreateAffine(2.25m, 0, 0.5m, 1.75m, -3.5m, 1.25m), ReferenceCanvasMode.Keep, ReferenceKernel.Triangle, Background: BackgroundKind.Colored),
        new("projective-expand-bicubic", (image, options) => image.Transform(new ProjectiveTransform(1, 0.0625, 0.001953125, 0.125, 0.875, 0.0009765625, 2, 1, 1), options, Ct), (_, _) => new ReferenceMatrix(1, 0.0625m, 0.001953125m, 0.125m, 0.875m, 0.0009765625m, 2, 1, 1), ReferenceCanvasMode.Expand, ReferenceKernel.CatmullRom),
        new("projective-keep-lanczos3-colored", (image, options) => image.Transform(new ProjectiveTransform(0.75, -0.03125, -0.0078125, 0.0625, 1, 0.015625, 1, 0.5, 1), options, Ct), (_, _) => new ReferenceMatrix(0.75m, -0.03125m, -0.0078125m, 0.0625m, 1, 0.015625m, 1, 0.5m, 1), ReferenceCanvasMode.Keep, ReferenceKernel.Lanczos3, Background: BackgroundKind.Colored),
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private enum BackgroundKind
    {
        Default,
        Colored,
        Translucent,
    }

    public static TheoryData<string, string> CorpusCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (fixture, format) in ResizeGoldenTests.CaseList())
        {
            data.Add(fixture.Id, format.ToString());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CorpusCases))]
    public void TransformedFramesAndPostersMatchTheIndependentReference(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        foreach (var transformCase in Cases)
        {
            switch (Enum.Parse<PixelFormat>(format))
            {
                case PixelFormat.Rgba32: Check<Rgba32>(fixture, transformCase); break;
                case PixelFormat.Bgra32: Check<Bgra32>(fixture, transformCase); break;
                case PixelFormat.Rgb24: Check<Rgb24>(fixture, transformCase); break;
                case PixelFormat.Rgba64: Check<Rgba64>(fixture, transformCase); break;
                case PixelFormat.Gray8: Check<Gray8>(fixture, transformCase); break;
                case PixelFormat.Gray16: Check<Gray16>(fixture, transformCase); break;
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }
    }

    public static TheoryData<string> SyntheticSources => [.. ResizeGoldenTests.Synthetic().Select(item => item.Name)];

    [Theory]
    [MemberData(nameof(SyntheticSources))]
    public void SyntheticSourcesMatchTheIndependentReference(string name)
    {
        var source = ResizeGoldenTests.Synthetic().Single(item => item.Name == name).Buffer;
        foreach (var transformCase in Cases)
        {
            if (source.Layout == RawPixelLayout.Rgba16Le)
            {
                CheckSingle<Rgba64>(source, transformCase);
            }
            else
            {
                CheckSingle<Rgba32>(source, transformCase);
            }
        }
    }

    [Fact]
    public void AProjectiveTransformationAcrossItsHorizonMatchesTheIndependentReference()
    {
        // W = 1 - x / 12 vanishes inside the canvas: the pixels whose centers come from beyond the horizon are the
        // background, and the kernels near it are widened to their limit of 16
        var source = ResizeGoldenTests.Synthetic().Single(item => item.Name == "rgba8-alpha-ramp-transparent-edges").Buffer;
        var transformCase = new TransformCase(
            "projective-horizon-keep-bilinear",
            (image, options) => image.Transform(new ProjectiveTransform(1, 0, -0.08333333333333333, 0, 1, 0, 0, 0, 1), options, Ct),
            (_, _) => new ReferenceMatrix(1, 0, -0.08333333333333333m, 0, 1, 0, 0, 0, 1),
            ReferenceCanvasMode.Explicit,
            ReferenceKernel.Triangle,
            CanvasWidth: 24,
            CanvasHeight: 10,
            Background: BackgroundKind.Colored);
        CheckSingle<Rgba32>(source, transformCase);
    }

    [Fact]
    public void TheReferenceDetectsWrongResults()
    {
        // The comparison must reject a one-unit error, the edges of the image blended into an opaque background in place of
        // a transparent one, and a geometry off by half a pixel
        var source = ResizeGoldenTests.Synthetic().Single(item => item.Name == "rgba16-low-bit-alpha-ramp").Buffer;
        var shrink = new ReferenceTransformOptions(ReferenceMatrix.CreateAffine(0.75m, 0.125m, -0.25m, 0.875m, 1, 0.5m), ReferenceCanvasMode.Keep, ReferenceKernel.CatmullRom, [0, 0, 0, 0]);
        var reference = ReferenceTransformer.Transform(source, shrink);
        using var image = Import<Rgba64>(source);
        image.Transform(new AffineTransform(0.75, 0.125, -0.25, 0.875, 1, 0.5), new TransformOptions { CanvasMode = TransformCanvasMode.Keep }, Ct);
        var actual = ImageSnapshots.CaptureFrameRows(image.Frames[0]);
        Assert.True(reference.Compare(actual, "baseline", writePreviews: false).IsMatch);

        var (x, y) = (actual.Width / 2, actual.Height / 2);
        Assert.NotEqual(0, actual.GetSample(x, y, 3));
        Assert.False(reference.Compare(actual.WithSample(x, y, 1, actual.GetSample(x, y, 1) ^ 1), "one unit", writePreviews: false).IsMatch);

        var opaque = ReferenceTransformer.Transform(source, shrink with { Background = [0, 0, 0, 65535] }).ToRoundedBuffer();
        Assert.False(reference.Compare(opaque, "opaque background", writePreviews: false).IsMatch);

        var shifted = ReferenceTransformer.Transform(source, shrink with { Forward = shrink.Forward.Then(ReferenceMatrix.CreateTranslation(0.5m, 0)) }).ToRoundedBuffer();
        Assert.False(reference.Compare(shifted, "shifted", writePreviews: false).IsMatch);
    }

    private static ReferenceMatrix Centered(int width, int height, ReferenceMatrix transform)
        => ReferenceMatrix.CreateTranslation(-width / 2m, -height / 2m).Then(transform);

    private static void Check<TPixel>(GoldenFixture fixture, TransformCase transformCase)
        where TPixel : unmanaged
    {
        var format = PixelFormats.GetPixelFormat<TPixel>();
        var frames = Enumerable.Range(0, fixture.Expected.FrameCount).Select(i => ResizeGoldenTests.GetFrame(fixture, i, format, poster: false)).ToArray();
        var poster = fixture.Expected.Poster is null ? null : ResizeGoldenTests.GetFrame(fixture, 0, format, poster: true);
        using var image = Import<TPixel>(frames[0]);
        for (var i = 1; i < frames.Length; i++)
        {
            using var single = Import<TPixel>(frames[i]);
            image.AppendFrame(single.Frames[0]);
        }

        if (poster is not null)
        {
            using var single = Import<TPixel>(poster);
            image.SetPosterFrame(single.Frames[0]);
        }

        var frameObjects = ((IEnumerable<ImageFrame<TPixel>>)image.Frames).ToArray();
        var (libraryOptions, referenceOptions) = transformCase.Create(frames[0]);
        transformCase.Apply(image, libraryOptions);

        var context = $"{fixture.Id} ({format}, {transformCase.Name})";
        var size = ReferenceTransformer.ComputeSize(frames[0].Width, frames[0].Height, referenceOptions);
        Assert.Equal(new Size(size.Width, size.Height), image.Size);
        for (var i = 0; i < frames.Length; i++)
        {
            Assert.Same(frameObjects[i], image.Frames[i]);
            AssertMatches(ReferenceTransformer.Transform(frames[i], referenceOptions), ImageSnapshots.CaptureFrameRows(frameObjects[i]), string.Create(CultureInfo.InvariantCulture, $"{context}, frame {i}"));
        }

        if (poster is not null)
        {
            AssertMatches(ReferenceTransformer.Transform(poster, referenceOptions), ImageSnapshots.CaptureFrameRows(image.PosterFrame!), context + ", poster");
        }
    }

    private static void CheckSingle<TPixel>(RawPixelBuffer source, TransformCase transformCase)
        where TPixel : unmanaged
    {
        using var image = Import<TPixel>(source);
        var (libraryOptions, referenceOptions) = transformCase.Create(source);
        transformCase.Apply(image, libraryOptions);
        AssertMatches(ReferenceTransformer.Transform(source, referenceOptions), ImageSnapshots.CaptureFrameRows(image.Frames[0]), $"{transformCase.Name} ({PixelFormats.GetPixelFormat<TPixel>()})");
    }

    private static void AssertMatches(ReferenceTransformResult reference, RawPixelBuffer actual, string context)
    {
        var result = reference.Compare(actual, context);
        if (!result.IsMatch)
            throw new GoldenAssertionException(result.Describe());
    }

    private static Image<TPixel> Import<TPixel>(RawPixelBuffer buffer)
        where TPixel : unmanaged
        => Image.ImportPixelBytes<TPixel>(RawImport.ToPixelBytes(buffer, PixelFormats.GetPixelFormat<TPixel>()), buffer.Width, buffer.Height);

    /// <summary>The samples of an <see cref="Rgba64"/> color in a raw layout, for colors whose 8-bit samples are exact and whose gray is one of their samples.</summary>
    private static int[] ToSamples(Rgba64 color, RawPixelLayout layout)
    {
        var scale = layout.MaxSampleValue == byte.MaxValue ? 257 : 1;
        int[] rgba = [color.R / scale, color.G / scale, color.B / scale, color.A / scale];
        return layout.ChannelCount switch
        {
            4 => rgba,
            3 => rgba[..3],
            1 => [rgba[0]],
            _ => throw new ArgumentOutOfRangeException(nameof(layout)),
        };
    }

    private sealed record TransformCase(
        string Name,
        Action<Image, TransformOptions> Apply,
        Func<int, int, ReferenceMatrix> Forward,
        ReferenceCanvasMode Canvas,
        ReferenceKernel Kernel,
        bool Centered = false,
        int CanvasWidth = 0,
        int CanvasHeight = 0,
        bool Linear = false,
        BackgroundKind Background = BackgroundKind.Default)
    {
        /// <summary>Gets the options of the library and of the reference for a source; a format without alpha gets an opaque background.</summary>
        public (TransformOptions Library, ReferenceTransformOptions Reference) Create(RawPixelBuffer source)
        {
            var layout = source.Layout;
            var background = (Background, layout.HasAlpha, layout.IsColor) switch
            {
                (_, _, false) => Gray,
                (BackgroundKind.Default, true, _) => default,
                (BackgroundKind.Translucent, true, _) => Translucent,
                _ => Colored,
            };

            var library = new TransformOptions
            {
                Filter = Kernel switch
                {
                    ReferenceKernel.Nearest => ResamplingFilter.NearestNeighbor,
                    ReferenceKernel.Triangle => ResamplingFilter.Bilinear,
                    ReferenceKernel.CatmullRom => ResamplingFilter.Bicubic,
                    _ => ResamplingFilter.Lanczos3,
                },
                BackgroundColor = background,
                CanvasMode = Canvas switch
                {
                    ReferenceCanvasMode.Expand => TransformCanvasMode.Expand,
                    ReferenceCanvasMode.Keep => TransformCanvasMode.Keep,
                    _ => TransformCanvasMode.Explicit,
                },
                CanvasSize = new Size(CanvasWidth, CanvasHeight),
                WorkingSpace = Linear ? ResizeWorkingSpace.LinearSrgb : ResizeWorkingSpace.Encoded,
            };

            var reference = new ReferenceTransformOptions(Forward(source.Width, source.Height), Canvas, Kernel, ToSamples(background, layout), CanvasWidth, CanvasHeight, Centered, Linear);
            return (library, reference);
        }
    }
}
