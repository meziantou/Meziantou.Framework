using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The rubies of a layout (<see cref="RichTextBuilder.AppendRuby"/>).</summary>
/// <remarks>
/// <para>
/// The base of a ruby is text of the layout, in items of its own: a line never ends inside it, and it gets the room its
/// annotation needs beyond it, half on each side. The annotation is laid out on its own, in the writing mode and the
/// orientation of the layout, and its glyphs are added to those of the layout on the line of its base.
/// </para>
/// <para>
/// The annotation is placed in the coordinates of the line, as the glyphs of the base are: centered along the base,
/// and above the ascent of the base, which is on the right of a vertical line once it is turned. The coordinates of the
/// lines of the annotation are those of its own layout: its glyphs, decorations and backgrounds are moved there.
/// </para>
/// </remarks>
internal sealed partial class TextLayoutEngine
{
    /// <summary>Lays out the annotations of the rubies of the text, and measures their bases.</summary>
    /// <returns>The annotation of each span that is the base of a ruby, or <see langword="null"/> when the text has none.</returns>
    private RubyAnnotation?[]? CreateRubies()
    {
        RubyAnnotation?[]? rubies = null;
        for (var i = 0; i < _spans.Length; i++)
        {
            var span = _spans[i];
            if (span.RubyText is null || span.Length == 0)
                continue;

            var options = new TextLayoutOptions
            {
                WritingMode = _options.WritingMode,
                Orientation = _options.Orientation,
                Direction = _options.Direction,
                CombineUprightDigits = _options.CombineUprightDigits,
                Hinting = _options.Hinting,
            };
            var annotation = span.RubyText.Length == 0
                ? null
                : Create(span.RubyText, [new TextSpan(0, span.RubyText.Length, span.RubyStyle!)], options, _scale, double.PositiveInfinity, double.PositiveInfinity);
            var baseText = Create(_text.Substring(span.Start, span.Length), [new TextSpan(0, span.Length, span.Style)], options, _scale, double.PositiveInfinity, double.PositiveInfinity);
            var along = annotation is null ? 0 : GetAlong(annotation);
            var size = GetSize(span.Style);
            rubies ??= new RubyAnnotation?[_spans.Length];
            rubies[i] = new RubyAnnotation
            {
                Annotation = annotation,
                Along = along,
                Extent = annotation is null ? 0 : _isVertical ? annotation.Width : annotation.Height,
                Padding = Math.Max(0, along - GetAlong(baseText)) / 2,
                BaseAbove = _hasCentralBaseline ? size / 2 : span.Style.Fonts.Primary.GetMetrics(size).Ascent,
            };
        }

        return rubies;

        double GetAlong(TextLayoutData data) => _isVertical ? data.Height : data.Width;
    }

    /// <summary>Finds the bases of the rubies of a paragraph.</summary>
    /// <returns>For each character of the paragraph, the end of the base of the ruby it is in, or 0; empty when the paragraph has no ruby.</returns>
    private int[] FindRubyEnds(Paragraph paragraph)
    {
        if (_rubies is null)
            return [];

        int[]? ends = null;
        for (var index = FindSpan(paragraph.Start); index < _spans.Length && _spans[index].Start < paragraph.End; index++)
        {
            var span = _spans[index];
            var start = Math.Max(span.Start, paragraph.Start);
            var end = Math.Min(span.Start + span.Length, paragraph.End);
            if (_rubies[index] is null || start >= end)
                continue;

            ends ??= new int[paragraph.End - paragraph.Start];
            ends.AsSpan(start - paragraph.Start, end - start).Fill(end);
        }

        return ends ?? [];
    }

