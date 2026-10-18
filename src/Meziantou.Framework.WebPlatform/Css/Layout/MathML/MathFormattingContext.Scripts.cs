using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The layouts of the script and limit schemata: msub, msup, msubsup, mmultiscripts, munder, mover, munderover
// (https://w3c.github.io/mathml-core/#script-and-limit-schemata)
internal sealed partial class MathFormattingContext
{
    /// <summary>
    /// https://w3c.github.io/mathml-core/#subscripts-and-superscripts-msub-msup-msubsup and
    /// https://w3c.github.io/mathml-core/#prescripts-and-tensor-indices-mmultiscripts: a base and pairs of subscripts and
    /// superscripts (msub and msup have one script of the pair; mmultiscripts may have prescripts).
    /// </summary>
    private static MathContent LayoutScripts(LayoutContext context, ContainerBox box, MathBoxKind kind, MathStretchConstraint? stretch, MathMode mode)
    {
        var boxes = GetInFlowChildren(box);

        // The children of the scripts: -1 is a missing script of a pair; the mprescripts box is placed with the base
        var postscripts = new List<(int Sub, int Sup)>();
        var prescripts = new List<(int Sub, int Sup)>();
        var prescriptsBox = -1;
        switch (kind)
        {
            case MathBoxKind.Sub when boxes.Count == 2:
                postscripts.Add((1, -1));
                break;
            case MathBoxKind.Sup when boxes.Count == 2:
                postscripts.Add((-1, 1));
                break;
            case MathBoxKind.SubSup when boxes.Count == 3:
                postscripts.Add((1, 2));
                break;
            case MathBoxKind.Multiscripts when TryGetMultiscripts(boxes, postscripts, prescripts, out prescriptsBox):
                break;
            default:
                return LayoutRow(context, box, stretch, mode);
        }

        // The base is laid out with the stretch size constraint, the scripts without
        var children = new MathChild[boxes.Count];
        children[0] = Measure(context, box, boxes[0], stretch, mode);
        for (var i = 1; i < boxes.Count; i++)
        {
            children[i] = Measure(context, box, boxes[i], stretch: null, mode);
        }

        return ArrangeScripts(box, children, postscripts, prescripts, prescriptsBox, kind == MathBoxKind.Multiscripts);
    }

    /// <summary>
    /// Splits the children of a valid mmultiscripts (https://w3c.github.io/mathml-core/#prescripts-and-tensor-indices-mmultiscripts):
    /// a base that is not mprescripts, an even number of postscripts, then optionally mprescripts and an even number of prescripts.
    /// </summary>
    private static bool TryGetMultiscripts(List<ContainerBox> boxes, List<(int Sub, int Sup)> postscripts, List<(int Sub, int Sup)> prescripts, out int prescriptsBox)
    {
        prescriptsBox = -1;
        if (boxes.Count == 0 || IsPrescripts(boxes[0]))
            return false;

        for (var i = 1; i < boxes.Count; i++)
        {
            if (IsPrescripts(boxes[i]))
            {
                if (prescriptsBox >= 0)
                    return false;

                prescriptsBox = i;
            }
        }

        var postEnd = prescriptsBox >= 0 ? prescriptsBox : boxes.Count;
        if ((postEnd - 1) % 2 != 0 || (prescriptsBox >= 0 && (boxes.Count - prescriptsBox - 1) % 2 != 0))
            return false;

        for (var i = 1; i < postEnd; i += 2)
        {
            postscripts.Add((i, i + 1));
        }

        if (prescriptsBox >= 0)
        {
            for (var i = prescriptsBox + 1; i < boxes.Count; i += 2)
            {
                prescripts.Add((i, i + 1));
            }
        }

        return true;

        static bool IsPrescripts(ContainerBox box) => box.Element is { NamespaceUri: Dom.Namespaces.MathML, LocalName: "mprescripts" } && box.PseudoElement is null;
    }

