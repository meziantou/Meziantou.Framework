using System.Runtime.CompilerServices;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.WebPlatform.Imaging.Tests;

/// <summary>
/// Compares screenshots with the expected PNG files of the <c>Screenshots</c> directory of the project. A missing expected file
/// is created (and the test fails); set <c>SCREENSHOT_UPDATE_EXPECTATIONS=1</c> to replace the expected files with the actual
/// screenshots. The screenshots that differ are saved in the <c>screenshots-actual</c> directory of the test output.
/// </summary>
internal static class ScreenshotAssert
{
    /// <summary>
    /// Asserts that a screenshot matches its expected PNG: same size, and at most <paramref name="maxDifferentPixels"/> pixels
    /// whose components (not premultiplied) differ by more than <paramref name="maxChannelDifference"/> (antialiasing may differ
    /// at the rounding of the floating-point operations).
    /// </summary>
    public static void Matches(Image<Rgba32> actual, string name, int maxChannelDifference = 8, int maxDifferentPixels = 0)
    {
        var expectedPath = Path.Combine(GetProjectDirectory(), "Screenshots", name + ".png");
        if (Environment.GetEnvironmentVariable("SCREENSHOT_UPDATE_EXPECTATIONS") == "1")
        {
            Save(actual, expectedPath);
            return;
        }

        if (!File.Exists(expectedPath))
        {
            Save(actual, expectedPath);
            Assert.Fail($"The expected screenshot '{expectedPath}' did not exist: it was created from the actual screenshot, check it.");
        }

        using var expected = Image.Load<Rgba32>(expectedPath);
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            var actualPath = SaveActual(actual, name);
            Assert.Fail($"The screenshot '{name}' is {actual.Width}x{actual.Height} instead of {expected.Width}x{expected.Height}: see '{actualPath}'.");
        }

        var expectedPixels = GetPixels(expected);
        var actualPixels = GetPixels(actual);
        var differentPixels = 0;
        var maxDifference = 0;
        for (var i = 0; i < expectedPixels.Length; i += 4)
        {
            var difference = 0;
            for (var channel = 0; channel < 4; channel++)
            {
                difference = Math.Max(difference, Math.Abs(expectedPixels[i + channel] - actualPixels[i + channel]));
            }

            if (difference > maxChannelDifference)
            {
                differentPixels++;
                maxDifference = Math.Max(maxDifference, difference);
            }
        }

        if (differentPixels > maxDifferentPixels)
        {
            var actualPath = SaveActual(actual, name);
            Assert.Fail($"The screenshot '{name}' has {differentPixels} pixels that differ from the expected screenshot (by up to {maxDifference}): see '{actualPath}'.");
        }
    }

    /// <summary>Returns the pixels of an image as RGBA, not premultiplied.</summary>
    public static byte[] GetPixels(Image<Rgba32> image)
    {
        var pixels = new byte[image.Width * image.Height * 4];
        image.Frames[0].CopyPixelBytesTo(pixels, image.Width * 4);
        return pixels;
    }

    /// <summary>Returns the color of a pixel (RGBA, not premultiplied).</summary>
    public static Rgba32 GetPixel(Image<Rgba32> image, int x, int y) => image.GetPixel(x, y);

    private static string SaveActual(Image<Rgba32> actual, string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "screenshots-actual", name + ".png");
        Save(actual, path);
        return path;
    }

    private static void Save(Image<Rgba32> image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        image.Save(path, new Meziantou.Framework.Imaging.Formats.PngEncoder());
    }

    private static string GetProjectDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}

/// <summary>The colors of the pixels of the tests (not premultiplied).</summary>
internal static class Colors
{
    public static Rgba32 Red => new(255, 0, 0, 255);

    public static Rgba32 Lime => new(0, 255, 0, 255);

    public static Rgba32 Green => new(0, 128, 0, 255);

    public static Rgba32 Blue => new(0, 0, 255, 255);

    public static Rgba32 White => new(255, 255, 255, 255);

    public static Rgba32 Black => new(0, 0, 0, 255);

    public static Rgba32 Yellow => new(255, 255, 0, 255);

    public static Rgba32 Cyan => new(0, 255, 255, 255);

    public static Rgba32 Magenta => new(255, 0, 255, 255);

    public static Rgba32 Gray => new(128, 128, 128, 255);

    public static Rgba32 Orange => new(255, 165, 0, 255);

    public static Rgba32 Purple => new(128, 0, 128, 255);

    /// <summary>A pixel that nothing painted.</summary>
    public static Rgba32 Empty => default;
}

/// <summary>Reads the pixels of the screenshots.</summary>
internal static class ScreenshotExtensions
{
    /// <summary>Returns the color of a pixel (RGBA, not premultiplied).</summary>
    public static Rgba32 GetPixel(this Image<Rgba32> image, int x, int y) => image.Frames[0][x, y];
}
