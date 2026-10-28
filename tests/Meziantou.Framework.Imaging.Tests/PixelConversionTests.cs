using System.Buffers.Binary;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;
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

    /// <summary>Every pair of integer pixel formats: the ones <see cref="ReferenceModel"/> describes.</summary>
    public static TheoryData<PixelFormat, PixelFormat> AllPairs
    {
        get
        {
            var data = new TheoryData<PixelFormat, PixelFormat>();
            foreach (var source in PixelFormats.All.Where(format => !PixelFormats.IsFloatingPoint(format)))
            {
                foreach (var destination in PixelFormats.All.Where(format => !PixelFormats.IsFloatingPoint(format)))
                {
                    data.Add(source, destination);
                }
            }

            return data;
        }
    }

    /// <summary>Every pair of pixel formats of which one is a floating-point format (<see cref="FloatReferenceModel"/>).</summary>
    public static TheoryData<PixelFormat, PixelFormat> FloatPairs
    {
        get
        {
            var data = new TheoryData<PixelFormat, PixelFormat>();
            foreach (var source in PixelFormats.All)
            {
                foreach (var destination in PixelFormats.All)
                {
                    if (PixelFormats.IsFloatingPoint(source) || PixelFormats.IsFloatingPoint(destination))
                    {
                        data.Add(source, destination);
                    }
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
        AssertLayout<RgbaFloat>(16, ("R", 0), ("G", 4), ("B", 8), ("A", 12));
        AssertLayout<RgbaHalf>(8, ("R", 0), ("G", 2), ("B", 4), ("A", 6));
        AssertLayout<La16>(2, ("L", 0), ("A", 1));
        AssertLayout<La32>(4, ("L", 0), ("A", 2));
        AssertLayout<Rgb48>(6, ("R", 0), ("G", 2), ("B", 4));
        AssertLayout<A8>(1, ("A", 0));
    }

    [Fact]
    public void StructsStoreComponentsInDocumentedByteOrder()
    {
        Assert.Equal([1, 2, 3, 4], Bytes(new Rgba32(1, 2, 3, 4)));
        Assert.Equal([3, 2, 1, 4], Bytes(new Bgra32(r: 1, g: 2, b: 3, a: 4)));
        Assert.Equal([1, 2, 3], Bytes(new Rgb24(1, 2, 3)));
        Assert.Equal([7], Bytes(new Gray8(7)));
        Assert.Equal([7, 9], Bytes(new La16(7, 9)));
        Assert.Equal([9], Bytes(new A8(9)));

        // 16-bit components are native-endian ushort values
        Assert.Equal(Native16(0x0102, 0x0304, 0x0506, 0x0708), Bytes(new Rgba64(0x0102, 0x0304, 0x0506, 0x0708)));
        Assert.Equal(Native16(0xA1B2), Bytes(new Gray16(0xA1B2)));
        Assert.Equal(Native16(0xA1B2, 0x0102), Bytes(new La32(0xA1B2, 0x0102)));
        Assert.Equal(Native16(0x0102, 0x0304, 0x0506), Bytes(new Rgb48(0x0102, 0x0304, 0x0506)));
    }

    [Fact]
    public void ConstructorsTakeLogicalOrderAndDefaultToOpaque()
    {
        var bgra = new Bgra32(r: 10, g: 20, b: 30);
        Assert.Equal((10, 20, 30, 255), (bgra.R, bgra.G, bgra.B, bgra.A));
        Assert.Equal(255, new Rgba32(1, 2, 3).A);
        Assert.Equal(65535, new Rgba64(1, 2, 3).A);
        Assert.Equal(255, new La16(1).A);
        Assert.Equal(65535, new La32(1).A);
        Assert.Equal(1f, new RgbaFloat(1, 2, 3).A);
        Assert.Equal(Half.One, new RgbaHalf((Half)1, (Half)2, (Half)3).A);
    }

    [Fact]
    public void FloatingPointStructsCompareTheirComponents()
    {
        Assert.Equal(new RgbaFloat(float.NaN, 0, 0), new RgbaFloat(float.NaN, 0, 0));
        Assert.NotEqual(new RgbaFloat(0.5f, 0, 0), new RgbaFloat(0.5f, 0, 0, 0.5f));
        Assert.True(new RgbaHalf(Half.NaN, Half.Zero, Half.Zero) == new RgbaHalf(Half.NaN, Half.Zero, Half.Zero));
        Assert.True(new RgbaHalf(Half.One, Half.Zero, Half.Zero) != new RgbaHalf(Half.Zero, Half.Zero, Half.Zero));
        Assert.Equal("RgbaFloat(1.5, -2, 0, 1)", new RgbaFloat(1.5f, -2, 0).ToString());
        Assert.Equal("RgbaHalf(0.5, 2, 0, 1)", new RgbaHalf((Half)0.5, (Half)2, Half.Zero).ToString());
        Assert.Equal(new RgbaFloat(1, 2, 3).GetHashCode(), new RgbaFloat(1, 2, 3).GetHashCode());
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
    [InlineData(PixelFormat.RgbaFloat, 16, 32, 4, true, false)]
    [InlineData(PixelFormat.RgbaHalf, 8, 16, 4, true, false)]
    public void PixelFormatQueries(PixelFormat format, int bytesPerPixel, int bitsPerComponent, int components, bool alpha, bool gray)
    {
        Assert.Equal(format is PixelFormat.RgbaFloat or PixelFormat.RgbaHalf, PixelFormats.IsFloatingPoint(format));
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
    // Gray with alpha, 16-bit RGB and alpha masks: the exact conversions both ways
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void GrayWithAlphaConvertsToRgbaAndBackExactly()
    {
        // Every 8-bit gray level with every alpha
        var la16 = new La16[65536];
        for (var i = 0; i < la16.Length; i++)
        {
            la16[i] = new La16((byte)i, (byte)(i >> 8));
        }

        var rgba = new Rgba32[la16.Length];
        PixelConverter.ConvertRow<La16, Rgba32>(la16, rgba);
        for (var i = 0; i < la16.Length; i++)
        {
            Assert.Equal(new Rgba32(la16[i].L, la16[i].L, la16[i].L, la16[i].A), rgba[i]);
        }

        Assert.Equal(la16, RoundTrip<La16, Rgba32>(la16));
        Assert.Equal(la16, RoundTrip<La16, Bgra32>(la16));
        Assert.Equal(la16, RoundTrip<La16, Rgba64>(la16));
        Assert.Equal(la16, RoundTrip<La16, La32>(la16));
        Assert.Equal(la16, RoundTrip<La16, RgbaFloat>(la16));

        // Every 16-bit gray level, with alpha ramps
        var la32 = new La32[65536];
        for (var i = 0; i < la32.Length; i++)
        {
            la32[i] = new La32((ushort)i, (ushort)(65535 - i));
        }

        Assert.Equal(la32, RoundTrip<La32, Rgba64>(la32));
        Assert.Equal(la32, RoundTrip<La32, RgbaFloat>(la32));

        // A gray format without alpha gets an opaque alpha, and comes back
        var gray = new Gray8[256];
        for (var i = 0; i < gray.Length; i++)
        {
            gray[i] = new Gray8((byte)i);
        }

        var opaque = new La16[gray.Length];
        PixelConverter.ConvertRow<Gray8, La16>(gray, opaque);
        Assert.All(opaque, pixel => Assert.Equal(byte.MaxValue, pixel.A));
        Assert.Equal(gray, RoundTrip<Gray8, La16>(gray));
    }

    [Fact]
    public void ColorToGrayWithAlphaKeepsTheAlphaAndTakesTheLuma()
    {
        Rgba64[] colors = [new Rgba64(0x1234, 0xABCD, 0x0F0F, 0x8000), new Rgba64(65535, 0, 0, 0), new Rgba64(0, 65535, 0, 65535)];
        var la32 = new La32[colors.Length];
        var la16 = new La16[colors.Length];
        PixelConverter.ConvertRow<Rgba64, La32>(colors, la32);
        PixelConverter.ConvertRow<Rgba64, La16>(colors, la16);
        for (var i = 0; i < colors.Length; i++)
        {
            var luma = ReferenceModel.Luma(colors[i].R, colors[i].G, colors[i].B, 65535);
            Assert.Equal(new La32((ushort)luma, colors[i].A), la32[i]);
            Assert.Equal(new La16(ReferenceModel.To8(luma), ReferenceModel.To8(colors[i].A)), la16[i]);
        }

        // Removing the alpha of gray pixels flattens them onto the background, whose luma is taken
        var gray = new Gray16[colors.Length];
        PixelConverter.ConvertRow<La32, Gray16>(la32, gray, Background);
        var flattened = ReferenceModel.Flatten(la32[0].L, Background.R, la32[0].A, 65535);
        Assert.Equal(ReferenceModel.Luma(flattened, ReferenceModel.Flatten(la32[0].L, Background.G, la32[0].A, 65535), ReferenceModel.Flatten(la32[0].L, Background.B, la32[0].A, 65535), 65535), gray[0].Value);
        Assert.Throws<UnsupportedImageFeatureException>(() => PixelConverter.ConvertRow<La32, Gray16>(la32, gray));
    }

    [Fact]
    public void Rgb48ConvertsToRgba64AndBackExactly()
    {
        var rgb48 = new Rgb48[65536];
        for (var i = 0; i < rgb48.Length; i++)
        {
            rgb48[i] = new Rgb48((ushort)i, (ushort)(65535 - i), (ushort)(i * 7));
        }

        var rgba = new Rgba64[rgb48.Length];
        PixelConverter.ConvertRow<Rgb48, Rgba64>(rgb48, rgba);
        for (var i = 0; i < rgb48.Length; i++)
        {
            Assert.Equal(new Rgba64(rgb48[i].R, rgb48[i].G, rgb48[i].B, ushort.MaxValue), rgba[i]);
        }

        Assert.Equal(rgb48, RoundTrip<Rgb48, Rgba64>(rgb48));
        Assert.Equal(rgb48, RoundTrip<Rgb48, RgbaFloat>(rgb48));

        // 8-bit colors widen to 16 bits exactly (v * 257) and come back
        var rgb24 = new Rgb24[256];
        for (var i = 0; i < rgb24.Length; i++)
        {
            rgb24[i] = new Rgb24((byte)i, (byte)(255 - i), (byte)(i * 3));
        }

        var wide = new Rgb48[rgb24.Length];
        PixelConverter.ConvertRow<Rgb24, Rgb48>(rgb24, wide);
        Assert.Equal(new Rgb48(257, 254 * 257, 3 * 257), wide[1]);
        Assert.Equal(rgb24, RoundTrip<Rgb24, Rgb48>(rgb24));

        // Removing alpha needs opaque pixels, or a background
        Rgba64[] translucent = [new Rgba64(1, 2, 3, 4)];
        Assert.Throws<UnsupportedImageFeatureException>(() => PixelConverter.ConvertRow<Rgba64, Rgb48>(translucent, new Rgb48[1]));
    }

    [Fact]
    public void AlphaMasksReadAsBlackAndKeepTheAlphaOnly()
    {
        var masks = new A8[256];
        for (var i = 0; i < masks.Length; i++)
        {
            masks[i] = new A8((byte)i);
        }

        var rgba = new Rgba32[masks.Length];
        PixelConverter.ConvertRow<A8, Rgba32>(masks, rgba);
        for (var i = 0; i < masks.Length; i++)
        {
            Assert.Equal(new Rgba32(0, 0, 0, (byte)i), rgba[i]);
        }

        Assert.Equal(masks, RoundTrip<A8, Rgba32>(masks));
        Assert.Equal(masks, RoundTrip<A8, Rgba64>(masks));
        Assert.Equal(masks, RoundTrip<A8, La16>(masks));
        Assert.Equal(masks, RoundTrip<A8, RgbaFloat>(masks));

        // The color of the source is discarded; a format without alpha is opaque
        var extracted = new A8[2];
        PixelConverter.ConvertRow<Rgba32, A8>([new Rgba32(10, 20, 30, 40), new Rgba32(255, 255, 255, 255)], extracted);
        Assert.Equal([new A8(40), new A8(255)], extracted);
        PixelConverter.ConvertRow<Rgb24, A8>([new Rgb24(1, 2, 3), new Rgb24(4, 5, 6)], extracted);
        Assert.Equal([new A8(255), new A8(255)], extracted);

        // Black flattened onto a background: what the mask covers is black
        var flattened = new Rgb24[1];
        PixelConverter.ConvertRow<A8, Rgb24>([new A8(128)], flattened, new Rgba64(65535, 65535, 65535));
        Assert.Equal(new Rgb24(127, 127, 127), flattened[0]);
        Assert.Throws<UnsupportedImageFeatureException>(() => PixelConverter.ConvertRow<A8, Rgb24>([new A8(128)], flattened));

        // Only black colors convert to a mask and back unchanged
        Assert.True(PixelConverter.IsExactlyRepresentable(new Rgba64(0, 0, 0, 257), PixelFormat.A8));
        Assert.False(PixelConverter.IsExactlyRepresentable(new Rgba64(257, 257, 257, 257), PixelFormat.A8));
        Assert.False(PixelConverter.IsExactlyRepresentable(new Rgba64(0, 0, 0, 1), PixelFormat.A8));
    }

    [Fact]
    public void CompactFormatsConvertImagesWithCloneAs()
    {
        using var image = new Image<Rgba32>(2, 1);
        image.Frames[0][0, 0] = new Rgba32(10, 10, 10, 200);
        image.Frames[0][1, 0] = new Rgba32(0, 0, 0, 0);
        using var gray = image.CloneAs<La16>();
        Assert.Equal(PixelFormat.La16, gray.PixelFormat);
        Assert.Equal(new La16(10, 200), gray.Frames[0][0, 0]);
        using var back = gray.CloneAs<Rgba32>();
        Assert.Equal(image.Frames[0][0, 0], back.Frames[0][0, 0]);
        using var mask = image.CloneAs<A8>();
        Assert.Equal(new A8(200), mask.Frames[0][0, 0]);
        using var wide = image.CloneAs<Rgb48>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) });
        var level = (ushort)(ReferenceModel.Flatten(10, 0, 200, 255) * 257);
        Assert.Equal(new Rgb48(level, level, level), wide.Frames[0][0, 0]);
    }

    [Fact]
    public void CompactFormatsConvertExactlyAndPngStoresThem()
    {
        // A gray image with transparency takes 2 bytes per pixel instead of the 4 of Rgba32
        using var image = new Image<La16>(64, 64, new La16(l: 0, a: 0));
        image.Frames[0][10, 10] = new La16(l: 200, a: 128);

        // Converting gray pixels to RGBA and back is exact
        using var rgba = image.CloneAs<Rgba32>();
        Assert.Equal(new Rgba32(200, 200, 200, 128), rgba.Frames[0][10, 10]);
        using var back = rgba.CloneAs<La16>();
        Assert.Equal(new La16(200, 128), back.Frames[0][10, 10]);

        // An alpha mask keeps the alpha of an image and reads as black
        using var mask = rgba.CloneAs<A8>();
        Assert.Equal(new A8(128), mask.Frames[0][10, 10]);

        // PNG stores gray with alpha as it is, and an untyped load gives it back as La16
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        stream.Position = 0;
        using var loaded = Image.Load(stream);
        Assert.Equal(PixelFormat.La16, loaded.PixelFormat);
    }

    private static TPixel[] RoundTrip<TPixel, TIntermediate>(TPixel[] pixels)
        where TPixel : unmanaged
        where TIntermediate : unmanaged
    {
        var intermediate = new TIntermediate[pixels.Length];
        PixelConverter.ConvertRow<TPixel, TIntermediate>(pixels, intermediate);
        var result = new TPixel[pixels.Length];
        PixelConverter.ConvertRow<TIntermediate, TPixel>(intermediate, result);
        return result;
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
    // Floating-point formats
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void IntegerSamplesBecomeTheNearestFloatOfTheirFraction()
    {
        var rgba64 = new Rgba64[65536];
        for (var v = 0; v < rgba64.Length; v++)
        {
            rgba64[v] = new Rgba64((ushort)v, (ushort)(65535 - v), (ushort)(v ^ 0x5A5A), (ushort)v);
        }

        var floats = new RgbaFloat[rgba64.Length];
        PixelConverter.ConvertRow<Rgba64, RgbaFloat>(rgba64, floats);
        for (var v = 0; v < rgba64.Length; v++)
        {
            FloatReferenceModel.AssertNearestFloat(v, 65535, floats[v].R);
            FloatReferenceModel.AssertNearestFloat(65535 - v, 65535, floats[v].G);
            FloatReferenceModel.AssertNearestFloat(v ^ 0x5A5A, 65535, floats[v].B);
        }

        // And back, exactly, for every value
        var back = new Rgba64[rgba64.Length];
        PixelConverter.ConvertRow<RgbaFloat, Rgba64>(floats, back);
        Assert.Equal(rgba64, back);

        var gray16 = new Gray16[65536];
        for (var v = 0; v < gray16.Length; v++)
        {
            gray16[v] = new Gray16((ushort)v);
        }

        PixelConverter.ConvertRow<Gray16, RgbaFloat>(gray16, floats);
        var grayBack = new Gray16[gray16.Length];
        PixelConverter.ConvertRow<RgbaFloat, Gray16>(floats, grayBack);
        Assert.Equal(gray16, grayBack);
        Assert.Equal(new RgbaFloat(0, 0, 0, 1), floats[0]);
        Assert.Equal(new RgbaFloat(1, 1, 1, 1), floats[65535]);
    }

    [Fact]
    public void EightBitSamplesRoundTripThroughEveryFloatingPointFormat()
    {
        var rgba = new Rgba32[256];
        var gray = new Gray8[256];
        for (var v = 0; v < 256; v++)
        {
            rgba[v] = new Rgba32((byte)v, (byte)(255 - v), (byte)(v ^ 0x5A), (byte)v);
            gray[v] = new Gray8((byte)v);
        }

        var floats = new RgbaFloat[256];
        var halves = new RgbaHalf[256];
        PixelConverter.ConvertRow<Rgba32, RgbaFloat>(rgba, floats);
        PixelConverter.ConvertRow<Rgba32, RgbaHalf>(rgba, halves);
        for (var v = 0; v < 256; v++)
        {
            FloatReferenceModel.AssertNearestFloat(v, 255, floats[v].R);
            FloatReferenceModel.AssertNearestHalf(v, 255, halves[v].R);
            FloatReferenceModel.AssertNearestHalf(255 - v, 255, halves[v].G);
        }

        var fromFloat = new Rgba32[256];
        var fromHalf = new Bgra32[256];
        PixelConverter.ConvertRow<RgbaFloat, Rgba32>(floats, fromFloat);
        PixelConverter.ConvertRow<RgbaHalf, Bgra32>(halves, fromHalf);
        Assert.Equal(rgba, fromFloat);
        Assert.Equal(rgba.Select(p => new Bgra32(p.R, p.G, p.B, p.A)), fromHalf);

        PixelConverter.ConvertRow<Gray8, RgbaHalf>(gray, halves);
        var grayBack = new Gray8[256];
        PixelConverter.ConvertRow<RgbaHalf, Gray8>(halves, grayBack);
        Assert.Equal(gray, grayBack);
    }

    [Fact]
    public void SixteenBitSamplesBecomeTheNearestHalf()
    {
        var rgba64 = new Rgba64[65536];
        for (var v = 0; v < rgba64.Length; v++)
        {
            rgba64[v] = new Rgba64((ushort)v, 0, 0);
        }

        var halves = new RgbaHalf[rgba64.Length];
        PixelConverter.ConvertRow<Rgba64, RgbaHalf>(rgba64, halves);
        for (var v = 0; v < rgba64.Length; v++)
        {
            FloatReferenceModel.AssertNearestHalf(v, 65535, halves[v].R);
        }

        // Half has 11 significant bits: 16-bit samples do not all come back
        var back = new Rgba64[rgba64.Length];
        PixelConverter.ConvertRow<RgbaHalf, Rgba64>(halves, back);
        Assert.Equal(new Rgba64(65535, 0, 0), back[65535]);
        Assert.NotEqual(rgba64[65534], back[65534]);
        Assert.False(PixelConverter.IsExactlyRepresentable(new Rgba64(65534, 0, 0), PixelFormat.RgbaHalf));
        Assert.False(PixelConverter.IsExactlyRepresentable(new Rgba64(257 * 100, 0, 65535, 0), PixelFormat.RgbaHalf));
        Assert.True(PixelConverter.IsExactlyRepresentable(new Rgba64(0, 65535, 65535, 0), PixelFormat.RgbaHalf));
        Assert.True(PixelConverter.IsExactlyRepresentable(new Rgba64(1, 2, 3, 4), PixelFormat.RgbaFloat));
    }

    [Theory]
    [MemberData(nameof(FloatPairs))]
    public void EveryFloatingPointPairMatchesReferenceModel(PixelFormat source, PixelFormat destination)
    {
        var bytes = FloatReferenceModel.CreateSampleRow(source, count: 600, seed: (int)source * 10 + (int)destination);
        var pixels = FloatReferenceModel.ReadRow(source, bytes);
        var output = new byte[pixels.Count * PixelFormats.GetBytesPerPixel(destination)];
        Assert.Equal(pixels.Count, PixelConverter.ConvertRow(source, bytes, destination, output, Background));
        var converted = FloatReferenceModel.ReadRow(destination, output);
        for (var i = 0; i < pixels.Count; i++)
        {
            FloatReferenceModel.AssertConverted(source, pixels[i], destination, Background, converted[i], i);
        }

        // The typed, untyped and scalar paths agree
        var method = typeof(PixelConversionTests).GetMethod(nameof(ConvertTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(PixelFormats.GetPixelType(source), PixelFormats.GetPixelType(destination));
        var (typed, scalar) = ((byte[], byte[]))method.Invoke(null, [bytes, pixels.Count])!;
        Assert.Equal(output, typed);
        Assert.Equal(output, scalar);
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Bgra32)]
    [InlineData(PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Rgba64)]
    [InlineData(PixelFormat.Gray8)]
    [InlineData(PixelFormat.Gray16)]
    public void SamplesThatAreNotNumbersAreAlwaysRejected(PixelFormat destination)
    {
        RgbaFloat[] row = [new(0.5f, 0.5f, 0.5f), new(0.25f, float.NaN, 0)];
        var output = new byte[2 * PixelFormats.GetBytesPerPixel(destination)];
        Array.Fill(output, (byte)0x5A);
        foreach (var options in new[] { null, new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip, ToneMapping = HdrToneMappingOptions.Default } })
        {
            var plan = PixelConversionPlan.Create(PixelFormat.RgbaFloat, destination, options);
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => plan.ConvertRow(unsafe(MemoryMarshal.AsBytes(row.AsSpan())), output));
            Assert.Contains("index 1", exception.Message);
            Assert.Equal("Not-a-number samples", exception.Feature);
            Assert.All(output, value => Assert.Equal((byte)0x5A, value));
        }

        // An alpha that is not a number too
        Assert.Throws<UnsupportedImageFeatureException>(() => PixelConverter.ConvertRow<RgbaHalf, Rgba64>([new RgbaHalf(Half.Zero, Half.Zero, Half.Zero, Half.NaN)], new Rgba64[1]));
    }

    [Fact]
    public void SamplesOutOfRangeAreRejectedUnlessClipped()
    {
        RgbaFloat[] row = [new(0, 1, 0.5f), new(1.5f, -0.25f, float.PositiveInfinity, 0.5f), new(0.5f, 0.5f, 0.5f, 2f)];
        var output = new Rgba64[3];
        output.AsSpan().Fill(new Rgba64(1, 2, 3, 4));
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => PixelConverter.ConvertRow<RgbaFloat, Rgba64>(row, output));
        Assert.Contains("index 1", exception.Message);
        Assert.Contains("OutOfRangeSamples", exception.Message);
        Assert.Equal("Out-of-range samples", exception.Feature);
        Assert.All(output, pixel => Assert.Equal(new Rgba64(1, 2, 3, 4), pixel));

        var clip = PixelConversionPlan.Create<RgbaFloat, Rgba64>(new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip });
        clip.ConvertRow<RgbaFloat, Rgba64>(row, output);
        Assert.Equal([new Rgba64(0, 65535, 32768), new Rgba64(65535, 0, 65535, 32768), new Rgba64(32768, 32768, 32768, 65535)], output);

        // The options are validated
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelConversionOptions { OutOfRangeSamples = (OutOfRangeSampleHandling)2 });
    }

    [Fact]
    public void FloatToHalfRoundsAndRejectsWhatOverflowsUnlessClipped()
    {
        RgbaFloat[] row = [new(1 / 3f, -2.5f, 65504f, 0.5f), new(float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1), new(65519f, -65519f, 1e-8f, 0)];
        var halves = new RgbaHalf[3];
        PixelConverter.ConvertRow<RgbaFloat, RgbaHalf>(row, halves);
        Assert.Equal(new RgbaHalf((Half)(1 / 3f), (Half)(-2.5f), Half.MaxValue, (Half)0.5f), halves[0]);
        Assert.True(Half.IsNaN(halves[1].R));
        Assert.Equal((Half.PositiveInfinity, Half.NegativeInfinity), (halves[1].G, halves[1].B));
        Assert.Equal((Half.MaxValue, Half.MinValue), (halves[2].R, halves[2].G));

        RgbaFloat[] overflow = [new(65520f, 0, 0)];
        Assert.Throws<UnsupportedImageFeatureException>(() => PixelConverter.ConvertRow<RgbaFloat, RgbaHalf>(overflow, new RgbaHalf[1]));
        var clipped = new RgbaHalf[1];
        PixelConverter.ConvertRow<RgbaFloat, RgbaHalf>(overflow, clipped, floats: FloatSampleConversion.Clipping);
        Assert.Equal(Half.MaxValue, clipped[0].R);

        // Half to float is exact, the samples that are not numbers and the infinities included
        var floats = new RgbaFloat[3];
        PixelConverter.ConvertRow<RgbaHalf, RgbaFloat>(halves, floats);
        Assert.Equal(new RgbaFloat((float)(Half)(1 / 3f), -2.5f, 65504f, 0.5f), floats[0]);
        Assert.Equal(new RgbaFloat(float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1), floats[1]);
    }

    [Fact]
    public void ToneMappingCompressesTheHighlightsAndKeepsTheHue()
    {
        // Reinhard with the peak of the row (4 whites): x (1 + x / 16) / (1 + x) on the largest component
        RgbaFloat[] row = [new(4, 2, 1), new(0.5f, 0.25f, 0), new(1, 1, 1, 0.5f)];
        var options = new PixelConversionOptions { ToneMapping = new HdrToneMappingOptions { Operator = HdrToneMappingOperator.Reinhard } };
        var plan = PixelConversionPlan.Create<RgbaFloat, Rgba64>(options);
        Assert.True(plan.RequiresPeak);
        plan.SetPeak(PixelConverter.GetLargestFiniteColor(PixelFormat.RgbaFloat, unsafe(MemoryMarshal.AsBytes(row.AsSpan()))));
        var output = new Rgba64[3];
        plan.ConvertRow<RgbaFloat, Rgba64>(row, output);
        static ushort Expected(double component, double largest) => (ushort)Math.Floor((component * (largest * (1 + (largest / 16)) / (1 + largest) / largest) * 65535) + 0.5);
        Assert.Equal(new Rgba64(65535, 32768, 16384), output[0]);
        Assert.Equal(new Rgba64(Expected(0.5, 0.5), Expected(0.25, 0.5), 0), output[1]);
        Assert.Equal(new Rgba64(Expected(1, 1), Expected(1, 1), Expected(1, 1), 32768), output[2]);

        // A source peak luminance is given in candelas per square meter, and the white is 203 of them
        var given = PixelConversionPlan.Create<RgbaFloat, Rgba64>(new PixelConversionOptions { ToneMapping = new HdrToneMappingOptions { Operator = HdrToneMappingOperator.Clip, SourcePeakLuminance = 812 } });
        Assert.False(given.RequiresPeak);
        given.ConvertRow<RgbaFloat, Rgba64>([new RgbaFloat(8, 2, 0.5f), new RgbaFloat(float.PositiveInfinity, 0.5f, 0)], output);
        Assert.Equal(new Rgba64(65535, 16384, 4096), output[0]);
        Assert.Equal(new Rgba64(65535, 0, 0), output[1]);

        // Negative components are still out of range after the curve
        Assert.Throws<UnsupportedImageFeatureException>(() => given.ConvertRow<RgbaFloat, Rgba64>([new RgbaFloat(2, -1, 0)], output));
    }

    [Fact]
    public void FloatingPointPixelsAreOpaqueWhenAlphaIsExactlyOne()
    {
        Assert.Equal(1, PixelConverter.IndexOfNonOpaque<RgbaFloat>([new RgbaFloat(5, 0, 0), new RgbaFloat(0, 0, 0, 0.999999f)]));
        Assert.Equal(0, PixelConverter.IndexOfNonOpaque<RgbaHalf>([new RgbaHalf(Half.Zero, Half.Zero, Half.Zero, Half.NaN)]));
        Assert.True(PixelConverter.IsOpaque(PixelFormat.RgbaHalf, unsafe(MemoryMarshal.AsBytes(new RgbaHalf[] { new(Half.One, Half.One, Half.One) }.AsSpan()))));

        // Alpha is removed after the samples are clipped: an alpha of 1.5 clipped to 1 is opaque
        var clip = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip };
        var output = new Rgb24[1];
        PixelConversionPlan.Create<RgbaFloat, Rgb24>(clip).ConvertRow<RgbaFloat, Rgb24>([new RgbaFloat(0.2f, 0.4f, 0.6f, 1.5f)], output);
        Assert.Equal(new Rgb24(51, 102, 153), output[0]);
        Assert.Throws<UnsupportedImageFeatureException>(() => PixelConverter.ConvertRow<RgbaFloat, Rgb24>([new RgbaFloat(0.2f, 0.4f, 0.6f, 0.5f)], output));
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
            if (PixelFormats.HasColor(format))
            {
                Assert.Throws<UnsupportedImageFeatureException>(() => PixelConversionPlan.Create(format, format, options: null, profile));
            }
            else
            {
                // An alpha mask has no color for a profile to describe: the profile is dropped
                Assert.Null(PixelConversionPlan.Create(format, format, options: null, profile).ColorProfile);
            }
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

            // The luma of a gray color is that gray (0.2126 + 0.7152 + 0.0722 = 1)
            if (PixelFormats.IsGrayscale(destination))
            {
                var y = Luma(r, g, b, sourceMax);
                (r, g, b) = (y, y, y);
            }

            // An alpha mask keeps the alpha only, and reads as black
            if (!PixelFormats.HasColor(destination))
            {
                (r, g, b) = (0, 0, 0);
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

            if (!PixelFormats.HasColor(format))
                return [.. result.Select(p => (0, 0, 0, p.A))];

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
                    case PixelFormat.La16:
                        (span[0], span[1]) = ((byte)r, (byte)a);
                        break;
                    case PixelFormat.La32:
                        Native16(checked((ushort)r), checked((ushort)a)).CopyTo(span);
                        break;
                    case PixelFormat.Rgb48:
                        Native16(checked((ushort)r), checked((ushort)g), checked((ushort)b)).CopyTo(span);
                        break;
                    case PixelFormat.A8:
                        span[0] = (byte)a;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(format), format, "The reference model does not know this format.");
                }
            }

            return result;
        }
    }

    /// <summary>
    /// The conversions from and to the floating-point formats, with exact rational arithmetic on the binary values of the
    /// samples (<see cref="BigInteger"/>): it shares no code with the library converters.
    /// </summary>
    internal static class FloatReferenceModel
    {
        /// <summary>Asserts that <paramref name="actual"/> is a float nearest to <c>numerator / denominator</c>.</summary>
        public static void AssertNearestFloat(int numerator, int denominator, float actual)
        {
            var exact = new Rational(numerator, denominator);
            var distance = Rational.Abs(Rational.FromDouble(actual) - exact);
            Assert.True(distance <= Rational.Abs(Rational.FromDouble(MathF.BitIncrement(actual)) - exact) && distance <= Rational.Abs(Rational.FromDouble(MathF.BitDecrement(actual)) - exact), $"{actual} is not the float nearest to {numerator}/{denominator}");
        }

        /// <summary>Asserts that <paramref name="actual"/> is a half nearest to <c>numerator / denominator</c>.</summary>
        public static void AssertNearestHalf(int numerator, int denominator, Half actual)
        {
            var exact = new Rational(numerator, denominator);
            var distance = Rational.Abs(Rational.FromDouble((double)actual) - exact);
            Assert.True(distance <= Rational.Abs(Rational.FromDouble((double)Half.BitIncrement(actual)) - exact) && distance <= Rational.Abs(Rational.FromDouble((double)Half.BitDecrement(actual)) - exact), $"{actual} is not the half nearest to {numerator}/{denominator}");
        }

        /// <summary>Random samples of a format plus edge cases; floating-point samples stay in [0, 1] (the cases out of range have their own tests).</summary>
        public static byte[] CreateSampleRow(PixelFormat format, int count, int seed)
        {
            if (!PixelFormats.IsFloatingPoint(format))
                return ReferenceModel.CreateRow(format, ReferenceModel.CreateSamplePixels(format, count, seed));

            var random = new DeterministicRandom(seed);
            var pixels = new List<(double R, double G, double B, double A)>
            {
                (0, 0, 0, 0), (1, 1, 1, 1), (1, 0, 0, 1), (0.5, 0.25, 0.75, 0.5), (1 / 3.0, 2 / 3.0, 0.1, 1),
                ((128.5 / 255) + 1e-7, (32767.5 / 65535) - 1e-9, 0.5 / 65535, 1), (1, 1, 1, 1 - 1e-7),
            };
            while (pixels.Count < count)
            {
                pixels.Add((random.Next(1 << 24) / (double)(1 << 24), random.Next(65536) / 65535.0, random.Next(1 << 20) / (double)(1 << 20), random.Next(4) == 0 ? 1 : random.Next(1 << 24) / (double)(1 << 24)));
            }

            var size = PixelFormats.GetBytesPerPixel(format);
            var result = new byte[pixels.Count * size];
            for (var i = 0; i < pixels.Count; i++)
            {
                var (r, g, b, a) = pixels[i];
                if (format == PixelFormat.RgbaFloat)
                {
                    unsafe { MemoryMarshal.Write(result.AsSpan(i * size), new RgbaFloat((float)r, (float)g, (float)b, (float)a)); }
                }
                else
                {
                    unsafe { MemoryMarshal.Write(result.AsSpan(i * size), new RgbaHalf((Half)r, (Half)g, (Half)b, (Half)a)); }
                }
            }

            return result;
        }

        /// <summary>Reads a row as exact rational components: an integer sample is itself (over 1), a floating-point sample its binary value.</summary>
        public static List<(Rational R, Rational G, Rational B, Rational A)> ReadRow(PixelFormat format, byte[] bytes)
        {
            var size = PixelFormats.GetBytesPerPixel(format);
            var result = new List<(Rational R, Rational G, Rational B, Rational A)>();
            for (var offset = 0; offset < bytes.Length; offset += size)
            {
                var span = bytes.AsSpan(offset, size);
                result.Add(format switch
                {
                    PixelFormat.RgbaFloat => FromFloats(unsafe(MemoryMarshal.Read<RgbaFloat>(span))),
                    PixelFormat.RgbaHalf => FromHalves(unsafe(MemoryMarshal.Read<RgbaHalf>(span))),
                    PixelFormat.Rgba32 => (span[0], span[1], span[2], span[3]),
                    PixelFormat.Bgra32 => (span[2], span[1], span[0], span[3]),
                    PixelFormat.Rgb24 => (span[0], span[1], span[2], 255),
                    PixelFormat.Gray8 => (span[0], span[0], span[0], 255),
                    PixelFormat.Rgba64 => (Read16(span, 0), Read16(span, 1), Read16(span, 2), Read16(span, 3)),
                    PixelFormat.Gray16 => (Read16(span, 0), Read16(span, 0), Read16(span, 0), 65535),
                    PixelFormat.La16 => (span[0], span[0], span[0], span[1]),
                    PixelFormat.La32 => (Read16(span, 0), Read16(span, 0), Read16(span, 0), Read16(span, 1)),
                    PixelFormat.Rgb48 => (Read16(span, 0), Read16(span, 1), Read16(span, 2), 65535),
                    PixelFormat.A8 => (0, 0, 0, span[0]),
                    _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The reference model does not know this format."),
                });
            }

            return result;

            static (Rational, Rational, Rational, Rational) FromFloats(RgbaFloat p) => (Rational.FromDouble(p.R), Rational.FromDouble(p.G), Rational.FromDouble(p.B), Rational.FromDouble(p.A));
            static (Rational, Rational, Rational, Rational) FromHalves(RgbaHalf p) => (Rational.FromDouble((double)p.R), Rational.FromDouble((double)p.G), Rational.FromDouble((double)p.B), Rational.FromDouble((double)p.A));
            static int Read16(Span<byte> span, int index) => unsafe(MemoryMarshal.Read<ushort>(span[(index * 2)..]));
        }

        /// <summary>
        /// Asserts one converted pixel. Integer to floating point: the nearest value of the fraction. Floating point to
        /// integer: flattening, luma and rounding on the exact values, where the library computes in double precision, so a
        /// sample within 1e-9 of a rounding tie may round either way. Floating point to floating point: the nearest value.
        /// </summary>
        public static void AssertConverted(PixelFormat source, (Rational R, Rational G, Rational B, Rational A) pixel, PixelFormat destination, Rgba64 background, (Rational R, Rational G, Rational B, Rational A) actual, int index)
        {
            var sourceMax = PixelFormats.IsFloatingPoint(source) ? 1 : ReferenceModel.Max(source);
            var (r, g, b, a) = (pixel.R / sourceMax, pixel.G / sourceMax, pixel.B / sourceMax, pixel.A / sourceMax);
            if (PixelFormats.IsFloatingPoint(destination))
            {
                foreach (var (expected, value) in new[] { (r, actual.R), (g, actual.G), (b, actual.B), (a, actual.A) })
                {
                    AssertNearest(expected, value, destination, index);
                }

                return;
            }

            if (!PixelFormats.HasAlpha(destination) && a != Rational.One)
            {
                var (bgR, bgG, bgB) = (new Rational(background.R, 65535), new Rational(background.G, 65535), new Rational(background.B, 65535));
                (r, g, b) = ((r * a) + (bgR * (Rational.One - a)), (g * a) + (bgG * (Rational.One - a)), (b * a) + (bgB * (Rational.One - a)));
                a = Rational.One;
            }

            if (PixelFormats.IsGrayscale(destination))
            {
                var y = (new Rational(2126, 10000) * r) + (new Rational(7152, 10000) * g) + (new Rational(722, 10000) * b);
                (r, g, b) = (y, y, y);
            }

            if (!PixelFormats.HasColor(destination))
            {
                (r, g, b) = (new Rational(0, 1), new Rational(0, 1), new Rational(0, 1));
            }

            var max = ReferenceModel.Max(destination);
            AssertRounded(r * max, actual.R, index);
            AssertRounded(g * max, actual.G, index);
            AssertRounded(b * max, actual.B, index);
            if (PixelFormats.HasAlpha(destination))
            {
                AssertRounded(a * max, actual.A, index);
            }
        }

        private static void AssertRounded(Rational exact, Rational actual, int index)
        {
            var nearest = Rational.Floor(exact + new Rational(1, 2));
            if (actual == nearest)
                return;

            // The library works in double precision: only a value at a rounding tie may round to the other side
            var tie = Rational.Abs(exact - Rational.Floor(exact) - new Rational(1, 2));
            Assert.True(tie < new Rational(1, 1_000_000_000) && Rational.Abs(actual - nearest) == Rational.One, $"Pixel {index}: {actual} instead of {nearest} ({exact.ToDouble()})");
        }

        private static void AssertNearest(Rational exact, Rational actual, PixelFormat format, int index)
        {
            var value = actual.ToDouble();
            var (up, down) = format == PixelFormat.RgbaFloat
                ? ((double)MathF.BitIncrement((float)value), (double)MathF.BitDecrement((float)value))
                : ((double)Half.BitIncrement((Half)value), (double)Half.BitDecrement((Half)value));
            var distance = Rational.Abs(actual - exact);
            Assert.True(distance <= Rational.Abs(Rational.FromDouble(up) - exact) && distance <= Rational.Abs(Rational.FromDouble(down) - exact), $"Pixel {index}: {value} is not the {format} nearest to {exact.ToDouble()}");
        }
    }

    /// <summary>An exact fraction of big integers, enough for the reference of the floating-point conversions.</summary>
    internal readonly struct Rational : IEquatable<Rational>
    {
        public Rational(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.Sign < 0)
            {
                (numerator, denominator) = (-numerator, -denominator);
            }

            var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
            if (!divisor.IsZero && !divisor.IsOne)
            {
                (numerator, denominator) = (numerator / divisor, denominator / divisor);
            }

            Numerator = numerator;
            Denominator = denominator;
        }

        public static Rational One => new(1, 1);

        public BigInteger Numerator { get; }

        public BigInteger Denominator { get; }

        public static Rational FromDouble(double value)
        {
            Assert.True(double.IsFinite(value));
            var bits = BitConverter.DoubleToInt64Bits(value);
            var exponent = (int)((bits >> 52) & 0x7FF);
            var mantissa = bits & 0xFFFFFFFFFFFFFL;
            if (exponent == 0)
            {
                exponent = 1;
            }
            else
            {
                mantissa |= 1L << 52;
            }

            exponent -= 1075;
            BigInteger numerator = bits < 0 ? -mantissa : mantissa;
            return exponent >= 0 ? new Rational(numerator << exponent, 1) : new Rational(numerator, BigInteger.One << -exponent);
        }

        public static Rational Abs(Rational value) => new(BigInteger.Abs(value.Numerator), value.Denominator);

        public static Rational Floor(Rational value)
        {
            var quotient = BigInteger.DivRem(value.Numerator, value.Denominator, out var remainder);
            return new Rational(remainder.Sign < 0 ? quotient - 1 : quotient, 1);
        }

        public double ToDouble() => (double)Numerator / (double)Denominator;

        public static implicit operator Rational(int value) => new(value, 1);

        public static Rational operator +(Rational left, Rational right) => new((left.Numerator * right.Denominator) + (right.Numerator * left.Denominator), left.Denominator * right.Denominator);

        public static Rational operator -(Rational left, Rational right) => new((left.Numerator * right.Denominator) - (right.Numerator * left.Denominator), left.Denominator * right.Denominator);

        public static Rational operator *(Rational left, Rational right) => new(left.Numerator * right.Numerator, left.Denominator * right.Denominator);

        public static Rational operator /(Rational left, int right) => new(left.Numerator, left.Denominator * right);

        public static bool operator ==(Rational left, Rational right) => left.Equals(right);

        public static bool operator !=(Rational left, Rational right) => !left.Equals(right);

        public static bool operator <(Rational left, Rational right) => left.Numerator * right.Denominator < right.Numerator * left.Denominator;

        public static bool operator >(Rational left, Rational right) => right < left;

        public static bool operator <=(Rational left, Rational right) => !(right < left);

        public static bool operator >=(Rational left, Rational right) => !(left < right);

        public bool Equals(Rational other) => Numerator == other.Numerator && Denominator == other.Denominator;

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is Rational other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator}");
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
