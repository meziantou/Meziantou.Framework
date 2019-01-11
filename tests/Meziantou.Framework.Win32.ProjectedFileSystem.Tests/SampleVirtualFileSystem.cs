using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    [TestClass]
    public class Tests
    {
        [TestMethod]
        public async Task Test()
        {
            var guid = Guid.NewGuid();
            var fullPath = Path.Combine(Path.GetTempPath(), "projFS", guid.ToString("N"));
            Directory.CreateDirectory(fullPath);

            using (var vfs = new SampleVirtualFileSystem(fullPath))
            {
                var options = new StartOptions();
                options.UseNegativePathCache = false;
                //options.Notifications.Add(new Notification(
                //    NotificationType.PRJ_NOTIFY_FILE_HANDLE_CLOSED_FILE_DELETED |
                //    NotificationType.PRJ_NOTIFY_FILE_HANDLE_CLOSED_FILE_MODIFIED |
                //    NotificationType.PRJ_NOTIFY_FILE_HANDLE_CLOSED_NO_MODIFICATION |
                //    NotificationType.PRJ_NOTIFY_FILE_OPENED |
                //    NotificationType.PRJ_NOTIFY_FILE_OVERWRITTEN |
                //    NotificationType.PRJ_NOTIFY_FILE_PRE_CONVERT_TO_FULL |
                //    NotificationType.PRJ_NOTIFY_FILE_RENAMED |
                //    NotificationType.PRJ_NOTIFY_HARDLINK_CREATED |
                //    NotificationType.PRJ_NOTIFY_NEW_FILE_CREATED |
                //    NotificationType.PRJ_NOTIFY_PRE_DELETE |
                //    NotificationType.PRJ_NOTIFY_PRE_RENAME |
                //    NotificationType.PRJ_NOTIFY_PRE_SET_HARDLINK
                //    ));

                vfs.Start(options);
                //var results = Directory.EnumerateFileSystemEntries(fullPath).ToList();
                //foreach (var result in results)
                //{
                //    var fi = new FileInfo(result);
                //    var length = fi.Length;

                //}

                //var fi2 = new FileInfo(Path.Combine(fullPath, "unknownfile.txt"));
                //Assert.IsFalse(fi2.Exists);
                //Assert.ThrowsException<FileNotFoundException>(() => fi2.Length);

                //CollectionAssert.AreEqual(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(fullPath, "a")));
                //using (var stream = File.OpenRead(Path.Combine(fullPath, "b")))
                //{
                //    Assert.AreEqual(1, stream.ReadByte());
                //    Assert.AreEqual(2, stream.ReadByte());
                //}

                Console.Read();
                await Task.Delay(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            }
        }
    }

    internal class SampleVirtualFileSystem : VirtualFileSystem
    {
        public SampleVirtualFileSystem(string rootFolder)
            : base(rootFolder)
        {
        }

        protected override IEnumerable<VirtualFileSystemEntry> GetEntries(string path)
        {
            yield return new VirtualFileSystemEntry() { Name = "a", Length = 1 };
            yield return new VirtualFileSystemEntry() { Name = "b", Length = 2 };
        }

        protected override VirtualFileSystemEntry GetEntry(string path)
        {
            return GetEntries(null).FirstOrDefault(p => CompareFileName(p.Name, path) == 0);
        }

        protected override Stream OpenRead(string path)
        {
            if (CompareFileName(path, "a") == 0)
            {
                return new MemoryStream(new byte[] { 1 });
            }

            if (CompareFileName(path, "b") == 0)
            {
                return new MemoryStream(new byte[] { 1, 2 });
            }

            return null;
        }
    }
}
