using System.Collections.Generic;
using System.IO;

namespace Meziantou.Framework.Win32.ProjectedFileSystem;

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
            yield return ProjectedFileSystemEntry.Directory("folder");
            yield return ProjectedFileSystemEntry.File("a", 1);
            yield return ProjectedFileSystemEntry.File("b", 2);
        }
        else if (AreFileNamesEqual(path, "folder"))
        {
            yield return ProjectedFileSystemEntry.File("c", 3);
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
