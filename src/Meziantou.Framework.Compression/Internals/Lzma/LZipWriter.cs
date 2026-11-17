// The lzip format (https://www.nongnu.org/lzip/manual/lzip_manual.html#File-format).
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>Encodes lzip data: members of at most <c>memberSize</c> bytes of data (0 for a single member).</summary>
internal sealed class LZipWriter : WriteOnlyStreamBase
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The stream does not own it")]
    private readonly Stream _output;
    private readonly LzmaEncoderSettings _settings;
    private readonly long _memberSize;
    private readonly byte _dictionaryByte;
    private readonly Crc32 _crc = new();
    private LzmaEncoderStream? _member;
    private long _dataSize;
    private bool _wroteMember;
    private bool _disposed;

    public LZipWriter(Stream output, LzmaEncoderSettings settings, long memberSize)
    {
        _output = output;
        _settings = settings;
        _memberSize = memberSize;
        _dictionaryByte = LZipReader.EncodeDictionarySize(settings.DictionarySize);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (!buffer.IsEmpty)
        {
            if (_member is null)
            {
                StartMember();
            }

            var count = buffer.Length;
            if (_memberSize > 0)
            {
                count = (int)Math.Min(count, _memberSize - _dataSize);
            }

            _member!.Write(buffer[..count]);
            _crc.Append(buffer[..count]);
            _dataSize += count;
            buffer = buffer[count..];
            if (_memberSize > 0 && _dataSize == _memberSize)
            {
                EndMember();
            }
        }
    }

    private void StartMember()
    {
        Span<byte> header = stackalloc byte[LZipReader.HeaderSize];
        LZipReader.Magic.CopyTo(header);
        header[4] = 1;
        header[5] = _dictionaryByte;
        _output.Write(header);
        _member = new LzmaEncoderStream(_output, _settings, writeEndMarker: true, leaveOpen: true);
        _crc.Reset();
        _dataSize = 0;
        _wroteMember = true;
    }

    private void EndMember()
    {
        var member = _member!;
        member.Dispose();
        _member = null;
        Span<byte> trailer = stackalloc byte[LZipReader.TrailerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(trailer, _crc.GetCurrentHashAsUInt32());
        BinaryPrimitives.WriteUInt64LittleEndian(trailer[4..], (ulong)_dataSize);
        BinaryPrimitives.WriteUInt64LittleEndian(trailer[12..], (ulong)(LZipReader.HeaderSize + member.CompressedLength + LZipReader.TrailerSize));
        _output.Write(trailer);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try
            {
                if (!_wroteMember)
                {
                    StartMember();
                }

                if (_member is not null)
                {
                    EndMember();
                }

                _output.Flush();
            }
            finally
            {
                _member?.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
