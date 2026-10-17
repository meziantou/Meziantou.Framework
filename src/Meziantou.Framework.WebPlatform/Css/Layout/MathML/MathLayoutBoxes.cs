using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The layout algorithms of the MathML boxes whose display is <c>block math</c> or <c>inline math</c> (https://w3c.github.io/mathml-core/#presentation-markup).</summary>
internal enum MathBoxKind : byte
{
    /// <summary>Not a MathML box laid out by the math layout (another display type, or not a MathML element).</summary>
    None,

    /// <summary>
    /// The grouping elements (<c>math</c>, <c>mrow</c>, <c>mstyle</c>, <c>merror</c>, <c>mphantom</c>, <c>maction</c>,
    /// <c>semantics</c>, <c>a</c>, <c>mprescripts</c>), the unknown MathML elements and the anonymous mrow boxes
    /// (https://w3c.github.io/mathml-core/#layout-of-mrow).
    /// </summary>
    Row,

    /// <summary>The token elements laid out as <c>mtext</c>: <c>mtext</c>, <c>mi</c>, <c>mn</c>, <c>ms</c>, <c>annotation</c>, <c>annotation-xml</c> (https://w3c.github.io/mathml-core/#layout-of-mtext).</summary>
    Token,

    /// <summary><c>mo</c> (https://w3c.github.io/mathml-core/#layout-of-operators).</summary>
    Operator,

    /// <summary><c>mspace</c> (https://w3c.github.io/mathml-core/#space-mspace).</summary>
    Space,

    /// <summary><c>mfrac</c> (https://w3c.github.io/mathml-core/#fractions-mfrac).</summary>
    Fraction,

    /// <summary><c>msqrt</c> (https://w3c.github.io/mathml-core/#square-root).</summary>
    SquareRoot,

    /// <summary><c>mroot</c> (https://w3c.github.io/mathml-core/#root-with-index).</summary>
    Root,

    /// <summary><c>mpadded</c> (https://w3c.github.io/mathml-core/#adjust-space-around-content-mpadded).</summary>
    Padded,

    /// <summary><c>msub</c>.</summary>
    Sub,

    /// <summary><c>msup</c>.</summary>
    Sup,

    /// <summary><c>msubsup</c>.</summary>
    SubSup,

    /// <summary><c>munder</c>.</summary>
    Under,

    /// <summary><c>mover</c>.</summary>
    Over,

    /// <summary><c>munderover</c>.</summary>
    UnderOver,

    /// <summary><c>mmultiscripts</c>.</summary>
    Multiscripts,
}

/// <summary>The stretch axis of an embellished operator (https://w3c.github.io/mathml-core/#dfn-stretch-axis).</summary>
internal enum MathStretchAxis : byte
{
    Block,
    Inline,
}

/// <summary>
/// The properties of an embellished operator (https://w3c.github.io/mathml-core/#dictionary-based-attributes), resolved for the
/// layout: the spacings in CSS px (with the font of the core operator), <c>minsize</c>/<c>maxsize</c> as lengths and
/// percentages of the height of the glyph.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct MathOperatorProperties(MathOperatorForm Form, MathOperatorFlags Flags, double LSpace, double RSpace, LengthPercentage MinSize, LengthPercentage? MaxSize, MathStretchAxis StretchAxis)
{
    public bool IsStretchy => (Flags & MathOperatorFlags.Stretchy) != MathOperatorFlags.None;

    public bool IsSymmetric => (Flags & MathOperatorFlags.Symmetric) != MathOperatorFlags.None;

    public bool IsLargeOp => (Flags & MathOperatorFlags.LargeOp) != MathOperatorFlags.None;

    public bool IsMovableLimits => (Flags & MathOperatorFlags.MovableLimits) != MathOperatorFlags.None;
}

/// <summary>
/// The classification of the MathML boxes for the math layout (https://w3c.github.io/mathml-core/#presentation-markup): the
/// layout algorithm of a box, its in-flow children, the embellished operators and their properties, the space-like elements.
/// Everything is computed from the box tree, the DOM and the computed styles (no layout).
/// </summary>
internal static class MathLayoutBoxes
{
    /// <summary>Returns the math layout algorithm of a box (<see cref="MathBoxKind.None"/> when it is not laid out by the math layout).</summary>
    public static MathBoxKind GetKind(LayoutBox? box)
    {
        if (box is not ContainerBox { Kind: ContainerBoxKind.Normal or ContainerBoxKind.Anonymous } container || container.Display.Inside != DisplayInside.Math || container.Display.Internal != DisplayInternal.None)
            return MathBoxKind.None;

        // https://w3c.github.io/mathml-core/#anonymous-mrow-boxes
        if (container.IsAnonymous)
            return MathBoxKind.Row;

        if (container.PseudoElement is not null || container.Element is not { NamespaceUri: Namespaces.MathML } element)
            return MathBoxKind.None;

        return element.LocalName switch
        {
            "mi" or "mn" or "mtext" or "ms" or "annotation" or "annotation-xml" => MathBoxKind.Token,
            "mo" => MathBoxKind.Operator,
            "mspace" => MathBoxKind.Space,
            "mfrac" => MathBoxKind.Fraction,
            "msqrt" => MathBoxKind.SquareRoot,
            "mroot" => MathBoxKind.Root,
            "mpadded" => MathBoxKind.Padded,
            "msub" => MathBoxKind.Sub,
            "msup" => MathBoxKind.Sup,
            "msubsup" => MathBoxKind.SubSup,
            "munder" => MathBoxKind.Under,
            "mover" => MathBoxKind.Over,
            "munderover" => MathBoxKind.UnderOver,
            "mmultiscripts" => MathBoxKind.Multiscripts,

            // The grouping elements and the unknown MathML elements behave as mrow (https://w3c.github.io/mathml-core/#new-display-math-value)
            _ => MathBoxKind.Row,
        };
    }

    /// <summary>Whether a box is a token element laid out as <c>mtext</c> or <c>mo</c>: its contents are laid out as a block container (see <c>BoxTreeBuilder</c>).</summary>
    public static bool IsTokenElement(ContainerBox box) => GetKind(box) is MathBoxKind.Token or MathBoxKind.Operator;

    /// <summary>
    /// Whether a box generates an anonymous mrow box around its children (https://w3c.github.io/mathml-core/#anonymous-mrow-boxes):
    /// <c>msqrt</c> and <c>mpadded</c> laid out by the math layout, and <c>mtd</c> laid out as a table cell.
    /// </summary>
    public static bool GeneratesAnonymousMrow(ContainerBox box)
    {
        if (box.Kind != ContainerBoxKind.Normal || box.PseudoElement is not null || box.Element is not { NamespaceUri: Namespaces.MathML } element)
            return false;

        return element.LocalName switch
        {
            "msqrt" or "mpadded" => GetKind(box) is MathBoxKind.SquareRoot or MathBoxKind.Padded,
            "mtd" => box.Display.Internal == DisplayInternal.TableCell,
            _ => false,
        };
    }

    /// <summary>Whether a child box is an in-flow child of a math box: the absolutely positioned boxes are out of flow, the text is not rendered.</summary>
    public static bool IsInFlowChild(LayoutBox box) => box is ContainerBox { IsOutOfFlow: false, IsInlineBox: false };

    /// <summary>Returns the number of in-flow children of a box.</summary>
    public static int CountInFlowChildren(ContainerBox box)
    {
        var count = 0;
        foreach (var child in box.Children)
        {
            if (IsInFlowChild(child))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Returns the first in-flow child of a box, or null.</summary>
    public static ContainerBox? GetFirstInFlowChild(ContainerBox box)
    {
        foreach (var child in box.Children)
        {
            if (IsInFlowChild(child))
                return (ContainerBox)child;
        }

        return null;
    }

    private static bool IsGroupingOrPadded(MathBoxKind kind) => kind is MathBoxKind.Row or MathBoxKind.Padded;

    private static bool IsScripted(MathBoxKind kind) => kind is MathBoxKind.Sub or MathBoxKind.Sup or MathBoxKind.SubSup or MathBoxKind.Under or MathBoxKind.Over or MathBoxKind.UnderOver or MathBoxKind.Multiscripts;

    /// <summary>Whether a box is a space-like element (https://w3c.github.io/mathml-core/#dfn-space-like).</summary>
    public static bool IsSpaceLike(LayoutBox box)
    {
        var kind = GetKind(box);
        if (kind == MathBoxKind.Space || (kind == MathBoxKind.Token && box.Element?.LocalName == "mtext"))
            return true;

        if (!IsGroupingOrPadded(kind))
            return false;

        foreach (var child in ((ContainerBox)box).Children)
        {
            if (IsInFlowChild(child) && !IsSpaceLike(child))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the core operator of a box when it is an embellished operator (https://w3c.github.io/mathml-core/#dfn-embellished-operator),
    /// else null.
    /// </summary>
    public static ContainerBox? GetCoreOperator(LayoutBox? box)
    {
        var kind = GetKind(box);
        if (kind == MathBoxKind.None)
            return null;

        var container = (ContainerBox)box!;
        if (kind == MathBoxKind.Operator)
            return container;

        if (IsScripted(kind) || kind == MathBoxKind.Fraction)
            return GetCoreOperator(GetFirstInFlowChild(container));

        if (!IsGroupingOrPadded(kind))
            return null;

        // One embellished operator and zero or more space-like elements
        ContainerBox? core = null;
        foreach (var child in container.Children)
        {
            if (!IsInFlowChild(child))
                continue;

            if (IsSpaceLike(child))
                continue;

            if (core is not null || GetCoreOperator(child) is not { } childCore)
                return null;

            core = childCore;
        }

        return core;
    }

    /// <summary>Returns the text content of a token element, without the white space around it.</summary>
    public static string GetTokenText(ContainerBox box) => (box.Element?.TextContent ?? "").Trim(" \t\n\r\f".ToCharArray());

    /// <summary>
    /// Returns whether a token element "contains only text content" (https://w3c.github.io/mathml-core/#embellished-operators):
    /// it has no element child (the comments and the processing instructions are not content).
    /// </summary>
    public static bool HasOnlyTextContent(ContainerBox box)
    {
        for (var child = box.Element?.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is Element)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the properties of an embellished operator (https://w3c.github.io/mathml-core/#dfn-algorithm-for-determining-the-properties-of-an-embellished-operator).
    /// </summary>
    /// <param name="embellished">The embellished operator.</param>
    /// <param name="core">Its core operator.</param>
    public static MathOperatorProperties GetOperatorProperties(ContainerBox embellished, ContainerBox core)
    {
        var element = core.Element!;
        var style = core.Style;
        var fontSize = style.Layout.FontSize;
        var content = GetTokenText(core);

        // https://w3c.github.io/mathml-core/#dfn-form
        MathOperatorForm form;
        var explicitForm = element.GetAttributeValue("form") is { } formValue ? InfraAscii.ToAsciiLowercase(formValue) switch
        {
            "infix" => MathOperatorForm.Infix,
            "prefix" => MathOperatorForm.Prefix,
            "postfix" => MathOperatorForm.Postfix,
            _ => (MathOperatorForm?)null,
        } : null;
        form = explicitForm ?? GetImplicitForm(embellished);

        // "If the core operator contains only text content Content, then set Category to the result of the algorithm to determine
        // the category of an operator (Content, Form)": an operator with an element child has the default category
        var hasOnlyText = HasOnlyTextContent(core);
        var category = hasOnlyText ? MathOperatorDictionary.GetCategory(content, form) : MathOperatorCategory.Default;
        if (hasOnlyText && category == MathOperatorCategory.Default && explicitForm is null)
        {
            foreach (var other in (ReadOnlySpan<MathOperatorForm>)[MathOperatorForm.Infix, MathOperatorForm.Postfix, MathOperatorForm.Prefix])
            {
                category = MathOperatorDictionary.GetCategory(content, other);
                if (category != MathOperatorCategory.Default)
                    break;
            }
        }

        var flags = MathOperatorDictionary.GetFlags(category);
        flags = ApplyBoolean(element, "stretchy", MathOperatorFlags.Stretchy, flags);
        flags = ApplyBoolean(element, "symmetric", MathOperatorFlags.Symmetric, flags);
        flags = ApplyBoolean(element, "largeop", MathOperatorFlags.LargeOp, flags);
        flags = ApplyBoolean(element, "movablelimits", MathOperatorFlags.MovableLimits, flags);

        // "Percentage values for lspace, rspace are interpreted relative to the value read from the dictionary"; the
        // font-relative lengths use the font of the core operator
        var lspace = MathOperatorDictionary.GetLSpace(category) * fontSize;
        var rspace = MathOperatorDictionary.GetRSpace(category) * fontSize;
        if (TryResolveLength(core, "lspace", out var lspaceValue))
        {
            lspace = lspaceValue.Resolve(lspace);
        }

        if (TryResolveLength(core, "rspace", out var rspaceValue))
        {
            rspace = rspaceValue.Resolve(rspace);
        }

        var minSize = TryResolveLength(core, "minsize", out var min) ? min : LengthPercentage.FromPercentage(100);
        LengthPercentage? maxSize = TryResolveLength(core, "maxsize", out var max) ? max : null;

        // https://w3c.github.io/mathml-core/#dfn-stretch-axis
        var axis = content.Length > 0 && char.IsSurrogatePair(content, 0) ? (content.Length == 2 ? char.ConvertToUtf32(content[0], content[1]) : -1) : (content.Length == 1 ? content[0] : -1);
        var stretchAxis = axis >= 0 && MathOperatorDictionary.HasInlineStretchAxis(axis) ? MathStretchAxis.Inline : MathStretchAxis.Block;
        return new MathOperatorProperties(form, flags, lspace, rspace, minSize, maxSize, stretchAxis);
    }

    /// <summary>The form of an embellished operator without form attribute (https://w3c.github.io/mathml-core/#dfn-form).</summary>
    private static MathOperatorForm GetImplicitForm(ContainerBox embellished)
    {
        var parent = embellished.Parent;
        var parentKind = GetKind(parent);
        if (parent is not null && (IsGroupingOrPadded(parentKind) || parentKind == MathBoxKind.SquareRoot))
        {
            // The first or the last of more than one in-flow children, ignoring the space-like children
            LayoutBox? first = null;
            LayoutBox? last = null;
            var count = 0;
            foreach (var child in parent.Children)
            {
                if (!IsInFlowChild(child) || IsSpaceLike(child))
                    continue;

                first ??= child;
                last = child;
                count++;
            }

            if (count > 1)
            {
                if (ReferenceEquals(first, embellished))
                    return MathOperatorForm.Prefix;

                if (ReferenceEquals(last, embellished))
                    return MathOperatorForm.Postfix;
            }
        }
        else if (parent is not null && IsScripted(parentKind) && !ReferenceEquals(GetFirstInFlowChild(parent), embellished))
        {
            return MathOperatorForm.Postfix;
        }

        return MathOperatorForm.Infix;
    }

    private static MathOperatorFlags ApplyBoolean(Element element, string name, MathOperatorFlags flag, MathOperatorFlags flags)
    {
        if (element.GetAttributeValue(name) is not { } value)
            return flags;

        if (InfraAscii.EqualsIgnoreAsciiCase(value, "true"))
            return flags | flag;

        if (InfraAscii.EqualsIgnoreAsciiCase(value, "false"))
            return flags & ~flag;

        return flags;
    }

    /// <summary>
    /// Resolves an attribute whose value is a &lt;length-percentage&gt; (https://w3c.github.io/mathml-core/#dfn-length-percentage)
    /// with the style of the element of a box (the font-relative lengths): a used length (zoomed) and a percentage.
    /// </summary>
    public static bool TryResolveLength(ContainerBox box, string attribute, out LengthPercentage value)
    {
        value = default;
        // The math functions are not valid in the attributes of MathML (as in the browsers: WPT
        // mathml/presentation-markup/fractions/frac-linethickness-002.html)
        if (box.Element?.GetAttributeValue(attribute) is not { } text || text.Contains('(', StringComparison.Ordinal) || !PresentationalHints.IsValidMathMLLengthPercentage(text))
            return false;

        var computed = CssValueComputer.ComputeGeneric(box.Style, property: null, text, baseUrl: null);
        return LengthPercentage.TryParse(computed, box.Style.Layout.EffectiveZoom, nonNegative: false, out value);
    }
}
