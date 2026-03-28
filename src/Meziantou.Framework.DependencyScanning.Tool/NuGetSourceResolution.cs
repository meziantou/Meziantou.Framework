namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed record NuGetSourceResolution(IReadOnlyList<string> PackageSources, IReadOnlyList<string> AllConfiguredSources, bool HasSourceMappings);
