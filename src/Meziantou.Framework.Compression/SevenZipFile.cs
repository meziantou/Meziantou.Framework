using System.Globalization;

namespace Meziantou.Framework.Compression;

/// <summary>Opens, creates and extracts 7z archives from paths, as <see cref="System.IO.Compression.ZipFile"/>.</summary>
public static class SevenZipFile
{
    /// <summary>
    /// Opens an archive to read it. A path that ends with <c>.001</c> opens a multi-volume archive: the volumes <c>.002</c>,
    /// <c>.003</c>, ... are found next to it, unless <see cref="ArchiveReaderOptions.VolumeResolver"/> is set.
    /// </summary>
    /// <param name="archiveFileName">The path of the archive.</param>
    /// <param name="options">The options to read the archive, such as its password.</param>
    /// <returns>The archive.</returns>
    public static SevenZipArchive OpenRead(string archiveFileName, ArchiveReaderOptions? options = null)
    {
        return Open(archiveFileName, SevenZipArchiveMode.Read, options, compressionOptions: null);
    }

    /// <summary>Opens an archive in a mode. A path that ends with <c>.001</c> opens a multi-volume archive in <see cref="SevenZipArchiveMode.Read"/> mode.</summary>
    /// <param name="archiveFileName">The path of the archive.</param>
    /// <param name="mode">
    /// The mode: <see cref="SevenZipArchiveMode.Read"/> opens an existing file, <see cref="SevenZipArchiveMode.Create"/> creates or
    /// replaces it, and <see cref="SevenZipArchiveMode.Update"/> opens it, or creates it when it does not exist.
    /// </param>
    /// <param name="readerOptions">The options to read the archive, such as its password.</param>
    /// <param name="compressionOptions">The options to write the archive.</param>
    /// <returns>The archive.</returns>
    public static SevenZipArchive Open(string archiveFileName, SevenZipArchiveMode mode, ArchiveReaderOptions? readerOptions = null, SevenZipCompressionOptions? compressionOptions = null)
    {
        ArgumentNullException.ThrowIfNull(archiveFileName);
        var (fileMode, access) = mode switch
        {
            SevenZipArchiveMode.Read => (FileMode.Open, FileAccess.Read),
            SevenZipArchiveMode.Create => (FileMode.Create, FileAccess.ReadWrite),
            SevenZipArchiveMode.Update => (FileMode.OpenOrCreate, FileAccess.ReadWrite),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        if (mode is SevenZipArchiveMode.Read && readerOptions?.VolumeResolver is null && archiveFileName.EndsWith(".001", StringComparison.OrdinalIgnoreCase))
        {
            var prefix = archiveFileName[..^4];
            var options = CopyOptions(readerOptions);
            options.VolumeResolver = index =>
            {
                var path = prefix + "." + (index + 1).ToString("D3", CultureInfo.InvariantCulture);
                return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
            };
            readerOptions = options;
        }

        var stream = new FileStream(archiveFileName, fileMode, access, mode is SevenZipArchiveMode.Read ? FileShare.Read : FileShare.None);
        try
        {
            if (mode is SevenZipArchiveMode.Update && stream.Length is 0)
            {
                // A new archive: write an empty one, then update it
                new SevenZipArchive(stream, SevenZipArchiveMode.Create, leaveOpen: true, readerOptions: null, compressionOptions).Dispose();
            }

            return new SevenZipArchive(stream, mode, leaveOpen: false, readerOptions, compressionOptions);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates an archive from the files and the directories of a directory. The symbolic links are stored as links, as the
    /// <c>-snl</c> switch of 7-Zip.
    /// </summary>
    /// <param name="sourceDirectoryName">The directory to archive.</param>
    /// <param name="destinationArchiveFileName">The path of the archive to create.</param>
    /// <param name="options">The options to write the archive.</param>
    /// <param name="includeBaseDirectory"><see langword="true"/> to include the name of the directory at the root of the archive.</param>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Compression.Tests/SevenZipFileTests.cs" ref:region="SevenZipFile_CreateFromDirectory" ref:dedent="true">
    /// var options = new SevenZipCompressionOptions
    /// {
    ///     Method = SevenZipCompressionMethod.Lzma2,
    ///     Level = 9,
    /// };
    ///
    /// SevenZipFile.CreateFromDirectory(sourceDirectory, archivePath, options);
    ///
    /// using var archive = SevenZipFile.OpenRead(archivePath);
    /// Assert.Equal("file.txt", archive.Entries[0].FullName);
    /// </code>
    /// </example>
    public static void CreateFromDirectory(string sourceDirectoryName, string destinationArchiveFileName, SevenZipCompressionOptions? options = null, bool includeBaseDirectory = false)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectoryName);
        ArgumentNullException.ThrowIfNull(destinationArchiveFileName);
        var entries = EnumerateDirectory(sourceDirectoryName, includeBaseDirectory);
        using var archive = Open(destinationArchiveFileName, SevenZipArchiveMode.Create, readerOptions: null, options);
        foreach (var (info, entryName) in entries)
        {
            if (info is DirectoryInfo directory && directory.LinkTarget is null)
            {
                var entry = archive.CreateEntry(entryName + "/");
                SevenZipArchive.SetMetadataFromFile(entry, directory);
            }
            else
            {
                archive.CreateEntryFromFile(info.FullName, entryName);
            }
        }
    }

    /// <summary>
    /// Creates an archive from the files and the directories of a directory. The symbolic links are stored as links, as the
    /// <c>-snl</c> switch of 7-Zip.
    /// </summary>
    /// <param name="sourceDirectoryName">The directory to archive.</param>
    /// <param name="destinationArchiveFileName">The path of the archive to create.</param>
    /// <param name="options">The options to write the archive.</param>
    /// <param name="includeBaseDirectory"><see langword="true"/> to include the name of the directory at the root of the archive.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the archive is written.</returns>
    public static async Task CreateFromDirectoryAsync(string sourceDirectoryName, string destinationArchiveFileName, SevenZipCompressionOptions? options = null, bool includeBaseDirectory = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectoryName);
        ArgumentNullException.ThrowIfNull(destinationArchiveFileName);
        var entries = EnumerateDirectory(sourceDirectoryName, includeBaseDirectory);
        var archive = Open(destinationArchiveFileName, SevenZipArchiveMode.Create, readerOptions: null, options);
        await using (archive.ConfigureAwait(false))
        {
            foreach (var (info, entryName) in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (info is DirectoryInfo directory && directory.LinkTarget is null)
                {
                    var entry = archive.CreateEntry(entryName + "/");
                    SevenZipArchive.SetMetadataFromFile(entry, directory);
                }
                else
                {
                    await archive.CreateEntryFromFileAsync(info.FullName, entryName, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Extracts an archive to a directory. An entry whose path leaves the directory (<c>..</c>, a rooted path, or a symbolic
    /// link that points outside of it) is rejected with an <see cref="IOException"/>.
    /// </summary>
    /// <param name="sourceArchiveFileName">The path of the archive (the first volume, <c>.001</c>, of a multi-volume archive).</param>
    /// <param name="destinationDirectoryName">The directory to extract to. It is created when it does not exist.</param>
    /// <param name="overwriteFiles"><see langword="true"/> to replace the existing files; <see langword="false"/> to throw an <see cref="IOException"/> when a file exists.</param>
    /// <param name="options">The options to read the archive, such as its password.</param>
    public static void ExtractToDirectory(string sourceArchiveFileName, string destinationDirectoryName, bool overwriteFiles = false, ArchiveReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sourceArchiveFileName);
        ArgumentNullException.ThrowIfNull(destinationDirectoryName);
        using var archive = OpenRead(sourceArchiveFileName, options);
        archive.ExtractToDirectory(destinationDirectoryName, overwriteFiles);
    }

    /// <summary>
    /// Extracts an archive to a directory. An entry whose path leaves the directory (<c>..</c>, a rooted path, or a symbolic
    /// link that points outside of it) is rejected with an <see cref="IOException"/>.
    /// </summary>
    /// <param name="sourceArchiveFileName">The path of the archive (the first volume, <c>.001</c>, of a multi-volume archive).</param>
    /// <param name="destinationDirectoryName">The directory to extract to. It is created when it does not exist.</param>
    /// <param name="overwriteFiles"><see langword="true"/> to replace the existing files; <see langword="false"/> to throw an <see cref="IOException"/> when a file exists.</param>
    /// <param name="options">The options to read the archive, such as its password.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the archive is extracted.</returns>
    public static async Task ExtractToDirectoryAsync(string sourceArchiveFileName, string destinationDirectoryName, bool overwriteFiles = false, ArchiveReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceArchiveFileName);
        ArgumentNullException.ThrowIfNull(destinationDirectoryName);
        var archive = OpenRead(sourceArchiveFileName, options);
        await using (archive.ConfigureAwait(false))
        {
            await archive.ExtractToDirectoryAsync(destinationDirectoryName, overwriteFiles, cancellationToken).ConfigureAwait(false);
        }
    }

    private static ArchiveReaderOptions CopyOptions(ArchiveReaderOptions? options)
    {
        var result = new ArchiveReaderOptions();
        if (options is not null)
        {
            result.Password = options.Password;
            result.EntryNameEncoding = options.EntryNameEncoding;
            result.VolumeResolver = options.VolumeResolver;
            result.MaxMemoryUsage = options.MaxMemoryUsage;
        }

        return result;
    }

    /// <summary>Lists the directories and the files under a directory, sorted, with their names in the archive. The links to directories are not followed.</summary>
    internal static List<(FileSystemInfo Info, string EntryName)> EnumerateDirectory(string sourceDirectoryName, bool includeBaseDirectory)
    {
        var root = new DirectoryInfo(Path.GetFullPath(sourceDirectoryName));
        if (!root.Exists)
            throw new DirectoryNotFoundException($"The directory '{sourceDirectoryName}' does not exist.");

        var result = new List<(FileSystemInfo, string)>();
        var prefix = includeBaseDirectory ? root.Name : null;
        if (prefix is not null)
        {
            result.Add((root, prefix));
        }

        Visit(root, prefix);
        return result;

        void Visit(DirectoryInfo directory, string? path)
        {
            var children = directory.EnumerateFileSystemInfos().OrderBy(info => info.Name, StringComparer.Ordinal).ToList();
            foreach (var child in children)
            {
                var name = path is null ? child.Name : path + "/" + child.Name;
                result.Add((child, name));
                if (child is DirectoryInfo childDirectory && childDirectory.LinkTarget is null)
                {
                    Visit(childDirectory, name);
                }
            }
        }
    }
}
