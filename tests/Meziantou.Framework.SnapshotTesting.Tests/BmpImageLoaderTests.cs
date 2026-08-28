using System.Buffers.Binary;

namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class BmpImageLoaderTests
{
    [Fact]
    public async Task Image_LoadAsync_Stream_DecodesBmpPixels()
    {
        var imageData = ImageTestData.CreateBmp24(
            width: 2,
            height: 1,
            pixels:
            [
                0xFFFF0000u,
                0xFF00FF00u,
            ],
            pixelsPerMeter: 2835);

        using var stream = new MemoryStream(imageData);
        var image = await Image.LoadAsync(stream);

        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(
        [
            new Argb(0xFFFF0000u),
            new Argb(0xFF00FF00u),
        ], image.Pixels.ToArray());
    }

    [Fact]
    public async Task Image_LoadAsync_Path_DecodesBmpPixels()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory / "sample.bmp";
        var imageData = ImageTestData.CreateBmp24(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF112233u,
            ],
            pixelsPerMeter: 2835);

        File.WriteAllBytes(path, imageData);
        var image = await Image.LoadAsync(path.Value);

        Assert.Equal(1, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal([new Argb(0xFF112233u)], image.Pixels.ToArray());
    }

    [Theory]
    [InlineData("bmp-rgb24-baseline")]
    [InlineData("bmp-rgba32-baseline")]
    public async Task Image_LoadAsync_BmpAndConvertedPng_AreIdentical(string scenario)
    {
        var bmpImage = await ImageTestData.LoadImageFixtureAsync(scenario + ".bmp");
        var pngImage = await ImageTestData.LoadImageFixtureAsync(scenario + ".from-bmp.png");

        Assert.Equal(bmpImage.Width, pngImage.Width);
        Assert.Equal(bmpImage.Height, pngImage.Height);
        Assert.Equal(bmpImage.Pixels.ToArray(), pngImage.Pixels.ToArray());
        Assert.Equal(bmpImage, pngImage);
    }

    [Fact]
    public void Image_Load_ReportsInvalidDataForAnOutOfRangePixelDataOffset()
    {
        var imageData = ImageTestData.CreateBmp24(width: 1, height: 1, pixels: [0xFF112233u], pixelsPerMeter: 2835);

        // The pixel data offset is a raw uint in the file header; the loader narrows it with a checked cast,
        // which would otherwise surface as OverflowException.
        BinaryPrimitives.WriteUInt32LittleEndian(imageData.AsSpan(10), uint.MaxValue);

        var exception = Assert.Throws<InvalidDataException>(() => Image.Load(imageData));
        Assert.IsType<OverflowException>(exception.InnerException);
    }

    [Fact]
    public void ImageComparer_ReportsADifferenceForCorruptImageData()
    {
        var expected = ImageTestData.CreateBmp24(width: 1, height: 1, pixels: [0xFF112233u], pixelsPerMeter: 2835);
        var actual = ImageTestData.CreateBmp24(width: 1, height: 1, pixels: [0xFF112233u], pixelsPerMeter: 2835);
        BinaryPrimitives.WriteUInt32LittleEndian(actual.AsSpan(10), uint.MaxValue);

        var equal = ImageComparer.Instance.Equals(new SnapshotData("bmp", expected), new SnapshotData("bmp", actual));

        Assert.False(equal);
    }
}
