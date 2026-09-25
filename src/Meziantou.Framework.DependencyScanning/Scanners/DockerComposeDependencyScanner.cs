using Meziantou.Framework.Yaml.Model;
using static Meziantou.Framework.DependencyScanning.Internals.YamlParserUtilities;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>
/// Scans Docker Compose files (<c>compose.yaml</c>, <c>docker-compose.yml</c>, <c>compose.override.yaml</c>, ...) for the images of their services.
/// The image of a service that has a <c>build</c> section is the name given to the image built from source, so it is not reported.
/// </summary>
public sealed class DockerComposeDependencyScanner : DependencyScanner
{
    private static readonly string[] Extensions = [".yml", ".yaml"];

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.DockerImage];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        // https://docs.docker.com/compose/intro/compose-application-model/#the-compose-file
        return IsComposeFile(context);
    }

    internal static bool IsComposeFile(CandidateFileContext context)
    {
        return context.HasExtension(Extensions, ignoreCase: true)
            && (context.FileName.StartsWith("compose.", StringComparison.OrdinalIgnoreCase) || context.FileName.StartsWith("docker-compose.", StringComparison.OrdinalIgnoreCase));
    }

    public override ValueTask ScanAsync(ScanFileContext context)
    {
        var yaml = LoadYamlFile(context);
        if (yaml is null)
            return ValueTask.CompletedTask;

        foreach (var document in yaml.Stream)
        {
            // https://docs.docker.com/reference/compose-file/services/#image
            if (GetProperty(document.Contents, "services", StringComparison.Ordinal) is not YamlMapping services)
                continue;

            foreach (var service in services)
            {
                if (service.Value is not YamlMapping serviceNode || GetProperty(serviceNode, "build", StringComparison.Ordinal) is not null)
                    continue;

                yaml.ReportDockerImage(this, GetProperty(serviceNode, "image", StringComparison.Ordinal));
            }
        }

        return ValueTask.CompletedTask;
    }
}