    /// <summary>Adds the glyphs, decorations and backgrounds of the annotations of the rubies whose base starts on a line.</summary>
    private void AddRubyAnnotations(in RubyPlacement placement)
    {
        var line = placement.Line;
        var placed = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(line.Glyphs);
        for (var index = FindSpan(line.From); index < _spans.Length && _spans[index].Start < line.To; index++)
        {
            if (_rubies![index] is not { Annotation: { } annotation } ruby || _spans[index].Start < line.From)
                continue;

            // The base on the line, with the room it was given
            var start = double.PositiveInfinity;
            var end = double.NegativeInfinity;
            foreach (ref readonly var glyph in placed)
            {
                if (glyph.Span == index)
                {
                    start = Math.Min(start, placement.X + glyph.PenX);
                    end = Math.Max(end, placement.X + glyph.PenX + glyph.Advance);
                }
            }

            if (start > end)
                continue;

            var center = (start + end) / 2;
            var alongOffset = center - (ruby.Along / 2);
            var acrossOffset = placement.Baseline - ruby.BaseAbove - ruby.Extent;
            AddAnnotationGlyphs(placement, annotation, alongOffset, acrossOffset, center);

            // The decorations and backgrounds of the annotation, in the coordinates of the line: a vertical line turns them
            AddAnnotationRectangles(placement.Decorations, placement.Brushes, _isVertical ? annotation.Vertical!.Decorations : annotation.Decorations, annotation, alongOffset, acrossOffset);
            AddAnnotationRectangles(placement.Backgrounds, placement.Brushes, _isVertical ? annotation.Vertical!.Backgrounds : annotation.Backgrounds, annotation, alongOffset, acrossOffset);
        }
    }

    private void AddAnnotationGlyphs(in RubyPlacement placement, TextLayoutData annotation, double alongOffset, double acrossOffset, double anchor)
    {
        var line = placement.Line;
        var linePositions = _isVertical ? annotation.Vertical!.Positions : annotation.Positions;
        foreach (var run in annotation.Runs)
        {
            var font = annotation.Fonts[run.Font];
            var brush = GetBrushIndex(placement.Brushes, annotation.Brushes[run.Brush]);
            var outline = run.Outline < 0 ? -1 : GetOutlineIndex(placement.Outlines, annotation.Outlines[run.Outline]);
            placement.Runs.Add(run with { Font = GetFontIndex(font), Brush = brush, Outline = outline, Start = placement.Glyphs.Count });
            var isUpright = _isVertical && !run.IsSideways;
            for (var i = run.Start; i < run.Start + run.Count; i++)
            {
                var along = alongOffset + linePositions[i * 2];
                var across = acrossOffset + linePositions[(i * 2) + 1];
                placement.Glyphs.Add(annotation.Glyphs[i]);
                placement.Anchors.Add(anchor);
                if (_isVertical)
                {
                    var imageX = placement.LineRight - (across - line.Y);
                    if (_hinter is not null)
                    {
                        // As the glyphs of the line: fitted along the y axis of their outlines
                        if (isUpright || _options.Hinting == TextHinting.Full)
                        {
                            along = Math.Round(along, MidpointRounding.AwayFromZero);
                        }

                        if (!isUpright || _options.Hinting == TextHinting.Full)
                        {
                            imageX = Math.Round(imageX, MidpointRounding.AwayFromZero);
                            across = placement.LineRight - imageX + line.Y;
                        }

                        _hinter.Add(font, run.Size, annotation.Glyphs[i]);
                    }

                    placement.LinePositions.Add(along);
                    placement.LinePositions.Add(across);
                    placement.Positions.Add(imageX);
                    placement.Positions.Add(along);
                }
                else
                {
                    if (_hinter is not null)
                    {
                        across = Math.Round(across, MidpointRounding.AwayFromZero);
                        if (_options.Hinting == TextHinting.Full)
                        {
                            along = Math.Round(along, MidpointRounding.AwayFromZero);
                        }

                        _hinter.Add(font, run.Size, annotation.Glyphs[i]);
                    }

                    placement.Positions.Add(along);
                    placement.Positions.Add(across);
                }
            }
        }
    }

    private static void AddAnnotationRectangles(List<DecorationRectangle> target, List<Brush?> brushes, DecorationRectangle[] rectangles, TextLayoutData annotation, double alongOffset, double acrossOffset)
    {
        foreach (var rectangle in rectangles)
        {
            target.Add(rectangle with { X = rectangle.X + alongOffset, Y = rectangle.Y + acrossOffset, Brush = GetBrushIndex(brushes, annotation.Brushes[rectangle.Brush]) });
        }
    }

    /// <summary>What the annotations of a line are added to, and where the line is.</summary>
    private readonly record struct RubyPlacement(
        Line Line,
        double X,
        double Baseline,
        double LineRight,
        List<int> Glyphs,
        List<double> Positions,
        List<double> LinePositions,
        List<double> Anchors,
        List<GlyphRun> Runs,
        List<Brush?> Brushes,
        List<Pen> Outlines,
        List<DecorationRectangle> Decorations,
        List<DecorationRectangle> Backgrounds);
}
