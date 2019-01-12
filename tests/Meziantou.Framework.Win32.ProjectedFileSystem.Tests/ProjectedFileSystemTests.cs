﻿using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    [TestClass]
    public class ProjectedFileSystemTests
    {
        [TestMethod]
        public void Test()
        {
            var guid = Guid.NewGuid();
            var fullPath = Path.Combine(Path.GetTempPath(), "projFS", guid.ToString("N"));
            Directory.CreateDirectory(fullPath);

            using (var vfs = new SampleVirtualFileSystem(fullPath))
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
                    Assert.Inconclusive(ex.Message);
                }

                // Get content
                var files = Directory.GetFiles(fullPath);
                foreach (var result in files)
                {
                    var fi = new FileInfo(result);
                    var length = fi.Length;
                }

                var directories = Directory.GetDirectories(fullPath);
                Assert.AreEqual(1, directories.Length);
                Assert.AreEqual("folder", Path.GetFileName(directories[0]));

                // Get unknown file
                var fi2 = new FileInfo(Path.Combine(fullPath, "unknownfile.txt"));
                Assert.IsFalse(fi2.Exists);
                Assert.ThrowsException<FileNotFoundException>(() => fi2.Length);

                // Get file content
                CollectionAssert.AreEqual(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(fullPath, "a")));
                using (var stream = File.OpenRead(Path.Combine(fullPath, "b")))
                {
                    Assert.AreEqual(1, stream.ReadByte());
                    Assert.AreEqual(2, stream.ReadByte());
                }
            }
        }
    }
}
