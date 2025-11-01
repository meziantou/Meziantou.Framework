namespace Meziantou.Framework.DependencyScanning;

/// <summary>Specifies the type of dependency detected during scanning.</summary>
public enum DependencyType
{
    /// <summary>Dependency type is unknown or not categorized.</summary>
    Unknown,

    /// <summary>.NET package from NuGet.org.</summary>
    NuGet,

    /// <summary>JavaScript package from npmjs.com.</summary>
    Npm,

    /// <summary>Python package from PyPI.</summary>
    PyPi,

    /// <summary>Docker container image.</summary>
    DockerImage,

    /// <summary>Git submodule or reference.</summary>
    GitReference,

    /// <summary>.NET SDK version.</summary>
    DotNetSdk,

    /// <summary>.NET target framework.</summary>
    DotNetTargetFramework,

    /// <summary>GitHub Actions workflow or reusable workflow.</summary>
    GitHubActions,

    /// <summary>Azure DevOps VM pool image.</summary>
    AzureDevOpsVMPool,

    /// <summary>Azure DevOps pipeline task.</summary>
    AzureDevOpsTask,

    /// <summary>Azure DevOps pipeline template.</summary>
    AzureDevOpsTemplate,

    /// <summary>Helm chart dependency.</summary>
    HelmChart,

    /// <summary>Ruby gem package.</summary>
    RubyGem,

    /// <summary>Renovate configuration extends reference.</summary>
    RenovateConfiguration,

    /// <summary>MSBuild project reference.</summary>
    MSBuildProjectReference,
}
