using System.Globalization;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

#pragma warning disable WEBPLATEXP0024 // The target exercises the text clusters of TextMetrics, a proposal
#pragma warning disable WEBPLATEXP0025 // The target exercises the shaped text, a proposal

namespace Meziantou.Framework.WebPlatform.Fuzz;

/// <summary>
/// The target of the text clusters and of the shaped text of the 2D canvas: a byte of options (direction, alignment,
/// baseline, letter spacing, which can be negative) followed by a text. The clusters must cover the text in logical order, the caret positions,
/// the advances and the selection rectangles must stay in the text, the bounding box of the whole text must be the one of
/// <c>measureText()</c>, the glyph runs must add up to the width of the text, and the invalid ranges must throw an
/// <c>IndexSizeError</c>.
/// </summary>
public static partial class WebPlatformFuzzTargets
{
    private const int CanvasTextMaxLength = 64;
    private const double CanvasTextTolerance = 1e-6;

    private static readonly CanvasTextAlign[] CanvasTextAligns = [CanvasTextAlign.Start, CanvasTextAlign.End, CanvasTextAlign.Left, CanvasTextAlign.Right, CanvasTextAlign.Center];

    private static readonly CanvasTextBaseline[] CanvasTextBaselines = [CanvasTextBaseline.Alphabetic, CanvasTextBaseline.Top, CanvasTextBaseline.Hanging, CanvasTextBaseline.Middle, CanvasTextBaseline.Ideographic, CanvasTextBaseline.Bottom];

    /// <summary>The tokens of the target: bidi controls, joiners, variation selectors, emoji, right-to-left letters, combining marks and ligatures.</summary>
    private static readonly string[] CanvasTextTokens = ["\u202D", "\u202E", "\u202C", "\u2066", "\u2067", "\u2069", "\u200D", "\u200C", "\uFE0F", "\u263A", "\U0001F3C1", "\U0001F468\u200D\U0001F469", "\u05D0", "\u05E7", "\u0627\u0644", "\u0301", "fi", "ffl", "\t", "\0", "(", ")", "-"];

