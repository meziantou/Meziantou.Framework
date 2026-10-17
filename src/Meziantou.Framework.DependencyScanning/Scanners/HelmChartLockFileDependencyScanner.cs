using Meziantou.Framework.DependencyScanning.Locations;
using Meziantou.Framework.Yaml.Model;
using static Meziantou.Framework.DependencyScanning.Internals.YamlParserUtilities;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>
/// Scans Helm Chart.lock files, and the requirements.lock files of apiVersion v1 charts, for chart dependencies.
/// The entries are not updatable, as the lock files record a digest of the dependencies. The dependency name is the chart <c>name</c>;
/// the <c>repository</c> and the <c>alias</c> are reported in <see cref="Dependency.Metadata"/> under the <c>repository</c> and <c>alias</c> keys.
/// </summary>
public sealed class HelmChartLockFileDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.HelmChart];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("Chart.lock", ignoreCase: true)
            || context.HasFileName("requirements.lock", ignoreCase: true);
    }

    public override ValueTask ScanAsync(ScanFileContext context)
    {
        var yaml = LoadYamlFile(context);
        if (yaml is null)
            return ValueTask.CompletedTask;

        foreach (var document in yaml.Stream)
        {
            if (document.Contents is not YamlMapping rootNode)
                continue;

            var dependenciesNode = GetProperty(rootNode, "dependencies", StringComparison.Ordinal);
            if (dependenciesNode is YamlSequence dependencies)
            {
                foreach (var dependency in dependencies)
                {
                    var nameNode = GetProperty(dependency, "name", StringComparison.Ordinal);
                    var name = GetScalarValue(nameNode);
                    if (string.IsNullOrEmpty(name) || !yaml.TryMarkAsReported(nameNode))
                        continue;

                    var versionNode = GetProperty(dependency, "version", StringComparison.Ordinal);
                    var repository = GetScalarValue(GetProperty(dependency, "repository", StringComparison.Ordinal));
                    var alias = GetScalarValue(GetProperty(dependency, "alias", StringComparison.Ordinal));
                    context.ReportDependency(
                        this,
                        name,
                        GetScalarValue(versionNode),
                        DependencyType.HelmChart,
                        nameLocation: new NonUpdatableLocation(context),
                        versionLocation: versionNode is YamlValue ? new NonUpdatableLocation(context) : null,
                        tags: [],
                        metadata: [
                            KeyValuePair.Create<string, object?>("repository", repository),
                            KeyValuePair.Create<string, object?>("alias", alias),
                        ]);
                }
            }
        }

        return ValueTask.CompletedTask;
    }
}
