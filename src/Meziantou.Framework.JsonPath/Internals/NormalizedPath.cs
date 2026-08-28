namespace Meziantou.Framework.Json.Internals;

/// <summary>
/// Holds the components of a matched node's normalized path and renders them on first use.
/// </summary>
/// <remarks>
/// Evaluation produces one of these per match, but most callers only read <c>Value</c> on some of them —
/// often none at all, as with <c>EvaluateValue</c>. Rendering eagerly made every evaluation pay for a string
/// per match whether or not anyone looked at it. This is a class rather than a struct so that the readonly
/// match structs can still cache the rendered string through the shared reference.
/// </remarks>
internal sealed class NormalizedPath
{
    private readonly List<PathComponent> _components;
    private string? _value;

    public NormalizedPath(List<PathComponent> components)
    {
        _components = components;
    }

    /// <summary>Gets the normalized path, building it on first access.</summary>
    /// <remarks>
    /// A benign race can render the same string twice; both are equal, so no lock is needed.
    /// </remarks>
    public string Value => _value ??= NormalizedPathBuilder.Build(_components);
}
