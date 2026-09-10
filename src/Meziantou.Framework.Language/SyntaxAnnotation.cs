namespace Meziantou.Framework.Language;

/// <summary>
/// A marker that can be attached to a node, token, or trivia and found again after the tree has been edited.
/// </summary>
/// <remarks>
/// Two annotations are equal only when they are the same annotation, even when their <see cref="Kind"/> and
/// <see cref="Data"/> match. That is what makes "find the node I marked earlier" work: an edit rebuilds the nodes
/// around the marked one, but carries its annotations forward, so the new tree can be searched for this exact
/// instance.
/// </remarks>
/// <example>
/// <code>
/// var marker = new SyntaxAnnotation();
/// var annotated = node.WithAdditionalAnnotations(marker);
/// var updated = root.ReplaceNode(node, annotated);
/// var found = updated.GetAnnotatedNodes(marker).Single();
/// </code>
/// </example>
public sealed class SyntaxAnnotation : IEquatable<SyntaxAnnotation?>
{
    private static long s_nextId;

    private readonly long _id;

    public SyntaxAnnotation() => _id = Interlocked.Increment(ref s_nextId);

    public SyntaxAnnotation(string? kind)
        : this() => Kind = kind;

    public SyntaxAnnotation(string? kind, string? data)
        : this(kind) => Data = data;

    /// <summary>Gets the category of the annotation, used to find annotations without holding the instance.</summary>
    public string? Kind { get; }

    /// <summary>Gets the payload carried alongside the annotation.</summary>
    public string? Data { get; }

    public bool Equals([NotNullWhen(true)] SyntaxAnnotation? other) => other is not null && _id == other._id;
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as SyntaxAnnotation);
    public override int GetHashCode() => _id.GetHashCode();

    public static bool operator ==(SyntaxAnnotation? left, SyntaxAnnotation? right) => left is null ? right is null : left.Equals(right);
    public static bool operator !=(SyntaxAnnotation? left, SyntaxAnnotation? right) => !(left == right);

    public override string ToString() => $"{Kind}: {Data}";
}
