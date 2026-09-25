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

- **Docker images** (Dockerfile `FROM`, `COPY --from`, `RUN --mount=...,from=` and `# syntax=`, Docker Compose and Kubernetes manifests, GitHub Actions `container`, `services` and `docker://` references, Azure Pipelines containers and services): the tag is the part after the last `:` that follows the last `/`, so `localhost:5000/image:1.0` is reported as `localhost:5000/image` version `1.0`. An image pinned by digest (`image@sha256:...` or `image:tag@sha256:...`) is reported with the digest as its version. This version is not updatable, and the digest and tag are available in `Dependency.Metadata` under the `digest` and `tag` keys. In a Dockerfile, only references with a tag or a digest are reported (except `# syntax=`), so build stage names are not mistaken for images.
- **Dockerfile**: the bodies of here-documents (`RUN <<EOF`, `COPY <<-"EOF" /file`) are skipped. When the whole image or the whole tag of a `FROM` comes from an `ARG` declared with a literal default value before the first `FROM` (`ARG NODE_VERSION=20` then `FROM node:${NODE_VERSION}`), the default value is reported, and its location in the `ARG` instruction is updatable unless several `FROM` instructions use the same argument. Other variable references are reported with a location that is not updatable.
- **Docker Compose**: `compose.yaml`, `compose.yml`, `docker-compose.yaml`, `docker-compose.yml`, and their `compose.*.yaml` and `docker-compose.*.yml` variants are scanned for `services.<name>.image`. The image of a service that has a `build` section is the name given to the image built from source, so it is not reported.
- **Kubernetes**: YAML manifests of `Pod`, `PodTemplate`, `Deployment`, `StatefulSet`, `DaemonSet`, `ReplicaSet`, `ReplicationController`, `Job` and `CronJob` objects, including `List` objects and files with several documents, are scanned for the images of `containers`, `initContainers` and `ephemeralContainers`. Custom resources are not scanned. Files that contain `{{`, such as Helm chart templates, are templates rather than manifests, so they are not scanned.
- **Variables**: names or versions that contain a variable reference, such as `image: node:${NODE_VERSION}`, are reported with a location that is not updatable.
- **MSBuild**: every `*.*proj` file (`.csproj`, `.vcxproj`, `.sqlproj`, `.wixproj`, `.esproj`, ...), except the non-MSBuild `.pbxproj` and `.vdproj`, and `.props` and `.targets` files are scanned. Like MSBuild, item types, metadata names and property names are case-insensitive (`<packagereference Include="A" version="1.0.0" />`), while keywords such as `ItemGroup` and `Include` are not. An `Include` or `Update` list, such as `Include="A;B"`, is reported as one dependency per item, and names and versions are trimmed. When an item declares several packages, their shared version is not updatable. An SDK without a version (`Sdk="Name"`, `#:sdk Name` in a file-based app) is not reported, as its version is built in or comes from `global.json`.
- **File-based apps**: like the .NET SDK, the `#:` directives are read until the first token of the file. Comments and other preprocessor directives (`#nullable`, `#region`, `#pragma`, ...) do not stop the scan, but `#if` does.
- **NuGet**: `packages.config` and `packages.<project>.config` are scanned. A `packages.<project>.config` file is associated with the `<project>.*proj` project in the same directory, and `packages.config` with the other projects; the package paths of the associated projects (`HintPath`, `Import` and `Error`) are reported too, when the `<id>.<version>` folder is a whole path segment. The assembly version of a `Reference` (`Include="Name, Version=1.0.0.0"`) is not the package version, so it is not reported as a dependency, and it is not changed when the package version is updated. It is available in `Dependency.Metadata` under the `assemblyVersion` key, and its location, which accepts only valid assembly versions, under the `assemblyVersionLocation` key. `.nuspec` dependencies without a version are reported without a version. The resolved versions in NuGet lock files (`packages.lock.json` and `packages.<project>.lock.json`) and `project.assets.json` are reported once per package and version, and are not updatable.
- **YAML files**: a value is only updatable when its text in the file is exactly its value (plain scalars, and single-line quoted scalars without escape sequences). Block scalars (`|`, `>`), escaped or multi-line values are reported with a location that is not updatable. A value referenced through an alias (`*name`) is reported once.
- **GitHub Actions**: workflows in `.github/workflows` and action metadata files (`action.yml`, `action.yaml`) anywhere in the repository are scanned. Local actions and reusable workflows (`uses: ./...`) are not reported.
- **Agent plugins**: plugin marketplaces (`.claude-plugin/marketplace.json`, `.github/plugin/marketplace.json`) are scanned for plugins hosted in a git repository (`github`, `url` and `git-subdir` sources, and `owner/repo#ref` or git URL strings), reported as `AgentPlugin`, and for plugins published to npm, reported as `Npm`. The version of a git source is its `sha` when there is one, and its `ref` otherwise. Settings files (`.claude/settings.json`, `.github/copilot/settings.json` and their `settings.local.json` counterparts) are scanned for the git and URL marketplaces declared in `extraKnownMarketplaces`, reported as `AgentPluginMarketplace`. Plugins stored next to the marketplace and local marketplaces are not reported. The plugin or marketplace name, the `ref`, the `path` and the npm `registry` are available in `Dependency.Metadata` under the `plugin`, `marketplace`, `ref`, `path` and `registry` keys.
- **Azure Pipelines**: templates referenced from steps, jobs, stages, `extends` and `variables`, deployment job lifecycle hooks, `git`, `github`, `githubenterprise` and `bitbucket` repository resources, and `npm` and `NuGet` package resources are reported. The name of a package resource is reported as written (`<repository>/<package>` on GitHub Packages), and its `package` alias, `connection` and `type` are available in `Dependency.Metadata`. A job `container` or `services` entry that names a container resource is not reported again. As the resource may be declared in another file, for example the pipeline that includes a template, a job `container` or `services` value is only reported when it is certainly an image: it has a tag, a digest or a `/`. Template expressions such as `${{ parameters.image }}` are not reported. Pipeline resources (`resources.pipelines`) are not reported.
- **Helm charts**: `Chart.yaml` and `Chart.lock` are scanned, and `requirements.yaml` and `requirements.lock` for apiVersion v1 charts. The dependency name is the chart `name`. The `repository` and `alias` values are available in `Dependency.Metadata` under the `repository` and `alias` keys. The entries of the lock files are not updatable, as they record a digest of the dependencies.
- **Git submodules**: the name is the `url` as written in `.gitmodules`, and the version is the commit recorded in the git index. When the index cannot be read, for example in a source archive or a clone made with `--no-checkout`, the submodule is reported with a `null` version. The submodule `name`, `path` and `branch` are available in `Dependency.Metadata`, as well as the `url`, which is resolved against `remote.origin.url` when it is relative (`../other.git`), and is `null` when a relative url cannot be resolved.
- **npm**: `package.json` dependency sections, `packageManager`, npm `overrides` (including nested overrides and their `.` key), Yarn `resolutions`, and pnpm `pnpm.overrides` are scanned. In overrides and resolutions, the package is the last one of the key, without its version selector: `"**/a/@scope/b@^1.0.0": "2.0.0"` is reported as `@scope/b` version `2.0.0`. Protocol and path specifiers (`workspace:`, `file:`, `link:`, git URLs, `github:`, tarball URLs, `owner/repo` shorthands, ...) and override references (`$name`) are reported with a version location that is not updatable. An alias (`"name": "npm:package@1.0.0"`) is reported as the aliased package, with the alias name in `Dependency.Metadata` under the `alias` key. The `packageManager` is reported under the name it is written with, and its version is not updatable when a Corepack hash (`+sha512...`) follows it, as the hash would no longer match. `package-lock.json` and `npm-shrinkwrap.json` (lockfile versions 2 and 3) are scanned for the packages installed in `node_modules`, with versions that are not updatable as their integrity hashes would no longer match; the root project, workspace folders and links are not reported. Lockfile version 1 (npm 6 and earlier), `pnpm-lock.yaml`, `yarn.lock` and `bun.lock` are not scanned.
- **Python**: `*requirements*.txt` and `*constraints*.txt` files, their pip-tools `.in` counterparts (such as `requirements.in`), and `.txt` and `.in` files in a `requirements` directory are scanned. Only exact pins (`==`) are reported; arbitrary equality (`===`) is not.
- **RubyGems**: `Gemfile`, `*.gemspec`, and `Gemfile.lock` files are scanned. `gem` and `add_*dependency` calls that start a statement are reported, so a call in a comment, a string, a heredoc or an `=begin` block is not. Names and requirements can be written as `"..."`, `'...'` or `%q<...>` literals, optionally followed by `.freeze`, and requirements can be passed as an array. A gem with several requirements is reported with the requirements joined by `, ` and a version location that is not updatable, as is a requirement that contains an escape sequence or an interpolation. Top-level Bundler lockfile specs are reported, and their versions are not updatable. A gem with native extensions has one lockfile entry per platform, such as `nokogiri (1.15.0-x86_64-linux)`: each entry is reported with the version without the platform, and the platform in `Dependency.Metadata` under the `platform` key.
- **Cargo**: `Cargo.toml` dependency sections (`[dependencies]`, `[dev-dependencies]`, `[build-dependencies]`, `[workspace.dependencies]`, and the platform-specific `[target.'cfg(...)'.dependencies]` and `[target.<triple>.dependencies]` forms) and `Cargo.lock` package entries are scanned. A crate declared over several entries, such as a `[dependencies.serde]` table or dotted keys (`serde.version` and `serde.features`), is reported once. Registry version requirements are updatable; git, path, workspace, and other non-version specifications are reported as non-updatable. A renamed dependency (`json = { package = "serde_json" }`) is reported as the crate of its `package`, which is its name location, with the key in `Dependency.Metadata` under the `alias` key. A key is only updatable when it appears once, and a key or a value only when its text in the file is exactly its value (no escape sequences). `Cargo.lock` versions are not updatable as their checksums would no longer match, and packages without a `source`, which are the crates of the workspace and path dependencies, are not reported.
- **Go modules**: `go.mod` `require`, `replace`, `go` and `toolchain` directives and `go.sum` checksum entries are scanned. A `replace` directive is reported as its replacement module, with the replaced module path in `Dependency.Metadata` under the `replaces` key; a replacement with a local directory (`=> ../fork`) is not reported. The `go` and `toolchain` directives are reported as the `go` and `toolchain` modules, which is how the go command names them (`go get go@1.23.0 toolchain@go1.23.0`), with versions such as `1.23.0` and `go1.23.0`. `exclude`, `retract`, `tool` and `godebug` directives, and quoted module paths, are not reported. `go.mod` versions are updatable; `go.sum` versions are not, as their hashes would no longer match.
- **Python projects**: `pyproject.toml`, `poetry.lock`, `uv.lock`, `Pipfile`, and `Pipfile.lock` are scanned. Exact versions from the dependency sections (`[project]` `dependencies`, `[project.optional-dependencies]`, `[dependency-groups]`, `[build-system]` `requires`, `[tool.uv]` `dev-dependencies`/`constraint-dependencies`/`override-dependencies`, Poetry dependency tables, and Pipfile `[packages]`/`[dev-packages]`) and lockfile entries are reported. Requirement strings follow PEP 508 (`name[extras] == version ; marker`), and Poetry and Pipfile entries can be a string or an inline table with a `version` key. Only exact pins (`==`, or a bare version in Poetry and Pipfile) are reported; ranges and arbitrary equality (`===`) are not. Poetry's `python` entry is not a package and is ignored, and so are the local projects of the lockfiles (`uv.lock` `editable`, `virtual`, `path` or `directory` sources, and `poetry.lock` `directory` or `file` sources). A name or version is not updatable when its TOML string contains an escape sequence or spans several lines. Lockfile versions are not updatable.
- **Composer**: `composer.json` `require` and `require-dev` entries, plus `composer.lock` package entries, are scanned. Platform requirements, such as `php` or `ext-json`, are ignored.
- **Java**: Maven `pom.xml` files are read as XML: the dependencies, plugins, plugin dependencies, extensions and parent are reported, including in `dependencyManagement`, `pluginManagement` and profiles, but not the coordinates in a plugin `configuration` or in `exclusions`. A dependency without a version, such as one managed by an imported BOM, is reported with a `null` version. A version that is exactly a reference to a property defined once in the `properties` of the same pom and not redefined by a profile, such as `${junit.version}`, is reported with the value of the property, the property name in `Dependency.Metadata` under the `property` key, and the location of the property definition. That location is not updatable when the pom references the property more than once, as updating it would change the other references too; references from other poms, such as child modules, are not counted. Other property references are reported as written, with a location that is not updatable.
- **Gradle**: `build.gradle` and `build.gradle.kts` dependencies written as `"group:artifact:version"` strings or with named arguments (`group: 'g', name: 'a', version: '1.0'` in Groovy, `group = "g", name = "a", version = "1.0"` in Kotlin), and plugins declared with a version (`id 'x' version '1.0'`, `id("x") version "1.0"`, `kotlin("jvm") version "1.0"`) are reported. Comments are ignored, and a version that is not a string literal, such as `"$version"`, is not reported. Version catalogs (`libs.versions.toml`) report `[libraries]` and `[plugins]`, with a literal version, a `version.ref` or a `version = { ref = "..." }`. A catalog version shared by several libraries or plugins, or written with escape sequences, is not updatable. Gradle plugins are reported as `JavaPackage` dependencies named after their plugin marker artifact, `<id>:<id>.gradle.plugin`, which is the artifact Gradle resolves a plugin from; the plugin id is available in `Dependency.Metadata` under the `pluginId` key.
- **Swift**: `Package.swift`, `Package@swift-X.Y.swift` and `Package.resolved` files are scanned. In a manifest, the version of a dependency is its whole requirement as written, such as `from: "1.0.0"`, so that the kind of requirement can change on update. A name or a requirement that is not made of string literals only, such as an interpolated or concatenated URL or `from: version`, is reported as written with a location that is not updatable. Escape sequences in string literals are resolved, and a string that contains one is not updatable.
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

    // Filter files to scan (FileSystemEntry is in System.IO.Enumeration)
    ShouldScanFilePredicate = (ref FileSystemEntry entry) => !entry.FileName.StartsWith('.'),

    // Filter directories to recurse into
    ShouldRecursePredicate = (ref FileSystemEntry entry) => entry.FileName is not ("node_modules" or "bin"),

    // Called when a file cannot be read or parsed. The file is skipped and the scan continues.
    OnFileScanFailed = (filePath, exception) => Console.Error.WriteLine($"Cannot scan {filePath}: {exception.Message}"),
};

