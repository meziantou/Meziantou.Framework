using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The layout constants of the OpenType MATH table of the first available font of a style, in CSS px, with the fallbacks of
/// MathML Core when the font has no MATH table (https://w3c.github.io/mathml-core/#layout-constants-mathconstants).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct MathFontParameters
{
    private readonly OpenTypeMath? _math;
    private readonly double _xHeight;
    private readonly double _ruleThickness;

    private MathFontParameters(MatchedFont font, double emSize, double xHeight)
    {
        Font = font;
        EmSize = emSize;
        Scale = emSize / font.Font.UnitsPerEm;
        _math = font.Font.MathTable is { HasConstants: true } math ? math : null;
        _xHeight = xHeight;

        // "Default rule thickness: post.underlineThickness or Default fallback constant if the constant is not available"
        _ruleThickness = font.Font.UnderlineThickness * Scale;
    }

    /// <summary>The first available font.</summary>
    public MatchedFont Font { get; }

    /// <summary>The used em size of the font (the font size, zoomed).</summary>
    public double EmSize { get; }

    /// <summary>The scale from the font units to CSS px.</summary>
    public double Scale { get; }

    /// <summary>Whether the first available font has an OpenType MATH table with constants.</summary>
    public bool HasMathTable => _math is not null;

    /// <summary>The OpenType MATH table of the first available font, or null.</summary>
    public OpenTypeMath? MathTable => Font.Font.MathTable;

    /// <summary>Returns the parameters of the first available font of a style.</summary>
    public static MathFontParameters Get(ComputedStyle style)
    {
        var font = style.Fonts.PrimaryFont;
        var fontSize = style.Layout.FontSize;
        var metrics = font.GetMetrics(fontSize);
        return new MathFontParameters(font, metrics.EmSize, metrics.XHeight);
    }

    private double Constant(MathConstant constant, double fallback) => _math is { } math ? math.GetConstant(constant) * Scale : fallback;

    public double RuleThickness => _ruleThickness;

    public double ScriptPercentScaleDown => _math is { ScriptPercentScaleDown: > 0 } math ? math.ScriptPercentScaleDown / 100.0 : 0.71;

    public double DisplayOperatorMinHeight => _math is { } math ? math.DisplayOperatorMinHeight * Scale : 0;

    public double AxisHeight => Constant(MathConstant.AxisHeight, _xHeight / 2);

    public double AccentBaseHeight => Constant(MathConstant.AccentBaseHeight, _xHeight);

    public double SubscriptShiftDown => Constant(MathConstant.SubscriptShiftDown, Font.Font.SubscriptYOffset * Scale);

    public double SubscriptTopMax => Constant(MathConstant.SubscriptTopMax, 4 * _xHeight / 5);

    public double SubscriptBaselineDropMin => Constant(MathConstant.SubscriptBaselineDropMin, 0);

    public double SuperscriptShiftUp => Constant(MathConstant.SuperscriptShiftUp, Font.Font.SuperscriptYOffset * Scale);

    public double SuperscriptShiftUpCramped => Constant(MathConstant.SuperscriptShiftUpCramped, 0);

    public double SuperscriptBottomMin => Constant(MathConstant.SuperscriptBottomMin, _xHeight / 4);

    public double SuperscriptBaselineDropMax => Constant(MathConstant.SuperscriptBaselineDropMax, 0);

    public double SubSuperscriptGapMin => Constant(MathConstant.SubSuperscriptGapMin, 4 * _ruleThickness);

    public double SuperscriptBottomMaxWithSubscript => Constant(MathConstant.SuperscriptBottomMaxWithSubscript, 4 * _xHeight / 5);

    public double SpaceAfterScript => Constant(MathConstant.SpaceAfterScript, EmSize / 24);

    public double UpperLimitGapMin => Constant(MathConstant.UpperLimitGapMin, 0);

    public double UpperLimitBaselineRiseMin => Constant(MathConstant.UpperLimitBaselineRiseMin, 0);

    public double LowerLimitGapMin => Constant(MathConstant.LowerLimitGapMin, 0);

    public double LowerLimitBaselineDropMin => Constant(MathConstant.LowerLimitBaselineDropMin, 0);

    public double StackTopShiftUp => Constant(MathConstant.StackTopShiftUp, 0);

    public double StackTopDisplayStyleShiftUp => Constant(MathConstant.StackTopDisplayStyleShiftUp, 0);

    public double StackBottomShiftDown => Constant(MathConstant.StackBottomShiftDown, 0);

    public double StackBottomDisplayStyleShiftDown => Constant(MathConstant.StackBottomDisplayStyleShiftDown, 0);

    public double StackGapMin => Constant(MathConstant.StackGapMin, 3 * _ruleThickness);

    public double StackDisplayStyleGapMin => Constant(MathConstant.StackDisplayStyleGapMin, 7 * _ruleThickness);

    public double StretchStackTopShiftUp => Constant(MathConstant.StretchStackTopShiftUp, 0);

    public double StretchStackBottomShiftDown => Constant(MathConstant.StretchStackBottomShiftDown, 0);

    public double StretchStackGapAboveMin => Constant(MathConstant.StretchStackGapAboveMin, 0);

    public double StretchStackGapBelowMin => Constant(MathConstant.StretchStackGapBelowMin, 0);

    public double FractionNumeratorShiftUp => Constant(MathConstant.FractionNumeratorShiftUp, 0);

    public double FractionNumeratorDisplayStyleShiftUp => Constant(MathConstant.FractionNumeratorDisplayStyleShiftUp, 0);

    public double FractionDenominatorShiftDown => Constant(MathConstant.FractionDenominatorShiftDown, 0);

    public double FractionDenominatorDisplayStyleShiftDown => Constant(MathConstant.FractionDenominatorDisplayStyleShiftDown, 0);

    public double FractionNumeratorGapMin => Constant(MathConstant.FractionNumeratorGapMin, _ruleThickness);

    public double FractionNumDisplayStyleGapMin => Constant(MathConstant.FractionNumDisplayStyleGapMin, 3 * _ruleThickness);

    public double FractionRuleThickness => Constant(MathConstant.FractionRuleThickness, _ruleThickness);

    public double FractionDenominatorGapMin => Constant(MathConstant.FractionDenominatorGapMin, _ruleThickness);

    public double FractionDenomDisplayStyleGapMin => Constant(MathConstant.FractionDenomDisplayStyleGapMin, 3 * _ruleThickness);

    public double OverbarVerticalGap => Constant(MathConstant.OverbarVerticalGap, 3 * _ruleThickness);

    public double OverbarExtraAscender => Constant(MathConstant.OverbarExtraAscender, _ruleThickness);

    public double UnderbarVerticalGap => Constant(MathConstant.UnderbarVerticalGap, 3 * _ruleThickness);

    public double UnderbarExtraDescender => Constant(MathConstant.UnderbarExtraDescender, _ruleThickness);

    public double RadicalVerticalGap => Constant(MathConstant.RadicalVerticalGap, 5 * _ruleThickness / 4);

    public double RadicalDisplayStyleVerticalGap => Constant(MathConstant.RadicalDisplayStyleVerticalGap, _ruleThickness + (_xHeight / 4));

    public double RadicalRuleThickness => Constant(MathConstant.RadicalRuleThickness, _ruleThickness);

    public double RadicalExtraAscender => Constant(MathConstant.RadicalExtraAscender, _ruleThickness);

    public double RadicalKernBeforeDegree => Constant(MathConstant.RadicalKernBeforeDegree, 5 * EmSize / 18);

    public double RadicalKernAfterDegree => Constant(MathConstant.RadicalKernAfterDegree, -10 * EmSize / 18);

    public double RadicalDegreeBottomRaisePercent => _math is { } math ? math.RadicalDegreeBottomRaisePercent / 100.0 : 0.6;
}
