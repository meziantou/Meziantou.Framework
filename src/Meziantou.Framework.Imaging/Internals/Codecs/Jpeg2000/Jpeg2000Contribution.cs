using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What one packet adds to one code-block: the bytes of consecutive coding passes that belong to one codeword segment.
/// The contributions of a code-block form a list, in the order of the packets.
/// </summary>
/// <remarks>
/// The packet reader stores the contributions in pooled memory rented without clearing: it assigns every element as a
/// whole before reading it (<c>AddContribution</c>), so that <see cref="IsSkipped"/> never holds a byte of the pool that
/// is neither 0 nor 1.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct Jpeg2000Contribution
{
    /// <summary>The index of the next contribution of the code-block, or -1.</summary>
    public int Next;

    /// <summary>The offset of the bytes in the data of the tile.</summary>
    public int Offset;

    /// <summary>The number of bytes.</summary>
    public int Length;

    /// <summary>The number of coding passes.</summary>
    public int PassCount;

    /// <summary>Whether the contribution is in a quality layer that is not decoded (<see cref="Jpeg2000DecodingOptions.QualityLayerLimit"/>).</summary>
    public bool IsSkipped;
}
