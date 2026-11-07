namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Temporary files of the tests that write images to disk.</summary>
internal static class TestFiles
{
    /// <summary>Creates an empty directory of its own for a test, which deletes it.</summary>
    public static FullPath CreateDirectory()
    {
        var directory = FullPath.GetTempPath() / "Meziantou.Framework.Imaging.Tests" / Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
