namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class PngImageLoaderTests
{
    [Fact]
    public async Task Image_LoadAsync_Stream_DecodesPngPixels()
    {
        var imageData = ImageTestData.CreatePngRgba32(
            width: 2,
            height: 1,
            pixels:
            [
                0xFFFF0000u,
                0x800000FFu,
            ]);

        using var stream = new MemoryStream(imageData);
        var image = await Image.LoadAsync(stream);

        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(
        [
            new Argb(0xFFFF0000u),
            new Argb(0x800000FFu),
        ], image.Pixels.ToArray());
    }

    [Fact]
    public void Load_Grayscale8_RepeatsTheSampleOnEveryChannel()
    {
        var image = Image.Load(ImageTestData.CreatePng(width: 2, height: 1, bitDepth: 8, colorType: 0, samples: [0x10, 0x20]));

        Assert.Equal([new Argb(0xFF101010u), new Argb(0xFF202020u)], image.Pixels.ToArray());
    }

    [Fact]
    public void Load_Grayscale8_WithTransparency_MarksTheTransparentSample()
    {
        var image = Image.Load(ImageTestData.CreatePng(width: 2, height: 1, bitDepth: 8, colorType: 0, samples: [0x10, 0x20], transparency: [0x00, 0x20]));

        Assert.Equal([new Argb(0xFF101010u), new Argb(0x00202020u)], image.Pixels.ToArray());
    }

    [Fact]
    public void Load_Rgb8_DecodesOpaquePixels()
    {
        var image = Image.Load(ImageTestData.CreatePng(width: 2, height: 1, bitDepth: 8, colorType: 2, samples: [0x10, 0x20, 0x30, 0x40, 0x50, 0x60]));

        Assert.Equal([new Argb(0xFF102030u), new Argb(0xFF405060u)], image.Pixels.ToArray());
    }

    [Fact]
    public void Load_Rgb8_WithTransparency_MarksTheTransparentColor()
    {
        var image = Image.Load(ImageTestData.CreatePng(
            width: 2,
            height: 1,
            bitDepth: 8,
            colorType: 2,
            samples: [0x10, 0x20, 0x30, 0x40, 0x50, 0x60],
            transparency: [0x00, 0x10, 0x00, 0x20, 0x00, 0x30]));

        Assert.Equal([new Argb(0x00102030u), new Argb(0xFF405060u)], image.Pixels.ToArray());
    }

    [Fact]
    public void Load_Indexed8_ResolvesThePalette()
    {
        var image = Image.Load(ImageTestData.CreatePng(
            width: 3,
            height: 1,
            bitDepth: 8,
            colorType: 3,
            samples: [0, 1, 2],
            palette: [0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF]));

        Assert.Equal([new Argb(0xFFFF0000u), new Argb(0xFF00FF00u), new Argb(0xFF0000FFu)], image.Pixels.ToArray());
    }

    [Fact]
    public void Load_Indexed8_WithTransparency_AppliesThePaletteAlpha()
    {
        var image = Image.Load(ImageTestData.CreatePng(
            width: 3,
            height: 1,
            bitDepth: 8,
            colorType: 3,
            samples: [0, 1, 2],
            palette: [0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF],
            transparency: [0x00, 0x80]));

        Assert.Equal([new Argb(0x00FF0000u), new Argb(0x8000FF00u), new Argb(0xFF0000FFu)], image.Pixels.ToArray());
    }

    [Fact]
    public void Load_Indexed8_ThrowsWhenThePaletteIndexIsOutOfRange()
    {
        var data = ImageTestData.CreatePng(width: 2, height: 1, bitDepth: 8, colorType: 3, samples: [0, 5], palette: [0xFF, 0x00, 0x00]);

        Assert.Throws<InvalidDataException>(() => Image.Load(data));
    }

    [Fact]
    public void Load_GrayscaleAlpha8_DecodesTheAlphaChannel()
    {
        var image = Image.Load(ImageTestData.CreatePng(width: 2, height: 1, bitDepth: 8, colorType: 4, samples: [0x10, 0xFF, 0x20, 0x80]));

        Assert.Equal([new Argb(0xFF101010u), new Argb(0x80202020u)], image.Pixels.ToArray());
    }

    [Fact]
    public void Load_StopsInflatingOnceTheImageDataIsLongerThanTheHeaderAllows()
    {
        // The header describes a 1x1 RGBA image (5 bytes of filtered image data) while the IDAT inflates to
        // 64 MiB of zeros. Reading the whole stream would materialize all of it before the size is checked.
        const int InflatedSize = 64 * 1024 * 1024;
        var imageData = ImageTestData.CreatePngWithRawImageData(width: 1, height: 1, bitDepth: 8, colorType: 6, new byte[InflatedSize]);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => Image.Load(imageData));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(allocated < 4 * 1024 * 1024, $"Decoding allocated {allocated} bytes for a 1x1 image.");
    }
}
