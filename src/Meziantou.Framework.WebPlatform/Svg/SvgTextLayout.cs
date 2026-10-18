using System.Globalization;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Unicode;
using static Meziantou.Framework.WebPlatform.Svg.SvgPathData;

namespace Meziantou.Framework.WebPlatform.Svg;

/// <summary>
/// A character of the layout of an SVG text (see <see cref="SvgTextLayout"/>): an addressable character
/// (https://svgwg.org/svg2-draft/text.html#TermAddressableCharacter, a UTF-16 code unit that is not removed by the white space
/// processing) and its glyph cell, in the user space of the text element.
/// </summary>
/// <remarks>
/// The position is the start of the character on its baseline, in the direction of the progression of the character
/// (<see cref="Progression"/>: a unit vector, rotated by the <c>rotate</c> attribute and by the tangent of a text path; the
/// progression of a character of a right-to-left run is the opposite of the direction of its line). The glyph cell spans <see cref="Advance"/> along the progression and [<see cref="CellStart"/>, <see cref="CellEnd"/>] along
/// <see cref="Across"/> (the unit vector towards the bottom of the glyphs of horizontal text). The code units after the first
/// one of a typographic character (a cluster of the text shaping) have the position of the first one and no advance.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal struct SvgTextCharacter
{
    /// <summary>The innermost text content element (text, tspan, textPath) or a element that contains the character.</summary>
    public Element Element;

    /// <summary>The style of the text of the character (the style of its parent element).</summary>
    public ComputedStyle Style;

    /// <summary>The text node of the character, and the offset of the character in it.</summary>
    public Text TextNode;
    public int TextOffset;

    public SvgPoint Position;
    public SvgPoint Progression;
    public SvgPoint Across;
    public double Advance;
    public double CellStart;
    public double CellEnd;

    /// <summary>The value of getRotationOfChar(): the rotate attribute, the tangent of the path, and the rotation of a sideways glyph.</summary>
    public double Rotation;

    /// <summary>Whether the character is the first code unit of a typographic character.</summary>
    public bool IsClusterStart;

    /// <summary>The shaped text of the run of the character (for the painting of its glyphs), or null.</summary>
    public Css.Fonts.ShapedText? Shaped;

    /// <summary>The index of the first glyph of the typographic character in <see cref="Shaped"/>, and the number of its glyphs (0 for the code units after the first one).</summary>
    public int GlyphStart;
    public int GlyphCount;

    /// <summary>Whether the glyphs are upright in vertical text (not rotated sideways).</summary>
    public bool IsUpright;

    /// <summary>
    /// Whether the character is in a right-to-left run of its line (its embedding level is odd, https://www.unicode.org/reports/tr9/):
    /// its glyphs are drawn from its end position, against its progression.
    /// </summary>
    public bool IsRightToLeft;

    /// <summary>
    /// The scale of the glyphs along the progression minus one (<c>lengthAdjust="spacingAndGlyphs"</c> stretches or compresses
    /// the glyphs, https://svgwg.org/svg2-draft/text.html#TextElementLengthAdjustAttribute): 0 for the glyphs that are not scaled.
    /// </summary>
    public double GlyphStretch;

    /// <summary>The scale of the glyphs along the progression.</summary>
    public readonly double GlyphScale => GlyphStretch + 1;

    /// <summary>The distance of the alphabetic baseline of the glyphs from the position, along <see cref="Across"/>.</summary>
    public readonly double BaselineOffset => CellStart + (Style?.PrimaryFontMetrics.Ascent ?? 0);

    /// <summary>Whether the character is not rendered (https://svgwg.org/svg2-draft/text.html#TextLayoutAlgorithm: it is beyond the ends of its text path).</summary>
    public bool IsHidden;

    /// <summary>The end of the character: its position moved by its advance along the progression.</summary>
    public readonly SvgPoint EndPosition => Position + (Progression * Advance);

    /// <summary>Returns the four corners of the glyph cell.</summary>
    public readonly (SvgPoint P1, SvgPoint P2, SvgPoint P3, SvgPoint P4) GetCell()
    {
        var start = Position + (Across * CellStart);
        var end = Position + (Across * CellEnd);
        var advance = Progression * Advance;
        return (start, start + advance, end + advance, end);
    }

    /// <summary>Whether a point is in the glyph cell.</summary>
    public readonly bool Contains(SvgPoint point)
    {
        var delta = point - Position;
        var along = (delta.X * Progression.X) + (delta.Y * Progression.Y);
        var across = (delta.X * Across.X) + (delta.Y * Across.Y);
        return along >= 0 && along <= Advance && across >= CellStart && across <= CellEnd;
    }
}

/// <summary>
/// The layout of an SVG text element (https://svgwg.org/svg2-draft/text.html#TextLayoutAlgorithm): its characters after the
/// white space processing, shaped (the fonts, the kerning, <c>letter-spacing</c>, <c>word-spacing</c>) and reordered by the
/// Unicode Bidirectional Algorithm (<c>direction</c> and <c>unicode-bidi</c>) on a single line in its writing mode, positioned
/// by the <c>x</c>, <c>y</c>, <c>dx</c>, <c>dy</c> and <c>rotate</c> attributes, adjusted by <c>textLength</c> and
/// <c>lengthAdjust</c>, anchored by <c>text-anchor</c> per anchored chunk, and laid out along the path of the <c>textPath</c>
/// elements. Computed on demand and cached on the text element until the styles change.
/// </summary>
/// <remarks>
/// <para>
/// A text element with an <c>inline-size</c> is auto-wrapped (https://svgwg.org/svg2-draft/text.html#InlineSize): its content is
/// broken into the lines of a block of that inline size (the soft wrap opportunities and the forced breaks of CSS Text, the line
/// boxes of CSS Inline), and each line is anchored by <c>text-anchor</c> at the position of the first character; the positioning
/// attributes other than the first <c>x</c> and <c>y</c>, <c>textLength</c> and the text paths are ignored.
/// </para>
/// <para>
/// The content areas of <c>shape-inside</c> (and <c>shape-subtract</c>) are not supported (the text is not wrapped), neither are
/// <c>side</c>, <c>method</c> and <c>spacing</c> of <c>textPath</c>.
/// </para>
/// </remarks>
internal sealed class SvgTextLayout
{
    private readonly Dictionary<Element, (int Start, int End)> _ranges;

    private SvgTextLayout(Element root, SvgTextCharacter[] characters, Dictionary<Element, (int Start, int End)> ranges, List<Element> elements, int version, SvgOutermostViewport? viewport)
    {
        Root = root;
        Characters = characters;
        _ranges = ranges;
        Elements = elements;
        Version = version;
        Viewport = viewport;
    }

    /// <summary>The text element.</summary>
    public Element Root { get; }

    /// <summary>The addressable characters of the text element, in logical order.</summary>
    public SvgTextCharacter[] Characters { get; }

    /// <summary>The text element and its rendered text content and a elements, in tree order.</summary>
    public List<Element> Elements { get; }

    private int Version { get; }

    private SvgOutermostViewport? Viewport { get; }

    /// <summary>Returns the range of the characters of a text content element (or of an a element) of the text.</summary>
    public bool TryGetRange(Element element, out int start, out int count)
    {
        if (_ranges.TryGetValue(element, out var range))
        {
            start = range.Start;
            count = range.End - range.Start;
            return true;
        }

        start = count = 0;
        return false;
    }