var dependencies = await DependencyScanner.ScanDirectoryAsync(
    @"C:\MyProject",
    options,
    cancellationToken);
```

Symbolic links and other reparse points to directories are never followed, so a link cannot make the scan loop or leave the root directory. A symbolic link to a file is scanned only when its target is inside the root directory, because updating its dependencies would write to the target.

### Error Handling

A file that cannot be scanned does not stop the scan: when a file cannot be opened or read, or a scanner throws while scanning it (for instance because the file is malformed), the remaining scanners are skipped for that file and the scan continues with the next file. The dependencies reported from the file before the failure are kept. Set `ScannerOptions.OnFileScanFailed` to be notified with the path of the file and the exception. The callback can be called concurrently, and an exception it throws stops the scan and is reported by the scan method, so it can be used to fail fast:

```csharp
var options = new ScannerOptions
{
    OnFileScanFailed = (filePath, exception) => throw new InvalidOperationException($"Cannot scan '{filePath}'", exception),
};
```

`ScanFileAsync`, `ScanFilesAsync` and `ScanDirectoryAsync` behave the same way: `ScanFileAsync` returns the dependencies found before the failure. The in-memory `ScanFileAsync` overloads, which take no options, skip the failure silently. Cancellation is not a failure: the scan methods throw an `OperationCanceledException` when their cancellation token is canceled. An exception thrown by the `onDependencyFound` callback also stops the scan.

### Custom File System

`ScannerOptions.FileSystem` reads and writes files through an `IFileSystem`. With a custom file system, `ScanDirectoryAsync` lists the files with `IFileSystem.GetFiles(path, "*", searchOption)`, which should throw a `DirectoryNotFoundException` when the directory does not exist. `ShouldScanFilePredicate` and `ShouldRecursePredicate` take a `FileSystemEntry`, which only an enumeration of the physical disk can provide, so they are not supported with a custom file system: the scan throws a `NotSupportedException`. Filter the files in `GetFiles` instead.

`OpenRead` can return a stream that cannot seek; it is read into memory. `OpenReadWrite` must return a seekable stream that supports `SetLength`, as an update rewrites the file in place.

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

Files are scanned in parallel, so the callback can be called concurrently from several threads and must be thread-safe. Use a concurrent collection, a lock, or `DegreeOfParallelism = 1`.

```csharp
var dependencies = new ConcurrentQueue<Dependency>();
await DependencyScanner.ScanDirectoryAsync(
    "C:\\MyProject",
    options: null,
    onDependencyFound: dependency =>
    {
        // Called concurrently
        dependencies.Enqueue(dependency);
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

The root directory and the file paths can be relative or contain `.` and `..` segments: they are made absolute before the scanners that depend on the location of the file, such as the GitHub Actions scanner, check them.

Dependencies scanned from in-memory content (`ScanFileAsync` with a `byte[]`) have locations that are not updatable, as there is no file to write.

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

An update never writes blindly: it first checks that the file still holds the expected value at the location, and throws a `DependencyScannerException` otherwise, for instance when the file was modified since the scan. The expected value is the one passed to `Location.UpdateAsync(oldValue, newValue)`, or, when none is given, the value the location holds: the value found by the scan, then the value written by the last update. So a dependency can be updated several times, and its name and its version can be updated in any order, even when they share a line such as `FROM node:18`. `Dependency.Name` and `Dependency.Version` keep the values found by the scan.

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
