namespace Meziantou.AspNetCore.Testing.Components.Internals;

internal static class ElementReferences
{
    /// <summary>The attribute that identifies the element of an <c>ElementReference</c>, as in Blazor.</summary>
    public const string AttributePrefix = "_bl_";

    public static string GetAttributeName(string captureId) => AttributePrefix + captureId;
}
