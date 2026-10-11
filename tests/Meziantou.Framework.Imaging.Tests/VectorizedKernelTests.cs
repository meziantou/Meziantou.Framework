using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The vectorized kernels must give exactly the results of their scalar references.
/// Inputs are seeded random data plus edge cases (lengths around the vector size, every bytes-per-pixel value, ties).
/// On hardware without vector support both paths are the scalar one and the tests are trivially true.
/// </summary>
public sealed class VectorizedKernelTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void PngFiltersMatchTheScalarReference(int bytesPerPixel)
    {
        var random = new Random(bytesPerPixel);
        int[] lengths = [0, 1, bytesPerPixel, 15, 16, 17, 31, 32, 33, 47, 63, 64, 65, 100, 255, 1000, 4099];
        foreach (var length in lengths)
        {
            for (var round = 0; round < 8; round++)
            {
                var row = new byte[length];
                var previous = new byte[length];
                random.NextBytes(row);
                random.NextBytes(previous);
                if (round == 1)
                {
                    // Smooth data (small differences, many Paeth ties)
                    for (var i = 0; i < length; i++)
                    {
                        row[i] = (byte)(i / 7);
                        previous[i] = (byte)((i / 7) + (i % 3));
                    }
                }
                else if (round == 2)
                {
                    Array.Fill(row, (byte)255);
                    Array.Fill(previous, (byte)0);
                }
                else if (round == 3)
                {
                    Array.Clear(previous);
                }

                for (byte type = PngFilters.None; type <= PngFilters.Paeth; type++)
                {
                    var expected = new byte[length];
                    var actual = new byte[length];
                    PngFilters.FilterScalar(type, row, previous, bytesPerPixel, expected);
                    PngFilters.Filter(type, row, previous, bytesPerPixel, actual);
                    Assert.Equal(expected, actual);
                    Assert.Equal(PngFilters.GetAdaptiveCostScalar(expected), PngFilters.GetAdaptiveCost(actual));

                    // The filter is reversible: unfiltering gives the row back
                    PngFilters.Unfilter(type, actual, previous, bytesPerPixel);
                    Assert.Equal(row, actual);

                    // Any bytes are a valid filtered scanline: the reconstruction of the row itself (wrapping sums
                    // included) is the one of the scalar reference
                    var reconstructed = (byte[])row.Clone();
                    var reference = (byte[])row.Clone();
                    PngFilters.Unfilter(type, reconstructed, previous, bytesPerPixel);
                    PngFilters.UnfilterScalar(type, reference, previous, bytesPerPixel);
                    Assert.Equal(reference, reconstructed);
                }
            }
        }
    }

    [Fact]
    public void PngAdaptiveCostMatchesTheScalarReferenceOnLongRows()
    {
        // Long rows of extreme values exercise the 16-bit partial sums and their flushes
        foreach (var value in new byte[] { 0x80, 0x7F, 0xFF, 0x01, 0x00 })
        {
            var data = new byte[(64 * 1024) + 13];
            Array.Fill(data, value);
            Assert.Equal(PngFilters.GetAdaptiveCostScalar(data), PngFilters.GetAdaptiveCost(data));
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(64)]
    [InlineData(255)]
    [InlineData(256)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void GifNearestColorMatchesTheScalarReference(int colors)
    {
        var random = new Random(colors);
        using var quantizer = new PaletteQuantizer(new AllocationScope(new ImageResourceLimits()));
        quantizer.Begin(256);

        // Distinct random colors: an exact palette, sorted by key
        var keys = new HashSet<int>();
        while (keys.Count < colors)
        {
            keys.Add(random.Next(0, 1 << 24));
        }

        quantizer.Add([.. keys]);
        quantizer.BuildPalette(256);
        Assert.Equal(colors, quantizer.PaletteCount);
        for (var i = 0; i < 20_000; i++)
        {
            var (r, g, b) = (random.Next(256), random.Next(256), random.Next(256));
            Assert.Equal(quantizer.FindNearestScalar(r, g, b), quantizer.FindNearest(r, g, b));
        }

        // Palette colors themselves (distance 0)
        foreach (var key in quantizer.Palette)
        {
            Assert.Equal(quantizer.FindNearestScalar(key >> 16, (key >> 8) & 0xFF, key & 0xFF), quantizer.FindNearest(key >> 16, (key >> 8) & 0xFF, key & 0xFF));
        }
    }

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(2, 0, true)]
    [InlineData(7, 0, true)]
    [InlineData(64, 0, true)]
    [InlineData(255, 0, true)]
    [InlineData(256, 0, true)]
    [InlineData(256, 0, false)]
    [InlineData(256, 3, true)]
    [InlineData(256, 3, false)]
    [InlineData(256, 6, true)]
    [InlineData(256, 6, false)]
    [InlineData(40, 7, true)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void GifColorMappingMatchesTheLinearSearch(int colors, int clusterBits, bool useVectors)
    {
        // clusterBits > 0 packs the palette into a small cube (many candidates per cell, ties, and cells that cannot hold
        // their candidates); clusterBits 7 with few colors gives every entry the same red sample
        var random = new Random(colors + clusterBits);
        using var quantizer = new PaletteQuantizer(new AllocationScope(new ImageResourceLimits())) { UseVectors = useVectors };
        quantizer.Begin(256);
        var keys = new HashSet<int>();
        while (keys.Count < colors)
        {
            keys.Add(clusterBits switch
            {
                0 => random.Next(0, 1 << 24),
                7 => (100 << 16) | random.Next(0, 1 << 16),
                _ => (random.Next(96, 96 + (1 << clusterBits)) << 16) | (random.Next(30, 30 + (1 << clusterBits)) << 8) | random.Next(200, 200 + (1 << clusterBits)),
            });
        }

        quantizer.Add([.. keys]);
        quantizer.BuildPalette(256);
        Assert.Equal(colors, quantizer.PaletteCount);
        for (var i = 0; i < 50_000; i++)
        {
            // Uniform colors, and colors around the palette (the previous result is the hint of the next search)
            var (r, g, b) = (random.Next(256), random.Next(256), random.Next(256));
            if (i % 2 == 1)
            {
                var near = quantizer.Palette[random.Next(colors)];
                r = Math.Clamp((near >> 16) + random.Next(-20, 21), 0, 255);
                g = Math.Clamp(((near >> 8) & 0xFF) + random.Next(-20, 21), 0, 255);
                b = Math.Clamp((near & 0xFF) + random.Next(-20, 21), 0, 255);
            }

            var expected = quantizer.FindNearestScalar(r, g, b);
            Assert.Equal(expected, quantizer.Map((r << 16) | (g << 8) | b));
            Assert.Equal(expected, quantizer.FindNearest(r, g, b, random.Next(colors)));
            Assert.Equal(expected, quantizer.FindNearestSorted(r, g, b, random.Next(colors)));
        }

        foreach (var key in quantizer.Palette.ToArray())
        {
            Assert.Equal(key, quantizer.Palette[quantizer.Map(key)]);
        }
    }

    [Fact]
    public void GifSortKeysAreSortedByTheRadixPasses()
    {
        // Unique 23-bit keys, below and above the length from which the counting passes are used
        foreach (var length in new[] { 0, 1, 2, 255, 256, 257, 1000, 32768 })
        {
            var keys = new int[length];
            for (var i = 0; i < length; i++)
            {
                keys[i] = (int)(((uint)i * 2654435761u) >> 9) ^ (i & 1);
            }

            keys = [.. keys.Distinct()];
            var expected = keys.Order().ToArray();
            PaletteQuantizer.SortKeys(keys, new int[keys.Length]);
            Assert.Equal(expected, keys);
        }
    }

    [Fact]
    public void GifNearestColorTiesSelectTheLowestIndex()
    {
        using var quantizer = new PaletteQuantizer(new AllocationScope(new ImageResourceLimits()));
        quantizer.Begin(256);

        // A grid with step 2: (2i, 0, 0) for i in 0..31; odd red values are equidistant from two entries
        var keys = Enumerable.Range(0, 32).Select(i => (2 * i) << 16).ToArray();
        quantizer.Add(keys);
        quantizer.BuildPalette(256);
        for (var red = 0; red < 64; red++)
        {
            var expected = quantizer.FindNearestScalar(red, 0, 0);
            Assert.Equal(red / 2, expected);
            Assert.Equal(expected, quantizer.FindNearest(red, 0, 0));
            Assert.Equal(expected, quantizer.FindNearestSorted(red, 0, 0, hint: 31));
            Assert.Equal(expected, quantizer.Map(red << 16));
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegInverseDctMatchesTheScalarReferenceBitForBit()
    {
        var random = new Random(20);
        const int Stride = 13;
        for (var round = 0; round < 20_000; round++)
        {
            // Dequantized coefficients: sparse or dense, small or extreme (12-bit range times 16-bit quantization values)
            var block = new double[64];
            var density = random.NextDouble();
            var magnitude = round % 4 == 0 ? 2047 * 255 : round % 4 == 1 ? 1023 : 64;
            for (var i = 0; i < 64; i++)
            {
                if (i == 0 || random.NextDouble() < density)
                {
                    block[i] = random.Next(-magnitude, magnitude + 1);
                }
            }

            var expected = new byte[(8 * Stride) + 8];
            var actual = new byte[(8 * Stride) + 8];
            JpegIdct.TransformScalar(block.ToArray(), expected, Stride);
            JpegIdct.Transform(block.ToArray(), actual, Stride);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void JpegColorConversionMatchesTheScalarReferenceExhaustively()
    {
        // Every (Y, Cb, Cr) triple: one row per (Cb, Cr) pair with Y = 0..255, plus odd row lengths for the remainders
        var luma = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            luma[i] = (byte)i;
        }

        var blue = new byte[256];
        var red = new byte[256];
        var expected = new byte[768];
        var actual = new byte[768];
        for (var cb = 0; cb < 256; cb++)
        {
            for (var cr = 0; cr < 256; cr++)
            {
                Array.Fill(blue, (byte)cb);
                Array.Fill(red, (byte)cr);
                var length = 256 - ((cb + cr) % 23);
                JpegRowWriter.ConvertYCbCrScalar(luma.AsSpan(0, length), blue.AsSpan(0, length), red.AsSpan(0, length), expected.AsSpan(0, 3 * length));
                JpegRowWriter.ConvertYCbCr(luma.AsSpan(0, length), blue.AsSpan(0, length), red.AsSpan(0, length), actual.AsSpan(0, 3 * length));
                Assert.True(expected.AsSpan(0, 3 * length).SequenceEqual(actual.AsSpan(0, 3 * length)), $"Cb = {cb}, Cr = {cr}");
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegUpsamplingMatchesTheScalarReference()
    {
        var random = new Random(420);
        for (var length = 1; length < 200; length++)
        {
            for (var round = 0; round < 6; round++)
            {
                var near = new byte[length];
                var far = new byte[length];
                random.NextBytes(near);
                random.NextBytes(far);
                if (round == 1)
                {
                    Array.Fill(near, (byte)255);
                    Array.Fill(far, (byte)255);
                }

                // Output widths of 2n and 2n - 1 (odd frame widths)
                foreach (var outputLength in new[] { 2 * length, (2 * length) - 1 })
                {
                    var expected = new byte[outputLength];
                    var actual = new byte[outputLength];
                    JpegRowWriter.UpsampleBothScalar(near, far, expected);
                    JpegRowWriter.UpsampleBoth(near, far, actual);
                    Assert.Equal(expected, actual);

                    JpegRowWriter.UpsampleHorizontalScalar(near, expected);
                    JpegRowWriter.UpsampleHorizontal(near, actual);
                    Assert.Equal(expected, actual);
                }

                foreach (var bias in new[] { 1, 2 })
                {
                    var expected = new byte[length];
                    var actual = new byte[length];
                    JpegRowWriter.UpsampleVerticalScalar(near, far, bias, expected);
                    JpegRowWriter.UpsampleVertical(near, far, bias, actual);
                    Assert.Equal(expected, actual);
                }
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegForwardDctMatchesTheScalarReference()
    {
        var random = new Random(85);
        var samples = new float[64];
        var quantization = new ushort[64];
        var scratch = new double[64];
        var expected = new int[64];
        var actual = new int[64];
        for (var round = 0; round < 20_000; round++)
        {
            for (var i = 0; i < 64; i++)
            {
                // Level-shifted samples: integers, chroma means (multiples of 0.25) and flat blocks (exact DC quotients and ties)
                samples[i] = round % 5 == 0 ? (round % 256) - 128 : round % 5 == 1 ? (random.Next(0, 1021) / 4f) - 128 : random.Next(-128, 128);
                quantization[i] = (ushort)(round % 3 == 0 ? 1 : random.Next(1, 256));
            }

            JpegForwardDct.TransformAndQuantizeScalar(samples, quantization, scratch, expected);
            JpegForwardDct.TransformAndQuantize(samples, quantization, scratch, actual);
            Assert.Equal(expected, actual);
        }
    }

    public static TheoryData<PixelFormat, ResamplingFilter, ResizeWorkingSpace> ResizeCases()
    {
        var data = new TheoryData<PixelFormat, ResamplingFilter, ResizeWorkingSpace>();
        foreach (var format in new[] { PixelFormat.Rgba32, PixelFormat.Bgra32, PixelFormat.Rgb24, PixelFormat.Gray8, PixelFormat.Rgba64, PixelFormat.Gray16 })
        {
            foreach (var filter in new[] { ResamplingFilter.Bilinear, ResamplingFilter.Bicubic, ResamplingFilter.Lanczos3 })
            {
                data.Add(format, filter, ResizeWorkingSpace.Encoded);
            }

            data.Add(format, ResamplingFilter.Bicubic, ResizeWorkingSpace.LinearSrgb);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ResizeCases))]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ResizeVectorizedPassesMatchTheScalarLoops(PixelFormat format, ResamplingFilter filter, ResizeWorkingSpace workingSpace)
    {
        var random = new Random((int)format);
        (int Width, int Height, int TargetWidth, int TargetHeight)[] sizes = [(37, 23, 11, 9), (37, 23, 80, 51), (5, 64, 3, 7), (64, 5, 129, 2), (1, 9, 4, 4), (101, 77, 100, 76)];
        foreach (var (width, height, targetWidth, targetHeight) in sizes)
        {
            var bytes = new byte[width * height * PixelFormats.GetBytesPerPixel(format)];
            random.NextBytes(bytes);
            var options = new ResizeOptions(targetWidth, targetHeight) { Mode = ResizeMode.Stretch, Filter = filter, WorkingSpace = workingSpace, AllowUpscaling = true };
            var expected = Resize(bytes, forceScalar: true);
            var actual = Resize(bytes, forceScalar: false);
            Assert.Equal(expected, actual);

            byte[] Resize(byte[] source, bool forceScalar)
            {
                using var image = ImportBytes(format, source, width, height);
                Resampler.TestForceScalar = forceScalar;
                try
                {
                    image.Resize(options);
                }
                finally
                {
                    Resampler.TestForceScalar = false;
                }

                var result = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(format)];
                image.Frames[0].CopyPixelBytesTo(result);
                return result;
            }
        }
    }

    public static TheoryData<PixelFormat, ConvolutionEdgeMode, ConvolutionWorkingSpace, bool> ConvolutionCases()
    {
        var data = new TheoryData<PixelFormat, ConvolutionEdgeMode, ConvolutionWorkingSpace, bool>();
        foreach (var format in new[] { PixelFormat.Rgba32, PixelFormat.Bgra32, PixelFormat.Rgb24, PixelFormat.Gray8, PixelFormat.Rgba64, PixelFormat.Gray16 })
        {
            foreach (var mode in Enum.GetValues<ConvolutionEdgeMode>())
            {
                data.Add(format, mode, ConvolutionWorkingSpace.Encoded, false);
            }

            data.Add(format, ConvolutionEdgeMode.Mirror, ConvolutionWorkingSpace.Encoded, true);
            data.Add(format, ConvolutionEdgeMode.Clamp, ConvolutionWorkingSpace.LinearSrgb, false);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ConvolutionCases))]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ConvolutionVectorizedRowsMatchTheScalarLoops(PixelFormat format, ConvolutionEdgeMode mode, ConvolutionWorkingSpace workingSpace, bool preserveAlpha)
    {
        // Row lengths that are and are not multiples of the vector width, and kernels wider or taller than the image
        var random = new Random((int)format);
        (int Width, int Height, int KernelWidth, int KernelHeight)[] cases = [(37, 23, 3, 3), (37, 23, 5, 7), (5, 64, 9, 1), (64, 5, 1, 9), (1, 9, 3, 5), (2, 2, 7, 7), (101, 77, 3, 3)];
        foreach (var (width, height, kernelWidth, kernelHeight) in cases)
        {
            var bytes = new byte[width * height * PixelFormats.GetBytesPerPixel(format)];
            random.NextBytes(bytes);
            var weights = new double[kernelWidth * kernelHeight];
            for (var i = 0; i < weights.Length; i++)
            {
                // Signed weights of various magnitudes, some of them zero (skipped terms)
                weights[i] = i % 4 == 3 ? 0 : (random.NextDouble() - 0.4) / weights.Length * 3;
            }

            var options = new ConvolutionOptions(new ConvolutionKernel(kernelWidth, kernelHeight, weights)) { EdgeMode = mode, WorkingSpace = workingSpace, PreserveAlpha = preserveAlpha };
            var expected = Convolve(bytes, forceScalar: true);
            var actual = Convolve(bytes, forceScalar: false);
            Assert.Equal(expected, actual);

            byte[] Convolve(byte[] source, bool forceScalar)
            {
                using var image = ImportBytes(format, source, width, height);
                Resampler.TestForceScalar = forceScalar;
                try
                {
                    image.Convolve(options);
                }
                finally
                {
                    Resampler.TestForceScalar = false;
                }

                var result = new byte[source.Length];
                image.Frames[0].CopyPixelBytesTo(result);
                return result;
            }
        }
    }

    [Fact]
    public void PixelConversionsMatchTheScalarReference()
    {
        CheckConversionsFrom<Rgba32>();
        CheckConversionsFrom<Bgra32>();
        CheckConversionsFrom<Rgb24>();
        CheckConversionsFrom<Rgba64>();
        CheckConversionsFrom<Gray8>();
        CheckConversionsFrom<Gray16>();

        static void CheckConversionsFrom<TSource>()
            where TSource : unmanaged
        {
            CheckConversion<TSource, Rgba32>();
            CheckConversion<TSource, Bgra32>();
            CheckConversion<TSource, Rgb24>();
            CheckConversion<TSource, Rgba64>();
            CheckConversion<TSource, Gray8>();
            CheckConversion<TSource, Gray16>();
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void RedBlueExchangeMatchesTheScalarReference()
    {
        var random = new Random(605);
        for (var pixels = 0; pixels <= 40; pixels++)
        {
            var source = new byte[pixels * 3];
            random.NextBytes(source);
            var expected = new byte[source.Length];
            PixelConverter.SwapRedAndBlue24Scalar(source, expected);

            // A longer destination keeps its other bytes
            var actual = new byte[source.Length + 5];
            Array.Fill(actual, (byte)0xA5);
            PixelConverter.SwapRedAndBlue24(source, actual);
            Assert.Equal(expected, actual[..source.Length]);
            Assert.All(actual[source.Length..], value => Assert.Equal(0xA5, value));

            // In place
            PixelConverter.SwapRedAndBlue24(source, source);
            Assert.Equal(expected, source);
        }
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static void CheckConversion<TSource, TDestination>()
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        var random = new Random(typeof(TSource).Name.Length + (31 * typeof(TDestination).Name.Length));
        var sourceSize = unsafe(System.Runtime.CompilerServices.Unsafe.SizeOf<TSource>());
        var sourceFormat = PixelFormats.GetPixelFormat<TSource>();
        Rgba64? opaqueBackground = new Rgba64(0x1234, 0xABCD, 0x00FF, ushort.MaxValue);
        for (var length = 0; length <= 40; length++)
        {
            for (var round = 0; round < 6; round++)
            {
                var source = new TSource[length];
                var bytes = unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(source.AsSpan()));
                random.NextBytes(bytes);
                if (round >= 2 && PixelFormats.HasAlpha(sourceFormat))
                {
                    // Opaque rows (the only ones that convert without a background), with at most one other pixel
                    var alphaSize = sourceSize / 4;
                    for (var i = 0; i < length; i++)
                    {
                        bytes.Slice((i * sourceSize) + (3 * alphaSize), alphaSize).Fill(0xFF);
                    }

                    if (round == 5 && length > 0)
                    {
                        bytes[(random.Next(length) * sourceSize) + (3 * alphaSize)] = 0x7F;
                    }

                    Assert.Equal(PixelConverter.IndexOfNonOpaqueScalar<TSource>(source), PixelConverter.IndexOfNonOpaque<TSource>(source));
                }

                foreach (var background in new[] { null, opaqueBackground })
                {
                    // A longer destination: only the pixels of the source are written
                    var expected = new TDestination[length + 2];
                    var actual = new TDestination[length + 2];
                    var expectedBytes = unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(expected.AsSpan()));
                    var actualBytes = unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(actual.AsSpan()));
                    expectedBytes.Fill(0xA5);
                    actualBytes.Fill(0xA5);
                    var expectedError = Record.Exception(() => PixelConverter.ConvertRowScalar<TSource, TDestination>(source, expected, background));
                    var actualError = Record.Exception(() => PixelConverter.ConvertRow<TSource, TDestination>(source, actual, background));
                    Assert.Equal(expectedError?.GetType(), actualError?.GetType());
                    Assert.Equal(expectedError?.Message, actualError?.Message);
                    Assert.Equal(expectedBytes.ToArray(), actualBytes.ToArray());
                }
            }
        }

        // In-place conversion between formats of the same size
        if (sourceSize == unsafe(System.Runtime.CompilerServices.Unsafe.SizeOf<TDestination>()))
        {
            var source = new TSource[37];
            var bytes = unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(source.AsSpan()));
            random.NextBytes(bytes);
            var expected = new TDestination[source.Length];
            PixelConverter.ConvertRowScalar<TSource, TDestination>(source, expected, opaqueBackground);
            PixelConverter.ConvertRow<TSource, TDestination>(source, unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<TSource, TDestination>(source.AsSpan())), opaqueBackground);
            Assert.Equal(unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(expected.AsSpan())).ToArray(), bytes.ToArray());
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void WebPForwardDctMatchesTheMatrixProducts()
    {
        var random = new Random(603);
        var residual = new int[16];
        var expected = new int[16];
        var actual = new int[16];
        for (var round = 0; round < 200_000; round++)
        {
            for (var i = 0; i < residual.Length; i++)
            {
                // Residuals of 8-bit samples; extreme blocks maximize every sum
                residual[i] = (round % 4) switch
                {
                    0 => random.Next(-255, 256),
                    1 => random.Next(2) == 0 ? -255 : 255,
                    2 => random.Next(-3, 4),
                    _ => (round & (1 << i)) != 0 ? 255 : -255,
                };
            }

            Vp8ForwardTransforms.ForwardDctReference(residual, expected);
            Vp8ForwardTransforms.ForwardDct(residual, actual);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void WebPQuantizationMatchesTheDivisions()
    {
        // Every step up to beyond the largest VP8 quantizer step, with every small magnitude, the magnitudes around the
        // multiples of the step and large ones
        var random = new Random(604);
        var coefficients = new int[16];
        var expected = new short[16];
        var actual = new short[16];
        for (var step = 1; step <= 1024; step++)
        {
            var dcStep = 1 + ((step * 7) % 500);
            for (var round = 0; round < 300; round++)
            {
                for (var i = 0; i < coefficients.Length; i++)
                {
                    var magnitude = (round % 4) switch
                    {
                        0 => (round * 16 / 4) + i, // 0, 1, 2, ...
                        1 => Math.Max(0, (random.Next(1, 3000) * step / 3) + random.Next(-2, 3)), // around the rounding boundaries
                        2 => random.Next(0, 70_000),
                        _ => random.Next(0, 10_000_000),
                    };
                    coefficients[i] = random.Next(2) == 0 ? magnitude : -magnitude;
                }

                foreach (var first in new[] { 0, 1 })
                {
                    Vp8Encoder.QuantizeReference(coefficients, expected, dcStep, step, first);
                    Vp8Encoder.Quantize(coefficients, actual, dcStep, step, first);
                    Assert.Equal(expected, actual);
                }
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void WebPColorConversionMatchesTheScalarReference()
    {
        var random = new Random(601);
        for (var length = 0; length <= 70; length++)
        {
            for (var round = 0; round < 40; round++)
            {
                var luma = new byte[length];
                var blue = new byte[length];
                var red = new byte[length];
                var alpha = new byte[length];
                random.NextBytes(luma);
                random.NextBytes(blue);
                random.NextBytes(red);
                random.NextBytes(alpha);
                if (round % 4 == 1)
                {
                    // Extreme samples: every clamp, in both directions
                    for (var i = 0; i < length; i++)
                    {
                        luma[i] = (byte)(random.Next(2) * 255);
                        blue[i] = (byte)(random.Next(2) * 255);
                        red[i] = (byte)(random.Next(2) * 255);
                    }
                }

                foreach (var withAlpha in new[] { false, true })
                {
                    var expected = new byte[length * 4];
                    var actual = new byte[length * 4];
                    WebPYuvConverter.ConvertSamplesScalar(luma, blue, red, withAlpha ? alpha : [], expected);
                    WebPYuvConverter.ConvertSamples(luma, blue, red, withAlpha ? alpha : [], actual);
                    Assert.Equal(expected, actual);
                }
            }
        }

        // Every (luma, chroma) pair with the other chroma sample at its extremes and at its center
        var lumas = new byte[256 * 256];
        var chromas = new byte[256 * 256];
        for (var i = 0; i < lumas.Length; i++)
        {
            lumas[i] = (byte)(i >> 8);
            chromas[i] = (byte)i;
        }

        foreach (var other in new byte[] { 0, 128, 255 })
        {
            var constant = new byte[lumas.Length];
            Array.Fill(constant, other);
            foreach (var (blue, red) in new[] { (chromas, constant), (constant, chromas) })
            {
                var expected = new byte[lumas.Length * 4];
                var actual = new byte[lumas.Length * 4];
                WebPYuvConverter.ConvertSamplesScalar(lumas, blue, red, [], expected);
                WebPYuvConverter.ConvertSamples(lumas, blue, red, [], actual);
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void WebPChromaUpsamplingMatchesTheScalarReference()
    {
        var random = new Random(602);
        for (var length = 1; length <= 70; length++)
        {
            for (var round = 0; round < 30; round++)
            {
                var near = new byte[length];
                var far = new byte[length];
                random.NextBytes(near);
                random.NextBytes(far);
                if (round % 3 == 1)
                {
                    Array.Fill(near, (byte)255);
                    Array.Fill(far, (byte)255);
                }

                // An even and an odd luma width
                foreach (var width in new[] { 2 * length, (2 * length) - 1 })
                {
                    var expected = new byte[width];
                    var actual = new byte[width];
                    WebPYuvConverter.UpsampleSamplesScalar(near, far, expected, 0, length);
                    WebPYuvConverter.UpsampleSamples(near, far, actual);
                    Assert.Equal(expected, actual);
                }
            }
        }
    }

    [Theory]
    [InlineData(8, 8, 8)]
    [InlineData(8, 8, 10)]
    [InlineData(8, 8, 12)]
    [InlineData(4, 4, 8)]
    [InlineData(4, 8, 12)]
    [InlineData(8, 4, 10)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Av1CdefInteriorRowsMatchTheScalarReference(int width, int height, int bitDepth)
    {
        // A window of 2 rows and 2 samples around the block, in a wider buffer; every direction offset reaches at most 2
        // rows and 2 samples from a sample
        var random = new Random((width * 100) + (height * 10) + bitDepth);
        const int Stride = 24;
        var maxSample = (1 << bitDepth) - 1;
        var coeffShift = bitDepth - 8;
        var offsets = new int[12];
        for (var round = 0; round < 2000; round++)
        {
            var window = new ushort[(height + 4) * Stride];
            for (var i = 0; i < window.Length; i++)
            {
                window[i] = (round % 5) switch
                {
                    0 => (ushort)random.Next(maxSample + 1),
                    1 => (ushort)(random.Next(2) * maxSample), // extreme differences
                    2 => (ushort)Math.Clamp((maxSample / 2) + random.Next(-3, 4), 0, maxSample), // flat: small differences
                    3 => (ushort)Math.Clamp((i % Stride * maxSample / Stride) + random.Next(-8, 9), 0, maxSample), // a ramp
                    _ => random.Next(4) == 0 ? (ushort)random.Next(maxSample + 1) : (ushort)(maxSample / 3),
                };
            }

            for (var t = 0; t < offsets.Length; t += 2)
            {
                offsets[t] = (random.Next(-2, 3) * Stride) + random.Next(-2, 3);
                offsets[t + 1] = -offsets[t];
            }

            // Strengths of the frame header (primary 0 to 15, secondary 0 to 4) at the bit depth, and the damping shifts
            var priStr = random.Next(4) == 0 ? 0 : random.Next(1, 16) << coeffShift;
            var secStr = random.Next(4) == 0 ? 0 : random.Next(1, 5) << coeffShift;
            var damping = random.Next(3, 7) + coeffShift;
            var priShift = priStr == 0 ? 0 : Math.Max(0, damping - (int)Math.Floor(Math.Log2(priStr)));
            var secShift = secStr == 0 ? 0 : Math.Max(0, damping - 1 - (int)Math.Floor(Math.Log2(secStr)));
            var (priTap0, priTap1) = random.Next(2) == 0 ? (4, 2) : (3, 3);
            var expected = new ushort[height * Stride];
            var actual = new ushort[height * Stride];
            Av1Cdef.FilterInteriorRowsScalar(expected, window, Stride, width, height, offsets, priStr, secStr, priShift, secShift, priTap0, priTap1, 2, 1);
            Av1Cdef.FilterInteriorRows(actual, window, Stride, width, height, offsets, priStr, secStr, priShift, secShift, priTap0, priTap1, 2, 1);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ChecksumsMatchTheByteAtATimeReferences()
    {
        // Published check values: CRC-32 of "123456789" is 0xCBF43926, Adler-32 of "Wikipedia" is 0x11E60398
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));
        Assert.Equal(0x11E60398u, BoundedInflater.ComputeAdler32("Wikipedia"u8));

        var random = new Random(32);
        var data = new byte[70_000];
        random.NextBytes(data);
        int[] lengths = [0, 1, 7, 8, 9, 15, 16, 17, 63, 64, 65, 255, 5535, 5536, 5537, 5552, 11_073, 70_000];
        foreach (var length in lengths)
        {
            foreach (var initial in new uint[] { 1, 0xFFF0FFF0, 0x12345678 % 65521 })
            {
                var span = data.AsSpan(0, length);
                Assert.Equal(Crc32.UpdateScalar(initial, span), Crc32.Update(initial, span));
                Assert.Equal(Crc32.UpdateScalar(initial, span), Crc32.UpdateTables(initial, span));
                var adler = ((initial >> 16) % 65521 << 16) | ((initial & 0xFFFF) % 65521);
                Assert.Equal(BoundedInflater.UpdateAdler32Scalar(adler, span), BoundedInflater.UpdateAdler32(adler, span));
            }
        }

        // Extreme bytes (all 0xFF) maximize every partial sum
        var ones = new byte[200_000];
        Array.Fill(ones, (byte)0xFF);
        Assert.Equal(BoundedInflater.UpdateAdler32Scalar(0xFFF0FFF0, ones), BoundedInflater.UpdateAdler32(0xFFF0FFF0, ones));
        Assert.Equal(Crc32.UpdateScalar(Crc32.Initial, ones), Crc32.Update(Crc32.Initial, ones));
        Assert.Equal(Crc32.UpdateScalar(Crc32.Initial, ones), Crc32.UpdateTables(Crc32.Initial, ones));
    }

    private static Image ImportBytes(PixelFormat format, byte[] bytes, int width, int height) => format switch
    {
        PixelFormat.Rgba32 => Image.ImportPixelBytes<Rgba32>(bytes, width, height),
        PixelFormat.Bgra32 => Image.ImportPixelBytes<Bgra32>(bytes, width, height),
        PixelFormat.Rgb24 => Image.ImportPixelBytes<Rgb24>(bytes, width, height),
        PixelFormat.Gray8 => Image.ImportPixelBytes<Gray8>(bytes, width, height),
        PixelFormat.Rgba64 => Image.ImportPixelBytes<Rgba64>(bytes, width, height),
        PixelFormat.Gray16 => Image.ImportPixelBytes<Gray16>(bytes, width, height),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    [Fact]
    public void ColorConversionInputTablesMatchTheScalarReferenceExhaustively()
    {
        // The first stage of an 8-bit conversion is read from 256-entry tables: every sample value of every channel, with
        // and without copied alpha and in both channel orders, gives exactly the samples of the stage-by-stage reference
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        (Metadata.IccProfile Source, Metadata.IccProfile Destination)[] pairs =
        [
            (Metadata.IccProfile.Srgb, displayP3),
            (displayP3, Metadata.IccProfile.Srgb),
            (IccTestProfiles.Rgb(IccTestProfiles.SrgbColorants, IccTestProfiles.Curve(0, 4096, 16384, 36864, 65535)), Metadata.IccProfile.Srgb),
            (IccTestProfiles.Gray(IccTestProfiles.Gamma(563)), Metadata.IccProfile.SrgbGray),
            (IccTestProfiles.GrayPaper(), Metadata.IccProfile.Srgb),
            (IccTestProfiles.RgbLabLut8(), Metadata.IccProfile.Srgb),
            (IccTestProfiles.RgbLabLutAToB(), IccTestProfiles.RgbXyzLut16()),
            (IccTestProfiles.CmykLabLut16(), Metadata.IccProfile.Srgb),
            (IccTestProfiles.CmykLabLutAToB(), IccTestProfiles.CmykLabLut16()),
        ];

        foreach (var (source, destination) in pairs)
        {
            foreach (var compensation in new[] { false, true })
            {
                var pipeline = IccPipeline.Create(source, destination, Metadata.IccRenderingIntent.RelativeColorimetric, compensation);
                Assert.NotNull(pipeline.ByteInputTables);
                var sourceChannels = pipeline.SourceChannelCount;
                var destinationChannels = pipeline.DestinationChannelCount;
                foreach (var (extra, reverse) in new[] { (0, false), (1, false), (1, true) })
                {
                    if (reverse && (sourceChannels != 3 || destinationChannels != 3))
                        continue;

                    // Each channel takes every value while the others follow a different sequence
                    var samples = new byte[256 * sourceChannels * (sourceChannels + extra)];
                    var position = 0;
                    for (var channel = 0; channel < sourceChannels; channel++)
                    {
                        for (var value = 0; value < 256; value++)
                        {
                            for (var other = 0; other < sourceChannels + extra; other++)
                            {
                                samples[position++] = other == channel ? (byte)value : (byte)((value * (37 + (60 * other))) + (91 * channel));
                            }
                        }
                    }

                    var colors = samples.Length / (sourceChannels + extra);
                    var expected = new byte[colors * (destinationChannels + extra)];
                    var actual = new byte[expected.Length];
                    ColorConversionKernels.ConvertScalar<byte, IccByteSample>(pipeline, samples, expected, sourceChannels + extra, destinationChannels + extra, reverse);
                    ColorConversionKernels.Convert<byte, IccByteSample>(pipeline, samples, actual, sourceChannels + extra, destinationChannels + extra, reverse);
                    Assert.Equal(expected, actual);
                }
            }
        }

        // Identical profiles have no stage and no table: the samples are copied
        Assert.Null(IccPipeline.Create(displayP3, displayP3, Metadata.IccRenderingIntent.Perceptual, blackPointCompensation: true).ByteInputTables);
    }
}
