using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Win32.ProjectedFileSystem;

public sealed class ProjectedFileSystemTests
{
    [ProjectedFileSystemFact]
    public void Test()
    {
        var guid = Guid.NewGuid();
        var fullPath = Path.Combine(Path.GetTempPath(), "projFS", guid.ToString("N"));
        try
        {
            Directory.CreateDirectory(fullPath);

            using var vfs = new SampleVirtualFileSystem(fullPath);
            var options = new ProjectedFileSystemStartOptions
            {
                UseNegativePathCache = false,
                Notifications =
                    {
                        new Notification(
                            PRJ_NOTIFY_TYPES.FILE_HANDLE_CLOSED_FILE_DELETED |
                            PRJ_NOTIFY_TYPES.FILE_HANDLE_CLOSED_FILE_MODIFIED |
                            PRJ_NOTIFY_TYPES.FILE_HANDLE_CLOSED_NO_MODIFICATION |
                            PRJ_NOTIFY_TYPES.FILE_OPENED |
                            PRJ_NOTIFY_TYPES.FILE_OVERWRITTEN |
                            PRJ_NOTIFY_TYPES.FILE_PRE_CONVERT_TO_FULL |
                            PRJ_NOTIFY_TYPES.FILE_RENAMED |
                            PRJ_NOTIFY_TYPES.HARDLINK_CREATED |
                            PRJ_NOTIFY_TYPES.NEW_FILE_CREATED |
                            PRJ_NOTIFY_TYPES.PRE_DELETE |
                            PRJ_NOTIFY_TYPES.PRE_RENAME |
                            PRJ_NOTIFY_TYPES.PRE_SET_HARDLINK),
                    },
            };

            vfs.Start(options);

            // Get content
            var files = Directory.GetFiles(fullPath);
            foreach (var result in files)
            {
                var fi = new FileInfo(result);
                var length = fi.Length;
            }

            var directories = Directory.GetDirectories(fullPath);
            directories.Should().ContainSingle();
            Assert.Equal("folder", Path.GetFileName(directories[0]));

            // Get unknown file
            var fi2 = new FileInfo(Path.Combine(fullPath, "unknownfile.txt"));
            Assert.False(fi2.Exists);
            new Func<object>(() => fi2.Length).Should().ThrowExactly<FileNotFoundException>();
            Assert.Equal([1], File.ReadAllBytes(Path.Combine(fullPath, "a")));
            using var stream = File.OpenRead(Path.Combine(fullPath, "b"));
            Assert.Equal(1, stream.ReadByte());
            Assert.Equal(2, stream.ReadByte());
        }
        finally
        {
            try
            {
                Directory.Delete(fullPath, recursive: true);
            }
            catch
            {
            }
        }
    }
}
