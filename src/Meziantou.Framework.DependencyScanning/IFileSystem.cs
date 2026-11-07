namespace Meziantou.Framework.DependencyScanning;

/// <summary>Provides an abstraction for file system operations, enabling testability and custom file access implementations.</summary>
public interface IFileSystem
{
    /// <summary>Opens a file for reading.</summary>
    /// <remarks>The stream does not have to be seekable: a stream that is not is read into memory before the file is scanned.</remarks>
    /// <param name="path">The path to the file to open.</param>
    /// <returns>A stream for reading the file.</returns>
    Stream OpenRead(string path);

    /// <summary>Opens a file for reading and writing.</summary>
    /// <remarks>
    /// The stream must be seekable and support <see cref="Stream.SetLength(long)"/>, because an update reads the whole
    /// file, then rewrites it from its start and truncates it to the new length.
    /// </remarks>
    /// <param name="path">The path to the file to open.</param>
    /// <returns>A stream for reading and writing the file.</returns>
    Stream OpenReadWrite(string path);

    /// <summary>Gets the files in a directory matching the specified pattern.</summary>
    /// <remarks>
    /// A directory scan with a custom file system calls this method with the <c>*</c> pattern to list the files to
    /// scan, and <see cref="SearchOption.AllDirectories"/> when <see cref="ScannerOptions.RecurseSubdirectories"/> is set.
    /// It should throw a <see cref="DirectoryNotFoundException"/> when the directory does not exist, which the scan
    /// then reports.
    /// </remarks>
    /// <param name="path">The directory path to search.</param>
    /// <param name="pattern">The search pattern to match file names.</param>
    /// <param name="searchOptions">Options controlling the search behavior.</param>
    /// <returns>An enumerable collection of file paths.</returns>
    IEnumerable<string> GetFiles(string path, string pattern, SearchOption searchOptions);
}
