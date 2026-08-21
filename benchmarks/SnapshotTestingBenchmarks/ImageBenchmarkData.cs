using Meziantou.Framework.SnapshotTesting;

namespace SnapshotTestingBenchmarks;

internal static class ImageBenchmarkData
{
    public static Image CreateImage(int width, int height, int seed)
    {
        var pixels = new Argb[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var red = (byte)((x * 17 + y * 31 + seed) % 256);
                var green = (byte)((x * 7 + y * 13 + seed) % 256);
                var blue = (byte)((x * 3 + y * 5 + seed) % 256);
                pixels[y * width + x] = new Argb(255, red, green, blue);
            }
        }

        return Image.Create(width, height, pixels);
    }
}
