using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// The expected color description of an AVIF input (<c>ImageMetadata.Cicp</c>): the code points of ITU-T H.273 of its
/// <c>colr</c> box of type <c>nclx</c>, or else of the color description of its AV1 sequence header.
/// </summary>
public sealed class CicpExpectation
{
    /// <summary>Gets the color primaries code point.</summary>
    [JsonPropertyName("colorPrimaries")]
    public required int ColorPrimaries { get; init; }

    /// <summary>Gets the transfer characteristics code point.</summary>
    [JsonPropertyName("transferCharacteristics")]
    public required int TransferCharacteristics { get; init; }

    /// <summary>Gets the matrix coefficients code point.</summary>
    [JsonPropertyName("matrixCoefficients")]
    public required int MatrixCoefficients { get; init; }

    /// <summary>Gets a value indicating whether the samples use the full range.</summary>
    [JsonPropertyName("fullRange")]
    public required bool FullRange { get; init; }

    /// <summary>Returns <c>primaries/transfer/matrix full</c> or <c>primaries/transfer/matrix limited</c>.</summary>
    /// <returns>The description.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{ColorPrimaries}/{TransferCharacteristics}/{MatrixCoefficients} {(FullRange ? "full" : "limited")}");
}
