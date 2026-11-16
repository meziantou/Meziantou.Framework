using System.Security.Cryptography;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Crypto;

/// <summary>Encrypts data with AES-CBC, padding the last block with zeros (as 7-Zip does).</summary>
internal sealed class AesCbcEncryptStream : WriteOnlyStreamBase
{
    private const int BlockSize = 16;
    private const int ChunkSize = 64 * 1024;

    private readonly Stream _inner;
    private readonly bool _leaveOpen;
    private readonly Aes _aes;
    private readonly byte[] _iv;
    private readonly byte[] _input = new byte[ChunkSize];
    private readonly byte[] _output = new byte[ChunkSize];
    private int _pending;
    private bool _disposed;

    public AesCbcEncryptStream(Stream inner, ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, bool leaveOpen)
    {
        _inner = inner;
        _leaveOpen = leaveOpen;
        _aes = Aes.Create();
        _aes.Key = key.ToArray();
        _iv = iv.ToArray();
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (!buffer.IsEmpty)
        {
            var count = Math.Min(buffer.Length, _input.Length - _pending);
            buffer[..count].CopyTo(_input.AsSpan(_pending));
            _pending += count;
            buffer = buffer[count..];
            if (_pending == _input.Length)
            {
                Encrypt(_pending);
            }
        }
    }

    private void Encrypt(int length)
    {
        _aes.EncryptCbc(_input.AsSpan(0, length), _iv, _output, PaddingMode.None);
        _output.AsSpan(length - BlockSize, BlockSize).CopyTo(_iv);
        _inner.Write(_output, 0, length);
        _pending = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            if (_pending > 0)
            {
                var length = (_pending + BlockSize - 1) / BlockSize * BlockSize;
                _input.AsSpan(_pending, length - _pending).Clear();
                Encrypt(length);
            }

            _aes.Dispose();
            if (!_leaveOpen)
            {
                _inner.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
