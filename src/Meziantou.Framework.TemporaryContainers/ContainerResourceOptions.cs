namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Resource limits for a container.</summary>
public sealed class ContainerResourceOptions
{
    internal ContainerResourceOptions()
    {
    }

    internal ContainerResourceOptions(ContainerResourceOptions other)
    {
        MemoryLimit = other.MemoryLimit;
        CpuLimit = other.CpuLimit;
        ReadOnlyRootFilesystem = other.ReadOnlyRootFilesystem;
    }

    /// <summary>Gets or sets the memory limit in bytes.</summary>
    public long? MemoryLimit { get; set; }

    /// <summary>Gets or sets the number of CPUs the container may use.</summary>
    public double? CpuLimit { get; set; }

    /// <summary>Gets or sets a value indicating whether the container's root filesystem is read-only.</summary>
    public bool ReadOnlyRootFilesystem { get; set; }
}
