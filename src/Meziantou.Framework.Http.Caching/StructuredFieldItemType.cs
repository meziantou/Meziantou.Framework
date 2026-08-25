namespace Meziantou.Framework.Http.Caching;

internal enum StructuredFieldItemType
{
    /// <summary>An item whose type is not used by <c>No-Vary-Search</c>: integer, decimal, token, or byte sequence.</summary>
    Other,
    Boolean,
    String,
}