    /// <summary>Places the base and the scripts (https://w3c.github.io/mathml-core/#base-with-subscript-and-superscript).</summary>
    private static MathContent ArrangeScripts(ContainerBox box, MathChild[] children, List<(int Sub, int Sup)> postscripts, List<(int Sub, int Sup)> prescripts, int prescriptsBox, bool isMultiscripts)
    {
        var parameters = MathFontParameters.Get(box.Style);
        ref var baseChild = ref children[0];

        // "LargeOpItalicCorrection is the italic correction of the base if it is an embellished operator with the largeop property
        // and 0 otherwise"; ItalicCorrection is its italic correction otherwise
        var isLargeOp = baseChild.IsEmbellishedOperator && baseChild.Operator.IsLargeOp;
        var largeOpItalicCorrection = isLargeOp ? baseChild.ItalicCorrection : 0;
        var italicCorrection = isLargeOp ? 0 : baseChild.ItalicCorrection;
        var cramped = box.Style.GetComputedValue("math-shift") == "compact";

        // SubShift and SuperShift: the maximum of the shifts of each pair
        double subShift = 0;
        double superShift = 0;
        var first = true;
        foreach (var pairs in (ReadOnlySpan<List<(int Sub, int Sup)>>)[postscripts, prescripts])
        {
            foreach (var (sub, sup) in pairs)
            {
                var (pairSubShift, pairSuperShift) = GetScriptShifts(parameters, baseChild, sub >= 0 ? children[sub] : null, sup >= 0 ? children[sup] : null, cramped);
                subShift = first ? pairSubShift : Math.Max(subShift, pairSubShift);
                superShift = first ? pairSuperShift : Math.Max(superShift, pairSuperShift);
                first = false;
            }
        }

        var content = new MathContent { Children = children };
        var spaceAfterScript = parameters.SpaceAfterScript;

        // The prescripts, right-aligned in their columns
        double inlineOffset = 0;
        foreach (var (sub, sup) in prescripts)
        {
            inlineOffset += spaceAfterScript;
            var pairInlineSize = Math.Max(children[sub].InlineSize, children[sup].InlineSize);
            content.Placements.Add(new MathPlacement(sub, inlineOffset + pairInlineSize - children[sub].InlineSize, subShift));
            content.Placements.Add(new MathPlacement(sup, inlineOffset + pairInlineSize - children[sup].InlineSize, -superShift));
            inlineOffset += pairInlineSize;
        }

        content.Placements.Add(new MathPlacement(0, inlineOffset, 0));
        if (prescriptsBox >= 0)
        {
            content.Placements.Add(new MathPlacement(prescriptsBox, inlineOffset, 0));
        }

        inlineOffset += baseChild.InlineSize;
        var inlineSize = inlineOffset;
        foreach (var (sub, sup) in postscripts)
        {
            double pairInlineSize = 0;
            if (sub >= 0)
            {
                content.Placements.Add(new MathPlacement(sub, inlineOffset - largeOpItalicCorrection, subShift));
                pairInlineSize = Math.Max(pairInlineSize, children[sub].InlineSize - largeOpItalicCorrection);
            }

            if (sup >= 0)
            {
                content.Placements.Add(new MathPlacement(sup, inlineOffset + italicCorrection, -superShift));
                pairInlineSize = Math.Max(pairInlineSize, children[sup].InlineSize + italicCorrection);
            }

            if (isMultiscripts)
            {
                // https://w3c.github.io/mathml-core/#base-with-prescripts-and-postscripts: the inline size is at least the end of
                // each script; the column of a pair is as wide as its widest script
                inlineSize = Math.Max(inlineSize, inlineOffset + pairInlineSize);
                inlineOffset += Math.Max(sub >= 0 ? children[sub].InlineSize : 0, sup >= 0 ? children[sup].InlineSize : 0) + spaceAfterScript;
            }
            else
            {
                inlineOffset += pairInlineSize + spaceAfterScript;
            }
        }

        content.InlineSize = isMultiscripts ? Math.Max(inlineSize, inlineOffset) : inlineOffset;
        ComputeBlockMetrics(content);
        return content;
    }

