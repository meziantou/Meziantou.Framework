using System.Runtime.InteropServices;

namespace Meziantou.Framework.DependencyScanning;

/// <summary>Provides context information and methods for scanning a file and reporting discovered dependencies.</summary>
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

    /// <summary>Gets the full path of the file being scanned.</summary>
    public string FullPath { get; }

    /// <summary>Gets a stream containing the file content. The stream is lazily opened on first access.</summary>
    public Stream Content => _content.Value;

    /// <summary>Gets the cancellation token for the scanning operation.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Gets the file system implementation for accessing files.</summary>
    public IFileSystem FileSystem { get; }

    private void UnsafeReportDependency(Dependency dependency)
    {
        _onDependencyFound(dependency);
    }

    /// <summary>Reports a discovered dependency to the scanning framework.</summary>
    /// <param name="scanner">The scanner that discovered the dependency.</param>
    /// <param name="name">The name of the dependency.</param>
    /// <param name="version">The version of the dependency.</param>
    /// <param name="type">The type of the dependency.</param>
    /// <param name="nameLocation">The location of the dependency name in the source file.</param>
    /// <param name="versionLocation">The location of the dependency version in the source file.</param>
    public void ReportDependency(DependencyScanner scanner, string? name, string? version, DependencyType type, Location? nameLocation, Location? versionLocation)
    {
        ReportDependency(scanner, name, version, type, nameLocation, versionLocation, [], []);
    }

    /// <summary>Reports a discovered dependency with additional tags and metadata to the scanning framework.</summary>
    /// <param name="scanner">The scanner that discovered the dependency.</param>
    /// <param name="name">The name of the dependency.</param>
    /// <param name="version">The version of the dependency.</param>
    /// <param name="type">The type of the dependency.</param>
    /// <param name="nameLocation">The location of the dependency name in the source file.</param>
    /// <param name="versionLocation">The location of the dependency version in the source file.</param>
    /// <param name="tags">Additional tags to associate with the dependency.</param>
    /// <param name="metadata">Additional metadata to associate with the dependency.</param>
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
