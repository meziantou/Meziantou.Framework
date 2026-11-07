using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Hashing;

/// <summary>The 25 lanes of 64 bits of the Keccak-p[1600] state (FIPS 202 §3.1).</summary>
[InlineArray(25)]
internal struct KeccakLanes
{
    private ulong _element0;
}
