namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A scan order of the coefficients of a transform block (AV1 specification, section 9.2): the position of each coefficient in
/// coding order. The tables of at most 256 positions store bytes, the larger ones 16-bit values.
/// </summary>
internal readonly ref struct Av1Scan
{
    private readonly ReadOnlySpan<byte> _narrow;
    private readonly ReadOnlySpan<ushort> _wide;

    public Av1Scan(ReadOnlySpan<byte> positions) => _narrow = positions;

    public Av1Scan(ReadOnlySpan<ushort> positions) => _wide = positions;

    /// <summary>Gets the position of the coefficient <paramref name="index"/> of the coding order.</summary>
    public int this[int index] => _wide.IsEmpty ? _narrow[index] : _wide[index];
}
