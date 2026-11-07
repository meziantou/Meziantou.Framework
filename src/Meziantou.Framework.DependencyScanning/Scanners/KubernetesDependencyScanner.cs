using Meziantou.Framework.Yaml.Model;
using static Meziantou.Framework.DependencyScanning.Internals.YamlParserUtilities;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>
/// Scans Kubernetes manifests for the images of the containers, init containers and ephemeral containers of their pod specs.
/// Pods, pod templates, Deployments, StatefulSets, DaemonSets, ReplicaSets, ReplicationControllers, Jobs and CronJobs are supported,
/// as well as <c>List</c> objects and files that contain several documents.
/// Files that contain <c>{{</c>, such as Helm chart templates, are not scanned, as their content is a template rather than a manifest.
/// </summary>
public sealed class KubernetesDependencyScanner : DependencyScanner
{
    private static readonly string[] Extensions = [".yml", ".yaml"];
    private static readonly string[] ContainerProperties = ["containers", "initContainers", "ephemeralContainers"];

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.DockerImage];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        if (!context.HasExtension(Extensions, ignoreCase: true))
            return false;

        // Files owned by other scanners are not Kubernetes manifests
        if (context.RelativeDirectory is ".github/workflows" or ".github\\workflows")
            return false;

        return !DockerComposeDependencyScanner.IsComposeFile(context);
    }

    public override ValueTask ScanAsync(ScanFileContext context)
    {
        // Every YAML file is a candidate, so the text is checked before it is parsed
        var yaml = LoadYamlFile(context, text =>
            text.Contains("apiVersion", StringComparison.Ordinal) &&
            text.Contains("kind", StringComparison.Ordinal) &&
            text.Contains("containers", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("{{", StringComparison.Ordinal));
        if (yaml is null)
            return ValueTask.CompletedTask;

        foreach (var document in yaml.Stream)
        {
            if (GetKind(document.Contents) is "List")
            {
                if (GetProperty(document.Contents, "items", StringComparison.Ordinal) is YamlSequence items)
                {
                    foreach (var item in items)
                    {
                        ScanObject(yaml, item);
                    }
                }
            }
            else
            {
                ScanObject(yaml, document.Contents);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static string? GetKind(YamlElement? node)
    {
        if (node is not YamlMapping || GetScalarValue(GetProperty(node, "apiVersion", StringComparison.Ordinal)) is null)
            return null;

        return GetScalarValue(GetProperty(node, "kind", StringComparison.Ordinal));
    }

    private void ScanObject(YamlFile yaml, YamlElement? node)
    {
        // https://kubernetes.io/docs/concepts/workloads/
        var podSpec = GetKind(node) switch
        {
            "Pod" => GetProperty(node, "spec", StringComparison.Ordinal),
            "PodTemplate" => GetPodTemplateSpec(node),
            "Deployment" or "StatefulSet" or "DaemonSet" or "ReplicaSet" or "ReplicationController" or "Job" => GetPodTemplateSpec(GetProperty(node, "spec", StringComparison.Ordinal)),
            "CronJob" => GetPodTemplateSpec(GetProperty(GetProperty(GetProperty(node, "spec", StringComparison.Ordinal), "jobTemplate", StringComparison.Ordinal), "spec", StringComparison.Ordinal)),
            _ => null,
        };

        foreach (var propertyName in ContainerProperties)
        {
            if (GetProperty(podSpec, propertyName, StringComparison.Ordinal) is YamlSequence containers)
            {
                foreach (var container in containers)
                {
                    yaml.ReportDockerImage(this, GetProperty(container, "image", StringComparison.Ordinal));
                }
            }
        }

        static YamlElement? GetPodTemplateSpec(YamlElement? node) => GetProperty(GetProperty(node, "template", StringComparison.Ordinal), "spec", StringComparison.Ordinal);
    }
}
