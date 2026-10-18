using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>The properties of a <see cref="ShapedGlyph"/>.</summary>
[Flags]
internal enum ShapedGlyphFlags : byte
{
    None = 0,

    /// <summary>The first glyph of a cluster: a caret position is allowed before it.</summary>
    ClusterStart = 1,

    /// <summary>
    /// In vertical text, the glyph is upright: its advance is its vertical advance (<c>vmtx</c>, or the ascent plus the descent
    /// of the font). The other glyphs of vertical text are rotated 90 degrees clockwise and advance by their horizontal advance.
    /// </summary>
    Upright = 2,

    /// <summary>The glyph is not painted and has no advance: a default ignorable character (e.g. a joiner, a bidi control, a variation selector) or a line feed.</summary>
    Invisible = 4,

    /// <summary>The glyph of a word-separator character (https://drafts.csswg.org/css-text-4/#word-separator): <c>word-spacing</c> is added to its advance, and the justification can expand it.</summary>
    WordSeparator = 8,

    /// <summary>The glyph of a combining mark (a mark of the GDEF table, or of a Unicode mark), attached to the previous glyph.</summary>
    Mark = 16,

    /// <summary>
    /// The last glyph of a typographic character unit whose advance includes <c>letter-spacing</c>
    /// (https://drafts.csswg.org/css-text-4/#letter-spacing-property): the inline layout removes it at the end of a line.
    /// </summary>
    LetterSpaced = 32,

    /// <summary>
    /// Breaking the text before the cluster of the glyph and shaping the two parts separately would give other glyphs (e.g. a
    /// kerning pair, a contextual substitution): a line that starts or ends there is shaped again (HarfBuzz's
    /// <c>HB_GLYPH_FLAG_UNSAFE_TO_BREAK</c>). All the glyphs of a cluster have the same value.
    /// </summary>
    UnsafeToBreak = 64,
}

/// <summary>
/// A glyph of a <see cref="ShapedText"/>. <see cref="Cluster"/> is the UTF-16 index (in the shaped text) of the first
/// character of the cluster the glyph belongs to: all the glyphs of a cluster have the same value. A cluster can have
/// several characters (a ligature) and several glyphs (a multiple substitution, a base with its marks).
/// </summary>
/// <param name="GlyphId">The glyph in the font of its <see cref="ShapedFontRun"/>.</param>
/// <param name="Cluster">The UTF-16 index of the first character of the cluster of the glyph.</param>
/// <param name="Advance">The advance in CSS px along the inline axis, including the kerning, <c>letter-spacing</c> and <c>word-spacing</c>.</param>
/// <param name="OffsetX">The offset of the glyph from the pen position in CSS px, along the inline axis (towards the inline end of a left-to-right run; physical right in horizontal text).</param>
/// <param name="OffsetY">The offset of the glyph from the pen position in CSS px, along the block axis (positive downwards in horizontal text).</param>
/// <param name="Flags">The properties of the glyph.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ShapedGlyph(ushort GlyphId, int Cluster, double Advance, double OffsetX, double OffsetY, ShapedGlyphFlags Flags)
{
    /// <summary>Whether the glyph is upright in vertical text.</summary>
    public bool IsUpright => (Flags & ShapedGlyphFlags.Upright) != ShapedGlyphFlags.None;

    /// <summary>Whether the glyph is not painted.</summary>
    public bool IsInvisible => (Flags & ShapedGlyphFlags.Invisible) != ShapedGlyphFlags.None;

    /// <summary>Whether a break before the cluster of the glyph needs the text to be shaped again.</summary>
    public bool IsUnsafeToBreak => (Flags & ShapedGlyphFlags.UnsafeToBreak) != ShapedGlyphFlags.None;
}

/// <summary>
/// A sequence of consecutive glyphs of a <see cref="ShapedText"/> rendered with the same font and the same orientation: the
/// unit drawn by a renderer.
/// </summary>
/// <param name="Font">The font of the glyphs.</param>
/// <param name="EmSize">The em size of the font in CSS px (<see cref="MatchedFont.GetEmSize"/> of the font size): the size the glyphs are drawn at.</param>
/// <param name="GlyphStart">The index of the first glyph of the run in <see cref="ShapedText.Glyphs"/>.</param>
/// <param name="GlyphCount">The number of glyphs of the run.</param>
/// <param name="IsUpright">Whether the glyphs are upright (vertical text).</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ShapedFontRun(MatchedFont Font, double EmSize, int GlyphStart, int GlyphCount, bool IsUpright)
{
    /// <summary>
    /// The scale of the synthesized small capitals or subscripts and superscripts of the run (1 otherwise): <see cref="EmSize"/>
    /// is the em size of the font size multiplied by it.
    /// </summary>
    public float SynthesisScale { get; init; } = 1;

    /// <summary>The em size of the font at the font size of the text (without the scale of the synthesis): its metrics are the ones of the line.</summary>
    public double BaseEmSize => EmSize / SynthesisScale;
}

