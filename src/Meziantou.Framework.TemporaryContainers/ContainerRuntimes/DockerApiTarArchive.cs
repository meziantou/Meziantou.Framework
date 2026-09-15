using System.Formats.Tar;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Builds and reads the tar payloads the Docker Engine API exchanges: the build context of <c>POST /build</c>, and the body of <c>GET</c> and <c>PUT /containers/{id}/archive</c>.</summary>
internal static class DockerApiTarArchive
{
    /// <summary>The mode of a file written from a stream, which has no mode of its own.</summary>
    internal const UnixFileMode RegularFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    // Windows has no executable bit to read, so the docker CLI marks everything it sends from a Windows host as
    // executable: a script in a build context must still run once it is copied into a Linux image.
    private const UnixFileMode WindowsFileMode = RegularFileMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    /// <summary>Creates an archive holding <paramref name="content"/> as its only file entry.</summary>
    /// <param name="entryName">The name the file takes inside the archive, which is the name it takes in the container.</param>
    /// <param name="content">The content of the file.</param>
    /// <param name="mode">The permissions the file gets in the container.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The archive, positioned at its beginning.</returns>
    public static async Task<Stream> CreateForFileAsync(string entryName, Stream content, UnixFileMode mode, CancellationToken cancellationToken)
    {
        // The tar entry declares its length up front, so a stream whose length cannot be read is buffered first.
        MemoryStream? buffer = null;
        try
        {
            if (!content.CanSeek)
            {
                buffer = new MemoryStream();
                await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                buffer.Position = 0;
            }

            var archive = new MemoryStream();
            try
            {
                await using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
                {
                    var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
                    {
                        DataStream = buffer ?? content,
                        Mode = mode,
                    };

                    await writer.WriteEntryAsync(entry, cancellationToken).ConfigureAwait(false);
                }

                archive.Position = 0;
                return archive;
            }
            catch
            {
                await archive.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            if (buffer is not null)
                await buffer.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Gets the permissions a host file keeps once it is copied into a container.</summary>
    public static UnixFileMode GetFileMode(string path)
        => OperatingSystem.IsWindows() ? WindowsFileMode : File.GetUnixFileMode(path);

    /// <summary>Creates an archive of <paramref name="directory"/> and everything below it.</summary>
    /// <param name="directory">The directory to archive.</param>
    /// <param name="entryPrefix">A directory name every entry is placed under, or an empty string to archive the content of <paramref name="directory"/> at the root. When it is not empty, it must end with a slash.</param>
    /// <param name="additionalFile">A file to add to the archive under a name of the caller's choosing, on top of the content of <paramref name="directory"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The archive, positioned at its beginning. It is backed by a temporary file that is deleted when the stream is disposed.</returns>
    public static async Task<Stream> CreateForDirectoryAsync(string directory, string entryPrefix, (string SourcePath, string EntryName)? additionalFile, CancellationToken cancellationToken)
    {
        var tempFile = PrivateTemporaryFile.CreatePath();
        try
        {
            await using (var file = PrivateTemporaryFile.Create(tempFile))
            await using (var writer = new TarWriter(file, TarEntryFormat.Pax, leaveOpen: true))
            {
                var root = new DirectoryInfo(directory);
                if (entryPrefix.Length > 0)
                    await WriteEntryAsync(writer, root.FullName, entryPrefix, cancellationToken).ConfigureAwait(false);

                // .dockerignore is not applied: it is a client-side filter the daemon never sees, so the whole context is sent.
                await WriteDirectoryContentAsync(writer, root, entryPrefix, cancellationToken).ConfigureAwait(false);

                if (additionalFile is var (sourcePath, additionalEntryName))
                    await WriteEntryAsync(writer, sourcePath, additionalEntryName, cancellationToken).ConfigureAwait(false);
            }

            return new TemporaryFileStream(tempFile);
        }
        catch
        {
            File.Delete(tempFile);
            throw;
        }
    }

    /// <summary>Writes the content of a directory, recursively. A symbolic link is archived as a link, like the docker CLI does, and a linked directory is not walked into.</summary>
    private static async Task WriteDirectoryContentAsync(TarWriter writer, DirectoryInfo directory, string entryPrefix, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.None,
        };

        foreach (var item in directory.EnumerateFileSystemInfos("*", options))
        {
            var entryName = entryPrefix + item.Name;
            if (item is DirectoryInfo subdirectory && item.LinkTarget is null)
            {
                await WriteEntryAsync(writer, subdirectory.FullName, entryName + "/", cancellationToken).ConfigureAwait(false);
                await WriteDirectoryContentAsync(writer, subdirectory, entryName + "/", cancellationToken).ConfigureAwait(false);
                continue;
            }

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

    /// <summary>Reads the content of the file an archive returned by <c>GET /containers/{id}/archive</c> holds.</summary>
    /// <param name="archive">The archive returned by the daemon.</param>
    /// <param name="containerPath">The path that was requested, used to report a path that turned out not to be a file.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The content of the file, positioned at its beginning.</returns>
    /// <exception cref="FileNotFoundException">The archive does not hold a file: the path is a directory, or a link that was not resolved.</exception>
    public static async Task<Stream> ReadSingleFileAsync(Stream archive, string containerPath, CancellationToken cancellationToken)
    {
        await using var reader = new TarReader(archive, leaveOpen: true);

        // The daemon archives the requested path as the first entry. A directory comes with everything below it, so
        // the first entry is the only one that says what the path is.
        var entry = await GetNextEntryAsync(reader, cancellationToken).ConfigureAwait(false);
        if (entry is null || !IsRegularFile(entry.EntryType))
            throw new FileNotFoundException($"The path '{containerPath}' does not point to a file in the container.", containerPath);

        var content = new MemoryStream();
        try
        {
            // An empty file has no data stream at all.
            if (entry.DataStream is { } data)
            {
                await data.CopyToAsync(content, cancellationToken).ConfigureAwait(false);
                content.Position = 0;
            }

            return content;
        }
        catch
        {
            await content.DisposeAsync().ConfigureAwait(false);
            throw;
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
}
