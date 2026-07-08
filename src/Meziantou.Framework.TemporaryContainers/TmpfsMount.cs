namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A tmpfs (in-memory) mount inside the container.</summary>
/// <param name="Target">The path inside the container.</param>
public sealed record TmpfsMount(string Target) : IMount;
