namespace Meziantou.Framework.Globbing;

/// <summary>Options for controlling glob pattern parsing and matching behavior.</summary>
[Flags]
public enum GlobOptions
{
    /// <summary>No special options.</summary>
    None = 0,

    /// <summary>Perform case-insensitive matching. Only ASCII letters are supported for case-insensitive character ranges.</summary>
    IgnoreCase = 0x1,

    /// <summary>Allow wildcard patterns to match path segments that start with a dot.</summary>
    MatchLeadingDot = 0x2,
}
