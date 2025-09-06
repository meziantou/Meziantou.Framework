namespace Meziantou.Framework.DependencyScanning;

public enum DependencyType
{
    Unknown,
    NuGet,
    Npm,
    PyPi,
    DockerImage,
    GitReference,
    DotNetSdk,
    DotNetTargetFramework,
    GitHubActions,
    AzureDevOpsVMPool,
    AzureDevOpsTask,
    AzureDevOpsTemplate,
    HelmChart,
    RubyGem,
    RenovateConfiguration,
    MSBuildProjectReference,
}
