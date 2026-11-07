using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Svg;
using static Meziantou.Framework.WebPlatform.Svg.SvgPathData;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The painting of the SVG texts (https://svgwg.org/svg2-draft/text.html): the glyphs of the characters laid out by SvgTextLayout
// (#368), at their positions and rotations, filled and stroked with the paints of their elements, and their decorations; the
// characters in the selection or in a highlight are painted with the paints of the highlight pseudo-elements.
internal sealed partial class Painter
{
    /// <summary>A run of glyphs of an SVG text drawn in one coordinate system (the glyphs of the characters with the same axes and the same font).</summary>
    /// <param name="Highlight">The index of the state of the highlights of the characters of the run (see <see cref="SvgTextHighlights"/>), or -1.</param>
    private sealed record SvgGlyphRun(GlyphRun Run, MatchedFont Font, SvgAffine Frame, Element Element, ComputedStyle Style, int Highlight);

    /// <summary>
    /// How the characters of a text content element that are in the same highlights are painted
    /// (https://drafts.csswg.org/css-pseudo-4/#highlight-styling): the fill and the stroke of the topmost highlight
    /// pseudo-element that specifies them (else the ones of the element), the background colors and the shadows of the
    /// highlights, from the bottom one.
    /// </summary>
    private sealed class SvgTextHighlightState
    {
        public bool HasFill { get; set; }

        public DisplayPaint? Fill { get; set; }

        public bool HasStroke { get; set; }

        public DisplayPaint? Stroke { get; set; }

        /// <summary>The style of the topmost highlight pseudo-element that specifies <c>stroke-width</c>, or null.</summary>
        public ComputedStyle? StrokeWidthStyle { get; set; }

        public List<DisplayColor> Backgrounds { get; } = [];

        public List<List<Shadow>> Shadows { get; } = [];
    }

    /// <summary>The highlights of a text: the index in <see cref="States"/> of the state of each character, or -1 for the characters that are not highlighted.</summary>
    private sealed record SvgTextHighlights(int[] Characters, List<SvgTextHighlightState> States);

    /// <summary>
    /// Paints a text element (https://svgwg.org/svg2-draft/text.html#TextRenderingOrder): the glyphs of its characters in the order
    /// of the characters, each run of characters of an element filled and stroked (in the order of <c>paint-order</c>) with the
    /// paints of the element, and the decorations of the text content elements.
    /// </summary>
    /// <param name="uniformOpacity">Whether the opacity of the text content elements is applied by the group of the text element (see <see cref="GetUniformSvgTextOpacity"/>).</param>
    private void PaintSvgText(Element element, ComputedStyle style, SvgAffine toGroup, SvgPaintContext context, Element source, bool uniformOpacity)
    {
        if (SvgTextLayout.Get(element, context.Viewport, inResource: context.IsResourceContent || context.UseInstanceRoot is not null) is not { } layout || layout.Characters.Length == 0)
            return;

        var styles = GetSvgTextStyles(layout, style, context);
        var toRoot = toGroup.Then(_svgBuilderTransform);
        PhysicalRect? boundingBox = null;
        PhysicalRect? GetBoundingBox() => boundingBox ??= layout.GetBoundingBox(layout.Root) is { } box ? new PhysicalRect(box.MinX, box.MinY, box.MaxX - box.MinX, box.MaxY - box.MinY) : null;
        var highlights = GetSvgTextHighlights(layout, toRoot, context, GetBoundingBox);
        var runs = BuildSvgGlyphRuns(layout, styles, highlights?.Characters);

        _builder.Save();
        _builder.Transform(DisplayMatrix.FromSvg(toGroup));
        var paintOrder = GetSvgPaintOrder(style);
        var paints = new Dictionary<Element, (DisplayPaint? Fill, DisplayPaint? Stroke)>(ReferenceEqualityComparer.Instance);
        (DisplayPaint? Fill, DisplayPaint? Stroke) GetPaints(Element textElement, ComputedStyle textStyle)
        {
            if (!paints.TryGetValue(textElement, out var result))
            {
                var fill = GetSvgPaint(textElement, textStyle, "fill", toRoot, context, GetBoundingBox);
                var stroke = GetSvgPaint(textElement, textStyle, "stroke", toRoot, context, GetBoundingBox);
                result = (fill is null ? null : fill with { Color = fill.Color.MultiplyAlpha(GetNumber(textStyle, "fill-opacity")) },
                    stroke is null ? null : stroke with { Color = stroke.Color.MultiplyAlpha(GetNumber(textStyle, "stroke-opacity")) });
                paints[textElement] = result;
            }

            return result;
        }

        // The opacity of the text content elements (tspan, textPath, a) between an element and the text element; a uniform opacity
        // is the one of the group of the text element (see GetUniformSvgTextOpacity)
        double GetGroupOpacity(Element textElement) => uniformOpacity ? 1 : GetSvgTextGroupOpacity(textElement, layout, styles);

        (DisplayPaint? Fill, DisplayPaint? Stroke) GetDecorationPaints(Element textElement, ComputedStyle textStyle)
        {
            var (fill, stroke) = GetPaints(textElement, textStyle);
            return WithGroupOpacity(fill, stroke, GetGroupOpacity(textElement));
        }

        static (DisplayPaint? Fill, DisplayPaint? Stroke) WithGroupOpacity(DisplayPaint? fill, DisplayPaint? stroke, double opacity)
            => opacity >= 1 ? (fill, stroke) : (fill is null ? null : fill with { Color = fill.Color.MultiplyAlpha(opacity) }, stroke is null ? null : stroke with { Color = stroke.Color.MultiplyAlpha(opacity) });

        // The paints and the stroke of a run: the ones of its element, or of the highlight pseudo-elements that specify them
        // (with the opacities of the element)
        (DisplayPaint? Fill, DisplayPaint? Stroke) GetRunPaints(SvgGlyphRun run)
        {
            var (fill, stroke) = GetPaints(run.Element, run.Style);
            if (run.Highlight < 0)
                return (fill, stroke);

            var state = highlights!.States[run.Highlight];
            if (state.HasFill)
            {
                fill = state.Fill is null ? null : state.Fill with { Color = state.Fill.Color.MultiplyAlpha(GetNumber(run.Style, "fill-opacity")) };
            }

            if (state.HasStroke)
            {
                stroke = state.Stroke is null ? null : state.Stroke with { Color = state.Stroke.Color.MultiplyAlpha(GetNumber(run.Style, "stroke-opacity")) };
            }

            return (fill, stroke);
        }

        SvgStrokeStyle GetRunStrokeStyle(SvgGlyphRun run)
        {
            var strokeStyle = SvgStrokeStyle.FromElement(run.Element, context.Viewport, run.Style);
            return run.Highlight >= 0 && highlights!.States[run.Highlight].StrokeWidthStyle is { } widthStyle
                ? strokeStyle with { Width = SvgStrokeStyle.FromElement(run.Element, context.Viewport, widthStyle).Width }
                : strokeStyle;
        }

        // https://drafts.csswg.org/css-pseudo-4/#highlight-painting: the backgrounds of the highlights are under the text and its
        // shadows: they cover the glyph cells of the highlighted characters
        if (highlights is not null)
        {
            PaintSvgTextHighlightBackgrounds(layout, styles, highlights, source);
        }

        // https://drafts.csswg.org/css-text-decor-3/#text-shadow-property: the shadows of the fill and of the stroke of the glyphs, the
        // first one on top, in the user space of the text (Chromium only paints the shadow of the first of the fill and the stroke
        // that is painted; the shadow of the stroke is tested by css/css-text-decor/text-shadow/svg-stroke.html)
        foreach (var run in runs)
        {
            if (run.Style.Layout.Visibility != Visibility.Visible)
                continue;

            // The shadows of the highlights are under the shadows of the text (as in Chromium, see
            // css/css-pseudo/svg-text-selection-shadow.html), from the bottom highlight
            var shadows = GetShadows(run.Style, "text-shadow");
            if (run.Highlight >= 0 && highlights!.States[run.Highlight].Shadows is { Count: > 0 } highlightShadows)
            {
                var all = new List<Shadow>(shadows ?? []);
                for (var i = highlightShadows.Count - 1; i >= 0; i--)
                {
                    all.AddRange(highlightShadows[i]);
                }

                shadows = all;
            }

            if (shadows is null)
                continue;

            var (runFill, runStroke) = GetRunPaints(run);
            var (fill, stroke) = WithGroupOpacity(runFill, runStroke, GetGroupOpacity(run.Element));
            var strokeStyle = stroke is null ? null : GetRunStrokeStyle(run);
            for (var i = shadows.Count - 1; i >= 0; i--)
            {
                var shadow = shadows[i];
                if (shadow.Color.IsTransparent)
                    continue;

                var shadowPaint = new DisplayPaint(shadow.Color) { BlurSigma = shadow.Blur / 2 };
                _builder.Save();
                _builder.Transform(DisplayMatrix.Translation(shadow.X, shadow.Y).PreConcat(DisplayMatrix.FromSvg(run.Frame)));
                if (fill is not null && !fill.Color.IsTransparent)
                {
                    AddGlyphRun(run.Run, GetFontPalette(run.Style, run.Font), shadowPaint with { Color = shadow.Color.MultiplyAlpha(fill.Color.A) }, colorize: false, source);
                }

                if (stroke is not null && !stroke.Color.IsTransparent && strokeStyle is { Width: > 0 })
                {
                    AddGlyphRun(run.Run, GetFontPalette(run.Style, run.Font), shadowPaint with { Color = shadow.Color.MultiplyAlpha(stroke.Color.A), Style = PaintStyle.Stroke, Stroke = ToStrokeStyle(strokeStyle) }, colorize: false, source);
                }

                _builder.Restore();
            }
        }

        // For the fill then the stroke (in the order of paint-order, https://svgwg.org/svg2-draft/painting.html#PaintOrder): the
        // decorations below the text (underlines and overlines), the glyphs, then the line-throughs (https://drafts.csswg.org/css-text-decor-4/#painting-order)
        foreach (var layer in paintOrder)
        {
            if (layer == SvgPaintLayer.Markers)
                continue;

            PaintSvgTextDecorations(layout, styles, GetDecorationPaints, TextDecorationLines.Underline | TextDecorationLines.Overline, layer, source);
            foreach (var run in runs)
            {
                if (run.Style.Layout.Visibility != Visibility.Visible)
                    continue;

                var (fill, stroke) = GetRunPaints(run);
                var inverseFrame = run.Frame.Invert() ?? SvgAffine.Identity;
                var groupOpacity = GetGroupOpacity(run.Element);
                if (groupOpacity <= 0)
                    continue;

                if (layer == SvgPaintLayer.Fill && fill is not null && !fill.Color.IsTransparent)
                {
                    SaveSvgTextGroup(groupOpacity, run.Element);
                    _builder.Transform(DisplayMatrix.FromSvg(run.Frame));
                    AddGlyphRun(run.Run, GetFontPalette(run.Style, run.Font), TransformSvgPaint(fill, inverseFrame), colorize: true, source);
                    _builder.Restore();
                }
                else if (layer == SvgPaintLayer.Stroke && stroke is not null && !stroke.Color.IsTransparent && GetRunStrokeStyle(run) is { Width: > 0 } strokeStyle)
                {
                    SaveSvgTextGroup(groupOpacity, run.Element);
                    _builder.Transform(DisplayMatrix.FromSvg(run.Frame));
                    AddGlyphRun(run.Run, GetFontPalette(run.Style, run.Font), TransformSvgPaint(stroke, inverseFrame) with { Style = PaintStyle.Stroke, Stroke = ToStrokeStyle(strokeStyle) }, colorize: false, source);
                    _builder.Restore();
                }
            }

            PaintSvgTextDecorations(layout, styles, GetDecorationPaints, TextDecorationLines.LineThrough, layer, source);
        }

        _builder.Restore();
    }

    /// <summary>Saves the state, in a layer of an opacity (the group opacity of a text content element, https://drafts.csswg.org/css-color-4/#transparency) when it is not 1.</summary>
    private void SaveSvgTextGroup(double opacity, Element source)
    {
        if (opacity < 1)
        {
            _builder.SaveLayer(new SaveLayerItem(opacity, BlendMode.Normal, Filters: null) { Source = source });
        }
        else
        {
            _builder.Save();
        }
    }

    /// <summary>
    /// Returns the opacity of the text content elements of a text when it is the same for all its rendered characters (e.g. a text
    /// whose content is one tspan element with an opacity), else null: the text is then one group, whose opacity is the product
    /// of the opacities (the nested groups of the same content are not composited one after the other).
    /// </summary>
    private double? GetUniformSvgTextOpacity(Element text, ComputedStyle style, SvgPaintContext context)
    {
        if (SvgTextLayout.Get(text, context.Viewport, inResource: context.IsResourceContent || context.UseInstanceRoot is not null) is not { } layout || layout.Characters.Length == 0)
            return null;

        var styles = GetSvgTextStyles(layout, style, context);
        double? result = null;
        Element? previous = null;
        foreach (ref readonly var character in layout.Characters.AsSpan())
        {
            if (character.IsHidden || !character.IsClusterStart || character.Element is not { } element || ReferenceEquals(element, previous))
                continue;

            previous = element;
            var opacity = GetSvgTextGroupOpacity(element, layout, styles);
            if (result is { } existing && existing != opacity)
                return null;

            result = opacity;
        }

        return result;
    }

    /// <summary>Returns the product of the opacities of the text content elements (tspan, textPath, a) between an element and its text element.</summary>
    private static double GetSvgTextGroupOpacity(Element textElement, SvgTextLayout layout, Dictionary<Element, ComputedStyle> styles)
    {
        var opacity = 1d;
        for (var ancestor = textElement; ancestor is not null && !ReferenceEquals(ancestor, layout.Root); ancestor = ancestor.ParentNode as Element)
        {
            // An element with display: contents is unboxed: its opacity does not apply
            var ancestorStyle = styles.TryGetValue(ancestor, out var cachedStyle) ? cachedStyle : ancestor.NodeDocument.GetStyleEngine().GetStyle(ancestor);
            if (ancestorStyle.Layout.Display.Box != DisplayBox.Contents)
            {
                opacity *= GetOpacity(ancestorStyle);
            }
        }

        return opacity;
    }

    /// <summary>
    /// Returns the styles of the text content elements of a text (their element instances in a use-element shadow tree inherit from
    /// the instance of their parent).
    /// </summary>
    private static Dictionary<Element, ComputedStyle> GetSvgTextStyles(SvgTextLayout layout, ComputedStyle rootStyle, SvgPaintContext context)
    {
        var styles = new Dictionary<Element, ComputedStyle>(ReferenceEqualityComparer.Instance) { [layout.Root] = rootStyle };
        foreach (var element in layout.Elements)
        {
            if (styles.ContainsKey(element))
                continue;

            if (context is { UseInstanceRoot: { } root } && element.ParentNode is Element parent && styles.TryGetValue(parent, out var parentStyle))
            {
                styles[element] = element.NodeDocument.GetStyleEngine().GetUseInstanceStyle(element, parentStyle, root);
            }
            else
            {
                styles[element] = element.NodeDocument.GetStyleEngine().GetStyle(element);
            }
        }

        return styles;
    }

    /// <summary>
    /// Builds the glyph runs of a text: the glyphs of the consecutive rendered characters of an element that have the same axes and
    /// the same font, in a coordinate system whose x axis is the direction of the glyphs (the progression of sideways glyphs, the
    /// physical x axis of upright glyphs, reversed for right-to-left text) and whose y axis points to the bottom of the glyphs; the
    /// axis of the progression is scaled for the glyphs stretched by <c>lengthAdjust="spacingAndGlyphs"</c>.
    /// </summary>
    /// <param name="highlights">The state of the highlights of each character (a run has a single state), or null.</param>
    private static List<SvgGlyphRun> BuildSvgGlyphRuns(SvgTextLayout layout, Dictionary<Element, ComputedStyle> styles, int[]? highlights = null)
    {
        var result = new List<SvgGlyphRun>();
        var ids = new List<ushort>();
        var positions = new List<PhysicalOffset>();
        var text = new System.Text.StringBuilder();
        DisplayFont? runFont = null;
        MatchedFont? runMatchedFont = null;
        Element? runElement = null;
        SvgPoint runX = default, runY = default;
        var runScale = 1d;
        var runUpright = false;
        var runHighlight = -1;
        var characters = layout.Characters;
        for (var i = 0; i < characters.Length; i++)
        {
            ref readonly var character = ref characters[i];
            if (!character.IsClusterStart || character.IsHidden || character.Shaped is not { } shaped || character.GlyphCount == 0)
                continue;

            // The glyphs are drawn in the direction of the line: against the progression of a character of a right-to-left run,
            // from its end position, the first glyph (in logical order) of the character at its start
            var rtl = character.IsRightToLeft;
            var forward = rtl ? character.Progression * -1 : character.Progression;
            var start = rtl ? character.EndPosition : character.Position;
            var axisX = character.IsUpright ? character.Across * -1 : forward;
            var axisY = character.IsUpright ? forward : character.Across;
            var glyphs = shaped.Glyphs;
            var runs = shaped.Runs;
            var pen = 0d;
            if (rtl)
            {
                for (var g = character.GlyphStart; g < character.GlyphStart + character.GlyphCount && g < glyphs.Length; g++)
                {
                    pen += glyphs[g].Advance;
                }
            }

            // The glyphs stretched by lengthAdjust="spacingAndGlyphs" are scaled along the progression
            var scale = character.GlyphScale > 0 ? character.GlyphScale : 1;
            for (var g = character.GlyphStart; g < character.GlyphStart + character.GlyphCount && g < glyphs.Length; g++)
            {
                var glyph = glyphs[g];
                var fontRun = FindFontRun(runs, g);
                var font = DisplayFont.From(fontRun.Font, fontRun.EmSize);

                // The origin of the glyph (the start of its alphabetic baseline) in the coordinates of the user space
                if (rtl)
                {
                    pen -= glyph.Advance;
                }

                SvgPoint origin;
                if (character.IsUpright)
                {
                    var metrics = fontRun.Font.GetMetricsForEmSize(fontRun.EmSize);
                    var horizontalAdvance = fontRun.Font.GetAdvance(glyph.GlyphId, fontRun.EmSize, vertical: false);
                    origin = start + (axisX * (-(horizontalAdvance / 2) + glyph.OffsetY)) + (axisY * ((pen + (glyph.Advance / 2) + ((metrics.Ascent - metrics.Descent) / 2) + glyph.OffsetX) * scale));
                }
                else
                {
                    origin = start + (axisX * ((pen + glyph.OffsetX) * scale)) + (axisY * (character.BaselineOffset + glyph.OffsetY));
                }

                if (!rtl)
                {
                    pen += glyph.Advance;
                }
                if (glyph.IsInvisible)
                    continue;

                var highlight = highlights is null ? -1 : highlights[i];
                if (runElement is null || !ReferenceEquals(runElement, character.Element) || runFont != font || !ReferenceEquals(runMatchedFont, fontRun.Font) || !Same(runX, axisX) || !Same(runY, axisY) || runScale != scale || runUpright != character.IsUpright
                    || runHighlight != highlight)
                {
                    Flush();
                    runHighlight = highlight;
                    runElement = character.Element;
                    runFont = font;
                    runMatchedFont = fontRun.Font;
                    runX = axisX;
                    runY = axisY;
                    runScale = scale;
                    runUpright = character.IsUpright;
                }

                ids.Add(glyph.GlyphId);

                // The coordinates of the origin in the frame of the run (an orthogonal basis, whose axis of the progression is scaled)
                var scaleX = character.IsUpright ? 1 : scale;
                var scaleY = character.IsUpright ? scale : 1;
                positions.Add(new PhysicalOffset(((origin.X * axisX.X) + (origin.Y * axisX.Y)) / scaleX, ((origin.X * axisY.X) + (origin.Y * axisY.Y)) / scaleY));
                if (g == character.GlyphStart)
                {
                    text.Append(character.TextNode.Data, character.TextOffset, 1);
                }
            }
        }

        Flush();
        return result;

        void Flush()
        {
            if (ids.Count > 0 && runFont is not null && runMatchedFont is not null && runElement is not null)
            {
                var scaleX = runUpright ? 1 : runScale;
                var scaleY = runUpright ? runScale : 1;
                var frame = new SvgAffine(runX.X * scaleX, runX.Y * scaleX, runY.X * scaleY, runY.Y * scaleY, 0, 0);
                result.Add(new SvgGlyphRun(new GlyphRun(runFont, [.. ids], [.. positions], text.ToString()), runMatchedFont, frame, runElement, styles.TryGetValue(runElement, out var style) ? style : runElement.NodeDocument.GetStyleEngine().GetStyle(runElement), runHighlight));
            }

            ids.Clear();
            positions.Clear();
            text.Clear();
        }

        static bool Same(SvgPoint a, SvgPoint b) => Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9;
    }

    private static ShapedFontRun FindFontRun(ReadOnlySpan<ShapedFontRun> runs, int glyph)
    {
        foreach (var run in runs)
        {
            if (glyph >= run.GlyphStart && glyph < run.GlyphStart + run.GlyphCount)
                return run;
        }

        return runs[^1];
    }

    /// <summary>
    /// Returns the highlights of the characters of a text (https://drafts.csswg.org/css-pseudo-4/#highlight-pseudos), or null when
    /// none is highlighted: the custom highlights of <c>CSS.highlights</c> (<c>::highlight(name)</c>) by priority, the target text
    /// of the text directives (<c>::target-text</c>), then the selection of the document (<c>::selection</c>).
    /// </summary>
    /// <remarks>
    /// The glyphs of a highlighted character are filled and stroked with <c>fill</c>, <c>stroke</c> and <c>stroke-width</c> of the
    /// topmost highlight pseudo-element that specifies them, else with the ones of its element (as in Chromium); the
    /// <c>color</c> of a highlight is its fill when it does not specify one (css/css-pseudo/svg-text-selection-002.html, as in
    /// Gecko). The decorations of the highlights are not painted.
    /// </remarks>
    private SvgTextHighlights? GetSvgTextHighlights(SvgTextLayout layout, SvgAffine toRoot, SvgPaintContext context, Func<PhysicalRect?> getBoundingBox)
    {
        // The layers of the highlights, in painting order
        List<(string PseudoElement, string? DefaultBackground, string? DefaultColor, List<(Node StartNode, uint StartOffset, Node EndNode, uint EndOffset)> Ranges)>? layers = null;
        if (_document.HasHighlights)
        {
            var sorted = new List<(int Priority, int Index, string Name, Css.Om.Highlight Highlight)>();
            var index = 0;
            foreach (var (name, highlight) in _document.Highlights)
            {
                sorted.Add((highlight.Priority, index++, name, highlight));
            }

            sorted.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.Index.CompareTo(b.Index));
            foreach (var (_, _, name, highlight) in sorted)
            {
                var ranges = new List<(Node, uint, Node, uint)>();
                foreach (var range in highlight)
                {
                    // https://drafts.csswg.org/css-highlight-api-1/#range-invalidation: the static ranges that are not valid are ignored
                    if (range is not StaticRange { IsValid: false })
                    {
                        ranges.Add((range.StartContainer, range.StartOffset, range.EndContainer, range.EndOffset));
                    }
                }

                if (ranges.Count > 0)
                {
                    (layers ??= []).Add(("highlight(" + CssSerializer.SerializeIdentifier(name) + ")", null, null, ranges));
                }
            }
        }

        if (_document.TargetTextRanges is { Count: > 0 } targetTextRanges)
        {
            var ranges = new List<(Node, uint, Node, uint)>();
            foreach (var range in targetTextRanges)
            {
                ranges.Add((range.StartContainer, range.StartOffset, range.EndContainer, range.EndOffset));
            }

            (layers ??= []).Add(("target-text", "mark", "marktext", ranges));
        }

        if (_document.GetSelection()?.GetRenderedBoundaryPoints() is { } selection)
        {
            (layers ??= []).Add(("selection", "highlight", null, [selection]));
        }

        // (the layers of a character are a bit mask)
        if (layers is null || layers.Count > 64)
            return null;

        var engine = _document.GetStyleEngine();
        var characters = layout.Characters;
        int[]? result = null;
        var states = new List<SvgTextHighlightState>();
        var stateIndexes = new Dictionary<(ulong Layers, Element Element), int>();
        Text? node = null;
        var nodeRanges = new List<(int Layer, int Start, int End)>();
        for (var i = 0; i < characters.Length; i++)
        {
            ref readonly var character = ref characters[i];
            if (character.TextNode is null || character.Element is not { } element)
                continue;

            if (!ReferenceEquals(character.TextNode, node))
            {
                // The ranges of the layers in the text node
                node = character.TextNode;
                nodeRanges.Clear();
                var length = (uint)node.Length;
                for (var layer = 0; layer < layers.Count; layer++)
                {
                    // https://html.spec.whatwg.org/multipage/interaction.html#inert-subtrees, https://drafts.csswg.org/css-ui-4/#valdef-user-select-none:
                    // as in the texts of the boxes, the text that is inert or that cannot be selected is not highlighted by the selection
                    if (layers[layer].PseudoElement == "selection" && (FocusModel.IsInert(node) || IsUserSelectNone(engine, element)))
                        continue;

                    foreach (var (startNode, startOffset, endNode, endOffset) in layers[layer].Ranges)
                    {
                        if (AbstractRange.CompareComposedPoints(startNode, startOffset, node, length) == AbstractRange.BoundaryPointPosition.After
                            || AbstractRange.CompareComposedPoints(endNode, endOffset, node, 0) == AbstractRange.BoundaryPointPosition.Before)
                        {
                            continue;
                        }

                        var start = ReferenceEquals(startNode, node) ? (int)startOffset : 0;
                        var end = ReferenceEquals(endNode, node) ? (int)endOffset : (int)length;
                        if (end > start)
                        {
                            nodeRanges.Add((layer, start, end));
                        }
                    }
                }
            }

            var mask = 0UL;
            foreach (var (layer, start, end) in nodeRanges)
            {
                if (character.TextOffset >= start && character.TextOffset < end)
                {
                    mask |= 1UL << layer;
                }
            }

            if (mask == 0)
                continue;

            if (result is null)
            {
                result = new int[characters.Length];
                Array.Fill(result, -1);
            }

            if (!stateIndexes.TryGetValue((mask, element), out var stateIndex))
            {
                stateIndex = states.Count;
                stateIndexes.Add((mask, element), stateIndex);
                states.Add(CreateState(mask, element, character.Style));
            }

            result[i] = stateIndex;
        }

        return result is null ? null : new SvgTextHighlights(result, states);

        SvgTextHighlightState CreateState(ulong mask, Element element, ComputedStyle textStyle)
        {
            var state = new SvgTextHighlightState();
            var currentColor = GetColor(textStyle, "color");
            for (var layer = 0; layer < layers.Count; layer++)
            {
                if ((mask & (1UL << layer)) == 0)
                    continue;

                var (pseudoElement, defaultBackground, defaultColor, _) = layers[layer];
                var style = engine.GetStyle(element, pseudoElement);
                var colorStyle = style.VisitedOrSelf;
                var ownColor = colorStyle.GetComputedValue("color") == "currentcolor" ? (DisplayColor?)null : GetColor(style, "color");
                var color = ownColor ?? currentColor;
                var background = GetHighlightColor(style, "background-color", color);
                if (defaultBackground is not null && background.IsTransparent && ownColor is null && !IsHighlightStyled(colorStyle))
                {
                    // https://drafts.csswg.org/css-pseudo-4/#paired-defaults: the highlight colors of the browsers for the
                    // selection and the target text that are not styled (fill and stroke do not suppress them)
                    background = DisplayColor.Parse(CssValueComputer.GetSystemColor(textStyle, defaultBackground)) ?? DisplayColor.Transparent;
                    if (defaultColor is not null && DisplayColor.Parse(CssValueComputer.GetSystemColor(textStyle, defaultColor)) is { } systemColor)
                    {
                        ownColor = color = systemColor;
                    }
                }

                if (!background.IsTransparent)
                {
                    state.Backgrounds.Add(background);
                }

                if (IsSpecifiedByHighlight(colorStyle, "fill"))
                {
                    state.HasFill = true;
                    state.Fill = GetSvgPaint(element, style, "fill", toRoot, context, getBoundingBox);
                }
                else if (ownColor is not null)
                {
                    state.HasFill = true;
                    state.Fill = new DisplayPaint(color);
                }

                if (IsSpecifiedByHighlight(colorStyle, "stroke"))
                {
                    state.HasStroke = true;
                    state.Stroke = GetSvgPaint(element, style, "stroke", toRoot, context, getBoundingBox);
                }

                if (IsSpecifiedByHighlight(style, "stroke-width"))
                {
                    state.StrokeWidthStyle = style;
                }

                if (GetShadows(style, "text-shadow") is { } shadows)
                {
                    state.Shadows.Add(shadows);
                }

                currentColor = color;
            }

            return state;
        }

        // Whether a property is specified for a highlight pseudo-element (also for the highlight pseudo-element of an ancestor,
        // which it inherits from)
        static bool IsSpecifiedByHighlight(ComputedStyle style, string property)
        {
            for (var current = style; current is not null; current = current.HighlightParent)
            {
                if (current.TryGetCascadedValue(property, out var cascaded) && cascaded.Position >= 0)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Paints the backgrounds of the highlights of a text: the glyph cells of the highlighted characters (the consecutive cells of
    /// a line are one quadrilateral), in the user space of the text.
    /// </summary>
    private void PaintSvgTextHighlightBackgrounds(SvgTextLayout layout, Dictionary<Element, ComputedStyle> styles, SvgTextHighlights highlights, Element source)
    {
        var characters = layout.Characters;
        for (var stateIndex = 0; stateIndex < highlights.States.Count; stateIndex++)
        {
            var state = highlights.States[stateIndex];
            if (state.Backgrounds.Count == 0)
                continue;

            var path = new DisplayPath();
            var hasPending = false;
            SvgPoint start = default, progression = default, across = default;
            double length = 0, cellStart = 0, cellEnd = 0;
            for (var i = 0; i < characters.Length; i++)
            {
                ref readonly var character = ref characters[i];
                if (highlights.Characters[i] != stateIndex || character.IsHidden || !character.IsClusterStart || character.Advance <= 0
                    || (styles.TryGetValue(character.Element, out var style) ? style : character.Style).Layout.Visibility != Visibility.Visible)
                {
                    continue;
                }

                var end = start + (progression * length);
                if (hasPending && Near(progression, character.Progression) && Near(across, character.Across) && Near(end, character.Position))
                {
                    length += character.Advance;
                    cellStart = Math.Min(cellStart, character.CellStart);
                    cellEnd = Math.Max(cellEnd, character.CellEnd);
                    continue;
                }

                if (hasPending)
                {
                    AddCells(path, start, progression, across, length, cellStart, cellEnd);
                }

                hasPending = true;
                start = character.Position;
                progression = character.Progression;
                across = character.Across;
                length = character.Advance;
                cellStart = character.CellStart;
                cellEnd = character.CellEnd;
            }

            if (hasPending)
            {
                AddCells(path, start, progression, across, length, cellStart, cellEnd);
            }

            if (path.IsEmpty)
                continue;

            foreach (var background in state.Backgrounds)
            {
                _builder.Add(new DrawPathItem(path, new DisplayPaint(background)) { Source = source });
            }
        }

        static bool Near(SvgPoint a, SvgPoint b) => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;

        static void AddCells(DisplayPath path, SvgPoint start, SvgPoint progression, SvgPoint across, double length, double cellStart, double cellEnd)
        {
            var p1 = start + (across * cellStart);
            var p2 = p1 + (progression * length);
            var p4 = start + (across * cellEnd);
            var p3 = p4 + (progression * length);
            path.AddQuad(new PhysicalOffset(p1.X, p1.Y), new PhysicalOffset(p2.X, p2.Y), new PhysicalOffset(p3.X, p3.Y), new PhysicalOffset(p4.X, p4.Y));
        }
    }

    /// <summary>
    /// Paints the decorations of the text content elements of a text (https://svgwg.org/svg2-draft/text.html#TextDecorationProperties):
    /// the lines of an element are painted under (or through) the glyph cells of its characters, at the positions of the primary font
    /// of the element, filled and stroked with the paints of the element.
    /// </summary>
    private void PaintSvgTextDecorations(SvgTextLayout layout, Dictionary<Element, ComputedStyle> styles, Func<Element, ComputedStyle, (DisplayPaint? Fill, DisplayPaint? Stroke)> getPaints, TextDecorationLines lines, SvgPaintLayer layer, Element source)
    {
        foreach (var element in layout.Elements)
        {
            if (!styles.TryGetValue(element, out var style) || style.Layout.Visibility != Visibility.Visible)
                continue;

            // The decorations propagate through the boxes: an element with display: contents has no decorations
            var value = style.GetComputedValue("text-decoration-line");
            if (value is "none" or "" || style.Layout.Display.Box == DisplayBox.Contents)
                continue;

            var applied = TextDecorationLines.None;
            foreach (var keyword in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                applied |= keyword switch
                {
                    "underline" => TextDecorationLines.Underline,
                    "overline" => TextDecorationLines.Overline,
                    "line-through" => TextDecorationLines.LineThrough,
                    _ => TextDecorationLines.None,
                };
            }

            applied &= lines;
            if (applied == TextDecorationLines.None || !layout.TryGetRange(element, out var start, out var count))
                continue;

            var (fill, stroke) = getPaints(element, style);
            if (fill is null && stroke is null)
                continue;

            var metrics = style.PrimaryFontMetrics;
            var fontUnits = metrics.FontUnitMetrics;
            var thickness = fontUnits.UnderlineThickness > 0 ? fontUnits.UnderlineThickness * metrics.Scale : style.FontSize / 10;
            var path = new DisplayPath();
            for (var i = start; i < start + count; i++)
            {
                ref readonly var character = ref layout.Characters[i];
                if (character.IsHidden || !character.IsClusterStart || character.Advance <= 0)
                    continue;

                var baseline = character.BaselineOffset;
                if ((applied & TextDecorationLines.Underline) != TextDecorationLines.None)
                {
                    var top = fontUnits.UnderlinePosition != 0 ? -fontUnits.UnderlinePosition * metrics.Scale : thickness;
                    AddDecoration(path, character, baseline + top - (thickness / 2), thickness);
                }

                if ((applied & TextDecorationLines.Overline) != TextDecorationLines.None)
                {
                    AddDecoration(path, character, baseline - metrics.Ascent, thickness);
                }

                if ((applied & TextDecorationLines.LineThrough) != TextDecorationLines.None)
                {
                    var center = fontUnits.StrikeoutPosition != 0 ? -(fontUnits.StrikeoutPosition * metrics.Scale) + (fontUnits.StrikeoutThickness * metrics.Scale / 2) : -metrics.XHeight / 2;
                    AddDecoration(path, character, baseline + center - (thickness / 2), thickness);
                }
            }

            if (path.IsEmpty)
                continue;

            if (layer == SvgPaintLayer.Fill && fill is not null && !fill.Color.IsTransparent)
            {
                _builder.Add(new DrawPathItem(path, fill) { Source = source });
            }

            if (layer == SvgPaintLayer.Stroke && stroke is not null && !stroke.Color.IsTransparent && SvgStrokeStyle.FromElement(element, viewport: null, style) is { Width: > 0 } strokeStyle)
            {
                _builder.Add(new DrawPathItem(path, stroke with { Style = PaintStyle.Stroke, Stroke = ToStrokeStyle(strokeStyle) with { Dashes = null } }) { Source = source });
            }
        }

        static void AddDecoration(DisplayPath path, in SvgTextCharacter character, double top, double thickness)
        {
            var start = character.Position + (character.Across * top);
            var end = start + (character.Progression * character.Advance);
            var across = character.Across * thickness;
            path.AddQuad(new PhysicalOffset(start.X, start.Y), new PhysicalOffset(end.X, end.Y), new PhysicalOffset(end.X + across.X, end.Y + across.Y), new PhysicalOffset(start.X + across.X, start.Y + across.Y));
        }
    }

    /// <summary>
    /// Paints the glyphs of a text element of a clipPath element (https://drafts.fxtf.org/css-masking/#ClipPathElement: the clipping
    /// path of a text is the union of its glyph cells... as in the browsers, the outlines of its glyphs) with a paint, in the
    /// coordinates given by <paramref name="toPainting"/>.
    /// </summary>
    private void PaintSvgTextGlyphs(Element text, SvgAffine toPainting, DisplayPaint paint)
    {
        if (SvgTextLayout.Get(text, inResource: true) is not { } layout || layout.Characters.Length == 0)
            return;

        var styles = GetSvgTextStyles(layout, text.NodeDocument.GetStyleEngine().GetStyle(text), new SvgPaintContext(default));
        _builder.Save();
        _builder.Transform(DisplayMatrix.FromSvg(toPainting));
        foreach (var run in BuildSvgGlyphRuns(layout, styles))
        {
            if (run.Style.Layout.Visibility != Visibility.Visible)
                continue;

            _builder.Save();
            _builder.Transform(DisplayMatrix.FromSvg(run.Frame));
            AddGlyphRun(run.Run, GetFontPalette(run.Style, run.Font), paint, colorize: false, text);
            _builder.Restore();
        }

        _builder.Restore();
    }
}
