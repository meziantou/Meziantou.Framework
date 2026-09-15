using System.Buffers.Binary;

namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class JpgImageLoaderTests
{
    // The .from-jpg.png files are the output of libjpeg-turbo's djpeg with its default settings (accurate
    // integer IDCT, fancy upsampling), which is also what Pillow and browsers produce.
    [Theory]
    [InlineData("grayscale-baseline")]
    [InlineData("ycbcr-444-baseline")]
    [InlineData("ycbcr-420-baseline")]
    [InlineData("grayscale-sampling-2x2")]
    [InlineData("grayscale-sampling-1x2")]
    [InlineData("ycbcr-422-odd-size")]
    [InlineData("ycbcr-420-odd-size")]
    [InlineData("ycbcr-411-baseline")]
    [InlineData("ycbcr-440-baseline")]
    [InlineData("ycbcr-420-restart-interval")]
    [InlineData("ycbcr-420-16bit-quantization-tables")]
    [InlineData("ycbcr-420-non-interleaved-scans")]
    [InlineData("ycbcr-420-extended-sequential")]
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
    public async Task Image_Load_AcceptsAMissingEndOfImageMarker()
    {
        var data = ImageTestData.ReadImageFixture("ycbcr-420-baseline.jpg");
        Assert.Equal((byte)0xD9, data[^1]);

        var image = Image.Load(data.AsSpan(0, data.Length - 2));

        Assert.Equal(await ImageTestData.LoadImageFixtureAsync("ycbcr-420-baseline.from-jpg.png"), image);
    }

    [Fact]
    public void Image_Load_ThrowsWhenTheScanDataIsTruncated()
    {
        var data = ImageTestData.ReadImageFixture("ycbcr-420-odd-size.jpg");

        Assert.Throws<InvalidDataException>(() => Image.Load(data.AsSpan(0, data.Length * 2 / 3)));
    }

    [Theory]
    [InlineData(65534, 32769)]
    [InlineData(16000, 16000)]
    public void Image_Load_RejectsAHugeFrameHeaderWithoutAllocatingThePixels(int width, int height)
    {
        // 65534x32769 exceeds the pixel limit. 16000x16000 is within it, but a few hundred bytes of scan data
        // cannot code that many blocks.
        var data = ImageTestData.ReadImageFixture("grayscale-baseline.jpg");
        ReadOnlySpan<byte> startOfFrameMarker = [0xFF, 0xC0];
        var frameHeaderOffset = data.AsSpan().IndexOf(startOfFrameMarker);
        Assert.True(frameHeaderOffset > 0);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(frameHeaderOffset + 5), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(frameHeaderOffset + 7), (ushort)width);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => Image.Load(data));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(allocated < 1024 * 1024, $"Decoding allocated {allocated} bytes.");
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
