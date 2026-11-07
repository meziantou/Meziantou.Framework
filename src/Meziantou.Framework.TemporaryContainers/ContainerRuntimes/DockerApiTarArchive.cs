using System.Formats.Tar;
using System.Net;
using System.Net.Http.Headers;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Builds and reads the tar payloads the Docker Engine API exchanges: the build context of <c>POST /build</c>, and the body of <c>GET</c> and <c>PUT /containers/{id}/archive</c>.</summary>
internal static class DockerApiTarArchive
{
    /// <summary>The mode of a file written from a stream, which has no mode of its own.</summary>
    internal const UnixFileMode RegularFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    // Windows has no executable bit to read, so the docker CLI marks everything it sends from a Windows host as
    // executable: a script in a build context must still run once it is copied into a Linux image.
    private const UnixFileMode WindowsFileMode = RegularFileMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    /// <summary>Writes an archive holding <paramref name="content"/> as its only file entry.</summary>
    /// <param name="destination">The stream the archive is written to, typically the body of a request. It does not need to be seekable.</param>
    /// <param name="entryName">The name the file takes inside the archive, which is the name it takes in the container.</param>
    /// <param name="content">The content of the file. It must be seekable: the tar entry declares its length before the content. See <see cref="EnsureSeekableAsync"/>.</param>
    /// <param name="mode">The permissions the file gets in the container.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async Task WriteFileAsync(Stream destination, string entryName, Stream content, UnixFileMode mode, CancellationToken cancellationToken)
    {
        await using var writer = new TarWriter(destination, TarEntryFormat.Pax, leaveOpen: true);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
        {
            DataStream = content,
            Mode = mode,
        };

        await writer.WriteEntryAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns a seekable stream with the content of <paramref name="content"/>: the stream itself when it is seekable, and a copy in a private temporary file, deleted when the returned stream is disposed, otherwise.</summary>
    /// <remarks>A tar entry declares its length before its content, so a stream whose length is unknown has to be read whole first. A temporary file keeps that copy out of memory.</remarks>
    public static async Task<Stream> EnsureSeekableAsync(Stream content, CancellationToken cancellationToken)
    {
        if (content.CanSeek)
            return content;

        var tempFile = PrivateTemporaryFile.CreatePath();
        try
        {
            await using (var file = PrivateTemporaryFile.Create(tempFile))
                await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);

            return new TemporaryFileStream(tempFile);
        }
        catch
        {
            File.Delete(tempFile);
            throw;
        }
    }

    /// <summary>Creates the body of a request whose content is written by <paramref name="write"/> while the request is sent, so the archive is never held in memory or in a file.</summary>
    public static HttpContent CreateContent(Func<Stream, CancellationToken, Task> write) => new StreamingTarContent(write);

    /// <summary>Gets the permissions a host file keeps once it is copied into a container.</summary>
    public static UnixFileMode GetFileMode(string path)
        => OperatingSystem.IsWindows() ? WindowsFileMode : File.GetUnixFileMode(path);

