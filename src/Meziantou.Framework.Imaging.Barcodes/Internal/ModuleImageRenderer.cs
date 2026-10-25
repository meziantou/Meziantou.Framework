namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>
/// Renders a grid of dark and light modules, surrounded by a quiet zone, as an <see cref="Image{TPixel}"/>.
/// </summary>
internal static class ModuleImageRenderer
{
    /// <summary>Gets the size in pixels of one dimension of the image, quiet zone included.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The size does not fit in an <see cref="int"/>.</exception>
    public static int GetTotalDimension(int moduleCount, int quietZoneModules, int moduleSize, string parameterName)
    {
        var value = ((long)moduleCount + (2L * quietZoneModules)) * moduleSize;
        if (value > int.MaxValue)
            throw new ArgumentOutOfRangeException(parameterName, "The output image dimensions are too large.");

        return (int)value;
    }

    /// <summary>Creates the image of a module grid.</summary>
    /// <param name="grid">The modules and their layout in pixels.</param>
    /// <param name="width">The image width, from <see cref="GetTotalDimension"/>.</param>
    /// <param name="height">The image height, from <see cref="GetTotalDimension"/>.</param>
    /// <param name="lightColor">The color of light modules and of the quiet zone.</param>
    /// <param name="darkColor">The color of dark modules.</param>
    public static Image<Rgba32> Render(ModuleGrid grid, int width, int height, Rgba32 lightColor, Rgba32 darkColor)
    {
        // The quiet zone and the light modules are the fill color; only the dark modules are written
        var image = new Image<Rgba32>(width, height, lightColor);
        try
        {
            image.Frames[0].ProcessPixelRows((grid, darkColor), static (pixels, state) =>
            {
                var (grid, darkColor) = state;
                var left = grid.HorizontalQuietZoneModules * grid.ModuleWidth;
                for (var row = 0; row < grid.Rows; row++)
                {
                    var top = (grid.VerticalQuietZoneModules + row) * grid.ModuleHeight;
                    var pixelRow = pixels.GetRowSpan(top);

                    // Consecutive dark modules are filled as one run
                    var column = 0;
                    while (column < grid.Columns)
                    {
                        if (!grid.IsDark(row, column))
                        {
                            column++;
                            continue;
                        }

                        var start = column;
                        while (column < grid.Columns && grid.IsDark(row, column))
                        {
                            column++;
                        }

                        pixelRow.Slice(left + (start * grid.ModuleWidth), (column - start) * grid.ModuleWidth).Fill(darkColor);
                    }

                    // Every pixel row of a module row is identical
                    for (var y = 1; y < grid.ModuleHeight; y++)
                    {
                        pixelRow.CopyTo(pixels.GetRowSpan(top + y));
                    }
                }
            });
        }
        catch
        {
            image.Dispose();
            throw;
        }

        return image;
    }
}
