using System.Text;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Unicode;
using GlyphBuffer = Meziantou.Framework.Fonts.GlyphBuffer;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The font synthesis of <c>font-variant-caps</c> and <c>font-variant-position</c>
/// (https://drafts.csswg.org/css-fonts-4/#font-synthesis-small-caps, https://drafts.csswg.org/css-fonts-4/#font-synthesis-position):
/// the small capitals that the font does not have are scaled-down capitals, and the subscripts and superscripts are reduced
/// glyphs on a shifted baseline. The code points are classified after the font fallback (<see cref="ResolveSynthesis"/>), and
/// the runs of the same synthesis are shaped together with their case mapping, their size and their features.
/// </summary>
internal static partial class TextShaper
{
    /// <summary>
    /// The size of the synthesized small capitals relative to the font size: the value of Chrome
    /// (<c>kSmallCapsFontSizeMultiplier</c> of Blink).
    /// </summary>
    internal const double SmallCapsScale = 0.7;

    private static readonly uint SmallCapsTag = OpenTypeTag.FromString("smcp");
    private static readonly uint CapitalsToSmallCapsTag = OpenTypeTag.FromString("c2sc");
    private static readonly uint PetiteCapsTag = OpenTypeTag.FromString("pcap");
    private static readonly uint CapitalsToPetiteCapsTag = OpenTypeTag.FromString("c2pc");
    private static readonly uint UnicaseTag = OpenTypeTag.FromString("unic");
    private static readonly uint SuperscriptsTag = OpenTypeTag.FromString("sups");
    private static readonly uint SubscriptsTag = OpenTypeTag.FromString("subs");

    /// <summary>The synthesis of a code point (<see cref="CodePointInfo.Synthesis"/>): bits that are compared to split the runs.</summary>
    [Flags]
    private enum Synthesis : byte
    {
        None = 0,

        /// <summary>The uppercase mapping of the character is shaped (a synthesized small capital of a lowercase letter).</summary>
        Uppercase = 1,

        /// <summary>The lowercase mapping of the character is shaped (<c>unicase</c> with the <c>smcp</c> feature of the font).</summary>
        Lowercase = 2,

        /// <summary>The glyphs are scaled down to <see cref="SmallCapsScale"/>: a synthesized small capital.</summary>
        SmallCaps = 4,

        /// <summary>A synthesized subscript or superscript: the glyphs are reduced and the baseline is shifted, without the features of <c>font-variant-position</c>.</summary>
        Position = 8,

        /// <summary>The features of <c>font-variant-caps</c> are replaced by the <see cref="FontCapsFeatures"/> in the bits 4 and 5.</summary>
        CapsFeaturesMask = 0x30,
    }

    private const int CapsFeaturesShift = 4;

    private static Synthesis WithCapsFeatures(Synthesis synthesis, FontCapsFeatures features) => synthesis | (Synthesis)((int)features << CapsFeaturesShift);

    private static FontCapsFeatures GetCapsFeatures(Synthesis synthesis) => (FontCapsFeatures)((int)(synthesis & Synthesis.CapsFeaturesMask) >> CapsFeaturesShift);

    /// <summary>A lowercase letter shaped as a synthesized small capital: its uppercase mapping, scaled down, without the features of <c>font-variant-caps</c>.</summary>
    private static readonly Synthesis SynthesizedLowercase = WithCapsFeatures(Synthesis.Uppercase | Synthesis.SmallCaps, FontCapsFeatures.None);

    /// <summary>An uppercase letter shaped as a synthesized small capital: scaled down, without the features of <c>font-variant-caps</c>.</summary>
    private static readonly Synthesis SynthesizedUppercase = WithCapsFeatures(Synthesis.SmallCaps, FontCapsFeatures.None);

    /// <summary>Whether a text needs the classification of <see cref="ResolveSynthesis"/>.</summary>
    private static bool NeedsSynthesis(in TextShapingOptions options)
        => options.Caps is not (FontCapsVariant.Normal or FontCapsVariant.TitlingCaps) || (options.Position != FontPositionVariant.Normal && options.SynthesizePosition);

