namespace Meziantou.Framework.Globbing.Internals;

/// <summary>Tells how much of the content of a directory a pattern matches. The values are ordered from the weakest to the strongest.</summary>
internal enum DirectoryContentMatch
{
    /// <summary>The pattern was not proven to match every path below the directory.</summary>
    None,

    /// <summary>The pattern matches every path below the directory, except the ones holding a segment that starts with a dot.</summary>
    AllExceptLeadingDot,

    /// <summary>The pattern matches every path below the directory.</summary>
    All,
}
