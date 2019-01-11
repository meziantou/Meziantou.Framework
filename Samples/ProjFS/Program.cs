using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Meziantou.Framework.Win32.ProjectedFileSystem;

namespace ProjFS
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            var guid = Guid.NewGuid();
            var fullPath = Path.Combine(Path.GetTempPath(), "projFS", guid.ToString("N"));
            Directory.CreateDirectory(fullPath);

            using (var vfs = new SampleVirtualFileSystem(fullPath))
            {
                var options = new StartOptions();
                options.UseNegativePathCache = false;

                options.Notifications.Add(new Notification(
                    NotificationType.PRJ_NOTIFY_FILE_HANDLE_CLOSED_FILE_DELETED |
                    NotificationType.PRJ_NOTIFY_FILE_HANDLE_CLOSED_FILE_MODIFIED |
                    NotificationType.PRJ_NOTIFY_FILE_HANDLE_CLOSED_NO_MODIFICATION |
                    NotificationType.PRJ_NOTIFY_FILE_OPENED |
                    NotificationType.PRJ_NOTIFY_FILE_OVERWRITTEN |
                    NotificationType.PRJ_NOTIFY_FILE_PRE_CONVERT_TO_FULL |
                    NotificationType.PRJ_NOTIFY_FILE_RENAMED |
                    //NotificationType.PRJ_NOTIFY_HARDLINK_CREATED |
                    NotificationType.PRJ_NOTIFY_NEW_FILE_CREATED |
                    NotificationType.PRJ_NOTIFY_PRE_DELETE |
                    NotificationType.PRJ_NOTIFY_PRE_RENAME
                    //| NotificationType.PRJ_NOTIFY_PRE_SET_HARDLINK
                    ));


                vfs.Start(options);

                Console.WriteLine("Started " + fullPath);
                Console.ReadLine();
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
            yield return new VirtualFileSystemEntry() { Name = "a.txt", Length = 1 };
            yield return new VirtualFileSystemEntry() { Name = "b.txt", Length = 2 };
        }

        protected override VirtualFileSystemEntry GetEntry(string path)
        {
            return GetEntries(null).FirstOrDefault(p => CompareFileName(p.Name, path) == 0);
        }

        protected override Stream OpenRead(string path)
        {
            if (CompareFileName(path, "a.txt") == 0)
            {
                return new MemoryStream(Encoding.Default.GetBytes("a"));
            }

            if (CompareFileName(path, "b.txt") == 0)
            {
                return new MemoryStream(Encoding.Default.GetBytes("b"));
            }

            return null;
        }
    }
}
