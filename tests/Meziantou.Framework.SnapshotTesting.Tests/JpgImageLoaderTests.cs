namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class JpgImageLoaderTests
{
    [Theory]
    [InlineData("grayscale-baseline")]
    [InlineData("ycbcr-444-baseline")]
    [InlineData("ycbcr-420-baseline")]
    public async Task Image_LoadAsync_JpegAndConvertedPng_AreIdentical(string scenario)
    {
        var jpegImage = await ImageTestData.LoadImageFixtureAsync(scenario + ".jpg");
        var pngImage = await ImageTestData.LoadImageFixtureAsync(scenario + ".from-jpg.png");

        Assert.Equal(jpegImage.Width, pngImage.Width);
        Assert.Equal(jpegImage.Height, pngImage.Height);
        Assert.Equal(jpegImage.Pixels.ToArray(), pngImage.Pixels.ToArray());
        Assert.Equal(jpegImage, pngImage);
    }

    [Fact]
    public async Task Image_LoadAsync_ThrowsWhenJpegIsProgressive()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => Image.LoadAsync(new MemoryStream(ImageTestData.CreateJpegProgressive())));
    }

    [Fact]
    public async Task Image_LoadAsync_ThrowsWhenJpegUsesCmyk()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => Image.LoadAsync(new MemoryStream(ImageTestData.CreateJpegCmyk())));
    }
}