    /// <summary>The shifts of the subscript and of the superscript of a pair, adjusted to the minimum gap between them (https://w3c.github.io/mathml-core/#base-with-subscript-and-superscript).</summary>
    private static (double SubShift, double SuperShift) GetScriptShifts(MathFontParameters parameters, in MathChild baseChild, MathChild? sub, MathChild? sup, bool cramped)
    {
        double subShift = 0;
        double superShift = 0;
        if (sub is { } subscript)
        {
            subShift = Math.Max(parameters.SubscriptShiftDown, Math.Max(subscript.InkAscent - parameters.SubscriptTopMax, parameters.SubscriptBaselineDropMin + baseChild.InkDescent));
        }

        if (sup is { } superscript)
        {
            superShift = Math.Max(
                cramped ? parameters.SuperscriptShiftUpCramped : parameters.SuperscriptShiftUp,
                Math.Max(parameters.SuperscriptBottomMin + superscript.InkDescent, baseChild.InkAscent - parameters.SuperscriptBaselineDropMax));
        }

        if (sub is { } s && sup is { } p)
        {
            var subSuperGap = subShift - s.InkAscent + (superShift - p.InkDescent);
            var gapMin = parameters.SubSuperscriptGapMin;
            if (subSuperGap < gapMin)
            {
                var delta = parameters.SuperscriptBottomMaxWithSubscript - (superShift - p.InkDescent);
                if (delta > 0)
                {
                    delta = Math.Min(delta, gapMin - subSuperGap);
                    superShift += delta;
                    subSuperGap += delta;
                }

                delta = gapMin - subSuperGap;
                if (delta > 0)
                {
                    subShift += delta;
                }
            }
        }

        return (subShift, superShift);
    }

    /// <summary>https://w3c.github.io/mathml-core/#underscripts-and-overscripts-munder-mover-munderover.</summary>
    private static MathContent LayoutUnderOver(LayoutContext context, ContainerBox box, MathBoxKind kind, MathStretchConstraint? stretch, MathMode mode)
    {
        var boxes = GetInFlowChildren(box);
        var expected = kind == MathBoxKind.UnderOver ? 3 : 2;
        if (boxes.Count != expected)
            return LayoutRow(context, box, stretch, mode);

        // "If the munder, mover or munderover elements have a computed math-style property equal to compact and their base is an
        // embellished operator with the movablelimits property, then their layout algorithms are respectively the same as the
        // ones described for msub, msup and msubsup"
        var (baseCore, baseOperator) = GetOperator(boxes[0]);
        if (IsCompact(box) && baseCore is not null && baseOperator.IsMovableLimits)
        {
            return LayoutScripts(context, box, kind switch { MathBoxKind.Under => MathBoxKind.Sub, MathBoxKind.Over => MathBoxKind.Sup, _ => MathBoxKind.SubSup }, stretch, mode);
        }

        var children = new MathChild[boxes.Count];
        MeasureUnderOverChildren(context, box, boxes, children, stretch, mode);
        var under = kind == MathBoxKind.Over ? -1 : 1;
        var over = kind switch { MathBoxKind.Over => 1, MathBoxKind.UnderOver => 2, _ => -1 };
        return ArrangeUnderOver(box, children, under, over);
    }

    /// <summary>
    /// Measures the children of munder, mover and munderover, stretching the operators along the inline axis
    /// (https://w3c.github.io/mathml-core/#dfn-algorithm-for-stretching-operators-along-the-inline-axis).
    /// </summary>
    private static void MeasureUnderOverChildren(LayoutContext context, ContainerBox box, List<ContainerBox> boxes, MathChild[] children, MathStretchConstraint? stretch, MathMode mode)
    {
        var start = 0;
        if (stretch is not null)
        {
            children[0] = Measure(context, box, boxes[0], stretch, mode);
            start = 1;
        }

        var toStretch = new List<int>();
        var others = new List<int>();
        for (var i = start; i < boxes.Count; i++)
        {
            var (core, properties) = GetOperator(boxes[i]);
            if (mode == MathMode.Layout && core is not null && properties.IsStretchy && properties.StretchAxis == MathStretchAxis.Inline)
            {
                toStretch.Add(i);
            }
            else
            {
                others.Add(i);
            }
        }

        foreach (var i in others)
        {
            children[i] = Measure(context, box, boxes[i], stretch: null, mode);
        }

        if (toStretch.Count == 0)
            return;

        if (others.Count == 0)
        {
            foreach (var i in toStretch)
            {
                children[i] = Measure(context, box, boxes[i], MathStretchConstraint.Inline(0), mode);
            }

            others = toStretch;
        }

        double target = 0;
        foreach (var i in others)
        {
            target = Math.Max(target, children[i].InlineSize);
        }

        foreach (var i in toStretch)
        {
            children[i] = Measure(context, box, boxes[i], MathStretchConstraint.Inline(target), mode);
        }
    }

