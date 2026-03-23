namespace Meziantou.Framework.Json.Internals;

/// <summary>Wildcard selector: selects all children of an object or array.</summary>
internal sealed class WildcardSelector : Selector
{
    public static readonly WildcardSelector Instance = new();

    public override SelectorKind Kind => SelectorKind.Wildcard;
}
