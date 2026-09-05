namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Describes how a volume should be created. Configure an instance and call <see cref="CreateVolume"/> to obtain a <see cref="TemporaryVolume"/>.</summary>
/// <example>
/// <code>
/// await using var volume = new VolumeDefinition().CreateVolume();
///
/// var definition = new ContainerDefinition(ImageSource.FromRegistry("redis:8"));
/// definition.Mounts.AddVolume(volume, "/data");
///
/// await using var container = definition.CreateContainer();
/// await container.StartAsync();
/// </code>
/// </example>
public class VolumeDefinition
{
    private ContainerRuntime _runtime = ContainerRuntime.Auto;

    /// <summary>Initializes a new instance of the <see cref="VolumeDefinition"/> class.</summary>
    public VolumeDefinition()
    {
        Labels = new ContainerLabelCollection();
        DriverOptions = new VolumeDriverOptionCollection();
    }

    /// <summary>Initializes a new instance of the <see cref="VolumeDefinition"/> class by deep-copying another definition.</summary>
    /// <param name="other">The definition to copy.</param>
    public VolumeDefinition(VolumeDefinition other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Runtime = other.Runtime;
        Name = other.Name;
        Driver = other.Driver;
        ReuseId = other.ReuseId;
        Labels = new ContainerLabelCollection(other.Labels);
        DriverOptions = new VolumeDriverOptionCollection(other.DriverOptions);
    }

    /// <summary>Gets or sets the container runtime to use.</summary>
    public ContainerRuntime Runtime
    {
        get => _runtime;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _runtime = value;
        }
    }

    /// <summary>Gets or sets the volume name. When <see langword="null"/>, a random name is generated, or a name derived from <see cref="ReuseId"/> when that is set.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the volume driver. When <see langword="null"/>, the runtime uses its default driver.</summary>
    public string? Driver { get; set; }

    /// <summary>Gets or sets an identifier used to reuse an existing volume across runs. When set, the volume is not removed on dispose.</summary>
    public string? ReuseId { get; set; }

    /// <summary>Gets the labels.</summary>
    public ContainerLabelCollection Labels { get; }

    /// <summary>Gets the driver-specific options.</summary>
    public VolumeDriverOptionCollection DriverOptions { get; }

    /// <summary>Creates a <see cref="TemporaryVolume"/> from a deep copy of this definition. Later changes to this definition do not affect the returned volume.</summary>
    /// <returns>A new volume.</returns>
    public virtual TemporaryVolume CreateVolume()
    {
        return new TemporaryVolume(new VolumeDefinition(this));
    }
}
