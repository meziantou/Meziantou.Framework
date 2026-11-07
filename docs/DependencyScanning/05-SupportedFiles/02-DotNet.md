---
title: .NET
description: The NuGet packages, SDKs, target frameworks and references found in MSBuild projects, packages.config, global.json, tool manifests and file-based apps.
---

## MSBuild projects

Every `*.*proj` file (`.csproj`, `.vcxproj`, `.sqlproj`, `.wixproj`, `.esproj`, ...) except the non-MSBuild `.pbxproj`
and `.vdproj`, and every `.props` and `.targets` file, is scanned by [MsBuildReferencesDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.MsBuildReferencesDependencyScanner):

| Written as | Reported as |
| --- | --- |
| `PackageReference`, `PackageDownload`, `GlobalPackageReference`, with `Version` or `VersionOverride` | [NuGet](cref:Meziantou.Framework.DependencyScanning.DependencyType.NuGet) |
| `PackageVersion` (central package management) | [NuGet](cref:Meziantou.Framework.DependencyScanning.DependencyType.NuGet) |
| `Sdk="Name/Version"`, `<Sdk Name="..." Version="..." />`, `<Import Sdk="..." Version="..." />` | [NuGet](cref:Meziantou.Framework.DependencyScanning.DependencyType.NuGet) |
| `TargetFramework`, `TargetFrameworks`, `TargetFrameworkVersion` | [DotNetTargetFramework](cref:Meziantou.Framework.DependencyScanning.DependencyType.DotNetTargetFramework), with no name |
| `ProjectReference` | [MSBuildProjectReference](cref:Meziantou.Framework.DependencyScanning.DependencyType.MSBuildProjectReference), with no version |

- Like MSBuild, item types, metadata names and property names are case-insensitive
  (`<packagereference Include="A" version="1.0.0" />`), while keywords such as `ItemGroup` and `Include` are not.
- An `Include` or `Update` list, such as `Include="A;B"`, is reported as one dependency per item, and names and versions
  are trimmed. When an item declares several packages, their shared version is not updatable.
- An SDK without a version (`Sdk="Microsoft.NET.Sdk"`) is not reported: its version is built in or comes from
  `global.json`.

## packages.config

`packages.config` and `packages.<project>.config` are scanned by [PackagesConfigDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.PackagesConfigDependencyScanner):

- A `packages.<project>.config` file is associated with the `<project>.*proj` project of the same folder, and
  `packages.config` with the other projects of the folder. The package paths of the associated projects (`HintPath`,
  `Import` and `Error`) are reported too, as more [NuGet](cref:Meziantou.Framework.DependencyScanning.DependencyType.NuGet) dependencies located in the project file, when the
  `<id>.<version>` folder is a whole path segment. Set [SearchForReferencesInAssociatedCsprojFiles](cref:Meziantou.Framework.DependencyScanning.Scanners.PackagesConfigDependencyScanner.SearchForReferencesInAssociatedCsprojFiles) to `false` on the
  scanner to skip the projects.
- The assembly version of a `Reference` (`Include="Name, Version=1.0.0.0"`) is not the package version: it is not
  reported as a dependency, and it is not changed when the package version is updated. It is in [Dependency.Metadata](cref:Meziantou.Framework.DependencyScanning.Dependency.Metadata)
  under the `assemblyVersion` key, and its location, which accepts only valid assembly versions, under the
  `assemblyVersionLocation` key.

## Other .NET files

| File | Scanner | Reported |
| --- | --- | --- |
| `.nuspec` | [NuSpecDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.NuSpecDependencyScanner) | The `dependency` elements, as [NuGet](cref:Meziantou.Framework.DependencyScanning.DependencyType.NuGet). A dependency without a version is reported without a version |
| `global.json` | [DotNetGlobalJsonDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.DotNetGlobalJsonDependencyScanner) | `sdk.version` as [DotNetSdk](cref:Meziantou.Framework.DependencyScanning.DependencyType.DotNetSdk), with no name, and the `msbuild-sdks` as [NuGet](cref:Meziantou.Framework.DependencyScanning.DependencyType.NuGet) |
| `dotnet-tools.json` | [DotNetToolManifestDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.DotNetToolManifestDependencyScanner) | The local tools, as [NuGet](cref:Meziantou.Framework.DependencyScanning.DependencyType.NuGet) |
| `project.json` | [ProjectJsonDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.ProjectJsonDependencyScanner) | The dependencies and tools, as [NuGet](cref:Meziantou.Framework.DependencyScanning.DependencyType.NuGet) |
| `packages.lock.json`, `packages.<project>.lock.json` | [NuGetLockFileDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.NuGetLockFileDependencyScanner) | The resolved packages, once per package and version, not updatable |
| `project.assets.json` | [ProjectAssetsDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.ProjectAssetsDependencyScanner) | The resolved packages, once per package and version, not updatable |

## File-based apps

`.cs` files are scanned by [DotNetFileBasedAppDependencyScanner](cref:Meziantou.Framework.DependencyScanning.Scanners.DotNetFileBasedAppDependencyScanner) for the `#:` directives of file-based apps:

| Directive | Reported as |
| --- | --- |
| `#:package Name@Version`, or with `Version` or `VersionOverride` metadata | [NuGet](cref:Meziantou.Framework.DependencyScanning.DependencyType.NuGet) |
| `#:sdk Name@Version` | [NuGet](cref:Meziantou.Framework.DependencyScanning.DependencyType.NuGet) |
| `#:property TargetFramework=Value` | [DotNetTargetFramework](cref:Meziantou.Framework.DependencyScanning.DependencyType.DotNetTargetFramework) |
| `#:project Path` | [MSBuildProjectReference](cref:Meziantou.Framework.DependencyScanning.DependencyType.MSBuildProjectReference) |
| `#:ref Path` | [DotNetAssemblyReference](cref:Meziantou.Framework.DependencyScanning.DependencyType.DotNetAssemblyReference) |

Like the .NET SDK, the directives are read until the first token of the file. Comments and other preprocessor
directives (`#nullable`, `#region`, `#pragma`, ...) do not stop the scan, but `#if` does. An SDK without a version
(`#:sdk Name`) is not reported.
