namespace Meziantou.Framework.Language;

/// <summary>What to do with the trivia around a node that is being removed.</summary>
/// <remarks>
/// A node's leading and trivia would normally disappear with it, which quietly throws away any comment sitting in
/// front of it. These say what to keep instead. Whatever is kept moves onto whatever now stands where the node was:
/// the thing that follows it, or the thing before it when nothing follows.
/// </remarks>
[Flags]
public enum SyntaxRemoveOptions
{
    /// <summary>Take the node and everything around it.</summary>
    KeepNoTrivia = 0,

    /// <summary>Keep what came in front of the node, such as the comment describing it.</summary>
    KeepLeadingTrivia = 1,

    /// <summary>Keep what came after the node.</summary>
    KeepTrailingTrivia = 2,

    /// <summary>Keep what came in front of and after the node.</summary>
    KeepExteriorTrivia = KeepLeadingTrivia | KeepTrailingTrivia,

    /// <summary>
    /// Keep one line break out of what was removed, so the lines either side of it do not run together. Has no effect
    /// when a line break was already kept by one of the other options.
    /// </summary>
    KeepEndOfLine = 4,
}
