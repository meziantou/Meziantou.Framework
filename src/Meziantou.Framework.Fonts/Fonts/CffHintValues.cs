namespace Meziantou.Framework.Fonts;

/// <summary>
/// The hinting values of a private DICT of a CFF or CFF2 table: Adobe Technical Note #5176, table 23, "Private DICT
/// Operators", with the meanings of the Type 1 font format (Adobe Type 1 Font Format, chapter 5, "Private Dictionary").
/// </summary>
/// <remarks>
/// The arrays are absolute values, the DICT giving each one as a difference with the one before it. In a CFF2 table,
/// the values of an instance of a variable font are blended (<c>blend</c>); they are not rounded.
/// </remarks>
internal sealed class CffHintValues
{
    /// <summary>The default value of <see cref="BlueScale"/>.</summary>
    public const double DefaultBlueScale = 0.039625;

    /// <summary>Gets the values of a private DICT without hinting values.</summary>
    public static CffHintValues Empty { get; } = new();

    /// <summary>
    /// Gets the alignment zones of the baseline and of the tops of the glyphs: pairs of a bottom and a top. The first
    /// pair is the zone of the baseline, whose flat edge is its top; the others are zones of tops, whose flat edge is
    /// their bottom.
    /// </summary>
    public double[] BlueValues { get; init; } = [];

    /// <summary>Gets the alignment zones of the bottoms of the descenders: pairs whose flat edge is their top.</summary>
    public double[] OtherBlues { get; init; } = [];

    /// <summary>Gets the zones of <see cref="BlueValues"/> of the family of the font.</summary>
    public double[] FamilyBlues { get; init; } = [];

    /// <summary>Gets the zones of <see cref="OtherBlues"/> of the family of the font.</summary>
    public double[] FamilyOtherBlues { get; init; } = [];

    /// <summary>Gets the number of pixels per unit under which the overshoots of the zones are suppressed (aligned with their flat edge).</summary>
    public double BlueScale { get; init; } = DefaultBlueScale;

    /// <summary>Gets the overshoot, in font units, that is at least one pixel once the overshoots are no longer suppressed.</summary>
    public double BlueShift { get; init; } = 7;

    /// <summary>Gets the distance, in font units, by which the zones extend on both sides for a stem edge to be in them.</summary>
    public double BlueFuzz { get; init; } = 1;

    /// <summary>Gets the dominant width of the horizontal stems, in font units; 0 when the DICT does not give it.</summary>
    public double StdHW { get; init; }

    /// <summary>Gets the dominant width of the vertical stems, in font units; 0 when the DICT does not give it.</summary>
    public double StdVW { get; init; }

    /// <summary>Gets the widths of the horizontal stems the font uses most, in increasing order.</summary>
    public double[] StemSnapH { get; init; } = [];

    /// <summary>Gets the widths of the vertical stems the font uses most, in increasing order.</summary>
    public double[] StemSnapV { get; init; } = [];
}
