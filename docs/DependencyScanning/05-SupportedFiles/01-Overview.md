---
title: Overview
description: The dependency types the scanners report, the files they come from, and the rules shared by all the scanners.
---

The built-in scanners recognize the files from their name, extension or folder. This table lists the dependency types,
and the pages of this group give the details of each file format.

| [DependencyType](cref:Meziantou.Framework.DependencyScanning.DependencyType) | Reported from | Page |
| --- | --- | --- |
| [NuGet](cref:Meziantou.Framework.DependencyScanning.DependencyType.NuGet) | MSBuild projects, `packages.config`, `.nuspec`, `global.json` MSBuild SDKs, `dotnet-tools.json`, `project.json`, file-based apps, NuGet lock files, `project.assets.json`, Azure Pipelines package resources | [.NET](02-DotNet.md) |
| [DotNetSdk](cref:Meziantou.Framework.DependencyScanning.DependencyType.DotNetSdk) | `global.json` | [.NET](02-DotNet.md) |
| [DotNetTargetFramework](cref:Meziantou.Framework.DependencyScanning.DependencyType.DotNetTargetFramework) | MSBuild projects, file-based apps | [.NET](02-DotNet.md) |
| [MSBuildProjectReference](cref:Meziantou.Framework.DependencyScanning.DependencyType.MSBuildProjectReference) | MSBuild projects, file-based apps | [.NET](02-DotNet.md) |
| [DotNetAssemblyReference](cref:Meziantou.Framework.DependencyScanning.DependencyType.DotNetAssemblyReference) | File-based apps | [.NET](02-DotNet.md) |
| [Npm](cref:Meziantou.Framework.DependencyScanning.DependencyType.Npm) | `package.json`, npm lock files, Azure Pipelines package resources, agent plugin marketplaces | [JavaScript](03-JavaScript.md) |
| [DockerImage](cref:Meziantou.Framework.DependencyScanning.DependencyType.DockerImage) | Dockerfiles, Docker Compose files, Kubernetes manifests, GitHub Actions, Azure Pipelines | [Containers](04-Containers.md) |
| [HelmChart](cref:Meziantou.Framework.DependencyScanning.DependencyType.HelmChart) | `Chart.yaml`, `requirements.yaml` and their lock files | [Containers](04-Containers.md) |
| [GitHubActions](cref:Meziantou.Framework.DependencyScanning.DependencyType.GitHubActions) | Workflows, action metadata files, `actions.lock` | [CI pipelines](05-Pipelines.md) |
| [AzureDevOpsTask](cref:Meziantou.Framework.DependencyScanning.DependencyType.AzureDevOpsTask), [AzureDevOpsTemplate](cref:Meziantou.Framework.DependencyScanning.DependencyType.AzureDevOpsTemplate), [AzureDevOpsVMPool](cref:Meziantou.Framework.DependencyScanning.DependencyType.AzureDevOpsVMPool) | Azure Pipelines YAML files | [CI pipelines](05-Pipelines.md) |
| [GitReference](cref:Meziantou.Framework.DependencyScanning.DependencyType.GitReference) | `.gitmodules`, Azure Pipelines repository resources | [CI pipelines](05-Pipelines.md), [Repository files](07-RepositoryFiles.md) |
| [PyPi](cref:Meziantou.Framework.DependencyScanning.DependencyType.PyPi) | Requirements files, `pyproject.toml`, `Pipfile` and their lock files | [Other ecosystems](06-OtherEcosystems.md) |
| [RubyGem](cref:Meziantou.Framework.DependencyScanning.DependencyType.RubyGem) | `Gemfile`, `.gemspec`, `Gemfile.lock` | [Other ecosystems](06-OtherEcosystems.md) |
| [RustCrate](cref:Meziantou.Framework.DependencyScanning.DependencyType.RustCrate) | `Cargo.toml`, `Cargo.lock` | [Other ecosystems](06-OtherEcosystems.md) |
| [GoModule](cref:Meziantou.Framework.DependencyScanning.DependencyType.GoModule) | `go.mod`, `go.sum` | [Other ecosystems](06-OtherEcosystems.md) |
| [JavaPackage](cref:Meziantou.Framework.DependencyScanning.DependencyType.JavaPackage) | `pom.xml`, Gradle build files, version catalogs | [Other ecosystems](06-OtherEcosystems.md) |
| [PhpPackage](cref:Meziantou.Framework.DependencyScanning.DependencyType.PhpPackage) | `composer.json`, `composer.lock` | [Other ecosystems](06-OtherEcosystems.md) |
| [SwiftPackage](cref:Meziantou.Framework.DependencyScanning.DependencyType.SwiftPackage) | `Package.swift`, `Package.resolved` | [Other ecosystems](06-OtherEcosystems.md) |
| [RenovateConfiguration](cref:Meziantou.Framework.DependencyScanning.DependencyType.RenovateConfiguration) | Renovate configuration files | [Repository files](07-RepositoryFiles.md) |
| [AgentPlugin](cref:Meziantou.Framework.DependencyScanning.DependencyType.AgentPlugin), [AgentPluginMarketplace](cref:Meziantou.Framework.DependencyScanning.DependencyType.AgentPluginMarketplace) | Plugin marketplaces and settings files | [Repository files](07-RepositoryFiles.md) |