    /// <summary>Writes an archive of <paramref name="directory"/> and everything below it.</summary>
    /// <param name="destination">The stream the archive is written to, typically the body of a request. It does not need to be seekable.</param>
    /// <param name="directory">The directory to archive.</param>
    /// <param name="entryPrefix">A directory name every entry is placed under, or an empty string to archive the content of <paramref name="directory"/> at the root. When it is not empty, it must end with a slash.</param>
    /// <param name="additionalFile">A file to add to the archive under a name of the caller's choosing, on top of the content of <paramref name="directory"/>.</param>
    /// <param name="ignore">The rules of the <c>.dockerignore</c> file of a build context, or <see langword="null"/> to archive everything.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async Task WriteDirectoryAsync(Stream destination, string directory, string entryPrefix, (string SourcePath, string EntryName)? additionalFile, DockerIgnore? ignore, CancellationToken cancellationToken)
    {
        await using var writer = new TarWriter(destination, TarEntryFormat.Pax, leaveOpen: true);
        var root = new DirectoryInfo(directory);
        if (entryPrefix.Length > 0)
            await WriteEntryAsync(writer, root.FullName, entryPrefix, cancellationToken).ConfigureAwait(false);

        // .dockerignore is a client-side filter the daemon never sees, so it is applied here, like the docker CLI does.
        await WriteDirectoryContentAsync(writer, root, entryPrefix, relativePrefix: "", ignore, cancellationToken).ConfigureAwait(false);

        if (additionalFile is var (sourcePath, additionalEntryName))
            await WriteEntryAsync(writer, sourcePath, additionalEntryName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the content of a directory, recursively. A symbolic link is archived as a link, like the docker CLI does, and a linked directory is not walked into.</summary>
    private static async Task WriteDirectoryContentAsync(TarWriter writer, DirectoryInfo directory, string entryPrefix, string relativePrefix, DockerIgnore? ignore, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.None,
        };

        foreach (var item in directory.EnumerateFileSystemInfos("*", options))
        {
            var entryName = entryPrefix + item.Name;
            var relativePath = relativePrefix + item.Name;
            var excluded = ignore?.IsExcluded(relativePath) is true;
            if (item is DirectoryInfo subdirectory && item.LinkTarget is null)
            {
                // An excluded directory is still walked when an exception can bring back something below it. Its own
                // entry is left out: the daemon creates the parent directories of the entries it extracts.
                if (excluded && ignore?.CanIncludeBelow(relativePath) is not true)
                    continue;

                if (!excluded)
                    await WriteEntryAsync(writer, subdirectory.FullName, entryName + "/", cancellationToken).ConfigureAwait(false);

                await WriteDirectoryContentAsync(writer, subdirectory, entryName + "/", relativePath + "/", ignore, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!excluded)
                await WriteEntryAsync(writer, item.FullName, entryName, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Writes a host file system entry. The entry keeps the type and the permissions of the host entry, so a script stays executable once it reaches the container.</summary>
    private static async Task WriteEntryAsync(TarWriter writer, string path, string entryName, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            await writer.WriteEntryAsync(path, entryName, cancellationToken).ConfigureAwait(false);
            return;
        }

        // On Windows, the writer reads the mode from nowhere, so the entry is built here with the mode the docker CLI uses.
        var info = new FileInfo(path);
        if (info.LinkTarget is { } linkTarget)
        {
            await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, entryName) { LinkName = linkTarget.Replace('\\', '/'), Mode = WindowsFileMode }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (Directory.Exists(path))
        {
            await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.Directory, entryName) { Mode = WindowsFileMode }, cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var content = File.OpenRead(path);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, entryName) { DataStream = content, Mode = WindowsFileMode }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens the content of the file an archive returned by <c>GET /containers/{id}/archive</c> holds. The content is read from the archive as the returned stream is read, so the file is never held in memory.</summary>
    /// <param name="archive">The archive returned by the daemon. The returned stream owns it.</param>
    /// <param name="containerPath">The path that was requested, used to report a path that turned out not to be a file.</param>
    /// <param name="owner">An object disposed with the returned stream, such as the response the archive is read from.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A stream, which cannot seek, positioned at the beginning of the content of the file. Its length is known.</returns>
    /// <exception cref="FileNotFoundException">The archive does not hold a file: the path is a directory, or a link that was not resolved.</exception>
    public static async Task<Stream> OpenSingleFileAsync(Stream archive, string containerPath, IDisposable? owner, CancellationToken cancellationToken)
    {
        TarReader? reader = null;
        var transferred = false;
        try
        {
            reader = new TarReader(archive, leaveOpen: true);

            // The daemon archives the requested path as the first entry. A directory comes with everything below it, so
            // the first entry is the only one that says what the path is.
            var entry = await GetNextEntryAsync(reader, cancellationToken).ConfigureAwait(false);
            if (entry is null || !IsRegularFile(entry.EntryType))
                throw new FileNotFoundException($"The path '{containerPath}' does not point to a file in the container.", containerPath);

            // An empty file has no data stream at all. The stream takes the reader, the archive and the owner over.
            var stream = new TarEntryStream(entry.DataStream, entry.Length, reader, archive, owner);
            reader = null;
            transferred = true;
            return stream;
        }
        finally
        {
            reader?.Dispose();
            if (!transferred)
            {
                await archive.DisposeAsync().ConfigureAwait(false);
                owner?.Dispose();
            }
        }
    }

    /// <summary>Extracts an archive returned by <c>GET /containers/{id}/archive</c> below <paramref name="destinationDirectory"/>.</summary>
    /// <param name="archive">The archive returned by the daemon.</param>
    /// <param name="destinationDirectory">The directory the entries are written to.</param>
    /// <param name="stripFirstSegment">Drops the leading directory of every entry, which is how <c>docker cp</c> gives a copied directory the name of a destination that does not exist yet.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once every entry is written.</returns>
    public static async Task ExtractToDirectoryAsync(Stream archive, string destinationDirectory, bool stripFirstSegment, CancellationToken cancellationToken)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectory));

        await using var reader = new TarReader(archive, leaveOpen: true);
        while (await GetNextEntryAsync(reader, cancellationToken).ConfigureAwait(false) is { } entry)
        {
            var entryName = GetEntryName(entry.Name, stripFirstSegment);
            if (entryName.Length == 0)
                continue;

            // The names come from the container, so nothing is written outside the destination, neither through a
            // name that climbs out of it nor through a link an earlier entry created.
            var destination = GetSafeDestinationPath(root, destinationDirectory, entryName);
            EnsureNoLinkBetween(root, destination, entryName);

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(destination);
                    break;

                case TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile:
                    PrepareFileDestination(destination);
                    await using (var file = File.Create(destination))
                    {
                        if (entry.DataStream is { } data)
                            await data.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                    }

                    break;

                case TarEntryType.SymbolicLink:
                    // The link is recreated as it is, like 'docker cp' does. The check above keeps a later entry from
                    // being written through it, so a link pointing outside the destination is harmless.
                    PrepareFileDestination(destination);
                    File.CreateSymbolicLink(destination, entry.LinkName);
                    break;

                case TarEntryType.HardLink:
                    // The target of a hard link is an earlier entry of the same archive, named from its root.
                    var linkTargetName = GetEntryName(entry.LinkName, stripFirstSegment);
                    var linkTarget = GetSafeDestinationPath(root, destinationDirectory, linkTargetName);
                    EnsureNoLinkBetween(root, linkTarget, linkTargetName);
                    PrepareFileDestination(destination);
                    File.Copy(linkTarget, destination);
                    break;

                default:
                    // Devices and pipes cannot be recreated by an unprivileged process, and they carry no content.
                    break;
            }
        }
    }

