namespace Meziantou.Framework.DependencyScanning;

/// <summary>
/// Specifies the type of a dependency.
/// </summary>
public enum DependencyType
{
    /// <summary>
    /// An unknown dependency type.
    /// </summary>
    Unknown,

    /// <summary>
    /// A NuGet package dependency.
    /// </summary>
    NuGet,

    /// <summary>
    /// An NPM package dependency.
    /// </summary>
    Npm,

    /// <summary>
    /// A Python PyPI package dependency.
    /// </summary>
    PyPi,

    /// <summary>
    /// A Docker image dependency.
    /// </summary>
    DockerImage,

    /// <summary>
    /// A Git reference (commit, tag, branch) dependency.
    /// </summary>
    GitReference,

    /// <summary>
    /// A .NET SDK dependency.
    /// </summary>
    DotNetSdk,

    /// <summary>
    /// A .NET target framework dependency.
    /// </summary>
    DotNetTargetFramework,

    /// <summary>
    /// A GitHub Actions dependency.
    /// </summary>
    GitHubActions,

    /// <summary>
    /// An Azure DevOps VM pool dependency.
    /// </summary>
    AzureDevOpsVMPool,

    /// <summary>
    /// An Azure DevOps task dependency.
    /// </summary>
    AzureDevOpsTask,

    /// <summary>
    /// An Azure DevOps template dependency.
    /// </summary>
    AzureDevOpsTemplate,

    /// <summary>
    /// A Helm chart dependency.
    /// </summary>
    HelmChart,

    /// <summary>
    /// A Ruby gem dependency.
    /// </summary>
    RubyGem,

    /// <summary>
    /// A Renovate configuration dependency.
    /// </summary>
    RenovateConfiguration,

    /// <summary>
    /// An MSBuild project reference dependency.
    /// </summary>
    MSBuildProjectReference,
}
