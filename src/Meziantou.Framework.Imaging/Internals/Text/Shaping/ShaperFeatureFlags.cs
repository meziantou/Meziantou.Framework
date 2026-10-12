namespace Meziantou.Framework.Imaging.Internals;

/// <summary>How a feature a shaping model names is applied.</summary>
[Flags]
internal enum ShaperFeatureFlags
{
    None = 0,

    /// <summary>The feature applies to the glyphs the analysis of the text chooses, not to the whole run.</summary>
    PerGlyph = 1,

    /// <summary>
    /// A zero width joiner between two glyphs separates the components of a ligature of the feature, as the zero width
    /// non-joiner does for every feature.
    /// </summary>
    ManualZeroWidthJoiner = 2,

    /// <summary>The sequences of the contextual lookups of the feature do not see through the zero width non-joiner.</summary>
    ManualZeroWidthNonJoiner = 4,

    /// <summary>The glyphs the feature puts in place of one are the parts of a glyph that stretches.</summary>
    Stretching = 8,
}
