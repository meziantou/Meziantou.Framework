namespace Meziantou.Framework.Fonts;

/// <summary>
/// The constants of mathematical layout: OpenType specification, "MATH - The Mathematical Typesetting Table",
/// "MathConstants table", in the order of the table.
/// </summary>
public enum MathConstant
{
    /// <summary>The percentage of scaling down for the level 1 superscripts and subscripts.</summary>
    ScriptPercentScaleDown,

    /// <summary>The percentage of scaling down for the level 2 (script script) superscripts and subscripts.</summary>
    ScriptScriptPercentScaleDown,

    /// <summary>The minimum height required for a delimited expression to be treated as a sub-formula.</summary>
    DelimitedSubFormulaMinHeight,

    /// <summary>The minimum height of n-ary operators (such as integral and summation) for formulas in display mode.</summary>
    DisplayOperatorMinHeight,

    /// <summary>The white space to be left between math formulas to ensure proper line spacing.</summary>
    MathLeading,

    /// <summary>The axis height of the font: the height of the center of fractions, of the minus sign and of the stretched delimiters.</summary>
    AxisHeight,

    /// <summary>The maximum height of a base that does not require raising its accents.</summary>
    AccentBaseHeight,

    /// <summary>The maximum height of a base that does not require flattening its accents.</summary>
    FlattenedAccentBaseHeight,

    /// <summary>The standard shift down applied to subscript elements.</summary>
    SubscriptShiftDown,

    /// <summary>The maximum allowed height of the top of a subscript that does not require moving it down.</summary>
    SubscriptTopMax,

    /// <summary>The minimum allowed drop of the baseline of a subscript relative to the bottom of the base.</summary>
    SubscriptBaselineDropMin,

    /// <summary>The standard shift up applied to superscript elements.</summary>
    SuperscriptShiftUp,

    /// <summary>The standard shift up applied to superscript elements when the current style is cramped.</summary>
    SuperscriptShiftUpCramped,

    /// <summary>The minimum allowed height of the bottom of a superscript that does not require moving it up.</summary>
    SuperscriptBottomMin,

    /// <summary>The maximum allowed drop of the baseline of a superscript relative to the top of the base.</summary>
    SuperscriptBaselineDropMax,

    /// <summary>The minimum gap between the superscript and the subscript ink.</summary>
    SubSuperscriptGapMin,

    /// <summary>The maximum height of the bottom of a superscript when the subscript is moved down to make room for it.</summary>
    SuperscriptBottomMaxWithSubscript,

    /// <summary>The extra white space added to the right of a subscript or superscript.</summary>
    SpaceAfterScript,

    /// <summary>The minimum gap between the bottom of an upper limit and the top of the base operator.</summary>
    UpperLimitGapMin,

    /// <summary>The minimum distance between the baseline of an upper limit and the top of the base operator.</summary>
    UpperLimitBaselineRiseMin,

    /// <summary>The minimum gap between the top of a lower limit and the bottom of the base operator.</summary>
    LowerLimitGapMin,

    /// <summary>The minimum distance between the baseline of a lower limit and the bottom of the base operator.</summary>
    LowerLimitBaselineDropMin,

    /// <summary>The standard shift up applied to the top element of a stack.</summary>
    StackTopShiftUp,

    /// <summary>The standard shift up applied to the top element of a stack in display style.</summary>
    StackTopDisplayStyleShiftUp,

    /// <summary>The standard shift down applied to the bottom element of a stack.</summary>
    StackBottomShiftDown,

    /// <summary>The standard shift down applied to the bottom element of a stack in display style.</summary>
    StackBottomDisplayStyleShiftDown,

    /// <summary>The minimum gap between the bottom of the top element of a stack and the top of the bottom element.</summary>
    StackGapMin,

    /// <summary>The minimum gap between the bottom of the top element of a stack and the top of the bottom element in display style.</summary>
    StackDisplayStyleGapMin,

    /// <summary>The standard shift up applied to the top element of a stretch stack.</summary>
    StretchStackTopShiftUp,

    /// <summary>The standard shift down applied to the bottom element of a stretch stack.</summary>
    StretchStackBottomShiftDown,

    /// <summary>The minimum gap between the ink of the stretched element and the ink bottom of the element above.</summary>
    StretchStackGapAboveMin,

    /// <summary>The minimum gap between the ink of the stretched element and the ink top of the element below.</summary>
    StretchStackGapBelowMin,

    /// <summary>The standard shift up applied to the numerator.</summary>
    FractionNumeratorShiftUp,

    /// <summary>The standard shift up applied to the numerator in display style.</summary>
    FractionNumeratorDisplayStyleShiftUp,

    /// <summary>The standard shift down applied to the denominator.</summary>
    FractionDenominatorShiftDown,

    /// <summary>The standard shift down applied to the denominator in display style.</summary>
    FractionDenominatorDisplayStyleShiftDown,

    /// <summary>The minimum tolerated gap between the ink bottom of the numerator and the ink of the fraction bar.</summary>
    FractionNumeratorGapMin,

    /// <summary>The minimum tolerated gap between the ink bottom of the numerator and the ink of the fraction bar in display style.</summary>
    FractionNumDisplayStyleGapMin,

    /// <summary>The thickness of the fraction bar.</summary>
    FractionRuleThickness,

    /// <summary>The minimum tolerated gap between the ink top of the denominator and the ink of the fraction bar.</summary>
    FractionDenominatorGapMin,

    /// <summary>The minimum tolerated gap between the ink top of the denominator and the ink of the fraction bar in display style.</summary>
    FractionDenomDisplayStyleGapMin,

    /// <summary>The horizontal distance between the top and bottom elements of a skewed fraction.</summary>
    SkewedFractionHorizontalGap,

    /// <summary>The vertical distance between the ink of the top and bottom elements of a skewed fraction.</summary>
    SkewedFractionVerticalGap,

    /// <summary>The distance between the overbar and the ink top of the base.</summary>
    OverbarVerticalGap,

    /// <summary>The thickness of the overbar.</summary>
    OverbarRuleThickness,

    /// <summary>The extra white space reserved above the overbar.</summary>
    OverbarExtraAscender,

    /// <summary>The distance between the underbar and the ink bottom of the base.</summary>
    UnderbarVerticalGap,

    /// <summary>The thickness of the underbar.</summary>
    UnderbarRuleThickness,

    /// <summary>The extra white space reserved below the underbar.</summary>
    UnderbarExtraDescender,

    /// <summary>The space between the ink top of the radicand and the radical bar.</summary>
    RadicalVerticalGap,

    /// <summary>The space between the ink top of the radicand and the radical bar in display style.</summary>
    RadicalDisplayStyleVerticalGap,

    /// <summary>The thickness of the radical bar.</summary>
    RadicalRuleThickness,

    /// <summary>The extra white space reserved above the radical bar.</summary>
    RadicalExtraAscender,

    /// <summary>The extra horizontal kern before the degree of a radical.</summary>
    RadicalKernBeforeDegree,

    /// <summary>The negative kern after the degree of a radical.</summary>
    RadicalKernAfterDegree,

    /// <summary>The height of the bottom of the degree of a radical, as a percentage of the height of the radical sign.</summary>
    RadicalDegreeBottomRaisePercent,
}
