using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// Shapes a run of text: turns its characters into the glyphs of a font and their positions, with the OpenType Layout
/// tables of the font.
/// </summary>
/// <remarks>
/// <para>A run has one font, one script, one language and one direction. The steps are:</para>
/// <list type="number">
/// <item><description>
/// <b>Characters.</b> The characters that the font does not have are decomposed when it has their parts, marks are put
/// in canonical order, and a character and its marks are composed when the font has the composed character: the text
/// is in the form the font was made for, whatever form it was typed in.
/// </description></item>
/// <item><description><b>Glyphs.</b> Each character gets its glyph from the character map, variation sequences included.</description></item>
/// <item><description>
/// <b>Substitution.</b> The lookups of the features of the GSUB table that apply to the run, in the order of the
/// lookup list.
/// </description></item>
/// <item><description>
/// <b>Positioning.</b> The advances of the glyphs, then the lookups of the GPOS table; the legacy kerning table when
/// the font has no kerning feature. The advance of a mark is removed.
/// </description></item>
/// </list>
/// <para>
/// The features applied to every run are the ones the OpenType specification asks a client to apply by default for
/// horizontal text: <c>ccmp</c>, <c>locl</c>, <c>rlig</c>, <c>calt</c>, <c>clig</c>, <c>liga</c>, <c>rclt</c>, the
/// direction features (<c>ltra</c>, <c>ltrm</c>, <c>rtla</c>, <c>rtlm</c>), the fraction features around a fraction
/// slash, and for positioning <c>abvm</c>, <c>blwm</c>, <c>mark</c>, <c>mkmk</c>, <c>curs</c>, <c>dist</c>,
/// <c>kern</c>.
/// </para>
/// <para>
/// A run of a vertical line whose glyphs are upright is shaped from the top to the bottom: each glyph advances by
/// minus its advance height and is placed by minus its vertical origin, so that its origin is at the top of its
/// advance, in the middle of its width. The feature of such a run is <c>vert</c>, with those that do not depend on the
/// direction (<c>ccmp</c>, <c>locl</c>, <c>rlig</c>, <c>abvm</c>, <c>blwm</c>, <c>mark</c>, <c>mkmk</c>); the letters
/// of the scripts that join are not joined, and neither the legacy kerning table nor the marks placed from the shapes
/// of the glyphs are used.
/// </para>
/// </remarks>
public static class TextShaper
{
    // The bits of the mask of a glyph: the features of every glyph, then the features of the parts of a fraction.
    private const uint GlobalMask = 0x01;
    private const uint FractionMask = 0x02;
    private const uint NumeratorMask = 0x04;
    private const uint DenominatorMask = 0x08;

    // The feature that mirrors a glyph in a right-to-left run, for the characters Unicode does not mirror itself.
    private const uint MirrorMask = 0x10;

    // The first bit of the features a shaping model applies to the glyphs it chooses; the features the caller sets
    // come after them.
    private const int FirstShaperBit = 5;

    private const int FractionSlash = 0x2044;
    private const int ZeroWidthNonJoiner = 0x200C;
    private const int ZeroWidthJoiner = 0x200D;
    private const int CombiningGraphemeJoiner = 0x034F;

    private static readonly uint Frac = ShapingFeature.ToTag("frac");
    private static readonly uint Numr = ShapingFeature.ToTag("numr");
    private static readonly uint Dnom = ShapingFeature.ToTag("dnom");
    private static readonly uint Kern = ShapingFeature.ToTag("kern");

    private static readonly uint Rtlm = ShapingFeature.ToTag("rtlm");
    // The required variation alternates: the glyphs a variable font replaces in a part of its variation space. Its
    // lookups are applied before every other one, whatever their place in the lookup list.
    private static readonly uint Rvrn = ShapingFeature.ToTag("rvrn");
    private static readonly uint Mark = ShapingFeature.ToTag("mark");
    private static readonly uint Vert = ShapingFeature.ToTag("vert");
    private static readonly uint Vrt2 = ShapingFeature.ToTag("vrt2");
    private static readonly uint Vrtr = ShapingFeature.ToTag("vrtr");
    private static readonly uint Cfar = ShapingFeature.ToTag("cfar");

    private static readonly uint[] LeftToRightFeatures = [ShapingFeature.ToTag("ltra"), ShapingFeature.ToTag("ltrm")];
    private static readonly uint[] RightToLeftFeatures = [ShapingFeature.ToTag("rtla"), Rtlm];

    private static readonly uint[] DefaultFeatures =
    [
        ShapingFeature.ToTag("abvm"), ShapingFeature.ToTag("blwm"), ShapingFeature.ToTag("ccmp"), ShapingFeature.ToTag("locl"),
        ShapingFeature.ToTag("mark"), ShapingFeature.ToTag("mkmk"), ShapingFeature.ToTag("rlig"),
        ShapingFeature.ToTag("calt"), ShapingFeature.ToTag("clig"), ShapingFeature.ToTag("curs"), ShapingFeature.ToTag("dist"),
        Kern, ShapingFeature.ToTag("liga"), ShapingFeature.ToTag("rclt"), Rvrn,
    ];

    // The features of a vertical line: those that do not depend on the direction, and the vertical forms. The other
    // vertical features (alternate metrics, kerning, rotated forms) are applied when the caller asks for them.
    private static readonly uint[] VerticalFeatures =
    [
        ShapingFeature.ToTag("abvm"), ShapingFeature.ToTag("blwm"), ShapingFeature.ToTag("ccmp"), ShapingFeature.ToTag("locl"),
        ShapingFeature.ToTag("mark"), ShapingFeature.ToTag("mkmk"), ShapingFeature.ToTag("rlig"), Rvrn, Vert,
    ];

    // The forms of the glyphs of a vertical line, which a font may list under a few scripts only: those of upright
    // glyphs, and those of glyphs on their side.
    private static readonly uint[] UprightFormFeatures = [Vert, Vrt2];
    private static readonly uint[] SidewaysFormFeatures = [Vrtr];

    // The features that place marks.
    private static readonly uint[] MarkFeatures = [Mark, ShapingFeature.ToTag("mkmk")];

    /// <summary>Adds the characters of a text to a buffer, each with the index of its first UTF-16 code unit as cluster.</summary>
    public static void AddText(GlyphBuffer buffer, ReadOnlySpan<char> text, int clusterOffset = 0)
    {
        var index = 0;
        while (index < text.Length)
        {
            Rune.DecodeFromUtf16(text[index..], out var rune, out var consumed);
            buffer.Add(rune.Value, clusterOffset + index);
            index += consumed;
        }
    }

