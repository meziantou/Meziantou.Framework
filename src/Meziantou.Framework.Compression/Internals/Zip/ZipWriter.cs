using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text;
using Meziantou.Framework.Compression.Internals.IO;
using Meziantou.Framework.Compression.Internals.Lzma;
using Meziantou.Framework.Compression.Internals.Ppmd;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>
/// Writes the entries of a zip archive one after the other, then its central directory. On a seekable stream, the local
/// header of an entry is written with placeholders, and rewritten once the content is written; otherwise the local header
/// has the data descriptor flag (bit 3), and a data descriptor with a signature follows the content. The content of a
/// ZipCrypto entry is always followed by a data descriptor: its encryption header depends on the CRC otherwise, which is
/// not known before the content.
/// </summary>
internal sealed class ZipWriter
{
    public const ushort VersionMadeBySpecification = 63;
    public const int HostMsDos = 0;
    public const int HostUnix = 3;

    private const uint Zip64EndOfCentralDirectorySignature = 0x06064B50;
    private const uint Zip64LocatorSignature = 0x07064B50;
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const int AesSmallFileLength = 20;
    private const long KnownLargeLength = 0xF0000000;
    private const int CopyBufferLength = 81920;

    private readonly Stream _output;
    private readonly bool _seekable;
    private readonly Zip64Mode _zip64;
    private long _position;

    public ZipWriter(Stream output, Zip64Mode zip64)
    {
        _output = output;
        _seekable = output.CanSeek;
        _zip64 = zip64;
        _position = _seekable ? output.Position : 0;
    }

    /// <summary>Gets the stream of the entry being written, or <see langword="null"/>.</summary>
    public EntryWriteStream? OpenStream { get; private set; }

    /// <summary>Checks the settings of an entry: the method and the encryption must be supported.</summary>
    public static void Validate(ZipEntrySettings settings)
    {
        if (settings.IsDirectory)
            return;

        switch (settings.Method)
        {
            case ZipCompressionMethod.Stored or ZipCompressionMethod.Deflate or ZipCompressionMethod.BZip2 or ZipCompressionMethod.Lzma or ZipCompressionMethod.Ppmd or ZipCompressionMethod.Xz:
                break;

            case ZipCompressionMethod.Zstandard:
#if !NET11_0_OR_GREATER
                throw new NotSupportedException("Writing a zip entry compressed with Zstandard (method 93) needs .NET 11 or later, whose System.IO.Compression has a Zstandard encoder.");
#else
                break;
#endif

            default:
                throw new NotSupportedException($"The compression method {settings.Method} cannot be written: it is read only, or not supported.");
        }

        if (settings.Level is not (CompressionLevel.Optimal or CompressionLevel.Fastest or CompressionLevel.NoCompression or CompressionLevel.SmallestSize))
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Level, "The compression level is not supported.");