/// <summary>
/// A caret position inside a cluster of several typographic character units (a ligature): the UTF-16 index of the
/// character and its distance from the start of its cluster along the inline axis, in CSS px.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct LigatureCaret(int Index, double Offset);

/// <summary>
/// The result of the text shaping of a text run (<see cref="TextShaper"/>): the glyphs, their advances and offsets, the
/// mapping of the glyphs to the characters (the clusters) and the font runs. It is immutable and cached.
/// </summary>
/// <remarks>
/// The glyphs are in logical order (the order of the characters), also in a right-to-left run: a renderer draws a
/// right-to-left run from its right edge. The offsets along the inline axis (<see cref="GetOffset"/>) are measured from the
/// inline-start edge of the run (the right edge of a right-to-left horizontal run). The number of glyphs is not related to
/// the number of characters: a ligature is one glyph for several characters, a multiple substitution several glyphs for one.
/// The caret positions inside a ligature (<see cref="LigatureCarets"/>) divide it at the caret values of the font (GDEF),
/// else equally between its typographic character units, as Chrome does.
/// </remarks>
internal sealed class ShapedText
{
    private readonly ShapedGlyph[] _glyphs;
    private readonly ShapedFontRun[] _runs;
    private readonly LigatureCaret[] _carets;
    private double[]? _offsets;

    internal ShapedText(int length, ShapedGlyph[] glyphs, ShapedFontRun[] runs, double width, TextShapingOptions options, LigatureCaret[]? carets = null)
    {
        Length = length;
        _glyphs = glyphs;
        _runs = runs;
        _carets = carets ?? [];
        Width = width;
        Options = options;
    }

    /// <summary>An empty shaped text (the part of a text shaped with its neighbors that has no glyph of its own).</summary>
    internal static ShapedText Empty(int length, TextShapingOptions options) => new(length, [], [], 0, options);

    /// <summary>The length of the shaped text in UTF-16 code units.</summary>
    public int Length { get; }

    /// <summary>The glyphs, in logical order.</summary>
    public ReadOnlySpan<ShapedGlyph> Glyphs => _glyphs;

    /// <summary>The font runs, in logical order.</summary>
    public ReadOnlySpan<ShapedFontRun> Runs => _runs;

    /// <summary>The caret positions inside the ligatures, sorted by index.</summary>
    public ReadOnlySpan<LigatureCaret> LigatureCarets => _carets;

    /// <summary>The advance of the run along the inline axis in CSS px: the sum of the advances of the glyphs.</summary>
    public double Width { get; }

    /// <summary>The options the text was shaped with.</summary>
    public TextShapingOptions Options { get; }

    /// <summary>
    /// Whether the text is a composition combined upright in vertical text (<see cref="TextShaper.CombineUpright"/>): its glyphs
    /// are horizontal (<see cref="IsVertical"/> is false) across a vertical line, compressed horizontally by <see cref="CombinedScale"/>.
    /// </summary>
    public bool IsCombinedUpright { get; init; }

    /// <summary>The horizontal scale of the glyphs of a composition combined upright (1 otherwise).</summary>
    public double CombinedScale { get; init; } = 1;

    /// <summary>Whether the run is right-to-left.</summary>
    public bool IsRightToLeft => Options.IsRightToLeft;

    /// <summary>Whether the run is vertical.</summary>
    public bool IsVertical => Options.IsVertical;

    /// <summary>
    /// Whether a UTF-16 index is a cluster boundary of the shaped text (0 and <see cref="Length"/> included): a position where
    /// the run can be split without splitting a glyph. The clusters of the shaper are the grapheme clusters, merged by the
    /// ligatures; the inline layout snaps the caret positions to the grapheme clusters (UAX #29).
    /// </summary>
    public bool IsClusterBoundary(int index)
    {
        if (index <= 0 || index >= Length)
            return index == 0 || index == Length;

        return GetClusterStart(index) == index;
    }

