using NuGet.Configuration;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed record NuGetSourceResolution(IReadOnlyList<PackageSource> PackageSources, IReadOnlyList<PackageSource> AllConfiguredSources, bool HasSourceMappings);
