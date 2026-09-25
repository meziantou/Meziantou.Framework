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

    /// <summary>Swift package dependency from Swift Package Manager.</summary>
    SwiftPackage,

    /// <summary>MSBuild project reference.</summary>
    MSBuildProjectReference,

    /// <summary>.NET assembly file reference.</summary>
    DotNetAssemblyReference,

    /// <summary>Claude Code or GitHub Copilot plugin referenced by a plugin marketplace.</summary>
    AgentPlugin,

    /// <summary>Claude Code or GitHub Copilot plugin marketplace.</summary>
    AgentPluginMarketplace,

    /// <summary>Rust crate package from crates.io or another Cargo source.</summary>
    RustCrate,

    /// <summary>Go module dependency.</summary>
    GoModule,

    /// <summary>Java package dependency from Maven or Gradle.</summary>
    JavaPackage,

    /// <summary>PHP package dependency from Composer.</summary>
    PhpPackage,
}