    /// <summary>Shapes the characters of a buffer.</summary>
    /// <param name="buffer">The characters of the run; it receives the glyphs and their positions.</param>
    /// <param name="face">The font.</param>
    /// <param name="script">The script of the run.</param>
    /// <param name="languages">The OpenType tags of the language of the run, by order of preference: the language system of the first one the font has for the script is used; empty for the default language system.</param>
    /// <param name="isRightToLeft"><see langword="true"/> when the run is laid out from right to left.</param>
    /// <param name="features">The features to turn on or off, besides the default ones.</param>
    /// <param name="isVertical">
    /// <see langword="true"/> when the run is laid out from top to bottom, its glyphs upright. The glyphs then have
    /// their vertical forms (the <c>vert</c> feature, wherever the font has it, in the place of the features of a
    /// horizontal line: <c>calt</c>, <c>clig</c>, <c>curs</c>, <c>dist</c>, <c>kern</c>, <c>liga</c>, <c>rclt</c>),
    /// a vertical advance (<see cref="GlyphPosition.YAdvance"/>, negative since the y axis of a font goes up) and
    /// no horizontal one, and an offset from the pen to their horizontal origin: the opposite of their vertical
    /// origin (<see cref="FontFace.GetVerticalOrigin"/>). A vertical run is never right to left. The rotated forms
    /// (<c>vrt2</c>) of a vertical run, and the forms of turned glyphs (<c>vrtr</c>) of a horizontal one, are found
    /// wherever the font has them too, when the caller asks for them.
    /// </param>
    /// <param name="scratch">The working storage that measures the outlines of a font without vertical origins, or <see langword="null"/> when the run is horizontal.</param>
    /// <param name="cache">What the runs of a layout share about their fonts, or <see langword="null"/> when the run is shaped on its own.</param>
    /// <param name="areCharactersMirrored">
    /// <see langword="true"/> when the caller already replaced the characters of a right-to-left run by their mirrored
    /// forms (rule L4 of the Unicode Bidirectional Algorithm): they are not replaced again, and the mirrored characters
    /// that have no mirrored form still get the mirrored forms of the font (<c>rtlm</c>).
    /// </param>
    /// <exception cref="InvalidFontException">A layout table of the font is malformed.</exception>
    /// <exception cref="FontResourceLimitException">The glyphs exceed <see cref="FontResourceLimits.MaxTextGlyphs"/>.</exception>
    public static void Shape(GlyphBuffer buffer, FontFace face, UnicodeScript script, ReadOnlySpan<uint> languages, bool isRightToLeft, ReadOnlySpan<ShapingFeature> features, bool isVertical = false, GlyphScratch? scratch = null, ShapingCache? cache = null, bool areCharactersMirrored = false)
    {
        buffer.StartShaping();
        SetCharacterProperties(buffer);
        FormClusters(buffer);
        if (isRightToLeft)
        {
            Mirror(buffer, face, areCharactersMirrored);
        }

        // What depends on the font and the settings of the run only is computed where it is used, by the first run that
        // has them, and kept for the other runs of the layout: a malformed table fails at the same place.
        var planned = cache?.GetPlan(face, script, languages, isRightToLeft, isVertical, features) ?? new Plan();
        var isPlanned = planned.IsComplete;
        Span<uint> scripts = stackalloc uint[OpenTypeScript.MaxTagCount];
        scripts = scripts[..OpenTypeScript.GetTags(script, scripts)];
        if (!isPlanned)
        {
            planned.ScriptTag = face.Gsub?.FindScriptTag(face.GsubData.Span, scripts) ?? 0;
            var model = GetShaper(script, planned.ScriptTag);

            // The letters of Arabic and of Syriac join along a horizontal line only: upright, each one is on its own
            if (isVertical && model == ScriptShaperKind.Arabic)
            {
                model = ScriptShaperKind.Default;
            }

            planned.Shaper = model;
            planned.ShaperPlan = model switch
            {
                ScriptShaperKind.Arabic => ArabicShaper.GetPlan(script, face.Gsub is { } substitutions && HasFeature(substitutions, face.GsubData.Span, scripts, languages, ArabicShaper.RequiredAlternates)),
                ScriptShaperKind.Hangul => HangulShaper.Plan,
                ScriptShaperKind.Indic => IndicShaper.Plan,
                ScriptShaperKind.Khmer => KhmerShaper.Plan,
                ScriptShaperKind.Myanmar => MyanmarShaper.Plan,
                ScriptShaperKind.Universal => UniversalShaper.Plan,
                _ => ShaperPlan.Default,
            };

            // A font of Thai made before OpenType Layout, which has no substitution for the script, has the forms of
            // its marks as characters of the private use area.
            planned.HasThaiPrivateUseForms = model == ScriptShaperKind.Thai && script == UnicodeScript.Thai
                && (face.Gsub is not { } thaiTable || thaiTable.FindLanguageSystem(face.GsubData.Span, scripts, languages) == 0);
        }

        var scriptTag = planned.ScriptTag;
        var shaper = planned.Shaper;
        var shaperPlan = planned.ShaperPlan;
        var hasThaiPrivateUseForms = planned.HasThaiPrivateUseForms;

        // What a model does to the characters themselves, before they are put in the form the font has.
        if (shaper == ScriptShaperKind.Thai)
        {
            ThaiShaper.DecomposeSaraAm(buffer);
            if (hasThaiPrivateUseForms)
            {
                ThaiShaper.RecordPrivateUseForms(buffer);
            }
        }
        else if (shaper == ScriptShaperKind.Hangul)
        {
            HangulShaper.ComposeSyllables(buffer, face);
        }
        else if (shaper is ScriptShaperKind.Indic or ScriptShaperKind.Universal)
        {
            IndicShaper.SeparateVowelSequences(buffer, script);
        }

        // A font that positions the points of Hebrew needs no precomposed letter.
        if (!isPlanned)
        {
            var featurePlan = new FeaturePlan(features, FirstShaperBit + shaperPlan.PerGlyphCount);
            planned.Features = featurePlan;
            planned.ComposesPresentationForms = shaper == ScriptShaperKind.Hebrew
                && ((featurePlan.TryGetMask(Mark, out var markMask) && markMask == 0) || !(face.Gpos is { } positioning && HasFeature(positioning, face.GposData.Span, scripts, languages, Mark)));
        }

        var plan = planned.Features!;
        var composesPresentationForms = planned.ComposesPresentationForms;
        Normalize(buffer, face, shaper, composesPresentationForms);

        // A font without vertical forms (no vert feature) may have the presentation forms of the vertical punctuation
        if (isVertical && !(face.Gsub is { } verticalTable && verticalTable.FindFeature(face.GsubData.Span, Vert) != LayoutTable.NoFeature))
        {
            UseVerticalPresentationForms(buffer, face);
        }

        MapGlyphs(buffer, face);
        if (hasThaiPrivateUseForms)
        {
            ThaiShaper.ApplyPrivateUseForms(buffer, face);
        }

        ReadOnlySpan<int> coordinates = face.Instance is null ? default : face.Instance.Coordinates;
        var applier = cache?.GetApplier(buffer, face) ?? new LayoutApplier(buffer, face);
        applier.SetGlyphClasses();
        var hasStretching = false;
        if (!isPlanned)
        {
            var hasSubstitutionFractions = false;
            planned.Substitutions = face.Gsub is { } gsub ? CollectLookups(gsub, face.GsubData.Span, scripts, languages, isRightToLeft, isVertical, plan, shaperPlan, coordinates, out hasSubstitutionFractions, out _) : [];
            planned.HasFractions = hasSubstitutionFractions;
        }

        var lookups = planned.Substitutions;
        var hasFractions = planned.HasFractions;
        SetMasks(buffer, plan, hasFractions, shaper, script, isVertical);
        // A font of Arabic made before OpenType Layout has no substitution for the script: its letters take their forms
        // from the presentation forms it has as characters.
        if (!isPlanned)
        {
            planned.HasArabicPresentationForms = shaper == ScriptShaperKind.Arabic && script == UnicodeScript.Arabic && (face.Gsub is not { } arabicTable || arabicTable.FindLanguageSystem(face.GsubData.Span, scripts, languages) == 0);
        }

        if (planned.HasArabicPresentationForms)
        {
            ArabicShaper.ApplyPresentationForms(buffer, face, FirstShaperBit);
        }

        // The model of an Indic script remembers what it asked the font.
        if (!isPlanned && shaper == ScriptShaperKind.Indic)
        {
            planned.Indic = CreateIndicShaper(face, script, scriptTag, scripts, languages, coordinates, plan);
        }

        var indic = planned.Indic;

        if (face.Gsub is { } substitutionTable)
        {
            applier.Begin(substitutionTable, face.GsubData, isPositioning: false, isRightToLeft, digests: cache?.GetDigests(face, isPositioning: false) ?? ShapingCache.Create(face, isPositioning: false));
        }

        // Stage after stage: the lookups of a stage, then what the model of the script does between two stages.
        var next = 0;
        for (var stage = 0; stage <= shaperPlan.FinalStage; stage++)
        {
            var isStretchingStage = false;
            for (; next < lookups.Count && lookups[next].Stage <= stage; next++)
            {
                applier.ApplyLookup(lookups[next].Index, lookups[next].Mask, lookups[next].Flags);
                isStretchingStage |= lookups[next].Flags.HasFlag(ShaperFeatureFlags.Stretching);
            }

            // The parts of the stretching glyphs are the glyphs the stage of their feature made, before another stage
            // makes others.
            if (isStretchingStage)
            {
                ArabicShaper.RecordStretching(buffer.Info);
                hasStretching = true;
            }

            if (shaper == ScriptShaperKind.Khmer && stage == KhmerShaper.ReorderingStage)
            {
                if (!isPlanned)
                {
                    planned.HasCfar = face.Gsub is { } table && HasFeature(table, face.GsubData.Span, scripts, languages, Cfar) && !(plan.TryGetMask(Cfar, out var cfarMask) && cfarMask == 0);
                }

                KhmerShaper.Reorder(buffer, face, FirstShaperBit, planned.HasCfar);
            }
            else if (shaper == ScriptShaperKind.Myanmar && stage == MyanmarShaper.ReorderingStage)
            {
                MyanmarShaper.Reorder(buffer, face);
            }
            else if (shaper == ScriptShaperKind.Universal)
            {
                // The glyph a feature made is told from the others by the stage that made it.
                switch (stage)
                {
                    case UniversalShaper.PreprocessingStage:
                        UniversalShaper.ClearSubstitutions(buffer.Info);
                        break;

                    case UniversalShaper.RephStage:
                        UniversalShaper.RecordReph(buffer.Info, FirstShaperBit);
                        UniversalShaper.ClearSubstitutions(buffer.Info);
                        break;

                    case UniversalShaper.PreBaseStage:
                        UniversalShaper.RecordPreBaseForm(buffer.Info);
                        break;

                    case UniversalShaper.ReorderingStage:
                        UniversalShaper.Reorder(buffer, face);
                        break;
                }
            }
            else if (indic is not null && stage == IndicShaper.InitialReorderingStage)
            {
                indic.ReorderBeforeBasicForms(buffer);
            }
            else if (indic is not null && stage == IndicShaper.FinalReorderingStage)
            {
                indic.ReorderAfterBasicForms(buffer);
            }
        }

        // The positions: the advance of each glyph, then what the font adds to it.
        buffer.CreatePositions();
        var info = buffer.Info;
        var positions = buffer.Positions;
        for (var i = 0; i < info.Length; i++)
        {
            if (isVertical)
            {
                // The pen is on the vertical origin of the glyph, and moves down by its advance height
                face.GetVerticalOrigin(info[i].Glyph, scratch, out var originX, out var originY);
                positions[i].YAdvance = -(info[i].ShaperData is > 0 and <= SpaceEm5 ? GetSpaceAdvance(face, info[i].ShaperData, isVertical: true) : face.GetAdvanceHeight(info[i].Glyph));
                positions[i].XOffset = -originX;
                positions[i].YOffset = -originY;
            }
            else
            {
                positions[i].XAdvance = info[i].ShaperData is > 0 and <= SpaceEm5 ? GetSpaceAdvance(face, info[i].ShaperData) : cache?.GetAdvanceWidth(face, info[i].Glyph) ?? face.GetAdvanceWidth(info[i].Glyph);
            }
        }

        // Myanmar and the universal model: the marks have no advance before the font places them, so that what it adds
        // to one stays ("OT processing cancels the width associated with a mark [...] the width of the base glyph must
        // be added back using the 'dist' feature").
        // A font without positioning table places no mark: a mark that has an advance is then drawn before the pen it
        // would have moved, over the glyph it follows.
        var marksAreMovedBack = face.Gpos is null && !isRightToLeft;
        var marksHaveNoAdvanceBeforePositioning = shaper is ScriptShaperKind.Myanmar or ScriptShaperKind.Universal;
        if (marksHaveNoAdvanceBeforePositioning)
        {
            for (var i = 0; i < info.Length; i++)
            {
                if (info[i].IsMark)
                {
                    positions[i].XOffset -= marksAreMovedBack ? positions[i].XAdvance : 0;
                    positions[i].YOffset -= marksAreMovedBack ? positions[i].YAdvance : 0;
                    positions[i].XAdvance = 0;
                    positions[i].YAdvance = 0;
                }
            }
        }

        var hasKerningFeature = false;
        if (face.Gpos is { } gpos)
        {
            if (!isPlanned)
            {
                planned.Positionings = CollectLookups(gpos, face.GposData.Span, scripts, languages, isRightToLeft, isVertical, plan, shaperPlan, coordinates, out _, out var hasKerning);
                planned.HasKerningFeature = hasKerning;
            }

            var positioningLookups = planned.Positionings!;
            hasKerningFeature = planned.HasKerningFeature;
            var digests = cache?.GetDigests(face, isPositioning: true) ?? ShapingCache.Create(face, isPositioning: true);

            // The plans a cache keeps remember the pairs of their kerning.
            if (!isPlanned && cache is not null)
            {
                planned.PairKerning = PairKerningCache.TryCreate(face, positioningLookups, isVertical);
            }

            applier.Begin(gpos, face.GposData, isPositioning: true, isRightToLeft, isVertical, digests);
            if (planned.PairKerning is not { } kerning || !kerning.TryApply(buffer, face, applier, digests, isRightToLeft))
            {
                foreach (var lookup in positioningLookups)
                {
                    applier.ApplyLookup(lookup.Index, lookup.Mask, lookup.Flags & ~ShaperFeatureFlags.PerSyllable);
                }
            }
        }

        // The pairs of the kerning table are those of a horizontal line
        if (!isVertical && !hasKerningFeature && face.HasLegacyKerning && (!plan.TryGetMask(Kern, out var kerningMask) || kerningMask != 0))
        {
            ApplyLegacyKerning(buffer, face, isRightToLeft);
        }

        // A mark does not move the pen. Without positioning lookups, a mark that has an advance is drawn before the pen
        // it would have moved, over the glyph it follows. The fonts of Hangul and of the Indic scripts give their marks
        // the advance they are to have: a vowel sign written after its consonant is a mark that takes room.
        var marksHaveNoAdvance = shaper is not (ScriptShaperKind.Hangul or ScriptShaperKind.Indic or ScriptShaperKind.Khmer or ScriptShaperKind.Myanmar or ScriptShaperKind.Universal);
        for (var i = 0; i < info.Length; i++)
        {
            if (info[i].IsMark && marksHaveNoAdvance)
            {
                if (marksAreMovedBack)
                {
                    positions[i].XOffset -= positions[i].XAdvance;
                    positions[i].YOffset -= positions[i].YAdvance;
                }

                positions[i].XAdvance = 0;
                positions[i].YAdvance = 0;
            }

            if (info[i].IsIgnorable)
            {
                positions[i] = default;
            }
        }

        // A font without positioning table places no mark: the marks are placed from the shapes of the glyphs. The
        // fonts of the scripts that have a model of their own draw their marks where they belong, and those of Thai
        // have the marks of each place as glyphs of their own.
        if (!isVertical && face.Gpos is null && shaper is ScriptShaperKind.Default or ScriptShaperKind.Arabic or ScriptShaperKind.Hebrew)
        {
            FallbackMarkPositioner.Apply(buffer, face, isRightToLeft);
        }

        LayoutApplier.FinishPositions(buffer.Positions, isRightToLeft);

        // The font saw the glyphs it has for the characters without visible form: a lookup can tell a joiner from a
        // space. They are hidden once it is done.
        HideIgnorables(buffer, face);

        // The glyphs of a right-to-left run were shaped in the order of the text: they are drawn in the other order.
        if (isRightToLeft)
        {
            buffer.Reverse();
            if (hasStretching)
            {
                ArabicShaper.ApplyStretching(buffer, face);
            }
        }

        planned.IsComplete = true;
    }

