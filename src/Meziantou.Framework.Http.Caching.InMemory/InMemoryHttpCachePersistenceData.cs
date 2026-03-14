namespace Meziantou.Framework.Http.Caching.InMemory;

internal sealed class InMemoryHttpCachePersistenceData
{
    public Dictionary<string, List<HttpCachePersistenceEntry>> Entries { get; set; } = new(StringComparer.Ordinal);
}
