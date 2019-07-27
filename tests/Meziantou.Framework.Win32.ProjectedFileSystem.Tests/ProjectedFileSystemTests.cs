using System;
using System.IO;
using Xunit;

namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    public sealed class ProjectedFileSystemTests
    {
        [AttributeUsage(AttributeTargets.All)]
        public sealed class ProjectedFileSystemFactAttribute : FactAttribute
        {
            public ProjectedFileSystemFactAttribute()
            {
                using var temporaryDirectory = TemporaryDirectory.Create();
                try
                {
                    using (var vfs = new SampleVirtualFileSystem(temporaryDirectory.FullPath))
                    {
                        var options = new ProjectedFileSystemStartOptions();
                        try
                        {
                            vfs.Start(options);
                        }
                        catch (NotSupportedException ex)
                        {
                            Skip = ex.Message;
                        }
                    }
                }
                catch
                {
                }
            }
        }

        [ProjectedFileSystemFactAttribute]
        public void Test()
        {
            using var temporaryDirectory = TemporaryDirectory.Create();

            using (var vfs = new SampleVirtualFileSystem(temporaryDirectory.FullPath))
            {
                var options = new ProjectedFileSystemStartOptions();
                options.UseNegativePathCache = false;
                options.Notifications.Add(new Notification(
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
                    PRJ_NOTIFY_TYPES.PRE_SET_HARDLINK
                    ));

                try
                {
                    vfs.Start(options);
                }
                catch (NotSupportedException ex)
                {
                    Assert.True(false, ex.Message);
                }

                // Get content
                var files = Directory.GetFiles(temporaryDirectory.FullPath);
                foreach (var result in files)
                {
                    var fi = new FileInfo(result);
                    var length = fi.Length;
                }

                var directories = Directory.GetDirectories(temporaryDirectory.FullPath);
                Assert.Single(directories);
                Assert.Equal("folder", Path.GetFileName(directories[0]));

                // Get unknown file
                var fi2 = new FileInfo(Path.Combine(temporaryDirectory.FullPath, "unknownfile.txt"));
                Assert.False(fi2.Exists);
                Assert.Throws<FileNotFoundException>(() => fi2.Length);

                // Get file content
                Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(temporaryDirectory.FullPath, "a")));
                using (var stream = File.OpenRead(Path.Combine(temporaryDirectory.FullPath, "b")))
                {
                    Assert.Equal(1, stream.ReadByte());
                    Assert.Equal(2, stream.ReadByte());
                }
            }
        }
    }
}
