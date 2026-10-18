using System.Numerics;
using System.Runtime.Intrinsics;
using Meziantou.Framework.Imaging.Drawing;
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

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void PngSampleExpansionsMatchTheScalarReferences()
    {
        var random = new Random(59);
        for (var width = 0; width <= 37; width++)
        {
            var scanline = new byte[width * 6];
            random.NextBytes(scanline);
            Check(PngSampleFormat.ExpandGrayAlpha8Scalar, PngSampleFormat.ExpandGrayAlpha8, scanline.AsSpan(0, width * 2).ToArray(), width, width * 4);
            Check(PngSampleFormat.ExpandGrayAlpha16Scalar, PngSampleFormat.ExpandGrayAlpha16, scanline.AsSpan(0, width * 4).ToArray(), width, width * 8);
            Check(PngSampleFormat.ExpandRgb16OpaqueScalar, PngSampleFormat.ExpandRgb16Opaque, scanline, width, width * 8);
        }

        static void Check(Expansion reference, Expansion expansion, byte[] scanline, int width, int length)
        {
            // The bytes after the row must not be written
            var expected = new byte[length + 9];
            var actual = new byte[length + 9];
            reference(scanline, width, expected);
            expansion(scanline, width, actual);
            Assert.Equal(expected, actual);
        }
    }

    private delegate void Expansion(ReadOnlySpan<byte> scanline, int width, Span<byte> destination);

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void PngAdaptiveCostsOfEveryFilterMatchTheScalarReference()
    {
        var random = new Random(53);
        foreach (var bytesPerPixel in new[] { 1, 2, 3, 4, 6, 8 })
        {
            foreach (var length in new[] { 1, 2, 7, 15, 16, 17, 31, 32, 33, 40, 100, 1000, (300 * 32) + 5 })
            {
                if (length < bytesPerPixel)
                    continue;

                var row = new byte[length];
                var previous = new byte[length];
                for (var round = 0; round < 4; round++)
                {
                    random.NextBytes(row);
                    random.NextBytes(previous);
                    if (round == 1)
                    {
                        // Extremes: the largest magnitudes, and long rows whose partial sums are flushed
                        Array.Fill(row, (byte)0x80);
                        Array.Fill(previous, (byte)0);
                    }
                    else if (round == 2)
                    {
                        Array.Fill(row, (byte)0xFF);
                    }

                    var expected = new long[5];
                    var actual = new long[5];
                    PngFilters.GetAdaptiveCostsScalar(row, previous, bytesPerPixel, expected);
                    PngFilters.GetAdaptiveCosts(row, previous, bytesPerPixel, actual);
                    Assert.Equal(expected, actual);
                }
            }
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
    public void GifInvalidIndexSearchMatchesTheScalarReference()
    {
        var random = new Random(31);
        foreach (var length in new[] { 0, 1, 15, 16, 17, 31, 32, 33, 64, 100, 257 })
        {
            foreach (var entries in new[] { 1, 2, 16, 100, 128, 255, 256 })
            {
                foreach (var transparent in new[] { -1, 0, entries - 1, entries, 200, 255 })
                {
                    var indices = new byte[length];
                    for (var round = 0; round < 8; round++)
                    {
                        // Valid indices, with zero, one or several invalid ones (the transparent index included)
                        for (var i = 0; i < length; i++)
                        {
                            indices[i] = (byte)random.Next(entries);
                        }

                        for (var k = 0; k < round % 3 && length > 0; k++)
                        {
                            indices[random.Next(length)] = (byte)random.Next(256);
                        }

                        if (length > 0 && transparent >= 0 && round == 7)
                        {
                            indices[random.Next(length)] = (byte)transparent;
                        }

                        Assert.Equal(GifDecoder.FindInvalidIndexScalar(indices, entries, transparent), GifDecoder.FindInvalidIndex(indices, entries, transparent));
                    }
                }
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void AnimationOverRowsMatchTheScalarReference()
    {
        var random = new Random(37);
        foreach (var pixels in new[] { 0, 1, 3, 4, 5, 7, 8, 9, 16, 33, 100 })
        {
            for (var round = 0; round < 64; round++)
            {
                // Runs of opaque, transparent and translucent pixels over canvases of every kind
                var source = new byte[pixels * 4];
                var canvas = new byte[pixels * 4];
                random.NextBytes(source);
                random.NextBytes(canvas);
                var run = 1 + random.Next(8);
                for (var i = 0; i < pixels; i++)
                {
                    var mode = ((i / run) + round) % 4;
                    source[(i * 4) + 3] = mode switch
                    {
                        0 => 0,
                        1 => byte.MaxValue,
                        2 => (byte)random.Next(256),
                        _ => (byte)(random.Next(2) * 255),
                    };

                    canvas[(i * 4) + 3] = (round % 3) switch
                    {
                        0 => 0,
                        1 => byte.MaxValue,
                        _ => canvas[(i * 4) + 3],
                    };
                }

                var expected = canvas.ToArray();
                var actual = canvas.ToArray();
                AnimationCompositor.BlendOverRow8Scalar(source, expected);
                AnimationCompositor.BlendOverRow8(source, actual);
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void IndexedRowsMatchTheScalarReferences()
    {
        var random = new Random(43);
        var palette = new uint[256];
        for (var i = 0; i < palette.Length; i++)
        {
            palette[i] = (uint)random.Next() ^ ((uint)random.Next() << 16);
        }

        foreach (var bits in new[] { 1, 2, 4, 8 })
        {
            foreach (var width in new[] { 1, 2, 3, 7, 8, 9, 15, 16, 17, 31, 33, 100 })
            {
                var packed = new byte[((width * bits) + 7) / 8];
                for (var round = 0; round < 8; round++)
                {
                    random.NextBytes(packed);
                    foreach (var rgba in new[] { false, true })
                    {
                        var bytesPerPixel = rgba ? 4 : 3;

                        // The bytes after the row must not be written
                        var expected = new byte[(width * bytesPerPixel) + 5];
                        var actual = new byte[(width * bytesPerPixel) + 5];
                        IndexedPixels.ExpandScalar(packed, bits, width, palette, expected, rgba);
                        IndexedPixels.Expand(packed, bits, width, palette, actual, rgba);
                        Assert.Equal(expected, actual);
                    }

                    foreach (var entries in new[] { 1, 2, 3, 15, 16, 100, 255, 256 })
                    {
                        var expectedColumn = -1;
                        for (var x = 0; x < width; x++)
                        {
                            if (IndexedPixels.ReadIndex(packed, x, bits) >= entries)
                            {
                                expectedColumn = x;
                                break;
                            }
                        }

                        Assert.Equal(expectedColumn, IndexedPixels.FindIndexOutside(packed, bits, width, entries));
                        if (bits == 8)
                        {
                            Assert.Equal(IndexedPixels.FindAtLeastScalar(packed, entries), IndexedPixels.FindAtLeast(packed, entries));
                        }
                    }
                }
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void BgrxToRgbMatchesTheScalarReference()
    {
        var random = new Random(47);
        for (var pixels = 0; pixels <= 40; pixels++)
        {
            var source = new byte[pixels * 4];
            random.NextBytes(source);

            // The bytes after the row must not be written
            var expected = new byte[(pixels * 3) + 7];
            var actual = new byte[(pixels * 3) + 7];
            PackedPixels.Bgrx32ToRgb24Scalar(source, expected);
            PackedPixels.Bgrx32ToRgb24(source, actual);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void TiffSixteenBitDifferencingMatchesTheScalarReference()
    {
        var random = new Random(41);
        foreach (var samplesPerPixel in new[] { 1, 2, 3, 4, 5 })
        {
            foreach (var pixels in new[] { 1, 2, 3, 5, 8, 9, 16, 17, 33, 100 })
            {
                foreach (var bigEndian in new[] { false, true })
                {
                    var line = new byte[pixels * samplesPerPixel * 2];
                    for (var round = 0; round < 4; round++)
                    {
                        // Random differences, and extremes whose sums wrap around
                        random.NextBytes(line);
                        if (round == 1)
                        {
                            line.AsSpan().Fill(0xFF);
                        }
                        else if (round == 2)
                        {
                            for (var i = 0; i < line.Length; i++)
                            {
                                line[i] = (i & 2) == 0 ? (byte)0xFF : (byte)0x80;
                            }
                        }

                        var expected = line.ToArray();
                        var actual = line.ToArray();
                        TiffBlockReader.UndoDifferencing16Scalar(expected, samplesPerPixel, bigEndian);
                        TiffBlockReader.UndoDifferencing16(actual, samplesPerPixel, bigEndian);
                        Assert.Equal(expected, actual);
                    }
                }
            }
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
            // Every other block only has coefficients in its top-left 4x4 corner (the sparse transform)
            var block = new double[64];
            var density = random.NextDouble();
            var magnitude = round % 4 == 0 ? 2047 * 255 : round % 4 == 1 ? 1023 : 64;
            var sparse = round % 2 == 1;
            for (var i = 0; i < 64; i++)
            {
                if ((i == 0 || random.NextDouble() < density) && (!sparse || ((i & 0b100_100) == 0)))
                {
                    block[i] = random.Next(-magnitude, magnitude + 1);
                }
            }

            var expected = new byte[(8 * Stride) + 8];
            JpegIdct.TransformScalar(block.ToArray(), expected, Stride);
            AssertTransform(block, expected, (coefficients, destination) => JpegIdct.Transform(coefficients, destination, Stride));
            AssertTransform(block, expected, (coefficients, destination) => JpegIdct.Transform(coefficients, destination, Stride, sparse));
            if (System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated)
            {
                AssertTransform(block, expected, (coefficients, destination) => JpegIdct.TransformVector128(coefficients, destination, Stride, sparse));
            }

            if (System.Numerics.Vector.IsHardwareAccelerated && System.Numerics.Vector<double>.Count is 2 or 4 or 8)
            {
                AssertTransform(block, expected, (coefficients, destination) => JpegIdct.TransformVector(coefficients, destination, Stride, sparse));
            }
        }

        static void AssertTransform(double[] block, byte[] expected, Action<double[], byte[]> transform)
        {
            var actual = new byte[expected.Length];
            transform(block.ToArray(), actual);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegDequantizationMatchesTheScalarReference()
    {
        var random = new Random(21);
        var coefficients = new short[64];
        var quantization = new double[64];
        for (var round = 0; round < 2_000; round++)
        {
            // Every magnitude of a coefficient and of a 16-bit quantization value, and the extremes of both
            for (var i = 0; i < 64; i++)
            {
                coefficients[i] = round % 5 == 0 ? (random.Next(2) == 0 ? short.MinValue : short.MaxValue) : (short)random.Next(short.MinValue, short.MaxValue + 1);
                quantization[i] = round % 7 == 0 ? ushort.MaxValue : random.Next(1, ushort.MaxValue + 1);
            }

            var expected = new double[64];
            var actual = new double[64];
            JpegIdct.DequantizeScalar(coefficients, quantization, expected);
            JpegIdct.Dequantize(coefficients, quantization, actual);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void JpegSparseBlocksAreTheTopLeftQuadrant()
    {
        var coefficients = new short[64];
        Assert.True(JpegIdct.IsSparse(coefficients));
        for (var i = 0; i < 64; i++)
        {
            Array.Clear(coefficients);
            coefficients[i] = -1;
            var inQuadrant = i / 8 < 4 && i % 8 < 4;
            Assert.Equal(inQuadrant, JpegIdct.IsSparse(coefficients));
            Assert.Equal(inQuadrant, JpegIdct.IsSparse(i | 64));
        }

        // The first 10 coefficients in zig-zag order are all in the quadrant
        var naturalIndexes = 0;
        foreach (var natural in JpegIdct.ZigZag[..10])
        {
            naturalIndexes |= natural;
        }

        Assert.True(JpegIdct.IsSparse(naturalIndexes));
        Assert.False(JpegIdct.IsSparse(naturalIndexes | JpegIdct.ZigZag[10]));
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
    public void JpegPlaneInterleavingMatchesTheScalarReference()
    {
        var random = new Random(422);
        for (var length = 0; length < 100; length++)
        {
            var first = new byte[length];
            var second = new byte[length];
            var third = new byte[length];
            random.NextBytes(first);
            random.NextBytes(second);
            random.NextBytes(third);
            var expected = new byte[3 * length];
            var actual = new byte[3 * length];
            JpegRowWriter.InterleaveScalar(first, second, third, expected);
            JpegRowWriter.Interleave(first, second, third, actual);
            Assert.Equal(expected, actual);
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
                // Level-shifted samples: integers, chroma means (multiples of 0.25) and flat blocks (exact DC quotients and ties),
                // and samples far out of range (the clamping of the DC and AC coefficients)
                samples[i] = round % 7 == 6 ? random.Next(-20_000, 20_000) : round % 5 == 0 ? (round % 256) - 128 : round % 5 == 1 ? (random.Next(0, 1021) / 4f) - 128 : random.Next(-128, 128);
                quantization[i] = (ushort)(round % 3 == 0 ? 1 : random.Next(1, 256));
            }

            JpegForwardDct.TransformAndQuantizeScalar(samples, quantization, scratch, expected);
            JpegForwardDct.TransformAndQuantize(samples, quantization, scratch, actual);
            Assert.Equal(expected, actual);
            if (System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated)
            {
                Array.Clear(actual);
                JpegForwardDct.TransformAndQuantizeVector128(samples, quantization, scratch, actual);
                Assert.Equal(expected, actual);
            }

            if (System.Numerics.Vector.IsHardwareAccelerated && System.Numerics.Vector<double>.Count is 2 or 4 or 8)
            {
                Array.Clear(actual);
                JpegForwardDct.TransformAndQuantizeVector(samples, quantization, scratch, actual);
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegEncoderColorConversionMatchesTheScalarReference()
    {
        var random = new Random(601);
        for (var length = 0; length < 100; length++)
        {
            for (var round = 0; round < 4; round++)
            {
                // Random pixels, then the extremes: black, white and saturated primaries
                var source = new byte[3 * length];
                random.NextBytes(source);
                if (round > 0)
                {
                    for (var i = 0; i < source.Length; i++)
                    {
                        source[i] = (byte)(round == 1 ? 0 : round == 2 ? 255 : (i / 3 + i) % 3 == 0 ? 255 : 0);
                    }
                }

                var expected = new float[3][];
                var actual = new float[3][];
                for (var c = 0; c < 3; c++)
                {
                    expected[c] = new float[length];
                    actual[c] = new float[length];
                }

                JpegEncoderCodec.ConvertRgbToYCbCrScalar(source, expected[0], expected[1], expected[2]);
                JpegEncoderCodec.ConvertRgbToYCbCr(source, actual[0], actual[1], actual[2]);
                for (var c = 0; c < 3; c++)
                {
                    Assert.Equal(expected[c], actual[c]);
                }
            }
        }

        // Every gray level and every value of one channel with the others at both ends
        var all = new byte[3 * 256 * 3 * 4];
        var count = 0;
        for (var value = 0; value < 256; value++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                foreach (var other in new byte[] { 0, 255, 128, (byte)value })
                {
                    all[(3 * count) + channel] = (byte)value;
                    all[(3 * count) + ((channel + 1) % 3)] = other;
                    all[(3 * count) + ((channel + 2) % 3)] = (byte)(255 - other);
                    count++;
                }
            }
        }

        var expectedAll = new float[3][];
        var actualAll = new float[3][];
        for (var c = 0; c < 3; c++)
        {
            expectedAll[c] = new float[count];
            actualAll[c] = new float[count];
        }

        JpegEncoderCodec.ConvertRgbToYCbCrScalar(all, expectedAll[0], expectedAll[1], expectedAll[2]);
        JpegEncoderCodec.ConvertRgbToYCbCr(all, actualAll[0], actualAll[1], actualAll[2]);
        for (var c = 0; c < 3; c++)
        {
            Assert.Equal(expectedAll[c], actualAll[c]);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegEncoderNonZeroMaskMatchesTheScalarReference()
    {
        var random = new Random(603);
        var coefficients = new int[64];
        for (var round = 0; round < 5_000; round++)
        {
            // Sparse and dense blocks, every magnitude of a coefficient (multiples of 256 included), and single coefficients
            var density = random.NextDouble();
            for (var i = 0; i < 64; i++)
            {
                coefficients[i] = random.NextDouble() < density ? random.Next(-2048, 2048) * (round % 3 == 0 ? 256 : 1) : 0;
            }

            if (round < 64)
            {
                Array.Clear(coefficients);
                coefficients[round] = round % 2 == 0 ? int.MinValue : 1 << 16;
            }

            Assert.Equal(JpegEncoderCodec.GetNonZeroMaskScalar(coefficients), JpegEncoderCodec.GetNonZeroMask(coefficients));
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegEncoderBlockExtractionMatchesTheScalarReference()
    {
        var random = new Random(602);
        const int PlaneWidth = 40;
        var plane = new float[PlaneWidth * 17];
        foreach (var (factorX, factorY) in new[] { (1, 1), (2, 1), (2, 2), (1, 2) })
        {
            for (var round = 0; round < 200; round++)
            {
                // Level-shifted luma and chroma values (any float), including the extremes of the range
                for (var i = 0; i < plane.Length; i++)
                {
                    plane[i] = round % 4 == 0 ? (random.Next(2) == 0 ? -128f : 127f) : (float)((random.NextDouble() * 256) - 128);
                }

                var x0 = random.Next(0, PlaneWidth - (8 * factorX) + 1);
                var y0 = random.Next(0, 17 - (8 * factorY) + 1);
                var expected = new float[64];
                var actual = new float[64];
                JpegEncoderCodec.ExtractBlockScalar(plane, PlaneWidth, x0, y0, factorX, factorY, expected);
                JpegEncoderCodec.ExtractBlock(plane, PlaneWidth, x0, y0, factorX, factorY, actual);
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegXlInverseDctMatchesTheScalarReferenceBitForBit()
    {
        var random = new Random(181);
        var sizes = new[] { 4, 8, 16, 32, 64, 128, 256 };
        var expectedScratch = new float[JpegXlInverseTransform.ScratchLength];
        var actualScratch = new float[JpegXlInverseTransform.ScratchLength];
        foreach (var rows in sizes)
        {
            foreach (var columns in sizes)
            {
                var rounds = rows * columns >= 128 * 128 ? 2 : 40;
                for (var round = 0; round < rounds; round++)
                {
                    // Dequantized coefficients: sparse or dense, small or large, with signed zeros and extremes
                    var coefficients = new float[rows * columns];
                    var density = random.NextDouble();
                    for (var i = 0; i < coefficients.Length; i++)
                    {
                        if (i == 0 || random.NextDouble() < density)
                        {
                            coefficients[i] = (round % 5) switch
                            {
                                0 => random.Next(-2048, 2049),
                                1 => (float)((random.NextDouble() - 0.5) * 1e-3),
                                2 => random.Next(2) == 0 ? -0f : float.MaxValue / 1e6f,
                                _ => (float)((random.NextDouble() - 0.5) * 64),
                            };
                        }
                    }

                    var stride = columns + (round % 3);
                    var expected = new float[rows * stride];
                    var actual = new float[rows * stride];
                    JpegXlInverseTransform.InverseDctScalar(coefficients.ToArray(), rows, columns, expected, stride, expectedScratch);
                    JpegXlInverseTransform.InverseDct(coefficients.ToArray(), rows, columns, actual, stride, actualScratch);
                    Assert.Equal(Array.ConvertAll(expected, BitConverter.SingleToInt32Bits), Array.ConvertAll(actual, BitConverter.SingleToInt32Bits));
                }
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegXlForwardDctMatchesTheScalarReferenceBitForBit()
    {
        var random = new Random(187);
        var sizes = new[] { 4, 8, 16, 32, 64 };
        foreach (var rows in sizes)
        {
            foreach (var columns in sizes)
            {
                for (var round = 0; round < 30; round++)
                {
                    // Samples of a plane wider than the block: XYB-like values, flat blocks, signed zeros and large values
                    var stride = columns + (round % 5);
                    var samples = new float[rows * stride];
                    for (var i = 0; i < samples.Length; i++)
                    {
                        samples[i] = (round % 4) switch
                        {
                            0 => 0.125f,
                            1 => random.Next(2) == 0 ? 0f : -0f,
                            2 => (float)((random.NextDouble() - 0.5) * 1e6),
                            _ => (float)(random.NextDouble() - 0.3),
                        };
                    }

                    var expected = new float[rows * columns];
                    var actual = new float[rows * columns];
                    JpegXlForwardTransform.ForwardDctScalar(samples, stride, rows, columns, expected, new float[rows * columns]);
                    JpegXlForwardTransform.ForwardDct(samples, stride, rows, columns, actual, new float[rows * columns]);
                    Assert.Equal(Array.ConvertAll(expected, BitConverter.SingleToInt32Bits), Array.ConvertAll(actual, BitConverter.SingleToInt32Bits));
                }
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegXlDequantizationMatchesTheScalarReferenceBitForBit()
    {
        var random = new Random(182);
        for (var round = 0; round < 3000; round++)
        {
            // Every length up to two blocks (remainders included); quantized values around the bias cases and extremes
            var size = round < 200 ? round % 131 : 64 << (round % 4);
            int Quantized() => (round % 7) switch
            {
                0 => random.Next(-3, 4),
                1 => random.Next(2) == 0 ? int.MaxValue - random.Next(3) : int.MinValue + random.Next(3),
                _ => random.Next(-300, 301),
            };

            var quantized = new int[3][];
            var matrices = new float[3][];
            for (var c = 0; c < 3; c++)
            {
                quantized[c] = new int[size];
                matrices[c] = new float[size];
                for (var k = 0; k < size; k++)
                {
                    quantized[c][k] = Quantized();
                    matrices[c][k] = (float)(random.NextDouble() * (round % 5 == 0 ? 1e-6 : 4)) * (round % 11 == 0 ? -1 : 1);
                }
            }

            var steps = new JpegXlVarDctDecoder.DequantizationSteps(
                StepY: (float)random.NextDouble() * 3,
                StepX: (float)random.NextDouble(),
                StepB: (float)random.NextDouble() * 2,
                CorrelationX: (float)(random.NextDouble() - 0.5),
                CorrelationB: round % 3 == 0 ? 0 : (float)random.NextDouble(),
                BiasX: (float)random.NextDouble(),
                BiasY: (float)random.NextDouble(),
                BiasB: round % 13 == 0 ? 0 : (float)random.NextDouble(),
                BiasNumerator: (float)random.NextDouble());
            var expected = new float[3][];
            var actual = new float[3][];
            for (var c = 0; c < 3; c++)
            {
                expected[c] = new float[size];
                actual[c] = new float[size];
            }

            JpegXlVarDctDecoder.DequantizeScalar(quantized[0], quantized[1], quantized[2], matrices[0], matrices[1], matrices[2], steps, expected[0], expected[1], expected[2]);
            JpegXlVarDctDecoder.Dequantize(quantized[0], quantized[1], quantized[2], matrices[0], matrices[1], matrices[2], steps, actual[0], actual[1], actual[2]);
            for (var c = 0; c < 3; c++)
            {
                Assert.Equal(Array.ConvertAll(expected[c], BitConverter.SingleToInt32Bits), Array.ConvertAll(actual[c], BitConverter.SingleToInt32Bits));
            }

            JpegXlVarDctDecoder.DequantizeScalar(quantized[2], matrices[2], steps.StepB, steps.BiasB, steps.BiasNumerator, expected[2]);
            JpegXlVarDctDecoder.Dequantize(quantized[2], matrices[2], steps.StepB, steps.BiasB, steps.BiasNumerator, actual[2]);
            Assert.Equal(Array.ConvertAll(expected[2], BitConverter.SingleToInt32Bits), Array.ConvertAll(actual[2], BitConverter.SingleToInt32Bits));
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegXlModularResidualsMatchTheScalarReference()
    {
        var random = new Random(186);
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(JpegXlModularResidualsMatchTheScalarReference));
        for (var round = 0; round < 600; round++)
        {
            // Pixel values, signed values near the limit of the 32-bit sums, and any value (the scalar 64-bit path)
            var width = round < 60 ? 1 + (round % 20) : random.Next(1, 70);
            var height = random.Next(1, 8);
            using var channel = new JpegXlModularChannel(width, height, 0, 0);
            channel.Allocate(scope);
            var samples = channel.Samples;
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = (round % 4) switch
                {
                    0 => random.Next(256),
                    1 => random.Next(-(1 << 26) + 1, 1 << 26),
                    2 => random.Next(2) == 0 ? int.MaxValue - random.Next(4) : int.MinValue + random.Next(4),
                    _ => random.Next(-70000, 70000),
                };
            }

            if (round % 4 == 1 && samples.Length > 1)
            {
                samples[random.Next(samples.Length)] = (1 << 26) - 1;
                samples[random.Next(samples.Length)] = -(1 << 26) + 1;
            }

            for (var predictor = 0; predictor < JpegXlMaTree.PredictorCount; predictor++)
            {
                var expected = new uint[samples.Length];
                var actual = new uint[samples.Length];
                JpegXlModularEncoder.ComputeResidualsScalar(channel, predictor, expected, scope);
                JpegXlModularEncoder.ComputeResiduals(channel, predictor, actual, scope);
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegXlIntegerRowsMatchTheScalarReference()
    {
        var random = new Random(188);
        for (var round = 0; round < 1500; round++)
        {
            // Samples in range, out of range on both sides, and other bit depths (scaled, never vectorized)
            var width = round < 80 ? round : random.Next(1, 200);
            var max = round % 5 == 4 ? (1 << random.Next(1, 17)) - 1 : byte.MaxValue;
            var alphaMax = (round % 3) switch
            {
                0 => 0,
                1 => byte.MaxValue,
                _ => (1 << random.Next(1, 17)) - 1,
            };
            int Sample(int largest) => round % 2 == 0 ? random.Next(largest + 1) : random.Next(-300, largest + 300);
            var channels = new int[4][];
            for (var c = 0; c < 4; c++)
            {
                channels[c] = new int[width];
                for (var x = 0; x < width; x++)
                {
                    channels[c][x] = Sample(c == 3 ? Math.Max(alphaMax, 1) : max);
                }
            }

            var expected = new byte[width * 3];
            var actual = new byte[width * 3];
            JpegXlDecoder.WriteRgb24RowScalar(channels[0], channels[1], channels[2], max, expected);
            JpegXlDecoder.WriteRgb24Row(channels[0], channels[1], channels[2], max, actual);
            Assert.Equal(expected, actual);

            expected = new byte[width * 4];
            actual = new byte[width * 4];
            JpegXlDecoder.WriteRgba32RowScalar(channels[0], channels[1], channels[2], channels[3], max, alphaMax, expected);
            JpegXlDecoder.WriteRgba32Row(channels[0], channels[1], channels[2], channels[3], max, alphaMax, actual);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegXlVerticalUnsqueezeMatchesTheScalarReference()
    {
        var random = new Random(191);
        for (var round = 0; round < 3000; round++)
        {
            // Monotonic and non-monotonic neighborhoods, equal values, and 32-bit extremes whose sums need 64 bits
            var length = round < 80 ? round : random.Next(1, 200);
            var rows = new int[4][];
            for (var r = 0; r < 4; r++)
            {
                rows[r] = new int[length];
            }

            for (var x = 0; x < length; x++)
            {
                switch (round % 5)
                {
                    case 0:
                        var start = random.Next(-1000, 1000);
                        var step = random.Next(-40, 41);
                        rows[0][x] = start;
                        rows[1][x] = start + step + random.Next(-3, 4);
                        rows[2][x] = start + (2 * step) + random.Next(-3, 4);
                        break;

                    case 1:
                        rows[0][x] = rows[1][x] = rows[2][x] = random.Next(-5, 5);
                        break;

                    case 2:
                        rows[0][x] = random.Next(2) == 0 ? int.MaxValue - random.Next(3) : int.MinValue + random.Next(3);
                        rows[1][x] = random.Next(int.MinValue, int.MaxValue);
                        rows[2][x] = random.Next(2) == 0 ? int.MaxValue : int.MinValue;
                        break;

                    default:
                        rows[0][x] = random.Next(-300, 300);
                        rows[1][x] = random.Next(-300, 300);
                        rows[2][x] = random.Next(-300, 300);
                        break;
                }

                rows[3][x] = round % 5 == 2 ? random.Next(int.MinValue, int.MaxValue) : random.Next(-50, 50);
            }

            var expectedFirst = new int[length];
            var expectedSecond = new int[length];
            var actualFirst = new int[length];
            var actualSecond = new int[length];
            JpegXlModularTransform.UnsqueezeRowsScalar(rows[0], rows[1], rows[2], rows[3], expectedFirst, expectedSecond);
            JpegXlModularTransform.UnsqueezeRows(rows[0], rows[1], rows[2], rows[3], actualFirst, actualSecond);
            Assert.Equal(expectedFirst, actualFirst);
            Assert.Equal(expectedSecond, actualSecond);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegXlCanvasRowsMatchTheScalarReference()
    {
        var random = new Random(189);
        float[] special = [float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0f, 0f, 1f, 254.5f / 255, 0.5f / 255, 1e30f, -1e30f, float.Epsilon];
        for (var round = 0; round < 1500; round++)
        {
            // Samples in range (rounding ties included), slightly and far out of range, and non-finite values
            var width = round < 80 ? round : random.Next(1, 200);
            var channels = new float[4][];
            for (var c = 0; c < 4; c++)
            {
                channels[c] = new float[width];
                for (var x = 0; x < width; x++)
                {
                    channels[c][x] = (round % 4) switch
                    {
                        0 => random.Next(256) / 255f,
                        1 => (float)((random.NextDouble() * 1.4) - 0.2),
                        2 => special[random.Next(special.Length)],
                        _ => (random.Next(512) + 0.5f) / 510f,
                    };
                }
            }

            foreach (var hasAlphaFormat in new[] { false, true })
            {
                foreach (var hasAlpha in new[] { false, true })
                {
                    var length = width * (hasAlphaFormat ? 4 : 3);
                    var expected = new byte[length];
                    var actual = new byte[length];
                    JpegXlDecoder.WriteCanvasRowScalar(channels[0], channels[1], channels[2], channels[3], hasAlphaFormat, hasAlpha, expected);
                    JpegXlDecoder.WriteCanvasRow(channels[0], channels[1], channels[2], channels[3], hasAlphaFormat, hasAlpha, actual);
                    Assert.Equal(expected, actual);
                }
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegXlColorMatricesMatchTheScalarReferenceBitForBit()
    {
        var random = new Random(185);
        for (var round = 0; round < 2000; round++)
        {
            var length = round < 100 ? round : random.Next(1, 300);
            var channels = new float[3][];
            for (var c = 0; c < 3; c++)
            {
                channels[c] = new float[length];
                for (var i = 0; i < length; i++)
                {
                    // XYB and YCbCr ranges, with zeros, signed zeros, values far out of range and subnormal values
                    channels[c][i] = (round % 5) switch
                    {
                        0 => random.Next(2) == 0 ? 0f : -0f,
                        1 => (float)((random.NextDouble() - 0.5) * 1e5),
                        2 => (float)random.NextDouble() * 1e-40f,
                        _ => (float)(random.NextDouble() - 0.5),
                    };
                }
            }

            var matrix = new float[9];
            for (var i = 0; i < matrix.Length; i++)
            {
                matrix[i] = (float)((random.NextDouble() - 0.3) * 12);
            }

            float[] biasRoot = [(float)random.NextDouble() * 0.2f, (float)random.NextDouble() * 0.2f, (float)random.NextDouble() * 0.2f];
            float[] bias = [-biasRoot[0] * biasRoot[0] * biasRoot[0], -biasRoot[1] * biasRoot[1] * biasRoot[1], -biasRoot[2] * biasRoot[2] * biasRoot[2]];
            var expected = Array.ConvertAll(channels, c => c.ToArray());
            var actual = Array.ConvertAll(channels, c => c.ToArray());
            JpegXlOutputColor.XybToLinearScalar(expected[0], expected[1], expected[2], matrix, biasRoot, bias);
            JpegXlOutputColor.XybToLinear(actual[0], actual[1], actual[2], matrix, biasRoot, bias);
            for (var c = 0; c < 3; c++)
            {
                Assert.Equal(Array.ConvertAll(expected[c], BitConverter.SingleToInt32Bits), Array.ConvertAll(actual[c], BitConverter.SingleToInt32Bits));
            }

            expected = Array.ConvertAll(channels, c => c.ToArray());
            actual = Array.ConvertAll(channels, c => c.ToArray());
            JpegXlColorTransform.YCbCrToRgbScalar(expected[0], expected[1], expected[2]);
            JpegXlColorTransform.YCbCrToRgb(actual[0], actual[1], actual[2]);
            for (var c = 0; c < 3; c++)
            {
                Assert.Equal(Array.ConvertAll(expected[c], BitConverter.SingleToInt32Bits), Array.ConvertAll(actual[c], BitConverter.SingleToInt32Bits));
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegXlGaborishRowsMatchTheScalarReferenceBitForBit()
    {
        var random = new Random(184);
        for (var round = 0; round < 2000; round++)
        {
            var width = round < 100 ? 1 + round : random.Next(1, 300);
            var rows = new float[3][];
            for (var r = 0; r < 3; r++)
            {
                rows[r] = new float[width + 2];
                for (var i = 0; i < rows[r].Length; i++)
                {
                    rows[r][i] = (round % 4) switch
                    {
                        0 => -0f,
                        1 => (float)random.NextDouble() * 1e-30f,
                        2 => (float)((random.NextDouble() - 0.5) * 1e6),
                        _ => (float)random.NextDouble(),
                    };
                }
            }

            var side = (float)random.NextDouble() * 0.2f;
            var corner = (float)random.NextDouble() * 0.1f;
            var scale = 1f / (1f + (4f * (side + corner)));
            var expected = new float[width];
            var actual = new float[width];
            JpegXlRestorationFilters.GaborishRowScalar(rows[0], rows[1], rows[2], scale, side * scale, corner * scale, expected);
            JpegXlRestorationFilters.GaborishRow(rows[0], rows[1], rows[2], scale, side * scale, corner * scale, actual);
            Assert.Equal(Array.ConvertAll(expected, BitConverter.SingleToInt32Bits), Array.ConvertAll(actual, BitConverter.SingleToInt32Bits));
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void JpegXlEdgePreservingFilterRowsMatchTheScalarReferenceBitForBit()
    {
        const int Border = 3;
        var random = new Random(183);

        // The neighbors and the patches of the three steps, as (row, column) offsets
        sbyte[] wideNeighbors = [-2, 0, -1, -1, -1, 0, -1, 1, 0, -2, 0, -1, 0, 1, 0, 2, 1, -1, 1, 0, 1, 1, 2, 0];
        sbyte[] crossNeighbors = [-1, 0, 0, -1, 0, 1, 1, 0];
        sbyte[] cross = [0, 0, -1, 0, 0, -1, 1, 0, 0, 1];
        for (var round = 0; round < 1500; round++)
        {
            var width = round < 120 ? 1 + (round % 40) : random.Next(1, 200);
            var step = round % 3;
            var paddedWidth = width + (2 * Border);
            var neighbors = step == 0 ? wideNeighbors : crossNeighbors;
            var patch = step == 2 ? cross[..2] : cross;
            var neighborOffsets = new int[neighbors.Length / 2];
            for (var i = 0; i < neighborOffsets.Length; i++)
            {
                neighborOffsets[i] = (neighbors[i * 2] * paddedWidth) + neighbors[(i * 2) + 1];
            }

            var patchOffsets = new int[patch.Length / 2];
            for (var i = 0; i < patchOffsets.Length; i++)
            {
                patchOffsets[i] = (patch[i * 2] * paddedWidth) + patch[(i * 2) + 1];
            }

            // Smooth, noisy, flat (equal samples: zero differences) and extreme planes
            var planes = new float[3][];
            for (var c = 0; c < 3; c++)
            {
                planes[c] = new float[paddedWidth * (1 + (2 * Border))];
                for (var i = 0; i < planes[c].Length; i++)
                {
                    planes[c][i] = (round % 4) switch
                    {
                        0 => 0.25f,
                        1 => (float)random.NextDouble() * 1e-3f,
                        2 => (float)((random.NextDouble() - 0.5) * 1e4),
                        _ => (float)random.NextDouble(),
                    };
                }
            }

            // Inverse strengths: blocks the filter leaves, and others
            var sigmaRow = new float[(width + 7) / 8];
            for (var i = 0; i < sigmaRow.Length; i++)
            {
                sigmaRow[i] = random.Next(5) == 0 ? -10 : (float)(-random.NextDouble() * 4);
            }

            var parameters = new JpegXlRestorationFilters.EdgePreservingStep(
                SigmaScale: (float)random.NextDouble() * 2,
                BorderScale: (float)random.NextDouble() * 3,
                ScaleX: (float)random.NextDouble() * 40,
                ScaleY: (float)random.NextDouble() * 2,
                ScaleB: (float)random.NextDouble());
            var isBorderRow = random.Next(2) == 0;
            var initial = new float[width];
            for (var i = 0; i < width; i++)
            {
                initial[i] = (float)random.NextDouble();
            }

            var expected = new[] { initial.ToArray(), initial.ToArray(), initial.ToArray() };
            var actual = new[] { initial.ToArray(), initial.ToArray(), initial.ToArray() };
            var rowStart = (Border * paddedWidth) + Border;
            JpegXlRestorationFilters.FilterRowScalar(planes[0], planes[1], planes[2], rowStart, width, neighborOffsets, patchOffsets, sigmaRow, isBorderRow, parameters, expected[0], expected[1], expected[2]);
            JpegXlRestorationFilters.FilterRow(planes[0], planes[1], planes[2], rowStart, width, neighborOffsets, patchOffsets, sigmaRow, isBorderRow, parameters, actual[0], actual[1], actual[2]);
            for (var c = 0; c < 3; c++)
            {
                Assert.Equal(Array.ConvertAll(expected[c], BitConverter.SingleToInt32Bits), Array.ConvertAll(actual[c], BitConverter.SingleToInt32Bits));
            }
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

    [Theory]
    [InlineData((int)WorkingAlpha.None)]
    [InlineData((int)WorkingAlpha.Premultiplied)]
    [InlineData((int)WorkingAlpha.Preserved)]
    public void WorkingSampleConversionsMatchTheScalarReference(int alphaMode)
    {
        CheckWorkingSamples<byte>((WorkingAlpha)alphaMode, byte.MaxValue);
        CheckWorkingSamples<ushort>((WorkingAlpha)alphaMode, ushort.MaxValue);

        [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
        static void CheckWorkingSamples<TSample>(WorkingAlpha alpha, int max)
            where TSample : unmanaged
        {
            var random = new Random(max + (int)alpha);
            for (var pixels = 0; pixels <= 40; pixels++)
            {
                // Random samples, with every sample (alpha included) often 0 or the maximum
                var samples = new TSample[pixels * 4];
                for (var i = 0; i < samples.Length; i++)
                {
                    var value = random.Next(4) switch
                    {
                        0 => 0,
                        1 => max,
                        _ => random.Next(max + 1),
                    };
                    samples[i] = typeof(TSample) == typeof(byte) ? (TSample)(object)(byte)value : (TSample)(object)(ushort)value;
                }

                var expected = new double[samples.Length];
                var actual = new double[samples.Length];
                WorkingSamples.LoadScalar<TSample>(samples, expected, alpha, transfer: 0);
                WorkingSamples.Load<TSample>(samples, actual, alpha, transfer: 0);
                Assert.Equal(expected.Select(BitConverter.DoubleToInt64Bits), actual.Select(BitConverter.DoubleToInt64Bits));

                // Filtered values: out of range, negative, around the rounding boundaries (k + 0.5 - 2^-20), and alphas
                // that round to zero or not
                var boundary = 0.5 - (1.0 / (1 << 20));
                var filtered = new double[samples.Length + 1];
                for (var i = 0; i < filtered.Length; i++)
                {
                    filtered[i] = random.Next(8) switch
                    {
                        0 => 0,
                        1 => random.Next(-2, max + 2) + boundary,
                        2 => Math.BitDecrement(random.Next(-2, max + 2) + boundary),
                        3 => (random.NextDouble() * 1.4 * max) - (0.2 * max),
                        4 => random.Next(max + 1) * (double)random.Next(max + 1),
                        5 => random.Next(4) - 1.5,
                        _ => random.NextDouble() * max,
                    };
                }

                var expectedSamples = new TSample[filtered.Length];
                var actualSamples = new TSample[filtered.Length];
                foreach (var count in new[] { samples.Length, filtered.Length })
                {
                    if (alpha != WorkingAlpha.None && count % 4 != 0)
                        continue;

                    WorkingSamples.StoreScalar<TSample>(filtered.AsSpan(0, count), expectedSamples.AsSpan(0, count), alpha, transfer: 0, max);
                    WorkingSamples.Store<TSample>(filtered.AsSpan(0, count), actualSamples.AsSpan(0, count), alpha, transfer: 0, max);
                    Assert.Equal(expectedSamples, actualSamples);
                }
            }
        }
    }

    public static TheoryData<byte> TransferCharacteristics() => [1, 4, 5, 6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18];

    [Theory]
    [MemberData(nameof(TransferCharacteristics))]
    public void LinearLightThresholdsMatchTheTransferFunctions(byte transfer)
    {
        // The encoding of the filters clips linear light to [0, 1]; the one of drawing clips the encoded value only. A
        // function that is not monotone at the scale of the last bits of its inputs has no table (PQ, whose exponent of
        // 78.84 magnifies the errors of rounding): it keeps the samples of the function
        var store = WorkingSamples.GetThresholds8(transfer);
        var draw = DrawingKernels.GetLinearThresholds8(transfer);
        if (transfer is CicpTransfer.Srgb)
        {
            Assert.NotNull(store);
            Assert.NotNull(draw);
        }

        var inputs = GetQuantizationInputs([.. new[] { store, draw }.OfType<QuantizationThresholds>()]);
        var expected = new byte[inputs.Length];
        var actual = new byte[inputs.Length];
        WorkingSamples.StoreScalar<byte>(inputs, expected, WorkingAlpha.None, transfer, byte.MaxValue);
        WorkingSamples.Store<byte>(inputs, actual, WorkingAlpha.None, transfer, byte.MaxValue);
        Assert.Equal(expected, actual);

        for (var i = 0; draw is not null && i < inputs.Length; i++)
        {
            Assert.Equal((int)DrawingKernels.EncodeLight(inputs[i], byte.MaxValue, transfer, curves: null, channel: 0), draw.Quantize(inputs[i]));
        }
    }

    [Fact]
    public void QuantizationThresholdsRefuseFunctionsThatDecrease()
    {
        static int Round(double value) => value <= 0 ? 0 : value >= 1 ? byte.MaxValue : (int)Math.Floor((value * byte.MaxValue) + 0.5);

        var table = QuantizationThresholds.Create(Round, byte.MaxValue);
        Assert.NotNull(table);
        var threshold = table.GetThreshold(100);
        Assert.Equal(100, Round(threshold));
        Assert.Equal(99, Round(Math.BitDecrement(threshold)));

        // One sample less just after a threshold, one sample more at the start of an interval of the coarse index, and a
        // value that is not a number with a sample of its own
        Assert.Null(QuantizationThresholds.Create(value => value == Math.BitIncrement(threshold) ? 99 : Round(value), byte.MaxValue));
        Assert.Null(QuantizationThresholds.Create(value => value == 0.75 ? Round(value) + 1 : Round(value), byte.MaxValue));
        Assert.Null(QuantizationThresholds.Create(value => double.IsNaN(value) ? 1 : Round(value), byte.MaxValue));

        // Samples that no input reaches, and samples that are skipped
        foreach (var quantize in new Func<double, int>[] { value => Round(value) / 2, value => Round(value) / 2 * 2 })
        {
            var other = QuantizationThresholds.Create(quantize, byte.MaxValue);
            Assert.NotNull(other);
            foreach (var value in GetQuantizationInputs(other))
            {
                Assert.Equal(quantize(value), other.Quantize(value));
            }
        }
    }

    // Every threshold and the inputs around it, the inputs around the junctions of the transfer functions, a sweep of
    // the floats from 0 to 2, special values and random values
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static double[] GetQuantizationInputs(params QuantizationThresholds[] tables)
    {
        var inputs = new List<double> { 0, -0.0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, double.Epsilon, 1, Math.BitIncrement(1), Math.BitDecrement(1) };
        foreach (var table in tables)
        {
            for (var sample = 1; sample <= byte.MaxValue; sample++)
            {
                var threshold = table.GetThreshold(sample);
                if (!double.IsNaN(threshold))
                {
                    AddAround(threshold, 1024);
                }
            }
        }

        foreach (var junction in (double[])[0.0031308, 0.018053968510807, 0.0228, 0.01, Math.Sqrt(10) / 1000, 1.0 / 12, 0.04045])
        {
            AddAround(junction, 4096);
        }

        for (var bits = 0; bits <= BitConverter.SingleToInt32Bits(2f); bits += 1 << 12)
        {
            inputs.Add(BitConverter.Int32BitsToSingle(bits));
        }

        var random = new Random(42);
        for (var i = 0; i < 100_000; i++)
        {
            inputs.Add((random.NextDouble() * 1.5) - 0.1);
        }

        return [.. inputs];

        void AddAround(double value, int count)
        {
            var bits = BitConverter.DoubleToInt64Bits(value);
            for (var i = -count; i <= count; i++)
            {
                inputs.Add(BitConverter.Int64BitsToDouble(bits + i));
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
        // Row lengths that are and are not multiples of the vector width, kernels wider or taller than the image, and kernels
        // of more taps than are summed a block of samples at a time
        var random = new Random((int)format);
        (int Width, int Height, int KernelWidth, int KernelHeight)[] cases = [(37, 23, 3, 3), (37, 23, 5, 7), (5, 64, 9, 1), (64, 5, 1, 9), (1, 9, 3, 5), (2, 2, 7, 7), (101, 77, 3, 3), (70, 9, 61, 1), (9, 70, 1, 61), (40, 30, 31, 31), (21, 19, 33, 33)];
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
    public void DrawingSourceOverOf8BitPixelsMatchesTheScalarReference()
    {
        // The division of the vectorized path: by 2^16, then by 255 as a product and a shift, below 2^32
        var scale = 255UL * CoverageRasterizer.FullCoverage;
        Assert.True((255 * scale) + (scale / 2) < (1UL << 32));
        for (uint y = 0; y < 65536; y++)
        {
            Assert.True((ulong)y * 32897 < (1UL << 32));
            Assert.Equal(y / 255, (y * 32897) >> 23);
        }

        // Over a transparent pixel, the general formulas give the paint and the alpha (E + Q / 2) >> 16
        var mismatches = 0;
        for (ulong paintAlpha = 1; paintAlpha <= 255; paintAlpha++)
        {
            for (ulong q = 1; q <= CoverageRasterizer.FullCoverage; q++)
            {
                var total = paintAlpha * q * 255;
                var isSame = (total + (scale / 2)) / scale == ((paintAlpha * q) + (CoverageRasterizer.FullCoverage / 2)) >> 16
                    && ((255 * total) + (total / 2)) / total == 255
                    && (total + (total / 2)) / total == 1;
                mismatches += isSame ? 0 : 1;
            }
        }

        Assert.Equal(0, mismatches);

        var random = new Random(20261014);
        int[] lengths = [4, 5, 7, 8, 9, 15, 16, 17, 33, 64, 101];
        foreach (var length in lengths)
        {
            for (var round = 0; round < 64; round++)
            {
                var pixels = new byte[length * 4];
                random.NextBytes(pixels);
                var opaqueShare = round % 4;
                var backdrop = round / 16 % 2 == 0 ? (byte)255 : (byte)0;
                for (var i = 0; i < length; i++)
                {
                    // Runs of opaque pixels (or of transparent ones, as on a layer), translucent and transparent ones
                    pixels[(i * 4) + 3] = opaqueShare == 0 || random.Next(4) < opaqueShare ? backdrop : random.Next(3) == 0 ? (byte)0 : (byte)random.Next(256);
                }

                var coverage = new uint[length];
                for (var i = 0; i < length; i++)
                {
                    coverage[i] = random.Next(4) switch
                    {
                        0 => 0,
                        1 => CoverageRasterizer.FullCoverage,
                        2 => (uint)random.Next(1, 256),
                        _ => (uint)random.Next((int)CoverageRasterizer.FullCoverage + 1),
                    };
                }

                var colors = new ushort[length * 4];
                for (var i = 0; i < colors.Length; i++)
                {
                    colors[i] = (i & 3) == 3 && random.Next(3) == 0 ? (random.Next(2) == 0 ? (ushort)0 : (ushort)255) : (ushort)random.Next(256);
                }

                var paintAlpha = (round / 4 % 4) switch
                {
                    0 => 255,
                    1 => 0,
                    _ => random.Next(256),
                };
                var color = new DrawingColor((ushort)random.Next(256), (ushort)random.Next(256), (ushort)random.Next(256), (ushort)paintAlpha);

                var expected = (byte[])pixels.Clone();
                var actual = (byte[])pixels.Clone();
                DrawingKernels.CompositeOverScalar<byte>(expected, 4, coverage, new DrawingSource(color));
                DrawingKernels.CompositeOver8(actual, coverage, new DrawingSource(color));
                Assert.Equal(expected, actual);

                expected = (byte[])pixels.Clone();
                actual = (byte[])pixels.Clone();
                DrawingKernels.CompositeOverScalar<byte>(expected, 4, coverage, new DrawingSource(colors));
                DrawingKernels.CompositeOver8(actual, coverage, new DrawingSource(colors));
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void DrawingSourceOfColorsMatchesThePixelByPixelFormulas()
    {
        // A run of full coverage stores the colors; a pixel on its own always goes through the formulas
        var random = new Random(20261018);
        CheckSource<byte>(random, byte.MaxValue);
        CheckSource<ushort>(random, ushort.MaxValue);

        static void CheckSource<TSample>(Random random, int max)
            where TSample : unmanaged, IBinaryInteger<TSample>, IMinMaxValue<TSample>
        {
            int[] lengths = [16, 17, 31, 64, 101];
            foreach (var length in lengths)
            {
                for (var round = 0; round < 64; round++)
                {
                    var pixels = new TSample[length * 4];
                    var colors = new ushort[length * 4];
                    var coverage = new uint[length];
                    var fullShare = round % 4;
                    for (var i = 0; i < length; i++)
                    {
                        for (var c = 0; c < 4; c++)
                        {
                            pixels[(i * 4) + c] = TSample.CreateTruncating(random.Next(max + 1));
                            colors[(i * 4) + c] = (ushort)random.Next(max + 1);
                        }

                        // Transparent pixels with a hidden color, and paints without opacity
                        if (random.Next(4) == 0)
                        {
                            pixels[(i * 4) + 3] = TSample.Zero;
                        }

                        if (random.Next(4) == 0)
                        {
                            colors[(i * 4) + 3] = 0;
                        }

                        coverage[i] = fullShare == 3 || random.Next(3) < fullShare ? CoverageRasterizer.FullCoverage : random.Next(3) switch
                        {
                            0 => 0,
                            1 => (uint)random.Next(1, 256),
                            _ => (uint)random.Next((int)CoverageRasterizer.FullCoverage + 1),
                        };
                    }

                    var expected = (TSample[])pixels.Clone();
                    for (var i = 0; i < length; i++)
                    {
                        DrawingKernels.Composite<TSample>(expected.AsSpan(i * 4, 4), 4, coverage.AsSpan(i, 1), new DrawingSource(colors.AsSpan(i * 4, 4), CompositeMode.Source));
                    }

                    var actual = (TSample[])pixels.Clone();
                    DrawingKernels.Composite<TSample>(actual, 4, coverage, new DrawingSource(colors, CompositeMode.Source));
                    Assert.Equal(expected, actual);
                }
            }
        }
    }

    [Fact]
    public void DrawingColorsReadFromAFrameOfTheSameFormatAreItsConvertedColors()
    {
        // Every sample of every channel (the channels are converted on their own), and every 16-bit gray
        CheckColors(256, i => new Rgba32((byte)i, (byte)(255 - i), (byte)(i * 37), (byte)(i * 101)));
        CheckColors(256, i => new Bgra32((byte)i, (byte)(255 - i), (byte)(i * 37), (byte)(i * 101)));
        CheckColors(256, i => new Rgb24((byte)i, (byte)(255 - i), (byte)(i * 37)));
        CheckColors(256, i => new Gray8((byte)i));
        CheckColors(65536, i => new Gray16((ushort)i));
        CheckColors(65536, i => new Rgba64((ushort)i, (ushort)(65535 - i), (ushort)(i * 37), (ushort)(i * 101)));

        static void CheckColors<TPixel>(int count, Func<int, TPixel> create)
            where TPixel : unmanaged
        {
            const int Width = 256;
            using var image = new Image<TPixel>(Width, count / Width);
            for (var i = 0; i < count; i++)
            {
                image.Frames[0][i % Width, i / Width] = create(i);
            }

            var storage = image.Frames[0].GetStorage();
            var colors = new Rgba64[Width];
            var expected = new ushort[Width * 4];
            for (var y = 0; y < image.Height; y++)
            {
                DrawingKernels.ReadColors<TPixel>(storage, y, 0, colors);
                DrawingKernels.ConvertColors<TPixel>(colors, expected);

                // Runs that start anywhere, around the length of a vector
                foreach (var (start, length) in new[] { (0, Width), (1, Width - 1), (3, 5), (5, 3), (7, 17), (2, 1) })
                {
                    var actual = new ushort[length * 4];
                    DrawingKernels.ReadDrawingColors<TPixel>(storage, y, start, actual);
                    Assert.Equal(expected.AsSpan(start * 4, length * 4).ToArray(), actual);
                }
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void DrawingBlurSumsMatchTheScalarReference()
    {
        var random = new Random(20261015);
        int[] lengths = [1, 2, 3, 4, 5, 7, 8, 9, 16, 33, 100];
        int[] tapCounts = [1, 2, 3, 4, 7, 13, 38];
        foreach (var length in lengths)
        {
            foreach (var taps in tapCounts)
            {
                var weights = Enumerable.Range(0, taps).Select(_ => random.NextDouble() / taps).ToArray();
                for (var round = 0; round < 6; round++)
                {
                    // Rows that start before, at and after the first pixel, and that end beyond the last one
                    var firstColumn = random.Next(-taps - 2, Math.Max(1, length - taps + 3));
                    var count = random.Next(1, length + taps + 4);
                    var values = Enumerable.Range(0, length).Select(_ => random.Next(4) == 0 ? 0 : random.Next(3) == 0 ? 1 : random.NextDouble()).ToArray();
                    var premultiplied = Enumerable.Range(0, length * 4).Select(_ => random.Next(4) == 0 ? 0 : random.NextDouble() * 65535).ToArray();

                    var expected = new float[count * 4];
                    var actual = new float[count * 4];
                    BlurKernels.BlurRowScalar(premultiplied, weights, firstColumn, expected);
                    BlurKernels.BlurRow(premultiplied, weights, firstColumn, actual);
                    Assert.Equal(expected, actual);

                    expected = new float[count];
                    actual = new float[count];
                    BlurKernels.BlurValuesScalar(values, weights, firstColumn, expected);
                    BlurKernels.BlurValues(values, weights, firstColumn, actual);
                    Assert.Equal(expected, actual);

                    var row = Enumerable.Range(0, count * 4).Select(_ => (float)(random.NextDouble() * 70000)).ToArray();
                    var expectedSums = Enumerable.Range(0, count * 4).Select(_ => random.NextDouble() * 1000).ToArray();
                    var actualSums = (double[])expectedSums.Clone();
                    var weight = weights[round % taps];
                    BlurKernels.AccumulateScalar(expectedSums, weight, row);
                    BlurKernels.Accumulate(actualSums, weight, row);
                    Assert.Equal(expectedSums, actualSums);
                }
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void DrawingTurbulenceOfTheFourChannelsMatchesTheReferenceOfEachChannel()
    {
        var random = new Random(20261019);
        Span<double> sums = stackalloc double[4];
        foreach (var seed in new[] { 0, 7, 12345, -3, int.MaxValue })
        {
            var primitive = new TurbulencePrimitive(FilterTurbulenceType.Turbulence, 0.1, 0.1, 1, seed);
            for (var i = 0; i < 2000; i++)
            {
                // Points on both sides of the origin, near the lattice and far from it, and stitching on tiles of any position
                var scale = random.Next(3) switch
                {
                    0 => 4,
                    1 => 300,
                    _ => 3000,
                };
                var x = (random.NextDouble() - 0.5) * scale;
                var y = random.Next(8) == 0 ? Math.Floor(x) : (random.NextDouble() - 0.5) * scale;
                var octaves = random.Next(0, 9);
                var isFractal = random.Next(2) == 0;
                var isStitching = random.Next(2) == 0;
                var stitch = new TurbulencePrimitive.Stitch
                {
                    Width = random.Next(1, 300),
                    Height = random.Next(1, 300),
                    WrapX = 4096 + random.Next(-300, 600),
                    WrapY = 4096 + random.Next(-300, 600),
                };

                primitive.GetTurbulence(x, y, octaves, isFractal, isStitching, stitch, sums);
                for (var channel = 0; channel < 4; channel++)
                {
                    Assert.Equal(BitConverter.DoubleToInt64Bits(primitive.GetTurbulence(channel, x, y, octaves, isFractal, isStitching, stitch)), BitConverter.DoubleToInt64Bits(sums[channel]));
                }
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void DrawingColorMatrixMatchesTheScalarReference()
    {
        var random = new Random(20261016);
        var matrices = new List<ColorMatrix>
        {
            ColorMatrix.CreateGrayscale(),
            ColorMatrix.CreateSepia(0.7),
            ColorMatrix.CreateHueRotation(77),
            ColorMatrix.CreateInvert(),
            ColorMatrix.CreateOpacity(0.37),

            // Sums beyond the samples, infinite ones, and sums that are not numbers
            new([3, -2, 0.5, 1, -0.25, -1, 0, 0, 0, 1.5, 1e300, 1e300, 0, 0, 0, 1e308, -1e308, 1e308, -1e308, 0]),
        };
        for (var i = 0; i < 30; i++)
        {
            matrices.Add(new ColorMatrix(Enumerable.Range(0, 20).Select(_ => (random.NextDouble() * 4) - 2).ToArray()));
        }

        CheckColorMatrix<Rgba32>(random, matrices, 4);
        CheckColorMatrix<Bgra32>(random, matrices, 4);
        CheckColorMatrix<Rgb24>(random, matrices, 3);
        CheckColorMatrix<Rgba64>(random, matrices, 8);
        CheckColorMatrix<Gray8>(random, matrices, 1);
        CheckColorMatrix<Gray16>(random, matrices, 2);

        static void CheckColorMatrix<TPixel>(Random random, List<ColorMatrix> matrices, int bytesPerPixel)
            where TPixel : unmanaged
        {
            foreach (var matrix in matrices)
            {
                var count = random.Next(1, 40);
                var x = random.Next(0, 3);
                var row = new byte[(count + x) * bytesPerPixel];
                random.NextBytes(row);
                var expected = new ushort[count * 4];
                var actual = new ushort[count * 4];
                ColorMatrixKernel.ApplyScalar<TPixel>(row, x, matrix, expected);
                ColorMatrixKernel.ApplyVector<TPixel>(row, x, matrix, actual);
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void DrawingCoverageProductsMatchTheScalarReference()
    {
        const uint Full = CoverageRasterizer.FullCoverage;
        var random = new Random(20261017);
        uint Coverage() => random.Next(5) switch
        {
            0 => 0,
            1 => Full,
            2 => (uint)random.Next((int)Full - 2, (int)Full),
            3 => (Full / 2) - 1 + (uint)random.Next(3),
            _ => (uint)random.Next((int)Full + 1),
        };

        foreach (var length in new[] { 0, 1, 3, 4, 5, 8, 13, 64, 101 })
        {
            for (var round = 0; round < 20; round++)
            {
                var coverage = Enumerable.Range(0, length).Select(_ => round == 0 ? 0 : Coverage()).ToArray();
                var mask = Enumerable.Range(0, length).Select(_ => Coverage()).ToArray();
                var expected = new uint[length];
                var actual = new uint[length];
                Assert.Equal(CoverageKernels.MultiplyScalar(coverage, mask, expected), CoverageKernels.Multiply(coverage, mask, actual));
                Assert.Equal(expected, actual);

                var opacity = round % 3 == 0 ? Full - 1 : (uint)random.Next((int)Full);
                CoverageKernels.ScaleScalar(coverage, opacity, expected);
                CoverageKernels.Scale(coverage, opacity, actual);
                Assert.Equal(expected, actual);

                // In place, as the clip and the opacity use them
                var inPlace = (uint[])coverage.Clone();
                CoverageKernels.Multiply(inPlace, mask, inPlace);
                CoverageKernels.MultiplyScalar(coverage, mask, expected);
                Assert.Equal(expected, inPlace);
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
    public void PixelConversionArithmeticMatchesTheScalarFormulasExhaustively()
    {
        // Every 16-bit sample reduced to 8 bits
        var samples = new ushort[Vector128<ushort>.Count];
        for (var value = 0; value <= ushort.MaxValue; value += samples.Length)
        {
            for (var lane = 0; lane < samples.Length; lane++)
            {
                samples[lane] = (ushort)(value + lane);
            }

            var reduced = PixelConverter.To8BitVector(Vector128.Create(samples));
            for (var lane = 0; lane < samples.Length; lane++)
            {
                Assert.Equal(PixelConverter.To8Bit(samples[lane]), reduced[lane]);
            }
        }

        // Every luma sum of 8-bit samples (2126 R + 7152 G + 722 B + 5000) divided by 10000
        for (var value = 0; value <= 2_555_000; value += 4)
        {
            var quotients = PixelConverter.DivideBy10000Vector(Vector128.Create(value, value + 1, value + 2, value + 3));
            if (quotients != Vector128.Create(value / 10000, (value + 1) / 10000, (value + 2) / 10000, (value + 3) / 10000))
                Assert.Fail($"Wrong quotient from {value}: {quotients}");
        }

        // Luma sums of 16-bit samples, up to 655,355,000: the values around every multiple of 10000 and a stride over the range
        for (var multiple = 0; multiple <= 65535; multiple++)
        {
            CheckExactDivision(Math.Max((multiple * 10000) - 2, 0));
        }

        for (var value = 0; value <= 655_355_000 - 3; value += 997)
        {
            CheckExactDivision(value);
        }

        // Every 8-bit flattening sum (s * a + bg * (255 - a) <= 255 * 255), rounded division by 255
        for (var value = 0; value <= 255 * 255; value += 8)
        {
            var quotients = PixelConverter.DivideBy255Vector(Vector128.Create((ushort)value, (ushort)(value + 1), (ushort)(value + 2), (ushort)(value + 3), (ushort)(value + 4), (ushort)(value + 5), (ushort)(value + 6), (ushort)(value + 7)));
            for (var lane = 0; lane < 8; lane++)
            {
                if (value + lane <= 255 * 255 && quotients[lane] != (value + lane + 127) / 255)
                    Assert.Fail($"Wrong quotient for {value + lane}: {quotients[lane]}");
            }
        }

        // 16-bit flattening sums (up to 65535 * 65535): the values around every multiple of 65535 and of every half-way
        // point, where the rounded quotient changes, and a stride over the range
        for (var multiple = 0L; multiple <= 65535; multiple++)
        {
            CheckDivisionBy65535(Math.Max((multiple * 65535) - 2, 0));
            CheckDivisionBy65535(Math.Min((multiple * 65535) + 32765, (65535L * 65535) - 3));
        }

        for (var value = 0L; value <= (65535L * 65535) - 3; value += 65521)
        {
            CheckDivisionBy65535(value);
        }

        static void CheckExactDivision(int value)
        {
            var quotients = PixelConverter.DivideBy10000ExactVector(Vector128.Create(value, value + 1, value + 2, value + 3));
            if (quotients != Vector128.Create(value / 10000, (value + 1) / 10000, (value + 2) / 10000, (value + 3) / 10000))
                Assert.Fail($"Wrong quotient from {value}: {quotients}");
        }

        static void CheckDivisionBy65535(long value)
        {
            var quotients = PixelConverter.DivideBy65535Vector(Vector128.Create((uint)value, (uint)(value + 1), (uint)(value + 2), (uint)(value + 3)));
            if (quotients != Vector128.Create((uint)((value + 32767) / 65535), (uint)((value + 32768) / 65535), (uint)((value + 32769) / 65535), (uint)((value + 32770) / 65535)))
                Assert.Fail($"Wrong quotient from {value}: {quotients}");
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void TransposingOrientationsMatchTheScalarLoops()
    {
        // Rotations by a quarter turn and diagonal reflections (with and without mirrors) of sizes that are and are not
        // multiples of the blocks and of the tiles
        var random = new Random(90);
        foreach (var format in new[] { PixelFormat.Rgba32, PixelFormat.Bgra32, PixelFormat.Rgba64, PixelFormat.Rgb24, PixelFormat.Gray8, PixelFormat.Gray16 })
        {
            foreach (var (width, height) in new[] { (1, 1), (3, 5), (4, 4), (7, 9), (33, 31), (64, 40), (70, 101) })
            {
                var bytes = new byte[width * height * PixelFormats.GetBytesPerPixel(format)];
                random.NextBytes(bytes);
                foreach (var orientation in new[] { Metadata.ExifOrientation.LeftTop, Metadata.ExifOrientation.RightTop, Metadata.ExifOrientation.RightBottom, Metadata.ExifOrientation.LeftBottom })
                {
                    Assert.Equal(Orient(forceScalar: true), Orient(forceScalar: false));

                    byte[] Orient(bool forceScalar)
                    {
                        using var image = ImportBytes(format, bytes, width, height);
                        image.Metadata.Orientation = orientation;
                        Resampler.TestForceScalar = forceScalar;
                        try
                        {
                            image.AutoOrient();
                        }
                        finally
                        {
                            Resampler.TestForceScalar = false;
                        }

                        var result = new byte[bytes.Length];
                        image.Frames[0].CopyPixelBytesTo(result);
                        return result;
                    }
                }
            }
        }
    }

    [Fact]
    public void GrayscaleMatchesTheScalarReference()
    {
        CheckGrayscale<Rgba32>();
        CheckGrayscale<Bgra32>();
        CheckGrayscale<Rgb24>();
        CheckGrayscale<Rgba64>();

        [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
        static void CheckGrayscale<TPixel>()
            where TPixel : unmanaged
        {
            var random = new Random(typeof(TPixel).Name.Length);
            for (var length = 0; length <= 40; length++)
            {
                // Random samples, often 0 or the maximum
                var pixels = new TPixel[length];
                var bytes = unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels.AsSpan()));
                random.NextBytes(bytes);
                for (var i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = random.Next(4) switch
                    {
                        0 => 0,
                        1 => 0xFF,
                        _ => bytes[i],
                    };
                }

                var expected = pixels.ToArray();
                ProcessingKernels.GrayscaleScalar<TPixel>(expected);
                var done = ProcessingKernels.GrayscaleVector<TPixel>(pixels);
                Assert.True(Vector128.IsHardwareAccelerated is false || length < 6 || done > 0);
                ProcessingKernels.GrayscaleScalar(pixels.AsSpan(done));
                Assert.Equal(unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(expected.AsSpan())).ToArray(), bytes.ToArray());
            }
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
        Rgba64?[] backgrounds = [null, opaqueBackground, new Rgba64(0, 0, 0, ushort.MaxValue), new Rgba64(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue), new Rgba64(0x8000, 0x7F7F, 0x0101, ushort.MaxValue)];
        for (var length = 0; length <= 70; length++)
        {
            for (var round = 0; round < 7; round++)
            {
                var source = new TSource[length];
                var bytes = unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(source.AsSpan()));
                random.NextBytes(bytes);
                if (round == 6 && PixelFormats.HasAlpha(sourceFormat))
                {
                    // Extreme samples: every component is 0, the maximum or random, and alpha is 0, the maximum or random
                    var alphaSize = sourceSize / 4;
                    for (var i = 0; i < bytes.Length; i += alphaSize)
                    {
                        var kind = random.Next(3);
                        if (kind < 2)
                        {
                            bytes.Slice(i, alphaSize).Fill(kind == 0 ? (byte)0 : (byte)0xFF);
                        }
                    }
                }
                else if (round >= 2 && PixelFormats.HasAlpha(sourceFormat))
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

                foreach (var background in backgrounds)
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
        var columns = new int[16];
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
            for (var i = 0; i < 16; i++)
            {
                columns[((i & 3) * 4) + (i >> 2)] = residual[i];
            }

            Vp8ForwardTransforms.ForwardDctColumns(columns, actual);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void WebPInverseDctMatchesTheScalarReference()
    {
        // Dequantized coefficients of every magnitude (16-bit extremes make the 32-bit products wrap), added to random
        // predictions in planes of several strides, so that every clamp is taken
        var random = new Random(605);
        var block = new short[16];
        for (var round = 0; round < 100_000; round++)
        {
            for (var i = 0; i < block.Length; i++)
            {
                block[i] = (round % 4) switch
                {
                    0 => (short)random.Next(-2048, 2049),
                    1 => (short)(random.Next(2) == 0 ? short.MinValue : short.MaxValue),
                    2 => (short)random.Next(-40, 41),
                    _ => (short)random.Next(short.MinValue, short.MaxValue + 1),
                };
            }

            var stride = 4 + random.Next(0, 6);
            var expected = new byte[(3 * stride) + 4];
            random.NextBytes(expected);
            var actual = expected.ToArray();
            Vp8Transforms.InverseDctAddFullScalar(block, expected, stride);
            Vp8Transforms.InverseDctAddFull(block, actual, stride);
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
                    Vp8Encoder.QuantizeScalar(coefficients, actual, dcStep, step, first);
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

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void WebPBoolDecoderMatchesTheByteAtATimeReference()
    {
        // Partitions of every short length (refills near and past the end, buffers shorter than 8 bytes), at the start, in
        // the middle and at the end of their buffer, read far past their end with skewed and uniform probabilities
        var random = new Random(605);
        for (var length = 0; length <= 40; length++)
        {
            for (var round = 0; round < 40; round++)
            {
                var before = round % 3 == 0 ? 0 : random.Next(1, 12);
                var after = round % 4 == 0 ? 0 : random.Next(1, 12);
                var data = new byte[before + length + after];
                random.NextBytes(data);
                if (round % 5 == 1)
                {
                    data.AsSpan(before, length).Fill(0xFF);
                }
                else if (round % 5 == 2 && length > 0)
                {
                    data[before] = 0xFF; // invalid: the window starts at the range
                }

                var reference = new ReferenceBoolDecoder(data, before, length);
                var actual = new Vp8BoolDecoder(data, before, length);
                var useLocals = round % 2 == 0;
                for (var i = 0; i < (length * 12) + 100; i++)
                {
                    var probability = (i % 3) switch
                    {
                        0 => random.Next(1, 256),
                        1 => 128,
                        _ => random.Next(2) == 0 ? 1 : 255,
                    };
                    var expected = reference.ReadBool(probability);
                    bool value;
                    if (useLocals)
                    {
                        actual.Load(out var window, out var range, out var bits);
                        value = Vp8BoolDecoder.ReadBool(ref actual, ref window, ref range, ref bits, probability);
                        actual.Store(window, range, bits);
                    }
                    else
                    {
                        value = actual.ReadBool(probability);
                    }

                    Assert.Equal(expected, value);
                    Assert.Equal(reference.OverrunBytes, actual.OverrunBytes);
                }
            }
        }
    }

    /// <summary>The VP8 boolean decoder of RFC 6386 section 7: a two-byte window in a 32-bit register that reads one byte per 8 shifts.</summary>
    private sealed class ReferenceBoolDecoder
    {
        private readonly byte[] _data;
        private readonly int _end;
        private int _position;
        private uint _value;
        private uint _range = 255;
        private int _bitCount;

        public ReferenceBoolDecoder(byte[] data, int offset, int length)
        {
            _data = data;
            _position = offset;
            _end = offset + length;
            _value = ((uint)NextByte() << 8) | NextByte();
        }

        public int OverrunBytes { get; private set; }

        public bool ReadBool(int probability)
        {
            var split = 1 + (((_range - 1) * (uint)probability) >> 8);
            var bigSplit = split << 8;
            bool result;
            if (_value >= bigSplit)
            {
                result = true;
                _range -= split;
                _value -= bigSplit;
            }
            else
            {
                result = false;
                _range = split;
            }

            if (_range < 128)
            {
                // The excess bits of an invalid partition (one that starts with 0xFF) leave the 32-bit register 16 bits above the window
                var shift = System.Numerics.BitOperations.LeadingZeroCount(_range) - 24;
                _range <<= shift;
                _value <<= shift;
                _bitCount += shift;
                if (_bitCount >= 8)
                {
                    _bitCount -= 8;
                    _value |= (uint)NextByte() << _bitCount;
                }
            }

            return result;
        }

        private byte NextByte()
        {
            if (_position < _end)
                return _data[_position++];

            OverrunBytes++;
            return 0;
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

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Av1LoopFilterLinesMatchTheScalarReference(int bitDepth)
    {
        // Vertical and horizontal edges of every filter size, with levels and sharpness over their whole range, on samples
        // that are flat (every mask passes), smooth steps, ramps, random and extreme
        var random = new Random(bitDepth * 13);
        var maxSample = (1 << bitDepth) - 1;
        const int Stride = 24;
        for (var round = 0; round < 20000; round++)
        {
            var plane = random.Next(2);
            var filterSize = plane is 0 ? (4 << random.Next(3)) : (4 << random.Next(2));
            var level = random.Next(1, 64);
            var sharpness = random.Next(8);
            var shift = sharpness > 4 ? 2 : sharpness > 0 ? 1 : 0;
            var limit = sharpness > 0 ? Math.Clamp(level >> shift, 1, 9 - sharpness) : Math.Max(1, level >> shift);
            var blimit = (2 * (level + 2)) + limit;
            var thresh = level >> 4;
            var expected = new ushort[Stride * Stride];
            var baseValue = random.Next(maxSample + 1);
            var stepValue = random.Next(-24, 25) << (bitDepth - 8);
            for (var y = 0; y < Stride; y++)
            {
                for (var x = 0; x < Stride; x++)
                {
                    var along = random.Next(2) == 0 ? x : y;
                    expected[(y * Stride) + x] = (ushort)Math.Clamp((round % 5) switch
                    {
                        0 => baseValue + random.Next(-1, 2),
                        1 => baseValue + (along >= 12 ? stepValue : 0) + random.Next(-2, 3),
                        2 => baseValue + ((along - 12) * random.Next(0, 3)),
                        3 => random.Next(maxSample + 1),
                        _ => random.Next(2) * maxSample,
                    }, 0, maxSample);
                }
            }

            var actual = expected.ToArray();
            var vertical = random.Next(2) == 0;
            var position = vertical ? (random.Next(8, 16) * Stride) + 12 : (12 * Stride) + random.Next(8, 16);
            var (step, lineStep) = vertical ? (1, Stride) : (Stride, 1);
            Av1LoopFilter.FilterLinesScalar(expected, position, step, lineStep, plane, filterSize, bitDepth, limit, blimit, thresh);
            Av1LoopFilter.FilterLines(actual, position, step, lineStep, plane, filterSize, bitDepth, limit, blimit, thresh);
            Assert.True(expected.AsSpan().SequenceEqual(actual), $"Round {round}: plane {plane}, filter size {filterSize}, level {level}, sharpness {sharpness}, vertical {vertical}.");
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Av1CdefDirectionMatchesTheScalarReference(int bitDepth)
    {
        // Blocks of random, extreme, flat and directional samples (lines of every direction), at every column of a row
        var random = new Random(bitDepth * 3);
        var maxSample = (1 << bitDepth) - 1;
        const int Stride = 40;
        for (var round = 0; round < 20000; round++)
        {
            var source = new ushort[Av1Cdef.SourceRows * Stride];
            var slopeX = random.Next(-4, 5);
            var slopeY = random.Next(-4, 5);
            for (var y = 0; y < Av1Cdef.SourceRows; y++)
            {
                for (var x = 0; x < Stride; x++)
                {
                    source[(y * Stride) + x] = (ushort)Math.Clamp((round % 5) switch
                    {
                        0 => random.Next(maxSample + 1),
                        1 => random.Next(2) * maxSample,
                        2 => (maxSample / 2) + random.Next(-2, 3),
                        3 => (((x * slopeX) + (y * slopeY)) & 1) * maxSample,
                        _ => ((x * slopeX) + (y * slopeY)) * (maxSample / 16),
                    }, 0, maxSample);
                }
            }

            var x0 = random.Next(Stride - 8 + 1);
            Av1Cdef.FindDirectionScalar(source, Stride, x0, bitDepth, out var expectedDirection, out var expectedVariance);
            Av1Cdef.FindDirection(source, Stride, x0, bitDepth, out var actualDirection, out var actualVariance);
            Assert.Equal((expectedDirection, expectedVariance), (actualDirection, actualVariance));
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Av1CdefEdgeBlocksMatchTheScalarReference(int bitDepth)
    {
        // Blocks of every plane size and subsampling at every position of small frames (the edges, the corners, and frames of
        // one block), with random, extreme, flat and ramp samples, every direction and strengths over their coded range
        var random = new Random(bitDepth);
        var maxSample = (1 << bitDepth) - 1;
        var coeffShift = bitDepth - 8;
        for (var round = 0; round < 3000; round++)
        {
            var (subX, subY) = (round % 4) switch
            {
                0 => (0, 0),
                1 => (1, 1),
                2 => (1, 0),
                _ => (0, 1),
            };
            var miRows = 2 * random.Next(1, 4);
            var miCols = 2 * random.Next(1, 4);
            var w = 8 >> subX;
            var h = 8 >> subY;
            var r = 2 * random.Next(miRows / 2);
            var c = 2 * random.Next(miCols / 2);
            var x0 = (c * 4) >> subX;
            var y0 = (r * 4) >> subY;
            var stride = ((miCols * 4) >> subX) + 4 + random.Next(3);
            var source = new ushort[Av1Cdef.SourceRows * stride];
            for (var i = 0; i < source.Length; i++)
            {
                source[i] = (round % 5) switch
                {
                    0 => (ushort)random.Next(maxSample + 1),
                    1 => (ushort)(random.Next(2) * maxSample),
                    2 => (ushort)Math.Clamp((maxSample / 2) + random.Next(-3, 4), 0, maxSample),
                    3 => (ushort)Math.Clamp((i % stride * maxSample / stride) + random.Next(-8, 9), 0, maxSample),
                    _ => random.Next(4) == 0 ? (ushort)random.Next(maxSample + 1) : (ushort)(maxSample / 3),
                };
            }

            var priStr = random.Next(4) == 0 ? 0 : random.Next(1, 16) << coeffShift;
            var secStr = random.Next(4) == 0 ? 0 : (1 << random.Next(3)) << coeffShift;
            var damping = random.Next(3, 7) + coeffShift - (subX | subY);
            var dir = random.Next(8);
            var priTaps = Av1Tables.CdefPriTaps.Slice(((priStr >> coeffShift) & 1) * 2, 2);
            var secTaps = Av1Tables.CdefSecTaps.Slice(((priStr >> coeffShift) & 1) * 2, 2);
            var expected = new ushort[(y0 + h + 1) * stride];
            for (var i = 0; i < expected.Length; i++)
            {
                expected[i] = (ushort)random.Next(maxSample + 1);
            }

            var actual = expected.ToArray();
            Av1Cdef.FilterEdgeBlockScalar(expected, stride, source, x0, y0, w, h, subX, subY, miRows, miCols, priStr, secStr, damping, dir, priTaps, secTaps);
            Av1Cdef.FilterEdgeBlock(actual, stride, source, x0, y0, w, h, subX, subY, miRows, miCols, priStr, secStr, damping, dir, priTaps, secTaps);
            Assert.True(expected.AsSpan().SequenceEqual(actual), $"Round {round}: {w}x{h} block at ({x0}, {y0}) of {miCols}x{miRows} blocks, direction {dir}, strengths {priStr}/{secStr}.");
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Av1LoopRestorationFiltersMatchTheScalarReferences(int bitDepth)
    {
        // Blocks of every width around the vector size up to the largest one, on a padded source of random, extreme, flat and
        // ramp samples, with Wiener coefficients and self-guided weights over their whole coded range and every self-guided set
        var random = new Random(bitDepth);
        var maxSample = (1 << bitDepth) - 1;
        var scratch = new int[Av1LoopRestoration.ScratchLength];
        var vectorScratch = new int[Av1LoopRestoration.ScratchLength];
        const int Padding = Av1LoopRestoration.Padding;
        int[] widths = [1, 4, 7, 8, 9, 15, 16, 17, 31, 33, 63, Av1LoopRestoration.MaxBlockWidth];
        for (var round = 0; round < 240; round++)
        {
            var w = widths[round % widths.Length];
            var h = (round % 3) switch
            {
                0 => Av1LoopRestoration.MaxBlockHeight,
                1 => random.Next(1, 9),
                _ => random.Next(1, Av1LoopRestoration.MaxBlockHeight + 1),
            };
            var sourceStride = w + (2 * Padding) + random.Next(3);
            var source = new ushort[(h + (2 * Padding)) * sourceStride];
            for (var i = 0; i < source.Length; i++)
            {
                source[i] = (round % 4) switch
                {
                    0 => (ushort)random.Next(maxSample + 1),
                    1 => (ushort)(random.Next(2) * maxSample),
                    2 => (ushort)Math.Clamp((maxSample / 2) + random.Next(-3, 4), 0, maxSample),
                    _ => (ushort)Math.Clamp((i % sourceStride * maxSample / sourceStride) + random.Next(-8, 9), 0, maxSample),
                };
            }

            var unit = new sbyte[10];
            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < 3; i++)
                {
                    unit[1 + (3 * pass) + i] = (sbyte)random.Next(Av1Tables.WienerTapsMin[i], Av1Tables.WienerTapsMax[i] + 1);
                }
            }

            unit[7] = (sbyte)(round % 16);
            unit[8] = (sbyte)random.Next(Av1Tables.SgrprojXqdMin[0], Av1Tables.SgrprojXqdMax[0] + 1);
            unit[9] = (sbyte)random.Next(Av1Tables.SgrprojXqdMin[1], Av1Tables.SgrprojXqdMax[1] + 1);

            var destinationStride = w + random.Next(3);
            var sourceOffset = (Padding * sourceStride) + Padding;
            var expected = new ushort[h * destinationStride];
            var actual = new ushort[h * destinationStride];
            Av1LoopRestoration.ApplyWienerScalar(source, sourceStride, sourceOffset, expected, destinationStride, w, h, unit, bitDepth, scratch);
            FillWithGarbage(vectorScratch);
            Av1LoopRestoration.ApplyWiener(source, sourceStride, sourceOffset, actual, destinationStride, w, h, unit, bitDepth, vectorScratch);
            Assert.True(expected.AsSpan().SequenceEqual(actual), $"Wiener filter, {w}x{h}, round {round}.");

            Av1LoopRestoration.ApplySelfGuidedScalar(source, sourceStride, sourceOffset, expected, destinationStride, w, h, unit, bitDepth, scratch);
            FillWithGarbage(vectorScratch);
            Av1LoopRestoration.ApplySelfGuided(source, sourceStride, sourceOffset, actual, destinationStride, w, h, unit, bitDepth, vectorScratch);
            Assert.True(expected.AsSpan().SequenceEqual(actual), $"Self-guided filter, set {unit[7]}, {w}x{h}, round {round}.");
        }

        // The vector paths must not read scratch entries they did not write
        void FillWithGarbage(int[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = random.Next(int.MinValue, int.MaxValue);
            }
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Av1InterPredictionMatchesTheScalarReference(int bitDepth)
    {
        // Blocks of every size at every sub-sample position, inside the reference and across each of its edges, with every
        // filter type and the rounding of the bit depth and of compound prediction
        var random = new Random(bitDepth * 23);
        var maxSample = (1 << bitDepth) - 1;
        const int Stride = 96;
        const int Height = 80;
        var reference = new ushort[Stride * Height];
        for (var i = 0; i < reference.Length; i++)
        {
            reference[i] = (ushort)(random.Next(4) == 0 ? random.Next(2) * maxSample : random.Next(maxSample + 1));
        }

        var intermediate = new int[Av1InterPredictor.MaxIntermediateHeight * 64];
        for (var round = 0; round < 4000; round++)
        {
            var w = 4 << random.Next(5);
            var h = 2 << random.Next(6);
            var lastX = random.Next(8, Stride);
            var lastY = random.Next(8, Height);
            var x = (random.Next(-24, lastX + 24) << 10) + (random.Next(16) << 6);
            var y = (random.Next(-24, lastY + 24) << 10) + (random.Next(16) << 6);
            var horizontalFilter = random.Next(4);
            var verticalFilter = random.Next(4);
            var interRound0 = 3 + (bitDepth is 12 ? 2 : 0);
            var interRound1 = random.Next(2) == 0 ? 7 : 11 - interRound0;
            var expected = new int[h * 70];
            var actual = new int[h * 70];
            Av1InterPredictor.PredictBlockScalar(reference, Stride, lastX, lastY, x, y, 1024, 1024, w, h, horizontalFilter, verticalFilter, interRound0, interRound1, intermediate, expected, 70);
            Av1InterPredictor.PredictBlock(reference, Stride, lastX, lastY, x, y, 1024, 1024, w, h, horizontalFilter, verticalFilter, interRound0, interRound1, intermediate, actual, 70);
            Assert.True(expected.AsSpan().SequenceEqual(actual), $"Round {round}: {w}x{h} at ({x}, {y}), limits ({lastX}, {lastY}), filters {horizontalFilter}/{verticalFilter}.");
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Av1ResidualAdditionMatchesTheScalarReference(int bitDepth)
    {
        // Rows of every transform width, residuals that clamp at both ends and any 32-bit value
        var random = new Random(bitDepth * 19);
        var maxSample = (1 << bitDepth) - 1;
        foreach (var width in new[] { 4, 8, 16, 32, 64 })
        {
            for (var round = 0; round < 500; round++)
            {
                var expected = new ushort[width];
                var residual = new int[width];
                for (var i = 0; i < width; i++)
                {
                    expected[i] = (ushort)random.Next(maxSample + 1);
                    residual[i] = (round % 3) switch
                    {
                        0 => random.Next(-maxSample - 2, maxSample + 2),
                        1 => random.Next(-64, 65),
                        _ => random.Next(int.MinValue, int.MaxValue),
                    };
                }

                var actual = expected.ToArray();
                Av1Reconstruction.AddResidualScalar(expected, residual, maxSample);
                Av1Reconstruction.AddResidual(actual, residual, maxSample);
                Assert.Equal(expected, actual);
            }
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Av1ChromaFromLumaMatchesTheScalarReference(int bitDepth)
    {
        // Every subsampling and transform size of the chroma from luma prediction, with the luma samples clamped at every
        // distance from the block (MaxLumaW and MaxLumaH), every alpha, on random and extreme samples
        var random = new Random(bitDepth * 17);
        var maxSample = (1 << bitDepth) - 1;
        const int Stride = 160;
        var scratch = new int[1024];
        for (var round = 0; round < 6000; round++)
        {
            var (subX, subY) = (round % 3) switch
            {
                0 => (0, 0),
                1 => (1, 0),
                _ => (1, 1),
            };
            int txSz;
            do
            {
                txSz = random.Next(Av1Constants.TxSizesAll);
            }
            while (Av1Tables.TxWidth[txSz] > 32 || Av1Tables.TxHeight[txSz] > 32);

            int w = Av1Tables.TxWidth[txSz];
            int h = Av1Tables.TxHeight[txSz];
            var startX = 4 * random.Next(4);
            var startY = 4 * random.Next(4);
            var maxLumaW = Math.Min(Stride, ((startX + random.Next(1, w + 1)) << subX) + (random.Next(2) << subX));
            var maxLumaH = Math.Min(Stride, ((startY + random.Next(1, h + 1)) << subY) + (random.Next(2) << subY));
            var luma = new ushort[Stride * Stride];
            for (var i = 0; i < luma.Length; i++)
            {
                luma[i] = round % 2 == 0 ? (ushort)random.Next(maxSample + 1) : (ushort)(random.Next(2) * maxSample);
            }

            var expected = new ushort[Stride * Stride];
            for (var i = 0; i < expected.Length; i++)
            {
                expected[i] = (ushort)random.Next(maxSample + 1);
            }

            var actual = expected.ToArray();
            var alpha = random.Next(-16, 17);
            Av1Reconstruction.PredictChromaFromLumaScalar(luma, Stride, expected, Stride, startX, startY, txSz, alpha, subX, subY, maxLumaW, maxLumaH, bitDepth, scratch);
            Av1Reconstruction.PredictChromaFromLuma(luma, Stride, actual, Stride, startX, startY, txSz, alpha, subX, subY, maxLumaW, maxLumaH, bitDepth, scratch);
            Assert.True(expected.AsSpan().SequenceEqual(actual), $"Round {round}: transform size {txSz}, subsampling {subX}/{subY}, alpha {alpha}, limits {maxLumaW}x{maxLumaH}.");
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Av1IntraPredictionMatchesTheScalarReference(int bitDepth)
    {
        // Every mode, angle delta, recursive filter, block size and edge availability, near the right and bottom limits of
        // the plane, with and without the edge filter, on random, extreme and flat samples
        var random = new Random(bitDepth * 31);
        var maxSample = (1 << bitDepth) - 1;
        const int Stride = 160;
        const int Height = 160;
        for (var round = 0; round < 8000; round++)
        {
            var log2W = random.Next(2, 7);
            var log2H = Math.Clamp(log2W + random.Next(-2, 3), 2, 6);
            var w = 1 << log2W;
            var h = 1 << log2H;
            var x = 4 * random.Next((Stride - w) / 4);
            var y = 4 * random.Next((Height - h) / 4);
            var maxX = random.Next(3) == 0 ? Math.Min(Stride - 1, x + random.Next(2 * w)) : Stride - 1;
            var maxY = random.Next(3) == 0 ? Math.Min(Height - 1, y + random.Next(2 * h)) : Height - 1;
            var expected = new ushort[Stride * Height];
            for (var i = 0; i < expected.Length; i++)
            {
                expected[i] = (round % 4) switch
                {
                    0 => (ushort)random.Next(maxSample + 1),
                    1 => (ushort)(random.Next(2) * maxSample),
                    2 => (ushort)Math.Clamp((maxSample / 2) + random.Next(-3, 4), 0, maxSample),
                    _ => (ushort)Math.Clamp((i % Stride * maxSample / Stride) + random.Next(-8, 9), 0, maxSample),
                };
            }

            var mode = random.Next(Av1Constants.PaethPred + 1);
            var angleDelta = mode is >= Av1Constants.VPred and <= Av1Constants.D67Pred ? random.Next(-3, 4) : 0;
            var filterIntraMode = w <= 32 && h <= 32 && random.Next(4) == 0 ? random.Next(5) : -1;
            var parameters = new Av1IntraPredictor.Parameters(bitDepth, mode, angleDelta, filterIntraMode, random.Next(4) != 0, random.Next(2) == 0);
            var haveLeft = x > 0 && random.Next(5) != 0;
            var haveAbove = y > 0 && random.Next(5) != 0;
            var haveAboveRight = haveAbove && random.Next(2) == 0;
            var haveBelowLeft = haveLeft && random.Next(2) == 0;
            var actual = expected.ToArray();
            Av1IntraPredictor.PredictScalar(expected, Stride, x, y, haveLeft, haveAbove, haveAboveRight, haveBelowLeft, log2W, log2H, maxX, maxY, parameters);
            Av1IntraPredictor.Predict(actual, Stride, x, y, haveLeft, haveAbove, haveAboveRight, haveBelowLeft, log2W, log2H, maxX, maxY, parameters);
            Assert.True(expected.AsSpan().SequenceEqual(actual), $"Round {round}: {w}x{h} at ({x}, {y}), {parameters}, left {haveLeft}, above {haveAbove}, limits ({maxX}, {maxY}).");
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Av1InverseTransformMatchesTheScalarReference(int bitDepth)
    {
        // Every transform size and every transform type that applies to it, on coefficients of the dequantized range (small,
        // sparse, a DC alone, the extremes) and on any 32-bit values, which saturate the intermediate clamps
        var random = new Random(bitDepth * 7);
        var limit = 1 << (7 + bitDepth);
        var expected = new int[64 * 64];
        var actual = new int[64 * 64];
        for (var txSz = 0; txSz < Av1Constants.TxSizesAll; txSz++)
        {
            var log2W = Av1Tables.TxWidthLog2[txSz];
            var log2H = Av1Tables.TxHeightLog2[txSz];
            var dequant = new int[Math.Min(32, 1 << log2W) * Math.Min(32, 1 << log2H)];
            var length = (1 << log2W) << log2H;
            for (var txType = 0; txType < Av1Constants.TxTypes; txType++)
            {
                if (!IsApplicable(Av1InverseTransform.GetRowKind(txType), log2W) || !IsApplicable(Av1InverseTransform.GetColumnKind(txType), log2H))
                    continue;

                for (var round = 0; round < 16; round++)
                {
                    Array.Clear(dequant);
                    for (var i = 0; i < dequant.Length; i++)
                    {
                        dequant[i] = (round % 6) switch
                        {
                            0 => random.Next(-300, 301),
                            1 => random.Next(6) == 0 ? random.Next(-limit, limit) : 0,
                            2 => i is 0 ? random.Next(-limit, limit) : 0,
                            3 => random.Next(2) == 0 ? -limit : limit - 1,
                            4 => random.Next(int.MinValue, int.MaxValue),
                            _ => i < 8 ? random.Next(-limit, limit) : 0,
                        };
                    }

                    Av1InverseTransform.TransformScalar(dequant, expected.AsSpan(0, length), txSz, txType, lossless: false, bitDepth);
                    actual.AsSpan().Fill(12345);
                    Av1InverseTransform.Transform(dequant, actual.AsSpan(0, length), txSz, txType, lossless: false, bitDepth);
                    Assert.True(expected.AsSpan(0, length).SequenceEqual(actual.AsSpan(0, length)), $"Transform size {txSz}, type {txType}, round {round}.");
                }
            }
        }

        static bool IsApplicable(Av1InverseTransform.Kind kind, int log2Size) => kind switch
        {
            Av1InverseTransform.Kind.Adst => log2Size <= 4,
            Av1InverseTransform.Kind.Identity => log2Size <= 5,
            _ => true,
        };
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Av1ForwardTransformMatchesTheScalarReference()
    {
        // Every transform size and every transform type that applies to it, on residuals of random, extreme (the largest
        // differences of 12-bit samples), sparse and constant samples
        var random = new Random(42);
        var expected = new int[32 * 32];
        var actual = new int[32 * 32];
        for (var txSz = 0; txSz < Av1Constants.TxSizesAll; txSz++)
        {
            var log2W = Av1Tables.TxWidthLog2[txSz];
            var log2H = Av1Tables.TxHeightLog2[txSz];
            var count = Math.Min(32, 1 << log2W) * Math.Min(32, 1 << log2H);
            var residual = new int[(1 << log2W) << log2H];
            for (var txType = 0; txType < Av1Constants.TxTypes; txType++)
            {
                if (!IsApplicable(Av1InverseTransform.GetRowKind(txType), log2W) || !IsApplicable(Av1InverseTransform.GetColumnKind(txType), log2H))
                    continue;

                for (var round = 0; round < 12; round++)
                {
                    for (var i = 0; i < residual.Length; i++)
                    {
                        residual[i] = (round % 4) switch
                        {
                            0 => random.Next(-4095, 4096),
                            1 => random.Next(2) == 0 ? -4095 : 4095,
                            2 => random.Next(8) == 0 ? random.Next(-300, 301) : 0,
                            _ => round is 3 ? 4095 : -4095,
                        };
                    }

                    Av1ForwardTransform.ForwardScalar(residual, expected.AsSpan(0, count), txSz, txType);
                    Av1ForwardTransform.Forward(residual, actual.AsSpan(0, count), txSz, txType);
                    Assert.True(expected.AsSpan(0, count).SequenceEqual(actual.AsSpan(0, count)), $"Transform size {txSz}, type {txType}, round {round}.");
                }
            }
        }

        static bool IsApplicable(Av1InverseTransform.Kind kind, int log2Size) => kind switch
        {
            Av1InverseTransform.Kind.Adst => log2Size <= 4,
            Av1InverseTransform.Kind.Identity => log2Size <= 5,
            _ => true,
        };
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Av1RestorationSearchSumsMatchTheScalarReferences()
    {
        // Rows of every length around the vector size, of random and extreme 12-bit samples, and filtered rows of values
        // up to the range of the horizontal Wiener filter of 12-bit samples
        var random = new Random(11);
        int[] lengths = [0, 1, 3, 4, 5, 7, 8, 9, 63, 64, 65, 256, 300];
        foreach (var length in lengths)
        {
            for (var round = 0; round < 6; round++)
            {
                var row = new ushort[length + 6];
                var target = new ushort[length];
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = round % 2 == 0 ? (ushort)random.Next(4096) : (ushort)(random.Next(2) * 4095);
                }

                for (var i = 0; i < target.Length; i++)
                {
                    target[i] = round % 2 == 0 ? (ushort)random.Next(4096) : (ushort)(random.Next(2) * 4095);
                }

                var expected = new long[9];
                var actual = new long[9];
                expected.AsSpan().Fill(round);
                actual.AsSpan().Fill(round);
                Av1RestorationSearch.AccumulateHorizontalScalar(row, target, expected);
                Av1RestorationSearch.AccumulateHorizontal(row, target, actual);
                Assert.Equal(expected, actual);

                var stride = length + random.Next(3);
                var rows = new int[(7 * stride) + 1];
                for (var i = 0; i < rows.Length; i++)
                {
                    rows[i] = round % 2 == 0 ? random.Next(-(1 << 22), 1 << 22) : (random.Next(2) == 0 ? -(1 << 22) : (1 << 22) - 1);
                }

                Av1RestorationSearch.AccumulateVerticalScalar(rows, stride, target, expected);
                Av1RestorationSearch.AccumulateVertical(rows, stride, target, actual);
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Av1SimdOperationsMatchTheirDefinitions()
    {
        // The operations on random and extreme lanes: the AdvSimd instructions on Arm64, the SSE ones on x64
        if (!Av1Simd.IsSupported)
            return;

        var random = new Random(5);
        for (var round = 0; round < 2000; round++)
        {
            var a = RandomInts(random, round);
            var b = RandomInts(random, round);
            var c = Vector128.Create(random.NextInt64(-(1L << 50), 1L << 50), random.NextInt64(-(1L << 50), 1L << 50));
            var va = Vector128.Create(a);
            var vb = Vector128.Create(b);
            Assert.Equal(Vector128.Create((long)a[0] * b[0], (long)a[1] * b[1]), Av1Simd.MultiplyWideningLower(va, vb));
            Assert.Equal(Vector128.Create((long)a[2] * b[2], (long)a[3] * b[3]), Av1Simd.MultiplyWideningUpper(va, vb));
            Assert.Equal(c + Av1Simd.MultiplyWideningLower(va, vb), Av1Simd.MultiplyWideningLowerAndAdd(c, va, vb));
            Assert.Equal(c + Av1Simd.MultiplyWideningUpper(va, vb), Av1Simd.MultiplyWideningUpperAndAdd(c, va, vb));
            Assert.Equal(c - Av1Simd.MultiplyWideningLower(va, vb), Av1Simd.MultiplyWideningLowerAndSubtract(c, va, vb));
            Assert.Equal(c - Av1Simd.MultiplyWideningUpper(va, vb), Av1Simd.MultiplyWideningUpperAndSubtract(c, va, vb));
            var d = Vector128.Create(-c.GetElement(1), c.GetElement(0) >> 7);
            Assert.Equal(Vector128.Create(Round(c.GetElement(0), 12), Round(c.GetElement(1), 12), Round(d.GetElement(0), 12), Round(d.GetElement(1), 12)), Av1Simd.RoundNarrow12(c, d));
            var shift = 1 + (round % 12);
            Assert.Equal(Vector128.Create(Round(a[0], shift), Round(a[1], shift), Round(a[2], shift), Round(a[3], shift)), Av1Simd.ShiftRightArithmeticRounded(va, shift));
            Assert.Equal(Vector128.Create(Saturate((long)a[0] + b[0]), Saturate((long)a[1] + b[1]), Saturate((long)a[2] + b[2]), Saturate((long)a[3] + b[3])), Av1Simd.AddSaturate(va, vb));
            Assert.Equal(Vector128.Create(Saturate((long)a[0] - b[0]), Saturate((long)a[1] - b[1]), Saturate((long)a[2] - b[2]), Saturate((long)a[3] - b[3])), Av1Simd.SubtractSaturate(va, vb));
            Assert.Equal(unchecked(a[0] + a[1] + a[2] + a[3]), Av1Simd.AddAcross(va));

            var e = RandomInts(random, round);
            var f = RandomInts(random, round);
            Av1Simd.Transpose(va, vb, Vector128.Create(e), Vector128.Create(f), out var r0, out var r1, out var r2, out var r3);
            Vector128<int>[] transposed = [r0, r1, r2, r3];
            int[][] rows = [a, b, e, f];
            for (var i = 0; i < 4; i++)
            {
                for (var j = 0; j < 4; j++)
                {
                    Assert.Equal(rows[i][j], transposed[j].GetElement(i));
                }
            }

            var x = new short[8];
            var y = new short[8];
            for (var i = 0; i < 8; i++)
            {
                x[i] = round % 3 is 0 ? (short)(random.Next(2) == 0 ? short.MinValue : short.MaxValue) : (short)random.Next(short.MinValue, short.MaxValue + 1);
                y[i] = (short)random.Next(short.MinValue, short.MaxValue + 1);
            }

            var vx = Vector128.Create(x);
            var vy = Vector128.Create(y);
            Assert.Equal(Vector128.Create(x[0] * y[0], x[1] * y[1], x[2] * y[2], x[3] * y[3]), Av1Simd.MultiplyWideningLower(vx, vy));
            Assert.Equal(Vector128.Create(x[4] * y[4], x[5] * y[5], x[6] * y[6], x[7] * y[7]), Av1Simd.MultiplyWideningUpper(vx, vy));
            Assert.Equal(unchecked((short)(x[0] + x[1] + x[2] + x[3] + x[4] + x[5] + x[6] + x[7])), Av1Simd.AddAcross(vx));
            Assert.Equal(Vector128.Create(unchecked((short)(x[0] + x[1])), (short)(x[2] + x[3]), (short)(x[4] + x[5]), (short)(x[6] + x[7]), 0, 0, 0, 0), Av1Simd.AddPairwise(vx));
            CheckExtract(x, y, 0, Av1Simd.ExtractVector128(vx, vy, 0));
            CheckExtract(x, y, 1, Av1Simd.ExtractVector128(vx, vy, 1));
            CheckExtract(x, y, 2, Av1Simd.ExtractVector128(vx, vy, 2));
            CheckExtract(x, y, 3, Av1Simd.ExtractVector128(vx, vy, 3));
            CheckExtract(x, y, 4, Av1Simd.ExtractVector128(vx, vy, 4));
            CheckExtract(x, y, 5, Av1Simd.ExtractVector128(vx, vy, 5));
            CheckExtract(x, y, 6, Av1Simd.ExtractVector128(vx, vy, 6));
            CheckExtract(x, y, 7, Av1Simd.ExtractVector128(vx, vy, 7));

            const int LineStep = 11;
            var samples = new ushort[(3 * LineStep) + 8 + 3];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = (ushort)random.Next(ushort.MaxValue + 1);
            }

            var columns = new Vector128<int>[8];
            Av1Simd.LoadTransposed(samples, 3, LineStep, columns);
            for (var i = 0; i < 4; i++)
            {
                for (var j = 0; j < 8; j++)
                {
                    Assert.Equal(samples[3 + (i * LineStep) + j], columns[j].GetElement(i));
                }
            }

            var stored = new ushort[samples.Length];
            Av1Simd.StoreTransposed(stored, 3, LineStep, columns);
            for (var i = 0; i < 4; i++)
            {
                Assert.True(samples.AsSpan(3 + (i * LineStep), 8).SequenceEqual(stored.AsSpan(3 + (i * LineStep), 8)), $"Row {i}.");
            }
        }

        static int[] RandomInts(Random random, int round)
        {
            var values = new int[4];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = (round % 4) switch
                {
                    0 => random.Next(2) == 0 ? int.MinValue : int.MaxValue,
                    1 => random.Next(-5000, 5001),
                    _ => random.Next(int.MinValue, int.MaxValue),
                };
            }

            return values;
        }

        static void CheckExtract(short[] upper, short[] lower, int index, Vector128<short> actual)
        {
            for (var i = 0; i < 8; i++)
            {
                Assert.Equal(i + index < 8 ? upper[i + index] : lower[i + index - 8], actual.GetElement(i));
            }
        }

        static int Round(long value, int bits) => (int)((value + (1L << (bits - 1))) >> bits);

        static int Saturate(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Av1EncoderKernelsMatchTheScalarReferences()
    {
        // Rows of every length around the vector sizes, longer than a chunk of the 32-bit sums, of random and extreme 12-bit
        // samples
        var random = new Random(7);
        int[] lengths = [0, 1, 3, 4, 7, 8, 9, 15, 16, 17, 31, 32, 33, 64, 100, 511, 512, 513, 1030, 4096, 5000];
        foreach (var length in lengths)
        {
            for (var round = 0; round < 8; round++)
            {
                var left = new ushort[length];
                var right = new ushort[length];
                for (var i = 0; i < length; i++)
                {
                    (left[i], right[i]) = (round % 4) switch
                    {
                        0 => ((ushort)random.Next(4096), (ushort)random.Next(4096)),
                        1 => ((ushort)4095, (ushort)0),
                        2 => ((ushort)0, (ushort)4095),
                        _ => ((ushort)random.Next(256), (ushort)random.Next(256)),
                    };
                }

                Assert.Equal(Av1EncoderKernels.SumOfAbsoluteDifferencesScalar(left, right), Av1EncoderKernels.SumOfAbsoluteDifferences(left, right));
                Assert.Equal(Av1EncoderKernels.SumOfSquaredDifferencesScalar(left, right), Av1EncoderKernels.SumOfSquaredDifferences(left, right));
                var expected = new int[length];
                var actual = new int[length];
                Av1EncoderKernels.SubtractScalar(left, right, expected);
                Av1EncoderKernels.Subtract(left, right, actual);
                Assert.Equal(expected, actual);

                // The chroma from luma error: source and DC prediction packed, luma minus its average (8 x 4095 at most)
                var packed = new int[length];
                var luma = new int[length];
                for (var i = 0; i < length; i++)
                {
                    packed[i] = left[i] | (right[i] << 16);
                    luma[i] = random.Next(-32760, 32761);
                }

                var alpha = random.Next(-16, 17);
                Assert.Equal(Av1EncoderKernels.GetChromaFromLumaErrorScalar(packed, luma, alpha, 4095), Av1EncoderKernels.GetChromaFromLumaError(packed, luma, alpha, 4095));
            }
        }
    }

    [Theory]
    [InlineData(PixelFormat.Rgb24, false, false)]
    [InlineData(PixelFormat.Rgba32, false, false)]
    [InlineData(PixelFormat.Rgba32, false, true)]
    [InlineData(PixelFormat.Rgba32, true, false)]
    [InlineData(PixelFormat.Rgba32, true, true)]
    [InlineData(PixelFormat.Gray8, true, false)]
    [InlineData(PixelFormat.Rgba64, false, false)]
    [InlineData(PixelFormat.Rgba64, false, true)]
    [InlineData(PixelFormat.Rgba64, true, false)]
    [InlineData(PixelFormat.Gray16, true, false)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void HeifYuvConversionMatchesTheScalarReference(PixelFormat layout, bool monochrome, bool limitedRangeAlpha)
    {
        // Every matrix that has no transfer characteristics, both ranges, the alpha planes of both ranges, and widths around
        // the vector sizes, with random samples and the extremes of the range
        var bitDepths = layout is PixelFormat.Rgba64 or PixelFormat.Gray16 ? new[] { 10, 12 } : [8];
        int[] matrices = monochrome ? [Av1Constants.McBt601] : [Av1Constants.McIdentity, Av1Constants.McBt709, Av1Constants.McUnspecified, Av1Constants.McFcc, Av1Constants.McBt470BG, Av1Constants.McBt601, Av1Constants.McSmpte240, Av1Constants.McSmpteYcgco, Av1Constants.McBt2020Ncl, Av1Constants.McSmpte2085, Av1Constants.McChromatNcl];
        var random = new Random((int)layout + (monochrome ? 10 : 0) + (limitedRangeAlpha ? 100 : 0));
        foreach (var bitDepth in bitDepths)
        {
            var max = (1 << bitDepth) - 1;
            foreach (var matrix in matrices)
            {
                foreach (var fullRange in new[] { false, true })
                {
                    foreach (var withAlpha in layout is PixelFormat.Rgba32 or PixelFormat.Rgba64 ? new[] { false, true } : [false])
                    {
                        if (limitedRangeAlpha && !withAlpha)
                            continue;

                        var format = new HeifBitstreamFormat(bitDepth, monochrome, 0, 0, Av1Constants.CspUnknown, matrix, fullRange, ColorDescriptionPresent: true, ColorPrimaries: 1, TransferCharacteristics: 1);
                        HeifBitstreamFormat? alphaFormat = withAlpha ? new HeifBitstreamFormat(bitDepth, IsMonochrome: true, 0, 0, Av1Constants.CspUnknown, Av1Constants.McIdentity, !limitedRangeAlpha, ColorDescriptionPresent: false, 2, 2) : null;
                        var converter = new HeifYuvConverter(AvifBinding.Instance, format, matrix, fullRange, alphaFormat, layout, isPremultiplied: false, colorPrimaries: 1, transferCharacteristics: 1);
                        foreach (var width in new[] { 1, 7, 8, 9, 10, 11, 15, 16, 17, 18, 19, 31, 33, 64, 101 })
                        {
                            var y = new ushort[width];
                            var u = new ushort[width];
                            var v = new ushort[width];
                            var a = new ushort[width];
                            for (var round = 0; round < 20; round++)
                            {
                                for (var x = 0; x < width; x++)
                                {
                                    y[x] = Sample(random, round, max);
                                    u[x] = Sample(random, round, max);
                                    v[x] = Sample(random, round, max);
                                    a[x] = Sample(random, round, max);
                                }

                                var expected = new byte[width * converter.BytesPerPixel];
                                var actual = new byte[width * converter.BytesPerPixel];
                                Array.Fill(actual, (byte)0xA5);
                                ReadOnlySpan<ushort> alpha = withAlpha ? a : [];
                                converter.ConvertRowScalar(y, u, v, alpha, expected);
                                converter.ConvertRow(y, u, v, alpha, actual);
                                Assert.Equal(expected, actual);
                            }
                        }
                    }
                }
            }
        }

        static ushort Sample(Random random, int round, int max) => (round % 4) switch
        {
            0 => (ushort)(random.Next(2) is 0 ? 0 : max),
            1 => (ushort)random.Next(Math.Min(max, 20)),
            2 => (ushort)(max - random.Next(Math.Min(max, 20))),
            _ => (ushort)random.Next(max + 1),
        };
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 0)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void HeifChromaUpsamplingMatchesTheScalarReference(int subsamplingX, int subsamplingY)
    {
        // Every chroma sample position of horizontally subsampled chroma, the bit depths of the codecs, and widths around
        // the vector sizes
        var random = new Random((subsamplingX * 2) + subsamplingY);
        foreach (var bitDepth in new[] { 8, 10, 12 })
        {
            var max = (1 << bitDepth) - 1;
            foreach (var position in new[] { Av1Constants.CspUnknown, Av1Constants.CspVertical, Av1Constants.CspColocated })
            {
                var format = new HeifBitstreamFormat(bitDepth, false, subsamplingX, subsamplingY, position, Av1Constants.McBt709, false, ColorDescriptionPresent: true, 1, 1);
                var converter = new HeifYuvConverter(AvifBinding.Instance, format, Av1Constants.McBt709, fullRange: false, alpha: null, bitDepth is 8 ? PixelFormat.Rgb24 : PixelFormat.Rgba64);
                foreach (var width in new[] { 1, 2, 3, 4, 17, 18, 19, 20, 21, 33, 34, 35, 36, 37, 38, 39, 40, 64, 65, 101, 128 })
                {
                    var chromaWidth = (width + subsamplingX) >> subsamplingX;
                    var near = new ushort[chromaWidth + 3];
                    var other = new ushort[chromaWidth + 3];
                    for (var round = 0; round < 20; round++)
                    {
                        for (var i = 0; i < near.Length; i++)
                        {
                            near[i] = (ushort)(round % 3 is 0 ? (random.Next(2) is 0 ? 0 : max) : random.Next(max + 1));
                            other[i] = (ushort)(round % 3 is 0 ? (random.Next(2) is 0 ? 0 : max) : random.Next(max + 1));
                        }

                        var parity = round & 1;
                        var expected = new ushort[width];
                        var actual = new ushort[width];
                        Array.Fill(actual, (ushort)0xA5A5);
                        converter.UpsampleRowScalar(near, subsamplingY is 0 ? near : other, parity, expected, 0, width);
                        converter.UpsampleRow(near, subsamplingY is 0 ? near : other, parity, actual);
                        Assert.Equal(expected, actual);
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void HevcIntraPredictionMatchesTheScalarReferences(int bitDepth)
    {
        // Every angular mode, the planar mode and the filtering of the neighbouring samples, for every block size, with
        // random neighbouring samples and the extremes of the range
        var random = new Random(bitDepth);
        var maxValue = (1 << bitDepth) - 1;
        const int Stride = 40;
        for (var log2 = 2; log2 <= 5; log2++)
        {
            var nTbS = 1 << log2;
            var p = new ushort[HevcIntraPredictor.MaxNeighbourCount];
            for (var round = 0; round < 40; round++)
            {
                for (var i = 0; i < p.Length; i++)
                {
                    p[i] = (ushort)(round % 4 is 0 ? (random.Next(2) is 0 ? 0 : maxValue) : random.Next(maxValue + 1));
                }

                var neighbours = p.AsSpan(0, (4 * nTbS) + 1);
                for (var mode = 2; mode <= 34; mode++)
                {
                    foreach (var edgeFilter in new[] { false, true })
                    {
                        var expected = new ushort[Stride * nTbS];
                        var actual = new ushort[Stride * nTbS];
                        HevcIntraPredictor.PredictAngularScalar(neighbours, expected, Stride, nTbS, mode, edgeFilter, bitDepth);
                        HevcIntraPredictor.PredictAngular(neighbours, actual, Stride, nTbS, mode, edgeFilter, bitDepth);
                        Assert.Equal(expected, actual);
                    }
                }

                var expectedPlanar = new ushort[Stride * nTbS];
                var actualPlanar = new ushort[Stride * nTbS];
                HevcIntraPredictor.PredictPlanarScalar(neighbours, expectedPlanar, Stride, nTbS);
                HevcIntraPredictor.PredictPlanar(neighbours, actualPlanar, Stride, nTbS, bitDepth);
                Assert.Equal(expectedPlanar, actualPlanar);

                foreach (var strong in new[] { false, true })
                {
                    var expectedFiltered = new ushort[neighbours.Length];
                    var actualFiltered = new ushort[neighbours.Length];
                    var expectedFlag = HevcIntraPredictor.FilterScalar(neighbours, expectedFiltered, nTbS, strong, bitDepth);
                    var actualFlag = HevcIntraPredictor.Filter(neighbours, actualFiltered, nTbS, strong, bitDepth);
                    Assert.Equal(expectedFlag, actualFlag);
                    Assert.Equal(expectedFiltered, actualFiltered);
                }
            }
        }
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 0)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void HevcInverseTransformMatchesTheScalarReference(int log2TrafoSize, int trType)
    {
        // Blocks of zeros, a DC coefficient, a few low frequencies, sparse and dense blocks, with coefficients over the whole
        // range of CoeffMinY to CoeffMaxY
        var random = new Random((log2TrafoSize * 10) + trType);
        var nTbS = 1 << log2TrafoSize;
        var scratch = new int[nTbS * nTbS];
        for (var round = 0; round < 1000; round++)
        {
            var block = new int[nTbS * nTbS];
            for (var i = 0; i < block.Length; i++)
            {
                var row = i / nTbS;
                var column = i % nTbS;
                block[i] = (round % 6) switch
                {
                    0 => 0,
                    1 => i is 0 ? random.Next(HevcInverseTransform.CoeffMin, HevcInverseTransform.CoeffMax + 1) : 0,
                    2 => row < 2 && column < 2 ? random.Next(-512, 513) : 0,
                    3 => random.Next(5) is 0 ? random.Next(-64, 65) : 0,
                    4 => random.Next(2) is 0 ? (random.Next(2) is 0 ? HevcInverseTransform.CoeffMin : HevcInverseTransform.CoeffMax) : 0,
                    _ => random.Next(HevcInverseTransform.CoeffMin, HevcInverseTransform.CoeffMax + 1),
                };
            }

            var expected = (int[])block.Clone();
            HevcInverseTransform.TransformScalar(expected, log2TrafoSize, trType, scratch);
            Array.Fill(scratch, int.MinValue);
            HevcInverseTransform.Transform(block, log2TrafoSize, trType, scratch);
            Assert.Equal(expected, block);
        }
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 0)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void HevcInverseTransformOfTheFirstRowsAndColumnsMatchesTheScalarReference(int log2TrafoSize, int trType)
    {
        // The coefficients outside the rows and the columns are not read: they are garbage here, and zeros for the reference,
        // whose residual is then rounded by Equation 8-299 for 8, 10 and 12 bits, or not
        var random = new Random((log2TrafoSize * 10) + trType + 1000);
        var nTbS = 1 << log2TrafoSize;
        var scratch = new int[nTbS * nTbS];
        for (var round = 0; round < 1000; round++)
        {
            var rows = random.Next(1, nTbS + 1);
            var columns = 4 * random.Next(1, (nTbS / 4) + 1);
            var residualShift = (round % 4) switch
            {
                0 => 0,
                1 => 12,
                2 => 10,
                _ => 8,
            };
            var block = new int[nTbS * nTbS];
            var expected = new int[nTbS * nTbS];
            for (var i = 0; i < block.Length; i++)
            {
                var inside = i / nTbS < rows && i % nTbS < columns;
                var value = (round % 5) switch
                {
                    0 => 0,
                    1 => i is 0 ? random.Next(HevcInverseTransform.CoeffMin, HevcInverseTransform.CoeffMax + 1) : 0,
                    2 => random.Next(5) is 0 ? random.Next(-64, 65) : 0,
                    3 => random.Next(2) is 0 ? (random.Next(2) is 0 ? HevcInverseTransform.CoeffMin : HevcInverseTransform.CoeffMax) : 0,
                    _ => random.Next(HevcInverseTransform.CoeffMin, HevcInverseTransform.CoeffMax + 1),
                };
                block[i] = inside ? value : random.Next(int.MinValue, int.MaxValue);
                expected[i] = inside ? value : 0;
            }

            HevcInverseTransform.TransformScalar(expected, log2TrafoSize, trType, scratch);
            if (residualShift > 0)
            {
                for (var i = 0; i < expected.Length; i++)
                {
                    expected[i] = (expected[i] + (1 << (residualShift - 1))) >> residualShift;
                }
            }

            Array.Fill(scratch, int.MinValue);
            HevcInverseTransform.Transform(block, log2TrafoSize, trType, scratch, rows, columns, residualShift);
            Assert.Equal(expected, block);
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void HevcPictureConstructionMatchesTheScalarReference(int bitDepth)
    {
        // Residuals that clip at both ends of the range of the samples, and rows of every block size and around them
        var random = new Random(bitDepth);
        var maxValue = (1 << bitDepth) - 1;
        foreach (var length in new[] { 1, 4, 7, 8, 9, 15, 16, 17, 32, 33 })
        {
            for (var round = 0; round < 200; round++)
            {
                var row = new ushort[length];
                var residual = new int[length];
                for (var x = 0; x < length; x++)
                {
                    row[x] = (ushort)random.Next(maxValue + 1);
                    residual[x] = (round % 3) switch
                    {
                        0 => random.Next(-maxValue - 1, maxValue + 2),
                        1 => random.Next(2) is 0 ? int.MinValue / 2 : int.MaxValue / 2,
                        _ => random.Next(-8, 9),
                    };
                }

                var expected = (ushort[])row.Clone();
                HevcSliceCoder.AddResidualRowScalar(expected, residual, maxValue);
                HevcSliceCoder.AddResidualRow(row, residual, maxValue);
                Assert.Equal(expected, row);
            }
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(16)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void HevcEncoderDistortionsMatchTheScalarReferences(int bitDepth)
    {
        // The block sizes of the encoder and others, with strides larger than the blocks, random samples, residuals that
        // clip, and the extremes of the range
        var random = new Random(bitDepth);
        var max = (1 << bitDepth) - 1;
        foreach (var (width, height) in new[] { (4, 4), (8, 8), (16, 16), (32, 32), (64, 64), (4, 16), (12, 16), (16, 4), (24, 32), (48, 64), (64, 16), (8, 32) })
        {
            for (var round = 0; round < 40; round++)
            {
                var first = new ushort[(width + 3) * height];
                var second = new ushort[(width + 7) * height];
                var residual = new int[width * width];
                for (var i = 0; i < first.Length; i++)
                {
                    first[i] = Sample(random, round, max);
                }

                for (var i = 0; i < second.Length; i++)
                {
                    second[i] = Sample(random, round, max);
                }

                for (var i = 0; i < residual.Length; i++)
                {
                    residual[i] = random.Next(-2 * max, (2 * max) + 1);
                }

                Assert.Equal(HevcSliceCoder.GetSadScalar(first, width + 3, second, width + 7, width, height), HevcSliceCoder.GetSad(first, width + 3, second, width + 7, width, height));
                Assert.Equal(HevcSliceCoder.GetSseScalar(first, width + 3, second, width + 7, width, height), HevcSliceCoder.GetSse(first, width + 3, second, width + 7, width, height, bitDepth));
                if (width != height || width > 32)
                    continue;

                Assert.Equal(HevcSliceCoder.GetReconstructionSseScalar(first, width + 3, second, width + 7, residual, width, bitDepth), HevcSliceCoder.GetReconstructionSse(first, width + 3, second, width + 7, residual, width, bitDepth));
                var expected = new int[width * width];
                var actual = new int[width * width];
                Assert.Equal(HevcSliceCoder.GetResidualScalar(first, width + 3, second, width + 7, expected, width), HevcSliceCoder.GetResidual(first, width + 3, second, width + 7, actual, width, bitDepth));
                Assert.Equal(expected, actual);
            }
        }

        static ushort Sample(Random random, int round, int max) => (ushort)(round % 4 is 0 ? (random.Next(2) is 0 ? 0 : max) : random.Next(max + 1));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void HevcInterPredictionMatchesTheScalarReferences(int bitDepth)
    {
        // Every fractional position of the luma and chroma filters, the block sizes of the prediction units, blocks inside
        // the reference picture and blocks that reach outside it, then the weighted sample prediction processes
        var random = new Random(bitDepth);
        var max = (1 << bitDepth) - 1;
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(HevcInterPredictionMatchesTheScalarReferences));
        using var reference = Av1Plane.Create(scope, 96, 80, 16);
        for (var y = 0; y < reference.Height; y++)
        {
            for (var x = 0; x < reference.Width; x++)
            {
                reference.Samples[(y * reference.Stride) + x] = (ushort)(random.Next(4) is 0 ? (random.Next(2) is 0 ? 0 : max) : random.Next(max + 1));
            }
        }

        var intermediate = new short[71 * 64];
        var window = new ushort[71 * 71];
        foreach (var luma in new[] { true, false })
        {
            foreach (var (width, height) in new[] { (4, 4), (8, 8), (16, 16), (32, 32), (64, 64), (12, 16), (4, 8), (24, 32), (48, 64), (16, 4), (2, 4), (6, 8), (64, 16) })
            {
                for (var round = 0; round < 6; round++)
                {
                    var xInt = random.Next(-8, 96 - width + 8);
                    var yInt = random.Next(-8, 80 - height + 8);
                    for (var frac = 0; frac < (luma ? 16 : 64); frac++)
                    {
                        var xFrac = luma ? frac & 3 : frac & 7;
                        var yFrac = luma ? frac >> 2 : frac >> 3;
                        var expected = new short[width * height];
                        var actual = new short[width * height];
                        HevcInterPredictor.PredictScalar(reference, xInt, yInt, xFrac, yFrac, width, height, luma, bitDepth, expected, intermediate, window);
                        HevcInterPredictor.Predict(reference, xInt, yInt, xFrac, yFrac, width, height, luma, bitDepth, actual, intermediate, window);
                        Assert.Equal(expected, actual);
                    }

                    // The weighted sample prediction of two predictions
                    var prediction0 = new short[width * height];
                    var prediction1 = new short[width * height];
                    HevcInterPredictor.Predict(reference, xInt, yInt, 1, 2, width, height, luma, bitDepth, prediction0, intermediate, window);
                    HevcInterPredictor.Predict(reference, yInt + 3, xInt + 2, 3, 1, width, height, luma, bitDepth, prediction1, intermediate, window);
                    var stride = width + 5;
                    var weight0 = random.Next(-128, 128);
                    var weight1 = random.Next(-128, 128);
                    var offset0 = random.Next(-(1 << (bitDepth - 1)), 1 << (bitDepth - 1));
                    var offset1 = random.Next(-(1 << (bitDepth - 1)), 1 << (bitDepth - 1));
                    var log2Wd = random.Next(0, 8) + (14 - bitDepth);
                    for (var kind = 0; kind < 4; kind++)
                    {
                        var expectedSamples = new ushort[stride * height];
                        var actualSamples = new ushort[stride * height];
                        foreach (var (samples, useVectors) in new[] { (expectedSamples, false), (actualSamples, true) })
                        {
                            switch (kind)
                            {
                                case 0:
                                    HevcInterPredictor.WeightDefault(prediction0, samples, stride, width, height, bitDepth, useVectors);
                                    break;
                                case 1:
                                    HevcInterPredictor.WeightDefault(prediction0, prediction1, samples, stride, width, height, bitDepth, useVectors);
                                    break;
                                case 2:
                                    HevcInterPredictor.WeightExplicit(prediction0, samples, stride, width, height, bitDepth, weight0, offset0, log2Wd, useVectors);
                                    break;
                                default:
                                    HevcInterPredictor.WeightExplicit(prediction0, prediction1, samples, stride, width, height, bitDepth, weight0, weight1, offset0, offset1, log2Wd, useVectors);
                                    break;
                            }
                        }

                        Assert.Equal(expectedSamples, actualSamples);
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void HevcDeblockingLumaLinesMatchTheScalarReference(int bitDepth)
    {
        // Both orientations and both filters, with every combination of the decisions, random values of tC and samples
        // that are smooth (most lines filtered), noisy (some lines left as they are) or at the extremes of the range
        var random = new Random(bitDepth);
        var maxValue = (1 << bitDepth) - 1;
        const int Stride = 24;
        for (var round = 0; round < 4000; round++)
        {
            var expected = new ushort[Stride * 16];
            var level = random.Next(maxValue + 1);
            for (var i = 0; i < expected.Length; i++)
            {
                expected[i] = (round % 3) switch
                {
                    0 => (ushort)Math.Clamp(level + random.Next(-6, 7), 0, maxValue),
                    1 => (ushort)random.Next(maxValue + 1),
                    _ => (ushort)(random.Next(2) is 0 ? 0 : maxValue),
                };
            }

            var actual = (ushort[])expected.Clone();
            var vertical = random.Next(2) is 0;
            var origin = vertical ? (6 * Stride) + 8 : (8 * Stride) + 6;
            var dE = random.Next(1, 3);
            var dEp = random.Next(2) is 0;
            var dEq = random.Next(2) is 0;
            var tC = random.Next(0, 25) << (bitDepth - 8);
            var filterP = random.Next(4) is not 0;
            var filterQ = random.Next(4) is not 0;
            HevcDeblockingFilter.FilterLumaLinesScalar(expected, origin, vertical, Stride, dE, dEp, dEq, tC, filterP, filterQ, maxValue);
            HevcDeblockingFilter.FilterLumaLines(actual, origin, vertical, Stride, dE, dEp, dEq, tC, filterP, filterQ, maxValue);
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(16)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void HevcSampleAdaptiveOffsetRowsMatchTheScalarReferences(int bitDepth)
    {
        // Runs of every length around the vector size, with random, flat (equal neighbours) and extreme samples, and offsets of
        // the whole range of SaoOffsetVal (31 << ( bitDepth - 10 ) at most)
        var random = new Random(bitDepth);
        var maxValue = (1 << bitDepth) - 1;
        var maxOffset = 31 << Math.Max(0, bitDepth - 10);
        for (var round = 0; round < 2000; round++)
        {
            var length = random.Next(1, 70);
            var samples = new ushort[length];
            var neighbours0 = new ushort[length];
            var neighbours1 = new ushort[length];
            foreach (var values in new[] { samples, neighbours0, neighbours1 })
            {
                for (var i = 0; i < length; i++)
                {
                    values[i] = (round % 3) switch
                    {
                        0 => (ushort)random.Next(maxValue + 1),
                        1 => (ushort)((maxValue / 2) + random.Next(-1, 2)),
                        _ => (ushort)(random.Next(2) * maxValue),
                    };
                }
            }

            int[] offsets = [random.Next(-maxOffset, maxOffset + 1), random.Next(-maxOffset, maxOffset + 1), round % 4 is 0 ? 0 : random.Next(-maxOffset, maxOffset + 1), random.Next(-maxOffset, maxOffset + 1), random.Next(-maxOffset, maxOffset + 1)];
            var expected = new ushort[length];
            var actual = new ushort[length];
            HevcSampleAdaptiveOffset.EdgeOffsetRowScalar(samples, neighbours0, neighbours1, expected, offsets, maxValue);
            HevcSampleAdaptiveOffset.EdgeOffsetRow(samples, neighbours0, neighbours1, actual, offsets, maxValue);
            Assert.Equal(expected, actual);

            offsets[0] = 0;
            var saoLeftClass = random.Next(32);
            HevcSampleAdaptiveOffset.BandOffsetRowScalar(samples, expected, bitDepth - 5, saoLeftClass, offsets, maxValue);
            HevcSampleAdaptiveOffset.BandOffsetRow(samples, actual, bitDepth - 5, saoLeftClass, offsets, maxValue);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void WebPLoopFilterMatchesTheScalarReference()
    {
        // Frames of 4x4 blocks of a random level each, with small or large variations inside them: the edges between the blocks
        // take every branch of the filters (not filtered, high edge variance, flat), at every level and sharpness
        var random = new Random(6386);
        var scope = new AllocationScope(ImageResourceLimits.Default);
        for (var round = 0; round < 200; round++)
        {
            var width = random.Next(1, 72);
            var height = random.Next(1, 72);
            var mbWidth = (width + 15) >> 4;
            var mbHeight = (height + 15) >> 4;
            using var expected = Vp8Planes.Create(scope, width, height);
            using var actual = Vp8Planes.Create(scope, width, height);
            var noise = (round % 4) switch
            {
                0 => 1,
                1 => 3,
                2 => 8,
                _ => 40,
            };
            foreach (var (plane, stride) in new[] { (0, expected.YStride), (1, expected.UVStride), (2, expected.UVStride) })
            {
                var samples = plane switch
                {
                    0 => expected.Y,
                    1 => expected.U,
                    _ => expected.V,
                };
                var blocksPerRow = (stride >> 2) + 1;
                var blockLevels = new int[(((samples.Length / stride) >> 2) + 1) * blocksPerRow];
                for (var i = 0; i < blockLevels.Length; i++)
                {
                    blockLevels[i] = random.Next(256);
                }

                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = (byte)Math.Clamp(blockLevels[(((i / stride) >> 2) * blocksPerRow) + ((i % stride) >> 2)] + random.Next(-noise, noise + 1), 0, 255);
                }
            }

            expected.Y.CopyTo(actual.Y);
            expected.U.CopyTo(actual.U);
            expected.V.CopyTo(actual.V);
            var filterInfo = new byte[mbWidth * mbHeight];
            for (var i = 0; i < filterInfo.Length; i++)
            {
                filterInfo[i] = (byte)(random.Next(64) | (random.Next(2) << 7));
            }

            var sharpness = random.Next(8);
            Vp8LoopFilter.ApplyScalar(expected, filterInfo, mbWidth, mbHeight, simple: false, sharpness);
            Vp8LoopFilter.Apply(actual, filterInfo, mbWidth, mbHeight, simple: false, sharpness);
            Assert.True(expected.Y.SequenceEqual(actual.Y), $"Luma of a {width}x{height} frame, round {round}.");
            Assert.True(expected.U.SequenceEqual(actual.U), $"Cb of a {width}x{height} frame, round {round}.");
            Assert.True(expected.V.SequenceEqual(actual.V), $"Cr of a {width}x{height} frame, round {round}.");
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void WebPLosslessTransformsMatchTheScalarReferences()
    {
        // Every predictor mode (14 and 15 included) on tiles of every size, with random, extreme and nearly flat pixels so that
        // the clamps and the select ties are taken; color transforms with every sign; widths around the vector size
        var random = new Random(4051);
        for (var round = 0; round < 1500; round++)
        {
            var width = round < 100 ? 1 + (round % 50) : random.Next(1, 140);
            var height = random.Next(1, 6);
            var sizeBits = random.Next(2, 6);
            var pixels = new uint[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = (round % 4) switch
                {
                    0 => (uint)random.Next() ^ ((uint)random.Next() << 16),
                    1 => random.Next(2) == 0 ? 0u : 0xFFFFFFFFu,
                    2 => 0x80808080u + (uint)random.Next(-2, 3),
                    _ => (uint)(random.Next(0x100) * 0x01010101) ^ (uint)random.Next(0, 4),
                };
            }

            var tilesWidth = (width + (1 << sizeBits) - 1) >> sizeBits;
            var tilesHeight = (height + (1 << sizeBits) - 1) >> sizeBits;
            var tiles = new uint[tilesWidth * tilesHeight];
            for (var i = 0; i < tiles.Length; i++)
            {
                tiles[i] = (uint)random.Next() ^ ((uint)random.Next() << 16);
            }

            var expected = pixels.ToArray();
            var actual = pixels.ToArray();
            Vp8LTransforms.InversePredictorScalar(expected, width, height, sizeBits, tiles, tilesWidth);
            Vp8LTransforms.InversePredictor(actual, width, height, sizeBits, tiles, tilesWidth);
            Assert.Equal(expected, actual);

            expected = pixels.ToArray();
            actual = pixels.ToArray();
            Vp8LTransforms.InverseColorTransformScalar(expected, width, height, sizeBits, tiles, tilesWidth);
            Vp8LTransforms.InverseColorTransform(actual, width, height, sizeBits, tiles, tilesWidth);
            Assert.Equal(expected, actual);

            expected = pixels.ToArray();
            actual = pixels.ToArray();
            Vp8LTransforms.AddGreenScalar(expected);
            Vp8LTransforms.AddGreen(actual);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void WebPAlphaAndOutputKernelsMatchTheScalarReferences()
    {
        // Alpha filtering and unfiltering (every filter, the first row and column, clamps of the gradient), the green
        // extraction and the ARGB to RGBA rows, at lengths around the vector sizes
        var random = new Random(4053);
        for (var round = 0; round < 600; round++)
        {
            var width = round < 40 ? 1 + round : random.Next(1, 100);
            var height = random.Next(1, 8);
            var values = new byte[width * height];
            random.NextBytes(values);
            if (round % 3 == 1)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = (byte)(random.Next(2) * 255);
                }
            }

            for (var filter = 0; filter < 4; filter++)
            {
                var expected = values.ToArray();
                var actual = values.ToArray();
                WebPAlphaDecoder.UnfilterScalar(expected, width, height, filter);
                WebPAlphaDecoder.Unfilter(actual, width, height, filter);
                Assert.Equal(expected, actual);

                var expectedFiltered = new byte[width];
                var actualFiltered = new byte[width];
                for (var y = 0; y < height; y++)
                {
                    var x0 = random.Next(0, width);
                    var x1 = random.Next(x0 + 1, width + 1);
                    WebPAlphaEncoder.FilterRowScalar(values, width, y, x0, x1, filter, expectedFiltered);
                    WebPAlphaEncoder.FilterRow(values, width, y, x0, x1, filter, actualFiltered);
                    Assert.Equal(expectedFiltered.AsSpan(0, x1 - x0).ToArray(), actualFiltered.AsSpan(0, x1 - x0).ToArray());
                }
            }

            var pixels = new uint[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = (uint)random.Next() ^ ((uint)random.Next() << 16);
            }

            var green = new byte[pixels.Length];
            WebPAlphaDecoder.ExtractGreen(pixels, green);
            Assert.Equal(pixels.Select(pixel => (byte)(pixel >> 8)).ToArray(), green);
            foreach (var opaque in new[] { false, true })
            {
                var expectedRgba = new byte[pixels.Length * 4];
                var actualRgba = new byte[pixels.Length * 4];
                WebPDecoder.ArgbToRgbaScalar(pixels, expectedRgba, opaque);
                WebPDecoder.ArgbToRgba(pixels, actualRgba, opaque);
                Assert.Equal(expectedRgba, actualRgba);
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void WebPLosslessPredictionCostsMatchTheScalarReferences()
    {
        // Every predictor mode on images of every small width (first row and column, the last column whose top-right pixel
        // is the first one of the row) and on 64x64 tiles, with random, extreme and nearly flat pixels (clamps, select ties)
        var random = new Random(4052);
        for (var round = 0; round < 300; round++)
        {
            var width = round < 70 ? 1 + round : random.Next(1, 150);
            var height = random.Next(1, round < 70 ? 6 : 70);
            var pixels = new uint[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = (round % 4) switch
                {
                    0 => (uint)random.Next() ^ ((uint)random.Next() << 16),
                    1 => random.Next(2) == 0 ? 0u : 0xFFFFFFFFu,
                    2 => 0x80808080u + (uint)random.Next(-2, 3),
                    _ => (uint)(random.Next(0x100) * 0x01010101) ^ (uint)random.Next(0, 4),
                };
            }

            var expected = new uint[width];
            var actual = new uint[width];
            for (var mode = 0; mode < 16; mode++)
            {
                for (var y = 0; y < height; y++)
                {
                    var x0 = random.Next(0, width);
                    var x1 = random.Next(x0 + 1, width + 1);
                    Vp8LEncoder.PredictionResidualsScalar(pixels, width, y, x0, x1, mode, expected);
                    Vp8LEncoder.PredictionResiduals(pixels, width, y, x0, x1, mode, actual);
                    Assert.Equal(expected.AsSpan(0, x1 - x0).ToArray(), actual.AsSpan(0, x1 - x0).ToArray());
                }

                if (mode < 14)
                {
                    var tilesX = (width + 63) >> 6;
                    var tilesY = (height + 63) >> 6;
                    var histogram = new int[4 * 256];
                    for (var ty = 0; ty < tilesY; ty++)
                    {
                        for (var tx = 0; tx < tilesX; tx++)
                        {
                            Assert.Equal(Vp8LEncoder.TileCostScalar(pixels, width, height, tx, ty, 6, mode, default), Vp8LEncoder.TileCost(pixels, width, height, tx, ty, 6, mode, default));
                            Assert.Equal(Vp8LEncoder.TileCostScalar(pixels, width, height, tx, ty, 6, mode, histogram), Vp8LEncoder.TileCost(pixels, width, height, tx, ty, 6, mode, histogram));
                        }
                    }
                }
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

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ColorConversionCacheMatchesTheScalarReference()
    {
        // The 8-bit conversions that interpolate a lookup table remember the colors of a call: rows of repeated colors (runs,
        // a few alternating colors, colors that share cache slots) give the samples of the stage-by-stage reference
        (Metadata.IccProfile Source, Metadata.IccProfile Destination)[] pairs =
        [
            (IccTestProfiles.RgbLabLut8(), Metadata.IccProfile.Srgb),
            (IccTestProfiles.RgbLabLutAToB(), IccTestProfiles.RgbXyzLut16()),
            (IccTestProfiles.CmykLabLut16(), Metadata.IccProfile.Srgb),
            (IccTestProfiles.CmykLabLutAToB(), IccTestProfiles.CmykLabLut16()),
            (Metadata.IccProfile.Cgats001CompatibleCmyk, Metadata.IccProfile.SrgbGray),
        ];

        var random = new Random(4242);
        foreach (var (source, destination) in pairs)
        {
            var pipeline = IccPipeline.Create(source, destination, Metadata.IccRenderingIntent.RelativeColorimetric, blackPointCompensation: false);
            Assert.True(pipeline.HasLookupTable);
            var sourceChannels = pipeline.SourceChannelCount;
            var destinationChannels = pipeline.DestinationChannelCount;
            foreach (var (extra, reverse) in new[] { (0, false), (1, false), (1, true) })
            {
                if (reverse && (sourceChannels != 3 || destinationChannels != 3))
                    continue;

                var palette = new byte[600][];
                for (var i = 0; i < palette.Length; i++)
                {
                    palette[i] = new byte[sourceChannels + extra];
                    random.NextBytes(palette[i]);
                }

                var colors = 3000;
                var samples = new byte[colors * (sourceChannels + extra)];
                for (var i = 0; i < colors; i++)
                {
                    // Runs of one color, then a few alternating colors, then many colors (more than the cache holds)
                    var color = i < 1000 ? palette[i / 50] : i < 2000 ? palette[600 - 1 - (i % 3)] : palette[random.Next(palette.Length)];
                    color.CopyTo(samples, i * (sourceChannels + extra));
                }

                var expected = new byte[colors * (destinationChannels + extra)];
                var actual = new byte[expected.Length];
                ColorConversionKernels.ConvertScalar<byte, IccByteSample>(pipeline, samples, expected, sourceChannels + extra, destinationChannels + extra, reverse);
                ColorConversionKernels.Convert<byte, IccByteSample>(pipeline, samples, actual, sourceChannels + extra, destinationChannels + extra, reverse);
                Assert.Equal(expected, actual);

                // In place, when the strides are equal
                if (sourceChannels == destinationChannels)
                {
                    ColorConversionKernels.Convert<byte, IccByteSample>(pipeline, samples, samples, sourceChannels + extra, destinationChannels + extra, reverse);
                    Assert.Equal(expected, samples);
                }
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ColorConversionOutputTablesMatchTheScalarReference()
    {
        // The 8-bit samples of the last stage (one inverse tone curve per destination channel) come from threshold tables
        // once a pipeline has converted enough colors: parametric, gamma and sampled inverse curves, gray destinations,
        // with and without a lookup table, give the samples of the stage-by-stage reference. A decreasing curve has no
        // table; identity curves and the output curves of a lookup table are evaluated
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        (Metadata.IccProfile Source, Metadata.IccProfile Destination, bool? HasTables)[] pairs =
        [
            (Metadata.IccProfile.Srgb, displayP3, true),
            (displayP3, Metadata.IccProfile.Srgb, true),
            (BuiltInIccProfiles.LinearSrgb, Metadata.IccProfile.Srgb, true),
            (Metadata.IccProfile.Srgb, IccTestProfiles.Rgb(IccTestProfiles.SrgbColorants, IccTestProfiles.Gamma(563)), true),
            (Metadata.IccProfile.Srgb, IccTestProfiles.Rgb(IccTestProfiles.SrgbColorants, IccTestProfiles.Curve(0, 4096, 16384, 36864, 65535)), true),
            (Metadata.IccProfile.Srgb, IccTestProfiles.Rgb(IccTestProfiles.SrgbColorants, IccTestProfiles.Parametric(4, 2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045, 0.005, 0.005), IccTestProfiles.Parametric(1, 1.8, 1.1, -0.1), IccTestProfiles.Parametric(2, 2.2, 0.9, 0.1, 0.05)), true),
            (Metadata.IccProfile.Srgb, IccTestProfiles.Rgb(IccTestProfiles.SrgbColorants, IccTestProfiles.Curve(65535, 30000, 0)), false),
            (Metadata.IccProfile.Srgb, Metadata.IccProfile.SrgbGray, true),
            (IccTestProfiles.GrayPaper(), IccTestProfiles.Gray(IccTestProfiles.Gamma(563)), true),
            (IccTestProfiles.RgbLabLut8(), Metadata.IccProfile.Srgb, true),
            (IccTestProfiles.CmykLabLut16(), displayP3, true),
            (Metadata.IccProfile.Srgb, BuiltInIccProfiles.LinearSrgb, null),
            (Metadata.IccProfile.Srgb, IccTestProfiles.RgbXyzLut16(), null),
            (Metadata.IccProfile.Srgb, IccTestProfiles.CmykLabLut16(), null),
        ];

        var random = new Random(31415);
        foreach (var (source, destination, hasTables) in pairs)
        {
            var pipeline = IccPipeline.Create(source, destination, Metadata.IccRenderingIntent.RelativeColorimetric, blackPointCompensation: false);
            var curves = pipeline.OutputCurves;
            Assert.Equal(hasTables is not null, curves is not null);
            Assert.NotNull(pipeline.ByteInputTables);

            // The tables are built once the colors of the calls reach the threshold
            Assert.Null(pipeline.GetByteOutputTables(IccPipeline.ByteOutputTablesThreshold - 1));
            var tables = pipeline.GetByteOutputTables(1);
            Assert.Equal(hasTables is true, tables is not null);
            Assert.Same(tables, pipeline.GetByteOutputTables(1));
            for (var channel = 0; tables is not null && channel < tables.Length; channel++)
            {
                var curve = curves![channel];
                foreach (var value in GetQuantizationInputs(tables[channel]))
                {
                    Assert.Equal(IccByteSample.Store(curve.Evaluate(value)), tables[channel].Quantize(value));
                }
            }

            var sourceChannels = pipeline.SourceChannelCount;
            var destinationChannels = pipeline.DestinationChannelCount;
            foreach (var (extra, reverse) in new[] { (0, false), (1, false), (1, true) })
            {
                if (reverse && (sourceChannels != 3 || destinationChannels != 3))
                    continue;

                // Each channel takes every value, then random colors
                var stride = sourceChannels + extra;
                var colors = (256 * sourceChannels) + 20_000;
                var samples = new byte[colors * stride];
                random.NextBytes(samples);
                for (var channel = 0; channel < sourceChannels; channel++)
                {
                    for (var value = 0; value < 256; value++)
                    {
                        samples[(((channel * 256) + value) * stride) + channel] = (byte)value;
                    }
                }

                var expected = new byte[colors * (destinationChannels + extra)];
                var actual = new byte[expected.Length];
                ColorConversionKernels.ConvertScalar<byte, IccByteSample>(pipeline, samples, expected, stride, destinationChannels + extra, reverse);
                ColorConversionKernels.Convert<byte, IccByteSample>(pipeline, samples, actual, stride, destinationChannels + extra, reverse);
                Assert.Equal(expected, actual);

                // In place, when the strides are equal
                if (sourceChannels == destinationChannels)
                {
                    ColorConversionKernels.Convert<byte, IccByteSample>(pipeline, samples, samples, stride, stride, reverse);
                    Assert.Equal(expected, samples);
                }
            }
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(5, 3)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Jpeg2000WaveletTransformsMatchTheScalarReferenceBitForBit(int originX, int originY)
    {
        // The vector paths filter the two halves of a row and strips of columns; the reference filters interleaved
        // signals, one sample at a time. Sizes around the vector sizes and the width of a strip, both parities of the
        // first sample (which change at each level with an odd origin), levels with a single row or column
        const int Levels = 3;
        int[] sizes = [1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17, 31, 33, 63, 64, 65, 67, 129, 200];
        var random = new Random((originX * 100) + originY);
        var resolutions = new Jpeg2000ResolutionGeometry[Levels + 1];
        foreach (var width in sizes)
        {
            foreach (var height in sizes)
            {
                for (var r = 0; r <= Levels; r++)
                {
                    var shift = Levels - r;
                    resolutions[r] = new Jpeg2000ResolutionGeometry
                    {
                        X0 = Jpeg2000TileGeometry.CeilingShift(originX, shift),
                        Y0 = Jpeg2000TileGeometry.CeilingShift(originY, shift),
                        X1 = Jpeg2000TileGeometry.CeilingShift(originX + width, shift),
                        Y1 = Jpeg2000TileGeometry.CeilingShift(originY + height, shift),
                    };
                }

                // Small samples, samples of 16 bits, and values whose sums wrap around
                var magnitude = random.Next(3) switch { 0 => 256, 1 => 1 << 16, _ => int.MaxValue };
                var samples = new int[width * height];
                var coefficients = new float[samples.Length];
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = random.Next(-magnitude, magnitude);
                    coefficients[i] = NextSingle(random);
                }

                var scratchLength = (int)Jpeg2000Dwt.GetScratchLength(width, height);
                var label = $"{width}x{height}";
                CheckIntegers(samples, scratchLength, (plane, scratch) => Jpeg2000Dwt.ForwardReversibleScalar(plane, width, resolutions, scratch), (plane, scratch) => Jpeg2000Dwt.ForwardReversible(plane, width, resolutions, scratch), label);
                CheckIntegers(samples, scratchLength, (plane, scratch) => Jpeg2000Dwt.InverseReversibleScalar(plane, width, resolutions, scratch), (plane, scratch) => Jpeg2000Dwt.InverseReversible(plane, width, resolutions, scratch), label);
                CheckSingles(coefficients, scratchLength, (plane, scratch) => Jpeg2000Dwt.ForwardIrreversibleScalar(plane, width, resolutions, scratch), (plane, scratch) => Jpeg2000Dwt.ForwardIrreversible(plane, width, resolutions, scratch), label);
                CheckSingles(coefficients, scratchLength, (plane, scratch) => Jpeg2000Dwt.InverseIrreversibleScalar(plane, width, resolutions, scratch), (plane, scratch) => Jpeg2000Dwt.InverseIrreversible(plane, width, resolutions, scratch), label);
            }
        }

        static void CheckIntegers(int[] source, int scratchLength, Action<int[], int[]> reference, Action<int[], int[]> transform, string label)
        {
            var expected = (int[])source.Clone();
            var actual = (int[])source.Clone();
            reference(expected, new int[scratchLength]);
            transform(actual, new int[scratchLength]);
            Assert.True(expected.AsSpan().SequenceEqual(actual), label);
        }

        static void CheckSingles(float[] source, int scratchLength, Action<float[], float[]> reference, Action<float[], float[]> transform, string label)
        {
            var expected = (float[])source.Clone();
            var actual = (float[])source.Clone();
            reference(expected, new float[scratchLength]);
            transform(actual, new float[scratchLength]);
            Assert.True(ToBits(expected).AsSpan().SequenceEqual(ToBits(actual)), label);
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Jpeg2000ComponentTransformsMatchTheScalarReferenceBitForBit()
    {
        var random = new Random(2000);
        int[] lengths = [0, 1, 3, 4, 5, 7, 8, 9, 15, 16, 17, 31, 33, 64, 100, 257];
        foreach (var length in lengths)
        {
            for (var round = 0; round < 6; round++)
            {
                var magnitude = round switch { 0 => 256, 1 => 1 << 16, 2 => 1 << 24, _ => int.MaxValue };
                var integers = new int[3][];
                var singles = new float[3][];
                for (var component = 0; component < 3; component++)
                {
                    integers[component] = new int[length];
                    singles[component] = new float[length];
                    for (var i = 0; i < length; i++)
                    {
                        integers[component][i] = random.Next(-magnitude, magnitude);
                        singles[component][i] = NextSingle(random);
                    }
                }

                var expectedIntegers = Clone(integers);
                var actualIntegers = Clone(integers);
                Jpeg2000ColorTransform.InverseReversibleScalar(expectedIntegers[0], expectedIntegers[1], expectedIntegers[2]);
                Jpeg2000ColorTransform.InverseReversible(actualIntegers[0], actualIntegers[1], actualIntegers[2]);
                var expectedSingles = Clone(singles);
                var actualSingles = Clone(singles);
                Jpeg2000ColorTransform.InverseIrreversibleScalar(expectedSingles[0], expectedSingles[1], expectedSingles[2]);
                Jpeg2000ColorTransform.InverseIrreversible(actualSingles[0], actualSingles[1], actualSingles[2]);
                for (var component = 0; component < 3; component++)
                {
                    Assert.Equal(expectedIntegers[component], actualIntegers[component]);
                    Assert.Equal(ToBits(expectedSingles[component]), ToBits(actualSingles[component]));

                    var expectedForward = new int[length];
                    var actualForward = new int[length];
                    Jpeg2000ColorTransform.ForwardReversibleScalar(integers[0], integers[1], integers[2], component, expectedForward);
                    Jpeg2000ColorTransform.ForwardReversible(integers[0], integers[1], integers[2], component, actualForward);
                    Assert.Equal(expectedForward, actualForward);

                    var expectedForwardSingles = new float[length];
                    var actualForwardSingles = new float[length];
                    Jpeg2000ColorTransform.ForwardIrreversibleScalar(integers[0], integers[1], integers[2], component, expectedForwardSingles);
                    Jpeg2000ColorTransform.ForwardIrreversible(integers[0], integers[1], integers[2], component, actualForwardSingles);
                    Assert.Equal(ToBits(expectedForwardSingles), ToBits(actualForwardSingles));
                }

                foreach (var precision in (int[])[1, 2, 8, 12, 16])
                {
                    var expectedShift = (int[])integers[0].Clone();
                    var actualShift = (int[])integers[0].Clone();
                    Jpeg2000ColorTransform.ShiftLevelScalar(expectedShift, precision);
                    Jpeg2000ColorTransform.ShiftLevel(actualShift, precision);
                    Assert.Equal(expectedShift, actualShift);

                    // Values around the two ends of the range, ties, both zeros, and what finite coefficients never
                    // produce: infinities and values that are not numbers
                    var values = (float[])singles[0].Clone();
                    var half = 1 << (precision - 1);
                    for (var i = 0; i < length; i++)
                    {
                        values[i] = random.Next(8) switch
                        {
                            0 => random.Next(-half - 2, half + 2) + 0.5f,
                            1 => random.Next(-half - 2, half + 2),
                            2 => (float)((random.NextDouble() * 2.2 * half) - (1.1 * half)),
                            3 => ((float[])[0f, -0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, float.MinValue, float.Epsilon])[random.Next(8)],
                            _ => values[i],
                        };
                    }

                    var expectedRounded = new int[length];
                    var actualRounded = new int[length];
                    Jpeg2000ColorTransform.ShiftLevelScalar(values, expectedRounded, precision);
                    Jpeg2000ColorTransform.ShiftLevel(values, actualRounded, precision);
                    Assert.Equal(expectedRounded, actualRounded);

                    // The decoder writes the samples over the coefficients
                    var shared = (float[])values.Clone();
                    Jpeg2000ColorTransform.ShiftLevel(shared, unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<float, int>(shared.AsSpan())), precision);
                    Assert.Equal(expectedRounded, Array.ConvertAll(shared, BitConverter.SingleToInt32Bits));
                }
            }
        }

        static T[][] Clone<T>(T[][] source) => Array.ConvertAll(source, values => (T[])values.Clone());
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Jpeg2000CodeBlockStoresMatchTheScalarReferenceBitForBit()
    {
        // Coefficients of the block decoders (the sign in the most significant bit), small and large, both signs with a
        // zero magnitude, every shift of the region of interest and step sizes from tiny to huge
        var random = new Random(15444);
        int[] lengths = [0, 1, 3, 4, 5, 7, 8, 9, 16, 17, 64, 1024];
        int[] specials = [0, 1, 2, 3, int.MaxValue, int.MinValue, int.MinValue | 1, int.MinValue | 3, 1 << 30, int.MinValue | (1 << 30)];
        float[] steps = [0.5f, 1f, 0.0078125f, 3.3e-5f, 1.7e20f, 2.5e38f, float.Epsilon];
        foreach (var length in lengths)
        {
            foreach (var regionShift in (int[])[0, 1, 2, 5, 15, 29, 30, 31, 37])
            {
                var source = new int[length];
                for (var i = 0; i < length; i++)
                {
                    source[i] = random.Next(4) switch
                    {
                        0 => specials[random.Next(specials.Length)],
                        1 => random.Next(1 << 12) | (random.Next(2) << 31),
                        _ => random.Next(int.MinValue, int.MaxValue),
                    };
                }

                var expected = new int[length];
                var actual = new int[length];
                Jpeg2000TileDecoder.StoreReversibleScalar(source, expected, regionShift);
                Jpeg2000TileDecoder.StoreReversible(source, actual, regionShift);
                Assert.Equal(expected, actual);

                foreach (var step in steps)
                {
                    var expectedSingles = new float[length];
                    var actualSingles = new float[length];
                    Jpeg2000TileDecoder.StoreIrreversibleScalar(source, expectedSingles, regionShift, step);
                    Jpeg2000TileDecoder.StoreIrreversible(source, actualSingles, regionShift, step);
                    Assert.Equal(ToBits(expectedSingles), ToBits(actualSingles));
                }
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Jpeg2000EncoderQuantizationMatchesTheScalarReferenceBitForBit()
    {
        // Coefficients of every magnitude and both zeros, quotients that do not fit in 31 bits, infinities and values that
        // are not numbers; step sizes from tiny to large
        var random = new Random(15446);
        float[] specials = [0f, -0f, 1f, -1f, 0.5f, -0.5f, 2147483520f, -2147483520f, 2147483648f, -3e9f, float.MaxValue, float.MinValue, float.PositiveInfinity, float.NegativeInfinity, float.NaN, float.Epsilon, -float.Epsilon];
        float[] steps = [1f, 0.5f, 0.013f, 1.7f, 1e-30f, 1e30f, 3.3e-3f];
        foreach (var length in (int[])[0, 1, 3, 4, 5, 7, 8, 9, 16, 17, 64, 1024])
        {
            var integers = new int[length];
            var singles = new float[length];
            for (var i = 0; i < length; i++)
            {
                integers[i] = random.Next(3) == 0 ? ((int[])[0, 1, -1, int.MinValue, int.MaxValue, int.MinValue + 1])[random.Next(6)] : random.Next(int.MinValue, int.MaxValue);
                singles[i] = random.Next(4) == 0 ? specials[random.Next(specials.Length)] : NextSingle(random);
            }

            var expected = new int[length];
            var actual = new int[length];
            Jpeg2000EncoderCodec.ToSignMagnitudeScalar(integers, expected);
            Jpeg2000EncoderCodec.ToSignMagnitude(integers, actual);
            Assert.Equal(expected, actual);

            foreach (var step in steps)
            {
                Jpeg2000EncoderCodec.QuantizeScalar(singles, expected, step);
                Jpeg2000EncoderCodec.Quantize(singles, actual, step);
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Jpeg2000RowPackingMatchesTheScalarReferenceBitForBit()
    {
        // Samples of 8 bits and samples whose high bits the packing drops; rows of the length of a vector and around it,
        // and rows whose last vector would write past the end of an RGB row
        var random = new Random(15445);
        foreach (var width in (int[])[0, 1, 3, 4, 5, 7, 8, 9, 15, 16, 17, 33, 100, 1021])
        {
            var channels = new int[4][];
            for (var k = 0; k < 4; k++)
            {
                channels[k] = new int[width];
                for (var x = 0; x < width; x++)
                {
                    channels[k][x] = random.Next(3) == 0 ? random.Next(int.MinValue, int.MaxValue) : random.Next(256);
                }
            }

            var expected = new byte[4 * width];
            var actual = new byte[4 * width];
            Jpeg2000RowWriter.PackGray8Scalar(channels[0], expected);
            Jpeg2000RowWriter.PackGray8(channels[0], actual);
            Assert.Equal(expected, actual);

            Array.Fill(expected, (byte)0x5A);
            Array.Fill(actual, (byte)0x5A);
            Jpeg2000RowWriter.PackRgb24Scalar(channels[0], channels[1], channels[2], expected.AsSpan(0, 3 * width));
            Jpeg2000RowWriter.PackRgb24(channels[0], channels[1], channels[2], actual.AsSpan(0, 3 * width));
            Assert.Equal(expected, actual);

            Jpeg2000RowWriter.PackRgba32Scalar(channels[0], channels[1], channels[2], channels[3], expected);
            Jpeg2000RowWriter.PackRgba32(channels[0], channels[1], channels[2], channels[3], actual);
            Assert.Equal(expected, actual);

            Jpeg2000RowWriter.PackRgba32Scalar(channels[0], channels[1], channels[2], default, expected);
            Jpeg2000RowWriter.PackRgba32(channels[0], channels[1], channels[2], default, actual);
            Assert.Equal(expected, actual);
        }
    }

    private static int[] ToBits(float[] values) => Array.ConvertAll(values, BitConverter.SingleToInt32Bits);

    /// <summary>A finite single-precision number: an integer, a fraction, a large or a tiny magnitude, or a zero of either sign.</summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static float NextSingle(Random random) => random.Next(8) switch
    {
        0 => random.Next(-32768, 32768),
        1 => random.Next(-4096, 4096) + 0.5f,
        2 => (float)((random.NextDouble() - 0.5) * 1e20),
        3 => (float)((random.NextDouble() - 0.5) * 1e-30),
        4 => random.Next(2) == 0 ? 0f : -0f,
        _ => (float)((random.NextDouble() - 0.5) * 65536),
    };
}
