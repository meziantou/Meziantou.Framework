using Meziantou.Framework.Barcodes.Internal.Linear;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// Lays out an EAN or UPC symbol with its human-readable digits as the GS1 General Specifications print them: the guard bars
/// extend below the data bars, the digits are grouped between the guards, the digits that have no bars of their own are
/// printed outside the bars, and the digits of an add-on are printed above its bars, which start lower.
/// </summary>
internal static class EanUpcSceneBuilder
{
    /// <summary>How far the guard bars extend below the data bars, in modules.</summary>
    /// <remarks>
    /// The GS1 General Specifications extend the guard bars by 5 modules: a fixed rule, in modules so that it scales with
    /// the symbol as the specification does. The digits are printed in this space, between the guards.
    /// </remarks>
    public const int GuardExtensionModules = 5;

    /// <summary>The space between the digits printed outside the bars and the bars, in modules.</summary>
    public const int OutsideDigitGapModules = 1;

    /// <summary>The size of the small digits (the number system and check digits of UPC), relative to the other digits.</summary>
    public const double SmallDigitScale = 0.8;

    /// <exception cref="ArgumentOutOfRangeException">The size of the drawing is not a finite number.</exception>
    public static Scene Create(in SymbolSceneContent content, EanUpcLayout layout)
    {
        var text = content.Text!;
        var snap = content.SnapToPixels;
        var moduleWidth = content.ModuleWidth;
        var moduleHeight = content.ModuleHeight;
        var modules = content.Modules;
        var columns = modules.GetLength(1);
        var measurer = new FontFaceTextMeasurer(text.Fonts);
        var gap = OutsideDigitGapModules * moduleWidth;
        var groups = layout.Groups;

        // FontSize is the largest size: the digits shrink so that every group fits between its guards
        var fontSize = text.FontSize;
        var scale = 1d;
        foreach (var group in groups)
        {
            var available = GetAvailableWidth(layout, group, moduleWidth, gap);
            var groupWidth = measurer.Measure(group.Text, CreateFont(text, GetFontSize(group, fontSize))).Width;
            if (groupWidth > available)
            {
                scale = Math.Min(scale, available / groupWidth);
            }
        }

        fontSize *= scale;
        var metrics = new TextMetrics[groups.Length];
        for (var i = 0; i < groups.Length; i++)
        {
            metrics[i] = measurer.Measure(groups[i].Text, CreateFont(text, GetFontSize(groups[i], fontSize)));
        }

        var lineMetrics = measurer.Measure("0", CreateFont(text, fontSize));

        // Vertically: the add-on digits at the top, then the bars of the add-on, which end with the guards. The bars of the
        // add-on keep at least half the height of the data bars, which moves the symbol down when the digits are large.
        var addOnBarsTop = 0d;
        var barsTop = 0d;
        if (layout.HasAddOn)
        {
            addOnBarsTop = Snap(lineMetrics.Height + text.Spacing, snap, Math.Ceiling);
            barsTop = Math.Max(0, Snap(addOnBarsTop + (moduleHeight / 2) - moduleHeight - (GuardExtensionModules * moduleWidth), snap, Math.Ceiling));
        }

        var dataBottom = barsTop + moduleHeight;
        var guardBottom = dataBottom + (GuardExtensionModules * moduleWidth);
        var textTop = Snap(dataBottom + text.Spacing, snap, RoundAwayFromZero);

        // Horizontally: the quiet zones hold the digits printed outside the bars
        var left = content.HorizontalQuietZoneModules * moduleWidth;
        var right = left;
        for (var i = 0; i < groups.Length; i++)
        {
            switch (groups[i].Position)
            {
                case EanUpcTextPosition.Left:
                    left = Math.Max(left, Snap(metrics[i].Width + gap, snap, Math.Ceiling));
                    break;

                case EanUpcTextPosition.Right when !layout.HasAddOn:
                    right = Math.Max(right, Snap(metrics[i].Width + gap, snap, Math.Ceiling));
                    break;
            }
        }

        var width = left + (columns * moduleWidth) + right;
        var texts = new SceneText[groups.Length];
        var height = guardBottom;
        var paint = text.Paint ?? content.DarkPaint;
        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            var groupFont = CreateFont(text, GetFontSize(group, fontSize));
            var x = group.Position switch
            {
                EanUpcTextPosition.Left => Math.Max(0, Snap(left - gap - metrics[i].Width, snap, Math.Floor)),
                EanUpcTextPosition.Right => left + (layout.SymbolEnd * moduleWidth) + gap,
                _ => Snap(left + (group.Range.Start * moduleWidth) + (((group.Range.Length * moduleWidth) - metrics[i].Width) / 2), snap, Math.Floor),
            };

            // The small digits stand on the baseline of the others
            var y = group.Position switch
            {
                EanUpcTextPosition.AboveAddOn => 0,
                _ when group.IsSmall => textTop + lineMetrics.Ascent - metrics[i].Ascent,
                _ => textTop,
            };

            height = Math.Max(height, y + metrics[i].Height);
            texts[i] = new SceneText(x, y, group.Text, groupFont, paint is SolidPaint solid ? solid.Color : SceneColor.Black)
            {
                Baseline = SceneTextBaseline.TextTop,
                Fill = paint is SolidPaint ? null : paint,
                MeasuredWidth = metrics[i].Width,
            };
        }

