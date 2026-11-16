namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>The range decoder a <see cref="Ppmd7Model"/> decodes with (IPpmd7_RangeDec of 7-Zip).</summary>
internal interface IPpmd7RangeDecoder
{
    /// <summary>Divides the range by <paramref name="total"/> and returns the count of the current code in it.</summary>
    uint GetThreshold(uint total);

    /// <summary>Removes the interval [<paramref name="start"/>, <paramref name="start"/> + <paramref name="size"/>) of the last threshold.</summary>
    void Decode(uint start, uint size);

    /// <summary>Decodes a binary symbol whose probability of 0 is <paramref name="size0"/> / 2^14.</summary>
    uint DecodeBit(uint size0);
}