    /// <summary>Runs the text cluster and shaped text APIs on a text, and checks the properties of their results.</summary>
    public static void CanvasText(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        var options = data[0];
        var text = FuzzText.Decode(data[1..]);
        if (text.Length > CanvasTextMaxLength)
        {
            text = text[..CanvasTextMaxLength];
            if (char.IsHighSurrogate(text[^1]))
            {
                text = text[..^1];
            }
        }

        var context = FuzzCanvasContext.Value;
        context.Reset();
        context.Font = "12px sans-serif";
        context.Direction = (options & 1) != 0 ? CanvasDirection.Rtl : CanvasDirection.Ltr;
        context.TextAlign = CanvasTextAligns[(options >> 1) % CanvasTextAligns.Length];
        context.TextBaseline = CanvasTextBaselines[(options >> 4) % CanvasTextBaselines.Length];
        // An alignment value after the five alignments (they wrap) with spacing selects a negative spacing, which gives the
        // clusters negative advances: the positions then go from right to left, and only hold the properties that do not
        // depend on the order of the edges
        var isNegativeSpacing = (options & 0x80) != 0 && ((options >> 1) & 7) >= CanvasTextAligns.Length;
        context.LetterSpacing = isNegativeSpacing ? "-9px" : (options & 0x80) != 0 ? "3px" : "0px";

        var metrics = context.MeasureText(text);
        var shaped = context.ShapeText(text);
        FuzzAssert.Equal(metrics.Width, shaped.Width, "shapeText() and measureText() give the same width");
        var length = (uint)text.Length;
        var alignment = context.TextAlign switch
        {
            CanvasTextAlign.Center => metrics.Width / 2,
            CanvasTextAlign.Right => metrics.Width,
            CanvasTextAlign.Left => 0,
            CanvasTextAlign.End => context.Direction == CanvasDirection.Rtl ? 0 : metrics.Width,
            _ => context.Direction == CanvasDirection.Rtl ? metrics.Width : 0,
        };

        // The clusters are in logical order, do not overlap, and cover the text (but its null characters, which are not drawn)
        var clusters = metrics.GetTextClusters();
        var glyphClusters = shaped.GlyphClusters();
        FuzzAssert.Equal(clusters.Count, glyphClusters.Count, "getTextClusters() and glyphClusters() give the same clusters");
        var end = 0u;
        for (var i = 0; i < clusters.Count; i++)
        {
            var cluster = clusters[i];
            FuzzAssert.True(cluster.Start >= end && cluster.End > cluster.Start && cluster.End <= length, $"cluster {i} [{cluster.Start}, {cluster.End}) follows the previous one in the text");
            FuzzAssert.True(text.AsSpan((int)end, (int)(cluster.Start - end)).IndexOfAnyExcept('\0') < 0, $"only null characters precede cluster {i}");
            FuzzAssert.True(double.IsFinite(cluster.X) && (isNegativeSpacing || (cluster.X >= -alignment - CanvasTextTolerance && cluster.X <= metrics.Width - alignment + CanvasTextTolerance)), $"cluster {i} is in the text");
            FuzzAssert.Equal(cluster.Start, glyphClusters[i].Start, "the glyph clusters start as the text clusters");
            FuzzAssert.Equal(cluster.End, glyphClusters[i].End, "the glyph clusters end as the text clusters");
            end = cluster.End;
        }

        FuzzAssert.True(text.AsSpan((int)end).IndexOfAnyExcept('\0') < 0, "the clusters cover the end of the text");

        // The glyph runs add up to the width of the text, from left to right
        var runs = shaped.GlyphRuns();
        var runWidth = 0d;
        var runLeft = double.NegativeInfinity;
        foreach (var run in runs)
        {
            FuzzAssert.True(run.Start < run.End && run.End <= length && run.Width >= 0, "a glyph run is a range of the text");
            FuzzAssert.True(double.IsFinite(run.X) && run.X >= runLeft - CanvasTextTolerance, "the glyph runs are sorted from left to right");
            runLeft = run.X;
            runWidth += run.Width;
        }

        FuzzAssert.True(isNegativeSpacing || Math.Abs(runWidth - shaped.Width) <= CanvasTextTolerance * Math.Max(1, shaped.Width), $"the glyph runs add up to the width of the text ({runWidth} and {shaped.Width})");

        // The caret positions and the advances stay in the text
        foreach (var offset in (ReadOnlySpan<double>)[double.NegativeInfinity, -1000, -alignment, 0, metrics.Width / 3, metrics.Width - alignment, 1000, double.PositiveInfinity, double.NaN])
        {
            FuzzAssert.True(metrics.GetIndexFromOffset(offset) <= length, $"getIndexFromOffset({offset.ToString(CultureInfo.InvariantCulture)}) is a position of the text");
            FuzzAssert.Equal(metrics.GetIndexFromOffset(offset), shaped.AdvanceToPosition(offset), "advanceToPosition() and getIndexFromOffset() agree");
        }

        for (var position = 0u; position <= length; position++)
        {
            var advance = shaped.PositionToAdvance(position);
            FuzzAssert.True(double.IsFinite(advance) && (isNegativeSpacing || (advance >= -alignment - CanvasTextTolerance && advance <= metrics.Width - alignment + CanvasTextTolerance)), $"positionToAdvance({position}) is in the text");
        }

        // The selection rectangles are in the text, with the height of the font
        var height = metrics.FontBoundingBoxAscent + metrics.FontBoundingBoxDescent;
        foreach (var (start, stop) in (ReadOnlySpan<(uint, uint)>)[(0, length), (length / 3, length), (length, 0), (0, length / 2)])
        {
            var rects = metrics.GetSelectionRects(start, stop);
            FuzzAssert.True(start < stop || rects.Count == 1, "a collapsed selection is a caret rectangle");
            foreach (var rect in rects)
            {
                FuzzAssert.True(rect.Width >= 0 && (isNegativeSpacing || (rect.X >= -alignment - CanvasTextTolerance && rect.X + rect.Width <= metrics.Width - alignment + CanvasTextTolerance)), "a selection rectangle is in the text");
                FuzzAssert.True(Math.Abs(rect.Height - height) <= CanvasTextTolerance && Math.Abs(rect.Y + metrics.FontBoundingBoxAscent) <= CanvasTextTolerance, "a selection rectangle has the height of the font");
            }

            FuzzAssert.Equal(rects.Count, shaped.SelectionRects(start, stop).Count, "selectionRects() and getSelectionRects() agree");
        }

        // The bounding box of the whole text is the one of measureText()
        if (length > 0)
        {
            var box = metrics.GetActualBoundingBox(0, length);
            FuzzAssert.True(Math.Abs(box.X + metrics.ActualBoundingBoxLeft) <= CanvasTextTolerance && Math.Abs(box.Width - (metrics.ActualBoundingBoxLeft + metrics.ActualBoundingBoxRight)) <= CanvasTextTolerance, "the bounding box of the whole text is the one of measureText() horizontally");
            FuzzAssert.True(Math.Abs(box.Y + metrics.ActualBoundingBoxAscent) <= CanvasTextTolerance && Math.Abs(box.Height - (metrics.ActualBoundingBoxAscent + metrics.ActualBoundingBoxDescent)) <= CanvasTextTolerance, "the bounding box of the whole text is the one of measureText() vertically");
        }

        ExpectIndexSizeError(() => metrics.GetTextClusters(length, length), "getTextClusters() with a start at the end of the text");
        ExpectIndexSizeError(() => metrics.GetActualBoundingBox(0, length + 1), "getActualBoundingBox() with an end after the text");
        ExpectIndexSizeError(() => metrics.GetSelectionRects(length + 1, 0), "getSelectionRects() with a start after the text");
        ExpectIndexSizeError(() => shaped.GlyphClusters(0, length + 1), "glyphClusters() with an end after the text");
        ExpectIndexSizeError(() => shaped.PositionToAdvance(length + 1), "positionToAdvance() after the text");

        // The clusters and the runs are drawn (the backend checks the display lists)
        foreach (var cluster in clusters)
        {
            context.FillTextCluster(cluster, 8, 8);
        }

        context.StrokeGlyphRuns(runs, 8, 8);
        context.FillGlyphRuns(glyphClusters, 8, 8);
        _ = context.GetImageData(0, 0, 1, 1);

        static void ExpectIndexSizeError(Action action, string description)
        {
            try
            {
                action();
            }
            catch (DomException ex) when (ex.Name == "IndexSizeError")
            {
                return;
            }

            FuzzAssert.Fail(description + " must throw an IndexSizeError");
        }
    }

