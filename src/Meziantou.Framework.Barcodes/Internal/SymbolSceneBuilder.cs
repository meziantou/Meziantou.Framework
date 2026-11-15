using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// Lays out a grid of dark and light modules, surrounded by a quiet zone, optionally followed by a line of text and
/// covered by a logo, as a <see cref="Scene"/>. Every renderer draws this scene, so they all draw the same thing.
/// </summary>
internal static class SymbolSceneBuilder
{
    private const string DefaultFontFamily = "sans-serif";

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

    /// <summary>Creates the scene of a symbol.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The size of the drawing is not a finite number.</exception>
    /// <exception cref="ArgumentException">The logo is not a PNG, JPEG, GIF or WebP file.</exception>
    public static Scene Create(in SymbolSceneContent content)
    {
        var modules = content.Modules;
        var rows = modules.GetLength(0);
        var columns = modules.GetLength(1);
        var symbolWidth = (columns + (2.0 * content.HorizontalQuietZoneModules)) * content.ModuleWidth;
        var symbolHeight = (rows + (2.0 * content.VerticalQuietZoneModules)) * content.ModuleHeight;

        var width = symbolWidth;
        var height = symbolHeight;
        var symbolLeft = 0d;
        var textLeft = 0d;
        var textTop = 0d;
        TextMetrics textMetrics = default;
        var text = content.Text;
        var hasText = text is not null && !string.IsNullOrEmpty(content.TextValue);
        if (hasText)
        {
            var measurer = new FontFaceTextMeasurer(text!.Fonts);
            textMetrics = measurer.Measure(content.TextValue!, CreateFont(text));

            // A text wider than the symbol widens the drawing; the narrower of the two is centered
            width = Math.Max(symbolWidth, textMetrics.Width);
            symbolLeft = Center(width - symbolWidth, content.SnapToPixels);
            textLeft = Center(width - textMetrics.Width, content.SnapToPixels);
            textTop = symbolHeight + text.Spacing;
            if (content.SnapToPixels)
            {
                textTop = Math.Round(textTop, MidpointRounding.AwayFromZero);
            }

            height = textTop + textMetrics.Height;
        }

        if (!double.IsFinite(width) || !double.IsFinite(height))
            throw new ArgumentOutOfRangeException(content.SizeParameterName, "The dimensions of the drawing are too large.");

        var scene = new Scene(width, height, hasText ? text!.Fonts.FontFamily : DefaultFontFamily);
        if (hasText)
        {
            scene.Fonts = text!.Fonts;
        }

        if (content.LightPaint is not null)
        {
            scene.Root.Add(new SceneRectangle(0, 0, width, height) { Fill = content.LightPaint });
        }

        var modulesLeft = symbolLeft + (content.HorizontalQuietZoneModules * content.ModuleWidth);
        var modulesTop = content.VerticalQuietZoneModules * content.ModuleHeight;
        var cleared = content.Logo?.ClearedModules;
        var path = CreateDarkModulesPath(modules, modulesLeft, modulesTop, content.ModuleWidth, content.ModuleHeight, cleared);
        if (path is not null)
        {
            scene.Root.Add(new ScenePath(path) { Fill = content.DarkPaint });
        }

        if (hasText)
        {
            var paint = text!.Paint ?? content.DarkPaint;
            scene.Root.Add(new SceneText(textLeft, textTop, content.TextValue!, CreateFont(text), paint is SolidPaint solid ? solid.Color : SceneColor.Black)
            {
                Baseline = SceneTextBaseline.TextTop,
                Fill = paint is SolidPaint ? null : paint,
                MeasuredWidth = textMetrics.Width,
            });
        }

        if (content.Logo is { } logo)
        {
            if (logo.BackdropPaint is not null && cleared is { } area)
            {
                var backdropWidth = area.ColumnCount * content.ModuleWidth;
                var backdropHeight = area.RowCount * content.ModuleHeight;
                var radius = Math.Min(logo.BackdropCornerRadius * Math.Min(content.ModuleWidth, content.ModuleHeight), Math.Min(backdropWidth, backdropHeight) / 2);
                scene.Root.Add(new SceneRectangle(modulesLeft + (area.Column * content.ModuleWidth), modulesTop + (area.Row * content.ModuleHeight), backdropWidth, backdropHeight)
                {
                    Fill = logo.BackdropPaint,
                    CornerRadius = radius,
                });
            }

            scene.Root.Add(new SceneImage(symbolLeft + logo.X, logo.Y, logo.Size, logo.Size, logo.Data) { Alignment = SceneAspectRatio.XMidYMid });
        }

        return scene;

        static double Center(double space, bool snapToPixels)
        {
            var offset = space / 2;
            return snapToPixels ? Math.Floor(offset) : offset;
        }
    }

    private static SceneFont CreateFont(BarcodeTextOptions text) => new(text.FontSize, Family: text.Fonts.DefaultFamily);

    // One path holds every dark module, so the edges of adjacent modules are painted as one shape, without seams. The
    // modules of the cleared area are not drawn.
    private static PathData? CreateDarkModulesPath(bool[,] modules, double left, double top, double moduleWidth, double moduleHeight, ModuleRectangle? cleared)
    {
        var rows = modules.GetLength(0);
        var columns = modules.GetLength(1);
        var builder = new PathBuilder();
        var hasDarkModule = false;
        var row = 0;
        while (row < rows)
        {
            // Identical consecutive rows (the rows of PDF417, of stacked barcodes) are one rectangle per run, which stops at
            // the edges of the cleared area
            var rowCount = CountIdenticalRows(modules, row);
            var clearedStart = 0;
            var clearedEnd = 0;
            if (cleared is { } area)
            {
                if (row < area.Row)
                {
                    rowCount = Math.Min(rowCount, area.Row - row);
                }
                else if (row < area.EndRow)
                {
                    rowCount = Math.Min(rowCount, area.EndRow - row);
                    clearedStart = area.Column;
                    clearedEnd = area.EndColumn;
                }
            }

            var y = top + (row * moduleHeight);
            var height = rowCount * moduleHeight;
            var column = 0;
            while (column < columns)
            {
                if (!IsDrawn(modules, row, column, clearedStart, clearedEnd))
                {
                    column++;
                    continue;
                }

                var start = column;
                while (column < columns && IsDrawn(modules, row, column, clearedStart, clearedEnd))
                {
                    column++;
                }

                builder.AddRectangle(left + (start * moduleWidth), y, (column - start) * moduleWidth, height);
                hasDarkModule = true;
            }

            row += rowCount;
        }

        return hasDarkModule ? builder.Build() : null;

        static bool IsDrawn(bool[,] modules, int row, int column, int clearedStart, int clearedEnd) => modules[row, column] && (column < clearedStart || column >= clearedEnd);
    }

    /// <summary>Gets the number of rows, starting at <paramref name="row"/>, that have the same modules.</summary>
    public static int CountIdenticalRows(bool[,] modules, int row)
    {
        var rows = modules.GetLength(0);
        var count = 1;
        while (row + count < rows && AreRowsEqual(modules, row, row + count))
        {
            count++;
        }

        return count;
    }

    private static bool AreRowsEqual(bool[,] modules, int row1, int row2)
    {
        var columns = modules.GetLength(1);
        for (var column = 0; column < columns; column++)
        {
            if (modules[row1, column] != modules[row2, column])
                return false;
        }

        return true;
    }
}
