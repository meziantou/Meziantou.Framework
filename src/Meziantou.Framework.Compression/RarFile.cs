using System.Globalization;

namespace Meziantou.Framework.Compression;

/// <summary>Opens and extracts the RAR archives of the file system, finding the volumes of a multi-volume archive next to the first one.</summary>
public static class RarFile
{
    /// <summary>
    /// Opens a RAR archive. When <see cref="ArchiveReaderOptions.VolumeResolver"/> is <see langword="null"/>, the volumes
    /// of a multi-volume archive are found next to it: <c>name.part2.rar</c>, <c>name.part3.rar</c>, ... (keeping the
    /// number of digits of the first volume, as in <c>name.part01.rar</c>), or <c>name.r00</c>, <c>name.r01</c>, ... for
    /// the volumes of the old naming scheme that follow <c>name.rar</c>.
    /// </summary>
    /// <param name="path">The path of the archive, the first volume of a multi-volume archive.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The archive, which owns the files it opens.</returns>
    /// <exception cref="InvalidDataException">The file is not a RAR archive, or the archive is corrupted.</exception>
    /// <exception cref="ArchivePasswordException">The headers are encrypted and the password is missing or wrong.</exception>
    public static RarArchive OpenRead(string path, ArchiveReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var fullPath = Path.GetFullPath(path);
        var effectiveOptions = new ArchiveReaderOptions
        {
            Password = options?.Password,
            EntryNameEncoding = options?.EntryNameEncoding,
            MaxMemoryUsage = options?.MaxMemoryUsage ?? new ArchiveReaderOptions().MaxMemoryUsage,
            VolumeResolver = options?.VolumeResolver ?? (index => OpenVolume(fullPath, index)),
        };

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new RarArchive(stream, effectiveOptions, leaveOpen: false);
    }

    /// <summary>
    /// Extracts the entries of a RAR archive to a directory. An entry whose path leaves the directory (<c>..</c>, a rooted
    /// path, or a symbolic link that points outside of it) is rejected with an <see cref="IOException"/>.
    /// </summary>
    /// <param name="sourceArchiveFileName">The path of the archive, the first volume of a multi-volume archive.</param>
    /// <param name="destinationDirectoryName">The directory to extract to. It is created when it does not exist.</param>
    /// <param name="overwriteFiles"><see langword="true"/> to replace the existing files; <see langword="false"/> to throw an <see cref="IOException"/> when a file exists.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Compression.Tests/RarFileTests.cs" ref:region="RarFile_ExtractToDirectory" ref:dedent="true">
    /// RarFile.ExtractToDirectory(archivePath, destination, overwriteFiles: true);
    /// Assert.True(File.Exists(Path.Combine(destination, "sub/dir1/file1.txt")));
    /// </code>
    /// </example>
    public static void ExtractToDirectory(string sourceArchiveFileName, string destinationDirectoryName, bool overwriteFiles = false, ArchiveReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sourceArchiveFileName);
        ArgumentNullException.ThrowIfNull(destinationDirectoryName);
        using var archive = OpenRead(sourceArchiveFileName, options);
        archive.ExtractToDirectory(destinationDirectoryName, overwriteFiles);
    }

    /// <summary>
    /// Extracts the entries of a RAR archive to a directory. An entry whose path leaves the directory (<c>..</c>, a rooted
    /// path, or a symbolic link that points outside of it) is rejected with an <see cref="IOException"/>.
    /// </summary>
    /// <param name="sourceArchiveFileName">The path of the archive, the first volume of a multi-volume archive.</param>
    /// <param name="destinationDirectoryName">The directory to extract to. It is created when it does not exist.</param>
    /// <param name="overwriteFiles"><see langword="true"/> to replace the existing files; <see langword="false"/> to throw an <see cref="IOException"/> when a file exists.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when every entry is extracted.</returns>
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

    /// <summary>Gets the path of the volume <paramref name="index"/> (0 for the first one) of the archive whose first volume is <paramref name="path"/>.</summary>
    internal static string GetVolumePath(string path, int index)
    {
        if (index is 0)
            return path;

        var directory = Path.GetDirectoryName(path) ?? "";
        var fileName = Path.GetFileName(path);

        // name.partN.rar: the number of the first volume is not always 1, and its width is kept
        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var partIndex = stem.LastIndexOf(".part", StringComparison.OrdinalIgnoreCase);
        if (partIndex >= 0 && string.Equals(extension, ".rar", StringComparison.OrdinalIgnoreCase))
        {
            var digits = stem[(partIndex + 5)..];
            if (digits.Length > 0 && digits.All(char.IsAsciiDigit) && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var first))
            {
                var number = (first + index).ToString(CultureInfo.InvariantCulture).PadLeft(digits.Length, '0');
                return Path.Join(directory, stem[..(partIndex + 5)] + number + extension);
            }
        }

        // name.rar, name.r00, name.r01, ..., name.r99, name.s00, ...
        var volume = index - 1;
        var letter = (char)((char.IsAsciiLetterUpper(extension.Length > 1 ? extension[1] : 'r') ? 'R' : 'r') + (volume / 100));
        return Path.Join(directory, stem + "." + letter + (volume % 100).ToString("00", CultureInfo.InvariantCulture));
    }

    private static FileStream? OpenVolume(string path, int index)
    {
        var volumePath = GetVolumePath(path, index);
        if (!File.Exists(volumePath))
            return null;

        return new FileStream(volumePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
