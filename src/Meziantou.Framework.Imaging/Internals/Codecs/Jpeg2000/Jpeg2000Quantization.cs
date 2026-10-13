namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A QCD or QCC marker segment (T.800, A.6.4 and A.6.5): the quantization of the sub-bands of a component.</summary>
internal sealed class Jpeg2000Quantization
{
    /// <summary>No quantization: the reversible transformation, only exponents are signalled.</summary>
    public const int StyleNone = 0;

    /// <summary>Scalar derived: the values of the LL sub-band are signalled, the others are derived from them (equation E-5).</summary>
    public const int StyleDerived = 1;

    /// <summary>Scalar expounded: the values of every sub-band are signalled.</summary>
    public const int StyleExpounded = 2;

    private readonly byte[] _exponents;
    private readonly ushort[] _mantissas;

    public Jpeg2000Quantization(int style, int guardBits, byte[] exponents, ushort[] mantissas)
    {
        Style = style;
        GuardBits = guardBits;
        _exponents = exponents;
        _mantissas = mantissas;
    }

    /// <summary>Gets the quantization style, one of the <c>Style</c> constants.</summary>
    public int Style { get; }

    /// <summary>Gets the number of guard bits, 0 to 7.</summary>
    public int GuardBits { get; }

    /// <summary>Gets the number of sub-bands whose values are signalled.</summary>
    public int SignalledBandCount => _exponents.Length;

    /// <summary>Determines whether the marker segment signals the sub-bands of a component with <paramref name="decompositionLevels"/> levels.</summary>
    public bool Covers(int decompositionLevels) => Style == StyleDerived || SignalledBandCount >= (3 * decompositionLevels) + 1;

    /// <summary>
    /// Gets the exponent and the mantissa of a sub-band.
    /// </summary>
    /// <param name="bandIndex">The index of the sub-band in the order of the marker segment: 0 for the LL sub-band, then the HL, LH and HH sub-bands of each resolution level from the lowest.</param>
    /// <param name="exponent">Receives the exponent; it is negative when a derived exponent is.</param>
    /// <param name="mantissa">Receives the mantissa.</param>
    public void GetStep(int bandIndex, out int exponent, out int mantissa)
    {
        if (Style == StyleDerived)
        {
            // Equation E-5: the exponent decreases with the resolution level, the mantissa is the one of the LL sub-band
            var resolution = bandIndex == 0 ? 0 : ((bandIndex - 1) / 3) + 1;
            exponent = _exponents[0] - Math.Max(0, resolution - 1);
            mantissa = _mantissas[0];
            return;
        }

        exponent = _exponents[bandIndex];
        mantissa = _mantissas[bandIndex];
    }
}
