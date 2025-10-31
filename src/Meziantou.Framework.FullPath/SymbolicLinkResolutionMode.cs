namespace Meziantou.Framework;

public enum SymbolicLinkResolutionMode
{
    /// <summary>
    /// Return the immediate next link
    /// </summary>
    Immediate,

    /// <summary>
    /// Follow links to the final target
    /// </summary>
    FinalTarget,

    /// <summary>
    /// Follow all symbolic links in the path. The result does not not contain any symbolic link.
    /// </summary>
    AllSymbolicLinks,
}
