namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A bind mount that maps a host path into the container.</summary>
/// <param name="Source">The path on the host.</param>
/// <param name="Target">The path inside the container.</param>
/// <param name="ReadOnly">Whether the mount is read-only.</param>
public sealed record BindMount(string Source, string Target, bool ReadOnly = false) : IMount;
