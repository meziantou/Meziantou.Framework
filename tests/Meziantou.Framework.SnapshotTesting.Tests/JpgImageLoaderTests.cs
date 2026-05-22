namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class JpgImageLoaderTests
{
    [Theory]
    [InlineData("grayscale-baseline")]
    [InlineData("ycbcr-444-baseline")]
    [InlineData("ycbcr-420-baseline")]
    public async Task Image_LoadAsync_JpegAndConvertedPng_AreIdentical(string scenario)
    {
        var jpegPath = ImageTestData.GetImageFixturePath(scenario + ".jpg");
        var pngPath = ImageTestData.GetImageFixturePath(scenario + ".from-jpg.png");

        var jpegImage = await Image.LoadAsync(jpegPath);
        var pngImage = await Image.LoadAsync(pngPath);

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