    /// <summary>Whether a UTF-16 index is a caret position: a cluster boundary, or a caret position inside a ligature.</summary>
    public bool IsCaretPosition(int index) => IsClusterBoundary(index) || FindCaret(index) >= 0;

    private int FindCaret(int index)
    {
        var carets = _carets;
        if (carets.Length == 0)
            return -1;

        int low = 0, high = carets.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var value = carets[middle].Index;
            if (value < index)
            {
                low = middle + 1;
            }
            else if (value > index)
            {
                high = middle - 1;
            }
            else
            {
                return middle;
            }
        }

        return -1;
    }

    /// <summary>The UTF-16 index of the first character of the cluster that contains a character.</summary>
    public int GetClusterStart(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, Length);
        if (index == Length)
            return Length;

        var glyphs = _glyphs;
        var low = 0;
        var high = glyphs.Length - 1;
        var result = 0;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            if (glyphs[middle].Cluster <= index)
            {
                result = glyphs[middle].Cluster;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return result;
    }

    /// <summary>
    /// Whether breaking the text before a UTF-16 index and shaping the two parts separately would give other glyphs: a cluster
    /// boundary flagged <see cref="ShapedGlyphFlags.UnsafeToBreak"/>, or an index inside a cluster (e.g. a hyphenation
    /// opportunity inside a ligature, which the soft hyphens do not prevent, as in HarfBuzz): the line breaking then shapes the
    /// parts of the lines again.
    /// </summary>
    public bool IsUnsafeToBreak(int index)
    {
        if (index <= 0 || index >= Length || _glyphs.Length == 0)
            return false;

        var glyph = FindGlyph(_glyphs, index);
        if (glyph == _glyphs.Length || _glyphs[glyph].Cluster != index)
            return glyph > 0;

        return _glyphs[glyph].IsUnsafeToBreak;
    }

    /// <summary>
    /// The distance in CSS px along the inline axis from the inline-start edge of the run to the caret position before a
    /// character: the sum of the advances of the clusters before it, plus the caret position inside a ligature. An index
    /// inside a cluster (that is not a caret position) is snapped to the start of the cluster; <see cref="Length"/> gives
    /// <see cref="Width"/>.
    /// </summary>
    public double GetOffset(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, Length);
        return GetOffsets()[index];
    }

    /// <summary>The advance along the inline axis of the characters from <paramref name="start"/> to <paramref name="end"/> (snapped to the caret positions).</summary>
    public double GetWidth(int start, int end) => GetOffset(end) - GetOffset(start);

    /// <summary>
    /// Returns the caret position (a cluster boundary or a caret position inside a ligature, as a UTF-16 index) closest to a
    /// distance along the inline axis from the inline-start edge of the run (e.g. for <c>caretPositionFromPoint()</c>): the
    /// position before the part of the cluster under the distance, or after it when the distance is in its second half.
    /// </summary>
    public int GetIndexAtOffset(double offset)
    {
        if (offset <= 0 || Length == 0)
            return 0;

        var offsets = GetOffsets();
        var start = 0;
        for (var index = 1; index <= Length; index++)
        {
            if (index < Length && !IsCaretPosition(index))
                continue;

            if (offset < offsets[index])
                return offset - offsets[start] < (offsets[index] - offsets[start]) / 2 ? start : index;

            start = index;
        }

        return Length;
    }

    /// <summary>
    /// The offsets of the caret positions before each UTF-16 index (see <see cref="GetOffset"/>), and the width at index
    /// <see cref="Length"/>: the line breaking measures the text between its break opportunities with them.
    /// </summary>
    internal ReadOnlySpan<double> Offsets => GetOffsets();

    /// <summary>
    /// Returns the glyphs of the characters from <paramref name="start"/> to <paramref name="end"/> (cluster boundaries) with
    /// spacing adjustments, as a new shaped text whose character 0 is <paramref name="start"/>: the inline layout adjusts the
    /// spacing of the text of a line (the tab stops, the justification, the trimming of the spacing at the edges of the line,
    /// https://drafts.csswg.org/css-text-4/#spacing). <paramref name="before"/> and <paramref name="after"/> give, for each
    /// character of the range, the space inserted before its cluster (the glyphs of the cluster move along the inline axis) and
    /// after it (added to the advance of its last glyph); a negative value removes space (e.g. the blank half of a fullwidth
    /// punctuation). The values of the characters that do not start a cluster are added to their cluster.
    /// </summary>
    internal ShapedText WithSpacing(int start, int end, ReadOnlySpan<double> before, ReadOnlySpan<double> after)
    {
        var glyphs = _glyphs;
        var glyphStart = FindGlyph(glyphs, start);
        var glyphEnd = FindGlyph(glyphs, end);
        var result = new ShapedGlyph[glyphEnd - glyphStart];
        var width = 0d;
        for (var g = glyphStart; g < glyphEnd;)
        {
            // The glyphs of a cluster, and the spacing of its characters
            var cluster = glyphs[g].Cluster;
            var clusterEnd = g + 1;
            while (clusterEnd < glyphEnd && glyphs[clusterEnd].Cluster == cluster)
            {
                clusterEnd++;
            }

            var nextCluster = clusterEnd < glyphs.Length ? glyphs[clusterEnd].Cluster : Length;
            var space = 0d;
            var trailing = 0d;
            for (var c = Math.Max(cluster, start); c < Math.Min(nextCluster, end); c++)
            {
                space += before[c - start];
                trailing += after[c - start];
            }

            for (var i = g; i < clusterEnd; i++)
            {
                var glyph = glyphs[i];
                // A right-to-left text is drawn from its right edge: the larger advances move the glyphs to the left, so the
                // offsets keep them in place for the space after the cluster instead of the space before it
                var trailingSpace = i == clusterEnd - 1 ? trailing : 0;
                var advance = glyph.Advance + (i == g ? space : 0) + trailingSpace;
                var offset = IsRightToLeft ? trailingSpace : space;
                result[i - glyphStart] = glyph with { Cluster = glyph.Cluster - start, Advance = advance, OffsetX = glyph.OffsetX + offset };
                width += advance;
            }

            g = clusterEnd;
        }

        return new ShapedText(end - start, result, SliceRuns(glyphStart, glyphEnd), width, Options, SliceCarets(start, end)) { IsCombinedUpright = IsCombinedUpright, CombinedScale = CombinedScale };
    }

    /// <summary>
    /// Returns the glyphs of the characters from <paramref name="start"/> to <paramref name="end"/> as a new shaped text whose
    /// character 0 is <paramref name="start"/> (the text of an element shaped with its neighbors,
    /// https://drafts.csswg.org/css-text-3/#boundary-shaping): the glyphs of a cluster belong to the part where the cluster
    /// starts (e.g. a ligature across two elements is drawn with the first one).
    /// </summary>
    internal ShapedText Slice(int start, int end)
    {
        if (start == 0 && end == Length)
            return this;

        var glyphs = _glyphs;
        var glyphStart = FindGlyph(glyphs, start);
        var glyphEnd = FindGlyph(glyphs, end);
        var result = new ShapedGlyph[glyphEnd - glyphStart];
        var width = 0d;
        for (var i = glyphStart; i < glyphEnd; i++)
        {
            result[i - glyphStart] = glyphs[i] with { Cluster = glyphs[i].Cluster - start };
            width += glyphs[i].Advance;
        }

        return new ShapedText(end - start, result, SliceRuns(glyphStart, glyphEnd), width, Options, SliceCarets(start, end)) { IsCombinedUpright = IsCombinedUpright, CombinedScale = CombinedScale };
    }

    /// <summary>
    /// Returns this text where the glyphs of the characters from <paramref name="start"/> to <paramref name="end"/> (cluster
    /// boundaries) are replaced by the glyphs of the same characters shaped alone (<paramref name="part"/>, whose character 0
    /// is <paramref name="start"/>): the text of a line whose edges are unsafe to break (https://drafts.csswg.org/css-text-3/#line-breaking).
    /// </summary>
    internal ShapedText Replace(int start, int end, ShapedText part)
    {
        var glyphs = _glyphs;
        var glyphStart = FindGlyph(glyphs, start);
        var glyphEnd = FindGlyph(glyphs, end);
        var partGlyphs = part._glyphs;
        var result = new ShapedGlyph[glyphStart + partGlyphs.Length + glyphs.Length - glyphEnd];
        glyphs.AsSpan(0, glyphStart).CopyTo(result);
        for (var i = 0; i < partGlyphs.Length; i++)
        {
            result[glyphStart + i] = partGlyphs[i] with { Cluster = partGlyphs[i].Cluster + start };
        }

        glyphs.AsSpan(glyphEnd).CopyTo(result.AsSpan(glyphStart + partGlyphs.Length));

        // The font runs: the ones before, the ones of the part, the ones after
        var runs = new List<ShapedFontRun>(_runs.Length + part._runs.Length);
        foreach (var run in _runs)
        {
            var runEnd = Math.Min(run.GlyphStart + run.GlyphCount, glyphStart);
            if (runEnd > run.GlyphStart)
            {
                runs.Add(run with { GlyphCount = runEnd - run.GlyphStart });
            }
        }

        foreach (var run in part._runs)
        {
            runs.Add(run with { GlyphStart = run.GlyphStart + glyphStart });
        }

        var shift = partGlyphs.Length - (glyphEnd - glyphStart);
        foreach (var run in _runs)
        {
            var runStart = Math.Max(run.GlyphStart, glyphEnd);
            var runEnd = run.GlyphStart + run.GlyphCount;
            if (runEnd > runStart)
            {
                runs.Add(run with { GlyphStart = runStart + shift, GlyphCount = runEnd - runStart });
            }
        }

        var width = 0d;
        foreach (var glyph in result)
        {
            width += glyph.Advance;
        }

        // The caret positions inside the ligatures
        var carets = new List<LigatureCaret>();
        foreach (var caret in _carets)
        {
            if (caret.Index < start || caret.Index >= end)
            {
                carets.Add(caret);
            }
        }

        foreach (var caret in part._carets)
        {
            carets.Add(caret with { Index = caret.Index + start });
        }

        carets.Sort(static (a, b) => a.Index.CompareTo(b.Index));
        return new ShapedText(Length, result, [.. runs], width, Options, [.. carets]) { IsCombinedUpright = IsCombinedUpright, CombinedScale = CombinedScale };
    }

    /// <summary>The font runs of a range of glyphs, rebased to the start of the range.</summary>
    private ShapedFontRun[] SliceRuns(int glyphStart, int glyphEnd)
    {
        var runs = new List<ShapedFontRun>(_runs.Length);
        foreach (var run in _runs)
        {
            var runStart = Math.Max(run.GlyphStart, glyphStart);
            var runEnd = Math.Min(run.GlyphStart + run.GlyphCount, glyphEnd);
            if (runEnd > runStart)
            {
                runs.Add(run with { GlyphStart = runStart - glyphStart, GlyphCount = runEnd - runStart });
            }
        }

        return [.. runs];
    }

    /// <summary>The caret positions inside the ligatures of a range of characters, rebased to the start of the range.</summary>
    private LigatureCaret[]? SliceCarets(int start, int end)
    {
        if (_carets.Length == 0)
            return null;

        var carets = new List<LigatureCaret>();
        foreach (var caret in _carets)
        {
            if (caret.Index > start && caret.Index < end)
            {
                carets.Add(caret with { Index = caret.Index - start });
            }
        }

        return carets.Count == 0 ? null : [.. carets];
    }

    /// <summary>The first glyph whose cluster starts at or after a UTF-16 index (the glyphs are in logical order).</summary>
    internal static int FindGlyph(ReadOnlySpan<ShapedGlyph> glyphs, int index)
    {
        var low = 0;
        var high = glyphs.Length;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (glyphs[middle].Cluster < index)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>The offsets of the caret positions before each UTF-16 index, and the width at <see cref="Length"/>.</summary>
    private double[] GetOffsets()
    {
        if (_offsets is not null)
            return _offsets;

        var offsets = new double[Length + 1];
        var glyphs = _glyphs;
        var carets = _carets;
        var caret = 0;
        var glyph = 0;
        var pen = 0d;
        var clusterOffset = 0d;
        var current = 0d;
        for (var index = 0; index < Length; index++)
        {
            if (glyph < glyphs.Length && glyphs[glyph].Cluster == index)
            {
                clusterOffset = pen;
                current = pen;
                while (glyph < glyphs.Length && glyphs[glyph].Cluster == index)
                {
                    pen += glyphs[glyph].Advance;
                    glyph++;
                }
            }
            else if (caret < carets.Length && carets[caret].Index == index)
            {
                // A caret position inside a ligature
                current = clusterOffset + carets[caret].Offset;
            }

            while (caret < carets.Length && carets[caret].Index <= index)
            {
                caret++;
            }

            offsets[index] = current;
        }

        offsets[Length] = Width;
        _offsets = offsets;
        return offsets;
    }
}
