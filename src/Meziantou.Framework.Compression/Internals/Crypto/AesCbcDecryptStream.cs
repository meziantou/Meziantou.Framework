using System.Security.Cryptography;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Crypto;

/// <summary>
/// Decrypts AES-CBC data without padding (7z, RAR): the encrypted data are a whole number of 16-byte blocks, and the
/// decrypted data keep the zeros the encoder appended to the last block (the decoder of the content stops at its length).
/// </summary>
internal sealed class AesCbcDecryptStream : ReadOnlyStreamBase
{
    private const int BlockSize = 16;
    private const int ChunkSize = 64 * 1024;

    private readonly Stream _inner;
    private readonly bool _leaveOpen;
    private readonly Aes _aes;
    private readonly byte[] _iv;
    private readonly byte[] _input = new byte[ChunkSize];
    private readonly byte[] _output = new byte[ChunkSize];
    private int _outputOffset;
    private int _outputLength;
    private int _pending;
    private bool _ended;

    public AesCbcDecryptStream(Stream inner, ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, bool leaveOpen)
    {
        // AES is not available on the browser (WebAssembly)
        if (OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("AES encryption is not supported on the browser.");

        _inner = inner;
        _leaveOpen = leaveOpen;
        _aes = Aes.Create();
        _aes.Key = key.ToArray();
        _iv = iv.ToArray();
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        while (_outputOffset == _outputLength)
        {
            if (_ended)
                return 0;

            Fill();
        }

        var count = Math.Min(buffer.Length, _outputLength - _outputOffset);
        _output.AsSpan(_outputOffset, count).CopyTo(buffer);
        _outputOffset += count;
        return count;
    }

    private void Fill()
    {
        while (_pending < _input.Length)
        {
            var read = _inner.Read(_input.AsSpan(_pending));
            if (read is 0)
            {
                _ended = true;
                break;
            }

            _pending += read;
        }

        var length = _pending - (_pending % BlockSize);
        if (_ended && length != _pending)
            throw new InvalidDataException("The encrypted data are not a whole number of AES blocks.");

        _outputOffset = 0;
        _outputLength = length;
        if (length is 0)
            return;

        _aes.DecryptCbc(_input.AsSpan(0, length), _iv, _output, PaddingMode.None);

        // The IV of the next chunk is the last encrypted block of this one
        _input.AsSpan(length - BlockSize, BlockSize).CopyTo(_iv);
        _input.AsSpan(length, _pending - length).CopyTo(_input);
        _pending -= length;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _aes.Dispose();
            if (!_leaveOpen)
            {
                _inner.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
