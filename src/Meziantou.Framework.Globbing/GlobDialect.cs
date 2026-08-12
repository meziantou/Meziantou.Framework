namespace Meziantou.Framework.Globbing;

/// <summary>Specifies the glob pattern dialect to use when parsing a pattern.</summary>
public enum GlobDialect
{
    /// <summary>Use the default glob pattern syntax.</summary>
    Standard = 0,

    /// <summary>Use gitignore pattern syntax.</summary>
    Git = 1,

    /// <summary>Use MSBuild glob pattern syntax.</summary>
    MSBuild = 2,

    /// <summary>Use POSIX fnmatch pattern syntax without path-separator awareness.</summary>
    Posix = 3,

    /// <summary>Use POSIX fnmatch pattern syntax where ordinary wildcards do not cross path separators.</summary>
    PosixPath = 4,
}
