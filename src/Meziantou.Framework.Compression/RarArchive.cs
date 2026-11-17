using System.Text;
using Meziantou.Framework.Compression.Internals.Rar;

namespace Meziantou.Framework.Compression;

/// <summary>
/// A RAR archive (RAR 1.5-4.x or RAR5) opened for random access from a seekable stream. It reads the archives created by
/// an SFX module, solid archives (an entry is decoded after the entries it depends on), multi-volume archives (see
/// <see cref="ArchiveReaderOptions.VolumeResolver"/>), and archives encrypted with AES, including their headers.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Compression.Tests/RarArchiveTests.cs" ref:region="RarArchive_Read" ref:dedent="true">
/// using var stream = File.OpenRead(path);
/// using var archive = new RarArchive(stream);
/// Assert.Equal(RarFormatVersion.Rar5, archive.FormatVersion);
/// Assert.True(archive.IsSolid);
/// foreach (var entry in archive.Entries)
/// {
///     using var content = entry.Open();
///     using var buffer = new MemoryStream();
///     content.CopyTo(buffer);
///     Assert.Equal(entry.Length, buffer.Length);
/// }
/// </code>
/// </example>
public sealed class RarArchive : Archive
{
    private const int MaxCommentLength = 1024 * 1024;

    private readonly List<Stream> _volumes = [];
    private readonly bool _leaveOpen;
    private readonly ArchiveReaderOptions _options;
    private readonly RarKeyStore _keys;
    private readonly List<RarArchiveEntry> _entries = [];
    private readonly List<RarArchiveEntry> _compressedEntries = [];

    // The decoder of the last entry of a solid stream read to its end, and the index of this entry in _compressedEntries
    private RarUnpacker? _cachedUnpacker;
    private int _cachedIndex = -1;

    /// <summary>Opens a RAR archive from a seekable stream.</summary>
    /// <param name="stream">The first volume of the archive. It must be readable and seekable.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open after the archive is disposed.</param>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable or not seekable.</exception>
    /// <exception cref="InvalidDataException">The stream is not a RAR archive, or the archive is corrupted.</exception>
    /// <exception cref="ArchivePasswordException">The headers are encrypted and the password is missing or wrong.</exception>
    /// <exception cref="NotSupportedException">The archive is a RAR 1.4 archive, or uses an encryption that is not supported.</exception>
    public RarArchive(Stream stream, ArchiveReaderOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("The stream must be readable and seekable. Use the forward-only reader for other streams.", nameof(stream));

        _options = options ?? new ArchiveReaderOptions();
        _keys = new RarKeyStore(_options.Password);
        _leaveOpen = leaveOpen;
        _volumes.Add(stream);
        try
        {
            Load();
        }
        catch
        {
            DisposeVolumes();
            throw;
        }
    }

    /// <inheritdoc/>
    public override ArchiveFormat Format => ArchiveFormat.Rar;

    /// <summary>Gets the entries of the archive, in the order of the archive. The service headers (comments, quick open data, ...) are not entries.</summary>
    public new IReadOnlyList<RarArchiveEntry> Entries => _entries;

    /// <summary>Gets the version of the format of the archive.</summary>
    public RarFormatVersion FormatVersion { get; private set; }

    /// <summary>Gets a value indicating whether the archive is solid: the entries continue the compression dictionary of the previous ones.</summary>
    public bool IsSolid { get; private set; }

    /// <summary>Gets a value indicating whether the archive is a volume of a multi-volume archive.</summary>
    public bool IsMultiVolume { get; private set; }

    /// <summary>Gets the number of volumes that were read.</summary>
    public int VolumeCount => _volumes.Count;

    /// <summary>
    /// Gets the comment of the archive, or <see langword="null"/> when there is none, or when it cannot be decoded (a
    /// RAR 1.5 compression, a missing password, corrupted data).
    /// </summary>
    public string? Comment { get; private set; }

