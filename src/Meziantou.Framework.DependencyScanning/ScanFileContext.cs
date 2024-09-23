using System.Runtime.InteropServices;

namespace Meziantou.Framework.DependencyScanning;

[StructLayout(LayoutKind.Auto)]
public readonly struct ScanFileContext : IAsyncDisposable
{
    private readonly Lazy<Stream> _content;
    private readonly DependencyFound _onDependencyFound;

    internal ScanFileContext(string fullPath, DependencyFound onDependencyFound, IFileSystem fileSystem, CancellationToken cancellationToken)
    {
        FullPath = fullPath;
        _onDependencyFound = onDependencyFound;
        FileSystem = fileSystem;
        CancellationToken = cancellationToken;

        _content = new Lazy<Stream>(() => fileSystem.OpenRead(fullPath));
    }

    public string FullPath { get; }
    public Stream Content => _content.Value;
    public CancellationToken CancellationToken { get; }
    public IFileSystem FileSystem { get; }

    public void ReportDependency(Dependency dependency)
    {
        _onDependencyFound(dependency);
    }

    public void ReportDependency<TScanner>(string? name, string? version, DependencyType type, Location? nameLocation, Location? versionLocation)
        where TScanner : DependencyScanner
    {
        ReportDependency<TScanner>(name, version, type, nameLocation, versionLocation, []);
    }

    public void ReportDependency<TScanner>(string? name, string? version, DependencyType type, Location? nameLocation, Location? versionLocation, ReadOnlySpan<string> tags)
        where TScanner : DependencyScanner
    {
        var dep = new Dependency(name, version, type, nameLocation, versionLocation);
        dep.Tags.Add(typeof(TScanner).FullName);
        foreach (var tag in tags)
        {
            dep.Tags.Add(tag);
        }

        ReportDependency(dep);
    }

    internal void ResetStream()
    {
        if (_content.IsValueCreated)
        {
            _content.Value.Seek(0, SeekOrigin.Begin);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_content.IsValueCreated)
        {
            return _content.Value.DisposeAsync();
        }

        return default;
    }
}
