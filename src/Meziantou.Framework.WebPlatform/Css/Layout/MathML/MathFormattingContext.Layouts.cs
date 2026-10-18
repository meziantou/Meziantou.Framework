using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The layouts of mrow (and the elements laid out as mrow), mspace, mfrac, msqrt, mroot and mpadded
internal sealed partial class MathFormattingContext
{
    private const int SquareRootCharacter = 0x221A;

    private static bool IsCompact(ContainerBox box) => box.Style.GetComputedValue("math-style") == "compact";

    private static bool IsRightToLeft(ContainerBox box) => box.Style.Layout.Direction == Direction.Rtl;

    /// <summary>
    /// https://w3c.github.io/mathml-core/#layout-of-mrow: the children are put in a row with their alphabetic baselines aligned,
    /// with the spacing of the embellished operators and the italic corrections of the slanted children; the stretchy operators
    /// are stretched along the block axis (https://w3c.github.io/mathml-core/#dfn-algorithm-for-stretching-operators-along-the-block-axis).
    /// </summary>
    private static MathContent LayoutRow(LayoutContext context, ContainerBox box, MathStretchConstraint? stretch, MathMode mode)
    {
        var boxes = GetInFlowChildren(box);
        var children = new MathChild[boxes.Count];
        var content = new MathContent { Children = children };
        MeasureRowChildren(context, box, boxes, children, stretch, mode);

        // "Set add-space to true if the box corresponds to a math element or is not an embellished operator"; the anonymous mrow of
        // an mtd adds the spacing too (a table cell is not an embellished operator, as in the browsers: WPT
        // mathml/presentation-markup/mrow/no-spacing.html)
        var addSpace = (box.Element is { NamespaceUri: Namespaces.MathML, LocalName: "math" } && box.PseudoElement is null && !box.IsAnonymous)
            || (box.IsAnonymous && box.Parent?.Display.Internal == DisplayInternal.TableCell)
            || MathLayoutBoxes.GetCoreOperator(box) is null;
        double inlineOffset = 0;
        double previousItalicCorrection = 0;
        for (var i = 0; i < children.Length; i++)
        {
            ref var child = ref children[i];
            if (!child.IsSlanted)
            {
                inlineOffset += previousItalicCorrection;
            }

            if (child.IsEmbellishedOperator && addSpace)
            {
                inlineOffset += child.Operator.LSpace;
            }

            content.Placements.Add(new MathPlacement(i, inlineOffset, 0));
            inlineOffset += child.InlineSize;
            previousItalicCorrection = child.IsSlanted ? child.ItalicCorrection : 0;
            if (child.IsEmbellishedOperator && addSpace)
            {
                inlineOffset += child.Operator.RSpace;
            }
        }

        content.InlineSize = inlineOffset + previousItalicCorrection;
        content.ItalicCorrection = previousItalicCorrection;
        ComputeBlockMetrics(content);
        return content;
    }

    /// <summary>Measures the children of a row, stretching the stretchy operators along the block axis.</summary>
    private static void MeasureRowChildren(LayoutContext context, ContainerBox box, List<ContainerBox> boxes, MathChild[] children, MathStretchConstraint? stretch, MathMode mode)
    {
        // "If there is a block stretch size constraint or an inline stretch size constraint then the element being laid out is an
        // embellished operator. Lay out the one in-flow child that is an embellished operator with the same stretch size constraint
        // and all the other in-flow children without any stretch size constraint and stop."
        if (stretch is not null || mode != MathMode.Layout)
        {
            for (var i = 0; i < boxes.Count; i++)
            {
                var constraint = stretch is not null && MathLayoutBoxes.GetCoreOperator(boxes[i]) is not null ? stretch : null;
                children[i] = Measure(context, box, boxes[i], constraint, mode);
            }

            return;
        }

        // The children that are not stretchy operators along the block axis, then the stretchy ones with the ink extent of the others
        var toStretch = new List<int>();
        var hasOthers = false;
        double ascent = 0;
        double descent = 0;
        for (var i = 0; i < boxes.Count; i++)
        {
            var (core, properties) = GetOperator(boxes[i]);
            if (core is not null && properties.IsStretchy && properties.StretchAxis == MathStretchAxis.Block)
            {
                toStretch.Add(i);
                continue;
            }

            children[i] = Measure(context, box, boxes[i], stretch: null, mode);
            ascent = hasOthers ? Math.Max(ascent, children[i].InkAscent) : children[i].InkAscent;
            descent = hasOthers ? Math.Max(descent, children[i].InkDescent) : children[i].InkDescent;
            hasOthers = true;
        }

        if (toStretch.Count == 0)
            return;

        // "If LNotToStretch is empty, perform layout with block stretch size constraint (0, 0) for all the items of LToStretch": the
        // ink extent of these layouts gives the target (e.g. an embellished mrow whose space-like children are tall)
        if (!hasOthers)
        {
            foreach (var i in toStretch)
            {
                children[i] = Measure(context, box, boxes[i], MathStretchConstraint.Block(0, 0), mode);
                ascent = hasOthers ? Math.Max(ascent, children[i].InkAscent) : children[i].InkAscent;
                descent = hasOthers ? Math.Max(descent, children[i].InkDescent) : children[i].InkDescent;
                hasOthers = true;
            }
        }

        var target = MathStretchConstraint.Block(ascent, descent);
        foreach (var i in toStretch)
        {
            children[i] = Measure(context, box, boxes[i], target, mode);
        }
    }

