namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Represents a mount attached to a container. Implemented by <see cref="BindMount"/>, <see cref="VolumeMount"/>, and <see cref="TmpfsMount"/>, the only mounts the runtimes know how to create: the interface cannot be implemented outside of this library.</summary>
public interface IMount
{
    /// <summary>Gets the path of the mount inside the container. Being internal, it keeps other assemblies from implementing the interface.</summary>
    internal string ContainerPath { get; }
}
