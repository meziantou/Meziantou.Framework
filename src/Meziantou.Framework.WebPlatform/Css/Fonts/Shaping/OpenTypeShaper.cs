using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>The normalization of the characters before the shaping (HarfBuzz's <c>hb_ot_shape_normalization_mode_t</c>).</summary>
internal enum NormalizationMode : byte
{
    /// <summary>The characters are not decomposed when the font supports them, and never composed.</summary>
    None,

    /// <summary>The characters are fully decomposed.</summary>
    Decomposed,

    /// <summary>The characters are decomposed when the font does not support them, and the marks are composed with their base when the font supports the composition (never base with base).</summary>
    ComposedDiacritics,

    /// <summary>The characters are always decomposed, then composed back.</summary>
    ComposedDiacriticsNoShortCircuit,

    /// <summary>The default (<see cref="ComposedDiacritics"/>).</summary>
    Auto,
}

/// <summary>When the advances of the marks are set to zero (HarfBuzz's <c>hb_ot_shape_zero_width_marks_type_t</c>).</summary>
internal enum ZeroWidthMarks : byte
{
    None,

    /// <summary>The marks of the GDEF table, before GPOS.</summary>
    ByGdefEarly,

    /// <summary>The marks of the GDEF table, after GPOS.</summary>
    ByGdefLate,
}

/// <summary>
/// The shaper of a script (HarfBuzz's <c>hb_ot_shaper_t</c>, https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-ot-shaper.hh,
/// MIT license): the hooks that the shaping engine (<see cref="OpenTypeShapingEngine"/>) calls to implement the features, the
/// joining, the reordering and the syllables of a script. The <see cref="Default"/> shaper implements none; the
/// <see cref="ArabicShaper"/> implements the cursive joining scripts. A shaper is stateless: its data for a font is created
/// by <see cref="CreateData"/> and kept by the <see cref="ShapePlan"/>; the state of a run is in the glyphs of the buffer
/// (<see cref="GlyphInfo.ShaperCategory"/>, <see cref="GlyphInfo.ShaperAuxiliary"/>, <see cref="GlyphInfo.Syllable"/>).
/// </summary>
/// <remarks>
/// A shaper adds its features in <see cref="CollectFeatures"/> with the stages of their lookups (the pauses of
/// <see cref="ShapePlanBuilder.AddGsubPause"/>, where a callback can reorder the glyphs between two features), sets the masks
/// of its features on the glyphs in <see cref="SetupMasks"/> (e.g. the Arabic joining forms), and can modify the text before
/// the shaping (<see cref="PreprocessText"/>, e.g. the Hangul composition) and the glyphs after it
/// (<see cref="PostprocessGlyphs"/>). <see cref="Categorize"/> selects the shaper of a script.
/// </remarks>
internal abstract class OpenTypeShaper
{
    /// <summary>The shaper of the scripts that need no specific processing (HarfBuzz's <c>_hb_ot_shaper_default</c>).</summary>
    public static OpenTypeShaper Default { get; } = new DefaultShaper();

    /// <summary>The script tag of GPOS that the shaper requires (the fallback positioning is used otherwise), or 0.</summary>
    public virtual uint GposTag => 0;

    /// <summary>The normalization of the characters.</summary>
    public virtual NormalizationMode NormalizationPreference => NormalizationMode.Auto;

    /// <summary>When the advances of the marks are set to zero.</summary>
    public virtual ZeroWidthMarks ZeroWidthMarks => ZeroWidthMarks.ByGdefLate;

    /// <summary>Whether the fallback positioning (the marks without GPOS, the kern table) applies to the script.</summary>
    public virtual bool FallbackPosition => true;

    /// <summary>Adds the features of the script to a plan (with their stages and pauses).</summary>
    public virtual void CollectFeatures(ShapePlanBuilder builder)
    {
    }

    /// <summary>Overrides the features after the common features are added.</summary>
    public virtual void OverrideFeatures(ShapePlanBuilder builder)
    {
    }

    /// <summary>Creates the data of the shaper for a plan (e.g. the masks of its features), kept as <see cref="ShapePlan.ShaperData"/>.</summary>
    public virtual object? CreateData(ShapePlan plan) => null;

    /// <summary>Modifies the characters before the shaping.</summary>
    public virtual void PreprocessText(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font)
    {
    }

    /// <summary>Modifies the glyphs after the positioning.</summary>
    public virtual void PostprocessGlyphs(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font)
    {
    }

    /// <summary>Decomposes a character for the normalization (the canonical decomposition by default).</summary>
    public virtual bool Decompose(ShapePlan plan, in ShapingFont font, int ab, out int a, out int b) => ShapingNormalizer.DecomposeUnicode(ab, out a, out b);

    /// <summary>Composes two characters for the normalization (the canonical composition by default).</summary>
    public virtual bool Compose(ShapePlan plan, in ShapingFont font, int a, int b, out int ab) => ShapingNormalizer.ComposeUnicode(a, b, out ab);

    /// <summary>Sets the masks of the features of the shaper on the glyphs (the characters must not be modified).</summary>
    public virtual void SetupMasks(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font)
    {
    }

    /// <summary>Reorders a sequence of marks sorted by combining class (from <paramref name="start"/> to <paramref name="end"/>).</summary>
    public virtual void ReorderMarks(ShapePlan plan, ShapingBuffer buffer, int start, int end)
    {
    }

    /// <summary>
    /// Selects the shaper of a script (HarfBuzz's <c>hb_ot_shaper_categorize</c>, hb-ot-shaper.hh) from the script, the
    /// direction and the script tag chosen in the GSUB table. The syllabic scripts use the default shaper when the font was
    /// designed for the <c>DFLT</c> script (or <c>latn</c> was picked arbitrarily); the Indic scripts with a script tag of
    /// the Universal Shaping Engine (<c>dev3</c>...) use it; the Myanmar fonts with the old <c>mymr</c> tag use the default
    /// shaper. The Myanmar Zawgyi encoding (HarfBuzz's private script Qaag) is not detected.
    /// </summary>
    public static OpenTypeShaper Categorize(UnicodeScript script, ShapingDirection direction, uint gsubScriptTag)
    {
        var defaultScript = gsubScriptTag is OpenTypeTag.DefaultScript or OpenTypeTag.LatinScript;
        switch (script)
        {
            case UnicodeScript.Arabic or UnicodeScript.Syriac:
                // The Arabic shaper is used for Arabic even without its script tag in the font (it has a fallback shaping), but
                // only for horizontal text
                return (gsubScriptTag != OpenTypeTag.DefaultScript || script == UnicodeScript.Arabic) && direction != ShapingDirection.TopToBottom ? ArabicShaper.Instance : Default;

            case UnicodeScript.Thai or UnicodeScript.Lao:
                return ThaiShaper.Instance;

            case UnicodeScript.Hangul:
                return HangulShaper.Instance;

            case UnicodeScript.Hebrew:
                return HebrewShaper.Instance;

            case UnicodeScript.Bengali or UnicodeScript.Devanagari or UnicodeScript.Gujarati or UnicodeScript.Gurmukhi or UnicodeScript.Kannada or UnicodeScript.Malayalam
                or UnicodeScript.Oriya or UnicodeScript.Tamil or UnicodeScript.Telugu:
                if (defaultScript)
                    return Default;

                return (gsubScriptTag & 0xFF) == '3' ? UseShaper.Instance : IndicShaper.Instance;

            case UnicodeScript.Khmer:
                return KhmerShaper.Instance;

            case UnicodeScript.Myanmar:
                return defaultScript || gsubScriptTag == MyanmarOldTag ? Default : MyanmarShaper.Instance;

            default:
                return IsUseScript(script) && !defaultScript ? UseShaper.Instance : Default;
        }
    }

    private static readonly uint MyanmarOldTag = ShapePlanBuilder.Tag("mymr");

    /// <summary>The scripts of the Universal Shaping Engine (the list of HarfBuzz 14.6.0).</summary>
    private static bool IsUseScript(UnicodeScript script) => script is UnicodeScript.Tibetan or UnicodeScript.Mongolian or UnicodeScript.Sinhala or UnicodeScript.Buhid
        or UnicodeScript.Hanunoo or UnicodeScript.Tagalog or UnicodeScript.Tagbanwa or UnicodeScript.Limbu or UnicodeScript.TaiLe or UnicodeScript.Buginese
        or UnicodeScript.Kharoshthi or UnicodeScript.SylotiNagri or UnicodeScript.Tifinagh or UnicodeScript.Balinese or UnicodeScript.Nko or UnicodeScript.PhagsPa
        or UnicodeScript.Cham or UnicodeScript.KayahLi or UnicodeScript.Lepcha or UnicodeScript.Rejang or UnicodeScript.Saurashtra or UnicodeScript.Sundanese
        or UnicodeScript.EgyptianHieroglyphs or UnicodeScript.Javanese or UnicodeScript.Kaithi or UnicodeScript.MeeteiMayek or UnicodeScript.TaiTham
        or UnicodeScript.TaiViet or UnicodeScript.Batak or UnicodeScript.Brahmi or UnicodeScript.Mandaic or UnicodeScript.Chakma or UnicodeScript.Miao
        or UnicodeScript.Sharada or UnicodeScript.Takri or UnicodeScript.Duployan or UnicodeScript.Grantha or UnicodeScript.Khojki or UnicodeScript.Khudawadi
        or UnicodeScript.Mahajani or UnicodeScript.Manichaean or UnicodeScript.Modi or UnicodeScript.PahawhHmong or UnicodeScript.PsalterPahlavi
        or UnicodeScript.Siddham or UnicodeScript.Tirhuta or UnicodeScript.Ahom or UnicodeScript.Multani or UnicodeScript.Adlam or UnicodeScript.Bhaiksuki
        or UnicodeScript.Marchen or UnicodeScript.Newa or UnicodeScript.MasaramGondi or UnicodeScript.Soyombo or UnicodeScript.ZanabazarSquare
        or UnicodeScript.Dogra or UnicodeScript.GunjalaGondi or UnicodeScript.HanifiRohingya or UnicodeScript.Makasar or UnicodeScript.Medefaidrin
        or UnicodeScript.OldSogdian or UnicodeScript.Sogdian or UnicodeScript.Elymaic or UnicodeScript.Nandinagari or UnicodeScript.NyiakengPuachueHmong
        or UnicodeScript.Wancho or UnicodeScript.Chorasmian or UnicodeScript.DivesAkuru or UnicodeScript.KhitanSmallScript or UnicodeScript.Yezidi
        or UnicodeScript.CyproMinoan or UnicodeScript.OldUyghur or UnicodeScript.Tangsa or UnicodeScript.Toto or UnicodeScript.Vithkuqi or UnicodeScript.Kawi
        or UnicodeScript.NagMundari or UnicodeScript.Garay or UnicodeScript.GurungKhema or UnicodeScript.KiratRai or UnicodeScript.OlOnal or UnicodeScript.Sunuwar
        or UnicodeScript.Todhri or UnicodeScript.TuluTigalari or UnicodeScript.BeriaErfe or UnicodeScript.Sidetic or UnicodeScript.TaiYo or UnicodeScript.TolongSiki
        or UnicodeScript.Jurchen or UnicodeScript.ProtoCuneiform or UnicodeScript.Seal;

    private sealed class DefaultShaper : OpenTypeShaper;
}