    /// <summary>
    /// https://w3c.github.io/mathml-core/#space-mspace: the math content has no inline size (the <c>width</c> property sizes the
    /// content box); its line-ascent is the <c>height</c> attribute, its line-descent the rest of the block size of the content box.
    /// </summary>
    private static MathContent LayoutSpace(ContainerBox box)
    {
        var ascent = MathLayoutBoxes.TryResolveLength(box, "height", out var height) && !height.HasPercentage ? Math.Max(0, height.Resolve(0)) : 0;
        return new MathContent { Ascent = ascent, Descent = -ascent, InkAscent = ascent, InkDescent = -ascent };
    }

    /// <summary>Returns the requested line-descent of an mspace: the block size of its content box minus its requested line-ascent.</summary>
    private static void CompleteSpace(MathContent content, in ContentArea area)
    {
        var blockSize = area.BlockSize ?? 0;
        content.Descent = blockSize - content.Ascent;
        content.InkDescent = content.Descent;
    }

    /// <summary>https://w3c.github.io/mathml-core/#fractions-mfrac.</summary>
    private static MathContent LayoutFraction(LayoutContext context, ContainerBox box, MathStretchConstraint? stretch, MathMode mode)
    {
        var boxes = GetInFlowChildren(box);
        if (boxes.Count != 2)
            return LayoutRow(context, box, stretch, mode);

        // "If there is an inline stretch size constraint or a block stretch size constraint then the numerator is also laid out with
        // the same stretch size constraint [...] The denominator is always laid out without any stretch size constraint."
        var children = new MathChild[2];
        children[0] = Measure(context, box, boxes[0], stretch, mode);
        children[1] = Measure(context, box, boxes[1], stretch: null, mode);
        ref var numerator = ref children[0];
        ref var denominator = ref children[1];
        var content = new MathContent { Children = children, Centered = true };
        content.InlineSize = Math.Max(numerator.InlineSize, denominator.InlineSize);

        var parameters = MathFontParameters.Get(box.Style);
        var compact = IsCompact(box);
        var axisHeight = parameters.AxisHeight;

        // The fraction line thickness: the linethickness attribute, a percentage of the default thickness, negative values are 0
        var thickness = parameters.FractionRuleThickness;
        if (MathLayoutBoxes.TryResolveLength(box, "linethickness", out var lineThickness))
        {
            thickness = Math.Max(0, lineThickness.Resolve(thickness));
        }

        double numeratorShift;
        double denominatorShift;
        if (thickness > 0)
        {
            // https://w3c.github.io/mathml-core/#fraction-with-nonzero-line-thickness
            numeratorShift = Math.Max(
                compact ? parameters.FractionNumeratorShiftUp : parameters.FractionNumeratorDisplayStyleShiftUp,
                axisHeight + (thickness / 2) + (compact ? parameters.FractionNumeratorGapMin : parameters.FractionNumDisplayStyleGapMin) + numerator.InkDescent);
            denominatorShift = Math.Max(
                compact ? parameters.FractionDenominatorShiftDown : parameters.FractionDenominatorDisplayStyleShiftDown,
                (thickness / 2) + (compact ? parameters.FractionDenominatorGapMin : parameters.FractionDenomDisplayStyleGapMin) + denominator.InkAscent - axisHeight);
            content.AddItem(new MathItem(0, -(axisHeight + (thickness / 2)), 0, thickness, Glyph: null, SpansContentBox: true));
        }
        else
        {
            // https://w3c.github.io/mathml-core/#fraction-with-zero-line-thickness
            numeratorShift = compact ? parameters.StackTopShiftUp : parameters.StackTopDisplayStyleShiftUp;
            denominatorShift = compact ? parameters.StackBottomShiftDown : parameters.StackBottomDisplayStyleShiftDown;
            var gap = denominatorShift - denominator.InkAscent + (numeratorShift - numerator.InkDescent);
            var delta = (compact ? parameters.StackGapMin : parameters.StackDisplayStyleGapMin) - gap;
            if (delta > 0)
            {
                numeratorShift += delta / 2;
                denominatorShift += delta - (delta / 2);
            }
        }

        content.Placements.Add(new MathPlacement(0, (content.InlineSize - numerator.InlineSize) / 2, -numeratorShift));
        content.Placements.Add(new MathPlacement(1, (content.InlineSize - denominator.InlineSize) / 2, denominatorShift));
        ComputeBlockMetrics(content);
        if (thickness > 0)
        {
            content.Ascent = Math.Max(content.Ascent, axisHeight + (thickness / 2));
            content.Descent = Math.Max(content.Descent, (thickness / 2) - axisHeight);
        }

        content.SetInkFromLine();
        return content;
    }

