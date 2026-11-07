namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>A component of a composite glyph.</summary>
internal sealed class FontComponent
{
    public required int GlyphId { get; init; }

    /// <summary>Gets the x offset, or the index of a point of the glyph being built when <see cref="MatchesPoints"/> is set.</summary>
    public int Argument1 { get; init; }

    /// <summary>Gets the y offset, or the index of a point of the component when <see cref="MatchesPoints"/> is set.</summary>
    public int Argument2 { get; init; }

    public bool MatchesPoints { get; init; }

    public double XScale { get; init; } = 1;

    public double YScale { get; init; } = 1;

    public double Scale01 { get; init; }

    public double Scale10 { get; init; }

    public bool UseMyMetrics { get; init; }

    public bool RoundToGrid { get; init; }

    public bool OverlapCompound { get; init; }

    public bool ScaledComponentOffset { get; init; }

    public bool UnscaledComponentOffset { get; init; }

    public bool ForceWordArguments { get; init; }
}
