using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>
/// Reads the raw content of a streamed entry whose size is not known, up to its data descriptor. A position is the end of
/// the content when a data descriptor that matches the bytes read so far starts there: its compressed size is the number
/// of bytes before it (and, for a stored entry that is not encrypted, its CRC-32 and its uncompressed size are the ones of
/// these bytes), and it is followed by the signature of a header. Content that contains such a descriptor is read
/// incorrectly; it is very unlikely by chance (the size must match), as SharpCompress and Info-ZIP assume too.
/// The bytes read past the descriptor go back to the input.
/// </summary>
internal sealed class ZipDescriptorScanStream : ReadOnlyStreamBase
{
    private const int BufferLength = 64 * 1024;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The reader owns the input")]
    private readonly PushbackStream _input;
    private readonly bool _stored;
    private readonly bool _zip64;
    private readonly byte[] _buffer = new byte[BufferLength];
    private int _offset;
    private int _count;
    private bool _endOfInput;
    private long _position;
    private uint _crc = uint.MaxValue;

    /// <param name="input">The input, at the start of the content.</param>
    /// <param name="stored">Whether the entry is stored and not encrypted: the descriptor must have the CRC-32 and the length of the bytes before it.</param>
    /// <param name="zip64">Whether the entry is a zip64 entry (64-bit sizes in the descriptor).</param>
    public ZipDescriptorScanStream(PushbackStream input, bool stored, bool zip64)
    {
        _input = input;
        _stored = stored;
        _zip64 = zip64;
    }

    /// <summary>Gets the data descriptor, once the content is read to its end.</summary>
    public ZipDataDescriptor? Descriptor { get; private set; }

    public override int Read(Span<byte> buffer)
    {
        if (Descriptor is not null || buffer.IsEmpty)
            return 0;

        while (true)
        {
            Fill();

            // A position is checked once the bytes of the longest descriptor and of the next signature are known
            var limit = Math.Min(buffer.Length, _endOfInput ? _count : _count - ZipDataDescriptor.MaxLookahead + 1);
            var emitted = 0;
            while (emitted < limit)
            {
                var window = _buffer.AsSpan(_offset + emitted, _count - emitted);
                var position = _position + emitted;
                if (ZipDataDescriptor.MayStartAt(window, position)
                    && ZipDataDescriptor.TryMatch(window, position, _stored ? ~_crc : null, _stored, _zip64, requireNextSignature: true, out var descriptor, out var length))
                {
                    Descriptor = descriptor;
                    _buffer.AsSpan(_offset, emitted).CopyTo(buffer);
                    _input.Unread(window[length..]);
                    _position += emitted;
                    _count = 0;
                    return emitted;
                }

                if (_stored)
                {
                    _crc = ZipCrc32.Update(_crc, window[0]);
                }

                emitted++;
            }

            if (emitted > 0)
            {
                _buffer.AsSpan(_offset, emitted).CopyTo(buffer);
                _offset += emitted;
                _count -= emitted;
                _position += emitted;
                return emitted;
            }

            if (_endOfInput)
                throw new InvalidDataException("The data descriptor of a zip entry is not found: the archive is truncated or corrupted.");
        }
    }

    private void Fill()
    {
        while (!_endOfInput && _count < ZipDataDescriptor.MaxLookahead)
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
}
