﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Globbing.Internals;

#if NET472
using Microsoft.IO;
#else
using System.IO;
#endif

namespace Meziantou.Framework.Globbing
{
    /// <summary>
    ///     Glob patterns specify sets of filenames with wildcard characters. Supported syntaxes:
    ///     <list type="table">
    ///         <listheader>
    ///             <term>Wildcard</term>
    ///             <description>Description</description>
    ///         </listheader>
    ///         <item>
    ///             <term>*</term>
    ///             <description>matches any number of characters including none, excluding directory seperator</description>
    ///         </item>
    ///         <item>
    ///             <term>?</term>
    ///             <description>matches a single character</description>
    ///         </item>
    ///         <item>
    ///             <term>[abc]</term>
    ///             <description>matches one character in the brackets</description>
    ///         </item>
    ///         <item>
    ///             <term>[!abc]</term>
    ///             <description>matches any character not in the brackets</description>
    ///         </item>
    ///         <item>
    ///             <term>[a-z]</term>
    ///             <description>matches one character from the range given in the bracket. <c>GlobOptions.IgnoreCase</c> is only supported for ASCII letters.</description>
    ///         </item>
    ///         <item>
    ///             <term>[!a-z]</term>
    ///             <description>matches one character that is not from the range given in the bracket. <c>GlobOptions.IgnoreCase</c> is only supported for ASCII letters.</description>
    ///         </item>
    ///         <item>
    ///             <term>{abc,123}</term>
    ///             <description>comma delimited set of literals, matched 'abc' or '123'</description>
    ///         </item>
    ///         <item>
    ///             <term>**</term>
    ///             <description>match zero or more directories</description>
    ///         </item>
    ///         <item>
    ///             <term>!pattern</term>
    ///             <description>Leading '!' negates the pattern</description>
    ///         </item>
    ///         <item>
    ///             <term>\x</term>
    ///             <description>Escape the following character. For instance '\*' matches the character '*' instead of being the wildcard character.</description>
    ///         </item>
    ///     </list>
    /// </summary>
    /// <seealso href="https://en.wikipedia.org/wiki/Glob_(programming)"/>
    public sealed class Glob
    {
        internal readonly Segment[] _segments;

        public GlobMode Mode { get; }

        internal Glob(Segment[] segments, GlobMode mode)
        {
            _segments = segments;
            Mode = mode;
        }

        public static Glob Parse(string pattern, GlobOptions options)
        {
            return Parse(pattern.AsSpan(), options);
        }

        public static Glob Parse(ReadOnlySpan<char> pattern, GlobOptions options)
        {
            if (TryParse(pattern, options, out var result, out var errorMessage))
                return result;

            throw new ArgumentException($"The pattern '{pattern.ToString()}' is invalid: {errorMessage}", nameof(pattern));
        }

        public static bool TryParse(string pattern, GlobOptions options, [NotNullWhen(true)] out Glob? result)
        {
            return TryParse(pattern.AsSpan(), options, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> pattern, GlobOptions options, [NotNullWhen(true)] out Glob? result)
        {
            return TryParse(pattern, options, out result, out _);
        }

        private static bool TryParse(ReadOnlySpan<char> pattern, GlobOptions options, [NotNullWhen(true)] out Glob? result, [NotNullWhen(false)] out string? errorMessage)
        {
            return GlobParser.TryParse(pattern, options, out result, out errorMessage);
        }

        public bool IsMatch(string path) => IsMatch(path.AsSpan());

        public bool IsMatch(ReadOnlySpan<char> path)
        {
            var pathEnumerator = new PathReader(path, ReadOnlySpan<char>.Empty);
            return IsMatchCore(pathEnumerator, _segments);
        }

        public bool IsMatch(string directory, string filename) => IsMatch(directory.AsSpan(), filename.AsSpan());

        public bool IsMatch(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename)
        {
            if (filename.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) >= 0)
                throw new ArgumentException("Filename contains a directory separator", nameof(filename));

            return IsMatchCore(directory, filename);
        }

        internal bool IsMatchCore(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename)
        {
            var pathEnumerator = new PathReader(directory, filename);
            return IsMatchCore(pathEnumerator, _segments);
        }

        private bool IsMatchCore(PathReader pathReader, ReadOnlySpan<Segment> patternSegments)
        {
            for (var i = 0; i < patternSegments.Length; i++)
            {
                var patternSegment = patternSegments[i];
                if (patternSegment is RecursiveMatchAllSegment)
                {
                    var remainingPatternSegments = patternSegments[(i + 1)..];
                    if (remainingPatternSegments.IsEmpty) // Last segment
                        return true;

                    if (IsMatchCore(pathReader, remainingPatternSegments))
                        return true;

                    pathReader.ConsumeSegment();
                    while (!pathReader.IsEndOfPath)
                    {
                        if (IsMatchCore(pathReader, remainingPatternSegments))
                            return true;

                        pathReader.ConsumeSegment();
                    }

                    return false;
                }

                if (pathReader.IsEndOfPath)
                    return false;

                if (!patternSegment.IsMatch(ref pathReader))
                    return false;

                if (!pathReader.IsEndOfCurrentSegment)
                    return false;

                pathReader.ConsumeEndOfSegment();
            }

            // Ensure the path is fully parsed
            return pathReader.IsEndOfPath;
        }

        public bool IsPartialMatch(string folderPath) => IsPartialMatch(folderPath.AsSpan());

        public bool IsPartialMatch(ReadOnlySpan<char> folderPath)
        {
            return IsPartialMatchCore(new PathReader(folderPath, ReadOnlySpan<char>.Empty), _segments);
        }

        internal bool IsPartialMatchCore(ReadOnlySpan<char> folderPath, ReadOnlySpan<char> filename)
        {
            return IsPartialMatchCore(new PathReader(folderPath, filename), _segments);
        }

        private static bool IsPartialMatchCore(PathReader pathReader, ReadOnlySpan<Segment> patternSegments)
        {
            foreach (var patternSegment in patternSegments)
            {
                if (ShouldRecurse(patternSegment))
                    return true;

                if (pathReader.IsEndOfPath)
                    return true;

                if (!patternSegment.IsMatch(ref pathReader))
                    return false;

                pathReader.ConsumeSegment();
                patternSegments = patternSegments[1..];
            }

            return true;
        }

        public IEnumerable<string> EnumerateFiles(string directory, EnumerationOptions? options = null)
        {
            if (options is null && ShouldRecurseSubdirectories())
            {
                options = new EnumerationOptions { RecurseSubdirectories = true };
            }

            using var enumerator = new GlobFileSystemEnumerator(this, directory, options);
            while (enumerator.MoveNext())
                yield return enumerator.Current;
        }

        internal bool ShouldRecurseSubdirectories()
        {
            return _segments.Length > 1 || ShouldRecurse(_segments[0]);
        }

        private static bool ShouldRecurse(Segment patternSegment)
        {
            return patternSegment.IsRecursiveMatchAll;
        }

        public override string ToString()
        {
            using var sb = new ValueStringBuilder();
            if (Mode == GlobMode.Exclude)
            {
                sb.Append('!');
            }
            var first = true;
            foreach (var segment in _segments)
            {
                if (!first)
                {
                    sb.Append('/');
                }

                sb.Append(segment.ToString());
                first = false;
            }

            return sb.ToString();
        }
    }
}
