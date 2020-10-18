using System;
using System.Runtime.InteropServices;

#if NET472
using Microsoft.IO;
#else
using System.IO;
#endif

namespace Meziantou.Framework.Globbing.Internals
{
    [StructLayout(LayoutKind.Auto)]
    internal ref struct PathSegmentEnumerator
    {
        private ReadOnlySpan<char> _path;
        private ReadOnlySpan<char> _filename;

        public PathSegmentEnumerator(ReadOnlySpan<char> path, ReadOnlySpan<char> filename)
        {
            _path = path;
            _filename = filename;
            Current = default;
        }

        public ReadOnlySpan<char> Current { get; private set; }

        public PathSegmentEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            var span = _path;
            if (span.Length == 0) // Reach the end of the string
            {
                if (_filename.Length > 0)
                {
                    Current = _filename;
                    _filename = ReadOnlySpan<char>.Empty;
                    return true;
                }

                return false;
            }

            var index = span.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (index == -1) // The string is composed of only one line
            {
                _path = ReadOnlySpan<char>.Empty; // The remaining string is an empty string
                Current = span;
                return true;
            }

            Current = span[..index];
            _path = span[(index + 1)..];
            return true;
        }
    }
}
