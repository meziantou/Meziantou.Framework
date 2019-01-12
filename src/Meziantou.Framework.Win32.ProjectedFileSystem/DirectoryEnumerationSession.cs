using System;
using System.Collections.Generic;

namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    internal class DirectoryEnumerationSession : IDisposable
    {
        private IEnumerator<ProjectedFileSystemEntry> _enumerator;
        private ProjectedFileSystemEntry _current;

        public DirectoryEnumerationSession(IEnumerable<ProjectedFileSystemEntry> entries)
        {
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        public ProjectedFileSystemEntry GetNextEntry()
        {
            if (_current != null)
            {
                var current = _current;
                _current = null;
                return current;
            }

            if (_enumerator == null)
            {
                _enumerator = Entries.GetEnumerator();
            }

            if (_enumerator.MoveNext())
            {
                return _enumerator.Current;
            }

            return null;
        }

        public void Reenqueue()
        {
            _current = _enumerator.Current;
        }

        public IEnumerable<ProjectedFileSystemEntry> Entries { get; }

        public void Dispose()
        {
            Reset();
        }

        internal void Reset()
        {
            _current = null;
            _enumerator?.Dispose();
        }
    }

    //class Program
    //{
    //    // TODO notification on rename/delete/... 

    //    // https://github.com/Microsoft/Windows-classic-samples/blob/master/Samples/ProjectedFileSystem/regfsProvider.cpp
    //    static void Main(string[] args)
    //    {
    //        var guid = Guid.NewGuid();
    //        var fullPath = Path.Combine(Path.GetTempPath(), "projFS", guid.ToString("N"));
    //        Directory.CreateDirectory(fullPath);

    //        using (var vfs = new SampleVirtualFileSystem(fullPath))
    //        {
    //            vfs.Initialize();
    //            var results = Directory.EnumerateFileSystemEntries(fullPath).ToList();
    //            foreach (var result in results)
    //            {
    //                var fi = new FileInfo(result);
    //                var length = fi.Length;

    //            }

    //            try
    //            {
    //                var fi2 = new FileInfo(Path.Combine(fullPath, "unknownfile.txt"));
    //                var length2 = fi2.Length;
    //                Debug.Fail("File does not exist");
    //            }
    //            catch (FileNotFoundException)
    //            {
    //            }

    //            var bytes = File.ReadAllBytes(Path.Combine(fullPath, "a"));
    //            using (var stream = File.OpenRead(Path.Combine(fullPath, "b")))
    //            {
    //                var b1 = stream.ReadByte();
    //                var b2 = stream.ReadByte();
    //            }
    //        }
    //    }
    //}
}
