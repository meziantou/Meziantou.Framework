using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>
/// Measures and draws a grid of dark and light modules, surrounded by a quiet zone and optionally followed by a line of
/// text, on a <see cref="DrawingCanvas"/>.
/// </summary>
internal static class ModuleCanvasRenderer
{
    /// <summary>Validates a module size and rounds it when snapping.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The size is not a positive finite number.</exception>
    public static double GetModuleSize(double value, bool snapToPixels, string parameterName)
    {
        ThrowIfNotPositiveFinite(value, parameterName);
        return snapToPixels ? Math.Max(1, Math.Round(value, MidpointRounding.AwayFromZero)) : value;
    }

    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    public static void ThrowIfNotPositiveFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be a positive finite number.");
    }

    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not a finite number.</exception>
    public static void ThrowIfNegativeOrNotFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be zero or a positive finite number.");
    }

    /// <summary>Computes where the parts of a drawing go, relative to its top-left corner.</summary>
    public static ModuleCanvasLayout GetLayout(int columns, int rows, double moduleWidth, double moduleHeight, int horizontalQuietZoneModules, int verticalQuietZoneModules, TextLayout? text, double textSpacing, bool snapToPixels)
    {
        var symbolWidth = (columns + (2.0 * horizontalQuietZoneModules)) * moduleWidth;
        var symbolHeight = (rows + (2.0 * verticalQuietZoneModules)) * moduleHeight;
        if (text is null)
            return new ModuleCanvasLayout(symbolWidth, symbolHeight, SymbolLeft: 0, symbolWidth, symbolHeight, TextLeft: 0, TextTop: 0);

        // A text wider than the symbol widens the drawing; the narrower of the two is centered
        var width = Math.Max(symbolWidth, text.Width);
        var symbolLeft = Center(width - symbolWidth, snapToPixels);
        var textLeft = Center(width - text.Width, snapToPixels);
        var textTop = symbolHeight + textSpacing;
        if (snapToPixels)
        {
            textTop = Math.Round(textTop, MidpointRounding.AwayFromZero);
        }

        return new ModuleCanvasLayout(width, textTop + text.Height, symbolLeft, symbolWidth, symbolHeight, textLeft, textTop);

        static double Center(double space, bool snapToPixels)
        {
            var offset = space / 2;
            return snapToPixels ? Math.Floor(offset) : offset;
        }
    }

    /// <summary>Renders a drawing as an image of its size, rounded up.</summary>
    /// <param name="content">What the drawing paints.</param>
    /// <param name="layout">Where its parts go.</param>
    /// <param name="parameterName">The option to blame when the image is too large.</param>
    /// <exception cref="ArgumentOutOfRangeException">The size of the image does not fit in an <see cref="int"/>.</exception>
    public static Image<Rgba32> Render(ModuleCanvasContent content, ModuleCanvasLayout layout, string parameterName)
    {
        var width = Math.Ceiling(layout.Width);
        var height = Math.Ceiling(layout.Height);
        if (width > int.MaxValue || height > int.MaxValue)
            throw new ArgumentOutOfRangeException(parameterName, "The output image dimensions are too large.");

        var image = new Image<Rgba32>((int)width, (int)height, Colors.Transparent);
        try
        {
            image.Draw((content, layout), static (canvas, state) => Draw(canvas, default, state.layout, state.content));
        }
        catch
        {
            image.Dispose();
            throw;
        }

        return image;
    }

    /// <summary>Draws the modules, the quiet zone and the text at a position.</summary>
    /// <returns>The rectangle the drawing covers.</returns>
    public static RectangleD Draw(DrawingCanvas canvas, PointD position, ModuleCanvasLayout layout, ModuleCanvasContent content)
    {
        var left = position.X;
        var top = position.Y;
        if (content.SnapToPixels)
        {
            left = Math.Round(left, MidpointRounding.AwayFromZero);
            top = Math.Round(top, MidpointRounding.AwayFromZero);
        }

        var bounds = new RectangleD(left, top, layout.Width, layout.Height);
        if (content.LightBrush is not null)
        {
            canvas.FillRectangle(content.LightBrush, bounds);
        }

        var path = CreateDarkModulesPath(
            content,
            left + layout.SymbolLeft + (content.HorizontalQuietZoneModules * content.ModuleWidth),
            top + (content.VerticalQuietZoneModules * content.ModuleHeight));
        if (path is not null)
        {
            canvas.Fill(content.DarkBrush, path);
        }

        if (content.Text is not null)
        {
            canvas.FillText(content.TextBrush ?? content.DarkBrush, content.Text, new PointD(left + layout.TextLeft, top + layout.TextTop));
        }

        return bounds;
    }

    // One path holds every dark module, so the edges of adjacent modules are painted as one shape, without seams
    private static VectorPath? CreateDarkModulesPath(ModuleCanvasContent content, double left, double top)
    {
        var builder = new VectorPathBuilder();
        var hasDarkModule = false;
        var row = 0;
        while (row < content.Rows)
        {
            // Identical consecutive rows (every row of a linear barcode) are one rectangle per run
            var firstRow = row;
            while (row + 1 < content.Rows && AreRowsEqual(content, row, row + 1))
            {
                row++;
            }

            var y = top + (firstRow * content.ModuleHeight);
            var height = (row - firstRow + 1) * content.ModuleHeight;
            var column = 0;
            while (column < content.Columns)
            {
                if (!content.IsDark(firstRow, column))
                {
                    column++;
                    continue;
                }

                var start = column;
                while (column < content.Columns && content.IsDark(firstRow, column))
                {
                    column++;
                }

                builder.AddRectangle(new RectangleD(left + (start * content.ModuleWidth), y, (column - start) * content.ModuleWidth, height));
                hasDarkModule = true;
            }

            row++;
        }

        return hasDarkModule ? builder.Build() : null;
    }

    private static bool AreRowsEqual(ModuleCanvasContent content, int row1, int row2)
    {
        for (var column = 0; column < content.Columns; column++)
        {
            if (content.IsDark(row1, column) != content.IsDark(row2, column))
                return false;
        }

        return true;
    }
}