    /// <summary>Places the base, the underscript and the overscript (the scripts are -1 when absent).</summary>
    private static MathContent ArrangeUnderOver(ContainerBox box, MathChild[] children, int under, int over)
    {
        var parameters = MathFontParameters.Get(box.Style);
        ref var baseChild = ref children[0];
        var isLargeOp = baseChild.IsEmbellishedOperator && baseChild.Operator.IsLargeOp;
        var isInlineStretchy = baseChild.IsEmbellishedOperator && baseChild.Operator.IsStretchy && baseChild.Operator.StretchAxis == MathStretchAxis.Inline;
        var largeOpItalicCorrection = isLargeOp ? baseChild.ItalicCorrection : 0;
        var element = box.Element;

        // The inline offsets: the centers of the base and of the scripts are aligned (the overscript on its top accent attachment),
        // shifted by half the italic correction of a large operator
        var max = baseChild.InlineSize / 2;
        var min = -baseChild.InlineSize / 2;
        double topAccentAttachment = 0;
        if (under >= 0)
        {
            max = Math.Max(max, (children[under].InlineSize / 2) - (largeOpItalicCorrection / 2));
            min = Math.Min(min, (-children[under].InlineSize / 2) - (largeOpItalicCorrection / 2));
        }

        if (over >= 0)
        {
            topAccentAttachment = children[over].TopAccentAttachment ?? (children[over].InlineSize / 2);
            max = Math.Max(max, children[over].InlineSize - topAccentAttachment + (largeOpItalicCorrection / 2));
            min = Math.Min(min, -topAccentAttachment + (largeOpItalicCorrection / 2));
        }

        var content = new MathContent { Children = children, Centered = true, InlineSize = max - min };
        content.Placements.Add(new MathPlacement(0, -min - (baseChild.InlineSize / 2), 0));
        double overTop = 0;
        double underBottom = 0;
        if (under >= 0)
        {
            ref var underscript = ref children[under];
            double underShift;
            if (isLargeOp)
            {
                underShift = Math.Max(parameters.LowerLimitBaselineDropMin, parameters.LowerLimitGapMin + underscript.Ascent);
            }
            else if (isInlineStretchy)
            {
                underShift = Math.Max(parameters.StretchStackBottomShiftDown, parameters.StretchStackGapBelowMin + underscript.Ascent);
            }
            else
            {
                var accentUnder = element?.GetAttributeValue("accentunder") is { } value && InfraAscii.EqualsIgnoreAsciiCase(value, "true");
                underShift = (accentUnder ? 0 : parameters.UnderbarVerticalGap) + underscript.Ascent;
                underBottom = baseChild.InkDescent + underShift + underscript.Descent + parameters.UnderbarExtraDescender;
            }

            content.Placements.Add(new MathPlacement(under, -min - (underscript.InlineSize / 2) - (largeOpItalicCorrection / 2), baseChild.InkDescent + underShift));
        }

        if (over >= 0)
        {
            ref var overscript = ref children[over];
            double overShift;
            if (isLargeOp)
            {
                overShift = Math.Max(parameters.UpperLimitBaselineRiseMin, parameters.UpperLimitGapMin + overscript.Descent);
            }
            else if (isInlineStretchy)
            {
                overShift = Math.Max(parameters.StretchStackTopShiftUp, parameters.StretchStackGapAboveMin + overscript.Descent);
            }
            else
            {
                // An accent is at least AccentBaseHeight above the baseline, else the overbar gap separates it from the base
                var accent = element?.GetAttributeValue("accent") is { } value && InfraAscii.EqualsIgnoreAsciiCase(value, "true");
                overShift = (accent ? Math.Max(0, parameters.AccentBaseHeight - baseChild.Ascent) : parameters.OverbarVerticalGap) + overscript.Descent;
                overTop = baseChild.InkAscent + overShift + overscript.Ascent + parameters.OverbarExtraAscender;
            }

            content.Placements.Add(new MathPlacement(over, -min - topAccentAttachment + (largeOpItalicCorrection / 2), -(baseChild.InkAscent + overShift)));
        }

        // The OverbarExtraAscender and UnderbarExtraDescender are added beyond the scripts
        ComputeBlockMetrics(content);
        content.Ascent = Math.Max(content.Ascent, overTop);
        content.Descent = Math.Max(content.Descent, underBottom);
        content.SetInkFromLine();
        return content;
    }
}