    /// <summary>
    /// Returns the layout of the text element that contains a text content element (or an a element in a text), or null when it
    /// is not rendered (it is not connected, not in a text element of an svg element, or has display: none).
    /// </summary>
    /// <param name="element">The text content element.</param>
    /// <param name="viewport">The viewport of the outermost svg element, or null.</param>
    /// <param name="inResource">
    /// Whether the text is painted as the content of a resource or of a use element (e.g. in a clipPath or a defs element): it is
    /// laid out unless it has display: none.
    /// </param>
    public static SvgTextLayout? Get(Element element, SvgOutermostViewport? viewport = null, bool inResource = false)
    {
        if (!element.IsConnected || GetTextElement(element) is not SvgTextElement root || SvgUserSpace.GetOutermostSvgElement(root) is null)
            return null;

        var engine = root.NodeDocument.GetStyleEngine();
        var style = engine.GetStyle(root);
        var generation = BoxGeneration.Get(style);
        if (inResource ? style.Layout.Display.Box == DisplayBox.None || generation.Kind == BoxGenerationKind.NoStyle : !generation.IsBeingRendered)
            return null;

        viewport ??= SvgUserSpace.GetOutermostSvgElement(root) is { } outermost ? SvgUserSpace.GetOutermostViewport(outermost) : null;
        var version = engine.Version;
        if (root.CachedTextLayout is { } cached && cached.Version == version && cached.Viewport == viewport)
            return cached.TryGetRange(element, out _, out _) ? cached : null;

        var layout = new Builder(root, viewport).Build(version);
        root.CachedTextLayout = layout;
        return layout.TryGetRange(element, out _, out _) ? layout : null;
    }

    /// <summary>Returns the text element of a text content element: itself, or its nearest text ancestor.</summary>
    private static Element? GetTextElement(Element element)
    {
        for (Element? current = element; current is SvgElement; current = current.ParentNode as Element)
        {
            if (current is SvgTextElement)
                return current;

            if (current is not (SvgTSpanElement or SvgTextPathElement or SvgAElement))
                return null;
        }

        return null;
    }

    /// <summary>
    /// Returns the bounding box of the glyph cells of the characters of an element (https://svgwg.org/svg2-draft/coords.html#BoundingBoxes:
    /// "the union of the glyph cells of the text content"), in the user space of the text element, or null when it has none.
    /// </summary>
    public (double MinX, double MinY, double MaxX, double MaxY)? GetBoundingBox(Element element)
    {
        if (!TryGetRange(element, out var start, out var count))
            return null;

        (double MinX, double MinY, double MaxX, double MaxY)? result = null;
        for (var i = start; i < start + count; i++)
        {
            ref readonly var character = ref Characters[i];
            if (character.IsHidden || !character.IsClusterStart)
                continue;

            var (p1, p2, p3, p4) = character.GetCell();
            foreach (var point in (ReadOnlySpan<SvgPoint>)[p1, p2, p3, p4])
            {
                result = result is { } box
                    ? (Math.Min(box.MinX, point.X), Math.Min(box.MinY, point.Y), Math.Max(box.MaxX, point.X), Math.Max(box.MaxY, point.Y))
                    : (point.X, point.Y, point.X, point.Y);
            }
        }

        return result;
    }

    private sealed class Builder(SvgTextElement root, SvgOutermostViewport? viewport)
    {
        private readonly List<Item> _items = [];
        private readonly Dictionary<Element, (int Start, int End)> _ranges = new(ReferenceEqualityComparer.Instance);
        private readonly List<Element> _positioningElements = [];
        private readonly List<Element> _elements = [];
        private bool _previousIsCollapsibleSpace = true;

        /// <summary>Whether the previous character is a word character (for <c>text-transform: capitalize</c>).</summary>
        private bool _inWord;

        /// <summary>The rotation of the coordinate system of the characters placed on a path (the tangent of the path), or null.</summary>
        private double?[]? _pathRotations;

        /// <summary>
        /// The bidi control codes of <c>unicode-bidi</c> (https://drafts.csswg.org/css-writing-modes-4/#bidi-control-codes-injection)
        /// inserted before the items, in order.
        /// </summary>
        private readonly List<(int Index, string Controls)> _bidiControls = [];

        /// <summary>
        /// The resolution of the embedding levels of the text with the injected control codes, the index of each item in that
        /// text (and its length, after the last item), and the level of each item; null when the whole text is left-to-right.
        /// </summary>
        private BidiResolver? _bidi;
        private int[]? _bidiIndexes;
        private byte[]? _levels;

        /// <summary>Whether the text is auto-wrapped (it has an <c>inline-size</c>): the text paths are ignored.</summary>
        private bool _isWrapped;

        public SvgTextLayout Build(int version)
        {
            var rootStyle = root.NodeDocument.GetStyleEngine().GetStyle(root);
            _positioningElements.Add(root);
            _elements.Add(root);
            Collect(root, inTextPath: null);
            _ranges[root] = (0, _items.Count);

            // https://drafts.csswg.org/css-text-3/#white-space-phase-2: the collapsible space at the end of the line is removed
            if (_items.Count > 0 && _items[^1].IsCollapsibleSpace)
            {
                _items.RemoveAt(_items.Count - 1);
                foreach (var (element, range) in _ranges.ToArray())
                {
                    _ranges[element] = (Math.Min(range.Start, _items.Count), Math.Min(range.End, _items.Count));
                }
            }

            var characters = new SvgTextCharacter[_items.Count];
            _pathRotations = new double?[_items.Count];
            var layout = rootStyle.Layout;
            var mode = layout.WritingMode;
            var isVertical = mode != WritingMode.HorizontalTb;

            // The direction of the lines along the inline axis (sideways-lr: the lines are read from the bottom to the top), and
            // the direction of the paragraph along it
            var lineSign = mode == WritingMode.SidewaysLr ? -1 : 1;
            ResolveBidi(layout);
            var isRightToLeft = _bidi is { Paragraphs: [var paragraph, ..] } ? paragraph.IsRightToLeft : layout.Direction == Direction.Rtl;
            var progressionSign = lineSign * (isRightToLeft ? -1 : 1);
            Shape(characters, isVertical, lines: null);
            var lines = GetInlineSize(rootStyle, isVertical) is { } inlineSize ? BreakLines(characters, inlineSize) : null;
            _isWrapped = lines is not null;
            if (lines is { Count: > 1 })
            {
                // The lines are shaped separately (no kerning nor ligature across a line break)
                Shape(characters, isVertical, lines);
            }

            ResolveCharacterAttributes(characters, out var absolute, out var dx, out var dy, out var rotations);
            if (lines is not null)
            {
                // https://svgwg.org/svg2-draft/text.html#TSpanNotes: "the x, y, dx, dy, and rotate attributes are ignored for
                // auto-wrapped text (except for the initial current text position when the content area is specified by the
                // inline-size property)"; https://svgwg.org/svg2-draft/text.html#TextElementTextLengthAttribute: "The textLength
                // attribute is only applied when the wrapping area is not defined by the shape-inside or the inline-size properties"
                Array.Clear(rotations);
                PositionLines(characters, lines, rootStyle, mode, lineSign, progressionSign, characters.Length > 0 ? absolute[0] : default);
            }
            else
            {
                LayoutLine(characters, 0, characters.Length, isVertical, lineSign, across: 0);

                // The shifts of dx and dy (https://svgwg.org/svg2-draft/text.html#TextLayoutAdjustPositionsDxDy), in logical order
                var shift = default(SvgPoint);
                for (var i = 0; i < characters.Length; i++)
                {
                    shift += new SvgPoint(dx[i], dy[i]);
                    characters[i].Position += shift;
                }

                if (!isVertical)
                {
                    ApplyLineRelativeShifts(characters, absolute);
                }

                ApplyTextLength(root, characters, isVertical, lineSign, progressionSign);
                PositionChunks(characters, isVertical, lineSign, progressionSign, absolute);
            }

            FinalizeCharacters(characters, isVertical, lineSign, mode, rotations);
            return new SvgTextLayout(root, characters, _ranges, _elements, version, viewport);
        }

        // ------------------------------------------------------------------------------------------------------------
        // Content and white space processing (https://svgwg.org/svg2-draft/text.html#WhiteSpace)
        // ------------------------------------------------------------------------------------------------------------

