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
}
