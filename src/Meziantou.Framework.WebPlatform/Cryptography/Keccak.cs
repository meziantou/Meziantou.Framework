using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// The hash functions and extendable-output functions built on Keccak: SHA-3 and SHAKE (FIPS 202), cSHAKE and KMAC
/// (NIST SP 800-185), TurboSHAKE and KangarooTwelve (RFC 9861). The base class library cannot be used: its SHA-3 types are
/// not supported on every platform (e.g. macOS), and it does not implement TurboSHAKE and KangarooTwelve.
/// </summary>
internal static class Keccak
{
    // https://www.rfc-editor.org/rfc/rfc9861#section-3: the size of the chunks of the KangarooTwelve tree
    private const int KangarooTwelveChunkSize = 8192;

    /// <summary>SHA3-256, SHA3-384 or SHA3-512 (FIPS 202 §6.1).</summary>
    /// <param name="bits">The size of the digest in bits: 256, 384 or 512.</param>
    public static byte[] Sha3(int bits, ReadOnlySpan<byte> data)
    {
        var output = new byte[GetSha3Length(bits)];
        Sha3(bits, data, output);
        return output;
    }

    /// <summary>SHA3-256, SHA3-384 or SHA3-512 (FIPS 202 §6.1), written to <paramref name="output"/>.</summary>
    public static void Sha3(int bits, ReadOnlySpan<byte> data, Span<byte> output)
    {
        var length = GetSha3Length(bits);
        if (output.Length != length)
            throw new ArgumentException("The output length does not match the digest size.", nameof(output));

        // SHA3-d(M) = KECCAK[2d](M || 01, d): the rate is 1600 - 2d bits
        var sponge = new KeccakSponge(200 - (2 * length), 0x06);
        sponge.Absorb(data);
        sponge.Squeeze(output);
    }

