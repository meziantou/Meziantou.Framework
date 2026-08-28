# Meziantou.Framework.DependencyScanning.Tool

`Meziantou.Framework.DependencyScanning.Tool` is a .NET tool to update dependencies detected by `Meziantou.Framework.DependencyScanning`.

# How to use it

1. Install the tool

    ````bash
    dotnet tool update Meziantou.Framework.DependencyScanning.Tool --global
    ````

2. Run the tool

    ````bash
    Meziantou.Framework.DependencyScanning.Tool update --directory .
    ````

> [!WARNING]
> `--update-lock-files` runs `dotnet restore` and `npm install` on the scanned repository. Both execute
> code from that repository: `dotnet restore` evaluates MSBuild targets in the projects it restores, and
> `npm install` runs the `preinstall`/`postinstall` scripts of the resolved dependency tree. Only use this
> option on repositories you trust.

You can show available options using:

````bash
Meziantou.Framework.DependencyScanning.Tool --help
````

<!-- help -->
## Help

```
Description:
  List and update dependencies detected in a folder.

Usage:
  Meziantou.Framework.DependencyScanning.Tool [command] [options]

Options:
  -?, -h, --help  Show help and usage information
  --version       Show version information

Commands:
  update  Update dependencies
  list    List dependencies
```

### list

```
Description:
  List dependencies

Usage:
  Meziantou.Framework.DependencyScanning.Tool list [options]

Options:
  --directory <directory>              Root directory
  --files <files>                      Glob patterns to find files to scan
  --dependency-type <dependency-type>  Dependency types to include. Available values: Unknown, NuGet, Npm, PyPi, DockerImage, GitReference, DotNetSdk, DotNetTargetFramework, GitHubActions, AzureDevOpsVMPool, AzureDevOpsTask, AzureDevOpsTemplate, HelmChart, RubyGem, RenovateConfiguration, SwiftPackage, MSBuildProjectReference, DotNetAssemblyReference
  --upgradable                         Only list dependencies that can be upgraded
  --minimum-age <minimum-age>          Minimum age in days for package versions to consider for update (default: 7). Use 0 or negative to disable filtering. Versions whose publication date is unknown are skipped, except for Docker images as registries don't expose publication dates at all. [default: 7]
  --format <Json|Text>                 Output format. Available values: Text, Json
  -?, -h, --help                       Show help and usage information
```

### update

```
Description:
  Update dependencies

Usage:
  Meziantou.Framework.DependencyScanning.Tool update [options]

Options:
  --directory <directory>              Root directory
  --files <files>                      Glob patterns to find files to scan
  --dependency-type <dependency-type>  Dependency types to include. Available values: Unknown, NuGet, Npm, PyPi, DockerImage, GitReference, DotNetSdk, DotNetTargetFramework, GitHubActions, AzureDevOpsVMPool, AzureDevOpsTask, AzureDevOpsTemplate, HelmChart, RubyGem, RenovateConfiguration, SwiftPackage, MSBuildProjectReference, DotNetAssemblyReference
  --update-lock-files                  Update lock files when dependencies are updated. Runs 'dotnet restore' and 'npm install', which execute MSBuild targets and npm install scripts from the scanned repository. Only use it on repositories you trust.
  --minimum-age <minimum-age>          Minimum age in days for package versions to consider for update (default: 7). Use 0 or negative to disable filtering. Versions whose publication date is unknown are skipped, except for Docker images as registries don't expose publication dates at all. [default: 7]
  -?, -h, --help                       Show help and usage information
```
<!-- help -->