    private static async Task<TarEntry?> GetNextEntryAsync(TarReader reader, CancellationToken cancellationToken)
    {
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false) is { } entry)
        {
            if (entry is not PaxGlobalExtendedAttributesTarEntry)
                return entry;
        }

        return null;
    }

    private static bool IsRegularFile(TarEntryType entryType)
        => entryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile;

    private static string GetEntryName(string name, bool stripFirstSegment)
    {
        var entryName = name.Replace('\\', '/').TrimStart('/');
        if (stripFirstSegment)
        {
            var separatorIndex = entryName.IndexOf('/', StringComparison.Ordinal);
            entryName = separatorIndex < 0 ? "" : entryName[(separatorIndex + 1)..];
        }

        return entryName.TrimEnd('/');
    }

    /// <summary>Removes what a previous entry left at the destination of a file, so a link is replaced instead of being written through.</summary>
    private static void PrepareFileDestination(string destination)
    {
        if (Path.GetDirectoryName(destination) is { } parent)
            Directory.CreateDirectory(parent);

        var existing = new FileInfo(destination);
        if (existing.LinkTarget is not null || existing.Exists)
            existing.Delete();
    }

    /// <summary>Resolves an entry name below <paramref name="root"/>. The names come from the container, so an entry that escapes the destination is rejected instead of overwriting a file of the host.</summary>
    internal static string GetSafeDestinationPath(string root, string destinationDirectory, string entryName)
    {
        // The root of a drive keeps its trailing separator, every other directory has none.
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(root, entryName.Replace('/', Path.DirectorySeparatorChar)));
        if (!string.Equals(destination, root, StringComparison.Ordinal) && !destination.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"The archive entry '{entryName}' points outside of '{destinationDirectory}'.");

        return destination;
    }

    /// <summary>Rejects a destination one of whose parent directories below the root is a symbolic link: writing there would follow the link out of the destination.</summary>
    private static void EnsureNoLinkBetween(string root, string destination, string entryName)
    {
        var current = Path.GetDirectoryName(destination);
        while (current is not null && current.Length > root.Length)
        {
            if (new DirectoryInfo(current).LinkTarget is not null)
                throw new InvalidOperationException($"The archive entry '{entryName}' is written through the symbolic link '{current}'.");

            current = Path.GetDirectoryName(current);
        }
    }

    /// <summary>A request body written while the request is sent.</summary>
    private sealed class StreamingTarContent : HttpContent
    {
        private readonly Func<Stream, CancellationToken, Task> _write;

        public StreamingTarContent(Func<Stream, CancellationToken, Task> write)
        {
            _write = write;
            Headers.ContentType = new MediaTypeHeaderValue("application/x-tar");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => _write(stream, CancellationToken.None);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) => _write(stream, cancellationToken);

        // The length of the archive is only known once it is written: the body is sent with a chunked encoding.
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    /// <summary>The content of a tar entry, read from the archive as it is read. Disposing it disposes the archive and what it was read from.</summary>
    private sealed class TarEntryStream(Stream? data, long length, TarReader reader, Stream archive, IDisposable? owner) : Stream
    {
        private long _position;
        private bool _disposed;

        public override bool CanRead => !_disposed;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (data is null)
                return 0;

            var read = data.Read(buffer);
            _position += read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (data is null)
                return 0;

            var read = await data.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            _position += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;
                reader.Dispose();
                archive.Dispose();
                owner?.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                await reader.DisposeAsync().ConfigureAwait(false);
                await archive.DisposeAsync().ConfigureAwait(false);
                owner?.Dispose();
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
