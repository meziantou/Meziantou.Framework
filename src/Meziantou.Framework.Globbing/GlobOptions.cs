namespace Meziantou.Framework.Globbing;

/// <summary>Options for controlling glob pattern parsing and matching behavior.</summary>
[Flags]
public enum GlobOptions
{
    /// <summary>No special options.</summary>
    None = 0,

    /// <summary>
    ///     Perform case-insensitive matching: characters are compared as <see cref="StringComparison.OrdinalIgnoreCase"/>
    ///     compares them. A character matches a range when it is equal, ignoring the case, to one of the characters of the range.
    /// </summary>
    IgnoreCase = 0x1,

    /// <summary>
    ///     Allow wildcard patterns to match path segments that start with a dot. Only the <see cref="GlobDialect.Standard"/>
    ///     dialect uses it: the other dialects always match a leading dot, as git, MSBuild and fnmatch do.
    /// </summary>
    MatchLeadingDot = 0x2,
}
