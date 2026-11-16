using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>
/// Decrypts the WinZip AES encryption: a salt and a 2-byte password verifier, then the content encrypted with AES-CTR (a
/// little-endian counter that starts at 1), then the first 10 bytes of the HMAC-SHA1 of the encrypted content. The keys
/// derive from the password with PBKDF2-HMAC-SHA1 (1000 iterations). The input ends with the authentication code: the
/// stream holds back its last 10 bytes, so the length of the content does not need to be known.
/// </summary>
internal sealed class WinZipAesDecryptStream : ReadOnlyStreamBase
{
    public const int AuthenticationCodeLength = 10;
    private const int BufferLength = 16 * 1024;
    private const int KeyStreamLength = 4096;

    private readonly Stream _input;
    private readonly Aes _aes;
    private readonly IncrementalHash _hmac;
    private readonly byte[] _buffer = new byte[BufferLength];
    private readonly byte[] _counterBlocks = new byte[KeyStreamLength];
    private readonly byte[] _keyStream = new byte[KeyStreamLength];
    private int _keyStreamOffset = KeyStreamLength;
    private ulong _counter = 1;
    private int _offset;
    private int _count;
    private bool _endOfInput;
    private bool _verified;

    public WinZipAesDecryptStream(Stream input, string password, ZipEncryption encryption)
    {
        var keyLength = encryption switch
        {
            ZipEncryption.Aes128 => 16,
            ZipEncryption.Aes192 => 24,
            ZipEncryption.Aes256 => 32,
            _ => throw new ArgumentOutOfRangeException(nameof(encryption)),
        };

        Span<byte> salt = stackalloc byte[keyLength / 2];
        Span<byte> verifier = stackalloc byte[2];
        input.ReadExactlyOrThrow(salt);
        input.ReadExactlyOrThrow(verifier);

        var keys = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 1000, HashAlgorithmName.SHA1, (2 * keyLength) + 2);
        if (!keys.AsSpan(2 * keyLength).SequenceEqual(verifier))
            throw new ArchivePasswordException("The password of the zip entry is wrong.");

        _input = input;
        _aes = Aes.Create();
        _aes.Key = keys[..keyLength];
        _hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, keys.AsSpan(keyLength, keyLength));
    }

    /// <summary>Gets the number of bytes the encryption adds to the content.</summary>
    public static int GetOverhead(ZipEncryption encryption) => encryption switch
    {
        ZipEncryption.Aes128 => 8 + 2 + AuthenticationCodeLength,
        ZipEncryption.Aes192 => 12 + 2 + AuthenticationCodeLength,
        ZipEncryption.Aes256 => 16 + 2 + AuthenticationCodeLength,
        _ => 0,
    };

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        Fill();
        var available = _count - AuthenticationCodeLength;
        if (available <= 0)
        {
            Verify();
            return 0;
        }

        var length = Math.Min(available, buffer.Length);
        var cipherText = _buffer.AsSpan(_offset, length);
        _hmac.AppendData(cipherText);
        cipherText.CopyTo(buffer);
        XorKeyStream(buffer[..length]);
        _offset += length;
        _count -= length;
        return length;
    }

    /// <summary>Reads the rest of the content and checks the authentication code.</summary>
    /// <exception cref="InvalidDataException">The authentication code does not match: the entry is corrupted.</exception>
    public void VerifyAuthenticationCode()
    {
        var buffer = new byte[4096];
        while (Read(buffer) > 0)
        {
        }

        Verify();
    }

    private void Fill()
    {
        // Keeps more than the authentication code in the buffer, unless the input ends
        while (!_endOfInput && _count <= AuthenticationCodeLength)
        {
            if (_offset > 0)
            {
                _buffer.AsSpan(_offset, _count).CopyTo(_buffer);
                _offset = 0;
            }

            var read = _input.Read(_buffer.AsSpan(_count));
            if (read is 0)
            {
                _endOfInput = true;
            }

            _count += read;
        }
    }

    private void Verify()
    {
        if (_verified)
            return;

        if (_count < AuthenticationCodeLength)
            throw new InvalidDataException("The encrypted content of the zip entry is truncated.");

        Span<byte> hash = stackalloc byte[20];
        _hmac.GetHashAndReset(hash);
        if (!CryptographicOperations.FixedTimeEquals(hash[..AuthenticationCodeLength], _buffer.AsSpan(_offset, AuthenticationCodeLength)))
            throw new InvalidDataException("The authentication code of the zip entry does not match: the entry is corrupted.");

        _verified = true;
    }

    private void XorKeyStream(Span<byte> data)
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _aes.Dispose();
            _hmac.Dispose();
            _input.Dispose();
        }

        base.Dispose(disposing);
    }
}