    /// <summary>
    /// What shaping derives from a font and the settings of a run before it looks at the glyphs: the model of the
    /// script, the masks of the features and the lookups to apply, and the questions the Indic model asked the font.
    /// The first run that has the settings computes it; the other runs of the layout reuse it.
    /// </summary>
    internal sealed class Plan
    {
        public UnicodeScript Script { get; init; }

        public uint[] Languages { get; init; } = [];

        public bool IsRightToLeft { get; init; }

        public bool IsVertical { get; init; }

        public ShapingFeature[] FeatureSettings { get; init; } = [];

        /// <summary>Gets or sets a value indicating whether a run shaped with the settings completed: every value below is set.</summary>
        public bool IsComplete { get; set; }

        public uint ScriptTag { get; set; }

        public ScriptShaperKind Shaper { get; set; }

        public ShaperPlan ShaperPlan { get; set; } = ShaperPlan.Default;

        public bool HasThaiPrivateUseForms { get; set; }

        public FeaturePlan? Features { get; set; }

        public bool ComposesPresentationForms { get; set; }

        public List<PlannedLookup> Substitutions { get; set; } = [];

        public bool HasFractions { get; set; }

        public bool HasArabicPresentationForms { get; set; }

        public IndicShaper? Indic { get; set; }

        public bool HasCfar { get; set; }

        public List<PlannedLookup>? Positionings { get; set; }

        public bool HasKerningFeature { get; set; }

        /// <summary>Gets or sets the kerning of the pairs of glyphs, when the positioning lookups only kern pairs.</summary>
        public PairKerningCache? PairKerning { get; set; }

