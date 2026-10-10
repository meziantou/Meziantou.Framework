using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Convolution;
using Meziantou.Framework.Imaging.TestHarness.Jpeg;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Png;
using Meziantou.Framework.Imaging.TestHarness.Resampling;
using Meziantou.Framework.Imaging.TestHarness.Qoi;
using Meziantou.Framework.Imaging.TestHarness.WebP;

namespace Meziantou.Framework.Imaging.Tests.Conformance.Benchmarks;

/// <summary>
/// Validates the benchmark inputs and the outputs of the benchmarked operations against independent references,
/// outside any timed region, so that a faster but incorrect kernel or codec mode never counts as an improvement. The
/// workload sources of <c>benchmarks/.../Workloads</c> are compiled into this project: the tests exercise exactly the
/// benchmarked code paths. References: the synthetic source pixels (closed-form expressions), the harness PNG/APNG/GIF/JPEG
/// readers, the JPEG encoder model, and the reference resampler and convolver.
/// </summary>
public sealed class BenchmarkWorkloadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void PngInputsDecodeToTheSyntheticSourcesWithTheReferenceReader()
    {
        AssertLossless(BenchmarkInputs.SmallPng, Image.ImportPixelData<Rgba32>(BenchmarkInputs.SmallPngSource, 64, 64));
        AssertLossless(BenchmarkInputs.LargePng8, Image.ImportPixelData<Rgb24>(BenchmarkInputs.LargeRgb24Source, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight));
        AssertLossless(BenchmarkInputs.LargePng16, Image.ImportPixelData<Rgba64>(BenchmarkInputs.LargeRgba64Source, BenchmarkInputs.Large16Width, BenchmarkInputs.Large16Height));

        static void AssertLossless(byte[] png, Image source)
        {
            using (source)
            {
                var expected = ImageSnapshots.CaptureFrame(source.Frames[0]);
                AssertSamePixels(expected, ReferencePng.Parse(png).DecodePixels(), "reference reader");

                // The library decode (the timed load workloads) gives the same pixels
                using var decoded = Image.Load(png);
                AssertSamePixels(expected, ImageSnapshots.CaptureFrame(decoded.Frames[0]), "library decode");
            }
        }
    }

    [Fact]
    public void TheBaselineJpegInputMatchesTheEncoderModel()
    {
        // The baseline input is the output of the encode workload (2048x1536 Rgb24, quality 85, 4:2:0)
        var pixels = RawPixelBuffer.Create(BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight, RawPixelLayout.Rgb8, unsafe(MemoryMarshal.AsBytes(BenchmarkInputs.LargeRgb24Source.AsSpan())));
        AssertMatchesEncoderModel(BenchmarkInputs.BaselineJpeg, pixels, "baseline input");
    }

    [Fact]
    public void TheProgressiveJpegInputDecodesLikeTheSameCoefficientsCodedSequentially()
    {
        // Progressive and sequential codings of identical coefficients must decode to identical pixels
        var coefficients = ProgressiveJpegInput.CreateCoefficients(unsafe(MemoryMarshal.AsBytes(BenchmarkInputs.LargeRgb24Source.AsSpan())).ToArray(), BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight, BenchmarkInputs.JpegQuality);
        var sequential = coefficients.Encode();
        using var expected = Image.Load<Rgb24>(sequential);
        using var actual = Image.Load<Rgb24>(BenchmarkInputs.ProgressiveJpeg);
        AssertSamePixels(ImageSnapshots.CaptureFrame(expected.Frames[0]), ImageSnapshots.CaptureFrame(actual.Frames[0]), "progressive input");

        // The sequential coding is read by the independent reference reader: same coefficients, and a plain reconstruction
        // close to the synthetic source
        var reference = ReferenceJpeg.Parse(sequential);
        var blocks = reference.DecodeCoefficients();
        for (var c = 0; c < coefficients.Components.Length; c++)
        {
            var component = coefficients.Components[c];
            for (var b = 0; b < component.Blocks.Length; b += 97)
            {
                var block = blocks[c].GetBlock(b % component.BlocksX, b / component.BlocksX);
                Assert.Equal(component.Blocks[b], block.ToArray());
            }
        }

        var source = RawPixelBuffer.Create(BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight, RawPixelLayout.Rgb8, unsafe(MemoryMarshal.AsBytes(BenchmarkInputs.LargeRgb24Source.AsSpan())));
        var error = ReconstructionError.Measure(source, ImageSnapshots.CaptureFrame(actual.Frames[0]));
        Assert.True(error.Psnr > 35, error.Describe());
    }

    [Fact]
    public void TheServiceRequestEncodesTheResizedPixelsLikeTheEncoderModel()
    {
        using var output = new MemoryStream();
        ImageWorkloads.JpegDecodeResizeEncode(BenchmarkInputs.ServiceJpeg, new Size(320, 240), output);

        // Expected encoder input: the decoded request resized to fit 320x240 (both steps validated by their own suites)
        using var resized = Image.Load<Rgb24>(BenchmarkInputs.ServiceJpeg);
        resized.Resize(new ResizeOptions(320, 240) { Mode = ResizeMode.Contain }, Ct);
        AssertMatchesEncoderModel(output.ToArray(), ImageSnapshots.CaptureFrame(resized.Frames[0]), "service request");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ConvolutionWorkloadsMatchTheReferenceConvolver(bool preserveAlpha, bool linear)
    {
        // A 240x280 crop of each benchmark source (four bands of 70 rows when four workers are allowed), sequential and parallel
        decimal[] weights = [0.0625m, 0.125m, 0.0625m, 0.125m, 0.25m, 0.125m, 0.0625m, 0.125m, 0.0625m];
        var options = new ConvolutionOptions(new ConvolutionKernel(3, 3, [.. weights.Select(weight => (double)weight)]))
        {
            PreserveAlpha = preserveAlpha,
            WorkingSpace = linear ? ConvolutionWorkingSpace.LinearSrgb : ConvolutionWorkingSpace.Encoded,
        };

        foreach (var workers in new[] { 1, 4 })
        {
            var configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers };
            using var rgba32 = Crop(Image.ImportPixelData<Rgba32>(BenchmarkInputs.ResizeRgba32Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight, configuration: configuration));
            using var rgba64 = Crop(Image.ImportPixelData<Rgba64>(BenchmarkInputs.ResizeRgba64Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight, configuration: configuration));
            foreach (var source in new Image[] { rgba32, rgba64 })
            {
                var before = ImageSnapshots.CaptureFrame(source.Frames[0]);
                using var result = ImageWorkloads.ConvolveCopy(source, options);
                var reference = ReferenceConvolver.Convolve(before, new ReferenceConvolutionOptions(3, 3, weights, PreserveAlpha: preserveAlpha, Linear: linear));
                var comparison = reference.Compare(ImageSnapshots.CaptureFrame(result.Frames[0]), $"{source.PixelFormat} 3x3, alpha preserved: {preserveAlpha}, linear: {linear}, {workers} worker(s)", writePreviews: false);
                Assert.True(comparison.IsMatch, comparison.ToString());

                // The workload convolves a copy: the source can be reused by the next iteration
                var unchanged = PixelBufferComparer.Compare(before, ImageSnapshots.CaptureFrame(source.Frames[0]), ComparisonPolicy.Exact, "source");
                Assert.True(unchanged.IsMatch, unchanged.Describe());
            }
        }

        static Image Crop(Image image)
        {
            image.Crop(new Rectangle(600, 400, 240, 280));
            return image;
        }
    }

    public static TheoryData<ResamplingFilter, ResizeWorkingSpace, int, int> ResizeCases() => new()
    {
        { ResamplingFilter.NearestNeighbor, ResizeWorkingSpace.Encoded, 80, 45 },
        { ResamplingFilter.Bilinear, ResizeWorkingSpace.Encoded, 80, 45 },
        { ResamplingFilter.Bicubic, ResizeWorkingSpace.Encoded, 80, 45 },
        { ResamplingFilter.Lanczos3, ResizeWorkingSpace.Encoded, 80, 45 },
        { ResamplingFilter.Bicubic, ResizeWorkingSpace.LinearSrgb, 80, 45 },
        { ResamplingFilter.Bicubic, ResizeWorkingSpace.Encoded, 360, 203 },
    };

    [Theory]
    [MemberData(nameof(ResizeCases))]
    public void ResizeWorkloadsMatchTheReferenceResampler(ResamplingFilter filter, ResizeWorkingSpace workingSpace, int width, int height)
    {
        // A 240x135 crop of each benchmark source (same scale factors as the 1920x1080 workloads), sequential and parallel
        foreach (var workers in new[] { 1, 4 })
        {
            var configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers };
            using var rgba32 = Crop(Image.ImportPixelData<Rgba32>(BenchmarkInputs.ResizeRgba32Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight, configuration: configuration));
            using var rgba64 = Crop(Image.ImportPixelData<Rgba64>(BenchmarkInputs.ResizeRgba64Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight, configuration: configuration));
            foreach (var source in new Image[] { rgba32, rgba64 })
            {
                var options = new ResizeOptions(width, height) { Mode = ResizeMode.Stretch, Filter = filter, WorkingSpace = workingSpace, AllowUpscaling = true };
                using var result = ImageWorkloads.ResizeCopy(source, options);
                var kernel = filter switch
                {
                    ResamplingFilter.NearestNeighbor => ReferenceKernel.Nearest,
                    ResamplingFilter.Bilinear => ReferenceKernel.Triangle,
                    ResamplingFilter.Lanczos3 => ReferenceKernel.Lanczos3,
                    _ => ReferenceKernel.CatmullRom,
                };

                var reference = ReferenceResampler.Resize(ImageSnapshots.CaptureFrame(source.Frames[0]), new ReferenceResizeOptions(width, height, ReferenceResizeMode.Stretch, kernel, Linear: workingSpace == ResizeWorkingSpace.LinearSrgb));
                var comparison = reference.Compare(ImageSnapshots.CaptureFrame(result.Frames[0]), $"{source.PixelFormat} {filter} {workingSpace} {width}x{height}, {workers} worker(s)", writePreviews: false);
                Assert.True(comparison.IsMatch, comparison.ToString());
            }
        }

        static Image Crop(Image image)
        {
            image.Crop(new Rectangle(600, 400, 240, 135));
            return image;
        }
    }

    [Fact]
    public void AnimationInputsMatchTheirSourceWithTheReferenceReaders()
    {
        using var source = BenchmarkInputs.CreateAnimation();
        ApngOutputVerifier.Verify(BenchmarkInputs.Apng, source, context: "APNG input");
        var verification = GifOutputVerifier.Verify(BenchmarkInputs.Gif, source, new GifEncoder(), context: "GIF input");
        Assert.Equal(BenchmarkInputs.AnimationFrames, verification.Displayed.Count);
    }

    [Fact]
    public void WebPInputsMatchTheirSourcesWithTheReferenceReader()
    {
        // Lossless still: every sample (reference VP8L decoder)
        var photo = ReferenceWebP.Parse(BenchmarkInputs.LosslessWebP);
        var largeSource = BenchmarkInputs.LargeRgb24Source; // the property returns a new copy on every access
        var rgb = unsafe(MemoryMarshal.AsBytes(largeSource.AsSpan()));
        var frame = photo.DecodeFrame(0);
        Assert.Equal((BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight), (frame.Width, frame.Height));
        var decoded = frame.Span;
        for (var i = 0; i < largeSource.Length; i++)
        {
            if (!decoded.Slice(4 * i, 3).SequenceEqual(rgb.Slice(3 * i, 3)) || decoded[(4 * i) + 3] != 255)
                Assert.Fail($"Pixel {i} of the lossless WebP input differs from its source.");
        }

        // Lossy stills: structure, exact alpha plane, and bounded error of the library decode
        var lossy = ReferenceWebP.Parse(BenchmarkInputs.LossyWebP);
        Assert.False(Assert.Single(lossy.Frames).IsLossless);
        Assert.True(WorkloadChecks.RequirePsnr(BenchmarkInputs.LossyWebP, largeSource, minimum: 33) >= 33);
        var transparent = ReferenceWebP.Parse(BenchmarkInputs.TransparentWebP);
        Assert.Equal(BenchmarkInputs.ServiceRgba32Source.Select(pixel => pixel.A).ToArray(), transparent.DecodeAlpha(0));

        // Animation: full-canvas lossless frames equal to the source frames, 40 ms each
        using var source = BenchmarkInputs.CreateAnimation();
        var animation = ReferenceWebP.Parse(BenchmarkInputs.WebPAnimation);
        Assert.Equal(BenchmarkInputs.AnimationFrames, animation.Frames.Count);
        for (var i = 0; i < animation.Frames.Count; i++)
        {
            Assert.Equal(40, animation.Frames[i].DurationMilliseconds);
            AssertSamePixels(ImageSnapshots.CaptureFrame(source.Frames[i]), animation.DecodeFrame(i), $"WebP animation frame {i}");
        }
    }

    [Fact]
    public void QoiInputsMatchTheirSourcesWithTheReferenceReader()
    {
        var photo = ReferenceQoi.Parse(BenchmarkInputs.QoiRgb);
        Assert.Equal(3, photo.Channels);
        Assert.True(unsafe(MemoryMarshal.AsBytes(BenchmarkInputs.LargeRgb24Source.AsSpan())).SequenceEqual(photo.GetDeclaredPixels()), "The QOI RGB input differs from its source.");

        var transparent = ReferenceQoi.Parse(BenchmarkInputs.QoiRgba);
        Assert.Equal(4, transparent.Channels);
        Assert.True(unsafe(MemoryMarshal.AsBytes(BenchmarkInputs.ServiceRgba32Source.AsSpan())).SequenceEqual(transparent.Rgba.Span), "The QOI RGBA input differs from its source.");
    }

    [Fact]
    public void EagerAnimationEditsAreValidatedByTheReferenceReaders()
    {
        using var output = new MemoryStream();
        ImageWorkloads.EditAnimation(BenchmarkInputs.Apng, new PngEncoder(), output);
        using (var expected = EditEagerly(BenchmarkInputs.Apng))
        {
            ApngOutputVerifier.Verify(output.ToArray(), expected, context: "APNG edit");
        }

        ImageWorkloads.EditAnimation(BenchmarkInputs.Gif, new GifEncoder(), output);
        using (var expected = EditEagerly(BenchmarkInputs.Gif))
        {
            GifOutputVerifier.Verify(output.ToArray(), expected, new GifEncoder(), context: "GIF edit");
        }

        static Image<Rgba32> EditEagerly(byte[] input)
        {
            var image = Image.Load<Rgba32>(input);
            image.RemoveFrame(1);
            image.MoveFrame(image.Frames.Count - 1, 0);
            image.Resize(new ResizeOptions(image.Width / 2, image.Height / 2) { Mode = ResizeMode.Stretch }, Ct);
            return image;
        }
    }

    [Fact]
    public void SequentialAnimationTransformsMatchTheEagerTransforms()
    {
        // Resizing each displayed frame read sequentially must give the frames of an eager resize of the whole animation
        using var output = new MemoryStream();
        Assert.Equal(BenchmarkInputs.AnimationFrames, ImageWorkloads.TransformAnimationSequentially(BenchmarkInputs.Apng, new PngEncoder { AnimationMode = PngAnimationMode.Animated }, output));
        using (var expected = ResizeEagerly(BenchmarkInputs.Apng))
        {
            ApngOutputVerifier.Verify(output.ToArray(), expected, context: "sequential APNG");
        }

        Assert.Equal(BenchmarkInputs.AnimationFrames, ImageWorkloads.TransformAnimationSequentially(BenchmarkInputs.Gif, new GifEncoder(), output));
        using (var expected = ResizeEagerly(BenchmarkInputs.Gif))
        {
            GifOutputVerifier.Verify(output.ToArray(), expected, new GifEncoder(), context: "sequential GIF");
        }

        static Image<Rgba32> ResizeEagerly(byte[] input)
        {
            var image = Image.Load<Rgba32>(input);
            image.Resize(new ResizeOptions(image.Width / 2, image.Height / 2) { Mode = ResizeMode.Stretch }, Ct);
            return image;
        }
    }

    [Fact]
    public void RowLoopWorkloadsComputeTheExpectedValues()
    {
        var pixels = BenchmarkInputs.ResizeRgba32Source;
        using var image = Image.ImportPixelData<Rgba32>(pixels, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight);
        var state = new ImageWorkloads.SumState();
        Assert.Equal(pixels.Sum(pixel => (long)pixel.G), ImageWorkloads.SumGreen(image.Frames[0], state));

        ImageWorkloads.InvertRows(image.Frames[0]);
        var inverted = new Rgba32[pixels.Length];
        image.Frames[0].CopyPixelDataTo(inverted);
        Assert.Equal(pixels.Select(pixel => new Rgba32((byte)(255 - pixel.R), (byte)(255 - pixel.G), (byte)(255 - pixel.B), pixel.A)), inverted);
    }

    [Fact]
    public void TheBenchmarkSetupGatesAcceptTheInputsAndRejectWrongResults()
    {
        // The gates run by the benchmarks' global setups (before any timed region)
        WorkloadChecks.RequireLossless(BenchmarkInputs.SmallPng, BenchmarkInputs.SmallPngSource);
        Assert.True(WorkloadChecks.RequirePsnr(BenchmarkInputs.ServiceJpeg, ToRgb24(BenchmarkPixels.Rgb24(BenchmarkInputs.ServiceWidth, BenchmarkInputs.ServiceHeight, seed: 5)), minimum: 35) > 35);
        WorkloadChecks.RequireAnimation(BenchmarkInputs.Gif, BenchmarkInputs.AnimationWidth, BenchmarkInputs.AnimationHeight, BenchmarkInputs.AnimationFrames, FrameDuration.FromMilliseconds(40));

        // A one-unit error, another source or another structure fails
        var wrong = BenchmarkInputs.SmallPngSource;
        wrong[100] = new Rgba32((byte)(wrong[100].R ^ 1), wrong[100].G, wrong[100].B, wrong[100].A);
        Assert.Throws<InvalidOperationException>(() => WorkloadChecks.RequireLossless(BenchmarkInputs.SmallPng, wrong));
        Assert.Throws<InvalidOperationException>(() => WorkloadChecks.RequirePsnr(BenchmarkInputs.ServiceJpeg, ToRgb24(BenchmarkPixels.Rgb24(BenchmarkInputs.ServiceWidth, BenchmarkInputs.ServiceHeight, seed: 6)), minimum: 35));
        Assert.Throws<InvalidOperationException>(() => WorkloadChecks.RequireAnimation(BenchmarkInputs.Apng, BenchmarkInputs.AnimationWidth, BenchmarkInputs.AnimationHeight, BenchmarkInputs.AnimationFrames - 1, FrameDuration.FromMilliseconds(40)));
    }

    private static void AssertMatchesEncoderModel(byte[] jpeg, RawPixelBuffer pixels, string context)
    {
        var reference = ReferenceJpeg.Parse(jpeg);
        Assert.True(reference.Width == pixels.Width && reference.Height == pixels.Height && reference.Components.Count == 3, context);
        Assert.True(reference.Components[0].H == 2 && reference.Components[0].V == 2, context);
        var model = JpegEncoderModel.ComputeCoefficients(pixels, 2, 2, JpegEncoderModel.ScaleTable(JpegEncoderModel.AnnexKLuminance, BenchmarkInputs.JpegQuality), JpegEncoderModel.ScaleTable(JpegEncoderModel.AnnexKChrominance, BenchmarkInputs.JpegQuality));
        var comparison = JpegEncoderModel.Compare(model, reference.DecodeCoefficients());
        Assert.True(comparison.IsMatch, context + ": " + comparison.Describe());
    }

    private static Rgb24[] ToRgb24(byte[] samples) => unsafe(MemoryMarshal.Cast<byte, Rgb24>(samples)).ToArray();

    private static void AssertSamePixels(RawPixelBuffer expected, RawPixelBuffer actual, string context)
    {
        Assert.True(expected.Width == actual.Width && expected.Height == actual.Height && expected.Layout == actual.Layout, $"{context}: {actual.Width}x{actual.Height} {actual.Layout}, {expected.Width}x{expected.Height} {expected.Layout} expected");
        for (var y = 0; y < expected.Height; y++)
        {
            if (!expected.GetRow(y).SequenceEqual(actual.GetRow(y)))
                Assert.Fail($"{context}: row {y} differs.");
        }
    }
}
