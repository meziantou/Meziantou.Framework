using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// The AES Key Wrap algorithm without padding (RFC 3394), used by the AES-KW algorithm of the Web Cryptography API
/// (https://w3c.github.io/webcrypto/#aes-kw). The base class library only provides the variant with padding (RFC 5649).
/// </summary>
internal static class AesKeyWrap
{
    // https://www.rfc-editor.org/rfc/rfc3394#section-2.2.3.1: the default initial value
    private const ulong DefaultInitialValue = 0xA6A6A6A6A6A6A6A6UL;

    private const int SemiBlockSize = 8;

    /// <summary>The key wrap process (RFC 3394 section 2.2.1, the index-based variant): returns the n + 1 64-bit blocks of the ciphertext.</summary>
    /// <param name="kek">The key-encryption key: 16, 24 or 32 bytes.</param>
    /// <param name="keyData">The key data: a multiple of 8 bytes, at least 16 bytes.</param>
    /// <exception cref="ArgumentException">The length of the key-encryption key or of the key data is not supported.</exception>
    public static byte[] Wrap(ReadOnlySpan<byte> kek, ReadOnlySpan<byte> keyData)
    {
        ValidateKey(kek);
        if (keyData.Length % SemiBlockSize != 0 || keyData.Length < 2 * SemiBlockSize)
        {
            throw new ArgumentException("The key data must be a multiple of 64 bits, and at least 128 bits long.", nameof(keyData));
        }

        using var aes = Aes.Create();
        aes.SetKey(kek);

        // A = IV, R[i] = P[i]
        var n = keyData.Length / SemiBlockSize;
        var result = new byte[keyData.Length + SemiBlockSize];
        keyData.CopyTo(result.AsSpan(SemiBlockSize));
        var a = DefaultInitialValue;
        Span<byte> block = stackalloc byte[2 * SemiBlockSize];
        for (var j = 0; j <= 5; j++)
        {
            for (var i = 1; i <= n; i++)
            {
                // B = AES(K, A | R[i]), A = MSB(64, B) ^ t where t = (n * j) + i, R[i] = LSB(64, B)
                var r = result.AsSpan(i * SemiBlockSize, SemiBlockSize);
                BinaryPrimitives.WriteUInt64BigEndian(block, a);
                r.CopyTo(block[SemiBlockSize..]);
                aes.EncryptEcb(block, block, PaddingMode.None);
                a = BinaryPrimitives.ReadUInt64BigEndian(block) ^ (ulong)((n * j) + i);
                block[SemiBlockSize..].CopyTo(r);
            }
        }

        // C[0] = A, C[i] = R[i]
        BinaryPrimitives.WriteUInt64BigEndian(result, a);
        CryptographicOperations.ZeroMemory(block);
        return result;
    }

    /// <summary>
    /// The key unwrap process (RFC 3394 section 2.2.2, the index-based variant) and its integrity check (section 2.2.3):
    /// returns the key data, or <see langword="null"/> when the initial value is not the default one.
    /// </summary>
    /// <param name="kek">The key-encryption key: 16, 24 or 32 bytes.</param>
    /// <param name="wrapped">The wrapped key: a multiple of 8 bytes, at least 24 bytes.</param>
    /// <exception cref="ArgumentException">The length of the key-encryption key or of the wrapped key is not supported.</exception>
    public static byte[]? Unwrap(ReadOnlySpan<byte> kek, ReadOnlySpan<byte> wrapped)
    {
        ValidateKey(kek);
        if (wrapped.Length % SemiBlockSize != 0 || wrapped.Length < 3 * SemiBlockSize)
        {
            throw new ArgumentException("The wrapped key must be a multiple of 64 bits, and at least 192 bits long.", nameof(wrapped));
        }

        using var aes = Aes.Create();
        aes.SetKey(kek);

        // A = C[0], R[i] = C[i]
        var n = (wrapped.Length / SemiBlockSize) - 1;
        var result = wrapped[SemiBlockSize..].ToArray();
        var a = BinaryPrimitives.ReadUInt64BigEndian(wrapped);
        Span<byte> block = stackalloc byte[2 * SemiBlockSize];
        for (var j = 5; j >= 0; j--)
        {
            for (var i = n; i >= 1; i--)
            {
                // B = AES-1(K, (A ^ t) | R[i]) where t = n * j + i, A = MSB(64, B), R[i] = LSB(64, B)
                var r = result.AsSpan((i - 1) * SemiBlockSize, SemiBlockSize);
                BinaryPrimitives.WriteUInt64BigEndian(block, a ^ (ulong)((n * j) + i));
                r.CopyTo(block[SemiBlockSize..]);
                aes.DecryptEcb(block, block, PaddingMode.None);
                a = BinaryPrimitives.ReadUInt64BigEndian(block);
                block[SemiBlockSize..].CopyTo(r);
            }
        }

        // The integrity check: A must be the default initial value
        BinaryPrimitives.WriteUInt64BigEndian(block, a);
        BinaryPrimitives.WriteUInt64BigEndian(block[SemiBlockSize..], DefaultInitialValue);
        var valid = CryptographicOperations.FixedTimeEquals(block[..SemiBlockSize], block[SemiBlockSize..]);
        CryptographicOperations.ZeroMemory(block);
        if (!valid)
        {
            CryptographicOperations.ZeroMemory(result);
            return null;
        }

        return result;
    }

    private static void ValidateKey(ReadOnlySpan<byte> kek)
    {
        if (kek.Length is not (16 or 24 or 32))
        {
            throw new ArgumentException("The AES key must be 128, 192 or 256 bits long.", nameof(kek));
        }
    }
}
