using Meziantou.Framework.WebPlatform.Css.Fonts;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The layouts of the token elements (https://w3c.github.io/mathml-core/#token-elements) and of the operators
// (https://w3c.github.io/mathml-core/#layout-of-operators)
internal sealed partial class MathFormattingContext
{
    /// <summary>
    /// https://w3c.github.io/mathml-core/#layout-of-mtext: the contents of the token element are laid out as a block container
    /// (by <see cref="LayoutTokenContents"/>); the inline size of the math content is the fit-content size of the contents. The
    /// italic correction and the top accent attachment of a token made of a single glyph come from the MATH table.
    /// </summary>
    private static MathContent LayoutToken(LayoutContext context, ContainerBox box)
    {
        var state = box.LayoutState as MathLayoutState;
        var available = state?.AvailableInlineSize ?? context.InitialContainingBlockSize.Width;
        var sizes = IntrinsicSizing.GetIntrinsicInlineSizes(context, box, contentBlockSize: null);
        var content = new MathContent { IsTokenContent = true, InlineSize = sizes.FitContent(available) };
        if (GetTokenGlyph(box) is var (font, glyph, emSize) && font.Font.MathTable is { } math)
        {
            var scale = emSize / font.Font.UnitsPerEm;
            content.ItalicCorrection = (math.GetItalicsCorrection(glyph) ?? 0) * scale;
            content.TopAccentAttachment = math.GetTopAccentAttachment(glyph) * scale;
        }

        return content;
    }

    /// <summary>
    /// Lays out the contents of a token element in its content box. "If the mtext element contains only text content without
    /// forced line break or soft wrap opportunity", the line-ascent and line-descent of the math content are the ink extent of
    /// the text: the line is moved so that the ink-over edge of the text is at the block-start edge of the content box.
    /// Otherwise, the token is a block box aligned on its first baseline.
    /// </summary>
    private static void LayoutTokenContents(LayoutContext context, ContainerBox box, FragmentBuilder builder, in ContentArea area, MathContent content)
    {
        var start = builder.Count;
        var tokenArea = new ContentArea(area.WritingDirection, area.Offset, Math.Max(0, content.InlineSize), BlockSize: null);
        ContentLayoutResult result = default;
        if (!box.IsEmpty)
        {
            FormattingContext formattingContext = box.ChildrenAreInline ? InlineFormattingContext.Instance : BlockFormattingContext.Instance;
            result = formattingContext.LayoutFlow(context, box, builder, tokenArea);
        }

        if (box.ChildrenAreInline && builder.Count - start == 1 && builder.GetChild(start) is LineBoxFragment line && HasOnlyText(box))
        {
            var (inkAscent, inkDescent) = GetInkExtent(line);
            content.Ascent = inkAscent;
            content.Descent = inkDescent;
            content.InkAscent = inkAscent;
            content.InkDescent = inkDescent;
            builder.MoveChildren(new LogicalOffset(0, inkAscent - line.Baseline));
            return;
        }

        var ascent = result.FirstBaseline ?? result.BlockSize;
        content.Ascent = ascent;
        content.Descent = result.BlockSize - ascent;
        content.SetInkFromLine();
    }

    private static bool HasOnlyText(ContainerBox box)
    {
        foreach (var child in box.Children)
        {
            if (child is not TextRun)
                return false;
        }

        return true;
    }

    /// <summary>Returns the ink extent of the glyphs of a line from its baseline (the bounding boxes of the glyphs), or zeros when they have no ink.</summary>
    private static (double Ascent, double Descent) GetInkExtent(LineBoxFragment line)
    {
        var top = double.PositiveInfinity;
        var bottom = double.NegativeInfinity;
        foreach (var child in line.ChildrenSpan)
        {
            if (child is not TextFragment { ShapedText: { } shaped } text)
                continue;

            var baseline = child.Offset.Y + text.Baseline;
            foreach (var run in shaped.Runs)
            {
                var from = Math.Max(run.GlyphStart, text.GlyphStart);
                var to = Math.Min(run.GlyphStart + run.GlyphCount, text.GlyphStart + text.GlyphCount);
                var scale = run.EmSize / run.Font.Font.UnitsPerEm;
                for (var i = from; i < to; i++)
                {
                    var glyph = shaped.Glyphs[i];
                    if (glyph.IsInvisible || !run.Font.Font.TryGetGlyphBounds(glyph.GlyphId, out var bounds))
                        continue;

                    top = Math.Min(top, baseline + glyph.OffsetY - (bounds.YMax * scale));
                    bottom = Math.Max(bottom, baseline + glyph.OffsetY - (bounds.YMin * scale));
                }
            }
        }

        if (top > bottom)
            return (0, 0);

        return (line.Baseline - top, bottom - line.Baseline);
    }

    /// <summary>
    /// Returns the glyph of a token element made of a single character (after <c>text-transform: math-auto</c>), with its font
    /// and its em size, or null.
    /// </summary>
    private static (MatchedFont Font, ushort Glyph, double EmSize)? GetTokenGlyph(ContainerBox box)
    {
        if (GetSingleCodePoint(MathLayoutBoxes.GetTokenText(box)) is not { } codePoint)
            return null;

        var layout = box.Style.Layout;
        if ((layout.TextTransform & TextTransform.MathAuto) != TextTransform.None)
        {
            codePoint = MathItalicMapping.GetItalic(codePoint);
        }

        var font = box.Style.Fonts.GetFontForCharacter(codePoint);
        var glyph = font.GetGlyph(codePoint);
        return glyph == 0 ? null : (font, glyph, font.GetEmSize(layout.FontSize));
    }

    private static int? GetSingleCodePoint(string text)
    {
        if (text.Length == 1 && !char.IsSurrogate(text[0]))
            return text[0];

        if (text.Length == 2 && char.IsSurrogatePair(text[0], text[1]))
            return char.ConvertToUtf32(text[0], text[1]);

        return null;
    }

    private static IntrinsicSizes ComputeTokenIntrinsicSizes(LayoutContext context, ContainerBox box, in IntrinsicSizingInput input)
    {
        if (box.IsEmpty)
            return default;

        return box.ChildrenAreInline
            ? InlineFormattingContext.Instance.ComputeIntrinsicInlineSizes(context, box, input)
            : BlockFormattingContext.Instance.ComputeIntrinsicInlineSizes(context, box, input);
    }

    /// <summary>The operator glyph of an mo: the glyph of its single character in the first available font (mirrored in rtl), or null.</summary>
    private static (MathFontParameters Parameters, ushort Glyph)? GetOperatorGlyph(ContainerBox box)
    {
        // "If the content of the <mo> element is not made of a single character c then fall back to the layout algorithm of mtext"
        if (!MathLayoutBoxes.HasOnlyTextContent(box) || GetSingleCodePoint(MathLayoutBoxes.GetTokenText(box)) is not { } codePoint)
            return null;

        var parameters = MathFontParameters.Get(box.Style);
        return MathStretchyGlyphs.TryGetGlyph(parameters.Font, codePoint, IsRightToLeft(box), out var glyph) ? (parameters, glyph) : null;
    }

    /// <summary>
    /// https://w3c.github.io/mathml-core/#layout-of-operators: a stretchy operator is stretched to its stretch size constraint, a
    /// large operator in <c>math-style: normal</c> uses the size of DisplayOperatorMinHeight; the other operators are laid out as
    /// <c>mtext</c>.
    /// </summary>
    private static MathContent LayoutOperator(LayoutContext context, ContainerBox box, MathStretchConstraint? stretch)
    {
        if (GetOperatorGlyph(box) is not var (parameters, glyph))
            return LayoutToken(context, box);

        var (_, properties) = GetOperator(box);
        var font = parameters.Font;
        var emSize = parameters.EmSize;
        MathShapedGlyph? shaped = null;
        double delta = 0;
        if (properties.IsStretchy)
        {
            if (properties.StretchAxis == MathStretchAxis.Inline)
            {
                if (stretch is { Axis: MathStretchAxis.Inline } constraint && MathStretchyGlyphs.CanStretch(font, glyph, vertical: false))
                {
                    shaped = MathStretchyGlyphs.Shape(font, emSize, glyph, vertical: false, constraint.InlineSize);
                }
            }
            else if (stretch is { Axis: MathStretchAxis.Block } constraint && MathStretchyGlyphs.CanStretch(font, glyph, vertical: true))
            {
                var (targetAscent, targetDescent) = GetBlockStretchTarget(parameters, glyph, properties, constraint.Ascent, constraint.Descent);
                shaped = MathStretchyGlyphs.Shape(font, emSize, glyph, vertical: true, targetAscent + targetDescent);
                if (shaped is not null)
                {
                    // The stretchy glyph is shifted towards the line-under so that its center aligns with the center of the target
                    delta = ((shaped.Ascent - shaped.Descent) - (targetAscent - targetDescent)) / 2;
                }
            }
        }
        else if (properties.IsLargeOp && !IsCompact(box) && MathStretchyGlyphs.CanStretch(font, glyph, vertical: true))
        {
            // "Here we treat a non-stretchy largeop glyph as stretchy with target dimension DisplayOperatorMinHeight"
            shaped = MathStretchyGlyphs.Shape(font, emSize, glyph, vertical: true, parameters.DisplayOperatorMinHeight);
            if (shaped is not null && properties.IsSymmetric)
            {
                delta = ((shaped.Ascent - shaped.Descent) - (2 * parameters.AxisHeight)) / 2;
            }
        }

        if (shaped is null)
            return LayoutToken(context, box);

        var content = new MathContent
        {
            InlineSize = shaped.Width,
            Ascent = shaped.Ascent - delta,
            Descent = shaped.Descent + delta,
            ItalicCorrection = shaped.ItalicCorrection,
        };
        content.SetInkFromLine();
        content.AddItem(new MathItem(0, delta, shaped.Width, 0, shaped));
        return content;
    }

    /// <summary>
    /// The target ascent and descent of an operator stretched along the block axis (https://w3c.github.io/mathml-core/#layout-of-operators):
    /// symmetric around the math axis for a symmetric operator, then between its minsize and its maxsize (their percentages
    /// refer to the height of the glyph).
    /// </summary>
    private static (double Ascent, double Descent) GetBlockStretchTarget(MathFontParameters parameters, ushort glyph, MathOperatorProperties properties, double ascent, double descent)
    {
        var axisHeight = parameters.AxisHeight;
        if (properties.IsSymmetric)
        {
            var half = Math.Max(ascent - axisHeight, descent + axisHeight);
            ascent = half + axisHeight;
            descent = half - axisHeight;
        }

        var (glyphAscent, glyphDescent) = GetGlyphInk(parameters.Font, glyph, parameters.EmSize);
        var glyphHeight = glyphAscent + glyphDescent;
        var minSize = Math.Max(0, properties.MinSize.Resolve(glyphHeight));
        var maxSize = properties.MaxSize is { } max ? Math.Max(minSize, max.Resolve(glyphHeight)) : double.PositiveInfinity;
        var target = ascent + descent;
        if (target <= 0)
        {
            ascent = (minSize / 2) + axisHeight;
            descent = minSize - ascent;
        }
        else if (target < minSize)
        {
            ascent = (Math.Max(0, ascent - axisHeight) * minSize / target) + axisHeight;
            descent = minSize - ascent;
        }
        else if (maxSize < target)
        {
            ascent = (Math.Max(0, ascent - axisHeight) * maxSize / target) + axisHeight;
            descent = maxSize - ascent;
        }

        return (ascent, descent);
    }

    /// <summary>
    /// The intrinsic sizes of an operator: "the preferred inline size of a glyph stretched along the block axis" for the stretchy
    /// operators along the block axis and the large operators in <c>math-style: normal</c>, else the sizes of <c>mtext</c>.
    /// </summary>
    private static IntrinsicSizes ComputeOperatorIntrinsicSizes(LayoutContext context, ContainerBox box, in IntrinsicSizingInput input)
    {
        if (GetOperatorGlyph(box) is var (parameters, glyph))
        {
            var (_, properties) = GetOperator(box);
            var usesVariants = properties.IsStretchy
                ? properties.StretchAxis == MathStretchAxis.Block
                : properties.IsLargeOp && !IsCompact(box);
            if (usesVariants && MathStretchyGlyphs.CanStretch(parameters.Font, glyph, vertical: true))
                return IntrinsicSizes.FromSize(MathStretchyGlyphs.GetPreferredInlineSize(parameters.Font, parameters.EmSize, glyph));
        }

        return ComputeTokenIntrinsicSizes(context, box, input);
    }
}
