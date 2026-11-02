using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>
/// The halftone of the graphics state (ISO 32000-2, 10.6.5), for the red, green and blue components of the device the
/// page is rendered on: a type 5 halftone gives each its own screen, the other types one screen for the three.
/// </summary>
/// <remarks>
/// The halftone is read for a page, at the resolution of its device: the images of its thresholds, at the size of the
/// device, are kept until the page is rendered.
/// </remarks>
internal sealed class RenderHalftone(HalftoneComponent red, HalftoneComponent green, HalftoneComponent blue) : IDisposable
{
    private readonly Dictionary<(int X, int Y), Image<Rgba64>> _images = [];

    public HalftoneComponent Red { get; } = red;

    public HalftoneComponent Green { get; } = green;

    public HalftoneComponent Blue { get; } = blue;

    /// <summary>
    /// Gets the image of the thresholds of device space from its origin, in the red, green and blue channels: as large as
    /// a period of the three screens, which a brush repeats, or as the device when a period is larger.
    /// </summary>
    /// <param name="origin">The halftone origin, in pixels of the device (<c>/HTO</c>).</param>
    /// <param name="deviceSize">The size of the device, in pixels.</param>
    /// <param name="cache">The cache of the rendering, charged with the pixels of a new image.</param>
    public Image<Rgba64> GetThresholdImage((int X, int Y) origin, (int Width, int Height) deviceSize, RenderCache cache)
    {
        if (_images.TryGetValue(origin, out var image))
            return image;

        var width = (int)GetPeriod(Red.Screen.PeriodX, Green.Screen.PeriodX, Blue.Screen.PeriodX, Math.Max(1, deviceSize.Width));
        var height = (int)GetPeriod(Red.Screen.PeriodY, Green.Screen.PeriodY, Blue.Screen.PeriodY, Math.Max(1, deviceSize.Height));
        cache.Charge(((long)width * height / 256) + 1);
        image = new Image<Rgba64>(width, height, cache.Options.Configuration);
        try
        {
            image.Frames[0].ProcessPixelRows(rows =>
            {
                for (var y = 0; y < rows.Height; y++)
                {
                    var row = rows.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        long dx = x - origin.X;
                        long dy = y - origin.Y;
                        row[x] = new Rgba64(Red.Screen.GetThreshold(dx, dy), Green.Screen.GetThreshold(dx, dy), Blue.Screen.GetThreshold(dx, dy));
                    }
                }
            });
        }
        catch
        {
            image.Dispose();
            throw;
        }

        _images[origin] = image;
        return image;
    }

    public void Dispose()
    {
        foreach (var image in _images.Values)
        {
            image.Dispose();
        }

        _images.Clear();
    }

    /// <summary>Gets the least common multiple of the periods, or the size of the device when it is larger: the image then covers the device and is never repeated.</summary>
    private static long GetPeriod(long red, long green, long blue, long deviceSize)
    {
        var period = red;
        foreach (var other in (ReadOnlySpan<long>)[green, blue])
        {
            if (period >= deviceSize)
                break;

            var a = period;
            var b = other;
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }

            period = period / a * other;
        }

        return Math.Min(period, deviceSize);
    }
}
