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
        : this(name, rawVersion: version, resolvedVersion: version, type, nameLocation, versionLocation)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Dependency"/> class for a version that may be written with variables or properties.</summary>
    /// <param name="name">The name of the dependency.</param>
    /// <param name="rawVersion">The version as written where the dependency is declared, such as <c>$(XunitVersion)</c>, or <see langword="null"/> when the declaration has no version text.</param>
    /// <param name="resolvedVersion">The version once its variables or properties are replaced with their values, such as <c>2.9.3</c>, or <see langword="null"/> when it cannot be resolved.</param>
    /// <param name="type">The type of the dependency.</param>
    /// <param name="nameLocation">The location of the name. Unless it already knows the value it holds, it records <paramref name="name"/> as the value an update expects to find.</param>
    /// <param name="versionLocation">
    /// The location of the version, which is the location of the definition of the property when the version comes from one.
    /// Unless it already knows the value it holds, it records <see cref="Version"/> as the value an update expects to find.
    /// </param>
    public Dependency(string? name, string? rawVersion, string? resolvedVersion, DependencyType type, Location? nameLocation, Location? versionLocation)
    {
        Name = name;
        RawVersion = rawVersion;
        ResolvedVersion = resolvedVersion;
        Version = resolvedVersion ?? rawVersion;
        Type = type;
        NameLocation = nameLocation;
        VersionLocation = versionLocation;

        nameLocation?.InitializeCurrentValue(name);
        versionLocation?.InitializeCurrentValue(Version);
        if (nameLocation is not null && versionLocation is not null)
        {
            nameLocation.LinkSibling(versionLocation);
            versionLocation.LinkSibling(nameLocation);
        }
    }

    /// <summary>Gets the name of the dependency, as found by the scan. It does not change when the name is updated.</summary>
    public string? Name { get; }

    /// <summary>Gets the version of the dependency, as found by the scan. It does not change when the version is updated.</summary>
    /// <remarks>
    /// It is <see cref="ResolvedVersion"/> when the version can be resolved, and <see cref="RawVersion"/> otherwise, such as
    /// <c>$(XunitVersion)</c> when the property is not defined in the file.
    /// </remarks>
    public string? Version { get; }

    /// <summary>Gets the version as written where the dependency is declared, such as <c>$(XunitVersion)</c> or <c>${junit.version}</c>.</summary>
    /// <remarks>
    /// It is the same as <see cref="Version"/> when the version is a literal. It is <see langword="null"/> when the
    /// declaration has no version text, such as a version catalog library that references a version with <c>version.ref</c>.
    /// It does not change when the version is updated.
    /// </remarks>
    public string? RawVersion { get; }

    /// <summary>Gets the version once its variables or properties are replaced with their values, such as <c>2.9.3</c> for <c>$(XunitVersion)</c>.</summary>
    /// <remarks>
    /// It is the same as <see cref="Version"/> when the version is a literal. It is <see langword="null"/> when the
    /// version references a variable or a property whose value is unknown, such as a property defined in another file.
    /// It does not change when the version is updated.
    /// </remarks>
    public string? ResolvedVersion { get; }

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
    /// <exception cref="ArgumentException">The file cannot hold <paramref name="newValue"/>, such as a line break in a line of text, or a control character in an XML file.</exception>
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
    /// <exception cref="ArgumentException">The file cannot hold <paramref name="newValue"/>, such as a line break in a line of text, or a control character in an XML file.</exception>
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
