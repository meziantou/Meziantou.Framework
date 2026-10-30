---
title: .NET
description: The NuGet packages, SDKs, target frameworks and references found in MSBuild projects, packages.config, global.json, tool manifests and file-based apps.
---

## MSBuild projects

Every `*.*proj` file (`.csproj`, `.vcxproj`, `.sqlproj`, `.wixproj`, `.esproj`, ...) except the non-MSBuild `.pbxproj`
and `.vdproj`, and every `.props` and `.targets` file, is scanned by `MsBuildReferencesDependencyScanner`:

| Written as | Reported as |
| --- | --- |
| `PackageReference`, `PackageDownload`, `GlobalPackageReference`, with `Version` or `VersionOverride` | `NuGet` |
| `PackageVersion` (central package management) | `NuGet` |
| `Sdk="Name/Version"`, `<Sdk Name="..." Version="..." />`, `<Import Sdk="..." Version="..." />` | `NuGet` |
| `TargetFramework`, `TargetFrameworks`, `TargetFrameworkVersion` | `DotNetTargetFramework`, with no name |
| `ProjectReference` | `MSBuildProjectReference`, with no version |

- Like MSBuild, item types, metadata names and property names are case-insensitive
  (`<packagereference Include="A" version="1.0.0" />`), while keywords such as `ItemGroup` and `Include` are not.
- An `Include` or `Update` list, such as `Include="A;B"`, is reported as one dependency per item, and names and versions
  are trimmed. When an item declares several packages, their shared version is not updatable.
- An SDK without a version (`Sdk="Microsoft.NET.Sdk"`) is not reported: its version is built in or comes from
  `global.json`.

## packages.config

`packages.config` and `packages.<project>.config` are scanned by `PackagesConfigDependencyScanner`:

- A `packages.<project>.config` file is associated with the `<project>.*proj` project of the same folder, and
  `packages.config` with the other projects of the folder. The package paths of the associated projects (`HintPath`,
  `Import` and `Error`) are reported too, as more `NuGet` dependencies located in the project file, when the
  `<id>.<version>` folder is a whole path segment. Set `SearchForReferencesInAssociatedCsprojFiles` to `false` on the
  scanner to skip the projects.
- The assembly version of a `Reference` (`Include="Name, Version=1.0.0.0"`) is not the package version: it is not
  reported as a dependency, and it is not changed when the package version is updated. It is in `Dependency.Metadata`
  under the `assemblyVersion` key, and its location, which accepts only valid assembly versions, under the
  `assemblyVersionLocation` key.

## Other .NET files

| File | Scanner | Reported |
| --- | --- | --- |
| `.nuspec` | `NuSpecDependencyScanner` | The `dependency` elements, as `NuGet`. A dependency without a version is reported without a version |
| `global.json` | `DotNetGlobalJsonDependencyScanner` | `sdk.version` as `DotNetSdk`, with no name, and the `msbuild-sdks` as `NuGet` |
| `dotnet-tools.json` | `DotNetToolManifestDependencyScanner` | The local tools, as `NuGet` |
| `project.json` | `ProjectJsonDependencyScanner` | The dependencies and tools, as `NuGet` |
| `packages.lock.json`, `packages.<project>.lock.json` | `NuGetLockFileDependencyScanner` | The resolved packages, once per package and version, not updatable |
| `project.assets.json` | `ProjectAssetsDependencyScanner` | The resolved packages, once per package and version, not updatable |

## File-based apps

`.cs` files are scanned by `DotNetFileBasedAppDependencyScanner` for the `#:` directives of file-based apps:

| Directive | Reported as |
| --- | --- |
| `#:package Name@Version`, or with `Version` or `VersionOverride` metadata | `NuGet` |
| `#:sdk Name@Version` | `NuGet` |
| `#:property TargetFramework=Value` | `DotNetTargetFramework` |
| `#:project Path` | `MSBuildProjectReference` |
| `#:ref Path` | `DotNetAssemblyReference` |

Like the .NET SDK, the directives are read until the first token of the file. Comments and other preprocessor
directives (`#nullable`, `#region`, `#pragma`, ...) do not stop the scan, but `#if` does. An SDK without a version
(`#:sdk Name`) is not reported.
