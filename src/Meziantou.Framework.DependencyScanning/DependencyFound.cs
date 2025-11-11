namespace Meziantou.Framework.DependencyScanning;

/// <summary>Represents the callback method that is invoked when a dependency is found during scanning.</summary>
/// <param name="dependency">The dependency that was found.</param>
public delegate void DependencyFound(Dependency dependency);
