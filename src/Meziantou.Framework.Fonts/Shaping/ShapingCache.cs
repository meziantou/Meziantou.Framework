namespace Meziantou.Framework.Fonts;

/// <summary>
/// What shaping computes from a font alone, kept for the runs of one layout: the glyphs each lookup of its layout tables
/// may apply at, the plan of the runs of each font and settings, and the advances a variable font computes from the
/// outlines of its glyphs. A font has no cache of its own; this one lives as long as the engine that lays out a text.
/// </summary>
internal sealed class ShapingCache
{
    private readonly Dictionary<FontFace, (LookupDigests? Substitutions, LookupDigests? Positions)> _digests = [];
    private readonly Dictionary<FontFace, List<TextShaper.Plan>> _plans = [];
    private readonly Dictionary<(FontFace Face, int Glyph), int> _advances = [];
    private LayoutApplier? _applier;

    /// <summary>Gets the applier of the lookups of a run, kept from a run to the next one.</summary>
    public LayoutApplier GetApplier(GlyphBuffer buffer, FontFace face)
    {
        if (_applier is null)
        {
            _applier = new LayoutApplier(buffer, face);
        }
        else
        {
            _applier.Reset(buffer, face);
        }

        return _applier;
    }

    /// <summary>
    /// Gets the advance width of a glyph: kept for a font whose advances come from the outlines of its glyphs, where
    /// each one decodes the variations of the glyph.
    /// </summary>
    public int GetAdvanceWidth(FontFace face, int glyph)
    {
        if (!face.VariesAdvancesWithOutlines)
            return face.GetAdvanceWidth(glyph);

        if (!_advances.TryGetValue((face, glyph), out var advance))
        {
            advance = face.GetAdvanceWidth(glyph);
            _advances.Add((face, glyph), advance);
        }

        return advance;
    }

    /// <summary>Gets the plan of the runs of a font shaped with settings: a new one, not complete, for the first run.</summary>
    public TextShaper.Plan GetPlan(FontFace face, UnicodeScript script, ReadOnlySpan<uint> languages, bool isRightToLeft, bool isVertical, ReadOnlySpan<ShapingFeature> features)
    {
        if (!_plans.TryGetValue(face, out var plans))
        {
            plans = [];
            _plans.Add(face, plans);
        }

        foreach (var plan in plans)
        {
            if (plan.Matches(script, languages, isRightToLeft, isVertical, features))
                return plan;
        }

        var created = new TextShaper.Plan { Script = script, Languages = languages.ToArray(), IsRightToLeft = isRightToLeft, IsVertical = isVertical, FeatureSettings = features.ToArray() };
        plans.Add(created);
        return created;
    }

    /// <summary>Gets the digests of the lookups of the GSUB or GPOS table of a font, which has the table.</summary>
    public LookupDigests GetDigests(FontFace face, bool isPositioning)
    {
        _digests.TryGetValue(face, out var entry);
        var digests = isPositioning ? entry.Positions : entry.Substitutions;
        if (digests is null)
        {
            digests = Create(face, isPositioning);
            _digests[face] = isPositioning ? (entry.Substitutions, digests) : (digests, entry.Positions);
        }

        return digests;
    }

    /// <summary>Creates the digests of the lookups of the GSUB or GPOS table of a font, which has the table.</summary>
    public static LookupDigests Create(FontFace face, bool isPositioning)
        => isPositioning ? new LookupDigests(face.Gpos!, face.GposData, isPositioning: true) : new LookupDigests(face.Gsub!, face.GsubData, isPositioning: false);
}
