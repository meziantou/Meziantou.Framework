using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Resource limits for a container.</summary>
public sealed class ContainerResourceOptions
{
    private bool _isReadOnly;

    private readonly FreezableList<ContainerUlimit> _ulimits;

    internal ContainerResourceOptions()
    {
        _ulimits = [];
    }

    internal ContainerResourceOptions(ContainerResourceOptions other)
    {
        MemoryLimit = other.MemoryLimit;
        CpuLimit = other.CpuLimit;
        ReadOnlyRootFilesystem = other.ReadOnlyRootFilesystem;
        SharedMemorySize = other.SharedMemorySize;
        _ulimits = new FreezableList<ContainerUlimit>(other._ulimits);
    }

    /// <summary>Gets or sets the memory limit in bytes.</summary>
    public long? MemoryLimit
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    /// <summary>Gets or sets the number of CPUs the container may use.</summary>
    public double? CpuLimit
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether the container's root filesystem is read-only.</summary>
    public bool ReadOnlyRootFilesystem
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    /// <summary>Gets or sets the size of <c>/dev/shm</c>, in bytes. <see langword="null"/> keeps the default of the runtime (64 MB for docker), which is too small for browsers and some databases.</summary>
    public long? SharedMemorySize
    {
        get;
        set
        {
            if (value is { } size)
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size, nameof(value));

            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    /// <summary>Gets the resource limits (<c>ulimit</c>) of the processes of the container.</summary>
    public IList<ContainerUlimit> Ulimits => _ulimits;

    internal void MakeReadOnly()
    {
        _isReadOnly = true;
        _ulimits.MakeReadOnly();
    }
}
