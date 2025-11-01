namespace Meziantou.Framework.DependencyScanning;

/// <summary>
/// Represents a dependency found in a project or configuration file.
/// </summary>
public sealed class Dependency
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Dependency"/> class.
    /// </summary>
    /// <param name="name">The name of the dependency.</param>
    /// <param name="version">The version of the dependency.</param>
    /// <param name="type">The type of the dependency.</param>
    /// <param name="nameLocation">The location where the dependency name is defined.</param>
    /// <param name="versionLocation">The location where the dependency version is defined.</param>
    public Dependency(string? name, string? version, DependencyType type, Location? nameLocation, Location? versionLocation)
    {
        Name = name;
        Version = version;
        Type = type;
        NameLocation = nameLocation;
        VersionLocation = versionLocation;
    }

    /// <summary>
    /// Gets the name of the dependency.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Gets the version of the dependency.
    /// </summary>
    public string? Version { get; }

    /// <summary>
    /// Gets the type of the dependency.
    /// </summary>
    public DependencyType Type { get; }

    /// <summary>
    /// Gets the location where the dependency name is defined.
    /// </summary>
    public Location? NameLocation { get; }

    /// <summary>
    /// Gets the location where the dependency version is defined.
    /// </summary>
    public Location? VersionLocation { get; }

    /// <summary>
    /// Gets the collection of tags associated with this dependency.
    /// </summary>
    public ISet<string> Tags { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the metadata associated with this dependency.
    /// </summary>
    public IDictionary<string, object?> Metadata { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// Updates the dependency name in the source file.
    /// </summary>
    /// <param name="newValue">The new dependency name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous update operation.</returns>
    public Task UpdateNameAsync(string newValue, CancellationToken cancellationToken = default)
    {
        if (NameLocation is null)
            throw new InvalidOperationException("Name is not updatable");

        return NameLocation.UpdateAsync(Name, newValue, cancellationToken);
    }

    /// <summary>
    /// Updates the dependency version in the source file.
    /// </summary>
    /// <param name="newValue">The new dependency version.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous update operation.</returns>
    public Task UpdateVersionAsync(string newValue, CancellationToken cancellationToken = default)
    {
        if (VersionLocation is null)
            throw new InvalidOperationException("Version is not updatable");

        return VersionLocation.UpdateAsync(Version, newValue, cancellationToken);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{Type}:{Name}@{Version}:{VersionLocation}";
    }
}
