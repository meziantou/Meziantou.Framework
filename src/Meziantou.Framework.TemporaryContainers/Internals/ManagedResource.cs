namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>A container or a volume the library created, as reported by a runtime listing.</summary>
/// <param name="Id">The container id or the volume name.</param>
/// <param name="Labels">The labels the resource carries.</param>
internal sealed record ManagedResource(string Id, IReadOnlyDictionary<string, string> Labels);
