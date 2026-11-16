using System.IO.Compression;
using Meziantou.Framework.Compression.Internals.Zip;

namespace Meziantou.Framework.Compression;

/// <summary>Opens, creates and extracts zip archives from paths, as <see cref="System.IO.Compression.ZipFile"/>.</summary>
public static class ZipFile
{
    /// <summary>
    /// Opens an archive to read it. The volumes of a split archive are found next to it: the <c>.z01</c>, <c>.z02</c>, ...
    /// files of an Info-ZIP split archive whose last volume is <paramref name="archiveFileName"/> (or whose path is one of the
    /// volumes), or the <c>.002</c>, <c>.003</c>, ... parts that follow a <c>.zip.001</c> part of 7-Zip. When
    /// <see cref="ArchiveReaderOptions.VolumeResolver"/> is set, it is used instead, and <paramref name="archiveFileName"/> is
    /// the first volume.
    /// </summary>
    /// <param name="archiveFileName">The path of the archive.</param>
    /// <param name="options">The options to read the archive, such as its password.</param>
    /// <returns>The archive.</returns>
    /// <exception cref="InvalidDataException">The file is not a zip archive, or the archive is corrupted.</exception>
    public static ZipArchive OpenRead(string archiveFileName, ArchiveReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(archiveFileName);
        if (options?.VolumeResolver is not null)
            return new ZipArchive(File.OpenRead(archiveFileName), ZipArchiveMode.Read, leaveOpen: false, options, compressionOptions: null);

        var volumes = ZipVolumes.Find(Path.GetFullPath(archiveFileName));
        var first = File.OpenRead(volumes[0]);
        Func<int, Stream?>? resolver = volumes.Count > 1 ? index => index < volumes.Count ? File.OpenRead(volumes[index]) : null : null;
        return new ZipArchive(first, ZipArchiveMode.Read, leaveOpen: false, options, compressionOptions: null, resolver);
    }

    /// <summary>Opens an archive in a mode.</summary>
    /// <param name="archiveFileName">The path of the archive.</param>
    /// <param name="mode">
    /// The mode: <see cref="ZipArchiveMode.Read"/> opens an existing file (as <see cref="OpenRead"/>), <see cref="ZipArchiveMode.Create"/>
    /// creates or replaces it, and <see cref="ZipArchiveMode.Update"/> opens it, or creates it when it does not exist.
    /// </param>
    /// <param name="readerOptions">The options to read the archive, such as its password.</param>
    /// <param name="compressionOptions">The options of the new entries.</param>
    /// <returns>The archive.</returns>
    public static ZipArchive Open(string archiveFileName, ZipArchiveMode mode, ArchiveReaderOptions? readerOptions = null, ZipCompressionOptions? compressionOptions = null)
    {
        ArgumentNullException.ThrowIfNull(archiveFileName);
        if (mode is ZipArchiveMode.Read)
            return OpenRead(archiveFileName, readerOptions);

        var (fileMode, access) = mode switch
        {
            ZipArchiveMode.Create => (FileMode.Create, FileAccess.ReadWrite),
            ZipArchiveMode.Update => (FileMode.OpenOrCreate, FileAccess.ReadWrite),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        var stream = new FileStream(archiveFileName, fileMode, access, FileShare.None);
        try
        {
            return new ZipArchive(stream, mode, leaveOpen: false, readerOptions, compressionOptions);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates an archive from the files and the directories of a directory: their content, their last write time and, on
    /// Unix, their permissions (on Windows, their attributes). The symbolic links are stored as links, as Info-ZIP does with
    /// its <c>-y</c> option.
    /// </summary>
    /// <param name="sourceDirectoryName">The directory to archive.</param>
    /// <param name="destinationArchiveFileName">The path of the archive to create.</param>
    /// <param name="options">The options to write the archive.</param>
    /// <param name="includeBaseDirectory"><see langword="true"/> to include the name of the directory at the root of the archive.</param>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Compression.Tests/ZipArchiveWriteTests.cs" ref:region="ZipFile_CreateFromDirectory" ref:dedent="true">
    /// var options = new ZipCompressionOptions
    /// {
    ///     CompressionMethod = ZipCompressionMethod.Xz,
    ///     CompressionLevel = CompressionLevel.SmallestSize,
    /// };
    ///
    /// ZipFile.CreateFromDirectory(sourceDirectory, archivePath, options);
    ///
    /// using var archive = ZipFile.OpenRead(archivePath);
    /// Assert.Equal("file.txt", archive.Entries[0].FullName);
    /// </code>
    /// </example>
    public static void CreateFromDirectory(string sourceDirectoryName, string destinationArchiveFileName, ZipCompressionOptions? options = null, bool includeBaseDirectory = false)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectoryName);
        ArgumentNullException.ThrowIfNull(destinationArchiveFileName);
        var entries = SevenZipFile.EnumerateDirectory(sourceDirectoryName, includeBaseDirectory);
        using var archive = Open(destinationArchiveFileName, ZipArchiveMode.Create, readerOptions: null, options);
        foreach (var (info, entryName) in entries)
        {
            if (info is DirectoryInfo directory && directory.LinkTarget is null)
            {
                archive.CreateEntry(entryName + "/", ZipArchive.GetFileOptions(directory, options: null));
            }
            else
            {
                archive.CreateEntryFromFile(info.FullName, entryName);
            }
        }
    }

    /// <summary>
    /// Creates an archive from the files and the directories of a directory: their content, their last write time and, on
    /// Unix, their permissions (on Windows, their attributes). The symbolic links are stored as links, as Info-ZIP does with
    /// its <c>-y</c> option.
    /// </summary>
    /// <param name="sourceDirectoryName">The directory to archive.</param>
    /// <param name="destinationArchiveFileName">The path of the archive to create.</param>
    /// <param name="options">The options to write the archive.</param>
    /// <param name="includeBaseDirectory"><see langword="true"/> to include the name of the directory at the root of the archive.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the archive is written.</returns>
    public static async Task CreateFromDirectoryAsync(string sourceDirectoryName, string destinationArchiveFileName, ZipCompressionOptions? options = null, bool includeBaseDirectory = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectoryName);
        ArgumentNullException.ThrowIfNull(destinationArchiveFileName);
        var entries = SevenZipFile.EnumerateDirectory(sourceDirectoryName, includeBaseDirectory);
        var archive = Open(destinationArchiveFileName, ZipArchiveMode.Create, readerOptions: null, options);
        await using (archive.ConfigureAwait(false))
        {
            foreach (var (info, entryName) in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (info is DirectoryInfo directory && directory.LinkTarget is null)
                {
                    archive.CreateEntry(entryName + "/", ZipArchive.GetFileOptions(directory, options: null));
                }
                else
                {
                    await archive.CreateEntryFromFileAsync(info.FullName, entryName, options: null, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Extracts an archive to a directory. An entry whose path leaves the directory (<c>..</c>, a rooted path, or a symbolic
    /// link that points outside of it) is rejected with an <see cref="IOException"/>.
    /// </summary>
    /// <param name="sourceArchiveFileName">The path of the archive (the last volume, or the <c>.zip.001</c> part, of a split archive).</param>
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
    /// <param name="sourceArchiveFileName">The path of the archive (the last volume, or the <c>.zip.001</c> part, of a split archive).</param>
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
}
