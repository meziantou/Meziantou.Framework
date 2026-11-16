using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>
/// Encrypts with the traditional PKWARE encryption (ZipCrypto, APPNOTE 6.1): a 12-byte encryption header of random bytes
/// whose last byte is the check byte, then the content. The header is written when the stream is created.
/// </summary>
internal sealed class ZipCryptoEncryptStream : WriteOnlyStreamBase
{
    private const int ChunkSize = 16 * 1024;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The entry stream owns the output")]
    private readonly Stream _output;
    private readonly byte[] _buffer = new byte[ChunkSize];
    private ZipCryptoKeys _keys;
    private bool _disposed;

    /// <param name="output">The stream the encrypted data is written to. The stream does not dispose it.</param>
    /// <param name="password">The password.</param>
    /// <param name="checkByte">The last byte of the header: the high byte of the CRC, or of the MS-DOS time when the entry has a data descriptor.</param>
    public ZipCryptoEncryptStream(Stream output, string password, byte checkByte)
    {
        _output = output;
        _keys = ZipCryptoKeys.Create(password);

        Span<byte> header = stackalloc byte[ZipCryptoKeys.HeaderLength];
        RandomNumberGenerator.Fill(header[..^1]);
        header[^1] = checkByte;
        _keys.Encrypt(header, header);
        output.Write(header);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (!buffer.IsEmpty)
        {
            var length = Math.Min(buffer.Length, ChunkSize);
            _keys.Encrypt(buffer[..length], _buffer);
            _output.Write(_buffer, 0, length);
            buffer = buffer[length..];
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (!buffer.IsEmpty)
        {
            var length = Math.Min(buffer.Length, ChunkSize);
            _keys.Encrypt(buffer.Span[..length], _buffer);
            await _output.WriteAsync(_buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            buffer = buffer[length..];
        }
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}
