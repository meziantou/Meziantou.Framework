using System.IO.Hashing;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>The CRC-64 of the XZ format (CRC-64/XZ, the ECMA-182 polynomial reflected, with an initial value and a final XOR of all ones).</summary>
internal static class Crc64Xz
{
    /// <summary>Gets the parameters of CRC-64/XZ.</summary>
    public static Crc64ParameterSet Parameters { get; } = Crc64ParameterSet.Create(0x42F0E1EBA9EA3693, ulong.MaxValue, ulong.MaxValue, reflectValues: true);

    /// <summary>Computes the CRC-64/XZ of <paramref name="data"/>.</summary>
    public static ulong Compute(ReadOnlySpan<byte> data) => Crc64.HashToUInt64(Parameters, data);

    /// <summary>Creates an incremental CRC-64/XZ.</summary>
    public static Crc64 Create() => new(Parameters);
}
