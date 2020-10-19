using System;
using System.Collections.Generic;
using System.Linq;

#if NET472
using Microsoft.IO;
#else
using System.IO;
#endif

namespace Meziantou.Framework.Globbing
{
    public sealed class GlobCollection
    {
        private readonly Glob[] _globs;

        public GlobCollection(params Glob[] globs)
        {
            _globs = globs;
        }

        public bool IsMatch(string path) => IsMatch(path.AsSpan());
        public bool IsMatch(ReadOnlySpan<char> path) => IsMatch(path, ReadOnlySpan<char>.Empty);
        public bool IsMatch(string directory, string filename) => IsMatch(directory.AsSpan(), filename.AsSpan());

        public bool IsMatch(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename)
        {
            var match = false;
            foreach (var glob in _globs)
            {
                if (match && glob.Mode == GlobMode.Include)
                    continue;

                if (glob.IsMatch(directory, filename))
                {
                    if (glob.Mode == GlobMode.Exclude)
                        return false;

                    match = true;
                }
            }

            return match;
        }

        public bool IsPartialMatch(string folderPath) => IsPartialMatch(folderPath.AsSpan());
        public bool IsPartialMatch(ReadOnlySpan<char> folderPath) => IsPartialMatch(folderPath, ReadOnlySpan<char>.Empty);

        internal bool IsPartialMatch(ReadOnlySpan<char> folderPath, ReadOnlySpan<char> filename)
        {
            foreach (var glob in _globs)
            {
                if (glob.Mode == GlobMode.Exclude)
                    continue;

                if (glob.IsPartialMatch(folderPath, filename))
                    return true;
            }

            return false;
        }

        public IEnumerable<string> EnumerateFiles(string directory, EnumerationOptions? options = null)
        {
            if (options is null && _globs.Any(glob => glob.ShouldRecurseSubdirectories()))
            {
                options = new EnumerationOptions { RecurseSubdirectories = true };
            }

            using var enumerator = new GlobCollectionFileSystemEnumerator(this, directory, options);
            while (enumerator.MoveNext())
                yield return enumerator.Current;
        }
    }
}
