namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The features of a shaping model, in stages, before the features every script has.</summary>
internal sealed class ShaperPlan
{
    /// <summary>The plan of the scripts without a shaping model: every feature is in one stage.</summary>
    public static ShaperPlan Default { get; } = new([], finalStage: 1);

    private readonly ShaperFeature[] _features;

    public ShaperPlan(ShaperFeature[] features, int finalStage)
    {
        _features = features;
        FinalStage = finalStage;
        foreach (var feature in features)
        {
            if (feature.Flags.HasFlag(ShaperFeatureFlags.PerGlyph))
            {
                PerGlyphCount++;
            }
        }
    }

    /// <summary>
    /// Gets the stage of the features every script has and of the features the caller asks for: the last one. The
    /// stage 0 is the one of the features that replace the glyphs of an instance of a variable font, and the stage 1
    /// the one of the features of the direction and of the fractions.
    /// </summary>
    public int FinalStage { get; }

    /// <summary>Gets the number of features that apply to the glyphs the analysis of the text chooses.</summary>
    public int PerGlyphCount { get; }

    /// <summary>Finds a feature of the model.</summary>
    /// <param name="tag">The tag of the feature.</param>
    /// <param name="feature">The feature.</param>
    /// <param name="perGlyphIndex">The rank of the feature among the ones that have <see cref="ShaperFeatureFlags.PerGlyph"/>.</param>
    public bool TryGet(uint tag, out ShaperFeature feature, out int perGlyphIndex)
    {
        perGlyphIndex = 0;
        foreach (var candidate in _features)
        {
            if (candidate.Tag == tag)
            {
                feature = candidate;
                return true;
            }

            if (candidate.Flags.HasFlag(ShaperFeatureFlags.PerGlyph))
            {
                perGlyphIndex++;
            }
        }

        feature = default;
        return false;
    }
}
