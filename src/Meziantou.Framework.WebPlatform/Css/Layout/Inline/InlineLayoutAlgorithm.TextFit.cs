using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The text fitting (https://drafts.csswg.org/css-text-5/#text-fit): the inline-level contents of the lines of a block
// container are scaled to fit the line boxes. "Text fitting is applied after wrapping": the lines are broken with the
// computed font sizes, then each line is built with the used font sizes of its boxes multiplied by its line scaling
// factor (the styles of the line are the styles of the boxes with ComputedStyle.UsedFontScale, as the styles of the first
// formatted line are the styles of the boxes inheriting from ::first-line).
internal sealed partial class InlineLayoutAlgorithm
{
    /// <summary>The smallest line scaling factor: the content that cannot fit (its parts that are not scaled are wider than the line box) is not scaled down to nothing.</summary>
    private const double MinimumFontScale = 1d / 64;

    /// <summary>The <c>text-fit</c> of the block container, or none when its lines are not scaled (see <see cref="PrepareTextFit"/>).</summary>
    private TextFit _textFit;

    /// <summary>The line scaling factor of the line being built (<see cref="BuildLine"/>), 1 for the line breaking and the lines that are not scaled.</summary>
    private double _fontScale = 1;

    /// <summary>The line scaling factor of all the lines with <c>text-fit: consistent</c>.</summary>
    private double _consistentFontScale = 1;

    private readonly Dictionary<(ComputedStyle Style, double Scale), ComputedStyle> _scaledStyles = [];
    private readonly Dictionary<(int Item, bool IsFirstLine, double Scale), ShapedText> _scaledShaped = [];

    /// <summary>
    /// Returns a style of a line with the used font size of the line being built: "the used font-size is its computed value
    /// multiplied by the line scaling factor". The computed values are the ones of the style: the font-relative lengths (e.g. a
    /// padding or a letter-spacing in em) are not scaled, the values that refer to the used font size are (a line-height that is
    /// normal or a number, a letter-spacing that is a percentage).
    /// </summary>
    private ComputedStyle GetScaledStyle(ComputedStyle style)
    {
        if (_fontScale == 1)
            return style;

        if (!_scaledStyles.TryGetValue((style, _fontScale), out var scaled))
        {
            scaled = style.WithUsedFontScale(_fontScale);
            _scaledStyles.Add((style, _fontScale), scaled);
        }

        return scaled;
    }

    /// <summary>
    /// Returns the glyphs of a text item on the line being built, shaped at the used font size of the line ("Font/glyph selection
    /// and positioning may be redone"; the item is shaped alone, not with the items it is shaped with at its computed font size).
    /// </summary>
    private ShapedText GetScaledShaped(int index, bool isFirstLine)
    {
        if (!_scaledShaped.TryGetValue((index, isFirstLine, _fontScale), out var shaped))
        {
            shaped = Shape(index, GetLineStyle(Items[index], isFirstLine));
            _scaledShaped.Add((index, isFirstLine, _fontScale), shaped);
        }

        return shaped;
    }

    /// <summary>
    /// Decides whether the lines of the block container are scaled by its <c>text-fit</c>, and computes the scaling factor of
    /// <c>consistent</c>: "all line boxes are scaled by the smallest computed line scaling factor" (of the lines that have
    /// scalable parts; the last line and the lines that end with a forced break included, as in Blink).
    /// </summary>
    /// <remarks>
    /// "If a feature such as float or initial-letter would make the line's inline size depend on its block position, scaling
    /// is disabled for the block": the lines are not scaled in a block container that has floats or is beside floats. They are
    /// not scaled either with ruby annotations (not implemented), nor when the intrinsic sizes are computed (the scaling does
    /// not change the inline sizes that the content asks for).
    /// </remarks>
    private void PrepareTextFit(in ContentArea content)
    {
        _textFit = default;
        _consistentFontScale = 1;
        _fontScale = 1;
        var fit = _block.Style.Layout.Text.TextFit;
        var size = content.InlineSize;
        if (fit.Mode == TextFitMode.None || !double.IsFinite(size) || size <= 0 || HasRuby || !_blockFormattingContext!.Exclusions.IsEmpty)
            return;

        foreach (ref readonly var item in Items)
        {
            if (item.IsFloat)
                return;
        }

        _textFit = fit;
        if (fit.Target != TextFitTarget.Consistent)
            return;

        var ends = new List<LineEnd>();
        if (BreakAllLines(content, size, ends, out _) == int.MaxValue)
        {
            _textFit = default;
            return;
        }

        var smallest = double.PositiveInfinity;
        var position = new LinePosition(0, 0);
        var afterForcedBreak = false;
        for (var i = 0; i < ends.Count; i++)
        {
            var end = ends[i];
            var isFirstLine = i == 0;
            var indent = GetTextIndent(content, isFirstLine, afterForcedBreak);
            _lineStartOffset = indent;
            _lineAfterForcedBreak = afterForcedBreak;
            if (MeasureLineFit(position, end, size - indent, isFirstLine) is { } factor)
            {
                smallest = Math.Min(smallest, factor);
            }

            position = new LinePosition(end.Item, end.Position);
            afterForcedBreak = end.IsForced;
        }

        _consistentFontScale = double.IsFinite(smallest) ? fit.Clamp(smallest) : 1;
    }

    /// <summary>
    /// Returns the line scaling factor of a line: the factor of all the lines with <c>consistent</c>, else the factor of the
    /// line ("the last line of the block and lines that end in a forced break are not scaled" with <c>per-line</c>), limited by
    /// the percentage of <c>text-fit</c>.
    /// </summary>
    private double GetLineFontScale(LinePosition start, in LineEnd end, double availableSize, bool isFirstLine)
    {
        if (_textFit.Mode == TextFitMode.None)
            return 1;

        if (_textFit.Target == TextFitTarget.Consistent)
            return _consistentFontScale;

        if (_textFit.Target == TextFitTarget.PerLine && (end.IsForced || end.Item >= _items.Count))
            return 1;

        return MeasureLineFit(start, end, availableSize, isFirstLine) is { } factor ? _textFit.Clamp(factor) : 1;
    }

    /// <summary>
    /// Returns the factor that makes the scalable parts of a line fill the line box, or null when the line has no scalable
    /// part: "(A + B) / A, where A is the total inline size of the scalable parts and B is the remaining space" (without the
    /// white space at the end of the line). The scalable parts are the ones whose size follows the used font size (the text and
    /// the spacing that is a percentage; not the atomic inlines, the edges of the inline boxes, the spacing that is a length):
    /// the content of the line is measured with two font sizes, then with the factor, which is reduced when the content then
    /// exceeds the line box by a floating-point excess.
    /// </summary>
    private double? MeasureLineFit(LinePosition start, in LineEnd end, double availableSize, bool isFirstLine)
    {
        const double ProbeScale = 2;
        var size = CollectLineContent(start, end, availableSize, isFirstLine, fontScale: 1).Width;
        var scaledSize = CollectLineContent(start, end, availableSize, isFirstLine, ProbeScale).Width;
        _fontScale = 1;
        ClearLineSpacing();
        var scalable = (scaledSize - size) / (ProbeScale - 1);
        if (scalable <= LayoutEpsilon)
            return null;

        var factor = Math.Max(MinimumFontScale, (availableSize - (size - scalable)) / scalable);

        // The content is shaped again at the used font sizes: the sum of the scaled advances can exceed the line box by a
        // floating-point excess (e.g. 400.00000000000006px in a line box of 400px), which the factor must not let overflow
        var fittedSize = CollectLineContent(start, end, availableSize, isFirstLine, factor).Width;
        _fontScale = 1;
        ClearLineSpacing();
        if (fittedSize > availableSize && fittedSize - availableSize < LayoutEpsilon)
        {
            factor = Math.Max(MinimumFontScale, factor - (fittedSize - availableSize + LayoutEpsilon) / scalable);
        }

        return factor;
    }
}
