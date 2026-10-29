---
title: Overview
description: The dependency types the scanners report, the files they come from, and the rules shared by all the scanners.
---

The built-in scanners recognize the files from their name, extension or folder. This table lists the dependency types,
and the pages of this group give the details of each file format.

| `DependencyType` | Reported from | Page |
| --- | --- | --- |
| `NuGet` | MSBuild projects, `packages.config`, `.nuspec`, `global.json` MSBuild SDKs, `dotnet-tools.json`, `project.json`, file-based apps, NuGet lock files, `project.assets.json`, Azure Pipelines package resources | [.NET](02-DotNet.md) |
| `DotNetSdk` | `global.json` | [.NET](02-DotNet.md) |
| `DotNetTargetFramework` | MSBuild projects, file-based apps | [.NET](02-DotNet.md) |
| `MSBuildProjectReference` | MSBuild projects, file-based apps | [.NET](02-DotNet.md) |
| `DotNetAssemblyReference` | File-based apps | [.NET](02-DotNet.md) |
| `Npm` | `package.json`, npm lock files, Azure Pipelines package resources, agent plugin marketplaces | [JavaScript](03-JavaScript.md) |
| `DockerImage` | Dockerfiles, Docker Compose files, Kubernetes manifests, GitHub Actions, Azure Pipelines | [Containers](04-Containers.md) |
| `HelmChart` | `Chart.yaml`, `requirements.yaml` and their lock files | [Containers](04-Containers.md) |
| `GitHubActions` | Workflows, action metadata files, `actions.lock` | [CI pipelines](05-Pipelines.md) |
| `AzureDevOpsTask`, `AzureDevOpsTemplate`, `AzureDevOpsVMPool` | Azure Pipelines YAML files | [CI pipelines](05-Pipelines.md) |
| `GitReference` | `.gitmodules`, Azure Pipelines repository resources | [CI pipelines](05-Pipelines.md), [Repository files](07-RepositoryFiles.md) |
| `PyPi` | Requirements files, `pyproject.toml`, `Pipfile` and their lock files | [Other ecosystems](06-OtherEcosystems.md) |
| `RubyGem` | `Gemfile`, `.gemspec`, `Gemfile.lock` | [Other ecosystems](06-OtherEcosystems.md) |
| `RustCrate` | `Cargo.toml`, `Cargo.lock` | [Other ecosystems](06-OtherEcosystems.md) |
| `GoModule` | `go.mod`, `go.sum` | [Other ecosystems](06-OtherEcosystems.md) |
| `JavaPackage` | `pom.xml`, Gradle build files, version catalogs | [Other ecosystems](06-OtherEcosystems.md) |
| `PhpPackage` | `composer.json`, `composer.lock` | [Other ecosystems](06-OtherEcosystems.md) |
| `SwiftPackage` | `Package.swift`, `Package.resolved` | [Other ecosystems](06-OtherEcosystems.md) |
| `RenovateConfiguration` | Renovate configuration files | [Repository files](07-RepositoryFiles.md) |
| `AgentPlugin`, `AgentPluginMarketplace` | Plugin marketplaces and settings files | [Repository files](07-RepositoryFiles.md) |

## Rules shared by the scanners

- **Updatable or not.** A value is updatable when the scanner can rewrite exactly its text. A value is reported with a
  location that is not updatable when rewriting it would be wrong or unsafe: an entry of a lock file, whose hash or
  integrity data would no longer match; a value built from a variable, such as `image: node:${NODE_VERSION}`; or a value
  whose text differs from its value, such as an escaped string. Check `Location.IsUpdatable` before an update.
- **YAML files.** A value is updatable only when its text in the file is exactly its value: a plain scalar, or a
  single-line quoted scalar without escape sequences. Block scalars (`|`, `>`), escaped and multi-line values are not
  updatable. A value referenced through an alias (`*name`) is reported once.
- **Metadata.** Some scanners add values to `Dependency.Metadata`, such as the digest of an image or the alias of a
  package. The pages of this group list their keys.

## Lock files

Each lock file format has its own scanner, separate from the scanner of the manifest it is generated from, so you can
leave them out of `ScannerOptions.Scanners`. See [Choose the scanners](../04-ConfiguringTheScan.md#choose-the-scanners).
The versions found in lock files are not updatable.
