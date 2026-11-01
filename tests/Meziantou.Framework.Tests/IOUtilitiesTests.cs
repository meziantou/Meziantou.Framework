#pragma warning disable CS0618 // Type or member is obsolete
using System.Runtime.Versioning;

namespace Meziantou.Framework.Tests;

public class IOUtilitiesTests
{
    [Theory]
    [InlineData("sample.txt", "sample.txt")]
    [InlineData("sample/.txt", "sample_x47_.txt")]
    [InlineData("COM1", "_COM1_")]
    // A device name is reserved with any extension
    [InlineData("con.txt", "_con.txt_")]
    [InlineData("NUL.log.bak", "_NUL.log.bak_")]
    [InlineData("console.txt", "console.txt")]
    // Windows strips trailing periods and spaces
    [InlineData("report.", "report_x46_")]
    [InlineData("report ", "report_x32_")]
    [InlineData("report.. ", "report_x46__x46__x32_")]
    [InlineData("...", "_..._")]
    public void ToValidFileName(string fileName, string expectedResult)
    {
        var result = IOUtilities.ToValidFileName(fileName);
        Assert.Equal(expectedResult, result);
    }

    [Theory]
    [InlineData("a\0b", "a_x0_b")]
    [InlineData("a/b/c", "a_x47_b_x47_c")]
    [InlineData("/.", "_x47__x46_")]
    public void ToValidFileName_InvalidCharacters(string fileName, string expectedResult)
    {
        Assert.Equal(expectedResult, IOUtilities.ToValidFileName(fileName));
    }

    [Fact]
    public void ToValidFileName_ValidName_ReturnsSameInstance()
    {
        var fileName = new string(['s', 'a', 'm', 'p', 'l', 'e']);

        Assert.Same(fileName, IOUtilities.ToValidFileName(fileName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyDirectory_DirectorySymbolicLinkToAncestor_CopiesTheLink(bool useDirectoryInfo)
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var source = root.CreateSubdirectory("source");
            File.WriteAllText(Path.Combine(source.FullName, "file.txt"), "content");
            Directory.CreateSymbolicLink(Path.Combine(source.FullName, "loop"), source.FullName);
            var destination = Path.Combine(root.FullName, "destination");

            if (useDirectoryInfo)
            {
                IOUtilities.CopyDirectory(source, new DirectoryInfo(destination));
            }
            else
            {
                IOUtilities.CopyDirectory(source.FullName, destination);
            }

            Assert.Equal("content", File.ReadAllText(Path.Combine(destination, "file.txt")));
            Assert.Equal(source.FullName, new DirectoryInfo(Path.Combine(destination, "loop")).LinkTarget);
        }
        finally
        {
            IOUtilities.Delete(root);
        }
    }

    [Fact]
    public void Delete_ReadOnlyDirectory_DeletesTheTree()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = CreateTreeWithReadOnlyDirectory();

        IOUtilities.Delete(root);

        Assert.False(Directory.Exists(root.FullName));
    }

    [Fact]
    public async Task DeleteAsync_ReadOnlyDirectory_DeletesTheTree()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = CreateTreeWithReadOnlyDirectory();

        await IOUtilities.DeleteAsync(root, XunitCancellationToken);

