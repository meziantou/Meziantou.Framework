using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Bmp;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using static Meziantou.Framework.Imaging.Tests.Conformance.LosslessEncoderAssertions;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// BMP encoding of every raw reference of the golden corpus, verified without external tools by the independent harness
/// reader <see cref="ReferenceBmp"/> (validated against the FFmpeg-checked corpus by the decoder conformance tests). The
/// 24-bit and 32-bit layouts are lossless, so every reference must come back exactly, in every option combination; the
/// indexed layouts are exact whenever the colors fit their palette, and otherwise map every pixel to its nearest palette
/// entry; the 16-bit layouts store the nearest 5- or 6-bit level of every sample. FFmpeg reads the same outputs in
/// <c>InteropTests/BmpInteropTests</c>.
/// </summary>
public sealed class BmpEncoderConformanceTests
{
    private static readonly BmpRowOrder[] RowOrders = [BmpRowOrder.BottomUp, BmpRowOrder.TopDown];

    public static TheoryData<string, string> Cases() => LosslessEncoderAssertions.Cases();

    /// <summary>The cases whose pixel format has no alpha: the layouts that discard alpha need no background for them.</summary>
    public static TheoryData<string, string> OpaqueCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (fixture, format) in EncoderSources.GetCases(GoldenCorpus.Default).Where(item => !PixelFormats.HasAlpha(item.Format)))
        {
            data.Add(fixture.Id, format.ToString());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BmpEncodingPreservesEverySample(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var layout in new[] { BmpPixelLayout.Auto, BmpPixelLayout.Bgra32 })
        {
            foreach (var rowOrder in RowOrders)
            {
                var encoder = new BmpEncoder { PixelLayout = layout, RowOrder = rowOrder, AllowBitDepthReduction = true, MetadataHandling = MetadataHandling.Strip };
                foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
                {
                    var context = $"{id} {name} as {pixelFormat} ({layout}, {rowOrder})";
                    using var image = EncoderSources.CreateImage(source, pixelFormat);
                    var data = Encode(image, encoder);
                    var reference = ReferenceBmp.Parse(data);
                    AssertDeclaredSizes(data, reference);
                    Assert.Equal((source.Width, source.Height), (reference.Width, reference.Height));
                    Assert.Equal(rowOrder == BmpRowOrder.TopDown, reference.IsTopDown);
                    var hasAlpha = PixelFormats.HasAlpha(pixelFormat);
                    Assert.Equal(layout == BmpPixelLayout.Bgra32 || hasAlpha ? 32 : 24, reference.BitsPerPixel);
                    Assert.Equal(reference.BitsPerPixel == 32, reference.HasAlphaMask);

                    var expected = ToRgba8(source);
                    AssertExact(expected, RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba8, reference.Rgba.Span), context);
                    AssertLibraryReadsBack(data, expected, context);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(OpaqueCases))]
    public void SixteenBitLayoutsStoreTheNearestLevelOfEverySample(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var layout in new[] { BmpPixelLayout.Bgr565, BmpPixelLayout.Bgr555 })
        {
            foreach (var rowOrder in RowOrders)
            {
                var encoder = new BmpEncoder { PixelLayout = layout, RowOrder = rowOrder, AllowBitDepthReduction = true, MetadataHandling = MetadataHandling.Strip };
                foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
                {
                    var context = $"{id} {name} as {pixelFormat} ({layout}, {rowOrder})";
                    using var image = EncoderSources.CreateImage(source, pixelFormat);
                    var data = Encode(image, encoder);
                    var reference = ReferenceBmp.Parse(data);
                    AssertDeclaredSizes(data, reference);
                    Assert.Equal(16, reference.BitsPerPixel);
                    Assert.Equal(40, reference.HeaderLength);
                    Assert.Equal(layout == BmpPixelLayout.Bgr565 ? 3u : 0u, reference.Compression);
                    Assert.False(reference.HasAlphaMask);
                    Assert.Equal(rowOrder == BmpRowOrder.TopDown, reference.IsTopDown);

                    // Each 8-bit sample is stored as round(value * (2^n - 1) / 255) and read back as round(level * 255 / (2^n - 1))
                    var samples = ToRgba8(source);
                    var builder = new RawPixelBufferBuilder(source.Width, source.Height, RawPixelLayout.Rgba8);
                    for (var y = 0; y < source.Height; y++)
                    {
                        for (var x = 0; x < source.Width; x++)
                        {
                            for (var c = 0; c < 3; c++)
                            {
                                var maximum = c == 1 && layout == BmpPixelLayout.Bgr565 ? 63 : 31;
                                var level = ((samples.GetSample(x, y, c) * maximum) + 127) / 255;
                                builder.SetSample(x, y, c, ((level * 255) + (maximum / 2)) / maximum);
                            }

                            builder.SetSample(x, y, 3, byte.MaxValue);
                        }
                    }

                    var expected = builder.Build();
                    AssertExact(expected, RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba8, reference.Rgba.Span), context);
                    AssertLibraryReadsBack(data, expected, context);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(OpaqueCases))]
    public void IndexedLayoutsAreExactWhenTheColorsFitAndMapToTheNearestEntryOtherwise(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var (layout, bitsPerPixel) in new[] { (BmpPixelLayout.Indexed1, 1), (BmpPixelLayout.Indexed4, 4), (BmpPixelLayout.Indexed8, 8) })
        {
            foreach (var (compression, rowOrder) in new[] { (BmpCompression.None, BmpRowOrder.BottomUp), (BmpCompression.None, BmpRowOrder.TopDown), (BmpCompression.RunLength, BmpRowOrder.BottomUp) })
            {
                if (compression == BmpCompression.RunLength && bitsPerPixel == 1)
                    continue;

                foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
                {
                    var context = $"{id} {name} as {pixelFormat} ({layout}, {compression}, {rowOrder})";
                    var expected = ToRgba8(source);
                    var colors = CountColors(expected);
                    var capacity = 1 << bitsPerPixel;
                    using var image = EncoderSources.CreateImage(source, pixelFormat);
                    var strict = new BmpEncoder { PixelLayout = layout, Compression = compression, RowOrder = rowOrder, AllowBitDepthReduction = true, MetadataHandling = MetadataHandling.Strip };
                    if (colors > capacity)
                    {
                        // Colors are never reduced silently
                        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Encode(image, strict));
                        Assert.Equal("Color reduction", exception.Feature);
                    }

                    var data = Encode(image, new BmpEncoder { PixelLayout = layout, Compression = compression, RowOrder = rowOrder, AllowBitDepthReduction = true, AllowColorReduction = true, MetadataHandling = MetadataHandling.Strip });
                    var reference = ReferenceBmp.Parse(data);
                    AssertDeclaredSizes(data, reference);
                    Assert.Equal((source.Width, source.Height), (reference.Width, reference.Height));
                    Assert.Equal(bitsPerPixel, reference.BitsPerPixel);
                    Assert.Equal(40, reference.HeaderLength);
                    Assert.Equal(compression == BmpCompression.None ? 0u : bitsPerPixel == 8 ? 1u : 2u, reference.Compression);
                    Assert.Equal(rowOrder == BmpRowOrder.TopDown, reference.IsTopDown);
                    var actual = RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba8, reference.Rgba.Span);
                    if (colors <= capacity)
                    {
                        // The palette is the colors of the image, and allowing color reduction changes nothing
                        Assert.Equal(colors, reference.PaletteEntries);
                        AssertExact(expected, actual, context);
                        Assert.Equal(data, Encode(image, strict));
                    }
                    else
                    {
                        // A reduced palette may hold fewer entries than the layout allows (the quantizer merges close colors)
                        Assert.InRange(reference.PaletteEntries, 1, capacity);
                        AssertExact(MapToNearest(expected, reference.Palette.Span), actual, context);
                    }

                    AssertLibraryReadsBack(data, actual, context);
                }
            }
        }
    }

    /// <summary>The declared file size, pixel-data offset and image size are the bytes written.</summary>
    private static void AssertDeclaredSizes(byte[] data, ReferenceBmp reference)
    {
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(10));
        Assert.Equal((uint)data.Length, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(2)));
        Assert.Equal((uint)data.Length, offset + (uint)reference.PixelDataLength);
        Assert.Equal((uint)reference.PixelDataLength, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(34)));
    }

    private static int CountColors(RawPixelBuffer pixels)
    {
        var colors = new HashSet<int>();
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                colors.Add((pixels.GetSample(x, y, 0) << 16) | (pixels.GetSample(x, y, 1) << 8) | pixels.GetSample(x, y, 2));
            }
        }

        return colors.Count;
    }

    /// <summary>Replaces every pixel by the palette entry at the smallest squared RGB distance (the first one on a tie).</summary>
    private static RawPixelBuffer MapToNearest(RawPixelBuffer pixels, ReadOnlySpan<byte> palette)
    {
        var builder = new RawPixelBufferBuilder(pixels.Width, pixels.Height, RawPixelLayout.Rgba8);
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                var best = 0;
                var bestDistance = int.MaxValue;
                for (var entry = 0; entry < palette.Length / 3; entry++)
                {
                    var distance = 0;
                    for (var c = 0; c < 3; c++)
                    {
                        var difference = pixels.GetSample(x, y, c) - palette[(entry * 3) + c];
                        distance += difference * difference;
                    }

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = entry;
                    }
                }

                for (var c = 0; c < 3; c++)
                {
                    builder.SetSample(x, y, c, palette[(best * 3) + c]);
                }

                builder.SetSample(x, y, 3, byte.MaxValue);
            }
        }

        return builder.Build();
    }
}
