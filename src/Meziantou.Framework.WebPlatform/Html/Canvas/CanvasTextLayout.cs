using Meziantou.Framework.WebPlatform.Css.Fonts;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A glyph of a prepared text: its font, its id, its position from the left of the text and below the alphabetic baseline, and the index of its cluster in <see cref="CanvasTextLayout.Clusters"/>.</summary>
internal readonly record struct CanvasTextGlyph(MatchedFont Font, double EmSize, ushort GlyphId, double X, double OffsetY, int Cluster);

/// <summary>
/// A cluster of a prepared text (the text clusters of the proposals): a grapheme cluster, or several when a glyph spans them
/// (a ligature). <see cref="Start"/> and <see cref="End"/> are UTF-16 indexes in the text; <see cref="Left"/> is the distance
/// from the left of the text; <see cref="Run"/> is the index of its bidi run in visual order.
/// </summary>
internal readonly record struct CanvasTextCluster(int Start, int End, double Left, double Width, bool IsRightToLeft, int Run)
{
    public double Right => Left + Width;
}

/// <summary>
/// A text prepared by the text preparation algorithm of a 2D context (https://html.spec.whatwg.org/multipage/canvas.html#text-preparation-algorithm):
/// its glyphs and clusters, positioned from the left of the text on the alphabetic baseline, the direction, the alignment and
/// the baseline of the context when it was prepared, and the metrics of the font (above the alphabetic baseline, in CSS px).
/// <see cref="Clusters"/> are in the visual order of the bidi runs, and in logical order in a run.
/// </summary>
internal sealed record CanvasTextLayout(
    string Text,
    List<CanvasTextGlyph> Glyphs,
    List<CanvasTextCluster> Clusters,
    double Width,
    bool IsRightToLeft,
    CanvasTextAlign Align,
    CanvasTextBaseline Baseline,
    double Ascent,
    double Descent,
    double EmAscent,
    double EmDescent,
    double Hanging,
    double Ideographic)
{
    /// <summary>The glyphs of each cluster, from left to right (built the first time it is needed).</summary>
    private List<CanvasTextGlyph>[]? _glyphsByCluster;

    /// <summary>
    /// The caret positions inside the ligatures (a cluster of several typographic character units): the distance of the caret
    /// before the character at a UTF-16 index from the left of the text, at the caret values of the font, else at equal parts.
    /// </summary>
    public Dictionary<int, double> LigatureCarets { get; init; } = [];

    /// <summary>The distance of the alignment point of the text from its left.</summary>
    public double AlignmentOffset => GetAlignmentOffset(Align, Width);

    /// <summary>The distance of the baseline of the text above its alphabetic baseline.</summary>
    public double BaselineOffset => GetBaselineOffset(Baseline);

    /// <summary>The distance of the alignment point of a box of a given width from its left, for an alignment of the context (start and end are resolved with the direction of the text).</summary>
    public double GetAlignmentOffset(CanvasTextAlign align, double width)
    {
        var physical = align switch
        {
            CanvasTextAlign.Start => IsRightToLeft ? CanvasTextAlign.Right : CanvasTextAlign.Left,
            CanvasTextAlign.End => IsRightToLeft ? CanvasTextAlign.Left : CanvasTextAlign.Right,
            var value => value,
        };

        return physical switch
        {
            CanvasTextAlign.Right => width,
            CanvasTextAlign.Center => width / 2,
            _ => 0,
        };
    }

    /// <summary>The distance of a baseline above the alphabetic baseline (https://html.spec.whatwg.org/multipage/canvas.html#text-preparation-algorithm, step 10).</summary>
    public double GetBaselineOffset(CanvasTextBaseline baseline) => baseline switch
    {
        CanvasTextBaseline.Top => EmAscent,
        CanvasTextBaseline.Hanging => Hanging,
        CanvasTextBaseline.Middle => (EmAscent - EmDescent) / 2,
        CanvasTextBaseline.Ideographic => Ideographic,
        CanvasTextBaseline.Bottom => -EmDescent,
        _ => 0,
    };

    /// <summary>Returns the indexes of the clusters that have characters in a range of the text, in logical order.</summary>
    public List<int> GetClusters(int start, int end)
    {
        var result = new List<int>();
        for (var i = 0; i < Clusters.Count; i++)
        {
            var cluster = Clusters[i];
            if (cluster.Start < end && cluster.End > start)
            {
                result.Add(i);
            }
        }

        result.Sort((a, b) => Clusters[a].Start.CompareTo(Clusters[b].Start));
        return result;
    }

    /// <summary>Returns the glyphs of a cluster, from left to right.</summary>
    public List<CanvasTextGlyph> GetGlyphs(int cluster)
    {
        if (_glyphsByCluster is null)
        {
            var glyphsByCluster = new List<CanvasTextGlyph>[Clusters.Count];
            for (var i = 0; i < glyphsByCluster.Length; i++)
            {
                glyphsByCluster[i] = [];
            }

            foreach (var glyph in Glyphs)
            {
                glyphsByCluster[glyph.Cluster].Add(glyph);
            }

            _glyphsByCluster = glyphsByCluster;
        }

        return _glyphsByCluster[cluster];
    }

    /// <summary>Returns the glyphs of a set of clusters, from left to right.</summary>
    public List<CanvasTextGlyph> GetGlyphs(IReadOnlyList<int> clusters)
    {
        if (clusters.Count == 1)
            return GetGlyphs(clusters[0]);

        var result = new List<CanvasTextGlyph>();
        foreach (var cluster in clusters)
        {
            result.AddRange(GetGlyphs(cluster));
        }

        result.Sort((a, b) => a.X.CompareTo(b.X));
        return result;
    }

    /// <summary>
    /// Returns the bounding box of the outlines of the glyphs of a set of clusters (all the glyphs when it is null), from the
    /// left of the text and above the alphabetic baseline, or false when no glyph has ink.
    /// </summary>
    public bool TryGetInkBounds(IReadOnlyCollection<int>? clusters, out double left, out double right, out double top, out double bottom)
    {
        var set = clusters is null ? null : clusters as HashSet<int> ?? [.. clusters];
        (left, right, top, bottom) = (0, 0, 0, 0);
        var hasInk = false;
        foreach (var glyph in Glyphs)
        {
            if (set is not null && !set.Contains(glyph.Cluster))
                continue;

            if (!glyph.Font.Font.TryGetGlyphBounds(glyph.GlyphId, out var bounds) || (bounds.XMin == bounds.XMax && bounds.YMin == bounds.YMax))
                continue;

            var scale = glyph.EmSize / glyph.Font.Font.UnitsPerEm;
            var glyphLeft = glyph.X + (bounds.XMin * scale);
            var glyphRight = glyph.X + (bounds.XMax * scale);
            var glyphTop = (bounds.YMax * scale) - glyph.OffsetY;
            var glyphBottom = (bounds.YMin * scale) - glyph.OffsetY;
            if (!hasInk)
            {
                (left, right, top, bottom) = (glyphLeft, glyphRight, glyphTop, glyphBottom);
                hasInk = true;
            }
            else
            {
                left = Math.Min(left, glyphLeft);
                right = Math.Max(right, glyphRight);
                top = Math.Max(top, glyphTop);
                bottom = Math.Min(bottom, glyphBottom);
            }
        }

        return hasInk;
    }

    /// <summary>
    /// Returns the bounding box of the glyphs of the characters of a range (https://github.com/whatwg/html/pull/11000), relative
    /// to the alignment point and to the baseline: the bounding box that <c>measureText()</c> gives for the text where the
    /// other characters are not drawn.
    /// </summary>
    public DomRectReadOnly GetActualBoundingBox(uint start, uint end)
    {
        ThrowIfInvalidClusterRange(start, end);
        var baseline = BaselineOffset;
        if (!TryGetInkBounds(GetClusters((int)start, (int)end), out var left, out var right, out var top, out var bottom))
            return new DomRectReadOnly(0, baseline, 0, 0);

        return new DomRectReadOnly(left - AlignmentOffset, baseline - top, right - left, top - bottom);
    }

    /// <summary>
    /// Returns the rectangles of the selection of a range of the text, relative to the alignment point and to the baseline: one
    /// per bidi run that has characters in the range, of the height of the font. A collapsed range (or a start after the end) is
    /// a rectangle without width at the caret position of the end.
    /// </summary>
    public List<DomRectReadOnly> GetSelectionRects(uint start, uint end)
    {
        if (start > Text.Length || end > Text.Length)
            throw new DomException("The range is outside of the text", "IndexSizeError");

        var top = BaselineOffset - Ascent;
        var height = Ascent + Descent;
        var alignment = AlignmentOffset;
        var rects = new List<DomRectReadOnly>();
        if (start >= end)
        {
            rects.Add(new DomRectReadOnly(GetCaretX((int)end) - alignment, top, 0, height));
            return rects;
        }

        var bounds = new SortedDictionary<int, (double Left, double Right)>();
        foreach (var cluster in Clusters)
        {
            if (cluster.Start >= end || cluster.End <= start)
                continue;

            // The selected part of a ligature ends at its caret positions
            var from = GetCaretX(cluster, Math.Max((int)start, cluster.Start), snapToEnd: false);
            var to = GetCaretX(cluster, Math.Min((int)end, cluster.End), snapToEnd: true);
            var (left, right) = (Math.Min(from, to), Math.Max(from, to));
            var run = GetFragmentRun(cluster);
            bounds[run] = bounds.TryGetValue(run, out var value) ? (Math.Min(value.Left, left), Math.Max(value.Right, right)) : (left, right);
        }

        foreach (var (left, right) in bounds.Values)
        {
            rects.Add(new DomRectReadOnly(left - alignment, top, right - left, height));
        }

        return rects;
    }

    /// <summary>
    /// Returns the bidi run of the fragment of a cluster: a cluster without advance (e.g. a bidi control) is in the fragment of
    /// the character after it, as in the inline layout, which gives the characters removed by the bidi algorithm the level of
    /// the following character.
    /// </summary>
    public int GetFragmentRun(CanvasTextCluster cluster)
    {
        if (cluster.Width == 0 && Clusters.FindIndex(c => c.Start == cluster.End && c.Width > 0) is var next and >= 0)
            return Clusters[next].Run;

        return cluster.Run;
    }

    /// <summary>
    /// Returns the caret position (a UTF-16 index) closest to a distance from the left of the text: in the cluster under the
    /// point, the position before it in the reading direction when the point is on its first half, else the position after it.
    /// A point before or after the text gives the position at that side of the closest cluster.
    /// </summary>
    public uint GetIndexFromX(double x)
    {
        CanvasTextCluster? first = null;
        CanvasTextCluster? last = null;
        CanvasTextCluster? hit = null;
        CanvasTextCluster? next = null;
        foreach (var cluster in Clusters)
        {
            if (!(cluster.Width > 0))
                continue;

            if (first is null || cluster.Left < first.Value.Left)
            {
                first = cluster;
            }

            if (last is null || cluster.Right > last.Value.Right)
            {
                last = cluster;
            }

            if (cluster.Left <= x && x < cluster.Right)
            {
                hit = cluster;
            }
            else if (cluster.Left > x && (next is null || cluster.Left < next.Value.Left))
            {
                next = cluster;
            }
        }

        if (first is null || last is null)
            return 0;

        bool isFirstHalf;
        CanvasTextCluster target;
        if (x < first.Value.Left)
        {
            (target, isFirstHalf) = (first.Value, true);
        }
        else if (x >= last.Value.Right)
        {
            (target, isFirstHalf) = (last.Value, false);
        }
        else if (hit is { } value)
        {
            if (GetLigatureIndex(value, x) is { } index)
                return index;

            (target, isFirstHalf) = (value, x <= value.Left + (value.Width / 2));
        }
        else
        {
            (target, isFirstHalf) = (next ?? last.Value, true);
        }

        return (uint)(isFirstHalf != target.IsRightToLeft ? target.Start : target.End);
    }

    /// <summary>
    /// Returns the caret position closest to a point in a ligature that has caret positions inside it: the position before
    /// the part of the ligature under the point, or after it when the point is in its second half; null for another cluster.
    /// </summary>
    private uint? GetLigatureIndex(CanvasTextCluster cluster, double x)
    {
        var positions = new List<(int Index, double X)>();
        for (var index = cluster.Start + 1; index < cluster.End; index++)
        {
            if (LigatureCarets.TryGetValue(index, out var caret))
            {
                positions.Add((index, caret));
            }
        }

        if (positions.Count == 0)
            return null;

        positions.Add((cluster.Start, GetCaretX(cluster, cluster.Start, snapToEnd: false)));
        positions.Add((cluster.End, GetCaretX(cluster, cluster.End, snapToEnd: true)));
        positions.Sort((a, b) => a.X.CompareTo(b.X));
        for (var i = 0; i < positions.Count - 1; i++)
        {
            var (left, right) = (positions[i], positions[i + 1]);
            if (x < right.X || i == positions.Count - 2)
                return (uint)(x <= (left.X + right.X) / 2 ? left.Index : right.Index);
        }

        return null;
    }

    /// <summary>
    /// Returns the distance of a caret position of a cluster from the left of the text: its start edge for its start (the left
    /// edge in a left-to-right run, the right edge in a right-to-left run), its end edge for its end, the caret position
    /// inside a ligature, else the start or the end edge (<paramref name="snapToEnd"/>) for an index inside the cluster.
    /// </summary>
    private double GetCaretX(CanvasTextCluster cluster, int position, bool snapToEnd)
    {
        var startEdge = cluster.IsRightToLeft ? cluster.Right : cluster.Left;
        var endEdge = cluster.IsRightToLeft ? cluster.Left : cluster.Right;
        if (position <= cluster.Start)
            return startEdge;

        if (position >= cluster.End)
            return endEdge;

        if (LigatureCarets.TryGetValue(position, out var caret))
            return caret;

        return snapToEnd ? endEdge : startEdge;
    }

    /// <summary>
    /// Returns the distance of the caret position before a UTF-16 index from the left of the text: the start edge of the
    /// cluster of the character at the index (its left edge in a left-to-right run, its right edge in a right-to-left run), the
    /// caret position inside a ligature, or the end edge of the last cluster for the end of the text.
    /// </summary>
    public double GetCaretX(int position)
    {
        CanvasTextCluster? containing = null;
        CanvasTextCluster? lastLogical = null;
        foreach (var cluster in Clusters)
        {
            if (cluster.Start <= position && position < cluster.End && (containing is null || cluster.Start > containing.Value.Start))
            {
                containing = cluster;
            }

            if (lastLogical is null || cluster.End > lastLogical.Value.End)
            {
                lastLogical = cluster;
            }
        }

        if (containing is { } value)
            return GetCaretX(value, position, snapToEnd: false);

        if (lastLogical is { } end)
            return end.IsRightToLeft ? end.Left : end.Right;

        return 0;
    }

    /// <summary>Throws an <c>IndexSizeError</c> when the start of a range is not the index of a character of the text, or its end is after the text.</summary>
    public void ThrowIfInvalidClusterRange(uint start, uint end)
    {
        if (start >= Text.Length || end > Text.Length)
            throw new DomException("The range is outside of the text", "IndexSizeError");
    }

    /// <summary>
    /// Returns the layout of a part of the text: the glyphs and the clusters of a set of clusters, moved so that the left of
    /// <paramref name="left"/> is the left of the text, of the given width.
    /// </summary>
    public CanvasTextLayout Slice(IReadOnlyList<int> clusters, double left, double width)
    {
        var indexes = new Dictionary<int, int>();
        var sliceClusters = new List<CanvasTextCluster>(clusters.Count);
        foreach (var index in clusters)
        {
            indexes[index] = sliceClusters.Count;
            sliceClusters.Add(Clusters[index] with { Left = Clusters[index].Left - left });
        }

        var sliceGlyphs = new List<CanvasTextGlyph>();
        foreach (var glyph in Glyphs)
        {
            if (indexes.TryGetValue(glyph.Cluster, out var cluster))
            {
                sliceGlyphs.Add(glyph with { X = glyph.X - left, Cluster = cluster });
            }
        }

        var carets = new Dictionary<int, double>();
        foreach (var (index, caret) in LigatureCarets)
        {
            carets[index] = caret - left;
        }

        return this with { Glyphs = sliceGlyphs, Clusters = sliceClusters, Width = width, LigatureCarets = carets, _glyphsByCluster = null };
    }
}