        if (settings.Password is not null && settings.Encryption is not (ZipEncryption.ZipCrypto or ZipEncryption.Aes128 or ZipEncryption.Aes192 or ZipEncryption.Aes256))
            throw new ArgumentException($"The encryption {settings.Encryption} cannot be written.", nameof(settings));
    }

    /// <summary>Opens the stream that writes the content of a new entry. Disposing the stream completes the entry.</summary>
    public EntryWriteStream OpenEntry(ZipEntrySettings settings)
    {
        if (OpenStream is not null)
            throw new InvalidOperationException("Another entry of the archive is being written: dispose its stream before writing another entry.");

        Validate(settings);
        var stream = new EntryWriteStream(this, CreateRecord(settings), settings);
        OpenStream = stream;
        return stream;
    }

    /// <summary>Writes an entry without content (a directory).</summary>
    public void WriteEntryWithoutContent(ZipEntrySettings settings)
    {
        OpenEntry(settings).Dispose();
    }

    /// <summary>Writes an entry without content (a directory).</summary>
    public ValueTask WriteEntryWithoutContentAsync(ZipEntrySettings settings)
    {
        return OpenEntry(settings).DisposeAsync();
    }

    /// <summary>Copies an entry written by another <see cref="ZipWriter"/> (the staging area of the Update mode): its local header, its content and its data descriptor.</summary>
    public void CopyEntry(Stream source, ZipWriterRecord record)
    {
        source.Position = record.LocalHeaderOffset;
        record.LocalHeaderOffset = _position;
        CopyBytes(source, record.TotalLength);
    }

    /// <summary>
    /// Copies an entry of an existing archive without decoding it: its local header and its content, then a new data
    /// descriptor when it has one. The central directory record keeps the fields of the archive, and its extra fields but
    /// the zip64 one, which is written again with the new offset.
    /// </summary>
    public void CopyExistingEntry(Stream source, ZipEntryInfo info, ZipWriterRecord record)
    {
        source.Position = info.LocalHeaderOffset;
        var fixedHeader = new byte[ZipEntryInfo.LocalHeaderLength];
        source.ReadExactlyOrThrow(fixedHeader);
        if (BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader) != ZipEntryInfo.LocalHeaderSignature)
            throw new InvalidDataException($"The local header of the zip entry '{info.FullName}' is not found.");

        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.AsSpan(26));
        var variable = new byte[nameLength + BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.AsSpan(28))];
        source.ReadExactlyOrThrow(variable);
        record.LocalZip64 = HasZip64Extra(variable.AsSpan(nameLength));
        record.LocalHeaderOffset = _position;
        if (record.RewriteLocalName)
        {
            // The name of the central directory record, with its UTF-8 flag, in the local header too
            var flags = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.AsSpan(6)) | ZipEntryInfo.FlagUtf8);
            BinaryPrimitives.WriteUInt16LittleEndian(fixedHeader.AsSpan(6), flags);
            BinaryPrimitives.WriteUInt16LittleEndian(fixedHeader.AsSpan(26), checked((ushort)record.Name.Length));
            variable = [.. record.Name, .. variable.AsSpan(nameLength)];
        }

        WriteBytes(fixedHeader);
        WriteBytes(variable);
        CopyBytes(source, info.CompressedSize);
        var length = fixedHeader.Length + variable.Length + info.CompressedSize;
        if (record.HasDataDescriptor)
        {
            var descriptor = record.BuildDataDescriptor();
            WriteBytes(descriptor);
            length += descriptor.Length;
        }

        record.TotalLength = length;
    }

    /// <summary>Gets a value indicating whether extra fields have a zip64 extra field.</summary>
    public static bool HasZip64Extra(ReadOnlySpan<byte> extra)
    {
        while (extra.Length >= 4)
        {
            var id = BinaryPrimitives.ReadUInt16LittleEndian(extra);
            var size = BinaryPrimitives.ReadUInt16LittleEndian(extra[2..]);
            if (id == ZipWriterRecord.Zip64ExtraId)
                return true;

            if (size > extra.Length - 4)
                return false;

            extra = extra[(4 + size)..];
        }

        return false;
    }

    /// <summary>Removes the zip64 extra field of extra fields (the writer writes its own). The bytes after a truncated field are kept.</summary>
    public static byte[] RemoveZip64Extra(ReadOnlySpan<byte> extra)
    {
        var result = new List<byte>(extra.Length);
        while (extra.Length >= 4)
        {
            var id = BinaryPrimitives.ReadUInt16LittleEndian(extra);
            var size = BinaryPrimitives.ReadUInt16LittleEndian(extra[2..]);
            if (size > extra.Length - 4)
                break;

            if (id != ZipWriterRecord.Zip64ExtraId)
            {
                result.AddRange(extra[..(4 + size)]);
            }

            extra = extra[(4 + size)..];
        }

        result.AddRange(extra);
        return [.. result];
    }

    /// <summary>Writes the central directory and the end of central directory records (and the zip64 ones when needed).</summary>
    public void WriteCentralDirectory(IReadOnlyList<ZipWriterRecord> records, byte[] comment)
    {
        var buffer = BuildCentralDirectory(records, comment);
        buffer.CopyPendingTo(_output);
        _output.Flush();
    }

    /// <summary>Writes the central directory and the end of central directory records (and the zip64 ones when needed).</summary>
    public async ValueTask WriteCentralDirectoryAsync(IReadOnlyList<ZipWriterRecord> records, byte[] comment, CancellationToken cancellationToken)
    {
        var buffer = BuildCentralDirectory(records, comment);
        await buffer.CopyPendingToAsync(_output, cancellationToken).ConfigureAwait(false);
        await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Encodes a name or a comment: ASCII, or UTF-8 when <paramref name="utf8"/> is set.</summary>
    public static byte[] Encode(string value, bool utf8) => utf8 ? Encoding.UTF8.GetBytes(value) : Encoding.ASCII.GetBytes(value);

    /// <summary>
    /// Encodes the comment of an entry whose name is written already: UTF-8 when the entry has the UTF-8 flag, otherwise the
    /// code page 437 when it can, otherwise UTF-8 with the flag added (to the central directory record only).
    /// </summary>
    public static byte[] EncodeComment(string value, ref ushort flags)
    {
        if ((flags & ZipEntryInfo.FlagUtf8) != 0)
            return Encoding.UTF8.GetBytes(value);

        if (Cp437.TryGetBytes(value) is { } bytes)
            return bytes;

        flags |= ZipEntryInfo.FlagUtf8;
        return Encoding.UTF8.GetBytes(value);
    }

    /// <summary>Encodes the comment of the archive, which has no encoding flag: the code page 437 when it can (the encoding the readers assume), otherwise UTF-8.</summary>
    public static byte[] EncodeArchiveComment(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return [];

        return Cp437.TryGetBytes(value) ?? Encoding.UTF8.GetBytes(value);
    }

    /// <summary>Gets the MS-DOS date and time of a time, in the local time zone, clamped to the range of MS-DOS (1980 to 2107).</summary>
    public static (ushort Date, ushort Time) ToDosTime(DateTimeOffset value)
    {
        if (value.UtcDateTime.Year < 1980)
            return (0x21, 0); // 1980-01-01 00:00:00

        if (value.UtcDateTime.Year > 2107)
            return ((127 << 9) | (12 << 5) | 31, (23 << 11) | (59 << 5) | 29); // 2107-12-31 23:59:58

        var local = value.ToLocalTime().DateTime;
        if (local.Year < 1980)
            return (0x21, 0);

        if (local.Year > 2107)
            return ((127 << 9) | (12 << 5) | 31, (23 << 11) | (59 << 5) | 29);

        var date = ((local.Year - 1980) << 9) | (local.Month << 5) | local.Day;
        var time = (local.Hour << 11) | (local.Minute << 5) | (local.Second / 2);
        return ((ushort)date, (ushort)time);
    }

    private ZipWriterRecord CreateRecord(ZipEntrySettings settings)
    {
        var comment = settings.Comment;
        var utf8 = !Ascii.IsValid(settings.FullName) || !Ascii.IsValid(comment);
        var name = Encode(settings.FullName, utf8);
        if (name.Length > ushort.MaxValue)
            throw new ArgumentException("The name of the entry is longer than 65535 bytes.", nameof(settings));

        var commentBytes = Encode(comment, utf8);
        if (commentBytes.Length > ushort.MaxValue)
            throw new ArgumentException("The comment of the entry is longer than 65535 bytes.", nameof(settings));

        var directory = settings.IsDirectory;
        var method = directory ? ZipCompressionMethod.Stored : settings.Method;
        var encryption = directory || settings.Password is null ? ZipEncryption.None : settings.Encryption;
        var isAes = encryption is ZipEncryption.Aes128 or ZipEncryption.Aes192 or ZipEncryption.Aes256;

        ushort flags = utf8 ? ZipEntryInfo.FlagUtf8 : (ushort)0;
        if (encryption is not ZipEncryption.None)
        {
            flags |= ZipEntryInfo.FlagEncrypted;
        }

        if (!directory && (!_seekable || encryption is ZipEncryption.ZipCrypto))
        {
            flags |= ZipEntryInfo.FlagDataDescriptor;
        }

        switch (method)
        {
            case ZipCompressionMethod.Lzma:
                flags |= ZipWriterRecord.FlagLzmaEndMarker;
                break;

            case ZipCompressionMethod.Deflate:
                // The deflate option bits (APPNOTE 4.4.4): maximum, fast and super fast
                flags |= settings.Level switch
                {
                    CompressionLevel.SmallestSize => 0x2,
                    CompressionLevel.Fastest => 0x4,
                    CompressionLevel.NoCompression => 0x6,
                    _ => 0,
                };
                break;
        }

        var localZip64 = !directory && (_zip64 is Zip64Mode.Always || settings.KnownLength >= KnownLargeLength);
        var (dosDate, dosTime) = ToDosTime(settings.LastWriteTime);
        var unixSeconds = settings.LastWriteTime.ToUnixTimeSeconds();
        return new ZipWriterRecord
        {
            Name = name,
            Comment = commentBytes,
            VersionMadeBy = settings.VersionMadeBy,
            VersionNeeded = GetVersionNeeded(method, directory, localZip64, encryption),
            Flags = flags,
            RawMethod = isAes ? ZipEntryInfo.AesMethod : (ushort)method,
            Method = (ushort)method,
            DosDate = dosDate,
            DosTime = dosTime,
            UnixTime = unixSeconds is >= int.MinValue and <= int.MaxValue ? (int)unixSeconds : null,
            ExternalAttributes = settings.ExternalAttributes,
            Encryption = encryption,

            // AE-2 (no CRC) for the small entries, as WinZip does; the size of an entry is not known before its local header
            // on a stream that is not seekable: AE-2 there too, the authentication code checks the content
            AesVendorVersion = isAes ? (_seekable ? (ushort)1 : (ushort)2) : (ushort)0,
            LocalZip64 = localZip64,
            AlwaysZip64 = _zip64 is Zip64Mode.Always,
            LocalHeaderOffset = _position,
        };
    }

    public static ushort GetVersionNeeded(ZipCompressionMethod method, bool directory, bool zip64, ZipEncryption encryption)
    {
        int version = directory ? 20 : method switch
        {
            ZipCompressionMethod.Stored => 10,
            ZipCompressionMethod.Deflate => 20,
            ZipCompressionMethod.BZip2 => 46,
            _ => 63,
        };

        if (encryption is ZipEncryption.ZipCrypto)
        {
            version = Math.Max(version, 20);
        }
        else if (encryption is not ZipEncryption.None)
        {
            version = Math.Max(version, 51);
        }

        if (zip64)
        {
            version = Math.Max(version, 45);
        }

        return (ushort)version;
    }

    private static Stream? CreateCompressor(ZipEntrySettings settings, Stream output)
    {
        var level = settings.Level;
        switch (settings.Method)
        {
            case ZipCompressionMethod.Stored:
                return null;

            case ZipCompressionMethod.Deflate:
                return new DeflateStream(output, level, leaveOpen: true);

            case ZipCompressionMethod.BZip2:
                return new BZip2Stream(output, level, leaveOpen: true);

            case ZipCompressionMethod.Xz:
                return new XzStream(output, level, leaveOpen: true);

#if NET11_0_OR_GREATER
            case ZipCompressionMethod.Zstandard:
                // ZstandardStream is not available on the browser (WebAssembly)
                if (OperatingSystem.IsBrowser())
                    throw new PlatformNotSupportedException("The Zstandard compression is not supported on the browser.");

                return new ZstandardStream(output, level, leaveOpen: true);
#endif

            case ZipCompressionMethod.Lzma:
            {
                // The version of the LZMA SDK (9.20, as 7-Zip writes it), the length of the properties, then the properties
                var encoder = new LzmaEncoderStream(output, LzmaEncoderSettings.FromPreset(CodecStreamCore.GetPreset(level)), writeEndMarker: true, leaveOpen: true);
                output.Write([9, 20, 5, 0]);
                output.Write(encoder.Properties);
                return encoder;
            }

            case ZipCompressionMethod.Ppmd:
            {
                // The levels of 7-Zip, and its smaller model for a short content of a known length
                var sevenZipLevel = level switch
                {
                    CompressionLevel.Optimal => 5,
                    CompressionLevel.SmallestSize => 9,
                    _ => 1,
                };

                var (order, memoryInMiB, restoreMethod) = Ppmd8EncoderStream.GetDefaultParameters(sevenZipLevel, settings.KnownLength);
                Span<byte> header = stackalloc byte[2];
                BinaryPrimitives.WriteUInt16LittleEndian(header, Ppmd8EncoderStream.GetZipHeader(order, memoryInMiB, restoreMethod));
                output.Write(header);
                return new Ppmd8EncoderStream(output, order, memoryInMiB << 20, restoreMethod, leaveOpen: true);
            }

            default:
                throw new NotSupportedException($"The compression method {settings.Method} cannot be written.");
        }
    }

    private PendingWriteStream BuildCentralDirectory(IReadOnlyList<ZipWriterRecord> records, byte[] comment)
    {
        var buffer = new PendingWriteStream();
        var start = _position;
        foreach (var record in records)
        {
            record.WriteCentralRecord(buffer);
        }

        var size = buffer.TotalWritten;
        long count = records.Count;
        var end = start + size;
        if (_zip64 is Zip64Mode.Always || count >= ushort.MaxValue || start >= ZipWriterRecord.MaxClassicValue || size >= ZipWriterRecord.MaxClassicValue)
        {
            Span<byte> record = stackalloc byte[56 + 20];
            BinaryPrimitives.WriteUInt32LittleEndian(record, Zip64EndOfCentralDirectorySignature);
            BinaryPrimitives.WriteUInt64LittleEndian(record[4..], 44);
            BinaryPrimitives.WriteUInt16LittleEndian(record[12..], VersionMadeBySpecification);
            BinaryPrimitives.WriteUInt16LittleEndian(record[14..], 45);
            BinaryPrimitives.WriteUInt32LittleEndian(record[16..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(record[20..], 0);
            BinaryPrimitives.WriteUInt64LittleEndian(record[24..], (ulong)count);
            BinaryPrimitives.WriteUInt64LittleEndian(record[32..], (ulong)count);
            BinaryPrimitives.WriteUInt64LittleEndian(record[40..], (ulong)size);
            BinaryPrimitives.WriteUInt64LittleEndian(record[48..], (ulong)start);

            var locator = record[56..];
            BinaryPrimitives.WriteUInt32LittleEndian(locator, Zip64LocatorSignature);
            BinaryPrimitives.WriteUInt32LittleEndian(locator[4..], 0);
            BinaryPrimitives.WriteUInt64LittleEndian(locator[8..], (ulong)end);
            BinaryPrimitives.WriteUInt32LittleEndian(locator[16..], 1);
            buffer.Write(record);
        }

        Span<byte> eocd = stackalloc byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(eocd, EndOfCentralDirectorySignature);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd[8..], (ushort)Math.Min(count, ushort.MaxValue));
        BinaryPrimitives.WriteUInt16LittleEndian(eocd[10..], (ushort)Math.Min(count, ushort.MaxValue));
        BinaryPrimitives.WriteUInt32LittleEndian(eocd[12..], (uint)Math.Min(size, ZipWriterRecord.MaxClassicValue));
        BinaryPrimitives.WriteUInt32LittleEndian(eocd[16..], (uint)Math.Min(start, ZipWriterRecord.MaxClassicValue));
        BinaryPrimitives.WriteUInt16LittleEndian(eocd[20..], (ushort)comment.Length);
        buffer.Write(eocd);
        buffer.Write(comment);
        _position += buffer.TotalWritten;
        return buffer;
    }

    private void WriteBytes(ReadOnlySpan<byte> data)
    {
        _output.Write(data);
        _position += data.Length;
    }

    private void CopyBytes(Stream source, long length)
    {
        var buffer = new byte[(int)Math.Min(CopyBufferLength, Math.Max(length, 1))];
        while (length > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (read is 0)
                throw new InvalidDataException("The content of a zip entry is truncated.");

            WriteBytes(buffer.AsSpan(0, read));
            length -= read;
        }
    }

    /// <summary>
    /// The write-only stream of the content of a new entry: the content is compressed, encrypted, and written to memory,
    /// which is copied to the archive after each write. Disposing it completes the entry.
    /// </summary>
    internal sealed class EntryWriteStream : WriteOnlyStreamBase
    {
        private const int ChunkLength = 64 * 1024;

        private readonly ZipWriter _writer;
        private readonly ZipWriterRecord _record;
        private readonly ZipEntrySettings _settings;
        private readonly System.IO.Hashing.Crc32 _crc = new();
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "A memory buffer, which has nothing to release")]
        private PendingWriteStream _pending = null!;
        private long _contentStart;
        private Stream? _encryptor;
        private Stream? _compressor;
        private bool _compressorCreated;
        private long _length;
        private bool _completed;

        public EntryWriteStream(ZipWriter writer, ZipWriterRecord record, ZipEntrySettings settings)
        {
            _writer = writer;
            _record = record;
            _settings = settings;
            StartContent();
        }

        /// <summary>Gets or sets the action run once the entry is complete, with its record.</summary>
        public Action<ZipWriterRecord>? Completed { get; set; }

        public override bool CanWrite => !_completed;

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            while (!buffer.IsEmpty)
            {
                var chunk = buffer[..Math.Min(buffer.Length, ChunkLength)];
                Append(chunk);
                _pending.CopyPendingTo(_writer._output);
                buffer = buffer[chunk.Length..];
            }
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            while (!buffer.IsEmpty)
            {
                var chunk = buffer[..Math.Min(buffer.Length, ChunkLength)];
                Append(chunk.Span);
                await _pending.CopyPendingToAsync(_writer._output, cancellationToken).ConfigureAwait(false);
                buffer = buffer[chunk.Length..];
            }
        }

        public override void Flush()
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            _writer._output.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            return _writer._output.FlushAsync(cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_completed)
            {
                _completed = true;
                var patch = Complete();
                _pending.CopyPendingTo(_writer._output);
                if (patch is not null)
                {
                    var output = _writer._output;
                    output.Position = _record.LocalHeaderOffset;
                    output.Write(patch);
                    output.Position = _writer._position;
                }

                Completed?.Invoke(_record);
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_completed)
            {
                _completed = true;
                var patch = Complete();
                await _pending.CopyPendingToAsync(_writer._output, CancellationToken.None).ConfigureAwait(false);
                if (patch is not null)
                {
                    var output = _writer._output;
                    output.Position = _record.LocalHeaderOffset;
                    await output.WriteAsync(patch).ConfigureAwait(false);
                    output.Position = _writer._position;
                }

                Completed?.Invoke(_record);
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>Writes the local header and the encryption header to memory: nothing reaches the archive before the first byte of content.</summary>
        private void StartContent()
        {
            _pending = new PendingWriteStream();
            _pending.Write(_record.BuildLocalHeader());
            _contentStart = _pending.TotalWritten;
            _encryptor = _record.Encryption switch
            {
                ZipEncryption.ZipCrypto => new ZipCryptoEncryptStream(_pending, _settings.Password!, (byte)(_record.DosTime >> 8)),
                ZipEncryption.Aes128 or ZipEncryption.Aes192 or ZipEncryption.Aes256 => new WinZipAesEncryptStream(_pending, _settings.Password!, _record.Encryption),
                _ => null,
            };
        }

        private void Append(ReadOnlySpan<byte> data)
        {
            _crc.Append(data);
            _length += data.Length;
            (GetCompressor() ?? _encryptor ?? _pending).Write(data);
        }

        private Stream? GetCompressor()
        {
            if (!_compressorCreated)
            {
                _compressorCreated = true;
                _compressor = _settings.IsDirectory ? null : CreateCompressor(_settings, _encryptor ?? _pending);
            }

            return _compressor;
        }

        /// <summary>Flushes the codecs, adds the data descriptor, and returns the local header to write over the first one, if any.</summary>
        private byte[]? Complete()
        {
            var writer = _writer;
            writer.OpenStream = null;
            var record = _record;
            if (!_compressorCreated && record.Method is not (ushort)ZipCompressionMethod.Stored)
            {
                // An empty file is stored, as 7-Zip and ZipArchive write it: the zip decoder of 7-Zip expects no data for an
                // empty XZ entry, and the deflate encoder of .NET 10 writes nothing. Nothing is written to the archive yet:
                // the local header is written again.
                _encryptor?.Dispose();
                record.Method = (ushort)ZipCompressionMethod.Stored;
                if (!record.IsAes)
                {
                    record.RawMethod = (ushort)ZipCompressionMethod.Stored;
                }

                record.Flags &= unchecked((ushort)~0x0006);
                record.VersionNeeded = GetVersionNeeded(ZipCompressionMethod.Stored, directory: false, record.LocalZip64, record.Encryption);
                _settings.Method = ZipCompressionMethod.Stored;
                StartContent();
            }

            GetCompressor()?.Dispose();
            _encryptor?.Dispose();

            record.CompressedSize = _pending.TotalWritten - _contentStart;
            record.UncompressedSize = _length;
            record.Crc = _crc.GetCurrentHashAsUInt32();

            byte[]? patch = null;
            if (!record.HasDataDescriptor && _length + record.CompressedSize > 0)
            {
                if (record.IsAes && _length < AesSmallFileLength)
                {
                    record.AesVendorVersion = 2;
                }

                if (!record.LocalZip64 && (record.CompressedSize >= ZipWriterRecord.MaxClassicValue || record.UncompressedSize >= ZipWriterRecord.MaxClassicValue))
                {
                    // The local header has no room for the zip64 sizes: they go to a zip64 data descriptor
                    record.Flags |= ZipEntryInfo.FlagDataDescriptor;
                    record.VersionNeeded = Math.Max(record.VersionNeeded, (ushort)45);
                }

                if (_pending.TotalWritten == _pending.PendingLength)
                {
                    // Nothing reached the archive yet: the local header is updated in memory
                    _pending.Overwrite(0, record.BuildLocalHeader());
                }
                else
                {
                    patch = record.BuildLocalHeader();
                }
            }

            if (record.HasDataDescriptor)
            {
                _pending.Write(record.BuildDataDescriptor());
            }

            record.TotalLength = _pending.TotalWritten;
            writer._position += _pending.TotalWritten;
            return patch;
        }
    }
}
