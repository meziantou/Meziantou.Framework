using System.Formats.Tar;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Builds and reads the tar payloads the Docker Engine API exchanges: the build context of <c>POST /build</c>, and the body of <c>GET</c> and <c>PUT /containers/{id}/archive</c>.</summary>
internal static class DockerApiTarArchive
{
    private const UnixFileMode RegularFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    private const UnixFileMode DirectoryEntryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    /// <summary>Creates an archive holding <paramref name="content"/> as its only file entry.</summary>
    /// <param name="entryName">The name the file takes inside the archive, which is the name it takes in the container.</param>
    /// <param name="content">The content of the file.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The archive, positioned at its beginning.</returns>
    public static async Task<Stream> CreateForFileAsync(string entryName, Stream content, CancellationToken cancellationToken)
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
                    await WriteFileEntryAsync(writer, entryName, buffer ?? content, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Creates an archive of <paramref name="directory"/> and everything below it.</summary>
    /// <param name="directory">The directory to archive.</param>
    /// <param name="entryPrefix">A directory name every entry is placed under, or an empty string to archive the content of <paramref name="directory"/> at the root. When it is not empty, it must end with a slash.</param>
    /// <param name="additionalFile">A file to add to the archive under a name of the caller's choosing, on top of the content of <paramref name="directory"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The archive, positioned at its beginning. It is backed by a temporary file that is deleted when the stream is disposed.</returns>
    public static async Task<Stream> CreateForDirectoryAsync(string directory, string entryPrefix, (string SourcePath, string EntryName)? additionalFile, CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "MezTC_" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var file = File.Create(tempFile))
            await using (var writer = new TarWriter(file, TarEntryFormat.Pax, leaveOpen: true))
            {
                if (entryPrefix.Length > 0)
                    await WriteDirectoryEntryAsync(writer, entryPrefix, cancellationToken).ConfigureAwait(false);

                // .dockerignore is not applied: it is a client-side filter the daemon never sees, so the whole context is sent.
                foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
                {
                    var entryName = entryPrefix + Path.GetRelativePath(directory, path).Replace('\\', '/');
                    if (Directory.Exists(path))
                    {
                        await WriteDirectoryEntryAsync(writer, entryName + "/", cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    await using var content = File.OpenRead(path);
                    await WriteFileEntryAsync(writer, entryName, content, cancellationToken).ConfigureAwait(false);
                }

                if (additionalFile is var (sourcePath, additionalEntryName))
                {
                    await using var content = File.OpenRead(sourcePath);
                    await WriteFileEntryAsync(writer, additionalEntryName, content, cancellationToken).ConfigureAwait(false);
                }
            }

            return new TemporaryFileStream(tempFile);
        }
        catch
        {
            File.Delete(tempFile);
            throw;
        }
    }

    /// <summary>Reads the content of the single file entry of an archive returned by <c>GET /containers/{id}/archive</c>.</summary>
    /// <param name="archive">The archive returned by the daemon.</param>
    /// <param name="containerPath">The path that was requested, used to report a path that turned out not to be a file.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The content of the file, positioned at its beginning.</returns>
    public static async Task<Stream> ReadSingleFileAsync(Stream archive, string containerPath, CancellationToken cancellationToken)
    {
        await using var reader = new TarReader(archive, leaveOpen: true);
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false) is { } entry)
        {
            // Only the entries that carry content have a data stream, so this skips the directories of the archive.
            if (entry.DataStream is not { } data)
                continue;

            var content = new MemoryStream();
            try
            {
                await data.CopyToAsync(content, cancellationToken).ConfigureAwait(false);
                content.Position = 0;
                return content;
            }
            catch
            {
                await content.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        throw new FileNotFoundException($"The path '{containerPath}' does not point to a file in the container.", containerPath);
    }

    /// <summary>Extracts an archive returned by <c>GET /containers/{id}/archive</c> below <paramref name="destinationDirectory"/>.</summary>
    /// <param name="archive">The archive returned by the daemon.</param>
    /// <param name="destinationDirectory">The directory the entries are written to.</param>
    /// <param name="stripFirstSegment">Drops the leading directory of every entry, which is how <c>docker cp</c> gives a copied directory the name of a destination that does not exist yet.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once every entry is written.</returns>
    public static async Task ExtractToDirectoryAsync(Stream archive, string destinationDirectory, bool stripFirstSegment, CancellationToken cancellationToken)
    {
        await using var reader = new TarReader(archive, leaveOpen: true);
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false) is { } entry)
        {
            var entryName = entry.Name.Replace('\\', '/').TrimStart('/');
            if (stripFirstSegment)
            {
                var separatorIndex = entryName.IndexOf('/', StringComparison.Ordinal);
                entryName = separatorIndex < 0 ? "" : entryName[(separatorIndex + 1)..];
            }

            entryName = entryName.TrimEnd('/');
            if (entryName.Length == 0)
                continue;

            var destination = GetSafeDestinationPath(destinationDirectory, entryName);
            if (entry.EntryType is TarEntryType.Directory)
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            if (entry.DataStream is not { } data)
                continue;

            if (Path.GetDirectoryName(destination) is { } parent)
                Directory.CreateDirectory(parent);

            await using var file = File.Create(destination);
            await data.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteFileEntryAsync(TarWriter writer, string entryName, Stream content, CancellationToken cancellationToken)
    {
        var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
        {
            DataStream = content,
            Mode = RegularFileMode,
        };

        await writer.WriteEntryAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteDirectoryEntryAsync(TarWriter writer, string entryName, CancellationToken cancellationToken)
    {
        var entry = new PaxTarEntry(TarEntryType.Directory, entryName)
        {
            Mode = DirectoryEntryMode,
        };

        await writer.WriteEntryAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves an entry name below <paramref name="directory"/>. The names come from the container, so an entry that escapes the destination is rejected instead of overwriting a file of the host.</summary>
    private static string GetSafeDestinationPath(string directory, string entryName)
    {
        var root = Path.GetFullPath(directory);
        var destination = Path.GetFullPath(Path.Combine(root, entryName.Replace('/', Path.DirectorySeparatorChar)));
        if (destination != root && !destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"The archive entry '{entryName}' points outside of '{directory}'.");

        return destination;
    }
}
