using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>
/// Encrypts with the WinZip AES encryption (see <see cref="WinZipAesCipher"/>): a random salt and the password verifier are
/// written when the stream is created, then the encrypted content, then the authentication code when the stream is disposed.
/// </summary>
internal sealed class WinZipAesEncryptStream : WriteOnlyStreamBase
{
    private const int ChunkSize = 16 * 1024;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The entry stream owns the output")]
    private readonly Stream _output;
    private readonly WinZipAesCipher _cipher;
    private readonly byte[] _buffer = new byte[ChunkSize];
    private bool _disposed;

    /// <param name="output">The stream the encrypted data is written to. The stream does not dispose it.</param>
    /// <param name="password">The password.</param>
    /// <param name="encryption">The key length.</param>
    public WinZipAesEncryptStream(Stream output, string password, ZipEncryption encryption)
    {
        var keyLength = WinZipAesCipher.GetKeyLength(encryption);
        Span<byte> header = stackalloc byte[(keyLength / 2) + WinZipAesCipher.VerifierLength];
        var salt = header[..(keyLength / 2)];
        RandomNumberGenerator.Fill(salt);
        _cipher = WinZipAesCipher.Create(password, salt, encryption, header[(keyLength / 2)..]);
        _output = output;
        output.Write(header);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (!buffer.IsEmpty)
        {
            var length = Math.Min(buffer.Length, ChunkSize);
            var chunk = Encrypt(buffer[..length]);
            _output.Write(chunk);
            buffer = buffer[length..];
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (!buffer.IsEmpty)
        {
            var length = Math.Min(buffer.Length, ChunkSize);
            Encrypt(buffer.Span[..length]);
            await _output.WriteAsync(_buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            buffer = buffer[length..];
        }
    }

    /// <summary>Writes the authentication code. The stream cannot be written to afterwards.</summary>
    public void Finish()
    {
        if (_disposed)
            return;

        _disposed = true;
        Span<byte> code = stackalloc byte[WinZipAesCipher.AuthenticationCodeLength];
        _cipher.GetAuthenticationCode(code);
        _output.Write(code);
    }

    private Span<byte> Encrypt(ReadOnlySpan<byte> data)
    {
        var chunk = _buffer.AsSpan(0, data.Length);
        data.CopyTo(chunk);
        _cipher.XorKeyStream(chunk);
        _cipher.Authenticate(chunk);
        return chunk;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                Finish();
            }
            finally
            {
                _cipher.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