        if (!double.IsFinite(width) || !double.IsFinite(height))
            throw new ArgumentOutOfRangeException(content.SizeParameterName, "The dimensions of the drawing are too large.");

        var scene = new Scene(width, height, text.Fonts.FontFamily) { Fonts = text.Fonts };
        if (content.LightPaint is not null)
        {
            scene.Root.Add(new SceneRectangle(0, 0, width, height) { Fill = content.LightPaint });
        }

        // The bars of a run of dark modules end where the bars of their part of the symbol end
        var builder = new PathBuilder();
        var hasDarkModule = false;
        var column = 0;
        while (column < columns)
        {
            if (!modules[0, column])
            {
                column++;
                continue;
            }

            var start = column;
            var (top, bottom) = GetBarExtent(start);
            while (column < columns && modules[0, column] && GetBarExtent(column) == (top, bottom))
            {
                column++;
            }

            builder.AddRectangle(left + (start * moduleWidth), top, (column - start) * moduleWidth, bottom - top);
            hasDarkModule = true;
        }

        if (hasDarkModule)
        {
            scene.Root.Add(new ScenePath(builder.Build()) { Fill = content.DarkPaint });
        }

        foreach (var sceneText in texts)
        {
            scene.Root.Add(sceneText);
        }

        return scene;

        (double Top, double Bottom) GetBarExtent(int module)
        {
            if (layout.HasAddOn && module >= layout.AddOnStart)
                return (addOnBarsTop, guardBottom);

            foreach (var range in layout.ExtendedBars)
            {
                if (range.Contains(module))
                    return (barsTop, guardBottom);
            }

            return (barsTop, dataBottom);
        }
    }

    /// <summary>Gets the width a group of digits can take: its range of modules, or the gap before the add-on.</summary>
    public static double GetAvailableWidth(EanUpcLayout layout, EanUpcTextGroup group, double moduleWidth, double gap) => group.Position switch
    {
        EanUpcTextPosition.Below or EanUpcTextPosition.AboveAddOn => group.Range.Length * moduleWidth,
        EanUpcTextPosition.Right when layout.HasAddOn => ((layout.AddOnStart - layout.SymbolEnd) * moduleWidth) - (2 * gap),
        _ => double.PositiveInfinity,
    };

    private static double GetFontSize(EanUpcTextGroup group, double fontSize) => group.IsSmall ? fontSize * SmallDigitScale : fontSize;

    private static SceneFont CreateFont(BarcodeTextOptions text, double fontSize) => new(fontSize, Family: text.Fonts.DefaultFamily);

    private static double RoundAwayFromZero(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    private static double Snap(double value, bool snapToPixels, Func<double, double> round) => snapToPixels ? round(value) : value;
}
