using Meziantou.Framework.DependencyScanning;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal static class VersioningStrategies
{
    public static VersioningStrategy GetStrategy(DependencyType type)
    {
        return type switch
        {
            DependencyType.NuGet => NuGetVersioningStrategy.Instance,
            DependencyType.Npm => NpmVersioningStrategy.Instance,
            DependencyType.DockerImage => DockerVersioningStrategy.Instance,
            DependencyType.GitHubActions => GitHubActionsVersioningStrategy.Instance,
            DependencyType.DotNetSdk => SemanticVersioningStrategy.Strict,
            _ => SemanticVersioningStrategy.Strict,
        };
    }
}
