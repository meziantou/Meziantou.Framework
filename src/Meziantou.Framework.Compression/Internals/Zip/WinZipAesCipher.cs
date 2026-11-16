using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>
/// The WinZip AES encryption, shared by both directions: the keys derive from the password (UTF-8) and the salt with
/// PBKDF2-HMAC-SHA1 (1000 iterations) as the AES key, the HMAC key and a 2-byte password verifier; the content is encrypted
/// with AES-CTR (a little-endian counter that starts at 1), and authenticated by the first 10 bytes of the HMAC-SHA1 of
/// the encrypted content.
/// </summary>
internal sealed class WinZipAesCipher : IDisposable
{
    public const int AuthenticationCodeLength = 10;
    public const int VerifierLength = 2;
    private const int KeyStreamLength = 4096;

    private readonly Aes _aes;
    private readonly IncrementalHash _hmac;
    private readonly byte[] _counterBlocks = new byte[KeyStreamLength];
    private readonly byte[] _keyStream = new byte[KeyStreamLength];
    private int _keyStreamOffset = KeyStreamLength;
    private ulong _counter = 1;

    private WinZipAesCipher(ReadOnlySpan<byte> aesKey, ReadOnlySpan<byte> hmacKey)
    {
        _aes = Aes.Create();
        _aes.Key = aesKey.ToArray();
        _hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, hmacKey);
    }

    /// <summary>Gets the length of the key of an encryption (16, 24 or 32 bytes); the salt is half of it.</summary>
    public static int GetKeyLength(ZipEncryption encryption) => encryption switch
    {
        ZipEncryption.Aes128 => 16,
        ZipEncryption.Aes192 => 24,
        ZipEncryption.Aes256 => 32,
        _ => throw new ArgumentOutOfRangeException(nameof(encryption)),
    };

    /// <summary>Gets the strength of the AES extra field (0x9901): 1, 2 or 3.</summary>
    public static byte GetStrength(ZipEncryption encryption) => encryption switch
    {
        ZipEncryption.Aes128 => 1,
        ZipEncryption.Aes192 => 2,
        ZipEncryption.Aes256 => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(encryption)),
    };

    /// <summary>Gets the number of bytes the encryption adds to the content: the salt, the verifier and the authentication code.</summary>
    public static int GetOverhead(ZipEncryption encryption) => (GetKeyLength(encryption) / 2) + VerifierLength + AuthenticationCodeLength;

    /// <summary>Derives the keys of a password and a salt, and returns the cipher and the password verifier.</summary>
    public static WinZipAesCipher Create(string password, ReadOnlySpan<byte> salt, ZipEncryption encryption, Span<byte> verifier)
    {
        var keyLength = GetKeyLength(encryption);
        var keys = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 1000, HashAlgorithmName.SHA1, (2 * keyLength) + VerifierLength);
        keys.AsSpan(2 * keyLength, VerifierLength).CopyTo(verifier);
        return new WinZipAesCipher(keys.AsSpan(0, keyLength), keys.AsSpan(keyLength, keyLength));
    }

    /// <summary>Adds encrypted bytes to the authentication code.</summary>
    public void Authenticate(ReadOnlySpan<byte> cipherText) => _hmac.AppendData(cipherText);

    /// <summary>Writes the authentication code of the encrypted bytes (10 bytes).</summary>
    public void GetAuthenticationCode(Span<byte> destination)
    {
        Span<byte> hash = stackalloc byte[20];
        _hmac.GetHashAndReset(hash);
        hash[..AuthenticationCodeLength].CopyTo(destination);
    }

    /// <summary>Encrypts or decrypts bytes in place: AES-CTR is symmetric.</summary>
    public void XorKeyStream(Span<byte> data)
    {
        while (!data.IsEmpty)
        {
            if (_keyStreamOffset == KeyStreamLength)
            {
                var blocks = _counterBlocks.AsSpan();
                blocks.Clear();
                for (var i = 0; i < KeyStreamLength; i += 16)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(blocks[i..], _counter);
                    _counter++;
                }

                _aes.EncryptEcb(blocks, _keyStream, PaddingMode.None);
                _keyStreamOffset = 0;
            }

            var length = Math.Min(data.Length, KeyStreamLength - _keyStreamOffset);
            var keyStream = _keyStream.AsSpan(_keyStreamOffset, length);
            for (var i = 0; i < length; i++)
            {
                data[i] ^= keyStream[i];
            }

            _keyStreamOffset += length;
            data = data[length..];
        }
    }

    public void Dispose()
    {
        _aes.Dispose();
        _hmac.Dispose();
    }
}
