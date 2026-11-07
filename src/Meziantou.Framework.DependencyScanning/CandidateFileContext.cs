using System.Runtime.InteropServices;

namespace Meziantou.Framework.DependencyScanning;

/// <summary>Provides context information about a candidate file during the scanning process.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly ref struct CandidateFileContext
{
    private static readonly char[] DirectorySeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public CandidateFileContext(ReadOnlySpan<char> rootDirectory, ReadOnlySpan<char> directory, ReadOnlySpan<char> fileName)
    {
        RootDirectory = rootDirectory;
        Directory = directory;
        FileName = fileName;
    }

    /// <summary>Gets the root directory path of the scan operation.</summary>
    public ReadOnlySpan<char> RootDirectory { get; }

    /// <summary>Gets the directory path containing the file.</summary>
    public ReadOnlySpan<char> Directory { get; }

    /// <summary>Gets the file name without the directory path.</summary>
    public ReadOnlySpan<char> FileName { get; }

    /// <summary>Determines whether the file name matches the specified value.</summary>
    /// <param name="fileName">The file name to compare.</param>
    /// <param name="ignoreCase">Whether to perform a case-insensitive comparison.</param>
    /// <returns><see langword="true"/> if the file names match; otherwise, <see langword="false"/>.</returns>
    public bool HasFileName(string fileName, bool ignoreCase)
    {
        return FileName.Equals(fileName, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>Determines whether the file has the specified extension.</summary>
    /// <param name="extension">The extension to check (including the leading dot).</param>
    /// <param name="ignoreCase">Whether to perform a case-insensitive comparison.</param>
    /// <returns><see langword="true"/> if the file has the specified extension; otherwise, <see langword="false"/>.</returns>
    public bool HasExtension(string extension, bool ignoreCase)
    {
        return Path.GetExtension(FileName).Equals(extension, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>Determines whether the file has any of the specified extensions.</summary>
    /// <param name="extensions">The collection of extensions to check (each including the leading dot).</param>
    /// <param name="ignoreCase">Whether to perform a case-insensitive comparison.</param>
    /// <returns><see langword="true"/> if the file has any of the specified extensions; otherwise, <see langword="false"/>.</returns>
    public bool HasExtension(ReadOnlySpan<string> extensions, bool ignoreCase)
    {
        var fileExtension = Path.GetExtension(FileName);
        var comparisonType = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (var extension in extensions)
        {
            if (fileExtension.Equals(extension, comparisonType))
                return true;
        }

        return false;
    }

    /// <summary>Gets the directory path relative to the root directory, or an empty span when the directory is not under the root directory.</summary>
    public ReadOnlySpan<char> RelativeDirectory
    {
        get
        {
            var root = RootDirectory.TrimEnd(DirectorySeparators);
            var directory = Directory.TrimEnd(DirectorySeparators);
            if (directory.Length <= root.Length || !directory.StartsWith(root, StringComparison.Ordinal))
                return "";

            if (Array.IndexOf(DirectorySeparators, directory[root.Length]) < 0)
                return "";

            return directory[(root.Length + 1)..];
        }
    }
}
