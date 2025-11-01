namespace Meziantou.Framework.Globbing;

/// <summary>Determines whether a glob pattern should include or exclude matching paths.</summary>
public enum GlobMode
{
    /// <summary>Include matching paths.</summary>
    Include,

    /// <summary>Exclude matching paths.</summary>
    Exclude,
}