    /// <summary>The radical glyph and the metrics of a square root around a base (https://w3c.github.io/mathml-core/#radical-symbol).</summary>
    private static (MathShapedGlyph? Glyph, double Width, double Ascent, double Descent, double OverbarTop) LayoutRadical(ContainerBox box, in MathChild baseChild, MathMode mode, MathFontParameters parameters)
    {
        var compact = IsCompact(box);
        var gap = compact ? parameters.RadicalVerticalGap : parameters.RadicalDisplayStyleVerticalGap;
        var thickness = parameters.RadicalRuleThickness;
        var extraAscender = parameters.RadicalExtraAscender;
        var font = parameters.Font;
        MathShapedGlyph? glyph = null;
        double width = 0;
        // A font without a mirrored radical (no rtlm variant, U+221A has no Bidi_Mirroring_Glyph) gives the unmirrored glyph in rtl,
        // as in the browsers (WPT mathml/relations/css-styling/writing-mode/writing-mode-002.html)
        var rightToLeft = IsRightToLeft(box);
        if (MathStretchyGlyphs.TryGetGlyph(font, SquareRootCharacter, rightToLeft, out var radical) || (rightToLeft && MathStretchyGlyphs.TryGetGlyph(font, SquareRootCharacter, rightToLeft: false, out radical)))
        {
            if (mode == MathMode.Layout)
            {
                // "The radical target size for the stretchy radical glyph is the sum of RadicalRuleThickness, radical gap and the ink height of the base"
                var target = thickness + gap + baseChild.InkAscent + baseChild.InkDescent;
                glyph = MathStretchyGlyphs.Shape(font, parameters.EmSize, radical, vertical: true, target) ?? MathStretchyGlyphs.ShapeGlyph(font, parameters.EmSize, radical);
                width = glyph.Width;
            }
            else
            {
                width = MathStretchyGlyphs.GetPreferredInlineSize(font, parameters.EmSize, radical);
            }
        }

        var ascent = Math.Max(baseChild.Ascent, baseChild.InkAscent + gap + thickness + extraAscender);
        var descent = Math.Max(baseChild.Descent, (glyph is null ? 0 : glyph.Ascent + glyph.Descent) + extraAscender - ascent);

        // The overbar: "Its vertical center is shifted away from the alphabetic baseline by a distance towards the line-over equal to
        // the line-ascent of the math content, minus the RadicalExtraAscender, minus half the RadicalRuleThickness"
        var overbarTop = -(ascent - extraAscender);
        return (glyph, width, ascent, descent, overbarTop);
    }

    /// <summary>https://w3c.github.io/mathml-core/#square-root: the radical symbol and the overbar around the msqrt base (the anonymous mrow).</summary>
    private static MathContent LayoutSquareRoot(LayoutContext context, ContainerBox box, MathMode mode)
    {
        var boxes = GetInFlowChildren(box);
        var children = new MathChild[boxes.Count];
        for (var i = 0; i < boxes.Count; i++)
        {
            children[i] = Measure(context, box, boxes[i], stretch: null, mode);
        }

        var baseChild = children.Length > 0 ? children[0] : default;
        var parameters = MathFontParameters.Get(box.Style);
        var (glyph, width, ascent, descent, overbarTop) = LayoutRadical(box, baseChild, mode, parameters);
        var content = new MathContent { Children = children, InlineSize = width + baseChild.InlineSize, Ascent = ascent, Descent = descent };
        content.SetInkFromLine();
        if (children.Length > 0)
        {
            content.Placements.Add(new MathPlacement(0, width, 0));
        }

        AddRadicalItems(content, glyph, 0, width, baseChild.InlineSize, overbarTop, parameters.RadicalRuleThickness);
        return content;
    }

    private static void AddRadicalItems(MathContent content, MathShapedGlyph? glyph, double inlineOffset, double width, double baseInlineSize, double overbarTop, double thickness)
    {
        content.AddItem(new MathItem(inlineOffset + width, overbarTop, baseInlineSize, thickness, Glyph: null));
        if (glyph is not null)
        {
            // "At block offset shifted away from the line-over edge of the overbar towards the line-under by a distance given by the
            // ascent of the box metrics of the radical glyph"
            content.AddItem(new MathItem(inlineOffset, overbarTop + glyph.Ascent, glyph.Width, 0, glyph));
        }
    }

