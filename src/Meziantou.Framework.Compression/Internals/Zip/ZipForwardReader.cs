using System.Buffers.Binary;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>
/// Reads the local headers of a zip archive one after the other, from a stream that does not need to be seekable, and
/// stops at the central directory. The content of an entry whose local header does not know the compressed size (a
/// streamed entry, with a data descriptor) ends where its decoder ends for deflate and Deflate64, and otherwise at the
/// first data descriptor that matches the bytes before it (see <see cref="ZipDescriptorScanStream"/>).
/// </summary>
internal sealed class ZipForwardReader : ArchiveReader
{
    private const uint SplitMarkerSignature = 0x08074B50;
    private const uint TemporarySplitMarkerSignature = 0x30304B50;
    private const uint EndOfCentralDirectorySignature = 0x06054B50;

    private readonly Stream _source;
    private readonly bool _ownsSource;
    private readonly PushbackStream _input;
    private readonly ArchiveReaderOptions _options;
    private EntryState? _current;
    private bool _started;
    private int _generation;

    public ZipForwardReader(Stream stream, ArchiveReaderOptions? options, bool leaveOpen)
    {
        _options = options ?? new ArchiveReaderOptions();
        if (_options.VolumeResolver is { } resolver)
        {
            // The local data of the volumes of a split archive are contiguous
            _source = new ConcatenatedStream(stream, resolver, leaveFirstOpen: leaveOpen);
            _ownsSource = true;
        }
        else
        {
            _source = stream;
            _ownsSource = !leaveOpen;
        }

        _input = new PushbackStream(_source);
    }

    public override ArchiveFormat Format => ArchiveFormat.Zip;

    private protected override ArchiveEntry? ReadNextEntry()
    {
        _current?.Finish();
        _current = null;
        _generation++;

        Span<byte> buffer = stackalloc byte[4];
        var signature = ReadSignature(buffer);
        if (!_started && signature is SplitMarkerSignature or TemporarySplitMarkerSignature)
        {
            signature = ReadSignature(buffer);
        }
        else if (!_started && signature is not (ZipEntryInfo.LocalHeaderSignature or EndOfCentralDirectorySignature))
        {
            // Data prepended to the archive (the stub of a self-extracting archive): the archive starts at the first header
            _input.Unread(buffer);
            signature = SkipToFirstHeader();
        }

        _started = true;
        return signature switch
        {
            ZipEntryInfo.LocalHeaderSignature => ReadLocalHeader(),

            // The central directory, the end of central directory records, a digital signature, or the archive extra data record of an encrypted central directory
            ZipEntryInfo.CentralHeaderSignature or EndOfCentralDirectorySignature or 0x06064B50 or 0x05054B50 or 0x08064B50 => null,
            _ => throw new InvalidDataException("The stream is not a zip archive, or the archive is corrupted: a local header is expected."),
        };
    }

    private uint ReadSignature(Span<byte> buffer)
    {
        var read = _input.ReadAtMost(buffer);
        if (read < 4)
            throw new InvalidDataException(_started ? "The zip archive is truncated: its central directory is missing." : "The stream is not a zip archive.");

        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    private uint SkipToFirstHeader()
    {
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = _input.ReadAtMost(buffer);
            if (read < 4)
                throw new InvalidDataException("The stream is not a zip archive.");

            var data = buffer.AsSpan(0, read);
            for (var i = 0; i + 4 <= data.Length; i++)
            {
                if (data[i] is (byte)'P' && data[i + 1] is (byte)'K' && ((data[i + 2] is 3 && data[i + 3] is 4) || (data[i + 2] is 5 && data[i + 3] is 6)))
                {
                    _input.Unread(data[(i + 4)..]);
                    return BinaryPrimitives.ReadUInt32LittleEndian(data[i..]);
                }
            }

            // A signature may straddle two reads
            _input.Unread(data[^3..]);
        }
    }

