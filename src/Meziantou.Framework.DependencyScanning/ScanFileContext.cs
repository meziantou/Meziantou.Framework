using System.Runtime.InteropServices;

namespace Meziantou.Framework.DependencyScanning;

[StructLayout(LayoutKind.Auto)]
public readonly struct ScanFileContext : IAsyncDisposable
{
    private readonly Lazy<Stream> _content;
    private readonly DependencyFound _onDependencyFound;
    private readonly ScannerOptions _options;

    internal ScanFileContext(string fullPath, DependencyFound onDependencyFound, ScannerOptions options, CancellationToken cancellationToken)
    {
        FullPath = fullPath;
        _onDependencyFound = onDependencyFound;
        _options = options;
        FileSystem = options.FileSystem;
        CancellationToken = cancellationToken;

        _content = new Lazy<Stream>(() => options.FileSystem.OpenRead(fullPath));
    }

    public string FullPath { get; }
    public Stream Content => _content.Value;
    public CancellationToken CancellationToken { get; }
    public IFileSystem FileSystem { get; }

    private void UnsafeReportDependency(Dependency dependency)
    {
        _onDependencyFound(dependency);
    }

    public void ReportDependency(DependencyScanner scanner, string? name, string? version, DependencyType type, Location? nameLocation, Location? versionLocation)
    {
        ReportDependency(scanner, name, version, type, nameLocation, versionLocation, [], []);
    }

    public void ReportDependency(DependencyScanner scanner, string? name, string? version, DependencyType type, Location? nameLocation, Location? versionLocation, ReadOnlySpan<string> tags, ReadOnlySpan<KeyValuePair<string, object?>> metadata)
    {
        if (!ShouldReportDependency(scanner, type))
            return;

        var dep = new Dependency(name, version, type, nameLocation, versionLocation);
        dep.Tags.Add(scanner.GetType().FullName);
        foreach (var tag in tags)
        {
            dep.Tags.Add(tag);
        }

        foreach (var (key, value) in metadata)
        {
            dep.Metadata[key] = value;
        }

        UnsafeReportDependency(dep);
    }

    private bool ShouldReportDependency(DependencyScanner scanner, DependencyType type)
    {
        if (scanner.SupportedDependencyTypes is null || !scanner.SupportedDependencyTypes.Contains(type))
            throw new InvalidOperationException($"The scanner '{scanner.GetType().FullName}' does not support dependencies of type '{type}'. Supported types are: {string.Join(", ", scanner.SupportedDependencyTypes ?? [])}");

        if (_options.IncludedDependencyTypes.Count > 0 && !_options.IncludedDependencyTypes.Contains(type))
            return false;

        if (_options.ExcludedDependencyTypes.Contains(type))
            return false;

        return true;
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