## Rules shared by the scanners

- **Updatable or not.** A value is updatable when the scanner can rewrite exactly its text. A value is reported with a
  location that is not updatable when rewriting it would be wrong or unsafe: an entry of a lock file, whose hash or
  integrity data would no longer match; a value built from a variable that cannot be updated where it is defined, such
  as `image: node:${NODE_VERSION}`; or a value whose text differs from its value, such as an escaped string. Check [Location.IsUpdatable](cref:Meziantou.Framework.DependencyScanning.Location.IsUpdatable) before an update.
- **YAML files.** A value is updatable only when its text in the file is exactly its value: a plain scalar, or a
  single-line quoted scalar without escape sequences. Block scalars (`|`, `>`), escaped and multi-line values are not
  updatable. A value referenced through an alias (`*name`) is reported once.
- **Metadata.** Some scanners add values to [Dependency.Metadata](cref:Meziantou.Framework.DependencyScanning.Dependency.Metadata), such as the digest of an image or the alias of a
  package. The pages of this group list their keys.

## Properties and variables

A version written with a property or a variable, such as `$(XunitVersion)` in an MSBuild project, is resolved when the
same file defines it, and the location of the definition is reported as the version location:

<!-- ref:../../../tests/Meziantou.Framework.DependencyScanning.Tests/DependencyScannerTests.cs;region=DependencyScanning_ResolveProperty;format=md-fenced-code-block;dedent=true -->
```csharp
var root = Directory.CreateTempSubdirectory().FullName; // The folder to scan
var projectPath = Path.Combine(root, "Tests.csproj");
File.WriteAllText(projectPath, """
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <XunitVersion>2.9.2</XunitVersion>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="xunit.core" Version="$(XunitVersion)" />
        <PackageReference Include="xunit.assert" Version="$(XunitVersion)" />
      </ItemGroup>
    </Project>
    """);

var dependencies = await DependencyScanner.ScanDirectoryAsync(root, options: null, cancellationToken);
var xunitCore = dependencies.Single(dependency => dependency.Name == "xunit.core");

Assert.Equal("$(XunitVersion)", xunitCore.RawVersion); // As written
Assert.Equal("2.9.2", xunitCore.ResolvedVersion);       // null when the property is not found
Assert.Equal("2.9.2", xunitCore.Version);               // The resolved version when there is one
Assert.Equal("XunitVersion", xunitCore.Metadata["property"]);

// The resolved version location is the definition of the property, shared by every package that uses it.
// The raw version location is the Version attribute of the package.
var xunitAssert = dependencies.Single(dependency => dependency.Name == "xunit.assert");
Assert.Same(xunitCore.ResolvedVersionLocation, xunitAssert.ResolvedVersionLocation);
Assert.NotSame(xunitCore.RawVersionLocation, xunitAssert.RawVersionLocation);

// VersionLocation is the resolved version location: updating it updates the property
Assert.Same(xunitCore.ResolvedVersionLocation, xunitCore.VersionLocation);

await xunitCore.UpdateVersionAsync("2.9.3", cancellationToken);
Assert.Equal("""
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <XunitVersion>2.9.3</XunitVersion>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="xunit.core" Version="$(XunitVersion)" />
        <PackageReference Include="xunit.assert" Version="$(XunitVersion)" />
      </ItemGroup>
    </Project>
    """, File.ReadAllText(projectPath));
```
<!-- endref -->

