namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>A <see cref="VolumeMount"/> that keeps the <see cref="TemporaryVolume"/> it comes from, so the container can create the volume before it starts.</summary>
/// <param name="Volume">The volume to mount.</param>
/// <param name="Target">The path inside the container.</param>
/// <param name="ReadOnly">Whether the mount is read-only.</param>
internal sealed record OwnedVolumeMount(TemporaryVolume Volume, string Target, bool ReadOnly) : IMount;
