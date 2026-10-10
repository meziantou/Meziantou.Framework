using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Pixel layouts and the scalar reference row converters. Expected values come from
/// hand-written sample arrays or from <see cref="ReferenceModel"/>, an independent decimal-arithmetic transcription of
/// the specification; they are never produced by the converter under test.
/// </summary>
public sealed class PixelConversionTests
{
    private static readonly Rgba64 Background = new(0x1234, 0xABCD, 0x0F0F);

    public static TheoryData<PixelFormat, PixelFormat> AllPairs
    {
        get
        {
            var data = new TheoryData<PixelFormat, PixelFormat>();
            foreach (var source in PixelFormats.All)
            {
                foreach (var destination in PixelFormats.All)
                {
                    data.Add(source, destination);
                }
            }

            return data;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Layouts
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void StructsHaveExactSizesAndFieldOffsets()
    {
        AssertLayout<Rgba32>(4, ("R", 0), ("G", 1), ("B", 2), ("A", 3));
        AssertLayout<Bgra32>(4, ("B", 0), ("G", 1), ("R", 2), ("A", 3));
        AssertLayout<Rgb24>(3, ("R", 0), ("G", 1), ("B", 2));
        AssertLayout<Rgba64>(8, ("R", 0), ("G", 2), ("B", 4), ("A", 6));
        AssertLayout<Gray8>(1, ("Value", 0));
        AssertLayout<Gray16>(2, ("Value", 0));
    }

    [Fact]
    public void StructsStoreComponentsInDocumentedByteOrder()
    {
        Assert.Equal([1, 2, 3, 4], Bytes(new Rgba32(1, 2, 3, 4)));
        Assert.Equal([3, 2, 1, 4], Bytes(new Bgra32(r: 1, g: 2, b: 3, a: 4)));
        Assert.Equal([1, 2, 3], Bytes(new Rgb24(1, 2, 3)));
        Assert.Equal([7], Bytes(new Gray8(7)));

        // 16-bit components are native-endian ushort values
        Assert.Equal(Native16(0x0102, 0x0304, 0x0506, 0x0708), Bytes(new Rgba64(0x0102, 0x0304, 0x0506, 0x0708)));
        Assert.Equal(Native16(0xA1B2), Bytes(new Gray16(0xA1B2)));
    }

    [Fact]
    public void ConstructorsTakeLogicalOrderAndDefaultToOpaque()
    {
        var bgra = new Bgra32(r: 10, g: 20, b: 30);
        Assert.Equal((10, 20, 30, 255), (bgra.R, bgra.G, bgra.B, bgra.A));
        Assert.Equal(255, new Rgba32(1, 2, 3).A);
        Assert.Equal(65535, new Rgba64(1, 2, 3).A);
    }

    [Fact]
    public void StructsAreMutable()
    {
        var pixel = new Rgba64(1, 2, 3, 4) { B = 0xFFFE };
        Assert.Equal(0xFFFE, pixel.B);
        foreach (var format in PixelFormats.All)
        {
            var type = PixelFormats.GetPixelType(format);
            Assert.All(type.GetFields(BindingFlags.Public | BindingFlags.Instance), field => Assert.False(field.IsInitOnly, $"{type.Name}.{field.Name} is readonly"));
        }
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32, 4, 8, 4, true, false)]
    [InlineData(PixelFormat.Bgra32, 4, 8, 4, true, false)]
    [InlineData(PixelFormat.Rgb24, 3, 8, 3, false, false)]
    [InlineData(PixelFormat.Rgba64, 8, 16, 4, true, false)]
    [InlineData(PixelFormat.Gray8, 1, 8, 1, false, true)]
    [InlineData(PixelFormat.Gray16, 2, 16, 1, false, true)]
    public void PixelFormatQueries(PixelFormat format, int bytesPerPixel, int bitsPerComponent, int components, bool alpha, bool gray)
    {
        Assert.Equal(bytesPerPixel, PixelFormats.GetBytesPerPixel(format));
        Assert.Equal(bitsPerComponent, PixelFormats.GetBitsPerComponent(format));
        Assert.Equal(components, PixelFormats.GetComponentCount(format));
        Assert.Equal(alpha, PixelFormats.HasAlpha(format));
        Assert.Equal(gray, PixelFormats.IsGrayscale(format));
        Assert.Equal(bytesPerPixel, Marshal.SizeOf(PixelFormats.GetPixelType(format)));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Precision
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void EightBitValuesExpandExactlyAndRoundTripForEveryValue()
    {
        var gray = new Gray8[256];
        var rgba = new Rgba32[256];
        for (var v = 0; v < 256; v++)
        {
            gray[v] = new Gray8((byte)v);
            rgba[v] = new Rgba32((byte)v, (byte)(255 - v), (byte)(v ^ 0x5A), (byte)v);
        }

        var gray16 = new Gray16[256];
        PixelConverter.ConvertRow<Gray8, Gray16>(gray, gray16);
        var rgba64 = new Rgba64[256];
        PixelConverter.ConvertRow<Rgba32, Rgba64>(rgba, rgba64);
        for (var v = 0; v < 256; v++)
        {
            Assert.Equal((v << 8) | v, gray16[v].Value);
            Assert.Equal(new Rgba64((ushort)(v * 257), (ushort)((255 - v) * 257), (ushort)((v ^ 0x5A) * 257), (ushort)(v * 257)), rgba64[v]);
        }

        var gray8 = new Gray8[256];
        PixelConverter.ConvertRow<Gray16, Gray8>(gray16, gray8);
        Assert.Equal(gray, gray8);

        var back = new Bgra32[256];
        PixelConverter.ConvertRow<Rgba64, Bgra32>(rgba64, back);
        for (var v = 0; v < 256; v++)
        {
            Assert.Equal((rgba[v].R, rgba[v].G, rgba[v].B, rgba[v].A), (back[v].R, back[v].G, back[v].B, back[v].A));
        }
    }

    [Fact]
    public void SixteenBitReductionIsNearestForEveryValue()
    {
        var gray16 = new Gray16[65536];
        var rgba64 = new Rgba64[65536];
        for (var v = 0; v < 65536; v++)
        {
            gray16[v] = new Gray16((ushort)v);
            rgba64[v] = new Rgba64((ushort)v, (ushort)(65535 - v), (ushort)(v ^ 0x00FF), (ushort)v);
        }

        var gray8 = new Gray8[65536];
        PixelConverter.ConvertRow<Gray16, Gray8>(gray16, gray8);
        var rgba32 = new Rgba32[65536];
        PixelConverter.ConvertRow<Rgba64, Rgba32>(rgba64, rgba32);
        for (var v = 0; v < 65536; v++)
        {
            Assert.Equal(ReferenceModel.To8(v), gray8[v].Value);
            Assert.Equal(new Rgba32(ReferenceModel.To8(v), ReferenceModel.To8(65535 - v), ReferenceModel.To8(v ^ 0x00FF), ReferenceModel.To8(v)), rgba32[v]);
        }

        // Boundaries of the nearest rounding: v / 257 = k + 0.5 never happens; 128 -> 0 and 129 -> 1
        Assert.Equal(0, gray8[128].Value);
        Assert.Equal(1, gray8[129].Value);
        Assert.Equal(255, gray8[65535].Value);
    }

    [Fact]
    public void SixteenBitConversionsPreserveEveryValue()
    {
        var gray16 = new Gray16[65536];
        for (var v = 0; v < 65536; v++)
        {
            gray16[v] = new Gray16((ushort)v);
        }

        var rgba64 = new Rgba64[65536];
        PixelConverter.ConvertRow<Gray16, Rgba64>(gray16, rgba64);
        for (var v = 0; v < 65536; v++)
        {
            Assert.Equal(new Rgba64((ushort)v, (ushort)v, (ushort)v, 65535), rgba64[v]);
        }

        // Luma of equal components is the component itself: Gray16 -> Rgba64 -> Gray16 is lossless (no 8-bit bottleneck)
        var back = new Gray16[65536];
        PixelConverter.ConvertRow<Rgba64, Gray16>(rgba64, back);
        Assert.Equal(gray16, back);

        Rgba64[] lowBits = [new(0x0001, 0x0100, 0x0101, 0x00FF), new(0xFFFE, 0x7FFF, 0x8001, 0x0002)];
        var copy = new Rgba64[2];
        PixelConverter.ConvertRow<Rgba64, Rgba64>(lowBits, copy);
        Assert.Equal(lowBits, copy);
    }

    [Fact]
    public void GrayToColorReplicatesAndAddsOpaqueAlpha()
    {
        Gray8[] gray = [new(0), new(77), new(255)];
        var rgba = new Rgba32[3];
        var bgra = new Bgra32[3];
        var rgb = new Rgb24[3];
        PixelConverter.ConvertRow<Gray8, Rgba32>(gray, rgba);
        PixelConverter.ConvertRow<Gray8, Bgra32>(gray, bgra);
        PixelConverter.ConvertRow<Gray8, Rgb24>(gray, rgb);
        Assert.Equal([new Rgba32(0, 0, 0, 255), new Rgba32(77, 77, 77, 255), new Rgba32(255, 255, 255, 255)], rgba);
        Assert.Equal([new Bgra32(0, 0, 0, 255), new Bgra32(77, 77, 77, 255), new Bgra32(255, 255, 255, 255)], bgra);
        Assert.Equal([new Rgb24(0, 0, 0), new Rgb24(77, 77, 77), new Rgb24(255, 255, 255)], rgb);

        Gray16[] gray16 = [new(0x1234)];
        var reduced = new Rgb24[1];
        PixelConverter.ConvertRow<Gray16, Rgb24>(gray16, reduced);
        Assert.Equal(new Rgb24(0x12, 0x12, 0x12), reduced[0]); // 0x1234 / 257 = 18.13 -> 18
    }

    [Fact]
    public void AddingAlphaSetsFullOpacity()
    {
        Rgb24[] rgb = [new(1, 2, 3)];
        var rgba = new Rgba32[1];
        var rgba64 = new Rgba64[1];
        PixelConverter.ConvertRow<Rgb24, Rgba32>(rgb, rgba);
        PixelConverter.ConvertRow<Rgb24, Rgba64>(rgb, rgba64);
        Assert.Equal(new Rgba32(1, 2, 3, 255), rgba[0]);
        Assert.Equal(new Rgba64(257, 514, 771, 65535), rgba64[0]);
    }

    [Fact]
    public void ChannelOrderIsSwappedBetweenRgbaAndBgra()
    {
        Rgba32[] rgba = [new(1, 2, 3, 4), new(250, 0, 128, 0)];
        var bgra = new Bgra32[2];
        PixelConverter.ConvertRow<Rgba32, Bgra32>(rgba, bgra);
        Assert.Equal([3, 2, 1, 4, 128, 0, 250, 0], unsafe(MemoryMarshal.AsBytes(bgra.AsSpan())).ToArray());

        var back = new Rgba32[2];
        PixelConverter.ConvertRow<Bgra32, Rgba32>(bgra, back);
        Assert.Equal(rgba, back);
    }

    [Fact]
    public void InPlaceConversionBetweenSameSizedFormats()
    {
        var row = new Rgba32[] { new(1, 2, 3, 4), new(5, 6, 7, 8) };
        var asBgra = unsafe(MemoryMarshal.Cast<Rgba32, Bgra32>(row.AsSpan()));
        PixelConverter.ConvertRow<Rgba32, Bgra32>(row, asBgra);
        Assert.Equal([3, 2, 1, 4, 7, 6, 5, 8], unsafe(MemoryMarshal.AsBytes(row.AsSpan())).ToArray());
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Grayscale (Rec. 709 luma on encoded values)
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void LumaMatchesHandComputedSamples()
    {
        // (44, 3, 0): 0.2126 * 44 + 0.7152 * 3 = 9.3544 + 2.1456 = 11.5 exactly: ties round upward to 12
        // (255, 0, 0): 54.213 -> 54; (0, 255, 0): 182.376 -> 182; (0, 0, 255): 18.411 -> 18; white stays 255 (clamped)
        Rgb24[] rgb = [new(44, 3, 0), new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), new(255, 255, 255), new(0, 0, 0), new(10, 20, 30)];
        var gray = new Gray8[rgb.Length];
        PixelConverter.ConvertRow<Rgb24, Gray8>(rgb, gray);
        Assert.Equal([12, 54, 182, 18, 255, 0, 19], gray.Select(p => (int)p.Value)); // 2.126 + 14.304 + 2.166 = 18.596 -> 19

        // Same formula at 16-bit precision (no 8-bit intermediate)
        Rgba64[] rgba64 = [new(44, 3, 0), new(65535, 0, 0), new(0x0101, 0x0102, 0x0103), new(65535, 65535, 65535)];
        var gray16 = new Gray16[rgba64.Length];
        PixelConverter.ConvertRow<Rgba64, Gray16>(rgba64, gray16);
        Assert.Equal([12, 13933, 258, 65535], gray16.Select(p => (int)p.Value)); // 13932.741 -> 13933; 54.6382 + 184.5216 + 18.6998 = 257.8596 -> 258
    }

    [Fact]
    public void EightBitLumaMatchesReferenceOnDenseGrid()
    {
        var row = new List<Rgb24>();
        for (var r = 0; r < 256; r += 5)
        {
            for (var g = 0; g < 256; g += 3)
            {
                for (var b = 0; b < 256; b += 7)
                {
                    row.Add(new Rgb24((byte)r, (byte)g, (byte)b));
                }
            }
        }

        var source = row.ToArray();
        var gray = new Gray8[source.Length];
        PixelConverter.ConvertRow<Rgb24, Gray8>(source, gray);
        for (var i = 0; i < source.Length; i++)
        {
            Assert.Equal(ReferenceModel.Luma(source[i].R, source[i].G, source[i].B, 255), gray[i].Value);
        }
    }

    [Fact]
    public void SixteenBitLumaMatchesReferenceOnRandomSamples()
    {
        var random = new DeterministicRandom(709);
        var source = new Rgba64[20000];
        for (var i = 0; i < source.Length; i++)
        {
            source[i] = new Rgba64((ushort)random.Next(65536), (ushort)random.Next(65536), (ushort)random.Next(65536));
        }

        var gray = new Gray16[source.Length];
        PixelConverter.ConvertRow<Rgba64, Gray16>(source, gray);
        var gray8 = new Gray8[source.Length];
        PixelConverter.ConvertRow<Rgba64, Gray8>(source, gray8);
        for (var i = 0; i < source.Length; i++)
        {
            var expected = ReferenceModel.Luma(source[i].R, source[i].G, source[i].B, 65535);
            Assert.Equal(expected, gray[i].Value);
            Assert.Equal(ReferenceModel.To8(expected), gray8[i].Value);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Alpha
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(PixelFormat.Rgba32, PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Bgra32, PixelFormat.Gray8)]
    [InlineData(PixelFormat.Rgba64, PixelFormat.Gray16)]
    [InlineData(PixelFormat.Rgba64, PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Rgba32, PixelFormat.Gray16)]
    public void AlphaRemovalRejectsNonOpaquePixelsWithoutWriting(PixelFormat source, PixelFormat destination)
    {
        var max = PixelFormats.GetBitsPerComponent(source) == 16 ? 65535 : 255;
        foreach (var alpha in new[] { 0, max - 1, 1 })
        {
            var bytes = ReferenceModel.CreateRow(source, [(10, 20, 30, max), (200, 100, 50, alpha), (1, 2, 3, max)]);
            var output = new byte[3 * PixelFormats.GetBytesPerPixel(destination)];
            output.AsSpan().Fill(0xCD);

            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => PixelConverter.ConvertRow(source, bytes, destination, output));
            Assert.Equal("Alpha removal", exception.Feature);
            Assert.Contains("index 1", exception.Message, StringComparison.Ordinal);
            Assert.All(output, value => Assert.Equal(0xCD, value));
        }
    }

    [Fact]
    public void OpaquePixelsNeverRequireBackground()
    {
        Rgba32[] rgba = [new(1, 2, 3, 255), new(4, 5, 6, 255)];
        var rgb = new Rgb24[2];
        PixelConverter.ConvertRow<Rgba32, Rgb24>(rgba, rgb);
        Assert.Equal([new Rgb24(1, 2, 3), new Rgb24(4, 5, 6)], rgb);

        Rgba64[] rgba64 = [new(0x0102, 0x0304, 0x0506, 65535)];
        var gray16 = new Gray16[1];
        PixelConverter.ConvertRow<Rgba64, Gray16>(rgba64, gray16);
        Assert.Equal(ReferenceModel.Luma(0x0102, 0x0304, 0x0506, 65535), gray16[0].Value);

        // Opaque pixels are unchanged by flattening
        PixelConverter.ConvertRow<Rgba32, Rgb24>(rgba, rgb, Background);
        Assert.Equal([new Rgb24(1, 2, 3), new Rgb24(4, 5, 6)], rgb);
    }

    [Fact]
    public void FullyTransparentPixelsFlattenToTheBackground()
    {
        // Hidden colors of transparent pixels are irrelevant once flattened
        Rgba32[] rgba = [new(200, 10, 30, 0), new(0, 0, 0, 0)];
        var rgb = new Rgb24[2];
        PixelConverter.ConvertRow<Rgba32, Rgb24>(rgba, rgb, new Rgba64(0x1234, 0xABCD, 0x0F0F));
        var expected = new Rgb24(0x12, 0xAB, 0x0F); // 0x1234 / 257 = 18.1, 0xABCD / 257 = 171.0, 0x0F0F / 257 = 15
        Assert.Equal([expected, expected], rgb);

        Rgba64[] rgba64 = [new(0xFFFF, 0, 0x8000, 0)];
        var rgb64ToGray = new Gray16[1];
        PixelConverter.ConvertRow<Rgba64, Gray16>(rgba64, rgb64ToGray, Background);
        Assert.Equal(ReferenceModel.Luma(0x1234, 0xABCD, 0x0F0F, 65535), rgb64ToGray[0].Value);
    }

    [Fact]
    public void AlphaRampFlattensWithNearestRounding()
    {
        var source = new Rgba32[256];
        for (var a = 0; a < 256; a++)
        {
            source[a] = new Rgba32(255, 0, 100, (byte)a);
        }

        var white = new Rgba64(65535, 65535, 65535);
        var rgb = new Rgb24[256];
        PixelConverter.ConvertRow<Rgba32, Rgb24>(source, rgb, white);
        for (var a = 0; a < 256; a++)
        {
            Assert.Equal(255, rgb[a].R);
            Assert.Equal(ReferenceModel.Flatten(0, 255, a, 255), rgb[a].G);
            Assert.Equal(ReferenceModel.Flatten(100, 255, a, 255), rgb[a].B);
        }

        // Hand-computed: a = 128 over white: G = (0 * 128 + 255 * 127) / 255 = 127; B = (100 * 128 + 255 * 127) / 255 = 177.196 -> 177
        Assert.Equal(new Rgb24(255, 127, 177), rgb[128]);
        Assert.Equal(new Rgb24(255, 255, 255), rgb[0]);
        Assert.Equal(new Rgb24(255, 0, 100), rgb[255]);
    }

    [Fact]
    public void SixteenBitFlatteningIsPerformedAtSourcePrecision()
    {
        var bg = new Rgba64(0x0001, 0x8000, 0xFFFE);
        Rgba64[] source = [new(0xFFFF, 0x0000, 0x0001, 0x7FFF), new(0x1234, 0x5678, 0x9ABC, 0x0001)];
        var rgba = new Rgba64[2];
        var gray = new Gray16[2];
        var rgb = new Rgb24[2];
        PixelConverter.ConvertRow<Rgba64, Rgba64>(source, rgba, bg); // destination has alpha: background ignored
        PixelConverter.ConvertRow<Rgba64, Gray16>(source, gray, bg);
        PixelConverter.ConvertRow<Rgba64, Rgb24>(source, rgb, bg);
        Assert.Equal(source, rgba);
        for (var i = 0; i < source.Length; i++)
        {
            var r = ReferenceModel.Flatten(source[i].R, bg.R, source[i].A, 65535);
            var g = ReferenceModel.Flatten(source[i].G, bg.G, source[i].A, 65535);
            var b = ReferenceModel.Flatten(source[i].B, bg.B, source[i].A, 65535);
            Assert.Equal(ReferenceModel.Luma(r, g, b, 65535), gray[i].Value);
            Assert.Equal(new Rgb24(ReferenceModel.To8(r), ReferenceModel.To8(g), ReferenceModel.To8(b)), rgb[i]);
        }
    }

    [Fact]
    public void StraightAlphaAndHiddenColorsArePreservedBetweenAlphaFormats()
    {
        Rgba32[] source = [new(200, 10, 30, 0), new(1, 2, 3, 1), new(255, 128, 0, 254)];
        var bgra = new Bgra32[3];
        var rgba64 = new Rgba64[3];
        PixelConverter.ConvertRow<Rgba32, Bgra32>(source, bgra);
        PixelConverter.ConvertRow<Rgba32, Rgba64>(source, rgba64);
        Assert.Equal([new Bgra32(200, 10, 30, 0), new Bgra32(1, 2, 3, 1), new Bgra32(255, 128, 0, 254)], bgra);
        Assert.Equal([new Rgba64(200 * 257, 10 * 257, 30 * 257, 0), new Rgba64(257, 514, 771, 257), new Rgba64(65535, 128 * 257, 0, 254 * 257)], rgba64);
    }

    [Fact]
    public void BackgroundMustBeOpaque()
    {
        var output = new Rgb24[1];
        Assert.Throws<ArgumentException>(() => PixelConverter.ConvertRow<Rgba32, Rgb24>([new Rgba32(1, 2, 3, 4)], output, new Rgba64(1, 2, 3, 65534)));
        Assert.Throws<ArgumentException>(() => new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0, 0) });
    }

