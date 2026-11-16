using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Compression.Internals.IO;
using Meziantou.Framework.Compression.Internals.Zip;

namespace Meziantou.Framework.Compression;

/// <summary>
/// A zip archive opened for reading from a seekable stream. Its entries are read from the central directory when it is
/// opened, and their content can be read in any order.
/// </summary>
/// <remarks>
/// <para>
/// It reads the compression methods of <see cref="ZipCompressionMethod"/> that are not marked as not supported, the
/// traditional PKWARE encryption (ZipCrypto) and the WinZip AES encryption, zip64 archives, archives with data prepended
/// (self-extracting archives), and split archives: the Info-ZIP <c>.z01</c>, <c>.z02</c>, ..., <c>.zip</c> volumes and the
/// <c>.zip.001</c>, <c>.zip.002</c>, ... parts of 7-Zip. Writing zip archives is the job of <see cref="System.IO.Compression.ZipArchive"/>.
/// </para>
/// <para>
/// The names that are not marked as UTF-8 (general purpose flag bit 11) nor have an Info-ZIP Unicode path extra field are
/// decoded with <see cref="ArchiveReaderOptions.EntryNameEncoding"/>, or with the code page 437 when it is <see langword="null"/>.
/// The backslashes of the names of the entries created on MS-DOS or Windows are replaced with <c>/</c>. A password is
/// encoded in UTF-8.
/// </para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Compression.Tests/ReadOnlyZipArchiveTests.cs" ref:region="ReadOnlyZipArchive_Read" ref:dedent="true">
/// </code>
/// </example>
public sealed class ReadOnlyZipArchive : Archive
{
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint Zip64EndOfCentralDirectorySignature = 0x06064B50;
    private const uint Zip64LocatorSignature = 0x07064B50;
    private const uint ArchiveExtraDataSignature = 0x08064B50;
    private const int EndOfCentralDirectoryLength = 22;
    private const int Zip64LocatorLength = 20;
    private const int Zip64EndOfCentralDirectoryLength = 56;

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly ArchiveReaderOptions _options;
    private readonly List<ReadOnlyZipArchiveEntry> _entries = [];
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "It is _stream, which is disposed")]
    private readonly ConcatenatedStream? _volumes;

    /// <summary>Opens a zip archive from a seekable stream.</summary>
    /// <param name="stream">
    /// The archive: a readable and seekable stream. For a split archive, it is the first volume (the <c>.z01</c> file of an
    /// Info-ZIP split archive, the <c>.zip.001</c> file of 7-Zip), and <see cref="ArchiveReaderOptions.VolumeResolver"/>
    /// returns the next ones (the <c>.zip</c> file is the last volume of an Info-ZIP split archive).
    /// </param>
    /// <param name="options">The options, or <see langword="null"/> for the default options.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when the archive is disposed.</param>
    /// <exception cref="ArgumentException">The stream is not readable or not seekable.</exception>
    /// <exception cref="InvalidDataException">The stream is not a zip archive, or the archive is corrupted.</exception>
    /// <exception cref="NotSupportedException">The central directory of the archive is encrypted.</exception>
    public ReadOnlyZipArchive(Stream stream, ArchiveReaderOptions? options = null, bool leaveOpen = false)
        : this(stream, options, leaveOpen, options?.VolumeResolver)
    {
    }

    private ReadOnlyZipArchive(Stream stream, ArchiveReaderOptions? options, bool leaveOpen, Func<int, Stream?>? volumeResolver)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("The stream must be readable and seekable. Use a forward-only reader for a stream that is not seekable.", nameof(stream));

        _options = options ?? new ArchiveReaderOptions();
        if (volumeResolver is not null)
        {
            _volumes = new ConcatenatedStream(stream, volumeResolver, leaveFirstOpen: leaveOpen);
            _stream = _volumes;
            _ownsStream = true;
        }
        else
        {
            _stream = stream;
            _ownsStream = !leaveOpen;
        }

        try
        {
            ReadCentralDirectory();
        }
        catch
        {
            if (_ownsStream)
            {
                _stream.Dispose();
            }

            throw;
        }
    }

    /// <inheritdoc/>
    public override ArchiveFormat Format => ArchiveFormat.Zip;

    /// <summary>Gets the entries of the archive, in the order of the central directory.</summary>
    public new IReadOnlyList<ReadOnlyZipArchiveEntry> Entries => _entries;

    /// <summary>Gets the comment of the archive, or an empty string.</summary>
    public string Comment { get; private set; } = "";

    /// <summary>
    /// Opens a zip archive from a file. The volumes of a split archive are found next to it: the <c>.z01</c>, <c>.z02</c>, ...
    /// files of an Info-ZIP split archive whose last volume is <paramref name="path"/> (or whose path is one of the volumes),
    /// or the <c>.002</c>, <c>.003</c>, ... parts that follow a <c>.zip.001</c> part of 7-Zip.
    /// When <see cref="ArchiveReaderOptions.VolumeResolver"/> is set, it is used instead, and <paramref name="path"/> is the first volume.
    /// </summary>
    /// <param name="path">The path of the archive.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default options.</param>
    /// <returns>The archive.</returns>
    /// <exception cref="InvalidDataException">The file is not a zip archive, or the archive is corrupted.</exception>
    public static ReadOnlyZipArchive Open(string path, ArchiveReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (options?.VolumeResolver is not null)
            return new ReadOnlyZipArchive(File.OpenRead(path), options, leaveOpen: false);

        var volumes = ZipVolumes.Find(Path.GetFullPath(path));
        var first = File.OpenRead(volumes[0]);
        Func<int, Stream?>? resolver = volumes.Count > 1 ? index => index < volumes.Count ? File.OpenRead(volumes[index]) : null : null;
        return new ReadOnlyZipArchive(first, options, leaveOpen: false, resolver);
    }

    /// <summary>Gets the entry whose full name is <paramref name="entryName"/> (an ordinal comparison), or <see langword="null"/> when there is none.</summary>
    /// <param name="entryName">The path of the entry in the archive, with <c>/</c> separators.</param>
    /// <returns>The first entry whose <see cref="ArchiveEntry.FullName"/> is <paramref name="entryName"/>, or <see langword="null"/>.</returns>
    public new ReadOnlyZipArchiveEntry? GetEntry(string entryName) => (ReadOnlyZipArchiveEntry?)base.GetEntry(entryName);

    /// <summary>Creates a reader that reads the local headers of a zip archive one after the other, from a stream that does not need to be seekable.</summary>
    internal static ArchiveReader CreateForwardReader(Stream stream, ArchiveReaderOptions? options, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new ZipForwardReader(stream, options, leaveOpen);
    }

    private protected override IReadOnlyList<ArchiveEntry> GetEntries() => _entries;

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsStream)
        {
            _stream.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ReadCentralDirectory()
    {
        var length = _stream.Length;
        if (length < EndOfCentralDirectoryLength)
            throw new InvalidDataException("The stream is not a zip archive: it is too short.");

        // The end of central directory record is followed by a comment of at most 65535 bytes
        var tailLength = (int)Math.Min(length, EndOfCentralDirectoryLength + ushort.MaxValue);
        var tailStart = length - tailLength;
        var tail = ReadAt(tailStart, tailLength);
        var eocdIndex = FindEndOfCentralDirectory(tail, tailStart, length);
        var eocd = tail.AsSpan(eocdIndex);
        var eocdPosition = tailStart + eocdIndex;

        uint diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(eocd[4..]);
        uint centralDirectoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(eocd[6..]);
        long entryCount = BinaryPrimitives.ReadUInt16LittleEndian(eocd[10..]);
        long centralDirectorySize = BinaryPrimitives.ReadUInt32LittleEndian(eocd[12..]);
        long centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(eocd[16..]);
        var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(eocd[20..]);
        var comment = eocd[EndOfCentralDirectoryLength..];
        Comment = DecodeComment(comment[..Math.Min(commentLength, comment.Length)]);

        // The central directory ends where the zip64 end of central directory record, or the end of central directory record, starts
        var centralDirectoryEnd = eocdPosition;
        if (eocdPosition >= Zip64LocatorLength)
        {
            var locator = ReadAt(eocdPosition - Zip64LocatorLength, Zip64LocatorLength);
            if (BinaryPrimitives.ReadUInt32LittleEndian(locator) == Zip64LocatorSignature)
            {
                var zip64Disk = BinaryPrimitives.ReadUInt32LittleEndian(locator.AsSpan(4));
                var zip64Offset = BinaryPrimitives.ReadUInt64LittleEndian(locator.AsSpan(8));
                var record = FindZip64EndOfCentralDirectory(zip64Disk, zip64Offset, eocdPosition - Zip64LocatorLength, diskNumber, out var recordPosition);
                diskNumber = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(16));
                centralDirectoryDisk = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(20));
                entryCount = ToInt64(BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(32)));
                centralDirectorySize = ToInt64(BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(40)));
                centralDirectoryOffset = ToInt64(BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(48)));
                centralDirectoryEnd = recordPosition;
            }
        }

        var multiVolume = CheckVolumes(diskNumber);
        if (centralDirectorySize > centralDirectoryEnd)
            throw new InvalidDataException("The size of the central directory of the zip archive is invalid.");

        // The central directory may not be at its offset when data is prepended to the archive (a self-extracting archive):
        // the offsets are then shifted by the difference, as Info-ZIP does
        var expectedPosition = GetVolumeStart(centralDirectoryDisk, multiVolume) + centralDirectoryOffset;
        var actualPosition = centralDirectoryEnd - centralDirectorySize;
        var delta = 0L;
        if (expectedPosition != actualPosition && !multiVolume && !HasCentralHeaderAt(expectedPosition, centralDirectorySize, centralDirectoryEnd))
        {
            delta = actualPosition - expectedPosition;
            expectedPosition = actualPosition;
        }

        if (expectedPosition < 0 || expectedPosition > centralDirectoryEnd - centralDirectorySize)
            throw new InvalidDataException("The offset of the central directory of the zip archive is invalid.");

        if (centralDirectorySize > Array.MaxLength)
            throw new NotSupportedException("The central directory of the zip archive is too large.");

        // Each record has at least 46 bytes: a larger count is invalid, and must not size an allocation
        if (entryCount > centralDirectorySize / ZipEntryInfo.CentralHeaderLength)
            throw new InvalidDataException("The number of entries of the zip archive is larger than its central directory.");

        var centralDirectory = ReadAt(expectedPosition, (int)centralDirectorySize);
        if (centralDirectory.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(centralDirectory) == ArchiveExtraDataSignature)
            throw new NotSupportedException("The central directory of the zip archive is encrypted, which is not supported.");

        _entries.Capacity = (int)entryCount;
        var data = centralDirectory.AsSpan();
        while (data.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(data) == ZipEntryInfo.CentralHeaderSignature)
        {
            var info = ZipEntryInfo.ParseCentral(data, _options, out var recordLength);
            data = data[recordLength..];

            var offset = GetVolumeStart(info.DiskNumber, multiVolume) + info.LocalHeaderOffset + delta;
            if (offset < 0 || offset > length - ZipEntryInfo.LocalHeaderLength)
                throw new InvalidDataException($"The offset of the local header of the zip entry '{info.FullName}' is invalid.");

            info.LocalHeaderOffset = offset;
            _entries.Add(new ReadOnlyZipArchiveEntry(info, OpenEntry, fromCentralDirectory: true, sizesKnown: true));
        }

        if (_entries.Count < entryCount)
            throw new InvalidDataException("The central directory of the zip archive has fewer entries than its end record says.");
    }

    private static int FindEndOfCentralDirectory(byte[] tail, long tailStart, long length)
    {
        // The record whose comment ends at the end of the stream; otherwise, the last one (trailing data, or a truncated comment)
        int? fallback = null;
        for (var i = tail.Length - EndOfCentralDirectoryLength; i >= 0; i--)
        {
            if (tail[i] != 'P' || BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) != EndOfCentralDirectorySignature)
                continue;

            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20));
            if (tailStart + i + EndOfCentralDirectoryLength + commentLength == length)
                return i;

            fallback ??= i;
        }

        return fallback ?? throw new InvalidDataException("The stream is not a zip archive: the end of central directory record is not found.");
    }

    private byte[] FindZip64EndOfCentralDirectory(uint disk, ulong offset, long locatorPosition, uint lastDisk, out long position)
    {
        // At the offset of the locator; otherwise right before the locator (data prepended to the archive)
        var multiVolume = lastDisk > 0 && _volumes is not null;
        if (offset <= long.MaxValue && (!multiVolume || disk < _volumes!.VolumeCount))
        {
            var candidate = (multiVolume ? _volumes!.GetVolumeStart((int)disk) : 0) + (long)offset;
            if (candidate >= 0 && candidate <= locatorPosition - Zip64EndOfCentralDirectoryLength)
            {
                var record = ReadAt(candidate, Zip64EndOfCentralDirectoryLength);
                if (BinaryPrimitives.ReadUInt32LittleEndian(record) == Zip64EndOfCentralDirectorySignature)
                {
                    position = candidate;
                    return record;
                }
            }
        }

        if (locatorPosition >= Zip64EndOfCentralDirectoryLength)
        {
            position = locatorPosition - Zip64EndOfCentralDirectoryLength;
            var record = ReadAt(position, Zip64EndOfCentralDirectoryLength);
            if (BinaryPrimitives.ReadUInt32LittleEndian(record) == Zip64EndOfCentralDirectorySignature)
                return record;
        }

        throw new InvalidDataException("The zip64 end of central directory record of the zip archive is not found.");
    }

    private bool CheckVolumes(uint lastDisk)
    {
        if (lastDisk is 0)
            return false; // A single volume, or the parts of an archive split by 7-Zip, whose offsets are global

        var volumeCount = _volumes?.VolumeCount ?? 1;
        if (volumeCount is 1)
            throw new InvalidDataException($"The zip archive is the last volume of a split archive of {(long)lastDisk + 1} volumes: open the first volume (.z01) with a volume resolver, or open the archive from its path.");

        if (lastDisk + 1 != volumeCount)
            throw new InvalidDataException($"The split zip archive has {(long)lastDisk + 1} volumes, but {volumeCount} volumes are found.");

        return true;
    }

    private long GetVolumeStart(uint disk, bool multiVolume)
    {
        if (!multiVolume)
            return 0;

        if (disk >= _volumes!.VolumeCount)
            throw new InvalidDataException("A disk number of the split zip archive is invalid.");

        return _volumes.GetVolumeStart((int)disk);
    }

    private bool HasCentralHeaderAt(long position, long centralDirectorySize, long centralDirectoryEnd)
    {
        if (centralDirectorySize is 0 || position < 0 || position > centralDirectoryEnd - 4)
            return false;

        return BinaryPrimitives.ReadUInt32LittleEndian(ReadAt(position, 4)) == ZipEntryInfo.CentralHeaderSignature;
    }

    private string DecodeComment(ReadOnlySpan<byte> comment)
    {
        if (comment.IsEmpty)
            return "";

        return _options.EntryNameEncoding is { } encoding ? encoding.GetString(comment) : Cp437.GetString(comment);
    }

    private byte[] ReadAt(long position, int count)
    {
        var buffer = new byte[count];
        _stream.Position = position;
        _stream.ReadExactlyOrThrow(buffer);
        return buffer;
    }

    private static long ToInt64(ulong value)
    {
        if (value > long.MaxValue)
            throw new InvalidDataException("A size or an offset of the zip64 end of central directory record is invalid.");

        return (long)value;
    }

    private Stream OpenEntry(ReadOnlyZipArchiveEntry entry)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var info = entry.Info;
        ZipEntryOpener.ThrowIfNotSupported(info);

        var header = ReadAt(info.LocalHeaderOffset, ZipEntryInfo.LocalHeaderLength);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != ZipEntryInfo.LocalHeaderSignature)
            throw new InvalidDataException($"The local header of the zip entry '{info.FullName}' is not found.");

        var dataStart = info.LocalHeaderOffset + ZipEntryInfo.LocalHeaderLength + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26)) + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
        if (info.CompressedSize > _stream.Length - dataStart)
            throw new InvalidDataException($"The content of the zip entry '{info.FullName}' is truncated.");

        var raw = new BoundedReadStream(_stream, dataStart, info.CompressedSize);
        return ZipEntryOpener.Open(info, raw, info.UncompressedSize, _options, onEnd: null, isValid: null);
    }
}
