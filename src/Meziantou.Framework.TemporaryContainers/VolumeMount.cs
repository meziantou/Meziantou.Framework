namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A named volume mounted into the container.</summary>
/// <param name="Name">The volume name.</param>
/// <param name="Target">The path inside the container.</param>
public sealed record VolumeMount(string Name, string Target) : IMount;