    /// <summary>SHAKE128 or SHAKE256 (FIPS 202 §6.2).</summary>
    public static byte[] Shake(int securityBits, ReadOnlySpan<byte> data, int outputLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outputLength);
        var output = new byte[outputLength];
        Shake(securityBits, data, output);
        return output;
    }

    /// <summary>SHAKE128 or SHAKE256 (FIPS 202 §6.2), filling <paramref name="output"/>.</summary>
    public static void Shake(int securityBits, ReadOnlySpan<byte> data, Span<byte> output)
    {
        var sponge = new KeccakSponge(GetRate(securityBits), 0x1F);
        sponge.Absorb(data);
        sponge.Squeeze(output);
    }

    /// <summary>
    /// cSHAKE128 or cSHAKE256 (NIST SP 800-185 §3.3): when the function name N and the customization string S are both
    /// empty, cSHAKE is SHAKE.
    /// </summary>
    /// <param name="securityBits">128 or 256.</param>
    /// <param name="outputLength">The length L of the output, in bytes.</param>
    public static byte[] CShake(int securityBits, ReadOnlySpan<byte> data, int outputLength, ReadOnlySpan<byte> functionName, ReadOnlySpan<byte> customization)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outputLength);
        var output = new byte[outputLength];
        var sponge = CreateCShake(securityBits, functionName, customization);
        sponge.Absorb(data);
        sponge.Squeeze(output);
        return output;
    }

    /// <summary>
    /// KMAC128 or KMAC256 (NIST SP 800-185 §4.3): cSHAKE(bytepad(encode_string(K), rate) || X || right_encode(L), L,
    /// "KMAC", S).
    /// </summary>
    /// <param name="securityBits">128 or 256.</param>
    /// <param name="outputLength">The length L of the output, in bytes.</param>
    public static byte[] Kmac(int securityBits, ReadOnlySpan<byte> key, ReadOnlySpan<byte> data, int outputLength, ReadOnlySpan<byte> customization)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outputLength);
        var sponge = CreateCShake(securityBits, "KMAC"u8, customization);
        AbsorbLeftEncode(ref sponge, (ulong)GetRate(securityBits));
        AbsorbEncodeString(ref sponge, key);
        sponge.AbsorbZeroPadding();
        sponge.Absorb(data);
        AbsorbRightEncode(ref sponge, (ulong)outputLength * 8);

        var output = new byte[outputLength];
        sponge.Squeeze(output);
        sponge.Clear();
        return output;
    }

    /// <summary>TurboSHAKE128 or TurboSHAKE256 (RFC 9861 §2.2): the sponge over Keccak-p[1600, 12].</summary>
    /// <param name="securityBits">128 or 256.</param>
    /// <param name="outputLength">The length L of the output, in bytes.</param>
    /// <param name="domainSeparation">The domain separation byte D, in the range 0x01 to 0x7F (0x1F by default).</param>
    public static byte[] TurboShake(int securityBits, ReadOnlySpan<byte> data, int outputLength, byte domainSeparation = 0x1F)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outputLength);
        if (domainSeparation is 0 or > 0x7F)
            throw new ArgumentOutOfRangeException(nameof(domainSeparation));

        var output = new byte[outputLength];
        var sponge = new KeccakSponge(GetRate(securityBits), domainSeparation, rounds: 12);
        sponge.Absorb(data);
        sponge.Squeeze(output);
        return output;
    }

    /// <summary>KT128 or KT256 (RFC 9861 §3), the KangarooTwelve tree hash over TurboSHAKE128 or TurboSHAKE256.</summary>
    /// <param name="securityBits">128 (KT128) or 256 (KT256).</param>
    /// <param name="outputLength">The length L of the output, in bytes.</param>
    /// <param name="customization">The customization string C.</param>
    public static byte[] KangarooTwelve(int securityBits, ReadOnlySpan<byte> data, int outputLength, ReadOnlySpan<byte> customization)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outputLength);
        var rate = GetRate(securityBits);
        var output = new byte[outputLength];

        // S = M || C || length_encode(|C|)
        Span<byte> customizationLength = stackalloc byte[9];
        customizationLength = customizationLength[..LengthEncode((ulong)customization.Length, customizationLength)];
        var totalLength = (long)data.Length + customization.Length + customizationLength.Length;
        if (totalLength <= KangarooTwelveChunkSize)
        {
            // Single node: TurboSHAKE(S, 0x07, L)
            var sponge = new KeccakSponge(rate, 0x07, rounds: 12);
            sponge.Absorb(data);
            sponge.Absorb(customization);
            sponge.Absorb(customizationLength);
            sponge.Squeeze(output);
            return output;
        }

        var tree = new KangarooTwelveTree(rate, securityBits == 128 ? 32 : 64);
        tree.Absorb(data);
        tree.Absorb(customization);
        tree.Absorb(customizationLength);
        tree.Finish(output);
        return output;
    }

    /// <summary>
    /// Creates the sponge of cSHAKE128 or cSHAKE256 after the absorption of bytepad(encode_string(N) || encode_string(S),
    /// rate) (NIST SP 800-185 §3.3), or the sponge of SHAKE when N and S are empty.
    /// </summary>
    internal static KeccakSponge CreateCShake(int securityBits, ReadOnlySpan<byte> functionName, ReadOnlySpan<byte> customization)
    {
        var rate = GetRate(securityBits);
        if (functionName.IsEmpty && customization.IsEmpty)
            return new KeccakSponge(rate, 0x1F);

        var sponge = new KeccakSponge(rate, 0x04);
        AbsorbLeftEncode(ref sponge, (ulong)rate);
        AbsorbEncodeString(ref sponge, functionName);
        AbsorbEncodeString(ref sponge, customization);
        sponge.AbsorbZeroPadding();
        return sponge;
    }

    /// <summary>left_encode(x) (NIST SP 800-185 §2.3.1): the number n of bytes of x, then x in n big-endian bytes.</summary>
    private static void AbsorbLeftEncode(ref KeccakSponge sponge, ulong value)
    {
        Span<byte> buffer = stackalloc byte[9];
        var length = EncodeBigEndian(value, buffer[1..]);
        buffer[0] = (byte)length;
        sponge.Absorb(buffer[..(length + 1)]);
    }

    /// <summary>right_encode(x) (NIST SP 800-185 §2.3.1): x in n big-endian bytes, then the number n of bytes of x.</summary>
    private static void AbsorbRightEncode(ref KeccakSponge sponge, ulong value)
    {
        Span<byte> buffer = stackalloc byte[9];
        var length = EncodeBigEndian(value, buffer);
        buffer[length] = (byte)length;
        sponge.Absorb(buffer[..(length + 1)]);
    }

    /// <summary>encode_string(S) (NIST SP 800-185 §2.3.2): left_encode(len(S)) || S, the length being in bits.</summary>
    private static void AbsorbEncodeString(ref KeccakSponge sponge, ReadOnlySpan<byte> value)
    {
        AbsorbLeftEncode(ref sponge, (ulong)value.Length * 8);
        sponge.Absorb(value);
    }

    /// <summary>Writes the minimal big-endian representation of a value (at least one byte), and returns its length.</summary>
    private static int EncodeBigEndian(ulong value, Span<byte> destination)
    {
        var length = 1;
        while (length < 8 && (value >> (length * 8)) != 0)
        {
            length++;
        }

        for (var i = 0; i < length; i++)
        {
            destination[i] = (byte)(value >> ((length - 1 - i) * 8));
        }

        return length;
    }

    /// <summary>
    /// length_encode(x) (RFC 9861 §3.3): x in the minimal number n of big-endian bytes (none when x is 0), then n. Returns
    /// the length of the encoding.
    /// </summary>
    private static int LengthEncode(ulong value, Span<byte> destination)
    {
        if (value == 0)
        {
            destination[0] = 0;
            return 1;
        }

        var length = EncodeBigEndian(value, destination);
        destination[length] = (byte)length;
        return length + 1;
    }

    private static int GetRate(int securityBits)
    {
        return securityBits switch
        {
            128 => KeccakSponge.Rate128,
            256 => KeccakSponge.Rate256,
            _ => throw new ArgumentOutOfRangeException(nameof(securityBits)),
        };
    }

    private static int GetSha3Length(int bits)
    {
        return bits switch
        {
            224 or 256 or 384 or 512 => bits / 8,
            _ => throw new ArgumentOutOfRangeException(nameof(bits)),
        };
    }

    /// <summary>
    /// The tree of KangarooTwelve (RFC 9861 §3.2) when S is longer than a chunk: the first chunk S_0 is absorbed in the
    /// final node, followed by 0x03 0x00^7; each other chunk S_i is a leaf whose chaining value TurboSHAKE(S_i, 0x0B, 32 or
    /// 64) is absorbed in the final node; the final node ends with length_encode(n - 1) || 0xFF 0xFF and is squeezed with
    /// the domain separation byte 0x06.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private struct KangarooTwelveTree
    {
        private readonly int _rate;
        private readonly int _chainingValueLength;
        private KeccakSponge _finalNode;
        private KeccakSponge _leaf;
        private ulong _chunkIndex;
        private int _chunkPosition;

        public KangarooTwelveTree(int rate, int chainingValueLength)
        {
            _rate = rate;
            _chainingValueLength = chainingValueLength;
            _finalNode = new KeccakSponge(rate, 0x06, rounds: 12);
        }

        public void Absorb(ReadOnlySpan<byte> data)
        {
            while (!data.IsEmpty)
            {
                // A complete chunk is closed only when more data follows: the last chunk is closed by Finish
                if (_chunkPosition == KangarooTwelveChunkSize)
                {
                    CloseChunk();
                }

                var count = Math.Min(KangarooTwelveChunkSize - _chunkPosition, data.Length);
                if (_chunkIndex == 0)
                {
                    _finalNode.Absorb(data[..count]);
                }
                else
                {
                    _leaf.Absorb(data[..count]);
                }

                _chunkPosition += count;
                data = data[count..];
            }
        }

        public void Finish(Span<byte> output)
        {
            CloseChunk();

            // The number of leaves is n - 1, where n is the number of chunks
            Span<byte> suffix = stackalloc byte[11];
            var length = LengthEncode(_chunkIndex - 1, suffix);
            suffix[length] = 0xFF;
            suffix[length + 1] = 0xFF;
            _finalNode.Absorb(suffix[..(length + 2)]);
            _finalNode.Squeeze(output);
        }

        private void CloseChunk()
        {
            if (_chunkIndex == 0)
            {
                _finalNode.Absorb([0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
            }
            else
            {
                Span<byte> chainingValue = stackalloc byte[64];
                chainingValue = chainingValue[.._chainingValueLength];
                _leaf.Squeeze(chainingValue);
                _finalNode.Absorb(chainingValue);
            }

            _chunkIndex++;
            _chunkPosition = 0;
            _leaf = new KeccakSponge(_rate, 0x0B, rounds: 12);
        }
    }
}