        Assert.False(Directory.Exists(root.FullName));
    }

    // Deleting an entry requires write permission on its parent directory on Unix, e.g. the Go module cache
    [UnsupportedOSPlatform("windows")]
    private static DirectoryInfo CreateTreeWithReadOnlyDirectory()
    {
        var root = Directory.CreateTempSubdirectory();
        var readOnly = root.CreateSubdirectory("ro");
        var nested = readOnly.CreateSubdirectory("nested");
        File.WriteAllText(Path.Combine(readOnly.FullName, "f.txt"), "content");
        File.WriteAllText(Path.Combine(nested.FullName, "g.txt"), "content");
        File.SetUnixFileMode(nested.FullName, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        File.SetUnixFileMode(readOnly.FullName, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return root;
    }

    [Fact]
    public void DeleteIgnoresADirectoryRemovedAfterTheExistsCheck()
    {
        var directoryInfo = CreateDirectoryRemovedAfterTheExistsCheck();

        IOUtilities.Delete(directoryInfo);
    }

    [Fact]
    public async Task DeleteAsyncIgnoresADirectoryRemovedAfterTheExistsCheck()
    {
        var directoryInfo = CreateDirectoryRemovedAfterTheExistsCheck();

        await IOUtilities.DeleteAsync(directoryInfo, XunitCancellationToken);
    }

    [Fact]
    public void DeleteDoesNotFollowADirectorySymbolicLink()
    {
        var (link, target, targetFile) = CreateDirectorySymbolicLink();

        IOUtilities.Delete(new DirectoryInfo(link));

        Assert.False(Directory.Exists(link));
        Assert.True(Directory.Exists(target));
        Assert.True(File.Exists(targetFile));
        Directory.Delete(target, recursive: true);
    }

    [Fact]
    public async Task DeleteAsyncDoesNotFollowADirectorySymbolicLink()
    {
        var (link, target, targetFile) = CreateDirectorySymbolicLink();

        await IOUtilities.DeleteAsync(new DirectoryInfo(link), XunitCancellationToken);

        Assert.False(Directory.Exists(link));
        Assert.True(Directory.Exists(target));
        Assert.True(File.Exists(targetFile));
        Directory.Delete(target, recursive: true);
    }

    private static (string Link, string Target, string TargetFile) CreateDirectorySymbolicLink()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);

        var targetFile = Path.Combine(target, "content.txt");
        File.WriteAllText(targetFile, "content");

        var link = Path.Combine(root, "link");
        Directory.CreateSymbolicLink(link, target);

        return (link, target, targetFile);
    }

    // FileSystemInfo.Exists is cached, so this reproduces the window between the Exists check
    // and the enumeration of the directory content, without depending on timing.
    private static DirectoryInfo CreateDirectoryRemovedAfterTheExistsCheck()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        var directoryInfo = new DirectoryInfo(path);
        Assert.True(directoryInfo.Exists);
        Directory.Delete(path);

        return directoryInfo;
    }

    [Fact]
    public void DeleteThrowsWhenTheFailureNeverClears()
    {
        // Producing this on Windows needs mandatory locking; on Unix an unwritable parent directory does it
        if (OperatingSystem.IsWindows())
            return;

        var root = Directory.CreateTempSubdirectory();
        var locked = root.CreateSubdirectory("locked");
        var probe = Path.Combine(locked.FullName, "probe.txt");
        var target = Path.Combine(locked.FullName, "target.txt");
        File.WriteAllText(probe, "probe");
        File.WriteAllText(target, "target");

        try
        {
            File.SetUnixFileMode(locked.FullName, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            // Skip when the permission is not enforced for the current user, e.g. root
            try
            {
                File.Delete(probe);
                return;
            }
            catch (UnauthorizedAccessException)
            {
            }

            Assert.Throws<UnauthorizedAccessException>(() => IOUtilities.Delete(target));
        }
        finally
        {
            File.SetUnixFileMode(locked.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Documentation_ToValidFileName()
    {
        #region IOUtilities_ToValidFileName
        // Invalid characters are replaced by their code: '/' is 47
        Assert.Equal("Q1_x47_Q2 report.txt", IOUtilities.ToValidFileName("Q1/Q2 report.txt"));

        // Windows reserved device names are wrapped, even with an extension
        Assert.Equal("_con.txt_", IOUtilities.ToValidFileName("con.txt"));

        // Trailing periods and spaces, which Windows removes, are replaced too
        Assert.Equal("draft_x46_", IOUtilities.ToValidFileName("draft."));
        #endregion
    }

    [Fact]
    public void Documentation_CopyAndDelete()
    {
        #region IOUtilities_CopyAndDelete
        var source = Directory.CreateTempSubdirectory();
        var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var file = Path.Combine(source.FullName, "sub", "data.txt");

        // Create the folder of a file
        IOUtilities.PathCreateDirectory(file);
        File.WriteAllText(file, "content");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        // Copy a folder and its subfolders
        IOUtilities.CopyDirectory(source.FullName, destination);
        Assert.True(File.Exists(Path.Combine(destination, "sub", "data.txt")));

        // Delete a folder recursively, read-only files included
        IOUtilities.Delete(source);
        IOUtilities.Delete(destination);
        Assert.False(Directory.Exists(source.FullName));
        Assert.False(Directory.Exists(destination));
        #endregion
    }
}
