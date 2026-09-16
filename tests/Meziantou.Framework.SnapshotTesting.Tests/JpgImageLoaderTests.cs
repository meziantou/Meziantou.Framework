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

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(1, 2, 3)]
    [InlineData(66, 71, 82)]
    public async Task Image_Load_AssumesYcbcrWithoutColorMarkerUnlessTheComponentsAreRgb(byte firstId, byte secondId, byte thirdId)
    {
        var data = RemoveJfifMarkerAndRenameComponents(ImageTestData.ReadImageFixture("ycbcr-444-baseline.jpg"), [firstId, secondId, thirdId]);

        Assert.Equal(await ImageTestData.LoadImageFixtureAsync("ycbcr-444-baseline.from-jpg.png"), Image.Load(data));
    }

    [Fact]
    public void Image_Load_ThrowsNotSupportedForRgbComponentsWithoutColorMarker()
    {
        var data = RemoveJfifMarkerAndRenameComponents(ImageTestData.ReadImageFixture("ycbcr-444-baseline.jpg"), [(byte)'R', (byte)'G', (byte)'B']);

        Assert.Throws<NotSupportedException>(() => Image.Load(data));
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

    /// <summary>
    /// Removes the JFIF APP0 segment of a 3-component JPEG, and renames its components in the frame header and in
    /// the scan headers, so the decoder can only infer the color model from the component identifiers.
    /// </summary>
    private static byte[] RemoveJfifMarkerAndRenameComponents(byte[] data, byte[] componentIds)
    {
        const byte App0Marker = 0xE0;
        const byte StartOfFrameMarker = 0xC0;
        const byte StartOfScanMarker = 0xDA;

        var result = new List<byte>(data.Length) { data[0], data[1] };
        byte[] originalIds = [];
        var offset = 2;
        while (offset < data.Length)
        {
            Assert.Equal(0xFF, data[offset]);
            var marker = data[offset + 1];
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 2));
            var segment = data.AsSpan(offset, 2 + segmentLength).ToArray();
            offset += segment.Length;

            switch (marker)
            {
                case App0Marker:
                    continue;

                case StartOfFrameMarker:
                    Assert.Equal(3, segment[9]);
                    originalIds = [segment[10], segment[13], segment[16]];
                    segment[10] = componentIds[0];
                    segment[13] = componentIds[1];
                    segment[16] = componentIds[2];
                    break;

                case StartOfScanMarker:
                    for (var i = 0; i < segment[4]; i++)
                    {
                        var idOffset = 5 + (i * 2);
                        segment[idOffset] = componentIds[Array.IndexOf(originalIds, segment[idOffset])];
                    }

                    // The entropy-coded data and everything after it are kept as they are
                    result.AddRange(segment);
                    result.AddRange(data.AsSpan(offset));
                    return [.. result];
            }

            result.AddRange(segment);
        }

        throw new InvalidOperationException("The JPEG has no scan.");
    }
}
