namespace Meziantou.Framework.Json.Internals;

/// <summary>
/// The last step of a location in the value tree, linked to the steps before it. A <see langword="null"/> path is
/// the root. Extending a path shares its prefix instead of copying it, so it costs the same at any depth.
/// </summary>
internal sealed class PathNode
{
    private PathNode(PathNode? parent, string? name, long index)
    {
        Parent = parent;
        Name = name;
        Index = index;
        Depth = parent is null ? 1 : parent.Depth + 1;
    }

    public PathNode? Parent { get; }

    /// <summary>Gets the member name, or <see langword="null"/> when the step is an array index.</summary>
    public string? Name { get; }

    public long Index { get; }

    /// <summary>Gets the number of steps from the root to this one.</summary>
    public int Depth { get; }

    public static PathNode FromName(PathNode? parent, string name) => new(parent, name, index: 0);

    public static PathNode FromIndex(PathNode? parent, long index) => new(parent, name: null, index);
}
