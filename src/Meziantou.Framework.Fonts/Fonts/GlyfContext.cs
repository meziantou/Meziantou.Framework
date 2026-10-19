namespace Meziantou.Framework.Fonts;

/// <summary>The tables a TrueType outline is read from, and the instance of a variable font it is read for.</summary>
internal readonly ref struct GlyfContext
{
    public ReadOnlySpan<byte> Loca { get; init; }

    public ReadOnlySpan<byte> Glyf { get; init; }

    public ReadOnlySpan<byte> Hmtx { get; init; }

    public bool HasLongLocations { get; init; }

    public int GlyphCount { get; init; }

    /// <summary>Gets the number of glyphs that have their own advance in <see cref="Hmtx"/>.</summary>
    public int MetricCount { get; init; }

    /// <summary>Gets the <c>gvar</c> table, when the outline is read for an instance that is not the default one.</summary>
    public GvarTable? Variations { get; init; }

    public ReadOnlySpan<byte> Gvar { get; init; }

    /// <summary>Gets the normalized coordinates of the instance, as 2.14 fixed-point numbers.</summary>
    public ReadOnlySpan<int> Coordinates { get; init; }
}