    private ZipArchiveEntry ReadLocalHeader()
    {
        var header = new byte[ZipEntryInfo.LocalHeaderLength - 4];
        _input.ReadExactlyOrThrow(header);
        var name = new byte[BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(22))];
        var extra = new byte[BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(24))];
        _input.ReadExactlyOrThrow(name);
        _input.ReadExactlyOrThrow(extra);
        var info = ZipEntryInfo.ParseLocal(header, name, extra, _options);

        DataMode mode;
        if (!info.HasDataDescriptor)
        {
            mode = DataMode.Known;
        }
        else if (info.Encryption is ZipEncryption.None && (ZipCompressionMethod)info.Method is ZipCompressionMethod.Deflate or ZipCompressionMethod.Deflate64)
        {
            // The decoder finds the end of the data
            mode = DataMode.Decoder;
        }
        else if (info.CompressedSize > 0 && (info.CompressedSize != uint.MaxValue || info.HasZip64Extra))
        {
            // Info-ZIP sets the flag, and writes the sizes, when it writes to a seekable file
            mode = DataMode.Known;
        }
        else
        {
            mode = DataMode.Scan;
        }

        var state = new EntryState(this, info, mode, _generation, _input.Position);
        var entry = new ZipArchiveEntry(archive: null, info, _ => state.Open(), fromCentralDirectory: false, sizesKnown: !info.HasDataDescriptor);
        state.Entry = entry;
        _current = state;
        return entry;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _input.Dispose();
            if (_ownsSource)
            {
                _source.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private enum DataMode
    {
        /// <summary>The compressed size is known.</summary>
        Known,

        /// <summary>The content ends at the first data descriptor that matches it.</summary>
        Scan,

        /// <summary>The content ends where the deflate decoder ends.</summary>
        Decoder,
    }

    private sealed class EntryState
    {
        private readonly ZipForwardReader _reader;
        private readonly ZipEntryInfo _info;
        private readonly DataMode _mode;
        private readonly int _generation;
        private readonly long _dataStart;
        private Stream? _raw;
        private ZipDeflateEndStream? _decoder;
        private bool _opened;
        private bool _finished;
        private ZipDataDescriptor? _descriptor;

        public EntryState(ZipForwardReader reader, ZipEntryInfo info, DataMode mode, int generation, long dataStart)
        {
            _reader = reader;
            _info = info;
            _mode = mode;
            _generation = generation;
            _dataStart = dataStart;
        }

        public ZipArchiveEntry? Entry { get; set; }

        private bool IsCurrent => !_reader.IsDisposed && _reader._generation == _generation;

        public Stream Open()
        {
            ObjectDisposedException.ThrowIf(_reader.IsDisposed, _reader);
            if (!IsCurrent)
                throw new InvalidOperationException("The reader moved past this entry: its content can no longer be read.");

            if (_opened)
                throw new InvalidOperationException("The content of an entry of a forward-only reader can be opened once.");

            ZipEntryOpener.ThrowIfNotSupported(_info);
            _opened = true;
            var uncompressedSize = _info.HasDataDescriptor ? -1 : _info.UncompressedSize;
            if (_mode is DataMode.Decoder)
                return ZipEntryOpener.Open(_info, Stream.Null, uncompressedSize, _reader._options, OnEnd, () => IsCurrent, _ => GetDecoder());

            var raw = GetRawStream();
            return ZipEntryOpener.Open(_info, new NonClosingStream(raw), uncompressedSize, _reader._options, OnEnd, () => IsCurrent);
        }

        private (long Length, uint? Crc)? OnEnd()
        {
            Finish();
            if (_descriptor is not { } descriptor)
                return null;

            return (descriptor.UncompressedSize, _info.ExpectedCrc is null ? null : descriptor.Crc);
        }

        private ZipDeflateEndStream GetDecoder()
        {
            return _decoder ??= new ZipDeflateEndStream(_reader._input, (ZipCompressionMethod)_info.Method is ZipCompressionMethod.Deflate64);
        }

        private Stream GetRawStream()
        {
            return _raw ??= _mode switch
            {
                DataMode.Known => new BoundedReadStream(_reader._input, _info.CompressedSize),
                _ => new ZipDescriptorScanStream(_reader._input, stored: _info.Method is 0 && _info.Encryption is ZipEncryption.None, zip64: _info.HasZip64Extra),
            };
        }

        /// <summary>Skips the rest of the content, and reads the data descriptor.</summary>
        public void Finish()
        {
            if (_finished)
                return;

            _finished = true;
            if (_mode is DataMode.Decoder)
            {
                var decoder = GetDecoder();
                try
                {
                    decoder.SkipToEnd();
                }
                finally
                {
                    decoder.Release();
                }
            }

            var raw = _mode is DataMode.Decoder ? null : GetRawStream();
            switch (raw)
            {
                case BoundedReadStream bounded:
                    bounded.SkipToEnd();
                    if (bounded.Remaining > 0)
                        throw new InvalidDataException($"The content of the zip entry '{_info.FullName}' is truncated.");

                    break;

                case ZipDescriptorScanStream scan:
                    scan.CopyTo(Stream.Null);
                    _descriptor = scan.Descriptor;
                    break;
            }

            if (_info.HasDataDescriptor)
            {
                _descriptor ??= ReadDescriptor(_reader._input.Position - _dataStart);
                _info.CompressedSize = _descriptor.Value.CompressedSize;
                _info.UncompressedSize = _descriptor.Value.UncompressedSize;
                _info.Crc = _descriptor.Value.Crc;
                Entry?.SetSizesKnown();
            }
        }

        private ZipDataDescriptor ReadDescriptor(long compressedSize)
        {
            var input = _reader._input;
            Span<byte> buffer = stackalloc byte[ZipDataDescriptor.MaxLookahead];
            var read = input.ReadAtMost(buffer);
            var data = buffer[..read];
            if (!ZipDataDescriptor.TryMatch(data, compressedSize, crc: null, stored: false, _info.HasZip64Extra, requireNextSignature: true, out var descriptor, out var length)
                && !ZipDataDescriptor.TryMatch(data, compressedSize, crc: null, stored: false, _info.HasZip64Extra, requireNextSignature: false, out descriptor, out length))
            {
                throw new InvalidDataException($"The data descriptor of the zip entry '{_info.FullName}' is invalid.");
            }

            input.Unread(data[length..]);
            return descriptor;
        }
    }
}
