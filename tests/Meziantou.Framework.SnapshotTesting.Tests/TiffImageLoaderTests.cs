namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class TiffImageLoaderTests
{
    [Theory]
    [InlineData("tiff-rgb24-none")]
    [InlineData("tiff-rgb24-packbits")]
    [InlineData("tiff-rgb24-lzw")]
    public async Task Image_LoadAsync_TiffAndConvertedPng_AreIdentical(string scenario)
    {
        var tiffImage = await ImageTestData.LoadImageFixtureAsync(scenario + ".tiff");
        var pngImage = await ImageTestData.LoadImageFixtureAsync(scenario + ".from-tiff.png");

        Assert.Equal(tiffImage.Width, pngImage.Width);
        Assert.Equal(tiffImage.Height, pngImage.Height);
        Assert.Equal(tiffImage.Pixels.ToArray(), pngImage.Pixels.ToArray());
        Assert.Equal(tiffImage, pngImage);
    }

    [Fact]
    public async Task Image_LoadAsync_ThrowsWhenTiffIsTruncated()
    {
        var data = ImageTestData.ReadImageFixture("tiff-rgb24-none.tiff");
        var truncatedData = data[..^8];

        await Assert.ThrowsAsync<InvalidDataException>(() => Image.LoadAsync(new MemoryStream(truncatedData)));
    }

    [Fact]
    public async Task Image_LoadAsync_ThrowsWhenTiffIsBigTiff()
    {
        byte[] bigTiffHeader =
        [
            (byte)'I', (byte)'I',
            (byte)43, (byte)0,
            (byte)8, (byte)0,
            (byte)0, (byte)0,
        ];

        await Assert.ThrowsAsync<NotSupportedException>(() => Image.LoadAsync(new MemoryStream(bigTiffHeader)));
    }
}
