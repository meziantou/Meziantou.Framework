using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Convolution;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Heif;
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
        AssertLossless(BenchmarkInputs.InterlacedPng8, Image.ImportPixelData<Rgb24>(BenchmarkInputs.LargeRgb24Source, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight));
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
    public void TiffInputsDecodeToTheSyntheticSources()
    {
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffLzw, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffDeflate, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffDeflateRowStrips, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffPredictor8, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffPredictor16, BenchmarkInputs.LargeRgba64Source);

        // The predictor input in the other byte order, and with an odd size
        var source = BenchmarkPixels.Rgba64(37, 11, seed: 9, alpha: true);
        var littleEndian = TiffPredictorInput.Encode(unsafe(MemoryMarshal.AsBytes(source.AsSpan())), 37, 11, samplesPerPixel: 4, bitsPerSample: 16, bigEndian: false);
        WorkloadChecks.RequireLossless(littleEndian, source);
        Assert.Equal(ImageFormat.Tiff, Image.DetectFormat(littleEndian));
    }

    [Fact]
    public void IndexedInputsDecodeToTheReducedSource()
    {
        var source = BenchmarkInputs.IndexedRgb24Source;
        Assert.HasCountLessThanOrEqual(256, source.Distinct());
        Assert.Equal(ImageColorModel.Indexed, Image.Identify(BenchmarkInputs.BmpIndexed8).ColorModel);
        Assert.Equal(ImageColorModel.Indexed, Image.Identify(BenchmarkInputs.PngIndexed8).ColorModel);
        Assert.Equal(ImageColorModel.Indexed, Image.Identify(BenchmarkInputs.TiffIndexed8).ColorModel);
        WorkloadChecks.RequireLossless(BenchmarkInputs.PngIndexed8, source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffIndexed8, source);
    }

    [Fact]
    public void PngLayoutInputsDecodeToTheirSources()
    {
        // Layouts PngEncoder never writes: gray with alpha, 16-bit RGB without alpha. The reference reader parses them
        // (chunk CRCs; the zlib datastream of the layouts it decodes) and the library decodes them to the pixels they were made from
        Assert.Equal(new Size(BenchmarkInputs.ServiceWidth, BenchmarkInputs.ServiceHeight), Image.Identify(BenchmarkInputs.PngGrayAlpha8).Size);
        Assert.Equal(ImageColorModel.GrayscaleAlpha, Image.Identify(BenchmarkInputs.PngGrayAlpha8).ColorModel);
        Assert.Equal(ImageColorModel.Rgb, Image.Identify(BenchmarkInputs.PngRgb16).ColorModel);
        Assert.Equal(16, Image.Identify(BenchmarkInputs.PngRgb16).BitsPerComponent);
        _ = ReferencePng.Parse(BenchmarkInputs.PngGrayAlpha8).DecodePixels();
        _ = ReferencePng.Parse(BenchmarkInputs.PngRgb16);
        WorkloadChecks.RequireLossless(BenchmarkInputs.PngGrayAlpha8, BenchmarkInputs.GrayAlpha8Pixels);
        WorkloadChecks.RequireLossless(BenchmarkInputs.PngRgb16, BenchmarkInputs.Rgb16Pixels);
    }

    [Fact]
    public void RawAndBilevelInputsDecodeToTheirSources()
    {
        // The text page is black and white, with enough of both for the bilevel decoders to see real runs
        var bilevel = BenchmarkInputs.LargeBilevelSource;
        Assert.All(bilevel, static pixel => Assert.True(pixel.Value is 0 or 255));
        var black = bilevel.Count(static pixel => pixel.Value == 0);
        Assert.InRange(black, bilevel.Length / 10, bilevel.Length / 2);

        Assert.Equal(ImageColorModel.Grayscale, Image.Identify(BenchmarkInputs.PngBilevel).ColorModel);
        Assert.Equal(1, Image.Identify(BenchmarkInputs.PngBilevel).BitsPerComponent);
        _ = ReferencePng.Parse(BenchmarkInputs.PngBilevel);
        WorkloadChecks.RequireLossless(BenchmarkInputs.PngBilevel, bilevel);
        WorkloadChecks.RequireLossless(BenchmarkInputs.PnmBitmap, bilevel);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffBilevel, bilevel);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffCcittGroup4, bilevel);
        WorkloadChecks.RequireLossless(BenchmarkInputs.PnmRgb16, BenchmarkInputs.Rgb16Pixels);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffRgb16, BenchmarkInputs.Rgb16Pixels);
        Assert.Equal(16, Image.Identify(BenchmarkInputs.TiffRgb16).BitsPerComponent);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffUncompressed, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TgaUncompressed, BenchmarkInputs.LargeRgb24Source);
    }

    [Fact]
    public void TheBaselineJpegInputMatchesTheEncoderModel()
    {
        // The baseline input is the output of the encode workload (2048x1536 Rgb24, quality 85, 4:2:0)
        var pixels = RawPixelBuffer.Create(BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight, RawPixelLayout.Rgb8, unsafe(MemoryMarshal.AsBytes(BenchmarkInputs.LargeRgb24Source.AsSpan())));
        AssertMatchesEncoderModel(BenchmarkInputs.BaselineJpeg, pixels, "baseline input");
    }

    [Fact]
    public void TheInkJpegInputIsLoadedWithItsColorConversion()
    {
        // Four components (YCCK inks) of the size of the other JPEG inputs: identified, rejected without conversion, and
        // converted to sRGB pixels by the timed workload. The decoder tests compare such pixels with their references
        var info = Image.Identify(BenchmarkInputs.InkJpeg);
        Assert.Equal(ImageColorModel.Ycck, info.ColorModel);
        Assert.Equal(new Size(BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight), info.Size);
        Assert.Null(info.Metadata.IccProfile);
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(BenchmarkInputs.InkJpeg));

        using var image = Image.Load<Rgb24>(BenchmarkInputs.InkJpeg, BenchmarkInputs.InkJpegOptions);
        Assert.Equal(info.Size, image.Size);
        Assert.Same(Metadata.IccProfile.Srgb, image.Metadata.IccProfile);
        var pixels = ImageSnapshots.CaptureFrame(image.Frames[0]);
        Assert.True(pixels.Span.IndexOfAnyExcept(pixels.Span[0]) >= 0, "The converted image is uniform.");
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

    [Fact]
    public void GaussianBlurWorkloadsMatchTheReferenceConvolver()
    {
        // A 120x90 crop of each benchmark source (three bands of 30 rows when four workers are allowed), sequential and
        // parallel; the reference is the matrix of the products of the weights
        foreach (var workers in new[] { 1, 4 })
        {
            var configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers };
            using var rgba32 = Crop(Image.ImportPixelData<Rgba32>(BenchmarkInputs.ResizeRgba32Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight, configuration: configuration));
            using var rgba64 = Crop(Image.ImportPixelData<Rgba64>(BenchmarkInputs.ResizeRgba64Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight, configuration: configuration));
            foreach (var source in new Image[] { rgba32, rgba64 })
            {
                const double Sigma = 2;
                decimal[] row = [.. Enumerable.Range(-6, 13).Select(i => (decimal)Math.Exp(-(i * i) / (2 * Sigma * Sigma)))];
                var sum = row.Sum();
                row = [.. row.Select(weight => weight / sum)];
                var before = ImageSnapshots.CaptureFrame(source.Frames[0]);
                using var result = ImageWorkloads.GaussianBlurCopy(source, Sigma);
                var reference = ReferenceConvolver.Convolve(before, new ReferenceConvolutionOptions(13, 13, [.. row.SelectMany(vertical => row.Select(horizontal => vertical * horizontal))]));
                var comparison = reference.Compare(ImageSnapshots.CaptureFrame(result.Frames[0]), $"{source.PixelFormat} Gaussian blur {Sigma}, {workers} worker(s)", writePreviews: false);
                Assert.True(comparison.IsMatch, comparison.ToString());

                var unchanged = PixelBufferComparer.Compare(before, ImageSnapshots.CaptureFrame(source.Frames[0]), ComparisonPolicy.Exact, "source");
                Assert.True(unchanged.IsMatch, unchanged.Describe());
            }
        }

        static Image Crop(Image image)
        {
            image.Crop(new Rectangle(600, 400, 120, 90));
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
    public void JpegXlWorkloadsGiveBackTheirInputsAndTheirGatesRejectWrongResults()
    {
        // Transcoding: the JPEG file of the service request is recompressed, and rebuilt byte for byte
        using var output = new MemoryStream();
        Assert.True(ImageWorkloads.Recompress(BenchmarkInputs.ServiceJpeg, output) < BenchmarkInputs.ServiceJpeg.Length);
        var recompressed = output.ToArray();
        Assert.Equal(ImageFormat.JpegXl, Image.DetectFormat(recompressed));
        Assert.Equal(BenchmarkInputs.ServiceJpeg.Length, ImageWorkloads.Reconstruct(recompressed, output));
        Assert.Equal(BenchmarkInputs.ServiceJpeg, output.ToArray());

        // Progressive output: the pixels of the file written in one pass, or exactly the source for lossless output
        const int Width = 320;
        const int Height = 240;
        var source = ToRgb24(BenchmarkPixels.Rgb24(Width, Height, seed: 5));
        using var image = Image.ImportPixelData<Rgb24>(source, Width, Height);
        ImageWorkloads.Encode(image, new JpegXlEncoder { Compression = JpegXlCompression.Lossy }, output);
        var single = output.ToArray();
        ImageWorkloads.Encode(image, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Progressive = true }, output);
        var progressive = output.ToArray();
        ImageWorkloads.Encode(image, new JpegXlEncoder { Progressive = true }, output);
        var lossless = output.ToArray();
        WorkloadChecks.RequireLossless(lossless, source);
        WorkloadChecks.RequireSamePixels<Rgb24>(single, progressive);
        var refinements = WorkloadChecks.RequireRefinements<Rgb24>(progressive, minimum: 3);

        // The workload renders every refinement into one image, which ends as the image that is loaded at once
        using var destination = new Image<Rgb24>(Width, Height);
        Assert.Equal(refinements, ImageWorkloads.ReadRefinements(progressive, destination));
        using var expected = Image.Load<Rgb24>(progressive);
        var pixels = new Rgb24[Width * Height];
        var others = new Rgb24[Width * Height];
        expected.Frames[0].CopyPixelDataTo(pixels);
        destination.Frames[0].CopyPixelDataTo(others);
        Assert.Equal(pixels, others);

        // Other pixels, or a file that is not read in as many refinements, fail
        Assert.Throws<InvalidOperationException>(() => WorkloadChecks.RequireSamePixels<Rgb24>(single, lossless));
        Assert.Throws<InvalidOperationException>(() => WorkloadChecks.RequireRefinements<Rgb24>(progressive, refinements + 1));
        Assert.Throws<InvalidOperationException>(() => WorkloadChecks.RequireRefinements<Rgb24>(single, refinements));
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

    [Theory]
    [InlineData("hevc/x265-benchmark-intra-420-384x216", 1, false)]
    [InlineData("hevc/x265-benchmark-intra-444-384x216", 3, false)]
    [InlineData("hevc/x265-benchmark-inter-420-176x144", 1, true)]
    public void HeicInputsHoldThePicturesOfTheVectorsOfTheCorpus(string id, int chromaFormatIdc, bool isSequence)
    {
        var corpus = GoldenCorpus.Default;
        var vector = corpus.GetHevcVector(id);
        var frame = vector.Frames[0];
        var data = HevcStreamContainer.Wrap(File.ReadAllBytes(corpus.GetHevcVectorPath(vector)), frame.Width, frame.Height, bitDepth: 8, chromaFormatIdc);

        // The independent reader finds an image, or a sample for each picture of the vector
        var reader = ReferenceHeif.Parse(data);
        Assert.Equal(isSequence, reader.IsSequence);
        if (isSequence)
        {
            Assert.HasCount(vector.Frames.Count, reader.ColorTrack!.Samples);
            Assert.Equal((frame.Width, frame.Height), (reader.ColorTrack.Width, reader.ColorTrack.Height));
        }
        else
        {
            Assert.Single(vector.Frames);
            Assert.Equal("hvc1", reader.PrimaryItem!.Type);
            Assert.Equal((frame.Width, frame.Height), reader.PrimaryItem.Size);
        }

        // The library decode (the timed load workloads) gives every picture, at its size
        using var image = Image.Load(data);
        Assert.Equal(ImageFormat.Heic, image.Metadata.SourceFormat);
        Assert.Equal((frame.Width, frame.Height, vector.Frames.Count), (image.Width, image.Height, image.Frames.Count));
    }

    [Fact]
    public void HeicGridInputHoldsCopiesOfTheStillOfTheCorpus()
    {
        const int Columns = 8;
        const int Rows = 6;
        const int Width = 3000;
        const int Height = 1280;
        var corpus = GoldenCorpus.Default;
        var vector = corpus.GetHevcVector("hevc/x265-benchmark-intra-420-384x216");
        var stream = File.ReadAllBytes(corpus.GetHevcVectorPath(vector));
        var data = HevcStreamContainer.WrapGrid(stream, 384, 216, chromaFormatIdc: 1, Columns, Rows, Width, Height);

        // The independent reader finds a grid of coded items
        var reader = ReferenceHeif.Parse(data);
        var grid = reader.PrimaryItem!;
        Assert.Equal("grid", grid.Type);
        Assert.Equal((Rows, Columns, (long)Width, (long)Height), reader.ReadGridDescriptor(grid));
        var tiles = reader.GetGridTiles(grid);
        Assert.HasCount(Columns * Rows, tiles);
        Assert.All(tiles, tile => Assert.Equal("hvc1", tile.Type));

        // The library decode gives the image, whose tiles away from their edges and from the edges of the image (where the
        // chroma of the next tile is interpolated, or the chroma is extended) are the still image
        using var still = Image.Load<Rgb24>(HevcStreamContainer.Wrap(stream, 384, 216, bitDepth: 8, chromaFormatIdc: 1));
        using var image = Image.Load<Rgb24>(data);
        Assert.Equal((Width, Height), (image.Width, image.Height));
        var expected = new Rgb24[384 * 216];
        still.Frames[0].CopyPixelDataTo(expected);
        var actual = new Rgb24[Width * Height];
        image.Frames[0].CopyPixelDataTo(actual);
        for (var y = 0; y < Height; y++)
        {
            var tileY = y % 216;
            if (tileY < 2 || tileY >= 214 || y >= Height - 2)
                continue;

            for (var x = 0; x < Width; x++)
            {
                var tileX = x % 384;
                if (tileX >= 2 && tileX < 382 && x < Width - 2 && actual[(y * Width) + x] != expected[(tileY * 384) + tileX])
                    Assert.Fail($"The pixel ({x}, {y}) of the grid is not the pixel ({tileX}, {tileY}) of the still image.");
            }
        }
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
