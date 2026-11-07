namespace Meziantou.Framework.DependencyScanning;

/// <summary>Represents the callback method that is invoked when a dependency is found during scanning.</summary>
/// <remarks>
/// A directory scan or a multi-file scan scans several files concurrently, unless <see cref="ScannerOptions.DegreeOfParallelism"/>
/// is 1, so the callback can be invoked concurrently from several threads and must be thread-safe. For instance, add the
/// dependencies to a <see cref="System.Collections.Concurrent.ConcurrentQueue{T}"/> rather than to a <see cref="List{T}"/>.
/// An exception thrown by the callback stops the scan, and the scan method reports it.
/// </remarks>
/// <param name="dependency">The dependency that was found.</param>
public delegate void DependencyFound(Dependency dependency);
