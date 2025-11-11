# Meziantou.Framework.DependencyScanning

A .NET library for scanning source code directories and files to discover and manage project dependencies across multiple package ecosystems and configuration formats.

## Features

- **Multi-format Support**: Scan dependencies from various project files and configuration formats
- **Multiple Package Ecosystems**: NuGet, npm, PyPI, Docker, Ruby Gems, Helm Charts, and more
- **Parallel Scanning**: High-performance parallel file scanning with configurable degree of parallelism
- **Dependency Updates**: Locate and update dependency versions programmatically
- **Customizable Scanning**: Filter by file patterns, dependency types, and custom predicates

## Supported Dependency Types

The library can detect the following dependency types:

- **NuGet** - .NET packages from NuGet.org
- **Npm** - JavaScript packages from npmjs.com
- **PyPi** - Python packages from PyPI
- **DockerImage** - Docker container images
- **GitReference** - Git submodules and references
- **DotNetSdk** - .NET SDK versions
- **DotNetTargetFramework** - .NET target frameworks
- **GitHubActions** - GitHub Actions workflows and reusable workflows
- **AzureDevOpsVMPool** - Azure DevOps VM pool images
- **AzureDevOpsTask** - Azure DevOps pipeline tasks
- **AzureDevOpsTemplate** - Azure DevOps pipeline templates
- **HelmChart** - Helm chart dependencies
- **RubyGem** - Ruby gems
- **RenovateConfiguration** - Renovate configuration extends
- **MSBuildProjectReference** - MSBuild project references

## Usage

### Scan a Directory

```csharp
using Meziantou.Framework.DependencyScanning;

// Scan with default options
var dependencies = await DependencyScanner.ScanDirectoryAsync(
    "C:\\MyProject",
    options: null,
    cancellationToken);

foreach (var dependency in dependencies)
{
    Console.WriteLine($"{dependency.Type}: {dependency.Name}@{dependency.Version}");
}
```

### Scan with Custom Options

```csharp
var options = new ScannerOptions
{
    // Number of parallel scanning tasks (default: 16)
    DegreeOfParallelism = 8,

    // Recurse into subdirectories (default: true)
    RecurseSubdirectories = true,

    // Filter files to scan
    ShouldScanFilePredicate = (directory, fileName) =>
    {
        return !fileName.StartsWith(".");
    },

    // Filter directories to recurse into
    ShouldRecursePredicate = (directory, name) =>
    {
        return name != "node_modules" && name != "bin";
    }
};

var dependencies = await DependencyScanner.ScanDirectoryAsync(
    @"C:\MyProject",
    options,
    cancellationToken);
```

### Filter by Dependency Type

```csharp
var options = new ScannerOptions
{
    // Only scan for specific dependency types
    IncludedDependencyTypes = [DependencyType.NuGet, DependencyType.Npm].ToImmutableHashSet(),
};

// Or exclude specific types
var options2 = new ScannerOptions
{
    ExcludedDependencyTypes = [DependencyType.DockerImage].ToImmutableHashSet(),
};
```

### Stream Dependencies as They're Found

```csharp
await DependencyScanner.ScanDirectoryAsync(
    "C:\\MyProject",
    options: null,
    onDependencyFound: dependency =>
    {
        Console.WriteLine($"Found: {dependency.Name}@{dependency.Version}");
    },
    cancellationToken);
```

### Scan Individual Files

```csharp
// Scan a single file
var dependencies = await DependencyScanner.ScanFileAsync(
    rootDirectory: "C:\\MyProject",
    filePath: "C:\\MyProject\\package.json",
    options: null,
    cancellationToken);

// Scan multiple specific files
var filePaths = new[]
{
    "C:\\MyProject\\package.json",
    "C:\\MyProject\\MyProject.csproj"
};

var dependencies = await DependencyScanner.ScanFilesAsync(
    rootDirectory: "C:\\MyProject",
    filePaths,
    options: null,
    cancellationToken);
```

### Update Dependency Versions

```csharp
var dependencies = await DependencyScanner.ScanDirectoryAsync(
    "C:\\MyProject",
    options: null,
    cancellationToken);

// Update all NuGet packages to version 2.0.0
foreach (var dependency in dependencies.Where(d => d.Type == DependencyType.NuGet))
{
    if (dependency.VersionLocation?.IsUpdatable == true)
    {
        await dependency.UpdateVersionAsync("2.0.0", cancellationToken);
    }
}
```

### Custom Regex Scanner

For custom file formats, you can use the `RegexScanner`:

```csharp
var options = new ScannerOptions
{
    Scanners =
    [
        new RegexScanner
        {
            FilePatterns = [Glob.Parse("**/*.custom", GlobOptions.IgnoreCase)],
            DependencyType = DependencyType.DockerImage,
            RegexPattern = @"image:\s*(?<name>[a-z/]+)(:(?<version>[0-9.]+))?"
        }
    ]
};

var dependencies = await DependencyScanner.ScanDirectoryAsync(
    "C:\\MyProject",
    options,
    cancellationToken);
```

The regex pattern must include named groups:
- `name` - The dependency name (required)
- `version` - The dependency version (optional)
