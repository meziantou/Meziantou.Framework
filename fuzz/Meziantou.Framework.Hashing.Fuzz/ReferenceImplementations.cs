using System.Buffers.Binary;
using System.Numerics;

namespace Meziantou.Framework.Hashing.Fuzz;

/// <summary>
/// Straightforward implementations of the algorithms, written from their specifications, that the targets compare the
/// library with. They favor clarity over speed: no tables, no vectors, one byte or one block at a time.
/// </summary>
internal static class ReferenceImplementations
{
    private const string BcryptAlphabet = "./ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const int KangarooTwelveChunkSize = 8192;

    private static readonly ulong[] Blake2bInitializationVector =
    [
        0x6a09e667f3bcc908, 0xbb67ae8584caa73b, 0x3c6ef372fe94f82b, 0xa54ff53a5f1d36f1,
        0x510e527fade682d1, 0x9b05688c2b3e6c1f, 0x1f83d9abfb41bd6b, 0x5be0cd19137e2179,
    ];

    private static readonly byte[][] Blake2bSigma =
    [
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
        [14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3],
        [11, 8, 12, 0, 5, 2, 15, 13, 10, 14, 3, 6, 7, 1, 9, 4],
        [7, 9, 3, 1, 13, 12, 11, 14, 2, 6, 5, 10, 4, 0, 15, 8],
        [9, 0, 5, 7, 2, 4, 10, 15, 14, 1, 11, 12, 6, 8, 3, 13],
        [2, 12, 6, 10, 0, 11, 8, 3, 4, 13, 7, 5, 15, 14, 1, 9],
        [12, 5, 1, 15, 14, 13, 4, 10, 0, 7, 6, 3, 9, 2, 8, 11],
        [13, 11, 7, 14, 12, 1, 3, 9, 5, 0, 15, 4, 8, 6, 2, 10],
        [6, 15, 14, 9, 11, 3, 0, 8, 12, 2, 13, 7, 1, 4, 10, 5],
        [10, 2, 8, 4, 7, 6, 1, 5, 15, 11, 9, 14, 3, 12, 13, 0],
    ];

    /// <summary>CRC-32 (ISO-HDLC, the CRC of zlib and PNG): reflected polynomial 0xEDB88320, one bit at a time.</summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) is not 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    /// <summary>Adler-32 (RFC 1950 section 8.2): both sums are reduced modulo 65521 after every byte.</summary>
    public static uint Adler32(ReadOnlySpan<byte> data)
    {
        var a = 1u;
        var b = 0u;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }

    /// <summary>Unkeyed BLAKE2b (RFC 7693), one 128-byte block at a time.</summary>
    public static byte[] Blake2b(ReadOnlySpan<byte> message, int hashSize)
    {
        var state = (ulong[])Blake2bInitializationVector.Clone();
        state[0] ^= 0x01010000UL ^ (ulong)hashSize;

        var blockCount = Math.Max(1, (message.Length + 127) / 128);
        var block = new byte[128];
        for (var i = 0; i < blockCount; i++)
        {
            var offset = i * 128;
            var length = Math.Min(128, message.Length - offset);
            Array.Clear(block);
            message.Slice(offset, length).CopyTo(block);
            Blake2bCompress(state, block, (ulong)(offset + length), isFinalBlock: i == blockCount - 1);
        }

        var digest = new byte[64];
        for (var i = 0; i < 8; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(digest.AsSpan(i * 8), state[i]);
        }

        return digest[..hashSize];
    }

