using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>
/// Reads a RAR archive forward only, from a stream that does not need to be seekable. In a solid archive, the content of
/// the entries that are not read is decoded and discarded, as the next entries depend on it. The volumes of a
/// multi-volume archive come from <see cref="ArchiveReaderOptions.VolumeResolver"/>.
/// </summary>
internal sealed class RarForwardReader : ArchiveReader
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly ArchiveReaderOptions _options;
    private readonly RarKeyStore _keys;
    private readonly List<Stream> _volumes = [];
    private RarHeaderReader? _reader;
    private int _volumeIndex;
    private bool _isSolid;

    // The decoder of the solid stream, or null; broken when an entry of the solid stream could not be decoded
    private RarUnpacker? _unpacker;
    private Exception? _solidError;

    private RarEntryData? _current;
    private PackedStream? _currentPacked;
    private Stream? _currentDecoded;
    private bool _currentOpened;
    private int _generation;

    public RarForwardReader(Stream stream, ArchiveReaderOptions? options, bool leaveOpen)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        _options = options ?? new ArchiveReaderOptions();
        _keys = new RarKeyStore(_options.Password);
    }

    public override ArchiveFormat Format => ArchiveFormat.Rar;

    /// <summary>Gets the version of the format of the archive, once the first entry is read.</summary>
    public RarFormatVersion FormatVersion { get; private set; }

    private protected override ArchiveEntry? ReadNextEntry()
    {
        FinishCurrent();
        _generation++;
        if (_reader is null)
        {
            _reader = OpenVolume(_stream);
            FormatVersion = _reader.Format;
            _isSolid = _reader.MainHeader.IsSolid;
        }

        while (true)
        {
            var header = _reader.ReadNext();
            if (header is null or RarEndHeader)
            {
                var hasNext = _reader.MainHeader.IsVolume && (header as RarEndHeader)?.HasNextVolume is not false;
                if (hasNext && TryOpenNextVolume())
                    continue;

                return null;
            }

            if (header is RarFileHeader { Kind: RarHeaderKind.File, SplitBefore: false } file)
            {
                var data = new RarEntryData(file);
                data.Parts.Add(new RarDataPart(_volumeIndex, file.DataOffset, file.DataSize));
                _current = data;
                _currentPacked = new PackedStream(this, data, file.DataSize);
                _currentOpened = false;
                var generation = _generation;
                return new RarArchiveEntry(data, archive: null, entry => OpenCurrent(entry, generation));
            }
        }
    }

    private RarHeaderReader OpenVolume(Stream stream)
    {
        var reader = new RarHeaderReader(new RarInput(stream), _options, _keys);
        reader.ReadSignatureAndMainHeader();
        return reader;
    }

    private bool TryOpenNextVolume()
    {
        var next = _options.VolumeResolver?.Invoke(_volumeIndex + 1);
        if (next is null)
            return false;

        _volumes.Add(next);
        _volumeIndex++;
        _reader = OpenVolume(next);
        if (_reader.Format != FormatVersion)
            throw new InvalidDataException("The volumes of the RAR archive have different formats.");

        return true;
    }

    /// <summary>Moves to the next part of the data of an entry split between volumes.</summary>
    private long MoveToNextPart(RarEntryData data)
    {
        while (true)
        {
            var header = _reader!.ReadNext();
            if (header is null or RarEndHeader)
            {
                if (!TryOpenNextVolume())
                    throw new InvalidDataException($"The entry '{data.Header.Name}' continues in a volume that is missing.");

                continue;
            }

            if (header is RarFileHeader { Kind: RarHeaderKind.File, SplitBefore: true } file)
            {
                if (!string.Equals(file.Name, data.Header.Name, StringComparison.Ordinal))
                    throw new InvalidDataException($"The entry '{data.Header.Name}' does not continue in the next volume.");

                data.AddPart(file);
                data.Parts.Add(new RarDataPart(_volumeIndex, file.DataOffset, file.DataSize));
                return file.DataSize;
            }

            if (header.Kind is RarHeaderKind.File)
                throw new InvalidDataException($"The entry '{data.Header.Name}' does not continue in the next volume.");
        }
    }

    private Stream OpenCurrent(RarArchiveEntry entry, int generation)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (generation != _generation)
            throw new InvalidOperationException("The reader moved past this entry: its content can no longer be read.");

        if (_currentOpened)
            throw new InvalidOperationException("The content of the entry is already opened: the forward-only reader reads it once.");

        _currentOpened = true;
        var data = entry.Data;
        var header = data.Header;
        if (header.IsDirectory || header.RedirectionType is not RarRedirectionType.None)
            return new MemoryStream([], writable: false);

        var decoded = OpenDecoded(data, out var hashKey, out var passwordChecked);
        return new RarVerifyingStream(new NonClosingStream(decoded), header.Name, header.UnpackedSize, () => (data.Crc32, data.Blake2), data.Blake2 is not null, hashKey, header.IsEncrypted, passwordChecked);
    }

    private Stream OpenDecoded(RarEntryData data, out byte[]? hashKey, out bool passwordChecked)
    {
        var header = data.Header;
        RarDecoding.CheckSupported(header);
        hashKey = null;
        passwordChecked = false;
        Stream packed = _currentPacked!;
        if (header.IsEncrypted)
        {
            packed = RarDecoding.Decrypt(packed, header, _keys, out hashKey);
            passwordChecked = header.Encryption?.PasswordCheck is not null;
        }

        RarUnpacker? unpacker = null;
        var solid = false;
        if (data.IsCompressed)
        {
            if (header.IsSolid && _isSolid)
            {
                if (_solidError is not null)
                {
                    var message = $"The entry '{header.Name}' cannot be decoded: a previous entry of the solid archive could not be decoded. {_solidError.Message}";
                    throw _solidError switch
                    {
                        NotSupportedException => new NotSupportedException(message, _solidError),
                        ArchivePasswordException => new ArchivePasswordException(message, _solidError),
                        _ => new InvalidDataException(message, _solidError),
                    };
                }

                if (_unpacker is not null && RarDecoding.IsCompatible(_unpacker, header) && !_unpacker.IsBroken && _unpacker.IsFileFinished)
                {
                    unpacker = _unpacker;
                    solid = true;
                }
            }

            unpacker ??= RarDecoding.CreateUnpacker(header, _options.MaxMemoryUsage);
            _unpacker = unpacker;
        }

        _currentDecoded = RarDecoding.OpenDecoded(packed, header, unpacker, solid);
        return _currentDecoded;
    }

    private void FinishCurrent()
    {
        var data = _current;
        if (data is null)
            return;

        _current = null;
        try
        {
            if (_isSolid && data.IsCompressed && _solidError is null)
            {
                try
                {
                    if (!_currentOpened)
                    {
                        _currentOpened = true;
                        OpenDecoded(data, out _, out _);
                    }

                    if (_currentDecoded is RarUnpackStream unpack)
                    {
                        unpack.Drain();
                        if (!unpack.IsCompleted || unpack.Unpacker.IsBroken)
                        {
                            _solidError = new InvalidDataException($"The data of the entry '{data.Header.Name}' are corrupted.");
                        }
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or ArchivePasswordException or NotSupportedException)
                {
                    _solidError = ex;
                }
            }

            _currentDecoded?.Dispose();
            _currentDecoded = null;
            _currentPacked?.SkipToEnd();
        }
        finally
        {
            _currentPacked = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _currentDecoded?.Dispose();
            _currentPacked?.Dispose();
            foreach (var volume in _volumes)
            {
                volume.Dispose();
            }

            if (!_leaveOpen)
            {
                _stream.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>The compressed data of the current entry, in the volumes. Disposing it does nothing: the reader skips the rest.</summary>
    private sealed class PackedStream(RarForwardReader reader, RarEntryData data, long length) : ReadOnlyStreamBase
    {
        private long _remaining = length;

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
                return 0;

            while (_remaining is 0)
            {
                if (!data.IsSplitAfter)
                    return 0;

                _remaining = reader.MoveToNextPart(data);
            }

            if (buffer.Length > _remaining)
            {
                buffer = buffer[..(int)_remaining];
            }

            var read = reader._reader!.Input.Read(buffer);
            if (read is 0)
                throw new InvalidDataException("The RAR archive is truncated.");

            _remaining -= read;
            return read;
        }

        public void SkipToEnd()
        {
            while (true)
            {
                if (_remaining > 0)
                {
                    if (!reader._reader!.Input.Skip(_remaining))
                        throw new InvalidDataException("The RAR archive is truncated.");

                    _remaining = 0;
                }

                if (!data.IsSplitAfter)
                    return;

                _remaining = reader.MoveToNextPart(data);
            }
        }
    }
}
