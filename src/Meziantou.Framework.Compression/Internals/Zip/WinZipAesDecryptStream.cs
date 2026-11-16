using System.Security.Cryptography;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>
/// Decrypts the WinZip AES encryption (see <see cref="WinZipAesCipher"/>): a salt and a 2-byte password verifier, then the
/// encrypted content, then the authentication code. The input ends with the authentication code: the stream holds back its
/// last 10 bytes, so the length of the content does not need to be known.
/// </summary>
internal sealed class WinZipAesDecryptStream : ReadOnlyStreamBase
{
    private const int AuthenticationCodeLength = WinZipAesCipher.AuthenticationCodeLength;
    private const int BufferLength = 16 * 1024;

    private readonly Stream _input;
    private readonly WinZipAesCipher _cipher;
    private readonly byte[] _buffer = new byte[BufferLength];
    private int _offset;
    private int _count;
    private bool _endOfInput;
    private bool _verified;

    public WinZipAesDecryptStream(Stream input, string password, ZipEncryption encryption)
    {
        var keyLength = WinZipAesCipher.GetKeyLength(encryption);
        Span<byte> salt = stackalloc byte[keyLength / 2];
        Span<byte> verifier = stackalloc byte[WinZipAesCipher.VerifierLength];
        input.ReadExactlyOrThrow(salt);
        input.ReadExactlyOrThrow(verifier);

        Span<byte> expectedVerifier = stackalloc byte[WinZipAesCipher.VerifierLength];
        var cipher = WinZipAesCipher.Create(password, salt, encryption, expectedVerifier);
        if (!expectedVerifier.SequenceEqual(verifier))
        {
            cipher.Dispose();
            throw new ArchivePasswordException("The password of the zip entry is wrong.");
        }

        _input = input;
        _cipher = cipher;
    }

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
        _cipher.Authenticate(cipherText);
        cipherText.CopyTo(buffer);
        _cipher.XorKeyStream(buffer[..length]);
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

        Span<byte> code = stackalloc byte[AuthenticationCodeLength];
        _cipher.GetAuthenticationCode(code);
        if (!CryptographicOperations.FixedTimeEquals(code, _buffer.AsSpan(_offset, AuthenticationCodeLength)))
            throw new InvalidDataException("The authentication code of the zip entry does not match: the entry is corrupted.");

        _verified = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cipher.Dispose();
            _input.Dispose();
        }

        base.Dispose(disposing);
    }
}
