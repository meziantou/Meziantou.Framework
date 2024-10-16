namespace Meziantou.Framework.DependencyScanning;

public enum DependencyType
{
    Unknown,
    NuGet,
    Npm,
    PyPi,
    DockerImage,
    GitReference,
    GitSubmodule,
    DotNetSdk,
    DotNetTargetFramework,
    GitHubActions,
    AzureDevOpsVMPool,
    AzureDevOpsTask,
    AzureDevOpsTemplate,
    HelmChart,
    RubyGem,
    RenovateConfiguration,
}
