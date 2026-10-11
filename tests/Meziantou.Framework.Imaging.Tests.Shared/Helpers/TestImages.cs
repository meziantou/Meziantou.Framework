namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Small images with distinct pixels, and their comparison, for the tests of the multi-image formats.</summary>
internal static class TestImages
{
    public static Image<Rgba32> CreateRgba(int width, int height)
    {
        var pixels = new Rgba32[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32((byte)(i * 7), (byte)(i * 13), (byte)(i * 29), (byte)(200 - (i % 200)));
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    public static void AssertSamePixels<TPixel>(Image<TPixel> expected, Image<TPixel> actual)
        where TPixel : unmanaged
    {
        Assert.Equal(expected.Size, actual.Size);
        var expectedPixels = new TPixel[expected.Width * expected.Height];
        var actualPixels = new TPixel[expected.Width * expected.Height];
        expected.Frames[0].CopyPixelDataTo(expectedPixels);
        actual.Frames[0].CopyPixelDataTo(actualPixels);
        Assert.Equal(expectedPixels, actualPixels);
    }
}
