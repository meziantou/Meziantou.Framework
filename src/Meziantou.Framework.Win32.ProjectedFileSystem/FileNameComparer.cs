using System.Runtime.Versioning;

namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>Compares file names using Windows file name comparison rules.</summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class FileNameComparer : IComparer<string?>
{
    private FileNameComparer()
    {
    }

    /// <summary>Gets a singleton instance of the <see cref="FileNameComparer"/> class.</summary>
    public static IComparer<string> Instance { get; } = new FileNameComparer();

    public int Compare(string? x, string? y)
    {
        if (x is null && y is null)
            return 0;

        if (x is null)
            return -1;

        if (y is null)
            return 1;

        return NativeMethods.PrjFileNameCompare(x, y);
    }
}
