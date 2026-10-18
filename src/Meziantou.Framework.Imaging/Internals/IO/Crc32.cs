using System.Buffers.Binary;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using ArmCrc32 = System.Runtime.Intrinsics.Arm.Crc32;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The CRC-32 of ISO 3309 / ITU-T V.42 used by PNG chunks (reflected polynomial 0xEDB88320, initial and final XOR
/// 0xFFFFFFFF), computed with the table-driven method described in the PNG specification, Annex D (sample CRC code).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="UpdateTables"/> processes 8 bytes per step with eight derived tables ("slicing by 8"): table <c>k</c> gives the
/// contribution of a byte followed by <c>k</c> zero bytes, <c>T[k][i] = (T[k - 1][i] &gt;&gt; 8) ^ T[0][T[k - 1][i] &amp; 0xFF]</c>,
/// which is the byte-at-a-time recurrence applied <c>k</c> more times. The result is the CRC of the reference loop
/// (<see cref="UpdateScalar"/>); about 5 times faster on the chunk sizes of PNG files.
/// </para>
/// <para>
/// Arm64 processors have instructions for this very polynomial (the CRC32 extension): <see cref="Update"/> uses them when
/// they are available, 8 bytes per instruction, which is several times faster again and gives the same value.
/// </para>
/// <para>
/// x64 processors have a carry-less multiplication (PCLMULQDQ), which <see cref="UpdateCarrylessMultiply"/> uses to fold
/// the data 64 bytes at a time. The CRC is linear: the CRC of a message equals the CRC of any shorter message whose
/// polynomial is congruent modulo the generator <c>P</c> once aligned. A 128-bit block <c>A = A_hi x^64 + A_lo</c> followed
/// by <c>D</c> bits contributes like <c>A_hi (x^(D+64) mod P) + A_lo (x^D mod P)</c>, which has fewer than 96 bits and
/// is added to the block <c>D</c> bits further. The running CRC is added to the first 4 bytes, the blocks are folded onto
/// the last 16 bytes of the whole blocks, and the CRC of these 16 bytes and of the remaining bytes, from zero, is the
/// result. In the reflected bit order of this CRC, a block loaded as a little-endian 128-bit value has the coefficient of
/// <c>x^(127 - i)</c> at bit <c>i</c>; a constant <c>c = x (x^(n-1) mod P)</c>, of degree 1 to 32, is stored with the
/// coefficient of <c>x^(64 - j)</c> at bit <c>j</c>, so that the carry-less product of a half and a constant has the same
/// layout as a block, without a shift. The constants are computed from <c>P</c> when the type is initialized.
/// </para>
/// </remarks>
internal static class Crc32
{
    private static readonly uint[] Table = CreateTable();
    private static readonly uint[] SlicingTables = CreateSlicingTables(Table);

    // Folding by 128 bits (one block) and by 512 bits (four blocks): the constant of the high half, then of the low half
    private static readonly Vector128<ulong> Fold128 = Vector128.Create(GetFoldingConstant(128 + 64), GetFoldingConstant(128));
    private static readonly Vector128<ulong> Fold512 = Vector128.Create(GetFoldingConstant(512 + 64), GetFoldingConstant(512));

    /// <summary>Gets the initial running value.</summary>
    public const uint Initial = 0xFFFFFFFF;

    /// <summary>Updates a running (non-finalized) CRC with more bytes.</summary>
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        if (!ArmCrc32.Arm64.IsSupported)
            return Pclmulqdq.IsSupported ? UpdateCarrylessMultiply(crc, data) : UpdateTables(crc, data);

        while (data.Length >= 8)
        {
            crc = ArmCrc32.Arm64.ComputeCrc32(crc, BinaryPrimitives.ReadUInt64LittleEndian(data));
            data = data[8..];
        }

        foreach (var value in data)
        {
            crc = ArmCrc32.ComputeCrc32(crc, value);
        }

        return crc;
    }

    /// <summary>The x64 version of <see cref="Update"/>: the data is folded with carry-less multiplications, 64 bytes per step.</summary>
    internal static uint UpdateCarrylessMultiply(uint crc, ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 * Vector128<byte>.Count)
            return UpdateTables(crc, data);

        // Four independent blocks hide the latency of the multiplications
        var x0 = Vector128.Create(data).AsUInt64() ^ Vector128.CreateScalar(crc).AsUInt64();
        var x1 = Vector128.Create(data[16..]).AsUInt64();
        var x2 = Vector128.Create(data[32..]).AsUInt64();
        var x3 = Vector128.Create(data[48..]).AsUInt64();
        data = data[64..];
        while (data.Length >= 64)
        {
            x0 = Fold(x0, Fold512) ^ Vector128.Create(data).AsUInt64();
            x1 = Fold(x1, Fold512) ^ Vector128.Create(data[16..]).AsUInt64();
            x2 = Fold(x2, Fold512) ^ Vector128.Create(data[32..]).AsUInt64();
            x3 = Fold(x3, Fold512) ^ Vector128.Create(data[48..]).AsUInt64();
            data = data[64..];
        }

        var x = Fold(Fold(Fold(x0, Fold128) ^ x1, Fold128) ^ x2, Fold128) ^ x3;
        while (data.Length >= 16)
        {
            x = Fold(x, Fold128) ^ Vector128.Create(data).AsUInt64();
            data = data[16..];
        }

        Span<byte> block = stackalloc byte[Vector128<byte>.Count];
        x.AsByte().CopyTo(block);
        return UpdateTables(UpdateTables(0, block), data);

        static Vector128<ulong> Fold(Vector128<ulong> value, Vector128<ulong> constants)
            => Pclmulqdq.CarrylessMultiply(value, constants, 0x00) ^ Pclmulqdq.CarrylessMultiply(value, constants, 0x11);
    }

    /// <summary>The table-driven version of <see cref="Update"/>, 8 bytes per step.</summary>
    internal static uint UpdateTables(uint crc, ReadOnlySpan<byte> data)
    {
        var tables = SlicingTables.AsSpan();
        while (data.Length >= 8)
        {
            var low = crc ^ BinaryPrimitives.ReadUInt32LittleEndian(data);
            var high = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            crc = tables[(7 * 256) + (int)(low & 0xFF)]
                ^ tables[(6 * 256) + (int)((low >> 8) & 0xFF)]
                ^ tables[(5 * 256) + (int)((low >> 16) & 0xFF)]
                ^ tables[(4 * 256) + (int)(low >> 24)]
                ^ tables[(3 * 256) + (int)(high & 0xFF)]
                ^ tables[(2 * 256) + (int)((high >> 8) & 0xFF)]
                ^ tables[256 + (int)((high >> 16) & 0xFF)]
                ^ tables[(int)(high >> 24)];
            data = data[8..];
        }

        return UpdateScalar(crc, data);
    }

    /// <summary>The byte-at-a-time reference of <see cref="Update"/> (PNG specification, Annex D).</summary>
    internal static uint UpdateScalar(uint crc, ReadOnlySpan<byte> data)
    {
        var table = Table;
        foreach (var value in data)
        {
            crc = table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    /// <summary>Finalizes a running CRC.</summary>
    public static uint Finish(uint crc) => crc ^ 0xFFFFFFFF;

    /// <summary>Computes the CRC of a complete buffer.</summary>
    public static uint Compute(ReadOnlySpan<byte> data) => Finish(Update(Initial, data));

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    /// <summary>
    /// Gets <c>x (x^(n-1) mod P)</c>, congruent to <c>x^n</c>, with the coefficient of <c>x^(64 - j)</c> at bit <c>j</c>
    /// (see the remarks of the type).
    /// </summary>
    private static ulong GetFoldingConstant(int n)
    {
        // x^(n-1) mod P, P = x^32 + ... + 1 (0x104C11DB7), the coefficient of x^d at bit d
        ulong remainder = 1;
        for (var i = 0; i < n - 1; i++)
        {
            remainder <<= 1;
            if ((remainder & (1UL << 32)) != 0)
            {
                remainder ^= 0x1_04C1_1DB7;
            }
        }

        // Multiplied by x, the coefficient of x^(d + 1) goes to bit 64 - (d + 1)
        ulong constant = 0;
        for (var d = 0; d < 32; d++)
        {
            if ((remainder & (1UL << d)) != 0)
            {
                constant |= 1UL << (63 - d);
            }
        }

        return constant;
    }

    private static uint[] CreateSlicingTables(uint[] table)
    {
        var tables = new uint[8 * 256];
        table.CopyTo(tables, 0);
        for (var k = 1; k < 8; k++)
        {
            for (var i = 0; i < 256; i++)
            {
                var previous = tables[((k - 1) * 256) + i];
                tables[(k * 256) + i] = (previous >> 8) ^ table[previous & 0xFF];
            }
        }

        return tables;
    }
}
