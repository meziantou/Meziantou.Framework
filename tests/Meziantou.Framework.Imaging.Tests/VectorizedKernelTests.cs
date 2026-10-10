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
        using var quantizer = new GifQuantizer(new AllocationScope(new ImageResourceLimits()));
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

    [Fact]
    public void GifNearestColorTiesSelectTheLowestIndex()
    {
        using var quantizer = new GifQuantizer(new AllocationScope(new ImageResourceLimits()));
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
                var adler = ((initial >> 16) % 65521 << 16) | ((initial & 0xFFFF) % 65521);
                Assert.Equal(BoundedInflater.UpdateAdler32Scalar(adler, span), BoundedInflater.UpdateAdler32(adler, span));
            }
        }

        // Extreme bytes (all 0xFF) maximize every partial sum
        var ones = new byte[200_000];
        Array.Fill(ones, (byte)0xFF);
        Assert.Equal(BoundedInflater.UpdateAdler32Scalar(0xFFF0FFF0, ones), BoundedInflater.UpdateAdler32(0xFFF0FFF0, ones));
        Assert.Equal(Crc32.UpdateScalar(Crc32.Initial, ones), Crc32.Update(Crc32.Initial, ones));
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
}
