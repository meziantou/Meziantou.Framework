namespace Meziantou.Framework.DependencyScanning;

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

    public string? Name { get; }
    public string? Version { get; }
    public DependencyType Type { get; }
    public Location? NameLocation { get; }
    public Location? VersionLocation { get; }
    public ISet<string> Tags { get; } = new HashSet<string>(StringComparer.Ordinal);
    public IDictionary<string, object?> Metadata { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    public Task UpdateNameAsync(string newValue, CancellationToken cancellationToken = default)
    {
        if (NameLocation is null)
            throw new InvalidOperationException("Name is not updatable");

        return NameLocation.UpdateAsync(Name, newValue, cancellationToken);
    }

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
