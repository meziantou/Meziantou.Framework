using System.Collections.Generic;
using System.IO;

namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    internal sealed class SampleVirtualFileSystem : ProjectedFileSystemBase
    {
        public SampleVirtualFileSystem(string rootFolder)
            : base(rootFolder)
        {
        }

        protected override IEnumerable<ProjectedFileSystemEntry> GetEntries(string path)
        {
            if (AreFileNamesEqual(path, ""))
            {
                yield return new ProjectedFileSystemEntry() { Name = "folder", IsDirectory = true };
                yield return new ProjectedFileSystemEntry() { Name = "a", Length = 1 };
                yield return new ProjectedFileSystemEntry() { Name = "b", Length = 2 };
            }
            else if (AreFileNamesEqual(path, "folder"))
            {
                yield return new ProjectedFileSystemEntry() { Name = "c", Length = 3 };
            }
        }

        protected override Stream OpenRead(string path)
        {
            if (AreFileNamesEqual(path, "a"))
            {
                return new MemoryStream(new byte[] { 1 });
            }

            if (AreFileNamesEqual(path, "b"))
            {
                return new MemoryStream(new byte[] { 1, 2 });
            }

            if (AreFileNamesEqual(path, "folder\\c"))
            {
                return new MemoryStream(new byte[] { 1, 2, 3 });
            }

            return null;
        }
    }
}
