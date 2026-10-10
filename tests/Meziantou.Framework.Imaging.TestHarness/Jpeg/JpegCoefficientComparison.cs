namespace Meziantou.Framework.Imaging.TestHarness.Jpeg;

/// <summary>The result of <see cref="JpegEncoderModel.Compare"/>.</summary>
/// <param name="Compared">The number of coefficients compared.</param>
/// <param name="NearTies">The coefficients accepted as either neighbor of a rounding boundary.</param>
/// <param name="Mismatches">The coefficients that differ from the model.</param>
/// <param name="FirstMismatch">A description of the first mismatch, or <see langword="null"/>.</param>
public sealed record JpegCoefficientComparison(long Compared, long NearTies, long Mismatches, string? FirstMismatch)
{
    /// <summary>Gets a value indicating whether every coefficient matches.</summary>
    public bool IsMatch => Mismatches == 0;

    /// <summary>Describes the result.</summary>
    /// <returns>The description.</returns>
    public string Describe() => string.Create(CultureInfo.InvariantCulture, $"{Compared} coefficient(s), {NearTies} near tie(s), {Mismatches} mismatch(es). {FirstMismatch}");
}
