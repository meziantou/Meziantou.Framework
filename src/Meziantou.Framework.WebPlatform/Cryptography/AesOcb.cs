using System.Numerics;
using System.Security.Cryptography;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// The OCB authenticated-encryption mode (RFC 7253) with AES, used by the AES-OCB algorithm of the Modern Algorithms in the
/// Web Cryptography API (https://wicg.github.io/webcrypto-modern-algos/#aes-ocb). The base class library does not provide it:
/// the block cipher is the AES of the base class library in ECB mode, and the blocks are enciphered in batches.
/// </summary>
internal static class AesOcb
{
    /// <summary>The maximum length of the nonce, in bytes (RFC 7253 section 4.2: at most 120 bits).</summary>
    public const int MaxNonceLength = 15;

    private const int BlockSize = 16;

    // The number of blocks enciphered by each call to the block cipher
    private const int ChunkBlocks = 64;

    /// <summary>OCB-ENCRYPT (RFC 7253 section 4.2): returns the ciphertext followed by the tag.</summary>
    /// <param name="key">The AES key K: 16, 24 or 32 bytes.</param>
    /// <param name="nonce">The nonce N: 1 to 15 bytes.</param>
    /// <param name="associatedData">The associated data A.</param>
    /// <param name="plaintext">The plaintext P.</param>
    /// <param name="tagLengthBytes">The length of the tag (TAGLEN / 8): 8, 12 or 16 bytes.</param>
    /// <exception cref="ArgumentException">The length of the key, of the nonce or of the tag is not supported.</exception>
    public static byte[] Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> plaintext, int tagLengthBytes)
    {
        ValidateParameters(key, nonce, tagLengthBytes);
        using var aes = CreateAes(key);
        var result = new byte[plaintext.Length + tagLengthBytes];
        Span<byte> tag = stackalloc byte[BlockSize];
        Process(aes, nonce, associatedData, plaintext, result.AsSpan(0, plaintext.Length), tagLengthBytes, encrypt: true, tag);
        tag[..tagLengthBytes].CopyTo(result.AsSpan(plaintext.Length));
        return result;
    }

    /// <summary>OCB-DECRYPT (RFC 7253 section 4.3): returns the plaintext, or <see langword="null"/> when the tag is not valid (INVALID).</summary>
    /// <param name="key">The AES key K: 16, 24 or 32 bytes.</param>
    /// <param name="nonce">The nonce N: 1 to 15 bytes.</param>
    /// <param name="associatedData">The associated data A.</param>
    /// <param name="ciphertextAndTag">The ciphertext C followed by the tag.</param>
    /// <param name="tagLengthBytes">The length of the tag (TAGLEN / 8): 8, 12 or 16 bytes.</param>
    /// <exception cref="ArgumentException">The length of the key, of the nonce or of the tag is not supported, or the input is shorter than the tag.</exception>
    public static byte[]? Decrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> ciphertextAndTag, int tagLengthBytes)
    {
        ValidateParameters(key, nonce, tagLengthBytes);
        if (ciphertextAndTag.Length < tagLengthBytes)
        {
            throw new ArgumentException("The ciphertext is shorter than the tag.", nameof(ciphertextAndTag));
        }

        using var aes = CreateAes(key);
        var ciphertextLength = ciphertextAndTag.Length - tagLengthBytes;
        var plaintext = new byte[ciphertextLength];
        Span<byte> tag = stackalloc byte[BlockSize];
        Process(aes, nonce, associatedData, ciphertextAndTag[..ciphertextLength], plaintext, tagLengthBytes, encrypt: false, tag);
        if (!CryptographicOperations.FixedTimeEquals(tag[..tagLengthBytes], ciphertextAndTag[ciphertextLength..]))
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return null;
        }

        return plaintext;
    }

    private static void ValidateParameters(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, int tagLengthBytes)
    {
        if (key.Length is not (16 or 24 or 32))
        {
            throw new ArgumentException("The AES key must be 128, 192 or 256 bits long.", nameof(key));
        }

        if (nonce.Length is < 1 or > MaxNonceLength)
        {
            throw new ArgumentException("The nonce must be 1 to 15 bytes long.", nameof(nonce));
        }

        if (tagLengthBytes is not (8 or 12 or 16))
        {
            throw new ArgumentOutOfRangeException(nameof(tagLengthBytes), tagLengthBytes, "The tag must be 64, 96 or 128 bits long.");
        }
    }

    private static Aes CreateAes(ReadOnlySpan<byte> key)
    {
        var aes = Aes.Create();
        aes.SetKey(key);
        return aes;
    }

    // The encryption (P -> C) and the decryption (C -> P) share everything except the direction of the block cipher and the
    // input of the checksum (always the plaintext); tag receives the full 16-byte tag
    private static void Process(Aes aes, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> input, Span<byte> output, int tagLengthBytes, bool encrypt, Span<byte> tag)
    {
        // https://www.rfc-editor.org/rfc/rfc7253#section-4.1: L_* = ENCIPHER(K, zeros(128)), L_$ = double(L_*),
        // L_0 = double(L_$), L_i = double(L_(i-1))
        var fullBlocks = Math.Max(input.Length, associatedData.Length) / BlockSize;
        var lCount = fullBlocks == 0 ? 1 : BitOperations.Log2((uint)fullBlocks) + 1;
        Span<byte> lStar = stackalloc byte[BlockSize];
        Span<byte> lDollar = stackalloc byte[BlockSize];
        Span<byte> l = stackalloc byte[lCount * BlockSize];
        lStar.Clear();
        Encipher(aes, lStar, lStar);
        Double(lStar, lDollar);
        Double(lDollar, l[..BlockSize]);
        for (var i = 1; i < lCount; i++)
        {
            Double(l.Slice((i - 1) * BlockSize, BlockSize), l.Slice(i * BlockSize, BlockSize));
        }

        // https://www.rfc-editor.org/rfc/rfc7253#section-4.2: Nonce = num2str(TAGLEN mod 128, 7) || zeros(120 - bitlen(N)) || 1 || N
        Span<byte> block = stackalloc byte[BlockSize];
        block.Clear();
        block[0] = (byte)(((tagLengthBytes * 8) % 128) << 1);
        block[BlockSize - 1 - nonce.Length] |= 1;
        nonce.CopyTo(block[(BlockSize - nonce.Length)..]);

        // bottom = str2num(Nonce[123..128]), Ktop = ENCIPHER(K, Nonce[1..122] || zeros(6)),
        // Stretch = Ktop || (Ktop[1..64] xor Ktop[9..72]), Offset_0 = Stretch[1+bottom..128+bottom]
        var bottom = block[BlockSize - 1] & 0x3F;
        block[BlockSize - 1] &= 0xC0;
        Span<byte> stretch = stackalloc byte[BlockSize + 8 + 1];
        Encipher(aes, block, stretch[..BlockSize]);
        for (var i = 0; i < 8; i++)
        {
            stretch[BlockSize + i] = (byte)(stretch[i] ^ stretch[i + 1]);
        }

        stretch[^1] = 0;
        Span<byte> offset = stackalloc byte[BlockSize];
        var byteShift = bottom / 8;
        var bitShift = bottom % 8;
        for (var i = 0; i < BlockSize; i++)
        {
            offset[i] = (byte)((stretch[i + byteShift] << bitShift) | (stretch[i + byteShift + 1] >> (8 - bitShift)));
        }

        // The full blocks: Offset_i = Offset_(i-1) xor L_(ntz(i)), C_i = Offset_i xor ENCIPHER(K, P_i xor Offset_i),
        // Checksum_i = Checksum_(i-1) xor P_i (P_i = Offset_i xor DECIPHER(K, C_i xor Offset_i) for the decryption)
        Span<byte> checksum = stackalloc byte[BlockSize];
        checksum.Clear();
        Span<byte> offsets = stackalloc byte[ChunkBlocks * BlockSize];
        var blockCount = input.Length / BlockSize;
        for (var start = 0; start < blockCount; start += ChunkBlocks)
        {
            var count = Math.Min(ChunkBlocks, blockCount - start);
            var chunkOffsets = offsets[..(count * BlockSize)];
            ComputeOffsets(l, start, offset, chunkOffsets);

            var chunkInput = input.Slice(start * BlockSize, count * BlockSize);
            var chunkOutput = output.Slice(start * BlockSize, count * BlockSize);
            Xor(chunkInput, chunkOffsets, chunkOutput);
            if (encrypt)
            {
                aes.EncryptEcb(chunkOutput, chunkOutput, PaddingMode.None);
            }
            else
            {
                aes.DecryptEcb(chunkOutput, chunkOutput, PaddingMode.None);
            }

            Xor(chunkOutput, chunkOffsets, chunkOutput);
            UpdateChecksum(checksum, encrypt ? chunkInput : chunkOutput);
        }

        // The final partial block: Offset_* = Offset_m xor L_*, Pad = ENCIPHER(K, Offset_*), C_* = P_* xor Pad[1..bitlen(P_*)],
        // Checksum_* = Checksum_m xor (P_* || 1 || zeros(127 - bitlen(P_*)))
        var remaining = input.Length % BlockSize;
        if (remaining != 0)
        {
            Xor(offset, lStar, offset);
            Encipher(aes, offset, block);
            var finalInput = input[(blockCount * BlockSize)..];
            var finalOutput = output[(blockCount * BlockSize)..];
            Xor(finalInput, block[..remaining], finalOutput);

            var finalPlaintext = encrypt ? finalInput : finalOutput;
            block.Clear();
            finalPlaintext.CopyTo(block);
            block[remaining] = 0x80;
            Xor(checksum, block, checksum);
        }

        // Tag = ENCIPHER(K, Checksum xor Offset xor L_$) xor HASH(K, A)
        Xor(checksum, offset, block);
        Xor(block, lDollar, block);
        Encipher(aes, block, tag);
        Hash(aes, l, lStar, associatedData, block, offsets);
        Xor(tag, block, tag);
    }

    // https://www.rfc-editor.org/rfc/rfc7253#section-4.1: HASH(K, A)
    private static void Hash(Aes aes, ReadOnlySpan<byte> l, ReadOnlySpan<byte> lStar, ReadOnlySpan<byte> associatedData, Span<byte> sum, Span<byte> offsets)
    {
        // Sum_i = Sum_(i-1) xor ENCIPHER(K, A_i xor Offset_i), with Offset_0 = zeros(128) and Offset_i = Offset_(i-1) xor L_(ntz(i))
        Span<byte> offset = stackalloc byte[BlockSize];
        offset.Clear();
        sum.Clear();
        var blockCount = associatedData.Length / BlockSize;
        for (var start = 0; start < blockCount; start += ChunkBlocks)
        {
            var count = Math.Min(ChunkBlocks, blockCount - start);
            var chunkOffsets = offsets[..(count * BlockSize)];
            ComputeOffsets(l, start, offset, chunkOffsets);
            Xor(associatedData.Slice(start * BlockSize, count * BlockSize), chunkOffsets, chunkOffsets);
            aes.EncryptEcb(chunkOffsets, chunkOffsets, PaddingMode.None);
            UpdateChecksum(sum, chunkOffsets);
        }

        // The final partial block: Offset_* = Offset_m xor L_*, Sum = Sum_m xor ENCIPHER(K, (A_* || 1 || zeros(127 - bitlen(A_*))) xor Offset_*)
        var remaining = associatedData.Length % BlockSize;
        if (remaining != 0)
        {
            Span<byte> block = stackalloc byte[BlockSize];
            block.Clear();
            associatedData[(blockCount * BlockSize)..].CopyTo(block);
            block[remaining] = 0x80;
            Xor(offset, lStar, offset);
            Xor(block, offset, block);
            Encipher(aes, block, block);
            Xor(sum, block, sum);
        }
    }

    // Writes Offset_(start+1) to Offset_(start+count) in offsets; offset is Offset_start on input and the last offset on output
    private static void ComputeOffsets(ReadOnlySpan<byte> l, int start, Span<byte> offset, Span<byte> offsets)
    {
        var count = offsets.Length / BlockSize;
        for (var i = 0; i < count; i++)
        {
            var ntz = BitOperations.TrailingZeroCount(start + i + 1);
            Xor(offset, l.Slice(ntz * BlockSize, BlockSize), offset);
            offset.CopyTo(offsets[(i * BlockSize)..]);
        }
    }

    private static void UpdateChecksum(Span<byte> checksum, ReadOnlySpan<byte> blocks)
    {
        for (var i = 0; i < blocks.Length; i += BlockSize)
        {
            Xor(checksum, blocks.Slice(i, BlockSize), checksum);
        }
    }

    private static void Encipher(Aes aes, ReadOnlySpan<byte> input, Span<byte> output)
    {
        aes.EncryptEcb(input, output, PaddingMode.None);
    }

    // https://www.rfc-editor.org/rfc/rfc7253#section-2: double(S) = S << 1 if the first bit of S is 0, (S << 1) xor 0^120 10000111 otherwise
    private static void Double(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var carry = input[0] >> 7;
        for (var i = 0; i < BlockSize - 1; i++)
        {
            output[i] = (byte)((input[i] << 1) | (input[i + 1] >> 7));
        }

        output[BlockSize - 1] = (byte)((input[BlockSize - 1] << 1) ^ (carry * 0x87));
    }

    // destination = left xor right, on the length of destination
    private static void Xor(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, Span<byte> destination)
    {
        var length = destination.Length;
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            for (; i <= length - Vector<byte>.Count; i += Vector<byte>.Count)
            {
                (new Vector<byte>(left[i..]) ^ new Vector<byte>(right[i..])).CopyTo(destination[i..]);
            }
        }

        for (; i < length; i++)
        {
            destination[i] = (byte)(left[i] ^ right[i]);
        }
    }
}
