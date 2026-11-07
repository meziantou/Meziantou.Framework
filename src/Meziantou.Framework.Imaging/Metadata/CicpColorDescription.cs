namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>
/// An immutable set of coding-independent code points (ITU-T H.273, ISO/IEC 23091-2) that describes how the color samples
/// of an image are to be interpreted: color primaries, transfer characteristics, matrix coefficients and sample range.
/// </summary>
/// <remarks>
/// <para>
/// The color primaries and the transfer characteristics are labels: no operation converts the pixels, and high dynamic
/// range content (for example PQ, 16, or HLG, 18) is decoded to its coded sample values.
/// </para>
/// <para>
/// The matrix coefficients and the range describe the encoded source and are already applied by the decoder: decoded pixels
/// are always full-range R'G'B' or luma samples.
/// </para>
/// <para>
/// Every code point from 0 to 255 is accepted, reserved values included, so that a description round-trips unchanged.
/// </para>
/// </remarks>
public sealed class CicpColorDescription : IEquatable<CicpColorDescription>
{
    /// <summary>Initializes a new instance of the <see cref="CicpColorDescription"/> class.</summary>
    /// <param name="colorPrimaries">The color primaries code point.</param>
    /// <param name="transferCharacteristics">The transfer characteristics code point.</param>
    /// <param name="matrixCoefficients">The matrix coefficients code point of the encoded source.</param>
    /// <param name="fullRange"><see langword="true"/> when the encoded source uses the full sample range.</param>
    public CicpColorDescription(byte colorPrimaries, byte transferCharacteristics, byte matrixCoefficients, bool fullRange)
    {
        ColorPrimaries = colorPrimaries;
        TransferCharacteristics = transferCharacteristics;
        MatrixCoefficients = matrixCoefficients;
        FullRange = fullRange;
    }

    /// <summary>Gets the color primaries code point (for example 1 for BT.709 and sRGB, 9 for BT.2020, 12 for Display P3). A label: never applied.</summary>
    public byte ColorPrimaries { get; }

    /// <summary>Gets the transfer characteristics code point (for example 13 for sRGB, 8 for linear, 16 for PQ, 18 for HLG). A label: never applied.</summary>
    public byte TransferCharacteristics { get; }

    /// <summary>Gets the matrix coefficients code point of the encoded source (for example 0 for identity, 1 for BT.709, 6 for BT.601). Already applied by the decoder.</summary>
    public byte MatrixCoefficients { get; }

    /// <summary>Gets a value indicating whether the encoded source uses the full sample range. Already applied by the decoder: decoded samples are always full range.</summary>
    public bool FullRange { get; }

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] CicpColorDescription? other)
    {
        return other is not null
            && ColorPrimaries == other.ColorPrimaries
            && TransferCharacteristics == other.TransferCharacteristics
            && MatrixCoefficients == other.MatrixCoefficients
            && FullRange == other.FullRange;
    }

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as CicpColorDescription);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(ColorPrimaries, TransferCharacteristics, MatrixCoefficients, FullRange);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{ColorPrimaries}/{TransferCharacteristics}/{MatrixCoefficients} {(FullRange ? "full" : "limited")}");
}
