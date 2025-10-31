namespace Meziantou.Framework.DependencyScanning;

/// <summary>
/// Represents a dependency discovered during scanning, including its name, version, type, and location information.
/// <example>
/// <code>
/// var dependencies = await DependencyScanner.ScanDirectoryAsync("C:\\MyProject", null, cancellationToken);
/// foreach (var dependency in dependencies)
/// {
///     Console.WriteLine($"{dependency.Type}: {dependency.Name}@{dependency.Version}");
///     if (dependency.VersionLocation?.IsUpdatable == true)
///     {
///         await dependency.UpdateVersionAsync("2.0.0", cancellationToken);
///     }
/// }
/// </code>
/// </example>
/// </summary>
public sealed class Dependency
{
    public Dependency(string? name, string? version, DependencyType type, Location? nameLocation, Location? versionLocation)
    {
        Name = name;
        Version = version;
        Type = type;
        NameLocation = nameLocation;
        VersionLocation = versionLocation;
    }

    /// <summary>Gets the name of the dependency.</summary>
    public string? Name { get; }

    /// <summary>Gets the version of the dependency.</summary>
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
    /// <param name="newValue">The new name value to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the name location is not updatable.</exception>
    public Task UpdateNameAsync(string newValue, CancellationToken cancellationToken = default)
    {
        if (NameLocation is null)
            throw new InvalidOperationException("Name is not updatable");

        return NameLocation.UpdateAsync(Name, newValue, cancellationToken);
    }

    /// <summary>Updates the dependency version in the source file.</summary>
    /// <param name="newValue">The new version value to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the version location is not updatable.</exception>
    public Task UpdateVersionAsync(string newValue, CancellationToken cancellationToken = default)
    {
        if (VersionLocation is null)
            throw new InvalidOperationException("Version is not updatable");

        return VersionLocation.UpdateAsync(Version, newValue, cancellationToken);
    }

    public override string ToString()
    {
        return $"{Type}:{Name}@{Version}:{VersionLocation}";
    }
}
