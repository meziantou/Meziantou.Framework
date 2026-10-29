using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Samples;

/// <summary>Examples of the documentation site (docs/01-Imaging/08-Text), run by DocumentationExampleTests.</summary>
internal static class TextExamples
{
    /// <summary>A stack of fonts: Latin letters, Japanese ideographs and a color emoji in one line.</summary>
    #region Imaging_FontStackFallback
    public static void DrawFontStack(string latinFontPath, string japaneseFontPath, string emojiFontPath, string outputPath)
    {
        // Each character is drawn with the first font of the stack that has it. An emoji is looked for first in the
        // fonts that have color glyphs, and is drawn with its own colors
        var fonts = new FontStack(Font.Load(latinFontPath), Font.Load(japaneseFontPath), Font.Load(emojiFontPath));
        var layout = TextLayout.Create("Tokyo 東京 \U0001F680", new TextStyle(fonts, 32));

        using var image = new Image<Rgba32>((int)Math.Ceiling(layout.Width) + 20, (int)Math.Ceiling(layout.Height) + 20, new Rgba32(255, 255, 255));
        image.Draw(canvas => canvas.FillText(new Rgba32(0, 0, 0), layout, new PointD(10, 10)));
        image.Save(outputPath);
    }
    #endregion

    /// <summary>Left-to-right and right-to-left text in the same layout.</summary>
    #region Imaging_DrawBidiText
    public static void DrawMixedDirections(string latinFontPath, string hebrewFontPath, string outputPath)
    {
        var fonts = new FontStack(Font.Load(latinFontPath), Font.Load(hebrewFontPath));
        const string Shalom = "\u05E9\u05DC\u05D5\u05DD"; // "shalom", in Hebrew letters

        // Two paragraphs, each in the direction of its first letter: the first one is left-to-right with a Hebrew word
        // in it, the second one right-to-left with an English word and a number in it. Its parentheses are mirrored
        var text = $"The word {Shalom} means peace.\n{Shalom} (peace) 2026";

        // Start is the left of a left-to-right paragraph and the right of a right-to-left one
        var layout = TextLayout.Create(text, new TextStyle(fonts, 24), new TextLayoutOptions { MaxWidth = 340, Alignment = TextAlignment.Start });

        using var image = new Image<Rgba32>(360, (int)Math.Ceiling(layout.Height) + 20, new Rgba32(255, 255, 255));
        image.Draw(canvas => canvas.FillText(new Rgba32(0, 0, 0), layout, new PointD(10, 10)));
        image.Save(outputPath);
    }
    #endregion

    /// <summary>A bold and an oblique made from the outlines of a regular font.</summary>
    #region Imaging_SyntheticStyles
    public static void DrawSyntheticStyles(string fontPath, string outputPath)
    {
        // Only the regular font is available: the bold thickens its glyphs and the oblique slants them. A bold or an
        // italic font, or an instance of a variable font, looks better when there is one
        var font = Font.Load(fontPath);
        var text = new RichTextBuilder()
            .Append("Regular\n", new TextStyle(font, 28))
            .Append("Synthetic bold\n", new TextStyle(font, 28) { Synthesis = FontSynthesis.Bold })
            .Append("Synthetic oblique\n", new TextStyle(font, 28) { Synthesis = FontSynthesis.Oblique })
            .Append("Bold and oblique", new TextStyle(font, 28) { Synthesis = FontSynthesis.Bold | FontSynthesis.Oblique })
            .Build();
        var layout = TextLayout.Create(text);

        using var image = new Image<Rgba32>((int)Math.Ceiling(layout.Width) + 20, (int)Math.Ceiling(layout.Height) + 20, new Rgba32(255, 255, 255));
        image.Draw(canvas => canvas.FillText(new Rgba32(0, 0, 0), layout, new PointD(10, 10)));
        image.Save(outputPath);
    }
    #endregion

