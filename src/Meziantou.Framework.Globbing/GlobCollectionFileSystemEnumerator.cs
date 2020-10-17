using System;

#if NET472
using Microsoft.IO;
using Microsoft.IO.Enumeration;
#else
using System.IO;
using System.IO.Enumeration;
#endif

namespace Meziantou.Framework.Globbing
{
    public abstract class GlobCollectionFileSystemEnumerator<T> : FileSystemEnumerator<T>
    {
        private readonly GlobCollection _globs;

        protected GlobCollectionFileSystemEnumerator(GlobCollection globs, string directory, EnumerationOptions? options = null)
            : base(directory, options)
        {
            _globs = globs;
        }

        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry)
        {
            return base.ShouldRecurseIntoEntry(ref entry) && _globs.ShouldTraverseFolder(GetRelativeDirectory(ref entry), entry.FileName);
        }

        protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
        {
            return base.ShouldIncludeEntry(ref entry) && !entry.IsDirectory && _globs.IsMatch(GetRelativeDirectory(ref entry), entry.FileName);
        }

        private static ReadOnlySpan<char> GetRelativeDirectory(ref FileSystemEntry entry)
        {
            if (entry.Directory.Length == entry.RootDirectory.Length)
                return ReadOnlySpan<char>.Empty;

            return entry.Directory[(entry.RootDirectory.Length + 1)..];
        }
    }

    public sealed class GlobCollectionFileSystemEnumerator : GlobCollectionFileSystemEnumerator<string>
    {
        public GlobCollectionFileSystemEnumerator(GlobCollection globs, string directory, EnumerationOptions? options = null)
            : base(globs, directory, options)
        {
        }

        protected override string TransformEntry(ref FileSystemEntry entry)
        {
            return entry.ToFullPath();
        }
    }
}
