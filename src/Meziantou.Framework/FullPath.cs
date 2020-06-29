#if NET461 || NETSTANDARD2_0
#elif NETCOREAPP3_1
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Meziantou.Framework
{
    public readonly struct FullPath : IEquatable<FullPath>, IComparable<FullPath>
    {
        internal readonly string? _value;

        private FullPath(string path)
        {
            // The checks are already performed in the static methods
            // No need to check if the path is null or absolute here
            Debug.Assert(path != null);
            Debug.Assert(Path.IsPathFullyQualified(path));
            Debug.Assert(Path.GetFullPath(path) == path);
            _value = path;
        }

        public static FullPath Empty => default;

        public bool IsEmpty => _value is null;

        public static implicit operator string?(FullPath fullPath) => fullPath._value;

        public static bool operator ==(FullPath path1, FullPath path2) => path1.Equals(path2);
        public static bool operator !=(FullPath path1, FullPath path2) => !(path1 == path2);
        public static bool operator <(FullPath path1, FullPath path2) => path1.CompareTo(path2) < 0;
        public static bool operator >(FullPath path1, FullPath path2) => path1.CompareTo(path2) > 0;
        public static bool operator <=(FullPath path1, FullPath path2) => path1.CompareTo(path2) <= 0;
        public static bool operator >=(FullPath path1, FullPath path2) => path1.CompareTo(path2) >= 0;

        public static FullPath operator +(FullPath rootPath, string relativePath) => FromPath(rootPath, relativePath);
        public static FullPath operator /(FullPath rootPath, string relativePath) => FromPath(rootPath, relativePath);

        public string? GetExtension() => Path.GetExtension(_value);

        public FullPath GetDirectoryName()
        {
            var result = Path.GetDirectoryName(_value);
            if (result is null)
                return Empty;

            return new FullPath(result);
        }

        public int CompareTo(FullPath other) => FullPathComparer.Default.Compare(this, other);
        public int CompareTo(FullPath other, bool ignoreCase) => FullPathComparer.GetComparer(ignoreCase).Compare(this, other);

        public override bool Equals(object? obj) => obj is FullPath path && Equals(path);
        public bool Equals(FullPath other) => FullPathComparer.Default.Equals(this, other);
        public bool Equals(FullPath other, bool ignoreCase) => FullPathComparer.GetComparer(ignoreCase).Equals(this, other);

        public override int GetHashCode() => FullPathComparer.Default.GetHashCode(this);
        public int GetHashCode(bool ignoreCase) => FullPathComparer.GetComparer(ignoreCase).GetHashCode(this);

        public override string ToString() => _value ?? "";

        public string MakePathRelativeTo(FullPath rootPath)
        {
            if (IsEmpty)
                throw new InvalidOperationException("The path is empty");

            Debug.Assert(_value != null);

            if (rootPath.IsEmpty)
                return _value;

            if (rootPath == this)
                return ".";

            Debug.Assert(rootPath._value != null);
            return PathDifference(rootPath._value + Path.DirectorySeparatorChar, _value, compareCase: FullPathComparer.Default.IsCaseSensitive);
        }

        private static string PathDifference(string path1, string path2, bool compareCase)
        {
            var directorySeparator = Path.DirectorySeparatorChar;

            int i;
            int si = -1;

            for (i = 0; (i < path1.Length) && (i < path2.Length); ++i)
            {
                if ((path1[i] != path2[i]) && (compareCase || (char.ToUpperInvariant(path1[i]) != char.ToUpperInvariant(path2[i]))))
                    break;

                if (path1[i] == directorySeparator)
                {
                    si = i;
                }
            }

            if (i == 0)
                return path2;

            if ((i == path1.Length) && (i == path2.Length))
                return string.Empty;

            var relPath = new StringBuilder();
            // Walk down several dirs
            for (; i < path1.Length; ++i)
            {
                if (path1[i] == directorySeparator)
                {
                    relPath.Append("..");
                    relPath.Append(directorySeparator);
                }
            }
            // Same path except that path1 ended with a file name and path2 didn't
            if (relPath.Length == 0 && path2.Length - 1 == si)
                return "." + directorySeparator; // Truncate the file name

            return relPath.Append(path2.AsSpan(si + 1)).ToString();
        }

        public bool IsChildOf(FullPath rootPath)
        {
            if (IsEmpty)
                throw new InvalidOperationException("Path is empty");
            if (rootPath.IsEmpty)
                throw new ArgumentException("Root path is empty", nameof(rootPath));

            Debug.Assert(_value != null);
            Debug.Assert(rootPath._value != null);

            if (_value.Length <= rootPath._value.Length)
                return false;

            if (!_value.StartsWith(rootPath._value))
                return false;

            // rootpath: /a/b
            // current:  /a/b/c => true
            // current:  /a/b/  => false
            // current:  /a/bc  => false
            if (_value[rootPath._value.Length] == Path.DirectorySeparatorChar && _value.Length > rootPath._value.Length + 1)
                return true;

            return false;
        }

        public static FullPath FromPath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var fullPathWithoutTrailingDirectorySeparator = Path.TrimEndingDirectorySeparator(fullPath);
            if (string.IsNullOrEmpty(fullPathWithoutTrailingDirectorySeparator))
                return Empty;

            return new FullPath(fullPathWithoutTrailingDirectorySeparator);
        }

        public static FullPath FromPath(string rootPath, string relativePath) => FromPath(Path.Combine(rootPath, relativePath));
        public static FullPath FromPath(string rootPath, string path1, string path2) => FromPath(Path.Combine(rootPath, path1, path2));
        public static FullPath FromPath(string rootPath, string path1, string path2, string path3) => FromPath(Path.Combine(rootPath, path1, path2, path3));
        public static FullPath FromPath(params string[] paths) => FromPath(Path.Combine(paths));

        public static FullPath FromPath(FullPath rootPath, string relativePath)
        {
            if (rootPath.IsEmpty)
                return FromPath(relativePath);

            return FromPath(Path.Combine(rootPath._value!, relativePath));
        }

        public static FullPath FromPath(FullPath rootPath, string path1, string path2)
        {
            if (rootPath.IsEmpty)
                return FromPath(Path.Combine(path1, path2));

            return FromPath(Path.Combine(rootPath._value!, path1, path2));
        }

        public static FullPath FromPath(FullPath rootPath, string path1, string path2, string path3)
        {
            if (rootPath.IsEmpty)
                return FromPath(Path.Combine(path1, path2, path3));

            return FromPath(Path.Combine(rootPath._value!, path1, path2, path3));
        }
    }
}
#else
#error Platform not supported
#endif