- [RawVersion](cref:Meziantou.Framework.DependencyScanning.Dependency.RawVersion) is the version as written, and [ResolvedVersion](cref:Meziantou.Framework.DependencyScanning.Dependency.ResolvedVersion) the version once the properties are replaced
  with their values, or `null` when one of them is not defined in the file. [Version](cref:Meziantou.Framework.DependencyScanning.Dependency.Version) is the resolved version when
  there is one, and the raw version otherwise. Compare versions and check vulnerabilities with the resolved version;
  use the raw version to show where a version comes from.
- The name of the property is in [Dependency.Metadata](cref:Meziantou.Framework.DependencyScanning.Dependency.Metadata) under the `property` key.
- [ResolvedVersionLocation](cref:Meziantou.Framework.DependencyScanning.Dependency.ResolvedVersionLocation) is the definition of the property, and [VersionLocation](cref:Meziantou.Framework.DependencyScanning.Dependency.VersionLocation) is the same location.
  **One update changes every dependency that uses the property**: the dependencies that use the same definition share
  the same location instance, so after an update through one of them, the next update through another one expects the
  new value.
- [RawVersionLocation](cref:Meziantou.Framework.DependencyScanning.Dependency.RawVersionLocation) is where the reference is written, such as the `Version` attribute. Updating it replaces the
  reference, for instance with a literal version, so that only this dependency stops using the property.
- A version made of several parts, such as `$(Major).1.0`, is resolved, but it is not written in one place: it has no
  [ResolvedVersionLocation](cref:Meziantou.Framework.DependencyScanning.Dependency.ResolvedVersionLocation), and its [VersionLocation](cref:Meziantou.Framework.DependencyScanning.Dependency.VersionLocation) is not updatable. Neither is the one of a version that cannot be
  resolved.
- A property is only resolved when the file defines it once, with a literal value. A property defined several times, in
  a condition, from other properties, or in another file (`Directory.Build.props`, `gradle.properties`, a parent pom,
  environment variables), is not resolved.

The scanners that resolve properties and variables:

| Files | Resolved |
| --- | --- |
| MSBuild projects, `.props` and `.targets` files | `$(Name)` defined in a `PropertyGroup` of the project. See [MSBuild projects](02-DotNet.md#msbuild-projects) |
| File-based apps | `$(Name)` defined by a `#:property Name=Value` directive. See [File-based apps](02-DotNet.md#file-based-apps) |
| Dockerfiles | `$NAME` and `${NAME}` defined by an `ARG` with a default value before the first `FROM`. See [Dockerfiles](04-Containers.md#dockerfiles) |
| Maven (`pom.xml`) | `${name}` defined in the `properties` of the pom. See [Java](06-OtherEcosystems.md#java) |
| Gradle (`build.gradle`, `build.gradle.kts`) | `$name`, `${name}` and identifiers defined in the script, such as `def name = '1.0'`, `ext.name = '1.0'` or `val name = "1.0"`. See [Java](06-OtherEcosystems.md#java) |
| Version catalogs (`libs.versions.toml`) | `version.ref` defined in `[versions]`. See [Java](06-OtherEcosystems.md#java) |

The other scanners report a value that contains a variable, such as `image: node:${NODE_VERSION}` in a compose file, as
written, with a `null` [ResolvedVersion](cref:Meziantou.Framework.DependencyScanning.Dependency.ResolvedVersion) and a location that is not updatable.

## Lock files

Each lock file format has its own scanner, separate from the scanner of the manifest it is generated from, so you can
leave them out of [ScannerOptions.Scanners](cref:Meziantou.Framework.DependencyScanning.ScannerOptions.Scanners). See [Choose the scanners](../04-ConfiguringTheScan.md#choose-the-scanners).
The versions found in lock files are not updatable.