        private void Collect(Element parent, Element? inTextPath)
        {
            var styleEngine = parent.NodeDocument.GetStyleEngine();
            var parentStyle = styleEngine.GetStyle(parent);
            for (var node = parent.FirstChild; node is not null; node = node.NextSibling)
            {
                if (node is Text text)
                {
                    AppendText(text, parent, parentStyle, inTextPath);
                    continue;
                }

                if (node is not SvgElement child)
                    continue;

                // https://svgwg.org/svg2-draft/text.html#TextElement: the content of a text element is text, tspan, textPath and a elements
                var isTextPath = child is SvgTextPathElement;
                if (!(child is SvgTSpanElement or SvgAElement || (isTextPath && inTextPath is null)))
                    continue;

                var style = styleEngine.GetStyle(child);
                if (style.Layout.Display.IsNone)
                    continue;

                var start = _items.Count;
                if (child is SvgTSpanElement)
                {
                    _positioningElements.Add(child);
                }

                _elements.Add(child);

                // https://drafts.csswg.org/css-writing-modes-4/#unicode-bidi: the text content elements are inline boxes
                var unicodeBidi = style.Layout.UnicodeBidi;
                if (unicodeBidi != UnicodeBidi.Normal)
                {
                    _bidiControls.Add((_items.Count, BidiControls.GetInlineStart(unicodeBidi, style.Layout.Direction == Direction.Rtl)));
                }

                Collect(child, isTextPath ? child : inTextPath);
                if (unicodeBidi != UnicodeBidi.Normal)
                {
                    _bidiControls.Add((_items.Count, BidiControls.GetInlineEnd(unicodeBidi)));
                }

                _ranges[child] = (start, _items.Count);
            }
        }

        private void AppendText(Text text, Element element, ComputedStyle style, Element? textPath)
        {
            var data = text.Data;
            var collapse = style.Layout.WhiteSpaceCollapse;

            // https://drafts.csswg.org/css-text-4/#text-transform-property: the characters are transformed (one character for one
            // addressable character: a transformation into several characters keeps the original character)
            var transform = style.Layout.TextTransform;
            var turkic = transform != TextTransform.None && TextTransformer.UsesTurkicCasing(element);
            Span<char> transformed = stackalloc char[TextTransformer.MaxLength];
            for (var i = 0; i < data.Length; i++)
            {
                var c = data[i];
                var isWhiteSpace = c is ' ' or '\t' or '\n' or '\r';
                if (isWhiteSpace)
                {
                    _inWord = false;
                    switch (collapse)
                    {
                        case WhiteSpaceCollapse.Discard:
                            continue;

                        case WhiteSpaceCollapse.Collapse:
                        case WhiteSpaceCollapse.PreserveBreaks when c is not ('\n' or '\r'):
                            // https://drafts.csswg.org/css-text-3/#white-space-phase-1: the sequences of collapsible spaces are
                            // one space, removed at the start of the line (and at its end, see Build)
                            if (_previousIsCollapsibleSpace)
                                continue;

                            Add(' ', isCollapsibleSpace: true, i);
                            continue;

                        case WhiteSpaceCollapse.PreserveSpaces:
                            Add(' ', isCollapsibleSpace: false, i);
                            continue;

                        default:
                            // A preserved segment break: there is a single line (no automatic wrapping), it has no advance
                            Add(c is '\t' ? ' ' : c, isCollapsibleSpace: false, i);
                            continue;
                    }
                }

                if (transform != TextTransform.None && TextTransformer.Transform(c, transform, turkic, wordStart: !_inWord, transformed) == 1)
                {
                    _inWord = TextTransformer.IsWordCharacter(c);
                    c = transformed[0];
                }
                else
                {
                    _inWord = TextTransformer.IsWordCharacter(c);
                }

                Add(c, isCollapsibleSpace: false, i);
            }

            void Add(char character, bool isCollapsibleSpace, int offset)
            {
                _items.Add(new Item(character, element, style, textPath, isCollapsibleSpace, text, offset));
                _previousIsCollapsibleSpace = isCollapsibleSpace;
            }
        }

        // ------------------------------------------------------------------------------------------------------------
        // Shaping: the advances of the typographic characters
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Shapes the runs of the characters that have the same style and the same embedding level, in the direction of their
        /// level; with <paramref name="lines"/>, the runs end at the ends of the lines.
        /// </summary>
        private void Shape(SvgTextCharacter[] characters, bool isVertical, List<Line>? lines)
        {
            var buffer = new char[_items.Count];
            for (var i = 0; i < _items.Count; i++)
            {
                buffer[i] = _items[i].Character;
            }

            var runStart = 0;
            var line = 0;
            while (runStart < _items.Count)
            {
                var limit = _items.Count;
                if (lines is not null)
                {
                    while (lines[line].End <= runStart)
                    {
                        line++;
                    }

                    limit = lines[line].End;
                }

                var style = _items[runStart].Style;
                var level = _levels is null ? (byte)0 : _levels[runStart];
                var runEnd = runStart + 1;
                while (runEnd < limit && ReferenceEquals(_items[runEnd].Style, style) && (_levels is null || _levels[runEnd] == level))
                {
                    runEnd++;
                }

                var options = style.ShapingOptions;
                var isRightToLeft = (level & 1) != 0;
                if (options.IsRightToLeft != isRightToLeft)
                {
                    options = options with { IsRightToLeft = isRightToLeft };
                }

                var shaped = Css.Fonts.TextShaper.Shape(buffer.AsSpan(runStart, runEnd - runStart), style.Fonts, options);
                var glyphs = shaped.Glyphs;
                var metrics = style.PrimaryFontMetrics;
                for (var i = runStart; i < runEnd; i++)
                {
                    ref var character = ref characters[i];
                    character.Element = _items[i].Element;
                    character.Style = style;
                    character.TextNode = _items[i].TextNode;
                    character.TextOffset = _items[i].TextOffset;
                    character.IsClusterStart = shaped.IsClusterBoundary(i - runStart);
                    character.Advance = 0;
                    character.Shaped = null;
                    character.GlyphStart = character.GlyphCount = 0;
                }

                var g = 0;
                while (g < glyphs.Length)
                {
                    var cluster = glyphs[g].Cluster;
                    var clusterStart = g;
                    var advance = 0d;
                    var upright = !isVertical || glyphs[g].IsUpright;
                    while (g < glyphs.Length && glyphs[g].Cluster == cluster)
                    {
                        advance += glyphs[g].Advance;
                        g++;
                    }

                    ref var character = ref characters[runStart + cluster];
                    character.Advance = advance;
                    character.IsClusterStart = true;
                    character.Shaped = shaped;
                    character.GlyphStart = clusterStart;
                    character.GlyphCount = g - clusterStart;
                    character.IsUpright = isVertical && upright;

                    // The rotation of a sideways glyph of vertical text is added in FinalizeCharacters
                    character.Rotation = upright ? 0 : 90;
                }

                for (var i = runStart; i < runEnd; i++)
                {
                    ref var character = ref characters[i];
                    var (start, end) = GetCellExtent(character.Style, metrics, isVertical);
                    character.CellStart = start;
                    character.CellEnd = end;
                }

                runStart = runEnd;
            }
        }

        /// <summary>
        /// The extent of the glyph cells across the progression (https://svgwg.org/svg2-draft/text.html#TermGlyphCell): from the
        /// ascent to the descent of the font, shifted by the dominant baseline (the alignment point is on the baseline), centered
        /// on the central baseline in vertical text.
        /// </summary>
        private static (double Start, double End) GetCellExtent(ComputedStyle style, Css.Fonts.UsedFontMetrics metrics, bool isVertical)
        {
            var ascent = metrics.Ascent;
            var descent = metrics.Descent;
            if (isVertical)
                return (-(ascent + descent) / 2, (ascent + descent) / 2);

            // https://drafts.csswg.org/css-inline-3/#dominant-baseline-property, https://drafts.csswg.org/css-inline-3/#alignment-baseline-property:
            // the glyphs are moved so that the baseline is at the position
            var baseline = style.GetComputedValue("alignment-baseline") is { } alignment and not ("baseline" or "auto" or "") ? alignment : style.GetComputedValue("dominant-baseline");
            var offset = baseline switch
            {
                "text-top" or "text-before-edge" or "before-edge" => ascent,
                "hanging" => ascent * 0.8,
                "central" => (ascent - descent) / 2,
                "middle" => metrics.XHeight / 2,
                "mathematical" => ascent / 2,
                "ideographic" or "text-bottom" or "text-after-edge" or "after-edge" => -descent,
                _ => 0,
            };

            offset -= GetBaselineShift(style);
            return (-ascent + offset, descent + offset);
        }