        /// <summary>Determines whether the plan is the one of a font shaped with settings.</summary>
        public bool Matches(UnicodeScript script, ReadOnlySpan<uint> languages, bool isRightToLeft, bool isVertical, ReadOnlySpan<ShapingFeature> features)
            => Script == script && IsRightToLeft == isRightToLeft && IsVertical == isVertical && languages.SequenceEqual(Languages) && features.SequenceEqual(FeatureSettings);
    }

    private static ScriptShaperKind GetShaper(UnicodeScript script, uint scriptTag)
    {
        // A font that has features for a default script and none for the script of the text was made for no model: the
        // features do the work. A font without any script, or without substitution table, is shaped with the model.
        var hasDefaultScriptOnly = scriptTag == OpenTypeScript.Default || scriptTag == ShapingFeature.ToTag("dflt") || scriptTag == ShapingFeature.ToTag("latn");
        switch (script)
        {
            case UnicodeScript.Arabic or UnicodeScript.Syriac:
                return ScriptShaperKind.Arabic;

            case UnicodeScript.Hebrew:
                return ScriptShaperKind.Hebrew;

            case UnicodeScript.Hangul:
                return ScriptShaperKind.Hangul;

            case UnicodeScript.Thai or UnicodeScript.Lao:
                return ScriptShaperKind.Thai;

            case UnicodeScript.Khmer:
                return ScriptShaperKind.Khmer;

            case UnicodeScript.Myanmar:
                // The tag of the first version of the Myanmar model is the one of fonts that need no model.
                return hasDefaultScriptOnly || scriptTag == ShapingFeature.ToTag("mymr") ? ScriptShaperKind.Default : ScriptShaperKind.Myanmar;

            case UnicodeScript.Devanagari or UnicodeScript.Bengali or UnicodeScript.Gurmukhi or UnicodeScript.Gujarati or UnicodeScript.Oriya
                or UnicodeScript.Tamil or UnicodeScript.Telugu or UnicodeScript.Kannada or UnicodeScript.Malayalam:
                // The model is the one the font was made for: the tag with a 3 is the one of the universal model.
                if (hasDefaultScriptOnly)
                    return ScriptShaperKind.Default;

                return (scriptTag & 0xFF) == '3' ? ScriptShaperKind.Universal : ScriptShaperKind.Indic;

            default:
                return UniversalShaper.IsUniversalScript(script) && !hasDefaultScriptOnly ? ScriptShaperKind.Universal : ScriptShaperKind.Default;
        }
    }

    private static IndicShaper CreateIndicShaper(FontFace face, UnicodeScript script, uint scriptTag, ReadOnlySpan<uint> scripts, ReadOnlySpan<uint> languages, ReadOnlySpan<int> coordinates, FeaturePlan plan)
    {
        // The lookups of the features that tell what the font does with a consonant. A feature the caller turns off is
        // a feature the font does not have.
        var names = new[] { "rphf", "pref", "blwf", "pstf", "vatu" };
        var lookups = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var tag = ShapingFeature.ToTag(name);
            var isOff = plan.TryGetMask(tag, out var mask) && mask == 0;
            lookups[name] = face.Gsub is { } table && !isOff ? GetFeatureLookups(table, face.GsubData.Span, scripts, languages, tag, coordinates) : [];
        }

