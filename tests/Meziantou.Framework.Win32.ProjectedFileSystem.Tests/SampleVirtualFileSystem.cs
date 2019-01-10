using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    [TestClass]
    public class Tests
    {
        [TestMethod]
        public void Test()
        {
            var guid = Guid.NewGuid();
            var fullPath = Path.Combine(Path.GetTempPath(), "projFS", guid.ToString("N"));
            Directory.CreateDirectory(fullPath);

            using (var vfs = new SampleVirtualFileSystem(fullPath))
            {
                vfs.Initialize();
                var results = Directory.EnumerateFileSystemEntries(fullPath).ToList();
                foreach (var result in results)
                {
                    var fi = new FileInfo(result);
                    var length = fi.Length;

                }

                var fi2 = new FileInfo(Path.Combine(fullPath, "unknownfile.txt"));
                Assert.IsFalse(fi2.Exists);
                Assert.ThrowsException<FileNotFoundException>(() => fi2.Length);

                CollectionAssert.AreEqual(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(fullPath, "a")));
                using (var stream = File.OpenRead(Path.Combine(fullPath, "b")))
                {
                    Assert.AreEqual(1, stream.ReadByte());
                    Assert.AreEqual(2, stream.ReadByte());
                }
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
