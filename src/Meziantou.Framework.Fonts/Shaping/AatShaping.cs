namespace Meziantou.Framework.Fonts;

/// <summary>
/// What the shaper takes from the tables of Apple Advanced Typography: whether they replace the OpenType Layout tables
/// of a font, and the flags of the chains of the <c>morx</c> table for the features of a run.
/// </summary>
internal static class AatShaping
{
    /// <summary>Determines whether the glyphs of a font are substituted with its <c>morx</c> table rather than its <c>GSUB</c> table.</summary>
    /// <remarks>
    /// The <c>morx</c> table is used whenever the font has one, whatever its substitution table has (the reference
    /// shaper does the same, and the fonts that have both draw the same glyphs with either).
    /// </remarks>
    public static bool UsesMorx(FontFace face) => face.Morx is not null;

    /// <summary>Determines whether the glyphs of a font are kerned with its <c>kerx</c> table rather than its <c>GPOS</c> table.</summary>
    /// <remarks>
    /// The <c>kerx</c> table is used when the font has no positioning table, when its glyphs come from its <c>morx</c>
    /// table, or when it has no substitution table: the positioning table of such a font is not the one it was made
    /// with. These are the choices of the reference shaper, read from fonts that have the tables in every combination.
    /// </remarks>
    public static bool UsesKerx(FontFace face, bool usesMorx) => face.Kerx is not null && (face.Gpos is null || usesMorx || face.Gsub is null);

    /// <summary>Gets the flags of each chain of the <c>morx</c> table of a font for the features of a run.</summary>
    public static uint[] GetChainFlags(FontFace face, ReadOnlySpan<ShapingFeature> features)
    {
        // The settings the features of the caller select. A font says in its feature name table which feature types
        // it has: the features of a type it does not list select nothing. The features applied by default select
        // nothing either: what a font does by default is in the default flags of its chains.
        var selected = new HashSet<(int Type, int Setting)>();
        var feat = face.Feat;
        foreach (var feature in features)
        {
            if (!feature.IsGlobal || feat is null || !AatFeatures.TryGetMapping(feature.Tag, out var mapping))
                continue;

            if (!feat.TryGetFeature(mapping.Type, out _) && (mapping.LegacyType == 0 || !feat.TryGetFeature(mapping.LegacyType, out _)))
                continue;

            var selector = mapping.SelectorIsValue ? feature.Value : feature.Value != 0 ? mapping.Selector : mapping.OffSelector;
            selected.Add((mapping.Type, selector));
            if (mapping.LegacyType != 0 && feature.Value != 0)
            {
                selected.Add((mapping.LegacyType, mapping.LegacySelector));
            }
        }

        var chains = face.Morx!.Chains;
        var flags = new uint[chains.Length];
        for (var i = 0; i < chains.Length; i++)
        {
            flags[i] = MorxTable.GetFlags(chains[i], (type, setting) => selected.Contains((type, setting)));
        }

        return flags;
    }
}