    /// <summary>Gets the entry whose full name is <paramref name="entryName"/> (an ordinal comparison), or <see langword="null"/> when there is none.</summary>
    /// <param name="entryName">The path of the entry in the archive, with <c>/</c> separators.</param>
    /// <returns>The first entry whose <see cref="ArchiveEntry.FullName"/> is <paramref name="entryName"/>, or <see langword="null"/>.</returns>
    public new RarArchiveEntry? GetEntry(string entryName)
    {
        ArgumentNullException.ThrowIfNull(entryName);
        foreach (var entry in _entries)
        {
            if (string.Equals(entry.FullName, entryName, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    /// <summary>
    /// Creates a forward-only reader of a RAR archive, which reads a stream that does not need to be seekable. The entries
    /// it returns are <see cref="RarArchiveEntry"/> instances whose <see cref="RarArchiveEntry.Archive"/> is <see langword="null"/>.
    /// </summary>
    internal static ArchiveReader CreateForwardReader(Stream stream, ArchiveReaderOptions? options, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new RarForwardReader(stream, options, leaveOpen);
    }

    private protected override IReadOnlyList<ArchiveEntry> GetEntries() => _entries;

    private void Load()
    {
        var volumeIndex = 0;
        RarEntryData? pending = null;
        var seenFile = false;
        while (true)
        {
            var input = new RarInput(_volumes[volumeIndex]);
            var reader = new RarHeaderReader(input, _options, _keys);
            reader.ReadSignatureAndMainHeader();
            var main = reader.MainHeader;
            if (volumeIndex is 0)
            {
                FormatVersion = reader.Format;
                IsSolid = main.IsSolid;
                IsMultiVolume = main.IsVolume;
                Comment = main.OldComment;
            }
            else if (reader.Format != FormatVersion)
            {
                throw new InvalidDataException("The volumes of the RAR archive have different formats.");
            }

            var hasNextVolume = false;
            while (reader.ReadNext() is { } header)
            {
                switch (header)
                {
                    case RarFileHeader { Kind: RarHeaderKind.File } file:
                        seenFile = true;
                        if (file.SplitBefore)
                        {
                            if (pending is not null && pending.IsSplitAfter && string.Equals(pending.Header.Name, file.Name, StringComparison.Ordinal))
                            {
                                pending.AddPart(file);
                                pending.Parts.Add(new RarDataPart(volumeIndex, file.DataOffset, file.DataSize));
                            }

                            // Otherwise, the entry starts in a volume that is not read: it is skipped
                            break;
                        }

                        pending = new RarEntryData(file);
                        pending.Parts.Add(new RarDataPart(volumeIndex, file.DataOffset, file.DataSize));
                        var entry = new RarArchiveEntry(pending, this, OpenEntry);
                        _entries.Add(entry);
                        if (pending.IsCompressed)
                        {
                            _compressedEntries.Add(entry);
                        }

                        break;

                    case RarFileHeader { Kind: RarHeaderKind.Service } service:
                        if (!seenFile && Comment is null && string.Equals(service.Name, "CMT", StringComparison.Ordinal) && !service.SplitBefore)
                        {
                            Comment = ReadComment(service, volumeIndex);
                        }

                        break;

                    case RarEndHeader end:
                        hasNextVolume = end.HasNextVolume;
                        break;
                }
            }

            if (!main.IsVolume || (!hasNextVolume && pending?.IsSplitAfter is not true))
                break;

            var next = _options.VolumeResolver?.Invoke(volumeIndex + 1);
            if (next is null)
                break;

            if (!next.CanRead || !next.CanSeek)
            {
                next.Dispose();
                throw new InvalidDataException("The volumes of a RAR archive must be readable and seekable streams.");
            }

            _volumes.Add(next);
            volumeIndex++;
        }
    }

    private string? ReadComment(RarFileHeader header, int volumeIndex)
    {
        if (header.UnpackedSize is < 0 or > MaxCommentLength || header.DataSize > MaxCommentLength)
            return null;

        try
        {
            var data = new RarEntryData(header);
            data.Parts.Add(new RarDataPart(volumeIndex, header.DataOffset, header.DataSize));
            var unpacker = data.IsCompressed ? RarDecoding.CreateUnpacker(header, _options.MaxMemoryUsage) : null;
            using var stream = OpenContent(data, unpacker, solid: false);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
            string comment;
            if (header.Format is RarFormatVersion.Rar5)
            {
                comment = Encoding.UTF8.GetString(bytes);
            }
            else if ((header.Attributes & 1) != 0)
            {
                // The comments of RAR 2.9-4.x are UTF-16 when the first bit of their sub-flags is set, OEM text otherwise
                comment = Encoding.Unicode.GetString(bytes);
            }
            else
            {
                comment = _options.EntryNameEncoding?.GetString(bytes) ?? (System.Text.Unicode.Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : Encoding.Latin1.GetString(bytes));
            }

            // The comment may end with a null character
            var end = comment.IndexOf('\0', StringComparison.Ordinal);
            return end >= 0 ? comment[..end] : comment;
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArchivePasswordException)
        {
            return null;
        }
    }

    private Stream OpenEntry(RarArchiveEntry entry)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var data = entry.Data;
        var header = data.Header;
        if (header.IsDirectory || header.RedirectionType is RarRedirectionType.UnixSymlink or RarRedirectionType.WindowsSymlink or RarRedirectionType.WindowsJunction)
            return new MemoryStream([], writable: false);

        if (header.RedirectionType is RarRedirectionType.HardLink or RarRedirectionType.FileCopy)
        {
            var index = _entries.IndexOf(entry);
            for (var i = index - 1; i >= 0; i--)
            {
                var target = _entries[i];
                if (string.Equals(target.FullName, header.RedirectionTarget, StringComparison.Ordinal) && target.HardLinkTarget is null)
                    return target.Open();
            }

            throw new InvalidDataException($"The target of the link '{entry.FullName}', '{header.RedirectionTarget}', is not an entry of the archive.");
        }

        if (data.IsSplitAfter)
            throw new InvalidDataException($"The entry '{entry.FullName}' continues in a volume that is missing.");

        RarDecoding.CheckSupported(header);
        if (!data.IsCompressed)
            return OpenContent(data, unpacker: null, solid: false);

        var position = _compressedEntries.IndexOf(entry);
        var start = position;
        while (start > 0 && _compressedEntries[start].Data.Header.IsSolid)
        {
            start--;
        }

        RarUnpacker unpacker;
        int next;
        var cached = _cachedUnpacker;
        if (cached is not null && _cachedIndex >= start && _cachedIndex < position && !cached.IsBroken && cached.IsFileFinished && RarDecoding.IsCompatible(cached, header))
        {
            unpacker = cached;
            next = _cachedIndex + 1;
        }
        else
        {
            unpacker = RarDecoding.CreateUnpacker(header, _options.MaxMemoryUsage);
            next = start;
        }

        _cachedUnpacker = null;
        _cachedIndex = -1;

        // Decode the entries this one depends on
        var solid = next != start;
        for (; next < position; next++)
        {
            var previous = _compressedEntries[next].Data;
            RarDecoding.CheckSupported(previous.Header);
            if (!RarDecoding.IsCompatible(unpacker, previous.Header))
            {
                unpacker = RarDecoding.CreateUnpacker(previous.Header, _options.MaxMemoryUsage);
                solid = false;
            }

            using (var skipped = (RarUnpackStream)OpenDecoded(previous, unpacker, solid, out _, out _))
            {
                skipped.Drain();
            }

            solid = true;
        }

        if (!RarDecoding.IsCompatible(unpacker, header))
        {
            unpacker = RarDecoding.CreateUnpacker(header, _options.MaxMemoryUsage);
            solid = false;
        }

        return OpenContent(data, unpacker, solid, onCompleted: completed =>
        {
            if (completed && !unpacker.IsBroken && unpacker.IsFileFinished)
            {
                _cachedUnpacker = unpacker;
                _cachedIndex = position;
            }
        });
    }

    private Stream OpenDecoded(RarEntryData data, RarUnpacker? unpacker, bool solid, out byte[]? hashKey, out bool passwordChecked)
    {
        var header = data.Header;
        Stream packed = new RarPartsStream(_volumes, data.Parts);
        hashKey = null;
        passwordChecked = false;
        try
        {
            if (header.IsEncrypted)
            {
                packed = RarDecoding.Decrypt(packed, header, _keys, out hashKey);
                passwordChecked = header.Encryption?.PasswordCheck is not null;
            }

            return RarDecoding.OpenDecoded(packed, header, unpacker, solid);
        }
        catch
        {
            packed.Dispose();
            throw;
        }
    }

    private Stream OpenContent(RarEntryData data, RarUnpacker? unpacker, bool solid, Action<bool>? onCompleted = null)
    {
        var header = data.Header;
        var decoded = OpenDecoded(data, unpacker, solid, out var hashKey, out var passwordChecked);
        Stream result = new RarVerifyingStream(decoded, header.Name, header.UnpackedSize, () => (data.Crc32, data.Blake2), data.Blake2 is not null, hashKey, header.IsEncrypted, passwordChecked);
        if (onCompleted is not null)
        {
            result = new CompletionStream(result, decoded as RarUnpackStream, onCompleted);
        }

        return result;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeVolumes();
        }

        base.Dispose(disposing);
    }

    private void DisposeVolumes()
    {
        for (var i = _leaveOpen ? 1 : 0; i < _volumes.Count; i++)
        {
            _volumes[i].Dispose();
        }
    }

    /// <summary>Reports, when the content stream is disposed, whether the decoder finished the entry.</summary>
    private sealed class CompletionStream(Stream inner, RarUnpackStream? unpackStream, Action<bool> onCompleted) : Internals.IO.ReadOnlyStreamBase
    {
        private bool _disposed;
        private bool _ended;

        public override long Length => inner.Length;

        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer);
            if (read is 0 && !buffer.IsEmpty)
            {
                _ended = true;
            }

            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                var completed = false;
                if (_ended && unpackStream is not null)
                {
                    // The content is read: decode the end of the compressed data so that the next entry of the solid stream can follow
                    try
                    {
                        unpackStream.Drain();
                        completed = unpackStream.IsCompleted;
                    }
                    catch (InvalidDataException)
                    {
                    }
                }

                inner.Dispose();
                onCompleted(completed);
            }

            base.Dispose(disposing);
        }
    }
}
