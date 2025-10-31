namespace Meziantou.Framework.Globbing;

/// <summary>Options for controlling glob pattern parsing and matching behavior.</summary>
[Flags]
public enum GlobOptions
{
    /// <summary>No special options.</summary>
    None = 0,

    /// <summary>Perform case-insensitive matching. Only ASCII letters are supported for case-insensitive character ranges.</summary>
    IgnoreCase = 0x1,

    /// <summary>Use gitignore patterns</summary>
    Git = 0x2,
}
