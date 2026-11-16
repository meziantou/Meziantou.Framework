namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>The range encoder a <see cref="Ppmd7Model"/> encodes with.</summary>
internal interface IPpmd7RangeEncoder
{
    void Encode(uint start, uint size, uint total);

    /// <summary>Encodes a 0 whose probability is <paramref name="size0"/> / 2^14.</summary>
    void EncodeBit0(uint size0);

    /// <summary>Encodes a 1 when the probability of 0 is <paramref name="size0"/> / 2^14.</summary>
    void EncodeBit1(uint size0);
}
