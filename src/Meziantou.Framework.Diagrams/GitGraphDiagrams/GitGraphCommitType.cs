namespace Meziantou.Framework.Diagrams;

/// <summary>The type of a commit of a Git graph, which sets its symbol.</summary>
public enum GitGraphCommitType
{
    /// <summary>A filled circle.</summary>
    Normal,

    /// <summary>A circle with a cross: a commit that reverts changes.</summary>
    Reverse,

    /// <summary>A filled square: a commit to notice.</summary>
    Highlight,
}