        /// <summary>https://drafts.csswg.org/css-inline-3/#baseline-shift-property: the upward shift of the baseline (the shifts of the element and of its ancestors in the text).</summary>
        private static double GetBaselineShift(ComputedStyle style)
        {
            var result = 0d;
            for (Element? element = style.Element; element is SvgElement; element = element.ParentNode as Element)
            {
                var elementStyle = element.NodeDocument.GetStyleEngine().GetStyle(element);
                var fontSize = elementStyle.FontSize;
                result += elementStyle.GetComputedValue("baseline-shift") switch
                {
                    "baseline" or "" or "0px" => 0,
                    "sub" => -fontSize / 5,
                    "super" => fontSize / 3,
                    // "Raise (positive value) or lower (negative value) by the specified percentage of the line-height"
                    var value => SvgUserSpace.ResolveLength(element, value, elementStyle.Layout.UsedLineHeight) ?? 0,
                };

                if (element is SvgTextElement)
                    break;
            }

            return result;
        }

        /// <summary>
        /// The line-relative values of <c>baseline-shift</c> (https://drafts.csswg.org/css-inline-3/#baseline-shift-property) in
        /// horizontal text: <c>top</c>, <c>center</c> and <c>bottom</c> align the aligned subtree of the element (the layout bounds
        /// of its characters: their glyph cells and the half-leading of their line height) with the edges or the center of the
        /// line box of the text, which is as tall as the strut of the text element and the other characters, then as the aligned
        /// subtrees (https://drafts.csswg.org/css2/#line-height). The glyph cells are moved across the line. As in Firefox, a
        /// <c>y</c> attribute gives the position of the baseline of its character once shifted: the following characters keep
        /// their positions relative to it.
        /// </summary>
        private void ApplyLineRelativeShifts(SvgTextCharacter[] characters, (double? X, double? Y)[] absolute)
        {
            var styleEngine = root.NodeDocument.GetStyleEngine();
            var hasLineRelativeShift = false;
            foreach (var element in _elements)
            {
                hasLineRelativeShift |= !ReferenceEquals(element, root) && GetLineRelativeShift(styleEngine.GetStyle(element)) != BaselineShiftKind.Length;
            }

            if (!hasLineRelativeShift)
                return;

            // The strut of the root inline box
            var rootStyle = styleEngine.GetStyle(root);
            var rootMetrics = rootStyle.PrimaryFontMetrics;
            var rootLeading = (rootStyle.Layout.UsedLineHeight - (rootMetrics.Ascent + rootMetrics.Descent)) / 2;
            var lineTop = -rootMetrics.Ascent - rootLeading;
            var lineBottom = rootMetrics.Descent + rootLeading;

            // The layout bounds of the characters, in the line box or in their aligned subtree (relative to its baseline: the
            // shifts of the ancestors of the root of an aligned subtree do not move it)
            var subtreeRoots = new Element?[characters.Length];
            var subtrees = new List<(Element Root, double Top, double Bottom)>();
            for (var i = 0; i < characters.Length; i++)
            {
                ref var character = ref characters[i];
                if (character.Style is null)
                    continue;

                Element? subtreeRoot = null;
                for (var element = character.Element; element is SvgElement && !ReferenceEquals(element, root); element = element.ParentNode as Element)
                {
                    if (GetLineRelativeShift(styleEngine.GetStyle(element)) != BaselineShiftKind.Length)
                    {
                        subtreeRoot = element;
                        break;
                    }
                }

                subtreeRoots[i] = subtreeRoot;
                if (subtreeRoot is not null)
                {
                    var ancestorShift = GetBaselineShift(styleEngine.GetStyle(subtreeRoot));
                    character.CellStart += ancestorShift;
                    character.CellEnd += ancestorShift;
                }

                var leading = (character.Style.Layout.UsedLineHeight - (character.CellEnd - character.CellStart)) / 2;
                var top = character.CellStart - leading;
                var bottom = character.CellEnd + leading;
                if (subtreeRoot is null)
                {
                    lineTop = Math.Min(lineTop, top);
                    lineBottom = Math.Max(lineBottom, bottom);
                    continue;
                }

                var index = subtrees.FindIndex(subtree => ReferenceEquals(subtree.Root, subtreeRoot));
                if (index < 0)
                {
                    subtrees.Add((subtreeRoot, top, bottom));
                }
                else
                {
                    subtrees[index] = (subtreeRoot, Math.Min(subtrees[index].Top, top), Math.Max(subtrees[index].Bottom, bottom));
                }
            }

            // https://drafts.csswg.org/css2/#valdef-vertical-align-top: the aligned subtrees may make the line taller
            foreach (var (subtreeRoot, top, bottom) in subtrees)
            {
                var height = bottom - top;
                if (height <= lineBottom - lineTop)
                    continue;

                switch (GetLineRelativeShift(styleEngine.GetStyle(subtreeRoot)))
                {
                    case BaselineShiftKind.Bottom:
                        lineTop = lineBottom - height;
                        break;

                    case BaselineShiftKind.Center:
                        var extra = (height - (lineBottom - lineTop)) / 2;
                        lineTop -= extra;
                        lineBottom += extra;
                        break;

                    default:
                        lineBottom = lineTop + height;
                        break;
                }
            }

            // The positions of the baselines of the aligned subtrees, below the baseline of the root inline box
            var anchor = 0d;
            for (var i = 0; i < characters.Length; i++)
            {
                var shift = 0d;
                if (subtreeRoots[i] is { } subtreeRoot)
                {
                    var (_, top, bottom) = subtrees.Find(subtree => ReferenceEquals(subtree.Root, subtreeRoot));
                    shift = GetLineRelativeShift(styleEngine.GetStyle(subtreeRoot)) switch
                    {
                        BaselineShiftKind.Bottom => lineBottom - bottom,
                        BaselineShiftKind.Center => ((lineTop + lineBottom) / 2) - ((top + bottom) / 2),
                        _ => lineTop - top,
                    };
                }

                if (i == 0 || absolute[i].Y is not null)
                {
                    anchor = shift;
                }

                characters[i].CellStart += shift - anchor;
                characters[i].CellEnd += shift - anchor;
            }
        }

        /// <summary>Returns the line-relative value of <c>baseline-shift</c> of a style (<c>top</c>, <c>center</c>, <c>bottom</c>), or <see cref="BaselineShiftKind.Length"/>.</summary>
        private static BaselineShiftKind GetLineRelativeShift(ComputedStyle style) => style.GetComputedValue("baseline-shift") switch
        {
            "top" => BaselineShiftKind.Top,
            "center" => BaselineShiftKind.Center,
            "bottom" => BaselineShiftKind.Bottom,
            _ => BaselineShiftKind.Length,
        };