    /// <summary>A description cut to two lines, ended by an ellipsis.</summary>
    #region Imaging_TrimText
    public static bool DrawProductCard(string fontPath, string outputPath)
    {
        var font = Font.Load(fontPath);
        var title = TextLayout.Create("Wireless headphones", new TextStyle(font, 20));

        // At most two lines in the width of the card: the last line that fits ends with an ellipsis, after the last
        // word that leaves room for it
        var options = new TextLayoutOptions { MaxWidth = 220, MaxLines = 2, Trimming = TextTrimming.Word };
        var description = TextLayout.Create(
            "Noise cancelling, thirty hours of battery life, a case that folds flat and a cable for the plane.",
            new TextStyle(font, 16),
            options);

        using var image = new Image<Rgba32>(240, (int)Math.Ceiling(title.Height + description.Height) + 24, new Rgba32(255, 255, 255));
        image.Draw(canvas =>
        {
            canvas.StrokeRoundedRectangle(new Pen(new Rgba32(200, 200, 200), 1), new RectangleD(0.5, 0.5, image.Width - 1, image.Height - 1), 6);
            canvas.FillText(new Rgba32(0, 0, 0), title, new PointD(10, 8));
            canvas.FillText(new Rgba32(90, 90, 90), description, new PointD(10, 12 + title.Height));
        });

        image.Save(outputPath);

        // A part of the text is not in the layout: a caller can show the whole text elsewhere, in a tooltip for example
        return description.IsTruncated;
    }
    #endregion

    /// <summary>What a layout measures: its box, the baseline of each line, and the box of what it draws.</summary>
    #region Imaging_MeasureText
    public static RectangleD DrawMeasuredText(string fontPath, string outputPath)
    {
        var layout = TextLayout.Create("Typography\nlayout", Font.Load(fontPath), size: 40);

        const int Margin = 16;
        var origin = new PointD(Margin, Margin);
        using var image = new Image<Rgba32>((int)Math.Ceiling(layout.Width) + (2 * Margin), (int)Math.Ceiling(layout.Height) + (2 * Margin), new Rgba32(255, 255, 255));
        image.Draw(canvas =>
        {
            // The box of the layout: the advances of the glyphs, and the ascent and the descent of the font on each line
            canvas.FillRectangle(new Rgba32(225, 235, 250), new RectangleD(origin.X, origin.Y, layout.Width, layout.Height));

            // Each line has its position, its width and its baseline, from its top
            foreach (var line in layout.Lines)
            {
                var y = origin.Y + line.Y + line.Baseline;
                canvas.StrokeLine(new Pen(new Rgba32(30, 60, 160), 1), new PointD(origin.X + line.X, y), new PointD(origin.X + line.X + line.Width, y));
            }

            canvas.FillText(new Rgba32(0, 0, 0), layout, origin);

            // The ink box contains everything the text paints, which is not the box of the layout
            canvas.StrokeRectangle(new Pen(new Rgba32(200, 30, 30), 1), layout.InkBounds.Offset(origin.X, origin.Y));
        });

        image.Save(outputPath);
        return layout.InkBounds;
    }
    #endregion

    /// <summary>Hit testing: the character under a click, a caret, and the rectangles of a selection.</summary>
    #region Imaging_HitTestText
    public static TextHit DrawSelection(string fontPath, PointD click, string outputPath)
    {
        const string Text = "Select a part of the text\nacross two lines";
        var layout = TextLayout.Create(Text, Font.Load(fontPath), size: 24);
        var origin = new PointD(10, 10);

        // The positions are relative to the top-left corner of the layout: remove the origin it is drawn at
        var hit = layout.HitTest(new PointD(click.X - origin.X, click.Y - origin.Y));

        // A caret is a rectangle without width, as tall as its line, before the character of an index of the text
        var caret = layout.GetCaretRectangle(hit.CaretIndex);

        // A range of the text is one rectangle per line, or more where directions are mixed
        var start = Text.IndexOf("the text", StringComparison.Ordinal);
        var selection = layout.GetSelectionRectangles(start, Text.IndexOf(" two", StringComparison.Ordinal) - start);

        using var image = new Image<Rgba32>((int)Math.Ceiling(layout.Width) + 20, (int)Math.Ceiling(layout.Height) + 20, new Rgba32(255, 255, 255));
        image.Draw(canvas =>
        {
            foreach (var rectangle in selection)
            {
                canvas.FillRectangle(new Rgba32(180, 210, 255), rectangle.Offset(origin.X, origin.Y));
            }

            canvas.FillText(new Rgba32(0, 0, 0), layout, origin);
            canvas.FillRectangle(new Rgba32(200, 30, 30), new RectangleD(origin.X + caret.X - 1, origin.Y + caret.Y, 2, caret.Height));
        });

        image.Save(outputPath);
        return hit;
    }
    #endregion
}
