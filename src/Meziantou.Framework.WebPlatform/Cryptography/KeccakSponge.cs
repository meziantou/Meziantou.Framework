using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>The 25 lanes of 64 bits of the Keccak-p[1600] state (FIPS 202 §3.1).</summary>
[InlineArray(25)]
internal struct KeccakLanes
{
    private ulong _element0;
}

/// <summary>
/// An incremental sponge construction (FIPS 202 §4) over Keccak-p[1600, n<sub>r</sub>]: the data is absorbed in any number
/// of calls, then the output is squeezed in any number of calls (an extendable-output function). The padding is the
/// multi-rate padding pad10*1 (FIPS 202 §5.1) preceded by the domain separation bits, stored as a single byte with the
/// first padding bit included: 0x06 for SHA-3, 0x1F for SHAKE, 0x04 for cSHAKE (NIST SP 800-185 §3.3), or the byte D of
/// TurboSHAKE (RFC 9861 §2.2).
/// </summary>
/// <remarks>
/// The bytes are mapped to the lanes in little-endian order (FIPS 202 §B.1), independently of the endianness of the
/// platform. The struct is mutable: it must be passed by reference.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal struct KeccakSponge
{
    /// <summary>The rate of SHAKE128, cSHAKE128, KMAC128, TurboSHAKE128 and KT128, in bytes.</summary>
    public const int Rate128 = 168;

    /// <summary>The rate of SHAKE256, cSHAKE256, KMAC256, TurboSHAKE256, KT256 and SHA3-256, in bytes.</summary>
    public const int Rate256 = 136;

    // https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.202.pdf §3.2.5: the round constants of ι, for the rounds 0 to 23
    private static readonly ulong[] RoundConstants =
    [
        0x0000000000000001UL, 0x0000000000008082UL, 0x800000000000808AUL, 0x8000000080008000UL,
        0x000000000000808BUL, 0x0000000080000001UL, 0x8000000080008081UL, 0x8000000000008009UL,
        0x000000000000008AUL, 0x0000000000000088UL, 0x0000000080008009UL, 0x000000008000000AUL,
        0x000000008000808BUL, 0x800000000000008BUL, 0x8000000000008089UL, 0x8000000000008003UL,
        0x8000000000008002UL, 0x8000000000000080UL, 0x000000000000800AUL, 0x800000008000000AUL,
        0x8000000080008081UL, 0x8000000000008080UL, 0x0000000080000001UL, 0x8000000080008008UL,
    ];

    private KeccakLanes _lanes;
    private readonly int _rate;
    private readonly int _rounds;
    private readonly byte _domainSeparation;

    // The position of the next byte in the current block; while squeezing, _rate means that the block is consumed
    private int _position;
    private bool _squeezing;

    /// <summary>Initializes an empty sponge.</summary>
    /// <param name="rate">The rate r in bytes, a multiple of 8 lower than 200.</param>
    /// <param name="domainSeparation">The domain separation byte, which contains the first bit of the padding.</param>
    /// <param name="rounds">The number of rounds of the permutation: 24 for Keccak-f[1600] (FIPS 202), 12 for TurboSHAKE (RFC 9861).</param>
    public KeccakSponge(int rate, byte domainSeparation, int rounds = 24)
    {
        Debug.Assert(rate > 0 && rate < 200 && rate % 8 == 0);
        Debug.Assert(rounds is > 0 and <= 24);
        Debug.Assert(domainSeparation != 0);
        _rate = rate;
        _domainSeparation = domainSeparation;
        _rounds = rounds;
    }

    /// <summary>Creates the sponge of SHAKE128 (FIPS 202 §6.2).</summary>
    public static KeccakSponge CreateShake128() => new(Rate128, 0x1F);

    /// <summary>Creates the sponge of SHAKE256 (FIPS 202 §6.2).</summary>
    public static KeccakSponge CreateShake256() => new(Rate256, 0x1F);

    /// <summary>Absorbs data; this method must not be called after <see cref="Squeeze"/>.</summary>
    public void Absorb(ReadOnlySpan<byte> data)
    {
        Debug.Assert(!_squeezing);
        Span<ulong> lanes = _lanes;
        var rate = _rate;
        var position = _position;
        while (!data.IsEmpty)
        {
            if (position == 0 && data.Length >= rate)
            {
                // Whole blocks: XOR the lanes directly
                var laneCount = rate / 8;
                do
                {
                    for (var i = 0; i < laneCount; i++)
                    {
                        lanes[i] ^= BinaryPrimitives.ReadUInt64LittleEndian(data[(i * 8)..]);
                    }

                    Permute(lanes, _rounds);
                    data = data[rate..];
                }
                while (data.Length >= rate);

                continue;
            }

            var count = Math.Min(rate - position, data.Length);
            for (var i = 0; i < count; i++)
            {
                var index = position + i;
                lanes[index >> 3] ^= (ulong)data[i] << ((index & 7) << 3);
            }

            position += count;
            data = data[count..];
            if (position == rate)
            {
                Permute(lanes, _rounds);
                position = 0;
            }
        }

        _position = position;
    }

    /// <summary>Absorbs a single byte.</summary>
    public void Absorb(byte value)
    {
        Absorb(new ReadOnlySpan<byte>(in value));
    }

    /// <summary>
    /// Absorbs zero bytes until the absorbed length is a multiple of the rate: the zeros of bytepad(X, w) when w is the
    /// rate (NIST SP 800-185 §2.3.3) and the absorption started on a block boundary.
    /// </summary>
    public void AbsorbZeroPadding()
    {
        Debug.Assert(!_squeezing);
        if (_position != 0)
        {
            Permute(_lanes, _rounds);
            _position = 0;
        }
    }

    /// <summary>Squeezes the next bytes of the output; the first call pads the absorbed data.</summary>
    public void Squeeze(Span<byte> output)
    {
        Span<ulong> lanes = _lanes;
        var rate = _rate;
        var position = _position;
        if (!_squeezing)
        {
            // FIPS 202 §5.1 pad10*1, with the domain separation bits before it (the last bit is in the last byte of the block)
            lanes[position >> 3] ^= (ulong)_domainSeparation << ((position & 7) << 3);
            lanes[(rate - 1) >> 3] ^= 0x80UL << (((rate - 1) & 7) << 3);
            Permute(lanes, _rounds);
            position = 0;
            _squeezing = true;
        }

        while (!output.IsEmpty)
        {
            if (position == rate)
            {
                Permute(lanes, _rounds);
                position = 0;
            }

            if (position == 0 && output.Length >= rate)
            {
                var laneCount = rate / 8;
                for (var i = 0; i < laneCount; i++)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(output[(i * 8)..], lanes[i]);
                }

                output = output[rate..];
                position = rate;
                continue;
            }

            var count = Math.Min(rate - position, output.Length);
            for (var i = 0; i < count; i++)
            {
                var index = position + i;
                output[i] = (byte)(lanes[index >> 3] >> ((index & 7) << 3));
            }

            position += count;
            output = output[count..];
        }

        _position = position;
    }

    /// <summary>Squeezes a single byte of the output.</summary>
    public byte SqueezeByte()
    {
        byte value = 0;
        Squeeze(new Span<byte>(ref value));
        return value;
    }

    /// <summary>Clears the state, which may depend on secret data.</summary>
    public void Clear()
    {
        ((Span<ulong>)_lanes).Clear();
        _position = 0;
        _squeezing = false;
    }

    /// <summary>
    /// Keccak-p[1600, n<sub>r</sub>] (FIPS 202 §3.3): the last <paramref name="rounds"/> rounds of Keccak-f[1600] (rounds
    /// 24 - n<sub>r</sub> to 23), each round being ι ∘ χ ∘ π ∘ ρ ∘ θ. The lane A[x, y] is at index x + 5y.
    /// </summary>
    internal static void Permute(Span<ulong> state, int rounds)
    {
        Debug.Assert(state.Length == 25);
        var a00 = state[0];
        var a01 = state[1];
        var a02 = state[2];
        var a03 = state[3];
        var a04 = state[4];
        var a05 = state[5];
        var a06 = state[6];
        var a07 = state[7];
        var a08 = state[8];
        var a09 = state[9];
        var a10 = state[10];
        var a11 = state[11];
        var a12 = state[12];
        var a13 = state[13];
        var a14 = state[14];
        var a15 = state[15];
        var a16 = state[16];
        var a17 = state[17];
        var a18 = state[18];
        var a19 = state[19];
        var a20 = state[20];
        var a21 = state[21];
        var a22 = state[22];
        var a23 = state[23];
        var a24 = state[24];

        var roundConstants = RoundConstants;
        for (var round = 24 - rounds; round < 24; round++)
        {
            // θ (§3.2.1): C[x] = A[x, 0] ^ ... ^ A[x, 4], D[x] = C[x - 1] ^ ROT(C[x + 1], 1)
            var c0 = a00 ^ a05 ^ a10 ^ a15 ^ a20;
            var c1 = a01 ^ a06 ^ a11 ^ a16 ^ a21;
            var c2 = a02 ^ a07 ^ a12 ^ a17 ^ a22;
            var c3 = a03 ^ a08 ^ a13 ^ a18 ^ a23;
            var c4 = a04 ^ a09 ^ a14 ^ a19 ^ a24;
            var d0 = c4 ^ BitOperations.RotateLeft(c1, 1);
            var d1 = c0 ^ BitOperations.RotateLeft(c2, 1);
            var d2 = c1 ^ BitOperations.RotateLeft(c3, 1);
            var d3 = c2 ^ BitOperations.RotateLeft(c4, 1);
            var d4 = c3 ^ BitOperations.RotateLeft(c0, 1);

            // ρ (§3.2.2) and π (§3.2.3): B[y, 2x + 3y] = ROT(A[x, y] ^ D[x], r[x, y])
            var b00 = a00 ^ d0;
            var b16 = BitOperations.RotateLeft(a05 ^ d0, 36);
            var b07 = BitOperations.RotateLeft(a10 ^ d0, 3);
            var b23 = BitOperations.RotateLeft(a15 ^ d0, 41);
            var b14 = BitOperations.RotateLeft(a20 ^ d0, 18);
            var b10 = BitOperations.RotateLeft(a01 ^ d1, 1);
            var b01 = BitOperations.RotateLeft(a06 ^ d1, 44);
            var b17 = BitOperations.RotateLeft(a11 ^ d1, 10);
            var b08 = BitOperations.RotateLeft(a16 ^ d1, 45);
            var b24 = BitOperations.RotateLeft(a21 ^ d1, 2);
            var b20 = BitOperations.RotateLeft(a02 ^ d2, 62);
            var b11 = BitOperations.RotateLeft(a07 ^ d2, 6);
            var b02 = BitOperations.RotateLeft(a12 ^ d2, 43);
            var b18 = BitOperations.RotateLeft(a17 ^ d2, 15);
            var b09 = BitOperations.RotateLeft(a22 ^ d2, 61);
            var b05 = BitOperations.RotateLeft(a03 ^ d3, 28);
            var b21 = BitOperations.RotateLeft(a08 ^ d3, 55);
            var b12 = BitOperations.RotateLeft(a13 ^ d3, 25);
            var b03 = BitOperations.RotateLeft(a18 ^ d3, 21);
            var b19 = BitOperations.RotateLeft(a23 ^ d3, 56);
            var b15 = BitOperations.RotateLeft(a04 ^ d4, 27);
            var b06 = BitOperations.RotateLeft(a09 ^ d4, 20);
            var b22 = BitOperations.RotateLeft(a14 ^ d4, 39);
            var b13 = BitOperations.RotateLeft(a19 ^ d4, 8);
            var b04 = BitOperations.RotateLeft(a24 ^ d4, 14);

            // χ (§3.2.4): A[x, y] = B[x, y] ^ (~B[x + 1, y] & B[x + 2, y]), and ι (§3.2.5)
            a00 = b00 ^ (~b01 & b02) ^ roundConstants[round];
            a01 = b01 ^ (~b02 & b03);
            a02 = b02 ^ (~b03 & b04);
            a03 = b03 ^ (~b04 & b00);
            a04 = b04 ^ (~b00 & b01);
            a05 = b05 ^ (~b06 & b07);
            a06 = b06 ^ (~b07 & b08);
            a07 = b07 ^ (~b08 & b09);
            a08 = b08 ^ (~b09 & b05);
            a09 = b09 ^ (~b05 & b06);
            a10 = b10 ^ (~b11 & b12);
            a11 = b11 ^ (~b12 & b13);
            a12 = b12 ^ (~b13 & b14);
            a13 = b13 ^ (~b14 & b10);
            a14 = b14 ^ (~b10 & b11);
            a15 = b15 ^ (~b16 & b17);
            a16 = b16 ^ (~b17 & b18);
            a17 = b17 ^ (~b18 & b19);
            a18 = b18 ^ (~b19 & b15);
            a19 = b19 ^ (~b15 & b16);
            a20 = b20 ^ (~b21 & b22);
            a21 = b21 ^ (~b22 & b23);
            a22 = b22 ^ (~b23 & b24);
            a23 = b23 ^ (~b24 & b20);
            a24 = b24 ^ (~b20 & b21);
        }

        state[0] = a00;
        state[1] = a01;
        state[2] = a02;
        state[3] = a03;
        state[4] = a04;
        state[5] = a05;
        state[6] = a06;
        state[7] = a07;
        state[8] = a08;
        state[9] = a09;
        state[10] = a10;
        state[11] = a11;
        state[12] = a12;
        state[13] = a13;
        state[14] = a14;
        state[15] = a15;
        state[16] = a16;
        state[17] = a17;
        state[18] = a18;
        state[19] = a19;
        state[20] = a20;
        state[21] = a21;
        state[22] = a22;
        state[23] = a23;
        state[24] = a24;
    }
}
