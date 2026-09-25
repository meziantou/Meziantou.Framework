# Meziantou.Framework.DependencyScanning

A .NET library for scanning source code directories and files to discover and manage project dependencies across multiple package ecosystems and configuration formats.

## Features

- **Multi-format Support**: Scan dependencies from various project files and configuration formats
- **Multiple Package Ecosystems**: NuGet, npm, PyPI, Docker, Swift packages, Helm Charts, and more
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
- **RenovateConfiguration** - Renovate configuration extends
- **SwiftPackage** - Swift Package Manager dependencies
- **RustCrate** - Rust crates from Cargo manifests and lockfiles
- **GoModule** - Go modules from `go.mod` and `go.sum`
- **JavaPackage** - Java packages from Maven and Gradle
- **PhpPackage** - PHP packages from Composer
- **MSBuildProjectReference** - MSBuild project references
- **DotNetAssemblyReference** - .NET assembly file references
- **RubyGem** - Ruby gem packages
- **AgentPlugin** - Claude Code and GitHub Copilot plugins referenced by a plugin marketplace
- **AgentPluginMarketplace** - Claude Code and GitHub Copilot plugin marketplaces

## Scanner Notes

- **Docker images** (Dockerfile `FROM` and `COPY --from`, GitHub Actions `container`, `services` and `docker://` references, Azure Pipelines containers): the tag is the part after the last `:` that follows the last `/`, so `localhost:5000/image:1.0` is reported as `localhost:5000/image` version `1.0`. An image pinned by digest (`image@sha256:...` or `image:tag@sha256:...`) is reported with the digest as its version. This version is not updatable, and the digest and tag are available in `Dependency.Metadata` under the `digest` and `tag` keys. In a Dockerfile, only references with a tag or a digest are reported, so build stage names are not mistaken for images.
- **Variables**: names or versions that contain a variable reference, such as `FROM node:${NODE_VERSION}`, are reported with a location that is not updatable.
- **YAML files**: a value is only updatable when its text in the file is exactly its value (plain scalars, and single-line quoted scalars without escape sequences). Block scalars (`|`, `>`), escaped or multi-line values are reported with a location that is not updatable. A value referenced through an alias (`*name`) is reported once.
- **GitHub Actions**: workflows in `.github/workflows` and action metadata files (`action.yml`, `action.yaml`) anywhere in the repository are scanned. Local actions and reusable workflows (`uses: ./...`) are not reported.
- **Agent plugins**: plugin marketplaces (`.claude-plugin/marketplace.json`, `.github/plugin/marketplace.json`) are scanned for plugins hosted in a git repository (`github`, `url` and `git-subdir` sources, and `owner/repo#ref` or git URL strings), reported as `AgentPlugin`, and for plugins published to npm, reported as `Npm`. The version of a git source is its `sha` when there is one, and its `ref` otherwise. Settings files (`.claude/settings.json`, `.github/copilot/settings.json` and their `settings.local.json` counterparts) are scanned for the git and URL marketplaces declared in `extraKnownMarketplaces`, reported as `AgentPluginMarketplace`. Plugins stored next to the marketplace and local marketplaces are not reported. The plugin or marketplace name, the `ref`, the `path` and the npm `registry` are available in `Dependency.Metadata` under the `plugin`, `marketplace`, `ref`, `path` and `registry` keys.
- **Azure Pipelines**: templates referenced from steps, jobs, stages and `extends`, deployment job lifecycle hooks, and `git`, `github`, `githubenterprise` and `bitbucket` repository resources are reported. A job `container` that names a container resource is not reported again.
- **Helm charts**: the dependency name is the chart `name`. The `repository` and `alias` values are available in `Dependency.Metadata` under the `repository` and `alias` keys.
- **npm**: protocol and path specifiers (`workspace:`, `file:`, `link:`, git URLs, `github:`, tarball URLs, `owner/repo` shorthands, ...) are reported with a version location that is not updatable. An alias (`"name": "npm:package@1.0.0"`) is reported as the aliased package, with the alias name in `Dependency.Metadata` under the `alias` key.
- **Python**: `*requirements*.txt` files and `.txt` files in a `requirements` directory are scanned. Only exact pins (`==`) are reported.
- **RubyGems**: `Gemfile`, `*.gemspec`, and `Gemfile.lock` files are scanned. Gem declarations and top-level Bundler lockfile specs are reported. Lockfile versions are not updatable.
- **Cargo**: `Cargo.toml` dependency sections and `Cargo.lock` package entries are scanned. Registry version requirements are updatable; git, path, workspace, and other non-version specifications are reported as non-updatable. `Cargo.lock` versions are not updatable as their checksums would no longer match.
- **Go modules**: `go.mod` `require` directives and `go.sum` checksum entries are scanned. `go.mod` versions are updatable; `go.sum` versions are not, as their hashes would no longer match.
- **Python projects**: `pyproject.toml`, `poetry.lock`, `Pipfile`, and `Pipfile.lock` are scanned. Exact versions from the dependency sections (`[project]` `dependencies`, `[project.optional-dependencies]`, `[dependency-groups]`, Poetry dependency tables, and Pipfile `[packages]`/`[dev-packages]`) and lockfile entries are reported. Lockfile versions are not updatable.
- **Composer**: `composer.json` `require` and `require-dev` entries, plus `composer.lock` package entries, are scanned. Platform requirements, such as `php` or `ext-json`, are ignored.
- **Java**: Maven `pom.xml` dependencies, plugins, and parent, Gradle build dependencies, and Gradle version catalog libraries (`libs.versions.toml`, including `version.ref`) are scanned. Maven property references and catalog versions shared by several libraries are not updatable.
- **Renovate**: `.json5` files are read as JSON with comments and trailing commas. JSON5-only syntax, such as unquoted property names or single-quoted strings, is not supported.

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
    IncludedDependencyTypes = [DependencyType.NuGet, DependencyType.Npm],
};

// Or exclude specific types
var options2 = new ScannerOptions
{
    ExcludedDependencyTypes = [DependencyType.DockerImage],
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
            FilePatterns = [Glob.Parse("**/*.custom", GlobDialect.Standard, GlobOptions.IgnoreCase)],
            DependencyType = DependencyType.DockerImage,
            Regex = new Regex(@"image:\s*(?<name>[a-z/]+)(:(?<version>[0-9.]+))?", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10))
        }
    ]
};

var dependencies = await DependencyScanner.ScanDirectoryAsync(
    "C:\\MyProject",
    options,
    cancellationToken);
```

`FilePatterns` are matched against the path of the file relative to the scanned root directory, for instance `build/*.yml` or `**/*.custom`.

The regex pattern must include named groups:
- `name` - The dependency name (required)
- `version` - The dependency version (optional)