    private static IEnumerable<FuzzSeed> GetCanvasTextSeeds()
    {
        // The texts of the unit tests and of the web-platform-tests, with their options: (rtl, align, baseline, spacing)
        (string Text, byte Options)[] scenarios =
        [
            ("ABC", 0),
            ("Hi \u263A\uFE0F!", 0x10),
            ("Shaped", 4 << 1),
            ("UNAVAILABLE", 0x80),
            ("\U0001F3C1\U0001F3B6\U0001F3C1", 1),
            ("\uFF09\uFF08\u3042\uFF09\uFF08", 1 | 0x80),
            ("\u05D0\u05D9\u05E4\u05D4 \u05D4\u05E1\u05E4\u05E8\u05D9\u05D9\u05D4?", 1 | (2 << 1)),
            ("bidi\u05DE\u05EA\u05DE\u05D8\u05D9\u05E7\u05D4", 3 << 1),
            ("\u202EUNAVAILABLE\u202C", 1 | (1 << 1)),
            ("\u202DUNAVAILABLE\u202C", 0x30),
            ("X\u200DY", 0),
            ("A\u200DB\0C", 0x50),
            ("-0123456789-", 1 | (4 << 1)),
            ("Test \u263A\uFE0F \u05D0 World", 0x20),
            ("\U0001FFFD\u200D\U0001FFFD", 0),
            ("", 0),
            ("\0\0", 0),
            ("ABC", 0x80 | (5 << 1)),
            ("A\u202EB\u202CC", 0x80 | (7 << 1)),
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            byte[] seed = [scenarios[i].Options, .. System.Text.Encoding.UTF8.GetBytes(scenarios[i].Text)];
            yield return new FuzzSeed("canvas-text-" + i.ToString(CultureInfo.InvariantCulture), seed);
        }
    }
}