    // ---------------------------------------------------------------------------------------------------------------
    // All pairs, against the independent reference model
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void EveryPairMatchesReferenceModel(PixelFormat source, PixelFormat destination)
    {
        var pixels = ReferenceModel.CreateSamplePixels(source, count: 512, seed: (int)source * 10 + (int)destination);
        var bytes = ReferenceModel.CreateRow(source, pixels);
        var output = new byte[pixels.Count * PixelFormats.GetBytesPerPixel(destination)];

        // With an explicit background (alpha removal flattens)
        Assert.Equal(pixels.Count, PixelConverter.ConvertRow(source, bytes, destination, output, Background));
        Assert.Equal(ReferenceModel.CreateRow(destination, [.. pixels.Select(p => ReferenceModel.Convert(source, p, destination, Background))]), output);

        // Without background: non-opaque pixels are only accepted when the destination keeps alpha
        var removesAlpha = PixelFormats.HasAlpha(source) && !PixelFormats.HasAlpha(destination);
        if (removesAlpha)
        {
            Assert.Throws<UnsupportedImageFeatureException>(() => PixelConverter.ConvertRow(source, bytes, destination, output));
            var max = ReferenceModel.Max(source);
            pixels = [.. pixels.Select(p => (p.R, p.G, p.B, max))];
            bytes = ReferenceModel.CreateRow(source, pixels);
        }

        Array.Clear(output);
        PixelConverter.ConvertRow(source, bytes, destination, output);
        Assert.Equal(ReferenceModel.CreateRow(destination, [.. pixels.Select(p => ReferenceModel.Convert(source, p, destination, background: null))]), output);
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void TypedUntypedAndScalarReferencePathsAgree(PixelFormat source, PixelFormat destination)
    {
        // Later vectorized kernels must stay bit-identical to ConvertRowScalar; this test pins the contract for every pair
        var bytes = ReferenceModel.CreateRow(source, ReferenceModel.CreateSamplePixels(source, count: 1031, seed: 42));
        var count = bytes.Length / PixelFormats.GetBytesPerPixel(source);
        var untyped = new byte[count * PixelFormats.GetBytesPerPixel(destination)];
        PixelConverter.ConvertRow(source, bytes, destination, untyped, Background);

        var method = typeof(PixelConversionTests).GetMethod(nameof(ConvertTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(PixelFormats.GetPixelType(source), PixelFormats.GetPixelType(destination));
        var (typed, scalar) = ((byte[], byte[]))method.Invoke(null, [bytes, count])!;
        Assert.Equal(untyped, typed);
        Assert.Equal(untyped, scalar);
    }

    [Fact]
    public void DestinationMayBeLongerButNotShorter()
    {
        var output = new Rgba32[3];
        output[2] = new Rgba32(9, 9, 9, 9);
        PixelConverter.ConvertRow<Gray8, Rgba32>([new Gray8(1), new Gray8(2)], output);
        Assert.Equal(new Rgba32(9, 9, 9, 9), output[2]);
        Assert.Throws<ArgumentException>(() => PixelConverter.ConvertRow<Gray8, Rgba32>([new Gray8(1), new Gray8(2)], new Rgba32[1]));
        Assert.Throws<ArgumentException>(() => PixelConverter.ConvertRow(PixelFormat.Rgba32, new byte[5], PixelFormat.Gray8, new byte[2]));
        Assert.Throws<ArgumentException>(() => PixelConverter.ConvertRow(PixelFormat.Gray8, new byte[5], PixelFormat.Rgba32, new byte[19]));
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelConverter.ConvertRow(PixelFormat.Unknown, new byte[4], PixelFormat.Rgba32, new byte[4]));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Unsupported pixel types
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void UnsupportedPixelTypesFailBeforeSideEffects()
    {
        var destination = new Rgba32[] { new(9, 9, 9, 9) };
        Assert.Throws<NotSupportedException>(() => PixelConverter.ConvertRow<int, Rgba32>([1], destination));
        Assert.Equal(new Rgba32(9, 9, 9, 9), destination[0]);

        var ints = new[] { 123 };
        Assert.Throws<NotSupportedException>(() => PixelConverter.ConvertRow<Rgba32, int>([new Rgba32(1, 2, 3)], ints));
        Assert.Equal(123, ints[0]);

        // Same size as a supported struct is not enough
        var lookalike = new uint[] { 7 };
        Assert.Throws<NotSupportedException>(() => PixelConverter.ConvertRow<uint, Rgba32>(lookalike, destination));
        Assert.Throws<NotSupportedException>(() => PixelConverter.ConvertRowScalar<Rgba32, uint>(destination, lookalike));
        Assert.Equal(7u, lookalike[0]);

        Assert.Throws<NotSupportedException>(() => PixelConverter.IsOpaque<long>([1]));
        Assert.Throws<NotSupportedException>(() => PixelConversionPlan.Create<Guid, Rgba32>());
        Assert.Throws<NotSupportedException>(() => PixelConversionPlan.Create<Rgba32, decimal>());
        Assert.Throws<NotSupportedException>(() => SampleEndianness.ReadPixels<Rgba32>(new byte[4], new Rgba32[1], bigEndian: true));
        Assert.Throws<NotSupportedException>(() => SampleEndianness.ReadPixels<ulong>(new byte[8], new ulong[1], bigEndian: true));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Conversion plans: options and color profiles
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(PixelFormat.Rgba32, PixelFormat.Gray8)]
    [InlineData(PixelFormat.Rgb24, PixelFormat.Gray16)]
    [InlineData(PixelFormat.Rgba64, PixelFormat.Gray8)]
    public void RgbProfileCannotLabelGrayscalePixels(PixelFormat source, PixelFormat destination)
    {
        var profile = CreateProfile("RGB ");
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => PixelConversionPlan.Create(source, destination, options: null, profile));
        Assert.Equal("Incompatible color profile", exception.Feature);
        Assert.Contains("DiscardIncompatibleColorProfile", exception.Message, StringComparison.Ordinal);

        var plan = PixelConversionPlan.Create(source, destination, new PixelConversionOptions { DiscardIncompatibleColorProfile = true }, profile);
        Assert.Null(plan.ColorProfile);
        Assert.True(plan.ColorProfileDiscarded);
        Assert.True(plan.ConvertsToGrayscale);
    }

    [Theory]
    [InlineData(PixelFormat.Gray8, PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Gray16, PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Gray8, PixelFormat.Bgra32)]
    public void GrayProfileCannotLabelColorPixels(PixelFormat source, PixelFormat destination)
    {
        var profile = CreateProfile("GRAY");
        Assert.Throws<UnsupportedImageFeatureException>(() => PixelConversionPlan.Create(source, destination, PixelConversionOptions.Default, profile));
        Assert.Null(PixelConversionPlan.Create(source, destination, new PixelConversionOptions { DiscardIncompatibleColorProfile = true }, profile).ColorProfile);
    }

    [Theory]
    [InlineData("RGB ", PixelFormat.Rgba64, PixelFormat.Rgb24)]
    [InlineData("RGB ", PixelFormat.Rgba32, PixelFormat.Bgra32)]
    [InlineData("RGB ", PixelFormat.Rgb24, PixelFormat.Rgba64)]
    [InlineData("GRAY", PixelFormat.Gray16, PixelFormat.Gray8)]
    [InlineData("GRAY", PixelFormat.Gray8, PixelFormat.Gray8)]
    public void CompatibleProfilesAreRetained(string colorSpace, PixelFormat source, PixelFormat destination)
    {
        var profile = CreateProfile(colorSpace);
        var plan = PixelConversionPlan.Create(source, destination, new PixelConversionOptions { DiscardIncompatibleColorProfile = true }, profile);
        Assert.Same(profile, plan.ColorProfile);
        Assert.False(plan.ColorProfileDiscarded);
        Assert.Same(profile, PixelConversionPlan.Create(source, destination, options: null, profile).ColorProfile);
    }

    [Theory]
    [InlineData("CMYK")]
    [InlineData("Lab ")]
    [InlineData(null)]
    public void ProfilesWithOtherColorSpacesAreCompatibleWithNoPixelFormat(string? colorSpace)
    {
        var profile = colorSpace is null ? new IccProfile(new MetadataBlob([1, 2, 3])) : CreateProfile(colorSpace);
        foreach (var format in PixelFormats.All)
        {
            Assert.False(ColorProfileCompatibility.IsCompatible(profile, format));
            Assert.Throws<UnsupportedImageFeatureException>(() => PixelConversionPlan.Create(format, format, options: null, profile));
        }

        Assert.True(ColorProfileCompatibility.IsCompatible(profile: null, PixelFormat.Gray8));
        Assert.Null(PixelConversionPlan.Create(PixelFormat.Rgba32, PixelFormat.Gray8).ColorProfile);
    }

    [Fact]
    public void PlanAppliesBackgroundOnlyWhenAlphaIsRemoved()
    {
        var options = new PixelConversionOptions { BackgroundColor = new Rgba64(65535, 0, 0) };
        var removing = PixelConversionPlan.Create<Rgba32, Rgb24>(options);
        Assert.True(removing.RemovesAlpha);
        Assert.False(removing.RequiresOpaqueSource);
        Assert.Equal(new Rgba64(65535, 0, 0), removing.Background);

        var keeping = PixelConversionPlan.Create<Rgba32, Rgba64>(options);
        Assert.False(keeping.RemovesAlpha);
        Assert.Null(keeping.Background);
        Assert.True(keeping.ChangesPrecision);
        Assert.False(keeping.ReducesPrecision);

        var output = new Rgb24[1];
        removing.ConvertRow<Rgba32, Rgb24>([new Rgba32(0, 0, 255, 0)], output);
        Assert.Equal(new Rgb24(255, 0, 0), output[0]);

        var strict = PixelConversionPlan.Create<Rgba64, Gray8>(options: null, sourceProfile: null, ImageFormat.Jpeg);
        Assert.True(strict.RequiresOpaqueSource);
        Assert.True(strict.ReducesPrecision);
        Rgba64[] transparent = [new(1, 2, 3, 65535), new(1, 2, 3, 0)];
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => strict.EnsureConvertible<Rgba64>(transparent));
        Assert.Equal(ImageFormat.Jpeg, exception.Format);
        Assert.Throws<UnsupportedImageFeatureException>(() => strict.ConvertRow<Rgba64, Gray8>(transparent, new Gray8[2]));
        Assert.Throws<UnsupportedImageFeatureException>(() => strict.ConvertRow(unsafe(MemoryMarshal.AsBytes(transparent.AsSpan())), new byte[2]));
        strict.EnsureConvertible<Rgba64>([new Rgba64(1, 2, 3)]);

        // The typed entry points of a plan only accept the planned types
        Assert.Throws<InvalidOperationException>(() => strict.ConvertRow<Rgba32, Gray8>([new Rgba32(1, 2, 3)], new Gray8[1]));
        Assert.True(PixelConversionPlan.Create<Gray8, Gray8>().IsIdentity);
    }

    [Fact]
    public void OpacityQueries()
    {
        Assert.True(PixelConverter.IsOpaque<Rgb24>([new Rgb24(1, 2, 3)]));
        Assert.True(PixelConverter.IsOpaque<Rgba32>([new Rgba32(1, 2, 3, 255)]));
        Assert.False(PixelConverter.IsOpaque<Bgra32>([new Bgra32(1, 2, 3, 255), new Bgra32(1, 2, 3, 254)]));
        Assert.Equal(1, PixelConverter.IndexOfNonOpaque<Rgba64>([new Rgba64(1, 2, 3), new Rgba64(1, 2, 3, 65534)]));
        Assert.True(PixelConverter.IsOpaque(PixelFormat.Gray16, new byte[4]));
        Assert.False(PixelConverter.IsOpaque(PixelFormat.Rgba64, new byte[8]));
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelConverter.IsOpaque(PixelFormat.Unknown, new byte[8]));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Endianness
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void SixteenBitSamplesConvertBetweenEncodedAndNativeByteOrder()
    {
        byte[] bigEndian = [0x12, 0x34, 0xAB, 0xCD, 0x00, 0x01, 0xFF, 0xFE];
        var pixels = new Rgba64[1];
        SampleEndianness.ReadPixels<Rgba64>(bigEndian, pixels, bigEndian: true);
        Assert.Equal(new Rgba64(0x1234, 0xABCD, 0x0001, 0xFFFE), pixels[0]);

        var gray = new Gray16[4];
        SampleEndianness.ReadPixels<Gray16>(bigEndian, gray, bigEndian: false);
        Assert.Equal([new Gray16(0x3412), new Gray16(0xCDAB), new Gray16(0x0100), new Gray16(0xFEFF)], gray);

        var output = new byte[8];
        SampleEndianness.WritePixels<Rgba64>(pixels, output, bigEndian: true);
        Assert.Equal(bigEndian, output);
        SampleEndianness.WriteLittleEndian([0x1234, 0x0001], output);
        Assert.Equal([0x34, 0x12, 0x01, 0x00], output[..4]);

        var samples = new ushort[2];
        SampleEndianness.ReadBigEndian([0x80, 0x00, 0x00, 0x80], samples);
        Assert.Equal([0x8000, 0x0080], samples);
        SampleEndianness.ReadLittleEndian([0x80, 0x00, 0x00, 0x80], samples);
        Assert.Equal([0x0080, 0x8000], samples);
        SampleEndianness.WriteBigEndian([0x0102], output);
        Assert.Equal([0x01, 0x02], output[..2]);

        // Raw byte access of the pixel structs is native endian
        Assert.Equal(Native16(0x1234, 0xABCD, 0x0001, 0xFFFE), unsafe(MemoryMarshal.AsBytes(pixels.AsSpan())).ToArray());

        Assert.Throws<ArgumentException>(() => SampleEndianness.ReadBigEndian(new byte[3], new ushort[2]));
        Assert.Throws<ArgumentException>(() => SampleEndianness.ReadBigEndian(new byte[4], new ushort[1]));
        Assert.Throws<ArgumentException>(() => SampleEndianness.WriteBigEndian(new ushort[2], new byte[3]));
    }

    private static (byte[] Typed, byte[] Scalar) ConvertTyped<TSource, TDestination>(byte[] bytes, int count)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        var source = unsafe(MemoryMarshal.Cast<byte, TSource>(bytes));
        var typed = new TDestination[count];
        var scalar = new TDestination[count];
        PixelConverter.ConvertRow<TSource, TDestination>(source, typed, Background);
        PixelConverter.ConvertRowScalar<TSource, TDestination>(source, scalar, Background);
        return (unsafe(MemoryMarshal.AsBytes(typed.AsSpan())).ToArray(), unsafe(MemoryMarshal.AsBytes(scalar.AsSpan())).ToArray());
    }

