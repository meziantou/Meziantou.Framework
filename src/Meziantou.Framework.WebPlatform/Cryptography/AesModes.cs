using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// The AES modes of operation of the Web Cryptography API (https://w3c.github.io/webcrypto/#aes-ctr): CBC with PKCS#7
/// padding, CTR with a counter of any length, and GCM with any IV length and the tag lengths of the API. The base class
/// library only provides the block cipher for CTR and GCM: <see cref="AesGcm"/> requires a 96-bit IV and a tag of at least
/// 96 bits, and there is no CTR mode. The counter blocks are encrypted at once in ECB mode.
/// </summary>
internal static class AesModes
{
    private const int BlockSize = 16;

    /// <summary>The CBC encryption with the PKCS#7 padding (NIST SP 800-38A section 6.2, RFC 2315 section 10.3).</summary>
    public static byte[] CbcEncrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> plaintext)
    {
        using var aes = CreateAes(key);
        return aes.EncryptCbc(plaintext, iv, PaddingMode.PKCS7);
    }

    /// <summary>
    /// The CBC decryption, or null when the padding is invalid (https://w3c.github.io/webcrypto/#aes-cbc-operations-decrypt:
    /// the last byte p must be between 1 and 16, and the last p bytes must be p).
    /// </summary>
    public static byte[]? CbcDecrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> ciphertext)
    {
        using var aes = CreateAes(key);
        var padded = aes.DecryptCbc(ciphertext, iv, PaddingMode.None);
        var p = padded[^1];
        if (p is 0 or > BlockSize)
            return null;

        var invalid = 0;
        for (var i = padded.Length - p; i < padded.Length; i++)
        {
            invalid |= padded[i] ^ p;
        }

        return invalid != 0 ? null : padded.AsSpan(0, padded.Length - p).ToArray();
    }

    /// <summary>
    /// The CTR encryption and decryption (NIST SP 800-38A section 6.5): the rightmost <paramref name="counterLength"/> bits
    /// of the counter block are incremented as a big-endian integer modulo 2^counterLength (appendix B.1).
    /// </summary>
    public static byte[] Ctr(ReadOnlySpan<byte> key, ReadOnlySpan<byte> counter, int counterLength, ReadOnlySpan<byte> input)
    {
        if (counter.Length != BlockSize || counterLength is < 1 or > 128)
            throw new ArgumentException("The counter block must be 16 bytes and its counter between 1 and 128 bits", nameof(counter));

        var blockCount = (input.Length + BlockSize - 1) / BlockSize;
        var keyStream = new byte[blockCount * BlockSize];
        Span<byte> block = stackalloc byte[BlockSize];
        counter.CopyTo(block);
        for (var i = 0; i < blockCount; i++)
        {
            block.CopyTo(keyStream.AsSpan(i * BlockSize));
            Increment(block, counterLength);
        }

        using var aes = CreateAes(key);
        aes.EncryptEcb(keyStream, keyStream, PaddingMode.None);
        var output = new byte[input.Length];
        Xor(input, keyStream, output);
        return output;
    }

    /// <summary>
    /// The authenticated encryption of GCM (NIST SP 800-38D section 7.1): the ciphertext followed by the first
    /// <paramref name="tagLength"/> bytes of the tag.
    /// </summary>
    public static byte[] GcmEncrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> additionalData, int tagLength, ReadOnlySpan<byte> plaintext)
    {
        using var aes = CreateAes(key);
        var gcm = new Gcm(aes, iv);
        var output = new byte[plaintext.Length + tagLength];
        gcm.Crypt(plaintext, output.AsSpan(0, plaintext.Length));
        Span<byte> tag = stackalloc byte[BlockSize];
        gcm.ComputeTag(additionalData, output.AsSpan(0, plaintext.Length), tag);
        tag[..tagLength].CopyTo(output.AsSpan(plaintext.Length));
        return output;
    }

    /// <summary>
    /// The authenticated decryption of GCM (NIST SP 800-38D section 7.2): <paramref name="input"/> is the ciphertext followed
    /// by the tag. Returns null when the tag is not authentic.
    /// </summary>
    public static byte[]? GcmDecrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> additionalData, int tagLength, ReadOnlySpan<byte> input)
    {
        if (input.Length < tagLength)
            return null;

        var ciphertext = input[..^tagLength];
        using var aes = CreateAes(key);
        var gcm = new Gcm(aes, iv);
        Span<byte> tag = stackalloc byte[BlockSize];
        gcm.ComputeTag(additionalData, ciphertext, tag);
        if (!CryptographicOperations.FixedTimeEquals(tag[..tagLength], input[^tagLength..]))
            return null;

        var plaintext = new byte[ciphertext.Length];
        gcm.Crypt(ciphertext, plaintext);
        return plaintext;
    }

    private static Aes CreateAes(ReadOnlySpan<byte> key)
    {
        var aes = Aes.Create();
        aes.Key = key.ToArray();
        return aes;
    }

    // Increments the rightmost bits of a block as a big-endian integer, modulo 2^bits
    private static void Increment(Span<byte> block, int bits)
    {
        for (var i = BlockSize - 1; i >= 0 && bits > 0; i--, bits -= 8)
        {
            var mask = bits >= 8 ? 0xFF : (1 << bits) - 1;
            var value = block[i] & mask;
            var incremented = (value + 1) & mask;
            block[i] = (byte)((block[i] & ~mask) | incremented);
            if (incremented != 0)
                return;
        }
    }

    private static void Xor(ReadOnlySpan<byte> input, ReadOnlySpan<byte> keyStream, Span<byte> output)
    {
        for (var i = 0; i < input.Length; i++)
        {
            output[i] = (byte)(input[i] ^ keyStream[i]);
        }
    }

    /// <summary>The state of a GCM operation: the hash subkey H (with its multiplication tables) and the pre-counter block J0.</summary>
    private sealed class Gcm
    {
        // The reduction of the 4-bit shifts by the polynomial of GF(2^128) (Shoup's method)
        private static readonly ushort[] Last4 = [0x0000, 0x1c20, 0x3840, 0x2460, 0x7080, 0x6ca0, 0x48c0, 0x54e0, 0xe100, 0xfd20, 0xd940, 0xc560, 0x9180, 0x8da0, 0xa9c0, 0xb5e0];

        private readonly Aes _aes;
        private readonly ulong[] _hh = new ulong[16];
        private readonly ulong[] _hl = new ulong[16];
        private readonly byte[] _j0 = new byte[BlockSize];

        public Gcm(Aes aes, ReadOnlySpan<byte> iv)
        {
            _aes = aes;

            // H = CIPH_K(0^128)
            Span<byte> h = stackalloc byte[BlockSize];
            h.Clear();
            aes.EncryptEcb(h, h, PaddingMode.None);
            InitializeTables(BinaryPrimitives.ReadUInt64BigEndian(h), BinaryPrimitives.ReadUInt64BigEndian(h[8..]));

            // J0 = IV || 0^31 || 1 for a 96-bit IV, else GHASH_H(IV || 0^(s+64) || [len(IV)]_64)
            if (iv.Length == 12)
            {
                iv.CopyTo(_j0);
                _j0[15] = 1;
            }
            else
            {
                Span<byte> y = stackalloc byte[BlockSize];
                y.Clear();
                Absorb(y, iv);
                Span<byte> lengths = stackalloc byte[BlockSize];
                lengths.Clear();
                BinaryPrimitives.WriteUInt64BigEndian(lengths[8..], (ulong)iv.Length * 8);
                Xor(y, lengths, y);
                Multiply(y);
                y.CopyTo(_j0);
            }
        }

        /// <summary>GCTR_K(inc32(J0), input): the encryption and the decryption.</summary>
        public void Crypt(ReadOnlySpan<byte> input, Span<byte> output)
        {
            if (input.IsEmpty)
                return;

            var blockCount = (input.Length + BlockSize - 1) / BlockSize;
            var keyStream = new byte[blockCount * BlockSize];
            var counter = BinaryPrimitives.ReadUInt32BigEndian(_j0.AsSpan(12));
            for (var i = 0; i < blockCount; i++)
            {
                var block = keyStream.AsSpan(i * BlockSize, BlockSize);
                _j0.AsSpan(0, 12).CopyTo(block);
                BinaryPrimitives.WriteUInt32BigEndian(block[12..], unchecked(counter + (uint)i + 1));
            }

            _aes.EncryptEcb(keyStream, keyStream, PaddingMode.None);
            Xor(input, keyStream, output);
        }

        /// <summary>T = GCTR_K(J0, GHASH_H(A || 0^v || C || 0^u || [len(A)]_64 || [len(C)]_64)), 16 bytes.</summary>
        public void ComputeTag(ReadOnlySpan<byte> additionalData, ReadOnlySpan<byte> ciphertext, Span<byte> tag)
        {
            Span<byte> s = stackalloc byte[BlockSize];
            s.Clear();
            Absorb(s, additionalData);
            Absorb(s, ciphertext);
            Span<byte> lengths = stackalloc byte[BlockSize];
            BinaryPrimitives.WriteUInt64BigEndian(lengths, (ulong)additionalData.Length * 8);
            BinaryPrimitives.WriteUInt64BigEndian(lengths[8..], (ulong)ciphertext.Length * 8);
            Xor(s, lengths, s);
            Multiply(s);

            Span<byte> encryptedJ0 = stackalloc byte[BlockSize];
            _aes.EncryptEcb(_j0, encryptedJ0, PaddingMode.None);
            Xor(s, encryptedJ0, tag);
        }

        // Y = (Y xor X_i) * H for each block of the data, the last block padded with zeros
        private void Absorb(Span<byte> y, ReadOnlySpan<byte> data)
        {
            while (!data.IsEmpty)
            {
                var length = Math.Min(BlockSize, data.Length);
                for (var i = 0; i < length; i++)
                {
                    y[i] ^= data[i];
                }

                Multiply(y);
                data = data[length..];
            }
        }

        private void InitializeTables(ulong vh, ulong vl)
        {
            _hh[8] = vh;
            _hl[8] = vl;
            for (var i = 4; i > 0; i >>= 1)
            {
                var t = (vl & 1) * 0xe1000000UL;
                vl = (vh << 63) | (vl >> 1);
                vh = (vh >> 1) ^ (t << 32);
                _hh[i] = vh;
                _hl[i] = vl;
            }

            for (var i = 2; i <= 8; i *= 2)
            {
                for (var j = 1; j < i; j++)
                {
                    _hh[i + j] = _hh[i] ^ _hh[j];
                    _hl[i + j] = _hl[i] ^ _hl[j];
                }
            }
        }

        // x = x * H in GF(2^128) (NIST SP 800-38D section 6.3), 4 bits at a time
        private void Multiply(Span<byte> x)
        {
            var lo = x[15] & 0xF;
            var zh = _hh[lo];
            var zl = _hl[lo];
            for (var i = 15; i >= 0; i--)
            {
                lo = x[i] & 0xF;
                var hi = (x[i] >> 4) & 0xF;
                ulong rem;
                if (i != 15)
                {
                    rem = zl & 0xF;
                    zl = (zh << 60) | (zl >> 4);
                    zh >>= 4;
                    zh ^= (ulong)Last4[rem] << 48;
                    zh ^= _hh[lo];
                    zl ^= _hl[lo];
                }

                rem = zl & 0xF;
                zl = (zh << 60) | (zl >> 4);
                zh >>= 4;
                zh ^= (ulong)Last4[rem] << 48;
                zh ^= _hh[hi];
                zl ^= _hl[hi];
            }

            BinaryPrimitives.WriteUInt64BigEndian(x, zh);
            BinaryPrimitives.WriteUInt64BigEndian(x[8..], zl);
        }
    }
}
