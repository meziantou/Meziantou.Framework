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

    [Theory]
    [InlineData("external-progressive-huffman")]
    [InlineData("exif-orientation-2")]
    [InlineData("exif-orientation-6")]
    public async Task Image_LoadAsync_JpegAndConvertedPng_AreClose(string scenario)
    {
        var jpegImage = await ImageTestData.LoadImageFixtureAsync(scenario + ".jpg");
        var pngImage = await ImageTestData.LoadImageFixtureAsync(scenario + ".from-jpg.png");

        Assert.Equal(jpegImage.Width, pngImage.Width);
        Assert.Equal(jpegImage.Height, pngImage.Height);
        AssertImagePixelsAreSimilar(jpegImage.Pixels.Span, pngImage.Pixels.Span);
    }

    [Fact]
    public async Task Image_LoadAsync_ArithmeticJpeg_LoadsSuccessfully()
    {
        var image = await ImageTestData.LoadImageFixtureAsync("external-arithmetic-sequential.jpg");

        Assert.Equal(234, image.Width);
        Assert.Equal(213, image.Height);
    }

    [Fact]
    public async Task Image_LoadAsync_ThrowsWhenJpegUsesCmyk()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => Image.LoadAsync(new MemoryStream(ImageTestData.CreateJpegCmyk())));
    }

    private static void AssertImagePixelsAreSimilar(ReadOnlySpan<Argb> expected, ReadOnlySpan<Argb> actual)
    {
        Assert.Equal(expected.Length, actual.Length);

        var totalChannelDelta = 0L;
        var outlierChannelCount = 0;
        var maxChannelDelta = 0;
        var channelCount = checked(expected.Length * 3);

        for (var i = 0; i < expected.Length; i++)
        {
            var expectedPixel = expected[i];
            var actualPixel = actual[i];

            Assert.Equal(expectedPixel.A, actualPixel.A);

            AddChannelDelta(expectedPixel.R, actualPixel.R, ref totalChannelDelta, ref outlierChannelCount, ref maxChannelDelta);
            AddChannelDelta(expectedPixel.G, actualPixel.G, ref totalChannelDelta, ref outlierChannelCount, ref maxChannelDelta);
            AddChannelDelta(expectedPixel.B, actualPixel.B, ref totalChannelDelta, ref outlierChannelCount, ref maxChannelDelta);
        }

        var averageChannelDelta = (double)totalChannelDelta / channelCount;
        Assert.True(
            averageChannelDelta <= 2.0,
            $"Average RGB channel delta {averageChannelDelta:F3} is greater than 2.0 (max delta: {maxChannelDelta}, outliers: {outlierChannelCount}).");
    }

    private static void AddChannelDelta(byte expected, byte actual, ref long totalChannelDelta, ref int outlierChannelCount, ref int maxChannelDelta)
    {
        var delta = Math.Abs(expected - actual);
        totalChannelDelta += delta;
        if (delta > 10)
            outlierChannelCount++;

        if (delta > maxChannelDelta)
            maxChannelDelta = delta;
    }
}