    private static void AssertLayout<T>(int size, params (string Field, int Offset)[] fields)
        where T : unmanaged
    {
        Assert.Equal(size, Unsafe.SizeOf<T>());
        Assert.Equal(size, Marshal.SizeOf<T>());
        var layout = typeof(T).StructLayoutAttribute!;
        Assert.Equal(LayoutKind.Sequential, layout.Value);
        Assert.Equal(1, layout.Pack);
        var declared = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal(fields.Select(f => f.Field).Order(StringComparer.Ordinal), declared.Select(f => f.Name).Order(StringComparer.Ordinal));
        foreach (var (field, offset) in fields)
        {
            Assert.Equal(offset, Marshal.OffsetOf<T>(field).ToInt32());
        }
    }

    private static byte[] Bytes<T>(T value)
        where T : unmanaged
        => unsafe(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value))).ToArray();

    private static byte[] Native16(params ushort[] values)
    {
        var result = new byte[values.Length * 2];
        for (var i = 0; i < values.Length; i++)
        {
            if (BitConverter.IsLittleEndian)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(i * 2), values[i]);
            }
            else
            {
                BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(i * 2), values[i]);
            }
        }

        return result;
    }

    private static IccProfile CreateProfile(string colorSpace)
    {
        var data = new byte[132];
        BinaryPrimitives.WriteUInt32BigEndian(data, 132);
        System.Text.Encoding.ASCII.GetBytes(colorSpace, data.AsSpan(16));
        System.Text.Encoding.ASCII.GetBytes("acsp", data.AsSpan(36));
        return new IccProfile(new MetadataBlob(data));
    }

    /// <summary>
    /// An independent transcription of the library using exact decimal arithmetic and logical (R, G, B, A)
    /// tuples. It shares no code with the library converters.
    /// </summary>
    internal static class ReferenceModel
    {
        public static int Max(PixelFormat format) => PixelFormats.GetBitsPerComponent(format) == 16 ? 65535 : 255;

        public static int RoundHalfUp(decimal value) => (int)decimal.Floor(value + 0.5m);

        public static byte To8(int value16) => (byte)RoundHalfUp(value16 / 257m);

        public static int Luma(int r, int g, int b, int max) => Math.Min(max, RoundHalfUp((0.2126m * r) + (0.7152m * g) + (0.0722m * b)));

        public static int Flatten(int sample, int background, int alpha, int max) => RoundHalfUp(((decimal)sample * alpha + (decimal)background * (max - alpha)) / max);

        public static (int R, int G, int B, int A) Convert(PixelFormat source, (int R, int G, int B, int A) pixel, PixelFormat destination, Rgba64? background)
        {
            var sourceMax = Max(source);
            var destinationMax = Max(destination);
            var (r, g, b, a) = pixel;
            if (PixelFormats.HasAlpha(source) && !PixelFormats.HasAlpha(destination) && background is { } bg)
            {
                int bgR = bg.R, bgG = bg.G, bgB = bg.B;
                if (sourceMax == 255)
                {
                    (bgR, bgG, bgB) = (To8(bgR), To8(bgG), To8(bgB));
                }

                (r, g, b) = (Flatten(r, bgR, a, sourceMax), Flatten(g, bgG, a, sourceMax), Flatten(b, bgB, a, sourceMax));
                a = sourceMax;
            }

            if (PixelFormats.IsGrayscale(destination))
            {
                var y = PixelFormats.IsGrayscale(source) ? r : Luma(r, g, b, sourceMax);
                (r, g, b) = (y, y, y);
            }

            int Scale(int value) => (sourceMax, destinationMax) switch
            {
                (255, 65535) => value * 257,
                (65535, 255) => To8(value),
                _ => value,
            };

            return (Scale(r), Scale(g), Scale(b), PixelFormats.HasAlpha(destination) ? Scale(a) : destinationMax);
        }

        /// <summary>Random pixels plus edge cases: extremes, transparent hidden colors, alpha ramps, luma ties, low bits.</summary>
        public static List<(int R, int G, int B, int A)> CreateSamplePixels(PixelFormat format, int count, int seed)
        {
            var max = Max(format);
            var gray = PixelFormats.IsGrayscale(format);
            var alpha = PixelFormats.HasAlpha(format);
            var random = new DeterministicRandom(seed);
            List<(int R, int G, int B, int A)> result =
            [
                (0, 0, 0, 0), (max, max, max, max), (max, 0, 0, max), (0, max, 0, 0), (0, 0, max, 1), (44, 3, 0, max - 1), (1, 1, 1, 1),
                (max - 1, 1, max / 2, max / 2), (max / 2, (max / 2) + 1, 128, 128), (0x0101 % (max + 1), 0x00FF, 0x0100 % (max + 1), 0x7FFF % (max + 1)),
            ];
            while (result.Count < count)
            {
                result.Add((random.Next(max + 1), random.Next(max + 1), random.Next(max + 1), random.Next(4) == 0 ? max : random.Next(max + 1)));
            }

            return [.. result.Select(p => (p.R, gray ? p.R : p.G, gray ? p.R : p.B, alpha ? p.A : max))];
        }

        /// <summary>Serializes logical pixels in the documented storage layout of a format (16-bit samples native endian).</summary>
        public static byte[] CreateRow(PixelFormat format, IReadOnlyList<(int R, int G, int B, int A)> pixels)
        {
            var size = PixelFormats.GetBytesPerPixel(format);
            var result = new byte[pixels.Count * size];
            for (var i = 0; i < pixels.Count; i++)
            {
                var (r, g, b, a) = pixels[i];
                var span = result.AsSpan(i * size, size);
                switch (format)
                {
                    case PixelFormat.Rgba32:
                        (span[0], span[1], span[2], span[3]) = ((byte)r, (byte)g, (byte)b, (byte)a);
                        break;
                    case PixelFormat.Bgra32:
                        (span[0], span[1], span[2], span[3]) = ((byte)b, (byte)g, (byte)r, (byte)a);
                        break;
                    case PixelFormat.Rgb24:
                        (span[0], span[1], span[2]) = ((byte)r, (byte)g, (byte)b);
                        break;
                    case PixelFormat.Gray8:
                        span[0] = (byte)r;
                        break;
                    case PixelFormat.Rgba64:
                        Native16(checked((ushort)r), checked((ushort)g), checked((ushort)b), checked((ushort)a)).CopyTo(span);
                        break;
                    case PixelFormat.Gray16:
                        Native16(checked((ushort)r)).CopyTo(span);
                        break;
                }
            }

            return result;
        }
    }

    /// <summary>A reproducible pseudo-random sequence (SplitMix64) for test data; not used for security.</summary>
    private sealed class DeterministicRandom(int seed)
    {
        private ulong _state = (ulong)seed;

        public int Next(int maxExclusive)
        {
            _state += 0x9E3779B97F4A7C15;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            z ^= z >> 31;
            return (int)(z % (ulong)maxExclusive);
        }
    }
}
