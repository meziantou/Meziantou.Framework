namespace Meziantou.Framework.DependencyScanning;

/// <summary>Provides an abstraction for file system operations, enabling testability and custom file access implementations.</summary>
public interface IFileSystem
{
    /// <summary>Opens a file for reading.</summary>
    /// <param name="path">The path to the file to open.</param>
    /// <returns>A stream for reading the file.</returns>
    Stream OpenRead(string path);

    /// <summary>Opens a file for reading and writing.</summary>
    /// <param name="path">The path to the file to open.</param>
    /// <returns>A stream for reading and writing the file.</returns>
    Stream OpenReadWrite(string path);

    /// <summary>Gets the files in a directory matching the specified pattern.</summary>
    /// <param name="path">The directory path to search.</param>
    /// <param name="pattern">The search pattern to match file names.</param>
    /// <param name="searchOptions">Options controlling the search behavior.</param>
    /// <returns>An enumerable collection of file paths.</returns>
    IEnumerable<string> GetFiles(string path, string pattern, SearchOption searchOptions);
}
