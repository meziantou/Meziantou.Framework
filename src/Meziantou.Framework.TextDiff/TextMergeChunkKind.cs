namespace Meziantou.Framework;

/// <summary>Specifies how a chunk of a three-way merge was resolved.</summary>
public enum TextMergeChunkKind
{
    /// <summary>Neither side changed the chunk.</summary>
    Unchanged,

    /// <summary>Only "ours" changed the chunk: the merge takes it from "ours".</summary>
    ChangedInOurs,

    /// <summary>Only "theirs" changed the chunk: the merge takes it from "theirs".</summary>
    ChangedInTheirs,

    /// <summary>Both sides made the same change: the merge takes it from "ours".</summary>
    ChangedInBoth,

    /// <summary>Both sides made different changes: the merge cannot choose.</summary>
    Conflict,
}
