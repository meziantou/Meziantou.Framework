using Meziantou.Framework.Globbing.Internals;

namespace Meziantou.Framework.Globbing;

public sealed partial class Glob
{
    internal GlobMatchType MatchType => _matchType;

    /// <summary>
    ///     Determines whether the pattern matches every path below a directory, which lets an enumeration skip a
    ///     directory that an exclude pattern removes as a whole. The answer is conservative: <see cref="DirectoryContentMatch.None"/>
    ///     only means that the pattern was not proven to match everything.
    /// </summary>
    /// <param name="folderPath">The parent of the directory, or the whole path of the directory.</param>
    /// <param name="filename">The name of the directory, or an empty span when <paramref name="folderPath"/> holds the whole path.</param>
    internal DirectoryContentMatch MatchesDirectoryContent(ReadOnlySpan<char> folderPath, ReadOnlySpan<char> filename)
    {
        if (!_pathSeparatorAware)
            return DirectoryContentMatch.None;

        return MatchesDirectoryContent(new PathReader(folderPath, filename, PathItemType.Directory), _segments, _matchLeadingDot);
    }

    private static DirectoryContentMatch MatchesDirectoryContent(PathReader pathReader, ReadOnlySpan<Segment> patternSegments, ReadOnlySpan<bool> matchLeadingDot)
    {
        for (var i = 0; i < patternSegments.Length; i++)
        {
            // The directory is fully consumed: what is left of the pattern must match any path below it
            if (pathReader.IsEndOfPath)
                return MatchesAnyNonEmptyPath(patternSegments[i..], matchLeadingDot[i..]);

            var patternSegment = patternSegments[i];
            switch (patternSegment)
            {
                case RecursiveMatchAllSegment:
                {
                    // '**' matches no segment of the directory here, or consumes its current segment. Matching one
                    // alignment is enough, as matching a path only needs one.
                    var result = DirectoryContentMatch.None;
                    if (i + 1 < patternSegments.Length)
                    {
                        result = MatchesDirectoryContent(pathReader, patternSegments[(i + 1)..], matchLeadingDot[(i + 1)..]);
                        if (result is DirectoryContentMatch.All)
                            return result;
                    }

                    if (!CanMatchLeadingDot(pathReader, matchLeadingDot[i]))
                        return result;

                    pathReader.ConsumeSegment();
                    var consumed = MatchesDirectoryContent(pathReader, patternSegments[i..], matchLeadingDot[i..]);
                    return consumed > result ? consumed : result;
                }

                // These segments consume the rest of the path, whatever it holds
                case MatchNonEmptyTextSegment:
                case DirectoryContentSegment when ReferenceEquals(patternSegment, DirectoryContentSegment.IncludingContent):
                    return CanMatchLeadingDot(pathReader, matchLeadingDot[i]) ? DirectoryContentMatch.All : DirectoryContentMatch.None;

                // The other segments that consume several path segments depend on the path below the directory
                case { IsRecursiveMatchAll: true }:
                    return DirectoryContentMatch.None;
            }

            if (!CanMatchLeadingDot(pathReader, matchLeadingDot[i]))
                return DirectoryContentMatch.None;

            if (!patternSegment.IsMatch(ref pathReader) || !pathReader.IsEndOfCurrentSegment)
                return DirectoryContentMatch.None;

            pathReader.ConsumeEndOfSegment();
        }

        // The pattern stops at the directory or above it, so it matches nothing below the directory
        return DirectoryContentMatch.None;
    }

    // Whether the segments match any path made of one or more segments
    private static DirectoryContentMatch MatchesAnyNonEmptyPath(ReadOnlySpan<Segment> patternSegments, ReadOnlySpan<bool> matchLeadingDot)
    {
        var allowsLeadingDot = patternSegments switch
        {
            [RecursiveMatchAllSegment] => matchLeadingDot[0],
            [MatchNonEmptyTextSegment] => matchLeadingDot[0],
            [DirectoryContentSegment segment] when ReferenceEquals(segment, DirectoryContentSegment.IncludingContent) => matchLeadingDot[0],
            [RecursiveMatchAllSegment, MatchAllSegment] => matchLeadingDot[0] && matchLeadingDot[1],
            _ => (bool?)null,
        };

        return allowsLeadingDot switch
        {
            null => DirectoryContentMatch.None,
            true => DirectoryContentMatch.All,
            false => DirectoryContentMatch.AllExceptLeadingDot,
        };
    }
}
