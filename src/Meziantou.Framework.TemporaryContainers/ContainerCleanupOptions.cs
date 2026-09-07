namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Configures what <see cref="ContainerRuntime.CleanupAsync(ContainerCleanupOptions, CancellationToken)"/> removes.</summary>
public sealed class ContainerCleanupOptions
{
    /// <summary>Gets or sets which resources are removed. Defaults to <see cref="ContainerCleanupScope.Orphaned"/>.</summary>
    public ContainerCleanupScope Scope { get; set; } = ContainerCleanupScope.Orphaned;

    /// <summary>Gets or sets the minimum age a resource must have to be removed. Defaults to <see cref="TimeSpan.Zero"/>, which removes a resource whatever its age.</summary>
    /// <remarks>A resource created by a version of the library that did not record a creation time is never removed when this is set.</remarks>
    public TimeSpan MinimumAge { get; set; }

    /// <summary>Gets or sets a value indicating whether containers are removed. Defaults to <see langword="true"/>.</summary>
    public bool IncludeContainers { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether volumes are removed. Defaults to <see langword="true"/>. Volumes are removed after the containers, so a volume a removed container used is no longer in use.</summary>
    public bool IncludeVolumes { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the resources created with a <see cref="ContainerDefinition.ReuseId"/> are removed. Defaults to <see langword="false"/>: such a resource outlives the run that created it on purpose, and another run may be using it right now.</summary>
    public bool IncludeReusedResources { get; set; }
}