    /// <summary>
    /// Classifies the code points of a text whose small capitals or subscripts and superscripts may be synthesized: sets
    /// <see cref="CodePointInfo.Synthesis"/> and the number of code points added by the case mapping (<see cref="CodePointInfo.Expansion"/>).
    /// The marks are shaped with their base.
    /// </summary>
    private static void ResolveSynthesis(Span<CodePointInfo> codePoints, in TextShapingOptions options)
    {
        if (options.Caps is not (FontCapsVariant.Normal or FontCapsVariant.TitlingCaps))
        {
            var turkic = TextTransformer.UsesTurkicCasing(options.Language);
            Span<char> mapped = stackalloc char[CaseMapping.MaxLength];
            for (var i = 0; i < codePoints.Length; i++)
            {
                ref var info = ref codePoints[i];
                if (info.Cluster != info.Index)
                {
                    info.Synthesis = codePoints[i - 1].Synthesis;
                    continue;
                }

                info.Synthesis = GetCapsSynthesis(info.CodePoint, info.Font, info.Script, options);
                if ((info.Synthesis & (Synthesis.Uppercase | Synthesis.Lowercase)) != Synthesis.None)
                {
                    var length = MapCase(info.CodePoint, (info.Synthesis & Synthesis.Uppercase) != Synthesis.None, turkic, mapped);
                    info.Expansion = (byte)(CountCodePoints(mapped[..length]) - 1);
                }
            }
        }

        // https://drafts.csswg.org/css-fonts-4/#font-variant-position-prop: "if a variant glyph is not available for all the
        // characters in the run, simulated glyphs should be synthesized for all characters": the runs of a font are checked
        if (options.Position != FontPositionVariant.Normal && options.SynthesizePosition)
        {
            var feature = options.Position == FontPositionVariant.Super ? SuperscriptsTag : SubscriptsTag;
            var start = 0;
            while (start < codePoints.Length)
            {
                var font = codePoints[start].Font;
                var end = start + 1;
                while (end < codePoints.Length && ReferenceEquals(codePoints[end].Font, font))
                {
                    end++;
                }

                var supported = true;
                for (var i = start; i < end && supported; i++)
                {
                    var codePoint = codePoints[i].CodePoint;
                    if (codePoints[i].Cluster != codePoints[i].Index || IsLineSeparator(codePoint) || UnicodeProperties.IsDefaultIgnorable(codePoint))
                        continue;

                    supported = font.Font.WouldSubstitute(codePoints[i].Script, feature, font.GetGlyph(codePoint));
                }

                if (!supported)
                {
                    for (var i = start; i < end; i++)
                    {
                        codePoints[i].Synthesis |= Synthesis.Position;
                    }
                }

                start = end;
            }
        }
    }

    /// <summary>
    /// The synthesis of the small capitals of a character (https://drafts.csswg.org/css-fonts-4/#font-variant-caps-prop): the
    /// availability of the features is decided per script ("should explicitly not decide this on a per-character basis"),
    /// as Chrome's OpenTypeCapsSupport. The petite capitals fall back to the small capitals, <c>unicase</c> to the small
    /// capitals of the lowercased uppercase letters; the small capitals the font does not have are synthesized from the
    /// uppercase letters when <c>font-synthesis-small-caps</c> is <c>auto</c>. The caseless characters keep the features.
    /// </summary>
    private static Synthesis GetCapsSynthesis(int codePoint, MatchedFont font, UnicodeScript script, in TextShapingOptions options)
    {
        var isLowercase = CaseMapping.ChangesWhenUppercased(codePoint) || codePoint == 0x131;
        var isUppercase = !isLowercase && CaseMapping.ChangesWhenLowercased(codePoint);
        if (!isLowercase && !isUppercase)
            return Synthesis.None;

        var synthesize = options.SynthesizeSmallCaps;
        var openType = font.Font;
        switch (options.Caps)
        {
            case FontCapsVariant.SmallCaps:
                return isLowercase && synthesize && !openType.HasSubstitutionFeature(script, SmallCapsTag) ? SynthesizedLowercase : Synthesis.None;

            case FontCapsVariant.AllSmallCaps:
                if (!synthesize || (openType.HasSubstitutionFeature(script, SmallCapsTag) && openType.HasSubstitutionFeature(script, CapitalsToSmallCapsTag)))
                    return Synthesis.None;

                return isLowercase ? SynthesizedLowercase : SynthesizedUppercase;

            case FontCapsVariant.PetiteCaps:
                if (!isLowercase || openType.HasSubstitutionFeature(script, PetiteCapsTag))
                    return Synthesis.None;

                if (openType.HasSubstitutionFeature(script, SmallCapsTag))
                    return WithCapsFeatures(Synthesis.None, FontCapsFeatures.SmallCaps);

                return synthesize ? SynthesizedLowercase : Synthesis.None;

            case FontCapsVariant.AllPetiteCaps:
                if (openType.HasSubstitutionFeature(script, PetiteCapsTag) && openType.HasSubstitutionFeature(script, CapitalsToPetiteCapsTag))
                    return Synthesis.None;

                if (openType.HasSubstitutionFeature(script, SmallCapsTag) && openType.HasSubstitutionFeature(script, CapitalsToSmallCapsTag))
                    return WithCapsFeatures(Synthesis.None, FontCapsFeatures.AllSmallCaps);

                return !synthesize ? Synthesis.None : isLowercase ? SynthesizedLowercase : SynthesizedUppercase;

            case FontCapsVariant.Unicase:
                // "the property behaves as if small-caps was applied only to lowercased uppercase letters"
                if (!isUppercase || !synthesize || openType.HasSubstitutionFeature(script, UnicaseTag))
                    return Synthesis.None;

                return openType.HasSubstitutionFeature(script, SmallCapsTag) ? WithCapsFeatures(Synthesis.Lowercase, FontCapsFeatures.SmallCaps) : SynthesizedUppercase;

            default:
                return Synthesis.None;
        }
    }