    /// <summary>
    /// KT128 or KT256 (RFC 9861 section 3.2), assembled from the public <see cref="TurboShake"/>: S = M || C ||
    /// length_encode(|C|) is hashed directly when it fits in a chunk, else as a final node holding the first chunk and the
    /// chaining values of the other chunks.
    /// </summary>
    public static byte[] KangarooTwelve(int securityStrength, ReadOnlySpan<byte> message, ReadOnlySpan<byte> customization, int outputLength)
    {
        byte[] s = [.. message, .. customization, .. LengthEncode((ulong)customization.Length)];
        if (s.Length <= KangarooTwelveChunkSize)
            return TurboShake.HashData(securityStrength, s, outputLength, 0x07);

        var chainingValueLength = securityStrength is 128 ? 32 : 64;
        var finalNode = new List<byte>(KangarooTwelveChunkSize + 8 + (((s.Length / KangarooTwelveChunkSize) + 1) * chainingValueLength) + 11);
        finalNode.AddRange(s.AsSpan(0, KangarooTwelveChunkSize));
        finalNode.AddRange([0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);

        ulong leafCount = 0;
        for (var offset = KangarooTwelveChunkSize; offset < s.Length; offset += KangarooTwelveChunkSize)
        {
            var chunk = s.AsSpan(offset, Math.Min(KangarooTwelveChunkSize, s.Length - offset));
            finalNode.AddRange(TurboShake.HashData(securityStrength, chunk, chainingValueLength, 0x0B));
            leafCount++;
        }

        finalNode.AddRange(LengthEncode(leafCount));
        finalNode.AddRange([0xFF, 0xFF]);
        return TurboShake.HashData(securityStrength, finalNode.ToArray(), outputLength, 0x06);
    }

    /// <summary>The bcrypt variant of base64 (alphabet "./A-Za-z0-9", no padding) of 16 bytes of salt: 22 characters.</summary>
    public static string EncodeBcryptSalt(ReadOnlySpan<byte> salt)
    {
        var builder = new StringBuilder(22);
        for (var i = 0; i < salt.Length; i += 3)
        {
            var b0 = salt[i];
            builder.Append(BcryptAlphabet[b0 >> 2]);
            if (i + 1 >= salt.Length)
            {
                builder.Append(BcryptAlphabet[(b0 & 0x03) << 4]);
                break;
            }

            var b1 = salt[i + 1];
            builder.Append(BcryptAlphabet[((b0 & 0x03) << 4) | (b1 >> 4)]);
            if (i + 2 >= salt.Length)
            {
                builder.Append(BcryptAlphabet[(b1 & 0x0F) << 2]);
                break;
            }

            var b2 = salt[i + 2];
            builder.Append(BcryptAlphabet[((b1 & 0x0F) << 2) | (b2 >> 6)]);
            builder.Append(BcryptAlphabet[b2 & 0x3F]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Parses "$2$", "$2a$", "$2b$", "$2x$" or "$2y$", a 2-digit cost in the range 4 to 31, '$', then 22 characters of
    /// salt (16 bytes) and 31 characters of digest (23 bytes) in the bcrypt alphabet. The last character of each carries
    /// fewer than 6 bits (2 of the salt, 4 of the digest), and only the spelling whose other bits are zero decodes and
    /// encodes back to itself.
    /// </summary>
    public static bool TryParseBcryptHash(string hash, out BcryptVersion version, out int workFactor)
    {
        version = default;
        workFactor = 0;
        if (!hash.StartsWith("$2", StringComparison.Ordinal))
            return false;

        var index = 2;
        if (hash.Length > index && hash[index] is not '$')
        {
            version = hash[index] switch
            {
                'a' => BcryptVersion.Revision2A,
                'b' => BcryptVersion.Revision2B,
                'x' => BcryptVersion.Revision2X,
                'y' => BcryptVersion.Revision2Y,
                _ => (BcryptVersion)(-1),
            };

            if (version is (BcryptVersion)(-1))
                return false;

            index++;
        }
        else
        {
            version = BcryptVersion.Revision2;
        }

        // '$' NN '$' salt digest
        if (hash.Length != index + 1 + 2 + 1 + 22 + 31 || hash[index] is not '$' || hash[index + 3] is not '$')
            return false;

        if (!char.IsAsciiDigit(hash[index + 1]) || !char.IsAsciiDigit(hash[index + 2]))
            return false;

        workFactor = ((hash[index + 1] - '0') * 10) + (hash[index + 2] - '0');
        if (workFactor is < Bcrypt.MinWorkFactor or > Bcrypt.MaxWorkFactor)
            return false;

        var payload = hash.AsSpan(index + 4);
        foreach (var c in payload)
        {
            if (!BcryptAlphabet.Contains(c, StringComparison.Ordinal))
                return false;
        }

        return BcryptAlphabet.IndexOf(payload[21], StringComparison.Ordinal) % 16 is 0
            && BcryptAlphabet.IndexOf(payload[^1], StringComparison.Ordinal) % 4 is 0;
    }

    /// <summary>length_encode(x) (RFC 9861 section 3.3): x in big-endian without leading zero bytes, then the number of those bytes.</summary>
    private static byte[] LengthEncode(ulong value)
    {
        var bytes = new List<byte>(9);
        while (value > 0)
        {
            bytes.Insert(0, (byte)value);
            value >>= 8;
        }

        bytes.Add((byte)bytes.Count);
        return [.. bytes];
    }

    // https://www.rfc-editor.org/rfc/rfc7693#section-3.2
    private static void Blake2bCompress(ulong[] state, byte[] block, ulong counter, bool isFinalBlock)
    {
        var m = new ulong[16];
        for (var i = 0; i < 16; i++)
        {
            m[i] = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(i * 8));
        }

        var v = new ulong[16];
        state.CopyTo(v, 0);
        Blake2bInitializationVector.CopyTo(v, 8);
        v[12] ^= counter;
        if (isFinalBlock)
        {
            v[14] = ~v[14];
        }

        for (var round = 0; round < 12; round++)
        {
            var s = Blake2bSigma[round % 10];
            Blake2bMix(v, 0, 4, 8, 12, m[s[0]], m[s[1]]);
            Blake2bMix(v, 1, 5, 9, 13, m[s[2]], m[s[3]]);
            Blake2bMix(v, 2, 6, 10, 14, m[s[4]], m[s[5]]);
            Blake2bMix(v, 3, 7, 11, 15, m[s[6]], m[s[7]]);
            Blake2bMix(v, 0, 5, 10, 15, m[s[8]], m[s[9]]);
            Blake2bMix(v, 1, 6, 11, 12, m[s[10]], m[s[11]]);
            Blake2bMix(v, 2, 7, 8, 13, m[s[12]], m[s[13]]);
            Blake2bMix(v, 3, 4, 9, 14, m[s[14]], m[s[15]]);
        }

        for (var i = 0; i < 8; i++)
        {
            state[i] ^= v[i] ^ v[i + 8];
        }
    }

    // https://www.rfc-editor.org/rfc/rfc7693#section-3.1
    private static void Blake2bMix(ulong[] v, int a, int b, int c, int d, ulong x, ulong y)
    {
        v[a] = v[a] + v[b] + x;
        v[d] = BitOperations.RotateRight(v[d] ^ v[a], 32);
        v[c] += v[d];
        v[b] = BitOperations.RotateRight(v[b] ^ v[c], 24);
        v[a] = v[a] + v[b] + y;
        v[d] = BitOperations.RotateRight(v[d] ^ v[a], 16);
        v[c] += v[d];
        v[b] = BitOperations.RotateRight(v[b] ^ v[c], 63);
    }
}