        return new IndicShaper(face, script, scriptTag, FirstShaperBit, name => lookups[name]);
    }

    /// <summary>Gets the lookups of a feature of a layout table for the script and the language of a run.</summary>
    private static int[] GetFeatureLookups(LayoutTable table, ReadOnlySpan<byte> data, ReadOnlySpan<uint> scripts, ReadOnlySpan<uint> languages, uint tag, ReadOnlySpan<int> coordinates)
    {
        var system = table.FindLanguageSystem(data, scripts, languages);
        if (system == 0)
            return [];

        var substitutions = table.FindFeatureSubstitutions(data, coordinates);
        var result = new List<int>();
        var lookupCount = table.GetLookupCount(data);
        var count = LayoutTable.GetFeatureCount(data, system);
        for (var i = 0; i < count; i++)
        {
            var feature = LayoutTable.GetFeatureIndex(data, system, i);
            if (table.GetFeatureTag(data, feature) != tag)
                continue;

            var featureLookups = table.GetFeatureLookupCount(data, feature, substitutions);
            for (var j = 0; j < featureLookups; j++)
            {
                var lookup = table.GetFeatureLookup(data, feature, j, substitutions);
                if (lookup < lookupCount && !result.Contains(lookup))
                {
                    result.Add(lookup);
                }
            }
        }

        result.Sort();
        return [.. result];
    }

    /// <summary>Determines whether a layout table has a feature for the script and the language of a run.</summary>
    private static bool HasFeature(LayoutTable table, ReadOnlySpan<byte> data, ReadOnlySpan<uint> scripts, ReadOnlySpan<uint> languages, uint tag)
    {
        var system = table.FindLanguageSystem(data, scripts, languages);
        if (system == 0)
            return false;

        var count = LayoutTable.GetFeatureCount(data, system);
        for (var i = 0; i < count; i++)
        {
            if (table.GetFeatureTag(data, LayoutTable.GetFeatureIndex(data, system, i)) == tag)
                return true;
        }

        return false;
    }

    // The properties of the characters of Latin-1.
    private static readonly GlyphInfo[] Latin1Characters = CreateLatin1Characters();

    private static GlyphInfo[] CreateLatin1Characters()
    {
        var characters = new GlyphInfo[0x100];
        for (var i = 0; i < characters.Length; i++)
        {
            SetUnicodeProperties(ref characters[i], i);
        }

        return characters;
    }

    /// <summary>Gives each character its general category, its combining class and what it is for shaping.</summary>
    private static void SetCharacterProperties(GlyphBuffer buffer)
    {
        foreach (ref var info in buffer.Info)
        {
            SetCharacterProperties(ref info, info.CodePoint);
        }
    }

    /// <summary>Gives a character of a buffer the properties of a code point; its cluster is kept.</summary>
    internal static void SetCharacterProperties(ref GlyphInfo info, int codePoint)
    {
        info.CodePoint = codePoint;
        info.Glyph = codePoint;
        if ((uint)codePoint < (uint)Latin1Characters.Length)
        {
            // The characters of most texts, whose properties are computed once.
            ref readonly var properties = ref Latin1Characters[codePoint];
            info.Category = properties.Category;
            info.CombiningClass = properties.CombiningClass;
            info.Flags = properties.Flags;
            info.ShaperData = 0;
            return;
        }

        SetUnicodeProperties(ref info, codePoint);
    }

    private static void SetUnicodeProperties(ref GlyphInfo info, int codePoint)
    {
        info.Category = UnicodeProperties.GetGeneralCategory(codePoint);
        info.CombiningClass = ShapingCombiningClass.Get(codePoint, (byte)UnicodeProperties.GetCanonicalCombiningClass(codePoint));
        var flags = 0;

        // The fillers of Hangul are default ignorable for Unicode and are letters for a font: they take part in the
        // shaping of a syllable and are shown as the font draws them. So are the format controls of the shorthands
        // (Duployan): the font has the glyphs that make its letters overlap or step.
        if (UnicodeProperties.IsDefaultIgnorable(codePoint) && codePoint is not (0x115F or 0x1160 or 0x3164 or 0xFFA0 or (>= 0x1BCA0 and <= 0x1BCA3)))
        {
            flags |= GlyphInfo.DefaultIgnorable;
        }

        flags |= codePoint switch
        {
            ZeroWidthNonJoiner => GlyphInfo.ZeroWidthNonJoiner,
            ZeroWidthJoiner => GlyphInfo.ZeroWidthJoiner,

            // The free variation selectors of Mongolian and the tag characters have no visible form, and are what
            // the substitutions of a font look for: they are passed over when glyphs are positioned only.
            (>= 0x180B and <= 0x180D) or 0x180F or (>= 0xE0020 and <= 0xE007F) => GlyphInfo.Hidden,
            _ => 0,
        };
        info.Flags = (byte)flags;
        info.ShaperData = 0;
    }

    /// <summary>
    /// Mirrors the characters of a right-to-left run that have a mirrored form: an opening parenthesis is drawn as a
    /// closing one. The mirrored character is used when the font has it; otherwise the font is asked to mirror the
    /// glyph with its <c>rtlm</c> feature.
    /// </summary>
    /// <summary>Replaces the punctuation of a vertical run by its vertical presentation forms, those the font has.</summary>
    private static void UseVerticalPresentationForms(GlyphBuffer buffer, FontFace face)
    {
        foreach (ref var info in buffer.Info)
        {
            var form = VerticalForms.Get(info.CodePoint);
            if (form != info.CodePoint && face.MapCodePoint(form) != 0)
            {
                var cluster = info.Cluster;
                SetCharacterProperties(ref info, form);
                info.Cluster = cluster;
            }
        }
    }

    private static void Mirror(GlyphBuffer buffer, FontFace face, bool areCharactersMirrored)
    {
        foreach (ref var info in buffer.Info)
        {
            if (!UnicodeProperties.IsBidiMirrored(info.CodePoint))
                continue;

            if (UnicodeProperties.TryGetBidiMirroringGlyph(info.CodePoint, out var mirrored) && (areCharactersMirrored || face.MapCodePoint(mirrored) != 0))
            {
                // A character the caller mirrored is the mirrored form of another one, which it replaced.
                if (areCharactersMirrored)
                    continue;

                var cluster = info.Cluster;
                SetCharacterProperties(ref info, mirrored);
                info.Cluster = cluster;
            }
            else
            {
                info.ShaperData = NeedsMirroring;
            }
        }
    }

    /// <summary>
    /// Gives a character and what continues it one cluster: its marks, the modifier of an emoji, the parts of an emoji
    /// joined by zero width joiners, the second of two regional indicators, the tags of a tag sequence.
    /// </summary>
    private static void FormClusters(GlyphBuffer buffer)
    {
        var info = buffer.Info;
        var regionalIndicators = 0;
        for (var i = 1; i < info.Length; i++)
        {
            var codePoint = info[i].CodePoint;
            var previous = info[i - 1].CodePoint;
            regionalIndicators = previous is >= 0x1F1E6 and <= 0x1F1FF ? regionalIndicators + 1 : 0;
            var continues = info[i].Category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
                || codePoint is ZeroWidthJoiner or (>= 0x1F3FB and <= 0x1F3FF) or (>= 0xE0020 and <= 0xE007F)
                || (previous == ZeroWidthJoiner && UnicodeEmoji.IsExtendedPictographic(new Rune(codePoint)))
                || (codePoint is >= 0x1F1E6 and <= 0x1F1FF && (regionalIndicators & 1) == 1);
            if (continues)
            {
                info[i].Cluster = info[i - 1].Cluster;
            }
        }
    }

    /// <summary>Puts the characters in the form the font has glyphs for.</summary>
    /// <remarks>
    /// A character the font does not have is decomposed into characters it has, the marks are put in the order of
    /// shaping, and a character and a mark are composed when the font has the composed character.
    /// </remarks>
    private static void Normalize(GlyphBuffer buffer, FontFace face, ScriptShaperKind shaper, bool composesPresentationForms)
    {
        // A character that has marks is decomposed as far as the font has the parts, and so are its marks: the marks
        // are then ordered and composed with it again whatever the form of the text was. A character without marks is
        // only decomposed when the font does not have it.
        Span<int> parts = stackalloc int[UnicodeNormalization.MaxCanonicalDecompositionLength + 1];
        var marksEnd = 0;
        var isKept = false;
        var hasMarks = false;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (i >= marksEnd && i + 1 < buffer.Length && IsMark(buffer.Info[i + 1].Category))
            {
                hasMarks = true;

                // A variation selector chooses a glyph of the character it follows: such a character is kept as it is,
                // and so are the marks of a variation selector that has no character before it.
                isKept = IsVariationSelector(buffer.Info[i].CodePoint);
                for (marksEnd = i + 1; marksEnd < buffer.Length && IsMark(buffer.Info[marksEnd].Category); marksEnd++)
                {
                    isKept |= IsVariationSelector(buffer.Info[marksEnd].CodePoint);
                }
            }

            // The blocks of Hangul were composed and decomposed by their model: the letters stay letters. The Indic
            // model decomposes every character: "the shaping engine decomposes two- or three-part matras into their
            // constituent parts before any repositioning", each part having its own place in the syllable.
            var codePoint = buffer.Info[i].CodePoint;
            var isSyllabic = shaper is ScriptShaperKind.Indic or ScriptShaperKind.Khmer or ScriptShaperKind.Myanmar or ScriptShaperKind.Universal;
            var isComplete = isSyllabic ? !(i < marksEnd && isKept) : i < marksEnd && !isKept && shaper != ScriptShaperKind.Hangul;
            if (!isComplete && face.MapCodePoint(codePoint) != 0)
                continue;

            var count = (i < marksEnd && isKept) || (shaper == ScriptShaperKind.Indic && codePoint is 0x0931 or 0x09DC or 0x09DD or 0x0B94) ? 0 : Decompose(face, codePoint, parts, 0, isComplete, shaper);
            if (count > 0)
            {
                buffer.Resize(i, 1, count);
                for (var j = 0; j < count; j++)
                {
                    SetCharacterProperties(ref buffer.Info[i + j], parts[j]);
                }

                marksEnd += i < marksEnd ? count - 1 : 0;
                i += count - 1;
                continue;
            }

            if (face.MapCodePoint(codePoint) == 0)
            {
                ReplaceMissingCharacter(ref buffer.Info[i], face);
            }
        }

        // A text without a character that has marks has nothing to order nor to compose: the parts of the characters
        // the font does not have stay in the order of their decomposition, the canonical order of Unicode.
        if (!hasMarks)
            return;

        // Put the marks of each character in the order of shaping: by combining class, the order of equal classes kept.
        var info = buffer.Info;
        for (var start = 0; start < info.Length; start++)
        {
            if (info[start].CombiningClass == 0)
                continue;

            var end = start + 1;
            while (end < info.Length && info[end].CombiningClass != 0)
            {
                end++;
            }

            // A mark that moves takes the marks it passes into its cluster first, with what shares their clusters:
            // the characters of a cluster stay together.
            for (var i = start + 1; i < end; i++)
            {
                var j = i;
                while (j > start && info[j - 1].CombiningClass > info[i].CombiningClass)
                {
                    j--;
                }

                if (j == i)
                    continue;

                buffer.MergeClusters(j, i + 1);
                var current = info[i];
                info[j..i].CopyTo(info[(j + 1)..]);
                info[j] = current;
            }

            // The order the script draws its marks in.
            var moved = shaper switch
            {
                ScriptShaperKind.Arabic => ArabicShaper.ReorderMarks(info[start..end]),
                ScriptShaperKind.Hebrew => HebrewShaper.ReorderMarks(info[start..end]),
                _ => false,
            };

            if (moved)
            {
                buffer.MergeClusters(start, end);
            }

            start = end - 1;
        }

        // A combining grapheme joiner between two marks keeps them in the order of the text. Where it changed nothing,
        // it is a character without visible form like the others, which the lookups see through; where it kept two
        // marks apart, the substitutions see it between them.
        for (var i = 1; i + 1 < info.Length; i++)
        {
            if (info[i].CodePoint == CombiningGraphemeJoiner && info[i + 1].CombiningClass != 0 && info[i - 1].CombiningClass > info[i + 1].CombiningClass)
            {
                info[i].Flags |= GlyphInfo.Hidden;
            }
        }

        if (shaper == ScriptShaperKind.Hangul)
            return;

        // Compose a character and a mark when the font has the composed character and no other mark comes between.
        var starter = -1;
        var lastClass = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            ref var current = ref buffer.Info[i];
            var isMark = current.Category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
            // Indic: the parts of a matra are not composed again, and one letter Unicode never composes is.
            if (shaper is ScriptShaperKind.Indic or ScriptShaperKind.Khmer or ScriptShaperKind.Universal && starter >= 0 && IsMark(buffer.Info[starter].Category))
            {
                isMark = false;
            }

            var composite = 0;
            var isIndicLetter = shaper == ScriptShaperKind.Indic && starter >= 0 && buffer.Info[starter].CodePoint == 0x09AF && current.CodePoint == 0x09BC;
            if (isIndicLetter)
            {
                composite = 0x09DF;
            }

            if (starter >= 0 && isMark && (i == starter + 1 || lastClass < current.CombiningClass)
                && (isIndicLetter || UnicodeNormalization.TryComposeCanonical(buffer.Info[starter].CodePoint, current.CodePoint, out composite)
                    || (composesPresentationForms && HebrewShaper.TryCompose(buffer.Info[starter].CodePoint, current.CodePoint, out composite)))
                && face.MapCodePoint(composite) != 0)
            {
                buffer.MergeClusters(starter, i + 1);
                var cluster = buffer.Info[starter].Cluster;
                SetCharacterProperties(ref buffer.Info[starter], composite);
                buffer.Info[starter].Cluster = cluster;
                buffer.Resize(i, 1, 0);
                i--;
                continue;
            }

            if (current.CombiningClass == 0)
            {
                starter = i;
                lastClass = 0;
            }
            else
            {
                lastClass = current.CombiningClass;
            }
        }
    }

    private static bool IsMark(UnicodeCategory category) => category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    /// <summary>Decomposes a character into characters the font has; returns their number, or 0 when the font lacks one of them.</summary>
    /// <param name="face">The font.</param>
    /// <param name="codePoint">The character.</param>
    /// <param name="parts">Receives the characters.</param>
    /// <param name="depth">The number of decompositions that led to the character.</param>
    /// <param name="isComplete">
    /// <see langword="true"/> to decompose as far as the font has the parts; <see langword="false"/> to stop at the
    /// first character the font has.
    /// </param>
    /// <param name="shaper">The shaping model of the script, which can have decompositions of its own.</param>
    private static int Decompose(FontFace face, int codePoint, Span<int> parts, int depth, bool isComplete, ScriptShaperKind shaper)
    {
        if (depth > UnicodeNormalization.MaxCanonicalDecompositionLength)
            return 0;

        // Khmer: a vowel with two parts is the vowel drawn before the syllable and itself.
        if (!(shaper == ScriptShaperKind.Khmer && depth == 0 && KhmerShaper.TryDecompose(codePoint, out var first, out var second))
            && !UnicodeNormalization.TryDecomposeCanonical(codePoint, out first, out second))
        {
            return 0;
        }

        if (second >= 0 && face.MapCodePoint(second) == 0)
            return 0;

        // The first part can be decomposed again.
        var count = !isComplete && face.MapCodePoint(first) != 0 ? 0 : Decompose(face, first, parts, depth + 1, isComplete, shaper);
        if (count == 0)
        {
            if (face.MapCodePoint(first) == 0)
                return 0;

            parts[count++] = first;
        }

        if (second >= 0)
        {
            parts[count++] = second;
        }

        return count;
    }

    /// <summary>
    /// Replaces a character the font does not have by one that stands for it: the hyphen for the non-breaking hyphen,
    /// the space for the spaces of a fixed width, whose width is then set from the size of the font.
    /// </summary>
    private static void ReplaceMissingCharacter(ref GlyphInfo info, FontFace face)
    {
        if (info.CodePoint == 0x2011 && face.MapCodePoint(0x2010) != 0)
        {
            info.Glyph = 0x2010;
            return;
        }

        var width = GetSpaceKind(info.CodePoint);
        if (width != 0 && face.MapCodePoint(' ') != 0)
        {
            info.Glyph = ' ';
            info.ShaperData = (byte)width;
        }
    }

    // The widths of the spaces: a fraction of the em, or the width of another character.
    private const int NeedsMirroring = 0xFF;
    private const int SpaceOfSpace = 1;
    private const int SpaceEm = 2;
    private const int SpaceEm2 = 3;
    private const int SpaceEm3 = 4;
    private const int SpaceEm4 = 5;
    private const int SpaceEm6 = 6;
    private const int SpaceEm16 = 7;
    private const int SpaceFourEm18 = 8;
    private const int SpaceFigure = 9;
    private const int SpacePunctuation = 10;
    private const int SpaceNarrow = 11;
    private const int SpaceEm5 = 12;

    /// <summary>
    /// Determines whether a space character is drawn with the space of a font that has no glyph for it, with a width
    /// of its own (a fraction of the em, the width of a digit...): the spaces of the General Punctuation block, the
    /// no-break spaces and the ideographic space.
    /// </summary>
    public static bool IsSynthesizedSpace(int codePoint) => GetSpaceKind(codePoint) != 0;

    private static int GetSpaceKind(int codePoint)
    {
        return codePoint switch
        {
            0x00A0 => SpaceOfSpace,
            0x2000 or 0x2002 => SpaceEm2,
            0x2001 or 0x2003 or 0x3000 => SpaceEm,
            0x2004 => SpaceEm3,
            0x2005 => SpaceEm4,
            0x2006 => SpaceEm6,
            0x2007 => SpaceFigure,
            0x2008 => SpacePunctuation,
            0x2009 => SpaceEm5,
            0x200A => SpaceEm16,
            0x202F => SpaceNarrow,
            0x205F => SpaceFourEm18,
            _ => 0,
        };
    }

    private static int GetSpaceAdvance(FontFace face, int kind, bool isVertical = false)
    {
        var em = face.UnitsPerEm;
        var space = isVertical ? face.GetAdvanceHeight(face.MapCodePoint(' ')) : face.GetAdvanceWidth(face.MapCodePoint(' '));
        switch (kind)
        {
            case SpaceEm:
                return em;

            case SpaceEm2:
                return (em + 1) / 2;

            case SpaceEm3:
                return (em + 1) / 3;

            case SpaceEm4:
                return (em + 2) / 4;

            case SpaceEm5:
                return (em + 2) / 5;

            case SpaceEm6:
                return (em + 3) / 6;

            case SpaceEm16:
                return (em + 8) / 16;

            case SpaceFourEm18:
                return (int)(((long)em * 4) / 18);

            case SpaceFigure:
                // The width of the digits, which are all as wide in the fonts that have this space.
                for (var digit = '0'; digit <= '9'; digit++)
                {
                    var glyph = face.MapCodePoint(digit);
                    if (glyph != 0)
                        return (isVertical ? face.GetAdvanceHeight(glyph) : face.GetAdvanceWidth(glyph));
                }

                return space;

            case SpacePunctuation:
                {
                    var glyph = face.MapCodePoint('.');
                    glyph = glyph != 0 ? glyph : face.MapCodePoint(',');
                    return glyph != 0 ? (isVertical ? face.GetAdvanceHeight(glyph) : face.GetAdvanceWidth(glyph)) : space;
                }

            case SpaceNarrow:
                return space / 2;

            default:
                return space;
        }
    }

    /// <summary>
    /// Gives each character its glyph. A variation selector chooses the glyph of the character it follows when the
    /// font has the sequence.
    /// </summary>
    /// <remarks>
    /// OpenType specification, "cmap", format 14: a font supports a variation sequence with a glyph of its own or with
    /// the glyph of the character. In both cases the selector is part of the character and has no glyph: it leaves
    /// the buffer, and its cluster is the one of the character. A selector of a sequence the font does not have stays,
    /// as a character without visible form.
    /// </remarks>
    private static void MapGlyphs(GlyphBuffer buffer, FontFace face)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var info = buffer.Info;
            var codePoint = info[i].Glyph;
            if (i + 1 < info.Length && IsVariationSelector(info[i + 1].CodePoint))
            {
                var variation = face.MapVariation(codePoint, info[i + 1].CodePoint, out var glyph);
                if (variation != CmapVariation.NotFound)
                {
                    info[i].Glyph = variation == CmapVariation.Glyph ? glyph : face.MapCodePoint(codePoint);
                    buffer.Delete(i + 1);
                    continue;
                }
            }

            info[i].Glyph = face.MapCodePoint(codePoint);
        }
    }

    private static bool IsVariationSelector(int codePoint) => codePoint is (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0100 and <= 0xE01EF) or (>= 0x180B and <= 0x180D) or 0x180F;

    /// <summary>
    /// Gives each glyph the mask of the features that apply to it: all of them, and around a fraction slash the
    /// features that make the digits before it a numerator and the digits after it a denominator.
    /// </summary>
    private static void SetMasks(GlyphBuffer buffer, FeaturePlan plan, bool hasFractions, ScriptShaperKind shaper, UnicodeScript script, bool isVertical)
    {
        var info = buffer.Info;
        plan.SetMasks(info);
        if (shaper == ScriptShaperKind.Arabic)
        {
            ArabicShaper.SetFormMasks(buffer, FirstShaperBit);
        }
        else if (shaper == ScriptShaperKind.Hangul)
        {
            HangulShaper.SetMasks(info, FirstShaperBit, isVertical);
        }
        else if (shaper == ScriptShaperKind.Indic)
        {
            IndicShaper.FindSyllables(info);
        }
        else if (shaper == ScriptShaperKind.Khmer)
        {
            KhmerShaper.FindSyllables(info);
        }
        else if (shaper == ScriptShaperKind.Myanmar)
        {
            MyanmarShaper.FindSyllables(info);
        }
        else if (shaper == ScriptShaperKind.Universal)
        {
            UniversalShaper.FindSyllables(info);
            UniversalShaper.SetMasks(buffer, FirstShaperBit, script);
        }

        // The model of a script shapes a syllable as a whole: its glyphs are reordered and ligated together.
        if (shaper is ScriptShaperKind.Indic or ScriptShaperKind.Khmer or ScriptShaperKind.Myanmar or ScriptShaperKind.Universal)
        {
            for (var start = 0; start < info.Length;)
            {
                var end = start + 1;
                while (end < info.Length && info[end].Syllable == info[start].Syllable)
                {
                    end++;
                }

                buffer.MarkUnsafeToBreak(start, end);
                start = end;
            }
        }

        foreach (ref var glyph in info)
        {
            if (glyph.ShaperData == NeedsMirroring)
            {
                glyph.Mask |= MirrorMask;
                glyph.ShaperData = 0;
            }
        }

        if (!hasFractions)
            return;

        for (var i = 0; i < info.Length; i++)
        {
            if (info[i].CodePoint != FractionSlash)
                continue;

            var start = i;
            while (start > 0 && info[start - 1].Category == UnicodeCategory.DecimalDigitNumber)
            {
                start--;
            }

            var end = i + 1;
            while (end < info.Length && info[end].Category == UnicodeCategory.DecimalDigitNumber)
            {
                end++;
            }

            // A fraction has digits on both sides of the slash.
            if (start == i || end == i + 1)
                continue;

            for (var j = start; j < i; j++)
            {
                info[j].Mask |= NumeratorMask | FractionMask;
            }

            info[i].Mask |= FractionMask;
            for (var j = i + 1; j < end; j++)
            {
                info[j].Mask |= FractionMask | DenominatorMask;
            }

            i = end - 1;
        }
    }

    /// <summary>
    /// Gets the lookups of a table to apply to a run, in the order of the lookup list: those of the feature the
    /// language system requires and of each feature that is on.
    /// </summary>
    private static List<PlannedLookup> CollectLookups(
        LayoutTable table,
        ReadOnlySpan<byte> data,
        ReadOnlySpan<uint> scripts,
        ReadOnlySpan<uint> languages,
        bool isRightToLeft,
        bool isVertical,
        FeaturePlan plan,
        ShaperPlan shaper,
        ReadOnlySpan<int> coordinates,
        out bool hasFractions,
        out bool hasKerning)
    {
        hasFractions = false;
        hasKerning = false;
        var lookups = new List<PlannedLookup>();
        var system = table.FindLanguageSystem(data, scripts, languages);

        // The features an instance of a variable font replaces. The conditions are also tested for the default
        // instance: a range can contain it.
        var substitutions = table.FindFeatureSubstitutions(data, coordinates);
        if (system == 0)
        {
            AddOrientationForms(table, data, isVertical, listed: [], plan, shaper, lookups, substitutions);
            return lookups;
        }

        // The feature the language system requires is applied with the features of its name, or else first.
        var required = LayoutTable.GetRequiredFeature(data, system);
        if (required != LayoutTable.NoFeature && table.GetFeatureTag(data, required) is not 0 and var requiredTag)
        {
            var stage = GetStage(requiredTag, plan, shaper, isRightToLeft, isVertical, out var flags, out _);
            AddLookups(table, data, required, GlobalMask, lookups, substitutions, stage < 0 ? 0 : stage, flags);
        }

        // The fraction features need each other: a numerator without a fraction bar is not a fraction.
        var count = LayoutTable.GetFeatureCount(data, system);
        var hasFrac = false;
        var hasNumr = false;
        var hasDnom = false;
        Span<bool> listed = stackalloc bool[2];
        var orientationFeatures = isVertical ? UprightFormFeatures : SidewaysFormFeatures;
        for (var i = 0; i < count; i++)
        {
            var tag = table.GetFeatureTag(data, LayoutTable.GetFeatureIndex(data, system, i));
            hasFrac |= tag == Frac;
            hasNumr |= tag == Numr;
            hasDnom |= tag == Dnom;
            var orientation = orientationFeatures.AsSpan().IndexOf(tag);
            if (orientation >= 0)
            {
                listed[orientation] = true;
            }
        }

        AddOrientationForms(table, data, isVertical, listed, plan, shaper, lookups, substitutions);

        hasFractions = hasFrac || (hasNumr && hasDnom);
        for (var i = 0; i < count; i++)
        {
            var feature = LayoutTable.GetFeatureIndex(data, system, i);
            var tag = table.GetFeatureTag(data, feature);
            if (tag == 0)
                continue;

            var stage = GetStage(tag, plan, shaper, isRightToLeft, isVertical, out var flags, out var shaperMask);
            if (plan.TryGetMask(tag, out var mask))
            {
                // A feature the caller names: it applies where its value is not zero.
                if (mask != 0)
                {
                    hasKerning |= tag == Kern;
                    AddLookups(table, data, feature, mask, lookups, substitutions, stage, flags);
                }
            }
            else if (tag == Frac || tag == Numr || tag == Dnom)
            {
                if (hasFractions)
                {
                    AddLookups(table, data, feature, tag == Frac ? FractionMask : tag == Numr ? NumeratorMask : DenominatorMask, lookups, substitutions, stage, flags);
                }
            }
            else if (isRightToLeft && !isVertical && tag == Rtlm)
            {
                AddLookups(table, data, feature, MirrorMask, lookups, substitutions, stage, flags);
            }
            else if (stage >= 0)
            {
                hasKerning |= tag == Kern;
                AddLookups(table, data, feature, shaperMask, lookups, substitutions, stage, flags);
            }
        }

        // The lookups of a stage are applied in the order of the lookup list, each one once: a lookup of several
        // features applies where any of them does.
        lookups.Sort(static (left, right) => left.Stage != right.Stage ? left.Stage.CompareTo(right.Stage) : left.Index.CompareTo(right.Index));
        var unique = 0;
        for (var i = 0; i < lookups.Count; i++)
        {
            if (unique > 0 && lookups[unique - 1].Stage == lookups[i].Stage && lookups[unique - 1].Index == lookups[i].Index)
            {
                lookups[unique - 1] = lookups[unique - 1] with { Mask = lookups[unique - 1].Mask | lookups[i].Mask, Flags = lookups[unique - 1].Flags | lookups[i].Flags };
            }
            else
            {
                lookups[unique++] = lookups[i];
            }
        }

        lookups.RemoveRange(unique, lookups.Count - unique);
        return lookups;
    }

    /// <summary>Gets the stage a feature is applied in, or -1 for a feature that is only applied when the caller asks for it.</summary>
    /// <param name="tag">The tag of the feature.</param>
    /// <param name="plan">The features the caller sets.</param>
    /// <param name="shaper">The features of the shaping model of the script.</param>
    /// <param name="isRightToLeft">The direction of the run.</param>
    /// <param name="isVertical">Whether the run is a vertical line of upright glyphs.</param>
    /// <param name="flags">How the feature is applied.</param>
    /// <param name="mask">The bits of the glyphs the feature applies to, when the caller does not set it.</param>
    private static int GetStage(uint tag, FeaturePlan plan, ShaperPlan shaper, bool isRightToLeft, bool isVertical, out ShaperFeatureFlags flags, out uint mask)
    {
        flags = ShaperFeatureFlags.None;
        mask = GlobalMask;
        if (tag == Rvrn)
            return 0;

        if (shaper.TryGet(tag, out var feature, out var perGlyphIndex))
        {
            flags = feature.Flags;
            if (flags.HasFlag(ShaperFeatureFlags.PerGlyph))
            {
                mask = 1u << (FirstShaperBit + perGlyphIndex);
            }

            return feature.Stage;
        }

        if (tag == Frac || tag == Numr || tag == Dnom || (!isVertical && (isRightToLeft ? RightToLeftFeatures : LeftToRightFeatures).AsSpan().Contains(tag)))
            return 1;

        // A zero width joiner between a letter and a mark keeps the mark from being placed on the letter.
        if (MarkFeatures.AsSpan().Contains(tag))
        {
            flags = ShaperFeatureFlags.ManualZeroWidthJoiner | ShaperFeatureFlags.ManualZeroWidthNonJoiner;
        }

        if (plan.TryGetMask(tag, out _))
            return shaper.FinalStage;

        return (isVertical ? VerticalFeatures : DefaultFeatures).AsSpan().Contains(tag) && !shaper.IsDisabled(tag) ? shaper.FinalStage : -1;
    }

    /// <summary>
    /// Adds the lookups of the forms of a vertical line that a font does not list for the script and the language of
    /// a run: the first feature of that name in the font, whatever lists it. Fonts list them under a few scripts only,
    /// and a vertical line has characters of any of them. An upright run has the vertical forms (<c>vert</c>, applied
    /// by default) and the rotated forms (<c>vrt2</c>, when the caller asks for them); a horizontal run, the forms of
    /// the glyphs that are turned on their side in a vertical line (<c>vrtr</c>, when the caller asks for them).
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="data">The data of the table.</param>
    /// <param name="isVertical">Whether the run is a vertical line of upright glyphs.</param>
    /// <param name="listed">Whether the language system of the run lists each of these features; empty when the run has no language system.</param>
    /// <param name="plan">The features the caller sets.</param>
    /// <param name="shaper">The features of the shaping model of the script.</param>
    /// <param name="lookups">The lookups of the run.</param>
    /// <param name="substitutions">The features an instance of a variable font replaces.</param>
    private static void AddOrientationForms(LayoutTable table, ReadOnlySpan<byte> data, bool isVertical, ReadOnlySpan<bool> listed, FeaturePlan plan, ShaperPlan shaper, List<PlannedLookup> lookups, int substitutions)
    {
        var tags = isVertical ? UprightFormFeatures : SidewaysFormFeatures;
        for (var i = 0; i < tags.Length; i++)
        {
            if (i < listed.Length && listed[i])
                continue;

            // The vertical forms are applied by default, the other ones when the caller asks for them
            var tag = tags[i];
            var mask = tag == Vert ? GlobalMask : 0;
            if (plan.TryGetMask(tag, out var callerMask))
            {
                mask = callerMask;
            }

            if (mask == 0)
                continue;

            var feature = table.FindFeature(data, tag);
            if (feature != LayoutTable.NoFeature)
            {
                AddLookups(table, data, feature, mask, lookups, substitutions, shaper.FinalStage, ShaperFeatureFlags.None);
            }
        }
    }

    private static void AddLookups(LayoutTable table, ReadOnlySpan<byte> data, int feature, uint mask, List<PlannedLookup> lookups, int substitutions, int stage, ShaperFeatureFlags flags)
    {
        var count = table.GetFeatureLookupCount(data, feature, substitutions);
        var lookupCount = table.GetLookupCount(data);
        for (var i = 0; i < count; i++)
        {
            // A feature can name a lookup the table does not have: it is ignored.
            var lookup = table.GetFeatureLookup(data, feature, i, substitutions);
            if (lookup < lookupCount)
            {
                lookups.Add(new PlannedLookup(stage, lookup, mask, flags));
            }
        }
    }

    /// <summary>
    /// Makes the glyphs of the characters without visible form invisible: they become the glyph of the space, without
    /// advance, or are removed when the font has no space.
    /// </summary>
    private static void HideIgnorables(GlyphBuffer buffer, FontFace face)
    {
        var space = face.MapCodePoint(' ');
        for (var i = buffer.Length - 1; i >= 0; i--)
        {
            ref var info = ref buffer.Info[i];
            if (!info.IsIgnorable)
                continue;

            if (space != 0)
            {
                info.Glyph = space;
            }
            else
            {
                buffer.Delete(i);
            }
        }
    }

    /// <summary>Applies the pair adjustments of the legacy kerning table between each glyph and the next visible glyph that is not a mark.</summary>
    private static void ApplyLegacyKerning(GlyphBuffer buffer, FontFace face, bool isRightToLeft)
    {
        var info = buffer.Info;
        var positions = buffer.Positions;
        var previous = -1;
        for (var i = 0; i < info.Length; i++)
        {
            // The glyphs of a pair are the visible ones: marks and invisible glyphs do not come between them.
            if (info[i].IsMark || info[i].IsIgnorable)
                continue;

            if (previous >= 0)
            {
                // Half of the adjustment is taken from the advance of the first glyph, and the other half from the
                // second one, which is moved back by it: the two glyphs are as far apart as the font asks, and the
                // boundary between them stays between them.
                // The pairs of the table are a glyph and the glyph on its right: in a right-to-left run, the glyph that
                // comes later in the text is the one on the left.
                var adjustment = isRightToLeft ? face.GetLegacyKerning(info[i].Glyph, info[previous].Glyph) : face.GetLegacyKerning(info[previous].Glyph, info[i].Glyph);
                var first = adjustment >> 1;
                var second = adjustment - first;
                positions[previous].XAdvance += first;
                positions[i].XAdvance += second;
                positions[i].XOffset += second;
                if (adjustment != 0)
                {
                    buffer.MarkUnsafeToBreak(previous, i + 1);
                }
            }

            previous = i;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct PlannedLookup(int Stage, int Index, uint Mask, ShaperFeatureFlags Flags);

    /// <summary>
    /// The features the caller sets, and where the mask of a glyph holds the value of each one. A feature that is on
    /// for the whole run shares the bit of the features every glyph has; a feature with a value per range of
    /// characters, or with a value above 1 (the choice of an alternate), has bits of its own.
    /// </summary>
    internal sealed class FeaturePlan
    {
        private readonly List<(uint Tag, uint Mask)> _masks = [];
        private readonly ShapingFeature[] _features;

        /// <param name="features">The features the caller sets.</param>
        /// <param name="firstBit">The first bit after the ones of the default features, of the fractions and of the shaping model.</param>
        public FeaturePlan(ReadOnlySpan<ShapingFeature> features, int firstBit)
        {
            _features = features.ToArray();
            var shift = firstBit;
            foreach (var feature in _features)
            {
                if (TryGetMask(feature.Tag, out _))
                    continue;

                // The largest value of the feature, and whether it is the same for every character.
                var isGlobal = true;
                var maximum = 0;
                var last = 0;
                foreach (var setting in _features)
                {
                    if (setting.Tag != feature.Tag)
                        continue;

                    isGlobal &= setting.IsGlobal;
                    maximum = Math.Max(maximum, setting.Value);
                    last = setting.Value;
                }

                var bits = 32 - BitOperations.LeadingZeroCount((uint)Math.Max(maximum, 1));
                if ((isGlobal && last <= 1) || shift + bits > 32)
                {
                    // On or off for the whole run. A feature that no bit is left for is on where its last value is.
                    _masks.Add((feature.Tag, last > 0 ? GlobalMask : 0));
                }
                else
                {
                    _masks.Add((feature.Tag, ((1u << bits) - 1) << shift));
                    shift += bits;
                }
            }
        }

        /// <summary>Gets the bits that hold the value of a feature the caller sets; 0 for a feature that is off.</summary>
        /// <returns><see langword="false"/> when the caller does not set the feature.</returns>
        public bool TryGetMask(uint tag, out uint mask)
        {
            foreach (var entry in _masks)
            {
                if (entry.Tag == tag)
                {
                    mask = entry.Mask;
                    return true;
                }
            }

            mask = 0;
            return false;
        }

        /// <summary>Gives each glyph the bit of the features of every glyph and the value of each feature at its character.</summary>
        public void SetMasks(Span<GlyphInfo> info)
        {
            foreach (ref var glyph in info)
            {
                glyph.Mask = GlobalMask;
            }

            // In the order they were given: a later setting replaces an earlier one where both apply. A default feature
            // that is only turned off for a range stays on elsewhere.
            foreach (var entry in _masks)
            {
                if (entry.Mask is 0 or GlobalMask)
                    continue;

                var shift = BitOperations.TrailingZeroCount(entry.Mask);
                var isDefault = DefaultFeatures.AsSpan().Contains(entry.Tag);
                foreach (ref var glyph in info)
                {
                    var value = isDefault ? 1u : 0u;
                    foreach (var setting in _features)
                    {
                        if (setting.Tag == entry.Tag && glyph.Cluster >= setting.Start && glyph.Cluster < setting.End)
                        {
                            value = (uint)Math.Max(setting.Value, 0);
                        }
                    }

                    glyph.Mask |= (value << shift) & entry.Mask;
                }
            }
        }
    }
}