        // ------------------------------------------------------------------------------------------------------------
        // Bidirectional text, lines (https://svgwg.org/svg2-draft/text.html#TextLayoutAlgorithm, step 1: the CSS layout)
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Resolves the embedding levels of the characters (https://www.unicode.org/reports/tr9/): the text element is the block
        /// container of a paragraph (its <c>direction</c> is the direction of the paragraph), and the text content elements are
        /// inline boxes whose <c>unicode-bidi</c> inserts control codes in the text. Nothing is resolved for a left-to-right
        /// text without right-to-left characters nor control codes.
        /// </summary>
        private void ResolveBidi(LayoutStyle rootLayout)
        {
            var isRightToLeft = rootLayout.Direction == Direction.Rtl;
            var blockStart = BidiControls.GetBlockContainerStart(rootLayout.UnicodeBidi, isRightToLeft);
            if (!isRightToLeft && blockStart.Length == 0 && _bidiControls.Count == 0 && rootLayout.UnicodeBidi != UnicodeBidi.Plaintext)
            {
                // The right-to-left characters and the bidi control codes are after U+0590
                var needsBidi = false;
                foreach (var item in _items)
                {
                    if (item.Character >= 0x590)
                    {
                        needsBidi = true;
                        break;
                    }
                }

                if (!needsBidi)
                    return;
            }

            var count = _items.Count;
            var text = new List<char>(count + 8);
            var indexes = new int[count + 1];
            text.AddRange(blockStart);
            var control = 0;
            for (var i = 0; i < count; i++)
            {
                for (; control < _bidiControls.Count && _bidiControls[control].Index <= i; control++)
                {
                    text.AddRange(_bidiControls[control].Controls);
                }

                indexes[i] = text.Count;
                text.Add(_items[i].Character);
            }

            indexes[count] = text.Count;
            for (; control < _bidiControls.Count; control++)
            {
                text.AddRange(_bidiControls[control].Controls);
            }

            text.AddRange(BidiControls.GetBlockContainerEnd(rootLayout.UnicodeBidi));
            _bidi = new BidiResolver();
            _bidi.Resolve(CollectionsMarshal.AsSpan(text), BidiControls.GetParagraphDirection(rootLayout.UnicodeBidi, isRightToLeft));
            _bidiIndexes = indexes;
            _levels = new byte[count];
            var levels = _bidi.Levels;
            for (var i = 0; i < count; i++)
            {
                _levels[i] = levels[indexes[i]];
            }
        }

        /// <summary>
        /// Returns the inline size of the wrapping area of the text (https://svgwg.org/svg2-draft/text.html#InlineSize), or null
        /// when the text is not auto-wrapped: <c>inline-size</c> is the logical name of <c>width</c> (of <c>height</c> in
        /// vertical text), its percentages "refer to the width (for horizontal text) or height (for vertical text) of the
        /// current SVG viewport", and "a value of zero disables the creation of a wrapping area".
        /// </summary>
        private double? GetInlineSize(ComputedStyle rootStyle, bool isVertical)
        {
            var value = rootStyle.GetComputedValue(isVertical ? "height" : "width");
            if (value is "auto" or "")
                return null;

            var (width, height) = SvgUserSpace.GetViewportSize(root, viewport);
            return SvgUserSpace.ResolveLength(root, value, isVertical ? height : width) is { } size and > 0 && double.IsFinite(size) ? size : null;
        }

        /// <summary>
        /// Breaks the text into the lines of a block of an inline size (https://drafts.csswg.org/css-text-4/#line-breaking): a
        /// line ends at a forced break (a preserved line feed) or at the last soft wrap opportunity before the character that
        /// overflows (at a grapheme cluster boundary with <c>overflow-wrap: anywhere | break-word</c> when there is none). The
        /// spaces at the end of a line hang (https://drafts.csswg.org/css-text-4/#white-space-phase-2): the collapsible ones
        /// have no advance.
        /// </summary>
        private List<Line> BreakLines(SvgTextCharacter[] characters, double available)
        {
            var count = _items.Count;
            var lines = new List<Line>();
            if (count == 0)
                return lines;

            var text = new char[count];
            var runs = new List<LineBreakOptionsRun>();
            ComputedStyle? previousStyle = null;
            for (var i = 0; i < count; i++)
            {
                text[i] = _items[i].Character;
                var style = _items[i].Style;
                if (!ReferenceEquals(style, previousStyle))
                {
                    previousStyle = style;
                    runs.Add(new LineBreakOptionsRun(i, Css.Layout.InlineLayoutAlgorithm.CreateLineBreakOptions(style, IsChineseOrJapanese(style))));
                }
            }

            var opportunities = new LineBreakOpportunity[count];
            new LineBreaker().FindOpportunities(text, CollectionsMarshal.AsSpan(runs), opportunities);

            // (a little more than the inline size: the sum of the advances is not exact)
            var limit = available + 0.001;
            var lineStart = 0;
            var width = 0d;
            var lastBreak = -1;
            var lastEmergencyBreak = -1;
            for (var i = 0; i < count; i++)
            {
                var item = _items[i];
                if (item.Character == '\n')
                {
                    // A forced line break: the line feed ends its line
                    lines.Add(new Line(lineStart, i + 1));
                    lineStart = i + 1;
                    width = 0;
                    lastBreak = lastEmergencyBreak = -1;
                    continue;
                }

                if (i > lineStart && characters[i].IsClusterStart && item.Style.Layout.TextWrapMode == TextWrapMode.Wrap)
                {
                    if (opportunities[i] == LineBreakOpportunity.Allowed)
                    {
                        lastBreak = i;
                    }
                    else if (opportunities[i] == LineBreakOpportunity.Emergency)
                    {
                        lastEmergencyBreak = i;
                    }
                }

                width += characters[i].Advance;
                if (width <= limit || item.Character == ' ')
                    continue;

                var breakAt = lastBreak > lineStart ? lastBreak : lastEmergencyBreak;
                if (breakAt <= lineStart)
                    continue;

                lines.Add(new Line(lineStart, breakAt));
                lineStart = breakAt;
                width = 0;
                lastBreak = lastEmergencyBreak = -1;
                i = breakAt - 1;
            }

            if (lineStart < count || lines.Count == 0)
            {
                lines.Add(new Line(lineStart, count));
            }

            return lines;

            // https://drafts.csswg.org/css-text-4/#line-break-property: some rules depend on the content language
            static bool IsChineseOrJapanese(ComputedStyle style)
            {
                if (style.Layout.Text.LineBreak is not (LineBreak.Auto or LineBreak.Normal or LineBreak.Loose) || style.Element is not { } element)
                    return false;

                var language = Css.Selectors.HtmlElementStates.GetLanguage(element);
                return language is not null && (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) || language.StartsWith("ja", StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Lays out the characters of a line from the line-left edge, in visual order (https://www.unicode.org/reports/tr9/#L2,
        /// with the levels of the line, rule L1): the position of a character is its start in its direction (the line-right edge
        /// of a character of a right-to-left run), at the distance <paramref name="across"/> across the line. The inline axis of
        /// the text on a path is the distance along the path.
        /// </summary>
        private void LayoutLine(SvgTextCharacter[] characters, int start, int end, bool isVertical, int lineSign, double across)
        {
            var pen = 0d;
            if (_bidi is null)
            {
                for (var i = start; i < end; i++)
                {
                    Place(i, isRightToLeft: false);
                }

                return;
            }

            var textStart = _bidiIndexes![start];
            var textEnd = _bidiIndexes[end];
            var textLevels = new byte[textEnd - textStart];
            _bidi.GetLineLevels(textStart, textEnd, textLevels);
            var levels = new byte[end - start];
            for (var i = start; i < end; i++)
            {
                levels[i - start] = textLevels[_bidiIndexes[i] - textStart];
            }

            var runs = new List<BidiRun>();
            BidiResolver.GetVisualRuns(levels, runs);
            foreach (var run in runs)
            {
                if (run.IsRightToLeft)
                {
                    for (var i = run.End - 1; i >= run.Start; i--)
                    {
                        Place(start + i, isRightToLeft: true);
                    }
                }
                else
                {
                    for (var i = run.Start; i < run.End; i++)
                    {
                        Place(start + i, isRightToLeft: false);
                    }
                }
            }

            void Place(int index, bool isRightToLeft)
            {
                ref var character = ref characters[index];
                character.IsRightToLeft = isRightToLeft;
                var sign = IsOnPath(index) ? 1 : lineSign;
                var inline = (isRightToLeft ? pen + character.Advance : pen) * sign;
                character.Position = isVertical ? new SvgPoint(-across, inline) : new SvgPoint(inline, across);
                pen += character.Advance;
            }
        }

        /// <summary>
        /// Positions the lines of an auto-wrapped text (https://svgwg.org/svg2-draft/text.html#InlineSize): the line boxes are
        /// stacked in the block direction (https://drafts.csswg.org/css2/#line-height: a line box is as tall as the strut of the
        /// text element and the inline boxes of its characters), the first baseline at the position given by the <c>x</c> and
        /// <c>y</c> attributes of the first character, and "each line box is shifted according to the text-anchor property" so
        /// that its start, its middle or its end is at that position.
        /// </summary>
        private void PositionLines(SvgTextCharacter[] characters, List<Line> lines, ComputedStyle rootStyle, WritingMode mode, int lineSign, int progressionSign, (double? X, double? Y) origin)
        {
            var isVertical = mode != WritingMode.HorizontalTb;
            var anchor = rootStyle.GetComputedValue("text-anchor");

            // The glyph cells extend from the baseline along the across axis (towards the bottom in horizontal text, towards the
            // left in vertical text), which is the block direction except in vertical-lr and sideways-lr
            var blockSign = mode is WritingMode.VerticalLr or WritingMode.SidewaysLr ? -1 : 1;
            var across = 0d;
            var previous = (Above: 0d, Below: 0d);
            for (var l = 0; l < lines.Count; l++)
            {
                var (start, end) = lines[l];

                // The spaces (and the line feed) that end the line hang
                var contentEnd = end;
                while (contentEnd > start && _items[contentEnd - 1].Character is ' ' or '\n')
                {
                    contentEnd--;
                    if (_items[contentEnd].IsCollapsibleSpace)
                    {
                        characters[contentEnd].Advance = 0;
                    }
                }

                var extent = GetLineExtent(characters, start, end, rootStyle, isVertical);
                if (l > 0)
                {
                    across += blockSign > 0 ? previous.Below + extent.Above : -(previous.Above + extent.Below);
                }

                previous = extent;
                LayoutLine(characters, start, end, isVertical, lineSign, across);

                var min = double.PositiveInfinity;
                var max = double.NegativeInfinity;
                for (var i = start; i < contentEnd; i++)
                {
                    ref readonly var character = ref characters[i];
                    var inline = Inline(character.Position, isVertical);
                    var after = inline + (character.Advance * GetCharacterSign(character, i, lineSign));
                    min = Math.Min(min, Math.Min(inline, after));
                    max = Math.Max(max, Math.Max(inline, after));
                }

                var shift = !double.IsFinite(min) ? 0 : anchor switch
                {
                    "middle" => -(min + max) / 2,
                    "end" => progressionSign > 0 ? -max : -min,
                    _ => progressionSign > 0 ? -min : -max,
                };

                var offset = isVertical ? new SvgPoint(origin.X ?? 0, (origin.Y ?? 0) + shift) : new SvgPoint((origin.X ?? 0) + shift, origin.Y ?? 0);
                for (var i = start; i < end; i++)
                {
                    characters[i].Position += offset;
                }
            }
        }

        /// <summary>
        /// Returns the extent of a line box before and after its baseline along the across axis: the layout bounds of the strut
        /// of the text element and of the characters of the line (their glyph cells and the half-leading of their line height).
        /// </summary>
        private static (double Above, double Below) GetLineExtent(SvgTextCharacter[] characters, int start, int end, ComputedStyle rootStyle, bool isVertical)
        {
            var (cellStart, cellEnd) = GetCellExtent(rootStyle, rootStyle.PrimaryFontMetrics, isVertical);
            var leading = (rootStyle.Layout.UsedLineHeight - (cellEnd - cellStart)) / 2;
            var above = leading - cellStart;
            var below = cellEnd + leading;
            ComputedStyle? previous = rootStyle;
            for (var i = start; i < end; i++)
            {
                ref readonly var character = ref characters[i];
                if (ReferenceEquals(character.Style, previous) || character.Style is null)
                    continue;

                previous = character.Style;
                leading = (character.Style.Layout.UsedLineHeight - (character.CellEnd - character.CellStart)) / 2;
                above = Math.Max(above, leading - character.CellStart);
                below = Math.Max(below, character.CellEnd + leading);
            }

            return (above, below);
        }

        /// <summary>Whether a character is laid out along the path of a text path.</summary>
        private bool IsOnPath(int index) => !_isWrapped && _items[index].TextPath is not null;

        /// <summary>
        /// The sign of the progression of a character along the inline axis: the direction of the lines (the text on a path
        /// progresses along the path), reversed for a character of a right-to-left run.
        /// </summary>
        private int GetCharacterSign(in SvgTextCharacter character, int index, int lineSign) => (IsOnPath(index) ? 1 : lineSign) * (character.IsRightToLeft ? -1 : 1);

        // ------------------------------------------------------------------------------------------------------------
        // Positioning (https://svgwg.org/svg2-draft/text.html#TextLayoutAlgorithm)
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// The absolute positions of the x and y attributes, the dx and dy shifts and the rotate attributes resolved per
        /// addressable character (https://svgwg.org/svg2-draft/text.html#TextLayoutAlgorithm, step 3).
        /// </summary>
        private void ResolveCharacterAttributes(SvgTextCharacter[] characters, out (double? X, double? Y)[] absolute, out double[] dx, out double[] dy, out double[] rotations)
        {
            var count = characters.Length;
            absolute = new (double? X, double? Y)[count];
            dx = new double[count];
            dy = new double[count];
            var rotate = new double?[count];
            var (width, height) = SvgUserSpace.GetViewportSize(root, viewport);

            // The descendants override the values of their ancestors: the elements are in tree order
            foreach (var element in _positioningElements)
            {
                if (!_ranges.TryGetValue(element, out var range))
                    continue;

                var xs = ParseLengths(element, "x", width);
                var ys = ParseLengths(element, "y", height);
                var dxs = ParseLengths(element, "dx", width);
                var dys = ParseLengths(element, "dy", height);
                var rotates = ParseNumbers(element.GetAnimatedAttributeValue("rotate"));
                var index = 0;
                for (var i = range.Start; i < range.End; i++)
                {
                    if (!characters[i].IsClusterStart)
                        continue;

                    if (index < xs.Count)
                    {
                        absolute[i].X = xs[index];
                    }

                    if (index < ys.Count)
                    {
                        absolute[i].Y = ys[index];
                    }

                    if (index < dxs.Count)
                    {
                        dx[i] = dxs[index];
                    }

                    if (index < dys.Count)
                    {
                        dy[i] = dys[index];
                    }

                    // "If the attribute has fewer values than characters, the last value applies to the remaining characters"
                    if (rotates.Count > 0)
                    {
                        rotate[i] = rotates[Math.Min(index, rotates.Count - 1)];
                    }

                    index++;
                }
            }

            // https://svgwg.org/svg2-draft/text.html#TextAnchoringProperties: "each text chunk has an initial current text
            // position": the one of the first chunk is the origin when the first character (in logical order) has no x or y
            // (the anchored chunk is positioned by its visual extent, wherever its first character is displayed)
            if (count > 0)
            {
                absolute[0] = (absolute[0].X ?? 0, absolute[0].Y ?? 0);
            }

            rotations = new double[count];
            var lastRotation = 0d;
            for (var i = 0; i < count; i++)
            {
                if (characters[i].IsClusterStart)
                {
                    lastRotation = rotate[i] ?? 0;
                }

                rotations[i] = lastRotation;
            }
        }

        /// <summary>
        /// https://svgwg.org/svg2-draft/text.html#TextLayoutAlgorithm, step 5: the textLength attributes, from the innermost
        /// elements. <c>lengthAdjust="spacing"</c> distributes the difference between the gaps of the typographic characters;
        /// <c>spacingAndGlyphs</c> stretches the advances, the positions and the glyphs. The characters after the element move by the difference.
        /// </summary>
        private void ApplyTextLength(Element element, SvgTextCharacter[] characters, bool isVertical, int lineSign, int progressionSign)
        {
            for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
            {
                if (_ranges.ContainsKey(child))
                {
                    ApplyTextLength(child, characters, isVertical, lineSign, progressionSign);
                }
            }

            if (!_ranges.TryGetValue(element, out var range) || range.End <= range.Start || element.GetAnimatedAttributeValue("textLength") is not { } textLengthValue)
                return;

            var (width, height) = SvgUserSpace.GetViewportSize(element, viewport);
            if (SvgUserSpace.ResolveLength(element, textLengthValue, isVertical ? height : width) is not { } textLength || textLength < 0)
                return;

            // The text on a path progresses along the path
            var sign = _items[range.Start].TextPath is null ? progressionSign : 1;
            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            var clusters = 0;
            for (var i = range.Start; i < range.End; i++)
            {
                ref readonly var character = ref characters[i];
                if (!character.IsClusterStart)
                    continue;

                var start = Inline(character.Position, isVertical);
                var end = start + (character.Advance * GetCharacterSign(character, i, lineSign));
                min = Math.Min(min, Math.Min(start, end));
                max = Math.Max(max, Math.Max(start, end));
                clusters++;
            }

            if (clusters == 0 || !double.IsFinite(min))
                return;

            var difference = textLength - (max - min);
            if (element.GetAnimatedAttributeValue("lengthAdjust") == "spacingAndGlyphs")
            {
                if (max - min <= 0)
                    return;

                var scale = textLength / (max - min);
                var origin = sign > 0 ? min : max;
                for (var i = range.Start; i < range.End; i++)
                {
                    ref var character = ref characters[i];
                    var inline = Inline(character.Position, isVertical);
                    character.Position = WithInline(character.Position, origin + ((inline - origin) * scale), isVertical);
                    character.Advance *= scale;
                    character.GlyphStretch = (character.GlyphScale * scale) - 1;
                }
            }
            else
            {
                if (clusters < 2)
                    return;

                // The typographic characters are spaced in their visual order, from the start of the paragraph (their logical
                // order when the text has a single direction)
                var gap = difference / (clusters - 1) * sign;
                int[]? ranks = null;
                if (_levels is not null)
                {
                    var keys = new double[clusters];
                    var order = new int[clusters];
                    var cluster = 0;
                    for (var i = range.Start; i < range.End; i++)
                    {
                        ref readonly var character = ref characters[i];
                        if (!character.IsClusterStart)
                            continue;

                        var start = Inline(character.Position, isVertical);
                        var end = start + (character.Advance * GetCharacterSign(character, i, lineSign));
                        keys[cluster] = sign > 0 ? Math.Min(start, end) : -Math.Max(start, end);
                        order[cluster] = cluster;
                        cluster++;
                    }

                    Array.Sort(keys, order);
                    ranks = new int[clusters];
                    for (var rank = 0; rank < clusters; rank++)
                    {
                        ranks[order[rank]] = rank;
                    }
                }

                var index = -1;
                for (var i = range.Start; i < range.End; i++)
                {
                    ref var character = ref characters[i];
                    if (character.IsClusterStart)
                    {
                        index++;
                    }

                    var rank = ranks is null || index < 0 ? index : ranks[index];
                    character.Position = WithInline(character.Position, Inline(character.Position, isVertical) + (rank * gap), isVertical);
                }
            }

            for (var i = range.End; i < characters.Length; i++)
            {
                ref var character = ref characters[i];
                character.Position = WithInline(character.Position, Inline(character.Position, isVertical) + (difference * sign), isVertical);
            }
        }

        /// <summary>
        /// https://svgwg.org/svg2-draft/text.html#TextLayoutAlgorithm, steps 6 to 8, per anchored chunk: the absolute positions of
        /// the x and y attributes, the anchoring (text-anchor), then the positioning along the path of a textPath element.
        /// </summary>
        private void PositionChunks(SvgTextCharacter[] characters, bool isVertical, int lineSign, int progressionSign, (double? X, double? Y)[] absolute)
        {
            var count = characters.Length;
            var shift = default(SvgPoint);
            var start = 0;
            Element? previousTextPath = null;
            SvgPoint? afterTextPath = null;
            while (start < count)
            {
                // The anchored chunk: up to the next absolute position in the inline axis, or to the start or the end of a text path
                var textPath = _items[start].TextPath;
                var end = start + 1;
                while (end < count && !(characters[end].IsClusterStart && ((isVertical ? absolute[end].Y : absolute[end].X) is not null || !ReferenceEquals(_items[end].TextPath, textPath))))
                {
                    end++;
                }

                for (var i = start; i < end; i++)
                {
                    ref var character = ref characters[i];
                    var css = character.Position;
                    if (i == start && textPath is not null && !ReferenceEquals(textPath, previousTextPath))
                    {
                        // The text of a text path starts at the start of the path (its inline position is a distance along the path)
                        shift = new SvgPoint(-css.X, -css.Y);
                    }
                    else if (i == start && afterTextPath is { } continuation)
                    {
                        // The text after a text path continues at the end of the text on the path
                        shift = continuation - css;
                    }

                    if (absolute[i].X is { } x && (textPath is null || !isVertical))
                    {
                        shift = new SvgPoint(x - css.X, shift.Y);
                    }

                    if (absolute[i].Y is { } y && (textPath is null || isVertical))
                    {
                        shift = new SvgPoint(shift.X, y - css.Y);
                    }

                    character.Position = css + shift;
                }

                // The text on a path progresses along the path
                Anchor(characters, start, end, isVertical, lineSign, textPath is null ? progressionSign : 1);
                afterTextPath = null;
                if (textPath is not null)
                {
                    PlaceOnPath(characters, start, end, textPath, isVertical);
                    if (end == count || !ReferenceEquals(_items[end].TextPath, textPath))
                    {
                        afterTextPath = characters[end - 1].EndPosition;
                    }
                }

                previousTextPath = textPath;
                start = end;
            }
        }

        /// <summary>https://svgwg.org/svg2-draft/text.html#TextAnchoringProperties: shifts an anchored chunk so that its start, middle or end is at its position.</summary>
        private void Anchor(SvgTextCharacter[] characters, int start, int end, bool isVertical, int lineSign, int progressionSign)
        {
            var anchor = characters[start].Style.GetComputedValue("text-anchor");

            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            for (var i = start; i < end; i++)
            {
                ref readonly var character = ref characters[i];
                var inline = Inline(character.Position, isVertical);
                var after = inline + (character.Advance * GetCharacterSign(character, i, lineSign));
                min = Math.Min(min, Math.Min(inline, after));
                max = Math.Max(max, Math.Max(inline, after));
            }

            if (!double.IsFinite(min))
                return;

            // "start": the start of the chunk (its left edge, or its right edge when the paragraph progresses to the left) is at
            // the position of its first character (in logical order); "middle": its middle; "end": its end
            var anchorPosition = Inline(characters[start].Position, isVertical);
            var shift = anchor switch
            {
                "middle" => anchorPosition - ((min + max) / 2),
                "end" => progressionSign > 0 ? anchorPosition - max : anchorPosition - min,
                _ => progressionSign > 0 ? anchorPosition - min : anchorPosition - max,
            };

            if (shift == 0)
                return;

            for (var i = start; i < end; i++)
            {
                ref var character = ref characters[i];
                character.Position = WithInline(character.Position, Inline(character.Position, isVertical) + shift, isVertical);
            }
        }

        /// <summary>
        /// https://svgwg.org/svg2-draft/text.html#TextLayoutPath: places the characters of a text path along its path: the middle
        /// of each character is at the distance along the path of its inline position plus startOffset; the characters beyond
        /// the ends of an open path are hidden. The shift across the inline axis (dy) moves the characters along the normal.
        /// </summary>
        private void PlaceOnPath(SvgTextCharacter[] characters, int start, int end, Element textPath, bool isVertical)
        {
            if (GetTextPathGeometry(textPath, out var pathLengthScale) is not { IsEmpty: false } path)
            {
                for (var i = start; i < end; i++)
                {
                    characters[i].IsHidden = true;
                }

                return;
            }

            var totalLength = path.TotalLength;
            var startOffset = 0d;
            if (textPath.GetAnimatedAttributeValue("startOffset") is { } startOffsetValue)
            {
                var trimmed = startOffsetValue.Trim();
                if (trimmed.EndsWith('%', StringComparison.Ordinal) && double.TryParse(trimmed.AsSpan(0, trimmed.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage))
                {
                    // https://svgwg.org/svg2-draft/paths.html#PathLengthAttribute: pathLength has no effect on the percentages
                    startOffset = percentage / 100 * totalLength;
                }
                else if (SvgUserSpace.ResolveLength(textPath, trimmed, totalLength) is { } length)
                {
                    // "A value of zero is valid and must be treated as a scaling factor of infinity. A value of zero scaled
                    // infinitely must remain zero"
                    startOffset = length == 0 ? 0 : length * pathLengthScale;
                }
            }

            var isClosed = path.IsClosed;
            for (var i = start; i < end; i++)
            {
                ref var character = ref characters[i];
                var inline = Inline(character.Position, isVertical);
                var across = isVertical ? character.Position.X : character.Position.Y;

                // (the position of a character of a right-to-left run is its end along the path)
                var half = character.IsRightToLeft ? -character.Advance / 2 : character.Advance / 2;
                var middle = inline + half + startOffset;
                if (isClosed && totalLength > 0)
                {
                    middle %= totalLength;
                    if (middle < 0)
                    {
                        middle += totalLength;
                    }
                }

                if (!double.IsFinite(middle) || middle < 0 || middle > totalLength)
                {
                    character.IsHidden = true;
                    continue;
                }

                var (point, tangent) = path.GetPointAndTangentAtLength(middle);
                var normal = isVertical ? new SvgPoint(tangent.Y, -tangent.X) : new SvgPoint(-tangent.Y, tangent.X);
                character.Position = point - (tangent * half) + (normal * across);
                character.Progression = character.IsRightToLeft ? tangent * -1 : tangent;
                character.Across = normal;
                _pathRotations![i] = (Math.Atan2(tangent.Y, tangent.X) * 180 / Math.PI) - (isVertical ? 90 : 0);
            }
        }

        /// <summary>
        /// Returns the path of a textPath element (https://svgwg.org/svg2-draft/text.html#TextPathElement): its path attribute when
        /// it has a valid segment, else the geometry of the element referenced by href (with its transform), in the user space of
        /// the text. <paramref name="pathLengthScale"/> is the scale of the distances given in the units of pathLength.
        /// </summary>
        private static SvgPathGeometry? GetTextPathGeometry(Element textPath, out double pathLengthScale)
        {
            pathLengthScale = 1;
            if (textPath.GetAnimatedAttributeValue("path") is { } pathValue && SvgPathData.Parse(pathValue) is { Count: > 0 } inline)
                return new SvgPathGeometry(inline);

            if (SvgLinking.GetReferencedElement(textPath) is not SvgGeometryElement referenced)
                return null;

            var commands = SvgShapeGeometry.GetPath(referenced);
            if (commands.Count == 0)
                return null;

            var transform = SvgUserSpace.GetTransform(referenced);
            if (!transform.IsIdentity)
            {
                commands = TransformPath(SvgPathData.Normalize(commands), transform);
            }

            var geometry = new SvgPathGeometry(commands);
            if (SvgStrokeStyle.GetPathLength(referenced) is { } pathLength)
            {
                pathLengthScale = pathLength == 0 ? double.PositiveInfinity : geometry.TotalLength / pathLength;
            }

            return geometry;
        }

        /// <summary>Transforms normalized path data (M, L, C and Z commands, whose transformed control points give the transformed curves).</summary>
        private static List<SvgPathCommand> TransformPath(List<SvgPathCommand> commands, SvgAffine transform)
        {
            var result = new List<SvgPathCommand>(commands.Count);
            foreach (var command in commands)
            {
                var values = new float[command.Values.Length];
                for (var i = 0; i + 1 < values.Length; i += 2)
                {
                    var point = transform.Apply(command.Values[i], command.Values[i + 1]);
                    values[i] = (float)point.X;
                    values[i + 1] = (float)point.Y;
                }

                result.Add(new SvgPathCommand(command.Type, values));
            }

            return result;
        }

        /// <summary>
        /// Sets the progression, the rotation (the rotate attribute, and the rotation of the glyphs of vertical text: 90 degrees
        /// for a sideways glyph, -90 degrees in sideways-lr) and the axes of the glyph cells. The code units after the first one of
        /// a typographic character get its position.
        /// </summary>
        private void FinalizeCharacters(SvgTextCharacter[] characters, bool isVertical, int lineSign, WritingMode mode, double[] rotations)
        {
            for (var i = 0; i < characters.Length; i++)
            {
                ref var character = ref characters[i];

                // Shape set the rotation of the sideways glyphs of vertical text (90 degrees); sideways-lr rotates them the other way
                var glyphRotation = !isVertical ? 0 : mode == WritingMode.SidewaysLr ? -character.Rotation : character.Rotation;
                if (_pathRotations![i] is { } pathRotation)
                {
                    character.Rotation = glyphRotation + pathRotation;
                }
                else
                {
                    var direction = character.IsRightToLeft ? -lineSign : lineSign;
                    character.Progression = isVertical ? new SvgPoint(0, direction) : new SvgPoint(direction, 0);

                    // (the bottom of the sideways glyphs is on the left, on the right in sideways-lr)
                    character.Across = isVertical ? new SvgPoint(mode == WritingMode.SidewaysLr ? 1 : -1, 0) : new SvgPoint(0, 1);
                    character.Rotation = glyphRotation;
                }

                if (rotations[i] != 0)
                {
                    var (sin, cos) = SvgAffine.SinCosDegrees(rotations[i]);
                    character.Progression = Rotate(character.Progression, sin, cos);
                    character.Across = Rotate(character.Across, sin, cos);
                    character.Rotation += rotations[i];
                }

                if (!character.IsClusterStart && i > 0)
                {
                    var first = i - 1;
                    while (first > 0 && !characters[first].IsClusterStart)
                    {
                        first--;
                    }

                    character.Position = characters[first].Position;
                    character.Progression = characters[first].Progression;
                    character.Across = characters[first].Across;
                    character.Rotation = characters[first].Rotation;
                    character.IsHidden = characters[first].IsHidden;
                    character.IsRightToLeft = characters[first].IsRightToLeft;
                    character.Advance = 0;
                }
            }

            static SvgPoint Rotate(SvgPoint vector, double sin, double cos) => new((vector.X * cos) - (vector.Y * sin), (vector.X * sin) + (vector.Y * cos));
        }

        private static double Inline(SvgPoint point, bool isVertical) => isVertical ? point.Y : point.X;

        private static SvgPoint WithInline(SvgPoint point, double value, bool isVertical) => isVertical ? new SvgPoint(point.X, value) : new SvgPoint(value, point.Y);

        private static List<double> ParseLengths(Element element, string attribute, double percentageBasis)
        {
            var result = new List<double>();
            if (element.GetAnimatedAttributeValue(attribute) is not { } value)
                return result;

            foreach (var item in value.Split([',', ' ', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (SvgUserSpace.ResolveLength(element, item, percentageBasis) is not { } length)
                    return [];

                result.Add(length);
            }

            return result;
        }

        private static List<double> ParseNumbers(string? value)
        {
            var result = new List<double>();
            if (value is null)
                return result;

            foreach (var item in value.Split([',', ' ', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!double.TryParse(item, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    return [];

                result.Add(number);
            }

            return result;
        }
    }

    /// <summary>A line of an auto-wrapped text: a range of characters.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Line(int Start, int End);

    /// <summary>A character of the content of the text, after the white space processing.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Item(char Character, Element Element, ComputedStyle Style, Element? TextPath, bool IsCollapsibleSpace, Text TextNode, int TextOffset);
}