    /// <summary>The scale of the font size of the glyphs of a synthesis.</summary>
    private static double GetSynthesisScale(Synthesis synthesis, in TextShapingOptions options)
    {
        var scale = (synthesis & Synthesis.SmallCaps) != Synthesis.None ? SmallCapsScale : 1;
        if ((synthesis & Synthesis.Position) != Synthesis.None)
        {
            scale *= GetPositionScale(options.FontSize);
        }

        return scale;
    }

    /// <summary>
    /// The size of the synthesized subscripts and superscripts relative to the font size: as <c>font-size: smaller</c>, the
    /// reduction is larger for the larger sizes ("For larger text, the font size is often reduced by a third but for smaller
    /// text sizes, the reduction can be much less", https://drafts.csswg.org/css-fonts-4/#font-variant-position-prop). The
    /// values are the ones of Firefox (gfxFontStyle::AdjustForSubSuperscript): 0.82 up to 20px, 0.667 from 45px, interpolated
    /// between them.
    /// </summary>
    internal static double GetPositionScale(double fontSize)
    {
        const double SmallSize = 20;
        const double LargeSize = 45;
        const double SmallRatio = 0.82;
        const double LargeRatio = 0.667;
        if (fontSize < SmallSize)
            return SmallRatio;

        if (fontSize >= LargeSize)
            return LargeRatio;

        var t = (fontSize - SmallSize) / (LargeSize - SmallSize);
        return ((1 - t) * SmallRatio) + (t * LargeRatio);
    }

    /// <summary>
    /// The shift of the baseline of the synthesized subscripts and superscripts along the block axis (positive towards the
    /// line-under side), relative to the font size before its reduction: 0.34em up for the superscripts, 0.2em down for the
    /// subscripts, as Firefox (the OS/2 offsets "are not always accurate in practice and so cannot be relied upon").
    /// </summary>
    internal static double GetPositionShift(FontPositionVariant position, double fontSize) => position switch
    {
        FontPositionVariant.Super => -0.34 * fontSize,
        FontPositionVariant.Sub => 0.2 * fontSize,
        _ => 0,
    };

    /// <summary>
    /// Writes the case mapping of a code point for the synthesized small capitals: the mappings of <c>text-transform</c>
    /// ("the casing transformations must match those used for the text-transform property").
    /// </summary>
    private static int MapCase(int codePoint, bool uppercase, bool turkic, Span<char> destination)
    {
        if (codePoint <= char.MaxValue && !char.IsSurrogate((char)codePoint))
            return TextTransformer.Transform((char)codePoint, uppercase ? TextTransform.Uppercase : TextTransform.Lowercase, turkic, wordStart: false, destination);

        return uppercase ? CaseMapping.ToUpper(codePoint, destination) : CaseMapping.ToLower(codePoint, destination);
    }

    private static int CountCodePoints(ReadOnlySpan<char> text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsHighSurrogate(text[i]) || i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Adds the case mapping of a code point to the glyphs of a run: the code points of the mapping have the cluster of the character.</summary>
    private static void AddMappedCodePoint(GlyphBuffer buffer, int codePoint, int cluster, bool uppercase, bool turkic)
    {
        Span<char> mapped = stackalloc char[CaseMapping.MaxLength];
        var length = MapCase(codePoint, uppercase, turkic, mapped);
        for (var i = 0; i < length;)
        {
            Rune.DecodeFromUtf16(mapped[i..length], out var rune, out var consumed);
            buffer.Add(rune.Value, cluster);
            i += consumed;
        }
    }
}
