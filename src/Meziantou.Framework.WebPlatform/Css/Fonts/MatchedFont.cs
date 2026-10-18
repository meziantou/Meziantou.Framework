using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The metrics of a <see cref="MatchedFont"/> at a font size, in CSS px (https://drafts.csswg.org/css-inline-3/#ascent-descent).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct UsedFontMetrics
{
    /// <summary>The size of the em of the font: the font size after <c>size-adjust</c> and <c>font-size-adjust</c>.</summary>
    public double EmSize { get; init; }

    /// <summary>The number of CSS px per font unit (<see cref="EmSize"/> divided by the units per em of the font).</summary>
    public double Scale { get; init; }

    /// <summary>The ascent (the <c>ascent-override</c> of the font face, else the ascent of the font).</summary>
    public double Ascent { get; init; }

    /// <summary>The descent, positive below the baseline (the <c>descent-override</c> of the font face, else the descent of the font).</summary>
    public double Descent { get; init; }

    /// <summary>The line gap (the <c>line-gap-override</c> of the font face, else the line gap of the font).</summary>
    public double LineGap { get; init; }

    /// <summary>The x-height (https://drafts.csswg.org/css-values-4/#ex): 0.5em when the font does not define it.</summary>
    public double XHeight { get; init; }

    /// <summary>The cap height (https://drafts.csswg.org/css-values-4/#cap): the ascent when the font does not define it.</summary>
    public double CapHeight { get; init; }

    /// <summary>The metrics of the font in font units (multiply by <see cref="Scale"/>), e.g. for the decorations.</summary>
    public FontMetrics FontUnitMetrics { get; init; }

    /// <summary>The used value of <c>line-height: normal</c> (https://drafts.csswg.org/css-inline-3/#valdef-line-height-normal): the sum of the ascent, the descent and the line gap.</summary>
    public double NormalLineHeight => Ascent + Descent + LineGap;
}

/// <summary>
/// A font face selected by the font matching for a <see cref="FontDescription"/> (https://drafts.csswg.org/css-fonts-4/#font-matching-algorithm):
/// a face of a font file (a bundled font or a loaded web font), with the values of its variation axes, the synthesized styles
/// and the metric overrides of its <c>@font-face</c> rule. It does not depend on the font size: the metrics and the advances
/// are scaled by the users.
/// </summary>
internal sealed class MatchedFont
{
    /// <summary>The ideographic character whose advance defines the <c>ic</c> unit (https://drafts.csswg.org/css-values-4/#ic).</summary>
    internal const int WaterIdeograph = 0x6C34;

    private readonly FontVariationSetting[] _settings;
    private readonly int _opticalSizeAxis = -1;
    private readonly FontVariationInstance? _instance;
    private (double EmSize, FontVariationInstance? Instance) _opticalSizeInstance = (double.NaN, null);
    private FontMetrics? _defaultMetrics;

    /// <param name="font">The face of the font file.</param>
    /// <param name="face">The font face of the @font-face rule or of the font face set, or null for a bundled font.</param>
    /// <param name="description">The font properties the face was selected for.</param>
    /// <param name="selected">The values selected by the font matching (applied to the variation axes).</param>
    /// <param name="descriptors">The descriptors of the font face (the metric overrides, size-adjust and font-variation-settings).</param>
    /// <param name="capabilities">The styles the face supports (the ranges of its descriptors, or of the variation axes of its font).</param>
    internal MatchedFont(OpenTypeFont font, Om.FontFace? face, FontDescription description, FontSelectedValues selected, FontFaceDescriptorValues descriptors, FontSelectionCapabilities capabilities)
    {
        Font = font;
        Face = face;
        SizeAdjust = descriptors.SizeAdjust;
        AscentOverride = descriptors.AscentOverride;
        DescentOverride = descriptors.DescentOverride;
        LineGapOverride = descriptors.LineGapOverride;
        FaceFeatures = descriptors.FeatureSettings;
        FontSizeAdjust = description.SizeAdjust;

        // https://drafts.csswg.org/css-fonts-4/#font-synthesis-weight: a bold face is synthesized when the desired weight is
        // bold (600 or more) and the selected weight is not. As in Chrome and Safari, nothing is synthesized outside of the
        // range of weights or of oblique angles of a face that supports a range (a variable font, unless the descriptors of
        // its font face give a single value): the styles of the face are the ones of its designer
        // (https://github.com/w3c/csswg-drafts/issues/7999)
        SyntheticBold = description.SynthesizeWeight && description.Weight >= 600 && selected.Weight < 600 && capabilities.Weight.Min == capabilities.Weight.Max;

        // https://drafts.csswg.org/css-fonts-4/#font-synthesis-style: an oblique face is synthesized when the desired style is
        // italic or oblique and the selected face is upright (geometric shearing to the oblique angle, 14deg for italic)
        var hasObliqueRange = capabilities.Oblique is { } angles && angles.Min != angles.Max;
        if (description.Style.Kind != FontSlopeKind.Normal && selected.Slope.IsUpright && !hasObliqueRange
            && (description.SynthesizeStyle == FontStyleSynthesis.Auto || (description.SynthesizeStyle == FontStyleSynthesis.ObliqueOnly && description.Style.Kind == FontSlopeKind.Oblique)))
        {
            SyntheticObliqueAngle = description.Style.Kind == FontSlopeKind.Italic ? FontSlope.DefaultObliqueAngle : description.Style.Angle;
        }

        // https://drafts.csswg.org/css-fonts-4/#font-feature-variation-resolution: the values selected by the font matching
        // (wght, wdth, slnt, ital), then font-optical-sizing (opsz), then the font-variation-settings descriptor of the font
        // face, then the font-variation-settings property
        var variations = font.Variations;
        if (variations.IsVariable)
        {
            var settings = new List<FontVariationSetting>
            {
                new(OpenTypeTag.WeightAxis, selected.Weight),
                new(OpenTypeTag.WidthAxis, selected.Width),
            };

            if (selected.Slope.IsItalic)
            {
                settings.Add(new(OpenTypeTag.ItalicAxis, selected.Slope.Value));
            }
            else
            {
                settings.Add(new(OpenTypeTag.SlantAxis, -selected.Slope.Value));
                if (variations.GetAxis(OpenTypeTag.ItalicAxis) is not null)
                {
                    settings.Add(new(OpenTypeTag.ItalicAxis, 0));
                }
            }

            if (description.OpticalSizing && variations.GetAxis(OpenTypeTag.OpticalSizeAxis) is not null)
            {
                for (var i = 0; i < variations.Axes.Count; i++)
                {
                    if (variations.Axes[i].Tag == OpenTypeTag.OpticalSizeAxis)
                    {
                        _opticalSizeAxis = settings.Count;
                        settings.Add(new(OpenTypeTag.OpticalSizeAxis, double.NaN));
                        break;
                    }
                }
            }

            settings.AddRange(descriptors.VariationSettings);
            settings.AddRange(FontDescription.ParseVariationSettings(description.VariationSettings));
            _settings = [.. settings];
            if (_opticalSizeAxis < 0)
            {
                _instance = variations.CreateInstance(_settings);
                if (_instance.IsDefault)
                {
                    _instance = null;
                }
            }
        }
        else
        {
            _settings = [];
        }
    }

    /// <summary>The face of the font file.</summary>
    public OpenTypeFont Font { get; }

    /// <summary>The font face of the @font-face rule or of the font face set, or null for a bundled font.</summary>
    public Om.FontFace? Face { get; }

    /// <summary>Whether a bold face is synthesized (https://drafts.csswg.org/css-fonts-4/#font-synthesis-weight). The advances are not changed, as in the browsers.</summary>
    public bool SyntheticBold { get; }

    /// <summary>The angle of the synthesized oblique face in degrees, or 0 (https://drafts.csswg.org/css-fonts-4/#font-synthesis-style). The metrics are not changed.</summary>
    public double SyntheticObliqueAngle { get; }

    /// <summary>The <c>size-adjust</c> descriptor of the font face (https://drafts.csswg.org/css-fonts-5/#size-adjust-desc), 1 by default.</summary>
    public double SizeAdjust { get; }

    /// <summary>The <c>ascent-override</c> descriptor, as a fraction of the em size, or null (https://drafts.csswg.org/css-fonts-4/#descdef-font-face-ascent-override).</summary>
    public double? AscentOverride { get; }

    /// <summary>The <c>descent-override</c> descriptor, as a fraction of the em size, or null.</summary>
    public double? DescentOverride { get; }

    /// <summary>The <c>line-gap-override</c> descriptor, as a fraction of the em size, or null.</summary>
    public double? LineGapOverride { get; }

    /// <summary>The features of the <c>font-feature-settings</c> descriptor of the font face (https://drafts.csswg.org/css-fonts-4/#font-rend-desc), or null.</summary>
    public FontFeatureList? FaceFeatures { get; }

    /// <summary>The <c>font-size-adjust</c> of the font properties (https://drafts.csswg.org/css-fonts-5/#font-size-adjust-prop).</summary>
    public FontSizeAdjust FontSizeAdjust { get; }

    /// <summary>
    /// Whether the font is an installed font: a bundled font (see <see cref="BundledFonts"/>) or a font installed by the host
    /// (<see cref="InstalledFontCollection"/>), not the font of a font face.
    /// </summary>
    public bool IsBundled => Face is null;

    /// <summary>
    /// The size of the em of the font for a computed font size: the font size multiplied by <c>size-adjust</c>, or, when
    /// <c>font-size-adjust</c> is set, the size that makes the aspect value of the font equal to its value.
    /// </summary>
    public double GetEmSize(double fontSize)
    {
        if (FontSizeAdjust.IsNone)
            return fontSize * SizeAdjust;

        var aspect = GetAspectValue(FontSizeAdjust.Metric, fontSize * SizeAdjust);
        return aspect > 0 ? fontSize * FontSizeAdjust.Value / aspect : fontSize * SizeAdjust;
    }

    /// <summary>
    /// The instance of the variable font for an em size (the em size sets the <c>opsz</c> axis when <c>font-optical-sizing</c>
    /// is auto), or null for the default instance and the fonts that are not variable.
    /// </summary>
    public FontVariationInstance? GetVariationInstance(double emSize)
    {
        if (_opticalSizeAxis < 0)
            return _instance;

        if (_opticalSizeInstance.EmSize != emSize)
        {
            // https://drafts.csswg.org/css-fonts-4/#font-optical-sizing-def: the opsz axis is set to the used font size in CSS px
            var settings = (FontVariationSetting[])_settings.Clone();
            settings[_opticalSizeAxis] = new(OpenTypeTag.OpticalSizeAxis, emSize);
            var instance = Font.Variations.CreateInstance(settings);
            _opticalSizeInstance = (emSize, instance.IsDefault ? null : instance);
        }

        return _opticalSizeInstance.Instance;
    }

    /// <summary>
    /// Returns the values of the variation axes of the font in user coordinates for an em size (the em size sets the <c>opsz</c>
    /// axis when <c>font-optical-sizing</c> is auto), in order of precedence (a later value of an axis wins), for the painting;
    /// empty for the fonts that are not variable.
    /// </summary>
    public FontVariationSetting[] GetVariationSettings(double emSize)
    {
        if (_opticalSizeAxis < 0)
            return _settings;

        var settings = (FontVariationSetting[])_settings.Clone();
        settings[_opticalSizeAxis] = new(OpenTypeTag.OpticalSizeAxis, emSize);
        return settings;
    }

    /// <summary>Returns the metrics of the font for a computed font size, in CSS px.</summary>
    public UsedFontMetrics GetMetrics(double fontSize) => GetMetricsForEmSize(GetEmSize(fontSize));

    /// <summary>
    /// Returns the metrics of the font for an em size (<see cref="GetEmSize"/> of a font size, e.g. <see cref="ShapedFontRun.EmSize"/>:
    /// <c>size-adjust</c> and <c>font-size-adjust</c> are already applied), in CSS px.
    /// </summary>
    public UsedFontMetrics GetMetricsForEmSize(double emSize)
    {
        var instance = GetVariationInstance(emSize);
        var metrics = GetFontUnitMetrics(instance);
        var scale = emSize / Font.UnitsPerEm;
        var ascent = AscentOverride is { } ascentOverride ? ascentOverride * emSize : metrics.Ascent * scale;
        var descent = DescentOverride is { } descentOverride ? descentOverride * emSize : metrics.Descent * scale;
        var lineGap = LineGapOverride is { } lineGapOverride ? lineGapOverride * emSize : metrics.LineGap * scale;

        // As in the browsers (Blink rounds the ascent, the descent and the line gap of the fonts to integer pixels, except for
        // the tiny fonts), the line boxes of the normal line height and the baselines are on whole pixels when the font size is
        // an integer: the text is laid out and painted on the device pixels (the line-height: normal of a 16px Times-like font
        // is 18px instead of 18.4px)
        var capHeight = metrics.CapHeight is { } capHeightUnits ? capHeightUnits * scale : ascent;
        if (ascent >= 3 && ascent + descent >= 2)
        {
            ascent = Math.Round(ascent, MidpointRounding.AwayFromZero);
            descent = Math.Round(descent, MidpointRounding.AwayFromZero);
            lineGap = Math.Round(lineGap, MidpointRounding.AwayFromZero);
        }

        return new UsedFontMetrics
        {
            EmSize = emSize,
            Scale = scale,
            Ascent = ascent,
            Descent = descent,
            LineGap = lineGap,
            XHeight = metrics.XHeight is { } xHeight ? xHeight * scale : emSize / 2,
            CapHeight = capHeight,
            FontUnitMetrics = metrics,
        };
    }

    /// <summary>
    /// Returns the baselines of the font (https://drafts.csswg.org/css-inline-3/#baseline-types) for the metrics of a font size:
    /// the ones of the <c>BASE</c> table of the font, else the synthesized ones
    /// (https://drafts.csswg.org/css-inline-3/#baseline-synthesis-fonts): the alphabetic baseline is the origin of the glyphs;
    /// "the ideographic-over and ideographic-under baselines are typically 1em apart" (one gives the other), and "in CJK fonts
    /// the ascent and descent typically match the ideographic-over and ideographic-under baselines, so can be used as a fallback
    /// when both are missing"; the ideographic character face is the ideographic em box; the hanging baseline is at 80% of the
    /// ascent and the math baseline at half the ascent (as in Blink).
    /// </summary>
    public UsedFontBaselines GetBaselines(in UsedFontMetrics metrics)
    {
        var baselines = Font.GetBaselines();
        var scale = metrics.Scale;
        var alphabetic = baselines.Alphabetic * scale ?? 0;
        double ideographicUnder, ideographicOver;
        if (baselines.Ideographic is { } under)
        {
            ideographicUnder = under * scale;
            ideographicOver = baselines.IdeographicOver is { } over ? over * scale : ideographicUnder + metrics.EmSize;
        }
        else if (baselines.IdeographicOver is { } over)
        {
            ideographicOver = over * scale;
            ideographicUnder = ideographicOver - metrics.EmSize;
        }
        else
        {
            ideographicUnder = -metrics.Descent;
            ideographicOver = metrics.Ascent;
        }

        return new UsedFontBaselines
        {
            Alphabetic = alphabetic,
            IdeographicUnder = ideographicUnder,
            IdeographicOver = ideographicOver,
            IdeographicInkUnder = baselines.IdeographicInkUnder * scale ?? ideographicUnder,
            IdeographicInkOver = baselines.IdeographicInkOver * scale ?? ideographicOver,
            Hanging = baselines.Hanging * scale ?? metrics.Ascent * 0.8,
            Mathematical = baselines.Mathematical * scale ?? metrics.Ascent / 2,
            XMiddle = (alphabetic + metrics.XHeight) / 2,
            HasIdeographic = baselines.Ideographic is not null || baselines.IdeographicOver is not null,
        };
    }

    /// <summary>The glyph of a character, 0 when the font does not map it.</summary>
    public ushort GetGlyph(int codePoint) => Font.GetGlyphId(codePoint);

    /// <summary>Whether the font maps a character to a glyph (https://drafts.csswg.org/css-fonts-4/#font-style-matching: the font supports the character).</summary>
    public bool HasGlyph(int codePoint) => Font.HasGlyph(codePoint);

    /// <summary>The advance of a glyph in CSS px, horizontal or vertical (synthetic bold does not change the advances).</summary>
    public double GetAdvance(ushort glyph, double emSize, bool vertical)
    {
        var advance = vertical ? Font.GetAdvanceHeight(glyph) : Font.GetAdvanceWidth(glyph, GetVariationInstance(emSize));
        return advance * emSize / Font.UnitsPerEm;
    }

    /// <summary>
    /// The aspect value of the font for a metric of <c>font-size-adjust</c> (https://drafts.csswg.org/css-fonts-5/#aspect-value):
    /// the metric divided by the em size. The metrics the font does not have are 0.5em (x-height, width of 0), the ascent
    /// (cap height) and 1em (ideographic advances), as for the font-relative units.
    /// </summary>
    public double GetAspectValue(FontSizeAdjustMetric metric, double emSize)
    {
        var instance = GetVariationInstance(emSize);
        var unitsPerEm = (double)Font.UnitsPerEm;
        switch (metric)
        {
            case FontSizeAdjustMetric.CapHeight:
                var metrics = GetFontUnitMetrics(instance);
                return (metrics.CapHeight ?? metrics.Ascent) / unitsPerEm;
            case FontSizeAdjustMetric.ChWidth:
                return GetGlyph('0') is var zero and not 0 ? Font.GetAdvanceWidth(zero, instance) / unitsPerEm : 0.5;
            case FontSizeAdjustMetric.IcWidth:
                return GetGlyph(WaterIdeograph) is var water and not 0 ? Font.GetAdvanceWidth(water, instance) / unitsPerEm : 1;
            case FontSizeAdjustMetric.IcHeight:
                return GetGlyph(WaterIdeograph) is var waterVertical and not 0 ? Font.GetAdvanceHeight(waterVertical) / unitsPerEm : 1;
            default:
                return GetFontUnitMetrics(instance).XHeight is { } xHeight ? xHeight / unitsPerEm : 0.5;
        }
    }

    private FontMetrics GetFontUnitMetrics(FontVariationInstance? instance)
    {
        if (instance is not null)
            return Font.GetMetrics(instance);

        return _defaultMetrics ??= Font.GetMetrics(instance: null);
    }

    /// <inheritdoc />
    public override string ToString() => Font.FullName;
}

/// <summary>The descriptors of a font face that change the metrics and the variations of its font (https://drafts.csswg.org/css-fonts-5/#font-metrics-override-desc).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontFaceDescriptorValues(double SizeAdjust, double? AscentOverride, double? DescentOverride, double? LineGapOverride, FontVariationSetting[] VariationSettings, FontFeatureList? FeatureSettings = null)
{
    /// <summary>The descriptors of a bundled font (no override).</summary>
    public static FontFaceDescriptorValues Default { get; } = new(1, null, null, null, []);
}
