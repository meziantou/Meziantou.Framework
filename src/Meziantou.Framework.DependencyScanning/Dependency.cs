namespace Meziantou.Framework.DependencyScanning;

/// <summary>Represents a dependency discovered during scanning, including its name, version, type, and location information.</summary>
public sealed class Dependency
{
    /// <summary>Initializes a new instance of the <see cref="Dependency"/> class.</summary>
    /// <param name="name">The name of the dependency.</param>
    /// <param name="version">The version of the dependency.</param>
    /// <param name="type">The type of the dependency.</param>
    /// <param name="nameLocation">The location of the name. Unless it already knows the value it holds, it records <paramref name="name"/> as the value an update expects to find.</param>
    /// <param name="versionLocation">The location of the version. Unless it already knows the value it holds, it records <paramref name="version"/> as the value an update expects to find.</param>
    public Dependency(string? name, string? version, DependencyType type, Location? nameLocation, Location? versionLocation)
    {
        Name = name;
        Version = version;
        Type = type;
        NameLocation = nameLocation;
        VersionLocation = versionLocation;

        nameLocation?.InitializeCurrentValue(name);
        versionLocation?.InitializeCurrentValue(version);
        if (nameLocation is not null && versionLocation is not null)
        {
            nameLocation.LinkSibling(versionLocation);
            versionLocation.LinkSibling(nameLocation);
        }
    }

    /// <summary>Gets the name of the dependency, as found by the scan. It does not change when the name is updated.</summary>
    public string? Name { get; }

    /// <summary>Gets the version of the dependency, as found by the scan. It does not change when the version is updated.</summary>
    public string? Version { get; }

    /// <summary>Gets the type of the dependency.</summary>
    public DependencyType Type { get; }

    /// <summary>Gets the location of the dependency name in the source file, or <see langword="null"/> if not available.</summary>
    public Location? NameLocation { get; }

    /// <summary>Gets the location of the dependency version in the source file, or <see langword="null"/> if not available.</summary>
    public Location? VersionLocation { get; }

    /// <summary>Gets a collection of tags associated with this dependency, such as the scanner type that discovered it.</summary>
    public ISet<string> Tags { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Gets a dictionary of metadata associated with this dependency for storing additional scanner-specific information.</summary>
    public IDictionary<string, object?> Metadata { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>Updates the dependency name in the source file.</summary>
    /// <remarks>
    /// The file must still hold the value the location holds: the scanned name, or the name written by the previous
    /// update. So the name can be updated several times, and the name and the version can be updated in any order.
    /// </remarks>
    /// <param name="newValue">The new name value to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the name location is not updatable.</exception>
    /// <exception cref="DependencyScannerException">The file does not hold the expected name at the location anymore.</exception>
    public Task UpdateNameAsync(string newValue, CancellationToken cancellationToken = default)
    {
        if (NameLocation is null)
            throw new InvalidOperationException("Name is not updatable");

        return NameLocation.UpdateAsync(newValue, cancellationToken);
    }

    /// <summary>Updates the dependency version in the source file.</summary>
    /// <remarks>
    /// The file must still hold the value the location holds: the scanned version, or the version written by the
    /// previous update. So the version can be updated several times, and the name and the version can be updated in any order.
    /// </remarks>
    /// <param name="newValue">The new version value to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the version location is not updatable.</exception>
    /// <exception cref="DependencyScannerException">The file does not hold the expected version at the location anymore.</exception>
    public Task UpdateVersionAsync(string newValue, CancellationToken cancellationToken = default)
    {
        if (VersionLocation is null)
            throw new InvalidOperationException("Version is not updatable");

        return VersionLocation.UpdateAsync(newValue, cancellationToken);
    }

    public override string ToString()
    {
        return $"{Type}:{Name}@{Version}:{VersionLocation}";
    }
}
