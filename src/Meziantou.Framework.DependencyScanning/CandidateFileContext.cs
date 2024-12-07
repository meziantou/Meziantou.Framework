using System.Runtime.InteropServices;

namespace Meziantou.Framework.DependencyScanning;

[StructLayout(LayoutKind.Auto)]
public readonly ref struct CandidateFileContext
{
    public CandidateFileContext(ReadOnlySpan<char> rootDirectory, ReadOnlySpan<char> directory, ReadOnlySpan<char> fileName)
    {
        RootDirectory = rootDirectory;
        Directory = directory;
        FileName = fileName;
    }

    public ReadOnlySpan<char> RootDirectory { get; }
    public ReadOnlySpan<char> Directory { get; }
    public ReadOnlySpan<char> FileName { get; }

    public bool HasFileName(string fileName, bool ignoreCase)
    {
        return FileName.Equals(fileName, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    public bool HasExtension(string extension, bool ignoreCase)
    {
        return Path.GetExtension(FileName).Equals(extension, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

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

    public ReadOnlySpan<char> RelativeDirectory => Directory.SequenceEqual(RootDirectory) ? "" : Directory[(RootDirectory.Length + 1)..];
}