    /// <summary>https://w3c.github.io/mathml-core/#root-with-index.</summary>
    private static MathContent LayoutRoot(LayoutContext context, ContainerBox box, MathMode mode)
    {
        var boxes = GetInFlowChildren(box);
        if (boxes.Count != 2)
            return LayoutRow(context, box, stretch: null, mode);

        var children = new MathChild[2];
        children[0] = Measure(context, box, boxes[0], stretch: null, mode);
        children[1] = Measure(context, box, boxes[1], stretch: null, mode);
        ref var baseChild = ref children[0];
        ref var index = ref children[1];
        var parameters = MathFontParameters.Get(box.Style);

        // B: the base with the radical symbol and the overbar
        var (glyph, width, ascent, descent, overbarTop) = LayoutRadical(box, baseChild, mode, parameters);
        var rootInlineSize = width + baseChild.InlineSize;
        var kernBefore = Math.Max(0, parameters.RadicalKernBeforeDegree);
        var kernAfter = Math.Max(-index.InlineSize, parameters.RadicalKernAfterDegree);

        // "The alphabetic baseline of the index is shifted away from the line-under edge by a distance of
        // RadicalDegreeBottomRaisePercent × the block size of B + the line-descent of the index's margin box"
        var indexBaseline = -(-descent + (parameters.RadicalDegreeBottomRaisePercent * (ascent + descent)) + index.Descent);
        var content = new MathContent
        {
            Children = children,
            InlineSize = kernBefore + index.InlineSize + kernAfter + rootInlineSize,
            Ascent = Math.Max(ascent, -indexBaseline + index.Ascent),
            Descent = Math.Max(descent, indexBaseline + index.Descent),
        };
        content.SetInkFromLine();
        var rootOffset = kernBefore + index.InlineSize + kernAfter;
        content.Placements.Add(new MathPlacement(1, kernBefore, indexBaseline));
        content.Placements.Add(new MathPlacement(0, rootOffset + width, 0));
        AddRadicalItems(content, glyph, rootOffset, width, baseChild.InlineSize, overbarTop, parameters.RadicalRuleThickness);
        return content;
    }

    /// <summary>https://w3c.github.io/mathml-core/#adjust-space-around-content-mpadded: the mpadded inner box (the anonymous mrow) with the requested parameters.</summary>
    private static MathContent LayoutPadded(LayoutContext context, ContainerBox box, MathStretchConstraint? stretch, MathMode mode)
    {
        var boxes = GetInFlowChildren(box);
        var children = new MathChild[boxes.Count];
        for (var i = 0; i < boxes.Count; i++)
        {
            children[i] = Measure(context, box, boxes[i], stretch, mode);
        }

        var inner = children.Length > 0 ? children[0] : default;
        var height = TryGetPaddedLength(box, "height") is { } requestedHeight ? Math.Max(0, requestedHeight) : inner.Ascent;
        var depth = TryGetPaddedLength(box, "depth") is { } requestedDepth ? Math.Max(0, requestedDepth) : inner.Descent;
        var lspace = TryGetPaddedLength(box, "lspace") is { } requestedLspace ? Math.Max(0, requestedLspace) : 0;
        var voffset = TryGetPaddedLength(box, "voffset") ?? 0;

        // The requested width is the width property (the content box): the math content has the inline size of the inner box
        var content = new MathContent { Children = children, InlineSize = inner.InlineSize, Ascent = height, Descent = depth, ItalicCorrection = inner.ItalicCorrection };
        content.SetInkFromLine();
        if (children.Length > 0)
        {
            content.Placements.Add(new MathPlacement(0, lspace, -voffset));
        }

        return content;
    }

    /// <summary>Returns the value of an attribute of mpadded when it is present, valid and not a percentage, else null.</summary>
    private static double? TryGetPaddedLength(ContainerBox box, string attribute)
        => MathLayoutBoxes.TryResolveLength(box, attribute, out var value) && !value.HasPercentage ? value.Resolve(0) : null;

    /// <summary>The ink extent of a glyph of a font (font units scaled to CSS px): the ascent and the descent of its bounding box.</summary>
    private static (double Ascent, double Descent) GetGlyphInk(MatchedFont font, ushort glyph, double emSize)
    {
        var scale = emSize / font.Font.UnitsPerEm;
        return font.Font.TryGetGlyphBounds(glyph, out var bounds) ? (bounds.YMax * scale, -bounds.YMin * scale) : (0, 0);
    }
}
